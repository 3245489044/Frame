#!/usr/bin/env node
/**
 * ClineDaemon — Cline daemon 池的单 worker（控制面）。
 *
 * 架构：控制面 / 执行面解耦（治本方案 A，见《Cline 初始化挂起根因与架构修复方案》）。
 *   - 本进程 = 控制面：保持事件循环常响应。处理 C# 的 JSON-Line，ping 本地应答；
 *     其余命令转发给执行面子进程（engine），并中继其帧（阶段 trace / 心跳 / 终帧）；
 *     同时以自己常活的心跳 + 每请求预算看门狗（到点杀 engine 进程树并回带最后阶段）。
 *   - engine 子进程 = 执行面：独占 @cline/sdk（loadSdk + ClineCore.create + run/...）。
 *     即便 ClineCore 初始化阻塞其事件循环，也只阻塞执行面，不影响控制面与 C# 看门狗。
 *
 * 协议（stdin/stdout JSON-Line）：
 *   请求  { "id","cmd","payload" }
 *   响应  { "id","ok":true,"result":... }  / { "id","ok":false,"error":"..." }
 *   心跳  { "id","type":"heartbeat","elapsedMs":... }
 *   阶段  { "id","type":"trace","stage":"...","detail":... }
 *
 * 退出清理：stdin EOF / SIGTERM / parent disconnect -> 杀 engine 进程树 -> exit(0)（杜绝残留 node/后台进程）。
 */

import * as readline from "readline";
import { spawn, type ChildProcessWithoutNullStreams } from "child_process";
import * as path from "path";
import { fileURLToPath } from "url";

const HEARTBEAT_INTERVAL_MS = 10000;
/** 无显式预算时 daemon 侧每请求兜底（ms）：20 分钟（对齐 C# ClineDaemonOptions 默认）。 */
const DEFAULT_BUDGET_MS = 20 * 60 * 1000;

type Cmd = "ask" | "plan" | "resume" | "history" | "warmup" | "ping";

interface DaemonRequest {
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

/** 定位执行面 engine.js（与 daemon.js 同目录：build/engine.js）。 */
function resolveEngineScript(): string {
  return path.resolve(path.dirname(fileURLToPath(import.meta.url)), "engine.js");
}

/** 从请求 payload 推导每请求预算（warmup 用 warmupTimeoutSec；其余用 timeoutSec；缺省兜底）。 */
function requestBudgetMs(req: DaemonRequest): number {
  const p = req.payload ?? {};
  const sec =
    req.cmd === "warmup"
      ? Number(p.warmupTimeoutSec)
      : Number(p.timeoutSec);
  return Number.isFinite(sec) && sec > 0 ? sec * 1000 : DEFAULT_BUDGET_MS;
}

/**
 * 执行面宿主：管理单个 engine 子进程生命周期、帧中继、终帧关联、崩溃自动重建。
 */
class EngineHost {
  private child: ChildProcessWithoutNullStreams | null = null;
  // S5 源-投影声明：lastStage 唯一权威源 = Node（本进程 / engine）。C# 侧仅作只读投影（经 trace/csg 帧累计）。
  private readonly pending = new Map<string, (frame: Record<string, unknown>) => void>();
  private lastStage = "";

  private ensureChild(): ChildProcessWithoutNullStreams {
    if (this.child && this.child.exitCode === null && !this.child.killed) {
      return this.child;
    }
    const script = resolveEngineScript();
    this.child = spawn(process.execPath, [script], { stdio: ["pipe", "pipe", "pipe"] });
    // 执行面 stderr（--trace 阶段标记）透传至 daemon stderr，供 C# 侧捕获。
    this.child.stderr.setEncoding("utf8");
    this.child.stderr.on("data", (chunk: string) => process.stderr.write(chunk));
    this.child.stdout.setEncoding("utf8");
    this.child.stdout.on("data", (chunk: string) => this.onData(chunk));
    this.child.on("error", () => {
      /* 启动失败由 exit 兜底 */
    });
    this.child.on("exit", () => {
      send({ id: "", type: "trace", stage: "engine-exited" });
      // 崩溃时终结所有仍挂起请求，回带最后阶段，供 C# 归因。
      for (const [id, resolve] of Array.from(this.pending.entries())) {
        this.pending.delete(id);
        resolve({
          id,
          ok: false,
          error: `执行面(engine) 意外退出，最后阶段=${this.lastStage || "(无)"}`,
        });
      }
      this.child = null;
    });
    send({ id: "", type: "trace", stage: "engine-spawned", detail: { pid: this.child.pid } });
    return this.child;
  }

  private onData(chunk: string): void {
    const lines = chunk.split(/\r?\n/);
    for (const line of lines) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      let frame: Record<string, unknown>;
      try {
        frame = JSON.parse(trimmed) as Record<string, unknown>;
      } catch {
        continue;
      }
      const id = (frame.id as string) ?? "";
      const type = (frame.type as string) ?? "";

      if (type === "trace") {
        this.lastStage = (frame.stage as string) ?? this.lastStage;
        // 阶段帧不带 id，控制面盖章为当前请求 id（trace 帧 id 恒空）。
        send({ id, type: "trace", stage: this.lastStage, detail: frame.detail ?? undefined });
        continue;
      }

      if (type === "heartbeat") {
        send({ id, type: "heartbeat", elapsedMs: frame.elapsedMs ?? 0 });
        continue;
      }

      if (type === "pong") {
        send({ id, type: "pong" });
        continue;
      }

      if (type === "csg") {
        // CSG 状态帧：中继到 C#（CanonicalForm 跨语言状态契约，观测默认开）。
        send({ id, type: "csg", stage: (frame.stage as string) ?? "", form: frame.form ?? undefined });
        continue;
      }

      if (type === "persist") {
        // FsSink 桥接持久化帧：engine 经 BridgeToCSharpDbBackend 推送 → 中继到 C# agent.db / ur_nodes。
        send({ id, type: "persist", detail: frame.detail ?? undefined });
        continue;
      }

      if (type === "node") {
        // K2 识别产物帧：中继到 C#（per-call 归属由池按「本次调用选中的 worker」绑定，不按 id 反解）。
        // 载荷复用 detail 通道（与 persist 帧同形态：C# 侧 DaemonResponse.Detail 承载结构体）。
        send({ id, type: "node", detail: frame.node ?? undefined });
        continue;
      }

      // 终帧（engine 带 ok/error）。
      if (frame.ok !== undefined || frame.error !== undefined) {
        const resolve = this.pending.get(id);
        if (resolve) {
          this.pending.delete(id);
          resolve(frame);
        } else {
          send({ id, ok: frame.ok, result: frame.result, error: frame.error });
        }
      }
    }
  }

  /** 终止 engine 进程树（配合 stdio inherit 仅传 stdin/stdout，stderr 透传至 daemon）。 */
  killEngine(): void {
    const c = this.child;
    if (c && c.exitCode === null && !c.killed) {
      c.kill(); // 正常 SIGTERM（engine 退出时清理自身子进程）
    }
  }

  /** 转发一个请求到 engine；等待终帧或预算看门狗到期。 */
  async dispatch(req: DaemonRequest, budgetMs: number): Promise<void> {
    const id = req.id ?? "";
    this.ensureChild();
    const engine = this.child!;
    this.lastStage = "";

    const terminal = new Promise<Record<string, unknown>>((resolve) => this.pending.set(id, resolve));
    const startedAt = Date.now();
    // S5 单一 deadline 契约：本 pending 与 C# 侧 ClineDaemonIpc._pending 共用同一请求预算
    // （内层 budgetMs = 框架 deadline - margin）。任一预算到期均需原子化取消另一侧：node 看门狗到期
    // 杀 engine 并回错误帧 → C# 侧 TCS 随之 settle；C# 超时/取消在 finally 移除 pending 并 TryRemove。任一层丢帧由错误帧兜底归因。

    // 控制面常活心跳：证明控制面没挂（即使执行面被 SDK 阻塞）。
    let lastHeartbeatAt = 0;
    const heartbeatTimer = setInterval(() => {
      const now = Date.now();
      if (now - lastHeartbeatAt >= HEARTBEAT_INTERVAL_MS) {
        send({ id, type: "heartbeat", elapsedMs: now - startedAt });
        lastHeartbeatAt = now;
      }
    }, 1000);

    // 每请求预算看门狗（控制面常活循环可到期触发）：
    // 执行面预算内未出终帧 -> 杀 engine 进程树并以错误帧终帧回带最后阶段。
    const watchdog = new Promise<Record<string, unknown>>((resolve) => {
      setTimeout(() => {
        clearInterval(heartbeatTimer);
        const stage = this.lastStage || "(无)";
        this.pending.delete(id);
        this.killEngine();
        resolve({
          id,
          ok: false,
          error: `执行面(engine) 超时（${Math.round(budgetMs / 1000)}s），最后阶段=${stage}；已终止引擎子进程`,
        });
      }, budgetMs);
    });

    try {
      engine.stdin.write(`${JSON.stringify({ id, cmd: req.cmd, payload: req.payload ?? {} })}\n`, "utf8");
    } catch (err) {
      clearInterval(heartbeatTimer);
      this.pending.delete(id);
      const message = err instanceof Error ? err.message : String(err);
      respondError(id, `转发到执行面失败: ${message}`);
      return;
    }

    const frame = await Promise.race([terminal, watchdog]);
    clearInterval(heartbeatTimer);
    send({ id, ok: frame.ok, result: frame.result, error: frame.error });
  }
}

function main(): void {
  const host = new EngineHost();
  const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });

  // 串行：一次一个请求（单引擎，控制面互斥）。
  let pending: Promise<void> = Promise.resolve();

  rl.on("line", (line) => {
    const trimmed = line.trim();
    if (!trimmed) return;
    let req: DaemonRequest;
    try {
      req = JSON.parse(trimmed) as DaemonRequest;
    } catch {
      send({ id: "", ok: false, error: "无效 JSON-Line" });
      return;
    }

    const id = req.id ?? "";
    const cmd = (req.cmd ?? "") as Cmd;

    if (cmd === "ping") {
      send({ id, type: "pong" });
      return;
    }

    pending = pending.then(() => host.dispatch(req, requestBudgetMs(req)));
  });

  const shutdown = async (): Promise<void> => {
    // 优雅退出：先让在途请求在有限宽限内完成（stdin EOF / SIGTERM 场景下不打断未竟请求）。
    try {
      await Promise.race([pending, new Promise((r) => setTimeout(r, 3000))]);
    } catch {
      /* 忽略 */
    }
    host.killEngine();
    process.exit(0);
  };

  rl.on("close", () => {
    void shutdown();
  });
  process.on("SIGTERM", () => rl.close());
  process.on("disconnect", () => rl.close());
}

main();