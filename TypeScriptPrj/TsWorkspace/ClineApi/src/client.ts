/**
 * ClineApi — 类型化客户端
 *
 * 封装 runCline / runCliCommand，提供与 Cline CLI 能力对齐的 API。
 */

import { runCline, runCliCommand } from "./cline-cli.js";
import type { ClineHistoryEntry, ClineRunOptions, ClineRunResult } from "./types.js";

/** 客户端构造选项 */
export interface ClineClientOptions {
  /** 全局默认运行选项（provider / model / cwd / timeoutSec 等） */
  defaults?: Partial<Omit<ClineRunOptions, "prompt">>;
}

/**
 * Cline 客户端。
 *
 * 用法：
 *   const client = new ClineClient();
 *   const result = await client.ask("重构这个模块");
 */
export class ClineClient {
  private readonly defaults: Partial<Omit<ClineRunOptions, "prompt">>;

  constructor(options: ClineClientOptions = {}) {
    this.defaults = options.defaults ?? {};
  }

  /** act 模式执行任务 */
  async ask(prompt: string, options?: Partial<Omit<ClineRunOptions, "prompt" | "mode">>): Promise<ClineRunResult> {
    return this.run({ ...options, prompt, mode: "act" });
  }

  /** plan 模式：只规划不执行 */
  async plan(prompt: string, options?: Partial<Omit<ClineRunOptions, "prompt" | "mode">>): Promise<ClineRunResult> {
    return this.run({ ...options, prompt, mode: "plan" });
  }

  /** 恢复历史会话继续执行 */
  async resume(
    sessionId: string,
    prompt?: string,
    options?: Partial<Omit<ClineRunOptions, "prompt" | "sessionId">>
  ): Promise<ClineRunResult> {
    return this.run({
      ...options,
      prompt: prompt ?? "继续当前任务",
      sessionId,
      mode: "act",
    });
  }

  /** 列出历史会话（原始输出） */
  async history(limit = 10): Promise<{ stdout: string; exitCode: number; error?: string }> {
    const result = await runCliCommand(["history", "--limit", String(limit)]);
    return result;
  }

  /** 解析 history 输出为结构化数组（尽力而为） */
  parseHistory(stdout: string): ClineHistoryEntry[] {
    const trimmed = stdout.trim();
    if (!trimmed) {
      return [];
    }
    try {
      const parsed = JSON.parse(trimmed) as unknown;
      if (Array.isArray(parsed)) {
        return parsed as ClineHistoryEntry[];
      }
      if (Array.isArray((parsed as { sessions?: unknown[] })?.sessions)) {
        return (parsed as { sessions: ClineHistoryEntry[] }).sessions;
      }
      return [];
    } catch {
      return [];
    }
  }

  /** Cline CLI 版本 */
  async version(): Promise<{ stdout: string; exitCode: number; error?: string }> {
    return runCliCommand(["version"]);
  }

  /** 诊断配置 */
  async doctor(): Promise<{ stdout: string; exitCode: number; error?: string }> {
    return runCliCommand(["doctor"]);
  }

  private async run(options: ClineRunOptions): Promise<ClineRunResult> {
    return runCline({ ...this.defaults, ...options, prompt: options.prompt });
  }
}
