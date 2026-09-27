#!/usr/bin/env node
/**
 * ClineEngine — 执行面主体（由 bootstrap 入口 `engine.ts` 在 installSink 之后动态加载）。
 *
 * 职责：独占 @cline/sdk（loadSdk + ClineCore.create + ask/plan/resume/history/warmup）。
 * 与 daemon 控制面分离：本进程的事件循环即便被 ClineCore 初始化阻塞，也只阻塞执行面自身，
 * 不影响 daemon 主循环（心跳 / ping / 阶段追踪 / 预算杀进程）。
 * 这正是《Cline 初始化挂起根因与架构修复方案》治本方案 A（执行面隔离）。
 *
 * **本文件（及其静态依赖闭包）含 `node:fs` 消费者，必须只在 patch 之后被动态加载**（见 `engine.ts`）。
 *
 * 协议（stdin/stdout JSON-Line，继承 daemon 协议）：
 *   请求  { "id","cmd","payload" }
 *   阶段  { "id","type":"trace","stage":"...","detail":... }   // 无条件上报（观测默认开）
 *   心跳  { "id","type":"heartbeat","elapsedMs":... }
 *   终帧  { "id","ok":true,"result":... } / { "id","ok":false,"error":"..." }
 */

import * as readline from "readline";
import type { AgentSinkHost } from "fs-sink";
import { SdkTransport } from "./transport/sdk-transport.js";
import type { ClineMode, ClineRunOptions } from "./types.js";
import { clineLifecycleToJson } from "./csg/state-machine.js";

/** 主体启动参数（sink = 入口 preload 已安装并 patch 的门面）。 */
export interface EngineMainOptions {
  /** FsSink 门面：注入 SdkTransport（拦截已由 preload 完成，本层只消费）。 */
  sink?: AgentSinkHost;
}

const HEARTBEAT_INTERVAL_MS = 10000;

type Cmd = "ask" | "plan" | "resume" | "history" | "warmup" | "ping";

interface EngineRequest {
  id?: string;
  cmd?: string;
  payload?: Record<string, unknown>;
}

function send(obj: unknown): void {
  process.stdout.write(`${JSON.stringify(obj)}\n`);
}

function respondOk(id: string, result: unknown): void {
  send({ id, ok: true, result });
}

function respondError(id: string, error: string): void {
  send({ id, ok: false, error });
}

/** 阶段帧（不带 id；由控制面 daemon 按当前请求关联并盖章）。 */
function stage(stage: string, detail?: unknown): void {
  const frame: Record<string, unknown> = { type: "trace", stage };
  if (detail !== undefined) frame.detail = detail;
  send(frame);
}

/** CSG 状态帧：经 csg-protocol 上报 ClineCore 生命周期状态（CanonicalForm 跨语言契约）。 */
function sendCsg(state: "spawned" | "loading" | "ready" | "api"): void {
  send({ type: "csg", stage: state, form: clineLifecycleToJson(state) });
}

/** 主体入口：装配 transport（sink 由 preload 注入）并进入 JSON-Line 请求循环。 */
export async function startEngine(opts: EngineMainOptions = {}): Promise<void> {
  // sink 已由入口 preload patch 完成（H1：patch 先于 import("@cline/sdk")）；本层只消费其门面。
  const transport = new SdkTransport({ sink: opts.sink });
  transport.setStageReporter((s, detail) => stage(s, detail));
  // K2 识别产物帧（type:"node"）：不带 id，由控制面 daemon 按当前请求关联并盖章（与 trace 帧同范式）。
  // 帧形态 { type:"node", node:{ kind, ordinal, node, origin, source, payload } }。
  transport.setNodeReporter((node) => send({ type: "node", node }));

  let heartbeat: ReturnType<typeof setInterval> | undefined;
  const stopHeartbeat = (): void => {
    if (heartbeat) {
      clearInterval(heartbeat);
      heartbeat = undefined;
    }
  };

  const handle = async (req: EngineRequest): Promise<void> => {
    const id = req.id ?? "";
    const cmd = (req.cmd ?? "") as Cmd;
    const payload = (req.payload ?? {}) as Record<string, unknown>;

    if (cmd === "ping") {
      send({ id, type: "pong" });
      return;
    }

    const startedAt = Date.now();
    heartbeat = setInterval(() => {
      // 阶段保活（重构：代替 transport 心跳）：以 stage 帧声明"操作仍在推进"。
      // C# AEF 以阶段(stage)判定进度（ClineDaemonIpc.onProgress 绑 Trace）——被 ClineCore 同步阻塞时
      // 本事件循环不跑、不发 stage → C# 空闲看门狗判挂起并终止，杜绝"控制面心跳续命"的静默挂死。
      stage("engine-running", { elapsedMs: Date.now() - startedAt });
    }, HEARTBEAT_INTERVAL_MS);

    try {
      switch (cmd) {
        case "ask":
        case "plan": {
          stage("engine-run-start", { cmd });
          sendCsg("api");
          const result = await transport.run({
            ...(payload as Partial<ClineRunOptions>),
            prompt: payload.prompt as string,
            mode: (cmd === "plan" ? "plan" : (payload.mode as ClineMode) ?? "act") as ClineMode,
          });
          respondOk(id, result);
          break;
        }

        case "resume": {
          stage("engine-resume-start");
          const result = await transport.resume(
            payload.sessionId as string,
            (payload.prompt as string) ?? "继续当前任务",
            payload as Partial<ClineRunOptions>
          );
          respondOk(id, result);
          break;
        }

        case "history": {
          const limit = Number(payload.limit) || 10;
          stage("engine-history-start", { limit });
          const records = await transport.history(limit, true);
          respondOk(id, records);
          break;
        }

        case "warmup": {
          const timeoutMs =
            Number(payload.warmupTimeoutSec) > 0 ? Number(payload.warmupTimeoutSec) * 1000 : 90_000;
          stage("engine-warmup-start", { timeoutMs });
          await transport.warmup(timeoutMs, true);
          sendCsg("ready");
          respondOk(id, { warm: true });
          break;
        }

        default:
          respondError(id, `不支持的 engine 命令: ${cmd}`);
          break;
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      respondError(id, message);
    } finally {
      stopHeartbeat();
      // 观测唯一通道（④）：每轮结束把 FsSink 观测帧作为阶段上报，供 C# 诊断日志消费。
      // 判据：hits>0 ∧ misses==0 ∧ bindings==[]。
      const frame = opts.sink?.observe();
      if (frame) stage("fs-sink-observe", frame);
    }
  };

  // 串行：一次一个请求，保证同一 ClineCore 互斥（daemon 侧单引擎）。
  let pending: Promise<void> = Promise.resolve();
  const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });

  rl.on("line", (line) => {
    const trimmed = line.trim();
    if (!trimmed) return;
    let req: EngineRequest;
    try {
      req = JSON.parse(trimmed) as EngineRequest;
    } catch {
      respondError("", "无效 JSON-Line");
      return;
    }
    pending = pending.then(() => handle(req));
  });

  const shutdown = async (): Promise<void> => {
    stopHeartbeat();
    try {
      await transport.dispose();
    } catch {
      /* 忽略 */
    }
    process.exit(0);
  };

  rl.on("close", () => {
    void shutdown();
  });
  process.on("SIGTERM", () => rl.close());
  process.on("disconnect", () => rl.close());
}