#!/usr/bin/env node
/**
 * ClineApi — CLI 入口
 *
 * 供 C# / PowerShell 等外部进程调用，stdout 输出单个 JSON 对象。
 *
 * 用法：
 *   node build/cli.js ask "提示词" [选项]
 *   node build/cli.js plan "提示词" [选项]
 *   node build/cli.js resume --id <会话ID> [提示词]
 *   node build/cli.js history [--limit N]
 *   node build/cli.js version
 *   node build/cli.js config
 *   node build/cli.js help
 *
 * 通用选项：
 *   --provider <id>   --model <model>   --cwd <path>
 *   --timeout <sec>   --auto-approve <true|false>
 *   --data-dir <path> --thinking <level>
 *   --system <prompt> --retries <n>     --mode <act|plan>
 *   --api-key <key>   --base-url <url>  （API 凭据，由调用方直接传入）
 */

import { runCline, runCliCommand } from "./cline-cli.js";
import { loadConfig } from "./config.js";
import type { ClineRunOptions } from "./types.js";

// cline-cli → sdk-transport 仅以 `import type` 引用 @cline/sdk 并按需 loadSdk()，
// 顶层静态 import 不触发 SDK 加载副作用，故 version/config 等非 LLM 命令不受影响
// （根因见 docs/analysis/2026/8/8.7 §3.3）。

interface ParsedArgs {
  command: string;
  positional: string[];
  flags: Record<string, string | boolean>;
}

/** 解析命令行参数（支持 --key=value、--key value、--flag、短参数 -k=value） */
function parseArgs(argv: string[]): ParsedArgs {
  const positional: string[] = [];
  const flags: Record<string, string | boolean> = {};

  let i = 0;
  const takeValue = (key: string, inline?: string): void => {
    if (inline !== undefined) {
      flags[key] = inline;
      return;
    }
    if (i + 1 < argv.length && !argv[i + 1].startsWith("-")) {
      flags[key] = argv[i + 1];
      i += 1;
    } else {
      flags[key] = true;
    }
  };

  for (; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === "--") {
      positional.push(...argv.slice(i + 1));
      break;
    }
    if (arg.startsWith("--")) {
      const eq = arg.indexOf("=");
      const key = eq >= 0 ? arg.slice(2, eq) : arg.slice(2);
      const inline = eq >= 0 ? arg.slice(eq + 1) : undefined;
      takeValue(key, inline);
    } else if (arg.startsWith("-") && arg.length > 1) {
      const eq = arg.indexOf("=");
      const key = eq >= 0 ? arg.slice(1, eq) : arg.slice(1);
      const inline = eq >= 0 ? arg.slice(eq + 1) : undefined;
      takeValue(key, inline);
    } else {
      positional.push(arg);
    }
  }

  const command = positional[0] ?? "help";
  return { command, positional: positional.slice(1), flags };
}

function toBool(value: string | boolean | undefined, fallback?: boolean): boolean | undefined {
  if (value === undefined) {
    return fallback;
  }
  if (typeof value === "boolean") {
    return value;
  }
  if (value === "true" || value === "1" || value === "yes") {
    return true;
  }
  if (value === "false" || value === "0" || value === "no") {
    return false;
  }
  return fallback;
}

function toNumber(value: string | boolean | undefined): number | undefined {
  if (typeof value !== "string") {
    return undefined;
  }
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
}

/** 从 flags 构造运行选项 */
function buildOptions(parsed: ParsedArgs, extra?: Partial<ClineRunOptions>): ClineRunOptions {
  const flags = parsed.flags;
  const options: ClineRunOptions = {
    prompt: parsed.positional.join(" ").trim(),
    ...extra,
    provider: typeof flags["provider"] === "string" ? flags["provider"] : undefined,
    apiKey: typeof flags["api-key"] === "string" ? flags["api-key"] : undefined,
    baseUrl: typeof flags["base-url"] === "string" ? flags["base-url"] : undefined,
    clientType: typeof flags["client-type"] === "string" ? flags["client-type"] : undefined,
    model: typeof flags["model"] === "string" ? flags["model"] : undefined,
    cwd: typeof flags["cwd"] === "string" ? flags["cwd"] : undefined,
    timeoutSec: toNumber(flags["timeout"] ?? flags["t"]),
    autoApprove: toBool(flags["auto-approve"]),
    dataDir: typeof flags["data-dir"] === "string" ? flags["data-dir"] : undefined,
    thinking: typeof flags["thinking"] === "string" ? (flags["thinking"] as ClineRunOptions["thinking"]) : undefined,
    systemPrompt: typeof flags["system"] === "string" ? flags["system"] : undefined,
    retries: toNumber(flags["retries"]),
    sessionId: typeof flags["id"] === "string" ? flags["id"] : undefined,
    mode: flags["mode"] === "plan" ? "plan" : flags["mode"] === "act" ? "act" : undefined,
    trace: flags["trace"] === true || flags["trace"] === "true",
  };
  return options;
}

/** 输出单个 JSON 对象 */
function printJson(obj: unknown): void {
  console.log(JSON.stringify(obj, null, 2));
}

function printHelp(): void {
  const help = `ClineApi — 在 C#/PowerShell 中调用 Cline 编码代理

用法:
  node build/cli.js ask <提示词> [选项]        act 模式执行任务
  node build/cli.js plan <提示词> [选项]       plan 模式只规划
  node build/cli.js resume --id <会话ID> [提示词]
  node build/cli.js history [--limit N]        列出历史会话
  node build/cli.js version                    显示 @cline/sdk 版本
  node build/cli.js config                     显示当前生效配置（不含 API Key）
  node build/cli.js help                       显示本帮助

选项:
  --provider <id>       Provider（必须存在于集中凭据文件，或配合 --api-key 传入）
  --api-key <key>       API Key（由调用方直接传入；未传时从集中凭据文件读取）
  --base-url <url>      Provider API 基础 URL（由调用方直接传入；可选）
  --client-type <type>  自定义 Provider handler 类型（openai-responses/openai-compatible/...；可选）
  --model <model>       模型 ID
  --cwd <path>          工作目录
  --timeout <秒>        任务超时
  --auto-approve <bool> 自动批准工具调用
  --data-dir <path>     隔离数据目录
  --thinking <level>    none|low|medium|high|xhigh
  --system <prompt>     自定义系统提示词
  --retries <n>         最大重试次数
  --mode <act|plan>     运行模式（默认 act；resume 可指定 plan）
  --trace               向 stderr 输出传输层阶段标记（定位超时卡点）

API Key 管理: 可由调用方通过 --api-key 直接传入（优先）；未传入时从
<WorkFlowPrj>/credentials/cline.json 读取（已 git 忽略）。`;
  console.log(help);
}

async function main(): Promise<void> {
  const parsed = parseArgs(process.argv.slice(2));

  switch (parsed.command) {
    case "ask":
    case "plan": {
      if (!parsed.positional.length) {
        printJson({ ok: false, error: `缺少提示词。用法: cline-api ${parsed.command} "提示词"` });
        return;
      }
      const options = buildOptions(parsed);
      const result = await runCline({
        ...options,
        mode: parsed.command === "plan" ? "plan" : options.mode ?? "act",
      });
      printJson(result);
      break;
    }

    case "resume": {
      const sessionId = typeof parsed.flags["id"] === "string" ? parsed.flags["id"] : "";
      if (!sessionId) {
        printJson({ ok: false, error: "resume 需要 --id <会话ID>" });
        return;
      }
      const options = buildOptions(parsed);
      const result = await runCline({
        ...options,
        prompt: options.prompt || "继续当前任务",
        sessionId,
        mode: options.mode ?? "act",
      });
      printJson(result);
      break;
    }

    case "history": {
      const limit = toNumber(parsed.flags["limit"]) ?? 10;
      const trace = parsed.flags["trace"] === true || parsed.flags["trace"] === "true";
      const result = await runCliCommand(["history", "--limit", String(limit)], trace);
      printJson({
        ok: result.exitCode === 0,
        exitCode: result.exitCode,
        error: result.error,
        sessions: result.sessions ?? [],
        raw: result.stdout,
      });
      break;
    }

    case "version": {
      const trace = parsed.flags["trace"] === true || parsed.flags["trace"] === "true";
      const result = await runCliCommand(["version"], trace);
      printJson({
        ok: result.exitCode === 0,
        exitCode: result.exitCode,
        error: result.error,
        version: result.stdout.split(/\r?\n/)[0] ?? "",
      });
      break;
    }

    case "config": {
      // 仅输出非敏感配置（不含 API Key）；解析失败时输出可解析的 ok:false，而非抛异常到 stderr。
      try {
        printJson({ ok: true, config: loadConfig() });
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        printJson({ ok: false, error: `配置解析失败: ${message}` });
      }
      break;
    }

    case "help":
    default:
      printHelp();
      break;
  }
}

main()
  .then(() => process.exit(0))
  .catch((err) => {
    const message = err instanceof Error ? err.message : String(err);
    console.error(JSON.stringify({ ok: false, error: message }));
    process.exit(1);
  });
