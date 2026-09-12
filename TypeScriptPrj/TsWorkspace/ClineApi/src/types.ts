/**
 * ClineApi — 类型定义
 *
 * 与 Cline CLI（headless 模式 + --json）的输出结构对齐。
 */

/** Cline CLI --json 输出的单条消息（NDJSON 行） */
export interface ClineMessage {
  /** 消息类别：say=正常消息，ask=需要确认的问题 */
  type: "ask" | "say";
  /** 消息内容 */
  text: string;
  /** Unix 毫秒时间戳 */
  ts: number;
  /** type 为 say 时的子类型 */
  say?: string;
  /** type 为 ask 时的子类型 */
  ask?: string;
  /** 模型推理内容（可选） */
  reasoning?: string;
  /** 是否为流式中间消息（partial=true 表示未完成） */
  partial?: boolean;
}

/** Cline 运行模式 */
export type ClineMode = "act" | "plan";

/** 推理努力级别 */
export type ClineThinking = "none" | "low" | "medium" | "high" | "xhigh";

/**
 * 单次运行选项（映射到 Cline CLI 参数）。
 * 未指定的字段会回退到 cline.config.json 与集中凭据（credentials/cline.json）。
 */
export interface ClineRunOptions {
  /** 提示词 */
  prompt: string;
  /** 运行模式：act（默认）| plan */
  mode?: ClineMode;
  /** Provider 标识（如 deepseek），必须与集中凭据中的 providers 键一致 */
  provider?: string;
  /** API Key（由调用方直接传入；未传时回退到集中凭据文件） */
  apiKey?: string;
  /** Provider API 基础 URL（由调用方直接传入；未传时回退到集中凭据） */
  baseUrl?: string;
  /**
   * 自定义 Provider 的 handler 客户端类型（wire_api 对应值）：
   * "openai-responses" | "openai-compatible" | "anthropic" | "gemini" | ...；
   * 内置 provider 无需设置，仅自定义 providerId 需配合路由。
   */
  clientType?: string;
  /** 模型 ID */
  model?: string;
  /** 工作目录 */
  cwd?: string;
  /** 超时（秒），0 或未指定 = 使用配置默认值 */
  timeoutSec?: number;
  /** 是否自动批准全部工具调用 */
  autoApprove?: boolean;
  /** 会话 ID（恢复历史会话） */
  sessionId?: string;
  /** 隔离的 Cline 数据目录 */
  dataDir?: string;
  /** 推理努力级别 */
  thinking?: ClineThinking;
  /** 自定义系统提示词 */
  systemPrompt?: string;
  /** 连续错误最大重试次数 */
  retries?: number;
  /** warmup 命令的初始化硬超时（秒）；仅 daemon warmup 命令使用，覆盖 ClineCore 冷启动（由 C# 池预算推导）。 */
  warmupTimeoutSec?: number;
  /**
   * 是否输出传输层阶段追踪（--trace）：true 时向 **stderr** 流式打阶段标记
   * （run-start / load-sdk / ensure-core / core-start / 首块 / ended 等），
   * 供 C# 侧增量捕获并在超时时 dump 尾部，定位「卡在哪个阶段」。
   */
  trace?: boolean;
}

/** 结果完整性状态：ok=有结果文本；missing=成功但未捕获输出；partial=仅有部分内容。 */
export type ClineOutputState = "ok" | "missing" | "partial";

/** 结果取数来源（可观测：定位「成功但空」是事件格式缺还是会话本身空）。 */
export type ClineResultSource = "snapshot" | "persisted-file" | "event-stream" | "none";

/** 归档来源（FsSink 内存卷 / 磁盘回读 / 事件流聚合 / 无）。 */
export type ArchiveSource = "sink" | "disk" | "aggregate" | "none";

/**
 * 统一执行结果归档载荷（SDK 无关）——各 SDK 结果模型对齐此约定，供宿主统一消费，不再读磁盘。
 * Cline 侧 `ClineRunResult` 兼容保留 `archivedMessages` 并同时携带 `archiveMeta`。
 */
export interface AgentRunArtifact {
  sessionId?: string;
  /** 本次运行的全部消息（若拦截/可提取则非空）。 */
  archivedMessages?: unknown[];
  /** 归档元信息（消息条数、来源、是否来自 FsSink）。 */
  archiveMeta?: { messageCount?: number; source: ArchiveSource };
}

/** 结果元信息（随终帧回带，便于框架层归因/回归断言）。 */
export interface ClineRunResultMeta {
  /** 参与结果提取的消息条数（快照为准，缺省用事件流聚合数）。 */
  messageCount: number;
  /** resultText 的取数来源。 */
  eventSource: ClineResultSource;
  /** 结果文本长度。 */
  resultTextLength: number;
  /** 会话 ID。 */
  sessionId?: string;
  /** 真实输入 token 用量（P4 遥测；存在时供框架边界评估，缺失则估算）。 */
  inputTokens?: number;
  /** 真实输出 token 用量（P4 遥测）。 */
  outputTokens?: number;
  /** 真实缓存读取 token 用量（P4 遥测；provider 支持时存在）。 */
  cacheReadTokens?: number;
  /**
   * FsSink 观测（fsSink 已安装时有值）：
   *  - hits：命中内存卷的调用计数（验收判据 > 0，证明拦截真的发生）；
   *  - perRoute：每路由命中数；
   *  - misses：命中权威数据根却未拦截的路径数（验收判据 == 0，证明根对齐）；
   *  - bindings：ESM 绑定自检未对齐方法名（空 = 通过；非空 = facade 早于 patch 建立）。
   */
  sink?: { hits: number; perRoute: Record<string, number>; misses: number; bindings: string[] };
}

/** 单次运行结果 */
export interface ClineRunResult {
  /** 是否成功 */
  ok: boolean;
  /** 进程退出码 */
  exitCode: number;
  /** 全部 say/ask 消息 */
  messages: ClineMessage[];
  /** 最终结果文本（权威源见 outputState/meta.eventSource；非流式 say 消息降级链） */
  resultText: string;
  /** 会话 ID（可通过 cline history 反查） */
  sessionId?: string;
  /** 错误信息（ok=false 时存在） */
  error?: string;
  /**
   * 失败归属阶段（验收「卡点定位」用）："ensureCore" = SDK 初始化卡死；
   * "api" = core.start()/API 请求超时；其余 ok=false 场景缺省。
   */
  stage?: "ensureCore" | "api";
  /** 结果完整性状态（ok=true 时有意义）：ok / missing（成功但无文本）。 */
  outputState?: ClineOutputState;
  /** 空结果归因（outputState=missing 时）：no_assistant_text / session_empty。 */
  emptyReason?: string;
  /** 结果取数元信息（messageCount / eventSource / resultTextLength / sessionId）。 */
  meta?: ClineRunResultMeta;
  /** 模型停止原因（P4 遥测；如 model_context_window_exceeded，供边界评估/观测）。 */
  stopReason?: string;
  /**
   * FsSink 捕获的完整会话消息数组（磁盘零残留场景下 SDK 写盘被拦截进内存卷→backend，
   * 本字段承载最终落库所需的全量消息，使 C# 归档不再依赖读磁盘 messages.json）。
   * 仅 fsSink 启用且路由命中时存在；否则 undefined → C# 回退磁盘读取/聚合消息。
   */
  archivedMessages?: unknown[];
  /** 归档元信息（messageCount / source），对齐 AgentRunArtifact 约定。 */
  archiveMeta?: { messageCount?: number; source: ArchiveSource };
  /**
   * 引擎回带的真实轮次（进程内按 session 调用计数，供观测/日志参考）。
   * 归档权威轮次 = C# 侧 ur_nodes 已归档最大轮次 + 1（跨 worker 一致），本字段不用于落库键。
   */
  round?: number;
}

/** cline history 的会话条目 */
export interface ClineHistoryEntry {
  id?: string;
  [key: string]: unknown;
}
