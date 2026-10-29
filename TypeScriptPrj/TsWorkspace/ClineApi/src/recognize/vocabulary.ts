/**
 * 识别词表（受控 kind 集）与跨语言版本守卫 —— 结构契约层。
 *
 * 单一事实源 = `Config\recognizers.json`（决策 A：JSON 过渡；上移语言面归 K4c）。
 * 本模块只声明**结构契约 + 版本区间 + 容错归一**，不持有 kind 字面量清单
 * （清单由声明面加载 ⇒ 新增一类 = 只改声明，零代码 = D-1）。
 */

/**
 * 事实来源（复用节点框架 Origin 词表，不新增概念）：
 * `Live` = 事件流（逐事件、时序敏感）；`Snapshot` = 终态快照逐消息；`Derived` = 对账/补算。
 */
export type RecognizerOrigin = "Live" | "Snapshot" | "Derived";

/** 识别规则（表驱动；内容 = 数据，非逻辑）。 */
export interface RecognizerRule {
  /** 结构谓词合取（字段=值 / 字段∈集合 / 字段存在性）；禁语义判据。 */
  readonly match: Readonly<Record<string, unknown>>;
  /** 受控类别（须 ∈ RecognizerSpec.kinds）。 */
  readonly kind: string;
  /** `false` = 识别但不成节点（仅计入账；如流式片段）。缺省 `true`。 */
  readonly node?: boolean;
  /** 事实来源（缺省 `Live`）；使「同一 kind 可由不同来源产出」成为数据而非代码分支。 */
  readonly origin?: RecognizerOrigin;
  /** 载荷白名单投影（outKey → 帧字段名）；禁整块透传。 */
  readonly payload?: Readonly<Record<string, string>>;
  /** 键策略（K3 消费；本层只承载）。 */
  readonly key?: Readonly<Record<string, string>>;
  /** 背压（数据化，K3 消费）。 */
  readonly backpressure?: Readonly<Record<string, unknown>>;
  /**
   * 配对边声明（P1 只登记不消费；P3 canonicalize 消费）：
   * `pairWith` = 目标 kind，`by` = 配对字段（如 `callId`），`type` = CSG 边类型。
   */
  readonly edge?: RecognizerEdge;
  /**
   * 终结判定数据化（P1 只登记不消费；P3 RoundShape 消费）：
   * `TerminalWhenLast` = 该类最后一条为终答；`Terminal` = 恒为终答；`None` = 非终答。
   */
  readonly mark?: RecognizerMark;
}

/** 配对边声明（因果链从"隐式 callId"升级为"显式边"）。 */
export interface RecognizerEdge {
  readonly pairWith: string;
  readonly by: string;
  readonly type: string;
}

/** 终结标记（声明驱动，替代"代码猜最后一条"）。 */
export type RecognizerMark = "Terminal" | "TerminalWhenLast" | "None";

/** 未匹配帧的末路（P5 末路完备；禁隐式丢弃）。 */
export interface RecognizerDefault {
  readonly kind: string;
  /** `true` = 产出可见节点（`unrecognized` 必须可见）。 */
  readonly visible?: boolean;
}

/** 识别声明文件结构（`Config\agents\<agent>\recognizers.json`）。 */
export interface RecognizerSpec {
  /** agent 身份（目录名同源；缺省 = 目录推导，见 loader）。 */
  readonly agent?: string;
  /** 词表标识 + 契约版本，形如 `cline-signal@1`。 */
  readonly vocabulary: string;
  /** 受控 kind 全集（含兜底类）。 */
  readonly kinds: readonly string[];
  /** 表驱动识别规则。 */
  readonly recognizers: readonly RecognizerRule[];
  /** 末路（未匹配帧的归宿；缺省视为 `unrecognized`）。 */
  readonly default?: RecognizerDefault;
}

/** 无匹配帧的归宿类别（必须有；D-3 的兜底面）。 */
export const UNRECOGNIZED_KIND = "unrecognized";

/** 契约版本区间（非 SDK 版本）。
 *  1..2：@1 = 旧数据（单文件时代，读兼容）；@2 = agent 维度（`Config/agents/<agent>/`）。
 *  @3 及以上装载即抛（版本窗外禁静默降级）。 */
const MIN_VERSION = 1;
const MAX_VERSION = 2;

/** 词表/声明错误（缺失即抛、非法即抛；禁回退默认值 —— W-4）。 */
export class RecognizerVocabularyError extends Error {}

/** 解析 `cline-signal@1` → `1`；形态非法即抛。 */
export function parseVocabularyVersion(vocabulary: string): number {
  const matched = /^[a-z0-9-]+@(\d+)$/i.exec(vocabulary ?? "");
  if (!matched) {
    throw new RecognizerVocabularyError(`词表标识非法: "${vocabulary}"（期望 <name>@<version>）`);
  }
  return Number(matched[1]);
}

/** 版本区间校验（照 `CsgProtocol.EnsureCompatible` 范式）。 */
export function ensureCompatible(vocabulary: string): void {
  const version = parseVocabularyVersion(vocabulary);
  if (version < MIN_VERSION || version > MAX_VERSION) {
    throw new RecognizerVocabularyError(
      `词表版本不兼容: ${vocabulary}（本侧支持 ${MIN_VERSION}..${MAX_VERSION}）`
    );
  }
}

/** 非法/缺失 kind 容错归一（对齐 `parseCanonicalNodeType` 的 fallback 范式；D-5：不崩）。 */
export function normalizeKind(kind: unknown, allowed: ReadonlySet<string>): string {
  return typeof kind === "string" && allowed.has(kind) ? kind : UNRECOGNIZED_KIND;
}
