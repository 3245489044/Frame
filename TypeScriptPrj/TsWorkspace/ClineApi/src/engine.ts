#!/usr/bin/env node
/**
 * ClineEngine — Cline daemon 的执行面（子进程）。
 *
 * 职责：独占 @cline/sdk（loadSdk + ClineCore.create + ask/plan/resume/history/warmup）。
 * 与 daemon 控制面分离：本进程的事件循环即便被 ClineCore 初始化阻塞，也只阻塞执行面自身，
 * 不影响 daemon 主循环（心跳 / ping / 阶段追踪 / 预算杀进程）。
 * 这正是《Cline 初始化挂起根因与架构修复方案》治本方案 A（执行面隔离）。
 *
 * 协议（stdin/stdout JSON-Line，继承 daemon 协议）：
 *   请求  { "id","cmd","payload" }
 *   阶段  { "id","type":"trace","stage":"...","detail":... }   // 无条件上报（观测默认开）
 *   心跳  { "id","type":"heartbeat","elapsedMs":... }
 *   终帧  { "id","ok":true,"result":... } / { "id","ok":false,"error":"..." }
 */

import * as readline from "readline";
import { SdkTransport } from "./transport/sdk-transport.js";
import type { ClineMode, ClineRunOptions } from "./types.js";
import { clineLifecycleToJson } from "./csg/state-machine.js";

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

function main(): void {
  // fsSink:true → 构造即 preload patch node:fs/fs/promises（须先于 loadSdk/import("@cline/sdk")，H1），
  // 拦截 Cline 会话落盘到内存卷 + 镜像 DB，磁盘零残留。仅执行面子进程启用（H3 隔离）。
  // CLINE_FS_SINK_PERSIST_BRIDGE=1 → 注入 BridgeToCSharpDbBackend（写侧实时推 persist 帧到 C# ur_nodes），
  // 默认不注入（走 InMemoryDbBackend，archivedMessages 回传兜底，保持 Phase 1 行为可回退）。
  const persistBridge = process.env.CLINE_FS_SINK_PERSIST_BRIDGE === "1";
  const transport = new SdkTransport({ fsSink: { persistBridge } });
  transport.setStageReporter((s, detail) => stage(s, detail));

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

main();