/**
 * SdkTransport — 基于 @cline/sdk (ClineCore) 的框架级调用通道
 *
 * prompt 以 API 参数（StartSessionInput.prompt）直接传递，
 * 彻底规避 cline Windows 二进制对非 ASCII argv 的解析 bug，
 * 并原生获得 sessionId / 会话恢复能力。
 *
 * 事件流映射：
 *   CoreSessionEvent (chunk/agent_event/status/ended)
 *     → ClineMessage（say/ask）聚合 + ClineRunResult
 */

import * as fs from "fs";
import * as path from "path";
// 仅类型引用（编译期擦除）：sink 由入口 preload 安装后注入，本模块不再自建/patch（唯一入口 = sink-preload）。
import type { AgentSinkHost } from "fs-sink";
import { fromClineDir, resolveClineDataRoot } from "../runtime/cline-data-root.js";

// 仅保留类型导入（编译期擦除，无运行时副作用）。值（ClineCore / getClineDefaultSystemPrompt）
// 一律经 loadSdk() 惰性加载，避免 cli.ts 顶层 import 触发 SDK 模块加载/本地后端初始化副作用，
// 从而拖住 version/config 等非 LLM 命令（根因见 docs/analysis/2026/8/8.7）。
import type { CoreSessionEvent, Llms, RuntimeHost } from "@cline/sdk";

import type { ClineMessage, ClineOutputState, ClineRunOptions, ClineRunResult, ClineRunResultMeta, ClineResultSource } from "../types.js";
import { loadConfig, type ClineApiConfig } from "../config.js";
import { getProviderCredential, resolveProjectRoot } from "../credentials.js";

/** ClineCore 内部 host（类型声明为 private，运行时存在） */
type ClineCoreWithHost = import("@cline/sdk").ClineCore & { host: RuntimeHost };

/** history 等无显式超时的管理命令，初始化阶段默认硬超时（ms）。 */
const DEFAULT_INIT_TIMEOUT_MS = 15000;

/**
 * 传输层阶段追踪（--trace）：向 stderr 流式打阶段标记，不污染 stdout 的最终 JSON。
 * C# 侧（ClineClient.RunNodeAsync）增量捕获 stderr，超时时 dump 尾部，
 * 从而定位「卡在哪个阶段」（SDK 加载 / ClineCore 初始化 / core.start / API 请求）。
 * 格式: [cline-trace] <ts> <stage> [<json detail>]
 */
function trace(enabled: boolean | undefined, stage: string, detail?: unknown): void {
  if (!enabled) return;
  const extra = detail === undefined ? "" : ` ${JSON.stringify(detail)}`;
  process.stderr.write(`[cline-trace] ${Date.now()} ${stage}${extra}\n`);
}

/** 掩码敏感字段（apiKey/baseUrl 等），用于阶段追踪日志。 */
function maskSecret(value?: string): string {
  if (!value) return "(空)";
  if (value.length <= 8) return "****";
  return `${value.slice(0, 4)}...${value.slice(-4)}`;
}

/** 惰性加载 @cline/sdk —— 仅真实 LLM 调用路径才触发 SDK 模块加载与本地后端初始化副作用。 */
async function loadSdk(): Promise<typeof import("@cline/sdk")> {
  return await import("@cline/sdk");
}

/** 读取 @cline/sdk 版本 */
export function readSdkVersion(): string {
  try {
    const pkgPath = path.join(resolveProjectRoot(), "node_modules", "@cline", "sdk", "package.json");
    const pkg = JSON.parse(fs.readFileSync(pkgPath, "utf-8")) as { version?: string };
    return pkg.version ?? "unknown";
  } catch {
    return "unknown";
  }
}

/** 会话事件收集器（导出供回归测试：喂入 session_snapshot/status 事件即可验证权威结果提取）。 */
export class EventCollector {
  readonly messages: ClineMessage[] = [];
  status: "running" | "completed" | "failed" | "aborted" = "running";
  lastError?: string;
  sessionId?: string;
  /** SDK 会话最终快照中的消息（权威源，来自 session_snapshot 事件）。 */
  snapshotMessages?: Llms.Message[];
  /** 会话消息落盘路径（来自 snapshot.artifacts.messagesPath，供回读兜底）。 */
  snapshotMessagesPath?: string;
  /**
   * FsSink 消息读取器：patch 启用时从内存卷→DB 同步取消息（替代磁盘 messages.json 回读）。
   * 未启用时为 undefined → persisted-file 级回退磁盘读取，保持旧行为。
   */
  messagesFromSink?: (sessionId: string) => unknown[] | undefined;
  /** 真实输入 token 用量（P4 遥测：从会话快照消息 usage 字段聚合）。 */
  inputTokens?: number;
  /** 真实输出 token 用量（P4 遥测）。 */
  outputTokens?: number;
  /** 真实缓存读取 token 用量（P4 遥测；provider 支持时存在）。 */
  cacheReadTokens?: number;
  /** 模型停止原因（P4 遥测：snapshot/ended 携带的 stopReason/finishReason）。 */
  stopReason?: string;

  /** 处理 CoreSessionEvent，聚合消息与错误 */
  onEvent(ev: CoreSessionEvent): void {
    switch (ev.type) {
      case "status":
        this.sessionId = ev.payload.sessionId;
        if (ev.payload.status === "failed" || ev.payload.status === "aborted") {
          this.status = ev.payload.status;
        }
        if (ev.payload.status === "completed") {
          this.status = "completed";
        }
        break;

      case "ended":
        this.sessionId = ev.payload.sessionId;
        if (ev.payload.reason === "error") {
          this.status = "failed";
        }
        if (ev.payload.reason === "aborted") {
          this.status = "aborted";
        }
        // P4 遥测：ended 事件若携带显式 stopReason/finishReason 则捕获（容错：字段缺失则忽略）。
        const endedStop =
          (ev.payload as unknown as Record<string, unknown>)?.stopReason ??
          (ev.payload as unknown as Record<string, unknown>)?.finishReason;
        if (typeof endedStop === "string") this.stopReason = endedStop;
        break;

      case "session_snapshot": {
        // 会话最终快照 = 结果权威源（含完整 Llms.Message[] 与落盘路径），
        // 规避「SDK 事件格式升级后旧 say/ask 抓不到」导致成功但空结果（P0 根因）。
        this.sessionId = ev.payload.snapshot.sessionId;
        if (Array.isArray(ev.payload.snapshot.messages)) {
          this.snapshotMessages = ev.payload.snapshot.messages as Llms.Message[];
        }
        const msgsPath = ev.payload.snapshot.artifacts?.messagesPath;
        if (msgsPath) this.snapshotMessagesPath = msgsPath;
        // P4 遥测：从快照消息聚合真实 token 用量，并捕获停止原因（容错：字段缺失则忽略）。
        if (Array.isArray(ev.payload.snapshot.messages)) {
          this.aggregateUsage(ev.payload.snapshot.messages as unknown[]);
        }
        const snap = ev.payload.snapshot as unknown as Record<string, unknown>;
        const snapshotStop = snap?.stopReason ?? snap?.finishReason;
        if (typeof snapshotStop === "string") this.stopReason = snapshotStop;
        break;
      }

      case "chunk":
        // agent 流：chunk 内容是 NDJSON 格式的 agent 事件
        if (ev.payload.stream !== "agent") {
          break;
        }
        try {
          type ChunkEventShape = Omit<Partial<ClineMessage>, "type"> & {
            type?: string;
            error?: { message?: string } | string;
            message?: string;
          };
          const parsed = JSON.parse(ev.payload.chunk) as ChunkEventShape;
          if (parsed && typeof parsed === "object" && (parsed.type === "say" || parsed.type === "ask")) {
            this.messages.push({
              type: parsed.type,
              text: parsed.text ?? "",
              ts: typeof parsed.ts === "number" ? parsed.ts : Date.now(),
              say: parsed.say,
              ask: parsed.ask,
              reasoning: parsed.reasoning,
              partial: parsed.partial,
            });
          } else if (parsed?.type === "error") {
            const err = parsed.error;
            this.lastError =
              typeof err === "string"
                ? err
                : (err as { message?: string })?.message ?? parsed.message ?? "未知错误";
          }
        } catch {
          // 非 JSON chunk 忽略
        }
        break;

      default:
        break;
    }
  }

  /** 最终结果文本：最后一个非流式 say 消息（旧事件格式降级链）。 */
  getResultText(): string {
    const finishedSays = this.messages.filter((m) => m.type === "say" && !m.partial);
    return finishedSays[finishedSays.length - 1]?.text ?? "";
  }

  /** 参与结果提取的消息条数：有快照以快照为准，否则用事件流聚合数（可观测）。 */
  getMessageCount(): number {
    return this.snapshotMessages?.length ?? this.messages.length;
  }

  /** P4 遥测：从消息数组聚合真实 token 用量（容错：message.usage.{input_tokens,output_tokens,cache_read_input_tokens}）。 */
  private aggregateUsage(messages: unknown[] | undefined): void {
    if (!Array.isArray(messages)) return;
    for (const m of messages) {
      const usage = (m as { usage?: Record<string, unknown> })?.usage;
      if (!usage || typeof usage !== "object") continue;
      const num = (k: string): number | undefined =>
        typeof usage[k] === "number" ? (usage[k] as number) : undefined;
      const it = num("input_tokens") ?? num("inputTokens");
      const ot = num("output_tokens") ?? num("outputTokens");
      const cr = num("cache_read_input_tokens") ?? num("cacheReadTokens") ?? num("cache_read");
      if (it) this.inputTokens = (this.inputTokens ?? 0) + it;
      if (ot) this.outputTokens = (this.outputTokens ?? 0) + ot;
      if (cr) this.cacheReadTokens = (this.cacheReadTokens ?? 0) + cr;
    }
  }

  /** 从 Llms.Message[]（role/content）提取最后一条 assistant 文本。 */
  private static extractAssistantText(messages: unknown[]): string {
    for (let i = messages.length - 1; i >= 0; i--) {
      const m = messages[i] as { role?: string; content?: unknown } | undefined;
      if (!m || m.role !== "assistant") continue;
      const content = m.content;
      if (typeof content === "string") {
        if (content.trim()) return content.trim();
        continue;
      }
      if (!Array.isArray(content)) continue;
      const parts: string[] = [];
      for (const block of content as Array<{ type?: string; text?: string }>) {
        if (block && block.type === "text" && typeof block.text === "string" && block.text.trim()) {
          parts.push(block.text.trim());
        }
      }
      if (parts.length > 0) return parts.join("\n");
    }
    return "";
  }

  /** 从落盘会话消息文件回读最后一条 assistant 文本。 */
  private static readPersistedAssistantText(messagesPath: string): string {
    try {
      if (!fs.existsSync(messagesPath)) return "";
      const raw = JSON.parse(fs.readFileSync(messagesPath, "utf-8")) as { messages?: unknown[] };
      if (!Array.isArray(raw.messages)) return "";
      return EventCollector.extractAssistantText(raw.messages);
    } catch {
      return "";
    }
  }

  /**
   * 落盘消息路径：快照声明优先，否则按权威根 `<sessionsDir>/<id>/<id>.messages.json` 推导。
   * `clineDir` 为调用方显式覆盖（历史 `--data-dir` 语义：Cline 主目录），缺省用权威解析器
   * （与 SDK 同一算法/同一环境变量，根不再靠猜）。
   */
  private resolveMessagesPath(clineDir?: string): string | undefined {
    if (this.snapshotMessagesPath) return this.snapshotMessagesPath;
    if (!this.sessionId) return undefined;
    const sessionsDir = clineDir ? fromClineDir(clineDir).sessionsDir : resolveClineDataRoot().sessionsDir;
    return path.join(sessionsDir, this.sessionId, `${this.sessionId}.messages.json`);
  }

  /**
   * 权威结果文本（P0）：优先 SDK 会话快照 → 落盘消息回读 → 事件流聚合（旧 say/ask）。
   * 返回 { text, source }，source 标识取数来源，供 meta.eventSource 可观测。
   */
  getAuthoritativeResultText(dataDir?: string): { text: string; source: ClineResultSource } {
    if (this.snapshotMessages && this.snapshotMessages.length > 0) {
      const t = EventCollector.extractAssistantText(this.snapshotMessages);
      if (t) return { text: t, source: "snapshot" };
    }

    const msgsPath = this.resolveMessagesPath(dataDir);
    if (msgsPath) {
      // FsSink 已启用：persisted-file 级改从 DB（内存卷→后端）取，不再读磁盘 messages.json。
      if (this.sessionId && this.messagesFromSink) {
        const msgs = this.messagesFromSink(this.sessionId);
        if (msgs && msgs.length > 0) {
          const t = EventCollector.extractAssistantText(msgs);
          if (t) return { text: t, source: "persisted-file" };
        }
      }
      // 兼容：patch 未启用 / DB 未命中 → 回退磁盘读取，保持旧行为。
      const t = EventCollector.readPersistedAssistantText(msgsPath);
      if (t) return { text: t, source: "persisted-file" };
    }

    const streamText = this.getResultText();
    if (streamText) return { text: streamText, source: "event-stream" };

    // 有会话信息但无文本：会话本身空（source=snapshot）；连会话都没有：事件格式/链路缺失（source=none）。
    return { text: "", source: this.snapshotMessages || this.sessionId ? "snapshot" : "none" };
  }
}

/** ClineCore 会话配置（含 systemPrompt 必填字段） */
interface StartSessionConfigShape {
  providerId: string;
  modelId: string;
  apiKey?: string;
  baseUrl?: string;
  /** 自定义 provider 完整配置（handler 路由 + wire_api 等），经 createConfig 构造 */
  providerConfig?: Llms.ProviderConfig;
  thinking?: boolean;
  reasoningEffort?: string;
  enableTools: boolean;
  enableSpawnAgent: boolean;
  enableAgentTeams: boolean;
  cwd?: string;
  mode: "act" | "plan";
  maxIterations: number;
  systemPrompt: string;
  sessionId?: string;
}

/**
 * 基于 @cline/sdk 的调用通道。
 *
 * 用法：
 *   const transport = new SdkTransport({ sink: getInstalledSink() });
 *   const result = await transport.run({ prompt: "中文任务..." });
 */
export class SdkTransport {
  private corePromise?: Promise<ClineCoreWithHost>;
  /** 引擎进程内按 session 的调用计数（真实轮次观测；进程级，跨 worker 不一致，仅参考）。 */
  private readonly sessionRuns = new Map<string, number>();
  /** 已 patch 的 FsSink 门面（由入口 preload 安装后注入；未注入 = 不拦截）。 */
  private readonly sink?: AgentSinkHost;

  /**
   * @param opts.sink 入口 preload（`installSink`）安装的 FsSink 门面；仅 engine/cli 子进程注入（H3 隔离）。
   *        patch 时机唯一归口 preload（须先于 loadSdk/import("@cline/sdk")，H1），本类不再自行 patch/unpatch。
   */
  constructor(opts?: { sink?: AgentSinkHost }) {
    this.sink = opts?.sink;
  }

  /** 阶段追踪回调（执行面 engine 注入：把 loadSdk/create/core.start 等阶段作为协议帧上报控制面）。 */
  private onStage?: (stage: string, detail?: unknown) => void;

  /** 注入阶段上报回调（供 daemon 执行面子进程把每阶段进度推给控制面）。 */
  setStageReporter(reporter?: (stage: string, detail?: unknown) => void): void {
    this.onStage = reporter;
  }

  /** 统一阶段出口：stderr trace（--trace，向后兼容）+ 可选 onStage 回调（协议帧，观测默认开）。 */
  private emitStage(enabled: boolean | undefined, stage: string, detail?: unknown): void {
    // daemon/engine 路径经 onStage 结构化帧上报同一阶段；`--trace` 的 stderr 标记与其内容重复，
    // 仅在无 onStage（独立 CLI）时写 stderr，避免 C# 侧同阶段双通道重复日志。
    if (!this.onStage) trace(enabled, stage, detail);
    this.onStage?.(stage, detail);
  }

  /**
   * 惰性创建 ClineCore（backendMode: "local" 进程内自包含）。
   *
   * @param timeoutMs 可选硬超时（ms）。SDK 初始化是「唯一的卡死点」且无内部超时——
   *                  定时器必须在进入任何 await SDK 之前就绪；超时/失败时清空缓存，
   *                  允许后续调用重试，避免永久挂起缓存。
   */
  private ensureCore(timeoutMs?: number, traceEnabled?: boolean): Promise<ClineCoreWithHost> {
    this.emitStage(traceEnabled, "ensure-core-start", { timeoutMs: timeoutMs ?? 0, cacheHit: !!this.corePromise });
    if (!this.corePromise) {
      this.corePromise = loadSdk()
        .then((sdk) => {
          this.emitStage(traceEnabled, "load-sdk-ok");
          return sdk.ClineCore.create({
            clientName: "cline-api",
            backendMode: "local",
          }) as Promise<ClineCoreWithHost>;
        })
        .then((core) => {
          this.emitStage(traceEnabled, "ensure-core-ok");
          return core;
        });
    }
    const init = this.corePromise;
    if (!timeoutMs || timeoutMs <= 0) {
      return init;
    }

    let timer: ReturnType<typeof setTimeout> | undefined;
    const timeout = new Promise<ClineCoreWithHost>((_, reject) => {
      timer = setTimeout(
        () => reject(new Error(`ClineCore 初始化超时（${timeoutMs}ms）`)),
        timeoutMs
      );
    });

    return Promise.race([init, timeout])
      .finally(() => {
        if (timer !== undefined) {
          clearTimeout(timer);
        }
      })
      .catch((err) => {
        this.emitStage(traceEnabled, "ensure-core-failed", { message: err instanceof Error ? err.message : String(err) });
        // 初始化超时/失败：清空缓存，允许后续调用重试，避免永久挂起缓存。
        if (this.corePromise === init) {
          this.corePromise = undefined;
        }
        throw err;
      });
  }

  /** 执行任务（act/plan 模式由 options.mode 决定） */
  async run(options: ClineRunOptions): Promise<ClineRunResult> {
    const config = loadConfig();
    const { provider, credential, filePath } = getProviderCredential(
      options.provider ?? config.provider,
      config.credentialsFile
    );

    // API 凭据：调用方显式传入的参数优先，未传时回退到集中凭据文件
    const apiKey = options.apiKey ?? credential?.apiKey;
    const baseUrl = options.baseUrl ?? credential?.baseUrl;
    // 自定义 Provider 的 handler 客户端类型（wire_api 对应值）：调用方参数优先，回退到集中凭据
    const clientType = options.clientType ?? credential?.clientType;

    if (!apiKey) {
      return {
        ok: false,
        exitCode: -1,
        messages: [],
        resultText: "",
        error: `未找到 Provider「${provider || "(未指定)"}」的 API Key。请通过参数传入 apiKey，或检查集中凭据文件: ${filePath}`,
      };
    }

    const model = options.model ?? config.model ?? credential?.model;
    const mode: "act" | "plan" = options.mode === "plan" ? "plan" : "act";
    const cwd = options.cwd ?? config.cwd;
    const thinkingLevel = options.thinking ?? config.thinking;

    if (!model) {
      return {
        ok: false,
        exitCode: -1,
        messages: [],
        resultText: "",
        error: `未配置模型 ID（provider: ${provider}）。请在 cline.config.json 或集中凭据文件中设置 model`,
      };
    }

    // 阶段追踪起点：记录本次调用的配置快照（key 掩码），确认命令按预期分发。
    this.emitStage(options.trace, "run-start", {
      mode,
      provider,
      model,
      baseUrl: maskSecret(baseUrl),
      apiKey: maskSecret(apiKey),
      clientType: clientType ?? null,
      timeoutSec: options.timeoutSec ?? null,
      cwd: cwd ?? null,
    });

    // 自定义 Provider（如 OpenAI 兼容端点 base_url + wire_api）：
    // 构造完整 ProviderConfig，交给 SDK 路由到对应 handler 并应用 baseUrl。
    const providerConfig: Llms.ProviderConfig | undefined =
      clientType && apiKey && model
        ? {
            providerId: provider,
            modelId: model,
            apiKey,
            ...(baseUrl ? { baseUrl } : {}),
            clientType,
          }
        : undefined;

    // 真实 LLM 调用：在此惰性加载 SDK（模块缓存，后续 ensureCore 复用同一实例）。
    this.emitStage(options.trace, "load-sdk-start");
    const sdk = await loadSdk();
    this.emitStage(options.trace, "load-sdk-ok");

    // 构造会话配置
    const sessionConfig: StartSessionConfigShape = {
      providerId: provider,
      modelId: model,
      apiKey,
      ...(baseUrl ? { baseUrl } : {}),
      ...(providerConfig ? { providerConfig } : {}),
      thinking: thinkingLevel !== undefined && thinkingLevel !== "none",
      reasoningEffort: thinkingLevel !== undefined && thinkingLevel !== "none" ? thinkingLevel : undefined,
      enableTools: true,
      enableSpawnAgent: false,
      enableAgentTeams: false,
      cwd: cwd ?? undefined,
      mode,
      maxIterations: 5,
      systemPrompt: sdk.getClineDefaultSystemPrompt({
        rootPath: cwd ?? process.cwd(),
        providerId: provider,
        planModeSwitchTool: false,
      }),
      ...(options.sessionId ? { sessionId: options.sessionId } : {}),
    };

    // 预算竞速起点最前移：定时器必须在进入任何 await SDK 之前就绪（根因 §3.1）。
    const timeoutMs = (options.timeoutSec ?? config.timeoutSec ?? 0) * 1000;

    // 阶段 1：ensureCore（含硬超时）——卡在初始化时快速失败并归因，而非永久阻塞。
    this.emitStage(options.trace, "phase1-ensure-core", { timeoutMs });
    let core: ClineCoreWithHost;
    try {
      core = await this.ensureCore(timeoutMs > 0 ? timeoutMs : undefined, options.trace);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      this.emitStage(options.trace, "phase1-ensure-core-failed", { message });
      return {
        ok: false,
        exitCode: 1,
        messages: [],
        resultText: "",
        sessionId: undefined,
        error: message,
        stage: "ensureCore",
      };
    }

    const collector = new EventCollector();
    // FsSink 启用：persisted-file 级从内存卷→DB 取消息（getAuthoritativeResultText 使用）。
    if (this.sink) {
      collector.messagesFromSink = (sid) => this.sink!.getMessagesSync("cline", sid);
    }

    const unsub = core.host.subscribe((ev: CoreSessionEvent) => collector.onEvent(ev));
    this.emitStage(options.trace, "session-subscribe-ok");

    let timedOut = false;

    // 阶段 2：core.start()（API 请求 / 会话执行）——沿用原竞速定时器。
    try {
      this.emitStage(options.trace, "phase2-core-start");
      const startPromise = core.start({
        config: sessionConfig as never,
        prompt: options.prompt,
        source: "cline-api",
        mode,
      });

      if (timeoutMs > 0) {
        const timer = new Promise<never>((_, reject) =>
          setTimeout(() => {
            timedOut = true;
            this.emitStage(options.trace, "api-timeout", { timeoutMs });
            reject(new Error(`Cline 任务超时（${Math.round(timeoutMs / 1000)}s）`));
          }, timeoutMs)
        );
        await Promise.race([startPromise, timer]);
      } else {
        await startPromise;
      }
      this.emitStage(options.trace, "phase2-core-start-ok");
    } catch (err) {
      if (!timedOut && !collector.lastError) {
        const message = err instanceof Error ? err.message : String(err);
        collector.lastError = message;
      }
    } finally {
      unsub();
    }

    const failed = collector.status === "failed" || collector.status === "aborted" || !!collector.lastError || timedOut;
    const ok = !failed;
    // P0 结果权威源：会话快照 → 落盘回读 → 事件流聚合（旧 say/ask）。
    const authoritative = collector.getAuthoritativeResultText(options.dataDir);
    const resultText = authoritative.text;

    // 归档数据源（磁盘零残留联动）：FsSink 启用时从内存卷→backend 取全量消息，
    // 随终帧回带使 C# 归档不依赖读磁盘 messages.json（FsSink 拦截写盘 → 磁盘无文件）。
    // 仅成功且有会话 ID 时回带；未启用 / 未命中 / 空消息 → undefined（C# 回退磁盘/聚合）。
    let archivedMessages: unknown[] | undefined;
    if (this.sink && collector.sessionId) {
      try {
        // 先终态落库（取消 debounce，直接遍历内存卷快照镜像到 backend），再同步取读。
        await this.sink.flushAll();
        archivedMessages = this.sink.getMessagesSync("cline", collector.sessionId);
      } catch {
        archivedMessages = undefined; // 提取失败不阻断主返回链
      }
    }
    // 归档元信息：仅 FsSink 实际回带时置 source=sink；否则 undefined（C# 回退磁盘/聚合）。
    const archiveMeta =
      this.sink && collector.sessionId && archivedMessages && archivedMessages.length > 0
        ? { messageCount: archivedMessages.length, source: "sink" as const }
        : undefined;

    // 结果完整性状态：成功但无文本 → 显式 missing（含 emptyReason/meta），不再静默占位。
    let outputState: ClineOutputState | undefined;
    let emptyReason: string | undefined;
    if (ok) {
      outputState = resultText ? "ok" : "missing";
      if (!resultText) {
        emptyReason =
          authoritative.source === "event-stream" || authoritative.source === "none"
            ? "no_assistant_text"
            : "session_empty";
      }
    }

    // 观测：FsSink 命中/miss/绑定自检（未注入 sink 时 undefined）。misses=命中权威根却未拦截的路径数。
    const sinkObs = this.sink?.observe();
    const meta: ClineRunResultMeta = {
      messageCount: collector.getMessageCount(),
      eventSource: authoritative.source,
      resultTextLength: resultText.length,
      sessionId: collector.sessionId,
      inputTokens: collector.inputTokens,
      outputTokens: collector.outputTokens,
      cacheReadTokens: collector.cacheReadTokens,
      sink: sinkObs
        ? {
            hits: sinkObs.totalHits,
            perRoute: sinkObs.perRoute,
            misses: sinkObs.perSdk["cline"]?.misses ?? 0,
            bindings: sinkObs.bindings,
          }
        : undefined,
    };

    // 引擎回带真实轮次（进程内按 session 计数，供观测；归档权威轮次 = C# ur_nodes 已归档最大 + 1，跨 worker 一致）。
    const sessionKey = collector.sessionId ?? options.sessionId;
    let round: number | undefined;
    if (sessionKey) {
      const next = (this.sessionRuns.get(sessionKey) ?? 0) + 1;
      this.sessionRuns.set(sessionKey, next);
      round = next;
    }

    return {
      ok,
      exitCode: failed ? 1 : 0,
      messages: collector.messages,
      resultText,
      sessionId: collector.sessionId,
      error: timedOut
        ? `Cline 任务超时（${Math.round(timeoutMs / 1000)}s）`
        : collector.lastError,
      stage: timedOut ? "api" : undefined,
      outputState,
      emptyReason,
      meta,
      stopReason: collector.stopReason,
      archivedMessages,
      archiveMeta,
      round,
    };
  }

  /** 恢复历史会话继续执行 */
  async resume(sessionId: string, prompt: string, options?: Partial<ClineRunOptions>): Promise<ClineRunResult> {
    return this.run({ ...options, prompt, sessionId, mode: options?.mode ?? "act" });
  }

  /** 列出历史会话记录 */
  async history(limit = 10, traceEnabled?: boolean): Promise<unknown[]> {
    const core = await this.ensureCore(DEFAULT_INIT_TIMEOUT_MS, traceEnabled);
    this.emitStage(traceEnabled, "history-list", { limit });
    const records = await core.list(limit);
    this.emitStage(traceEnabled, "history-list-ok");
    return records as unknown[];
  }

  /**
   * 预热：仅初始化 ClineCore（触发 loadSdk + create），不发起任何会话/API 请求。
   * 供 daemon 池在启动期后台预热，把 56s 冷启动移出请求路径；预热后 corePromise 缓存，
   * 后续 history/run 复用同一实例。
   * @param timeoutMs 硬超时（ms），需覆盖冷启动；默认 90s。
   */
  async warmup(timeoutMs = 90000, traceEnabled?: boolean): Promise<void> {
    this.emitStage(traceEnabled, "warmup-start", { timeoutMs });
    await this.ensureCore(timeoutMs, traceEnabled);
    this.emitStage(traceEnabled, "warmup-ok");
  }

  /** SDK 版本 */
  async version(): Promise<string> {
    return readSdkVersion();
  }

  /**
   * 释放 ClineCore 资源。
   * 终态落库（flushAll）在此收口；fs patch 的 uninstall 归口入口 preload 的 disposeSink（本类不做 unpatch）。
   */
  async dispose(): Promise<void> {
    if (this.sink) {
      try {
        await this.sink.flushAll();
      } catch {
        /* 忽略 */
      }
    }
    if (this.corePromise) {
      try {
        const core = await this.corePromise;
        await core.dispose();
      } catch {
        /* 忽略 */
      }
      this.corePromise = undefined;
    }
  }
}
