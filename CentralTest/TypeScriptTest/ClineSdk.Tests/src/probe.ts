import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { sdkEntry } from "./config.js";

export type ProbeStatus = "ok" | "error" | "timeout";
export interface ProbeResult {
  status: ProbeStatus;
  durationMs: number;
  stdout: string;
  stderr: string;
}

export interface ProbeOptions {
  /** backendMode（缺省 "local"）。 */
  mode?: string;
  /** 是否在 create 后尝试 core.start。 */
  start?: boolean;
  /** 进程级硬超时（ms）。同步阻塞也能被 kill 兜底。缺省 20s。 */
  timeoutMs?: number;
}

/**
 * 启动一个独立子进程执行 Cline 调用，并带进程级硬超时。
 *
 * 为什么必须是子进程 + 进程级超时：
 * Cline 的 `@cline/core` 模块加载/create 可能**同步阻塞事件循环**，此时 JS 侧
 * `setTimeout`（含本框架、引擎、看门狗）都不会触发。只有把调用放进独立子进程，
 * 由父进程在超时后 `kill("SIGKILL")`，才能把一个「同步阻塞的挂起」变成有界的可观测结果。
 */
export function runProbe(opts: ProbeOptions = {}): Promise<ProbeResult> {
  const timeoutMs = opts.timeoutMs ?? 20_000;
  const runner = path.join(path.dirname(fileURLToPath(import.meta.url)), "runner.mjs");
  const args = [runner, sdkEntry, opts.mode ?? "local"];
  if (opts.start) args.push("--start");

  return new Promise((resolve) => {
    const child = spawn(process.execPath, args, { windowsHide: true });
    let stdout = "";
    let stderr = "";
    let timedOut = false;
    child.stdout.on("data", (d) => (stdout += d.toString()));
    child.stderr.on("data", (d) => (stderr += d.toString()));
    const t0 = Date.now();

    const timer = setTimeout(() => {
      timedOut = true;
      try {
        child.kill("SIGKILL");
      } catch {
        /* 已退出则忽略 */
      }
    }, timeoutMs);

    child.on("error", (e) => {
      clearTimeout(timer);
      resolve({
        status: "error",
        durationMs: Date.now() - t0,
        stdout,
        stderr: stderr + "\n[spawn error] " + e.message,
      });
    });

    child.on("close", () => {
      clearTimeout(timer);
      const durationMs = Date.now() - t0;
      resolve({
        status: timedOut ? "timeout" : stdout.includes("RESULT:ok") ? "ok" : "error",
        durationMs,
        stdout,
        stderr,
      });
    });
  });
}
