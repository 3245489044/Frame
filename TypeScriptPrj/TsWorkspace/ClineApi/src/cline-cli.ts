/**
 * ClineApi — 调用通道门面
 *
 * 基于 @cline/sdk（ClineCore）执行任务。prompt 以 API 参数传递，
 * 中文内容原生支持，无命令行编码问题。
 *
 * 历史说明：早期版本通过 spawn cline CLI（headless --json）实现，
 * 现已被官方 SDK 内嵌方案取代（prompt 走 API 参数，消除 argv 依赖）。
 */

import type { ClineRunOptions, ClineRunResult } from "./types.js";
import { SdkTransport } from "./transport/sdk-transport.js";

/** 执行一次 Cline 任务（act/plan 由 options.mode 决定） */
export async function runCline(options: ClineRunOptions): Promise<ClineRunResult> {
  const transport = new SdkTransport();

  try {
    return await transport.run(options);
  } finally {
    await transport.dispose();
  }
}

/**
 * 执行管理类命令（version / history）。
 * 返回形状与早期 CLI 进程实现保持一致（stdout/exitCode/error）。
 * history 额外携带结构化 sessions，避免调用方对 stdout 二次 JSON.parse。
 */
export async function runCliCommand(
  command: string[],
  traceEnabled?: boolean
): Promise<{ stdout: string; exitCode: number; error?: string; sessions?: unknown[] }> {
  const transport = new SdkTransport();
  const [cmd, ...rest] = command;

  try {
    if (cmd === "version") {
      const version = await transport.version();
      return { stdout: version, exitCode: 0 };
    }

    if (cmd === "history") {
      const limitIdx = rest.indexOf("--limit");
      const limit =
        limitIdx >= 0 && rest[limitIdx + 1] ? Number(rest[limitIdx + 1]) || 10 : 10;
      const records = await transport.history(limit, traceEnabled);
      return { stdout: JSON.stringify(records, null, 2), exitCode: 0, sessions: records };
    }

    return { stdout: "", exitCode: -1, error: `不支持的管理命令: ${cmd}` };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    return { stdout: "", exitCode: 1, error: message };
  } finally {
    await transport.dispose();
  }
}
