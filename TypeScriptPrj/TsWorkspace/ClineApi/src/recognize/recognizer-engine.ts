/**
 * RecognizerEngine —— 识别执行器（唯一实现，TS 引擎侧）。
 *
 * 职责：`事件 → 声明 → 基础节点` 的**匹配 + 白名单投影 + 分桶计数**；
 * 事件源（`agent_event` / `hook` / `chunk`）与 SDK 的接触面留在调用方（`sdk-transport`），
 * 本模块只吃标准化后的 {@link RecognizerFrame}，从而可离线单测（D-1 / D-3 / D-5）。
 *
 * 纪律：
 * ① `match` 只用**结构谓词**（字段=值 / ∈集合 / 存在性）—— 判据载体必须是结构事实；
 * ② `payload` 是**字段白名单投影**（禁整块透传，防载荷膨胀与误灌 LLM 上下文）；
 * ③ **无静默丢**：无匹配 → `unrecognized` + 分桶计数（D-3 的兜底面）；
 * ④ 声明非法（kind ∉ 词表）**装配期即抛**，不落到运行期语义兜底（W-3）。
 */

import {
  normalizeKind,
  UNRECOGNIZED_KIND,
  type RecognizerOrigin,
  type RecognizerRule,
  type RecognizerSpec,
} from "./vocabulary.js";

/** 待识别帧（引擎侧最小形态；由 `CoreSessionEvent` 投影而来）。 */
export interface RecognizerFrame {
  /** 事件源：`agent_event` / `hook` / `chunk` … */
  readonly source: string;
  /** 事件类型（`agent_event.event.type` / `hook.hookEventName` / `chunk` 内 `type`）。 */
  readonly eventType: string;
  /** 字段面（含事件内字段；`type` 由 `eventType` 提供，不重复登记）。 */
  readonly payload: Readonly<Record<string, unknown>>;
}

/** 识别产物基元（身份 = 识别结果；落库/投影由节点框架承担）。 */
export interface RecognizedNode {
  /** 受控类别（词表枚举值）。 */
  readonly kind: string;
  /** 白名单投影后的载荷字段。 */
  readonly payload: Readonly<Record<string, unknown>>;
  /** 该类内到达序（1-based）。 */
  readonly ordinal: number;
  /** `false` = 识别但不成节点（仅计入账）；调用方据此过滤。 */
  readonly node: boolean;
  /** 事实来源（复用节点框架 Origin 词表）；按规则 `origin` 取值，缺省 `Live`。 */
  readonly origin: RecognizerOrigin;
  /** 溯源（对账/审计）。 */
  readonly source: { readonly sdk: string; readonly eventType: string };
}

/** 识别账（D-3：`emitted + countedOnly + unrecognized = total`）。 */
export interface RecognizerStats {
  /** 到达帧总数。 */
  readonly total: number;
  /** 成节点数。 */
  readonly emitted: number;
  /** 识别但不成节点数（流式片段等）。 */
  readonly countedOnly: number;
  /** 未识别帧数。 */
  readonly unrecognized: number;
  /** 各类别计数（含 `unrecognized`）。 */
  readonly byKind: Readonly<Record<string, number>>;
  /** 未识别帧按「source/eventType」分桶（可见化用，禁静默丢）。 */
  readonly unrecognizedBuckets: Readonly<Record<string, number>>;
}

/** 字段取值：`source` / `type` 为帧级保留键，其余取 `payload` 字段。 */
function fieldOf(frame: RecognizerFrame, key: string): unknown {
  if (key === "source") return frame.source;
  if (key === "type") return frame.eventType;
  return frame.payload[key];
}

/** 单谓词匹配：数组 = ∈集合；`{exists:bool}` = 存在性；标量 = 相等。 */
function matchPredicate(actual: unknown, expected: unknown): boolean {
  if (Array.isArray(expected)) {
    return expected.some((candidate) => candidate === actual);
  }
  if (expected !== null && typeof expected === "object") {
    const structured = expected as { readonly exists?: unknown };
    if (typeof structured.exists === "boolean") {
      return (actual !== undefined) === structured.exists;
    }
    return false; // 未支持的谓词形态 ⇒ 不匹配（禁语义兜底）
  }
  return actual === expected;
}

/** 规则匹配 = 声明面全部谓词的合取。 */
function matchRule(match: Readonly<Record<string, unknown>>, frame: RecognizerFrame): boolean {
  return Object.entries(match).every(([key, expected]) => matchPredicate(fieldOf(frame, key), expected));
}

/** 载荷白名单投影（缺值即省略，不落 null/undefined 占位）。 */
function projectPayload(rule: RecognizerRule, frame: RecognizerFrame): Record<string, unknown> {
  const projected: Record<string, unknown> = {};
  for (const [outKey, sourceKey] of Object.entries(rule.payload ?? {})) {
    const value = fieldOf(frame, sourceKey);
    if (value !== undefined && value !== null) {
      projected[outKey] = value;
    }
  }
  return projected;
}


/** 识别执行器（无事件源依赖 ⇒ 可按 run 实例化）。 */
export class RecognizerEngine {
  private readonly kinds: ReadonlySet<string>;
  private readonly rules: readonly RecognizerRule[];
  private readonly ordinals = new Map<string, number>();
  private readonly byKind: Record<string, number> = {};
  private readonly unrecognizedBuckets: Record<string, number> = {};
  private total = 0;
  private emitted = 0;
  private countedOnly = 0;
  private unrecognized = 0;

  /** 装配期校验（W-3：非法声明装配期即拒，运行期不做语义兜底）。 */
  constructor(spec: RecognizerSpec) {
    this.kinds = new Set(spec.kinds);
    if (!this.kinds.has(UNRECOGNIZED_KIND)) {
      throw new Error(`识别声明非法：词表缺兜底类 "${UNRECOGNIZED_KIND}"`);
    }
    for (const rule of spec.recognizers) {
      if (!this.kinds.has(rule.kind)) {
        throw new Error(`识别声明非法：kind "${rule.kind}" ∉ 词表`);
      }
    }
    this.rules = spec.recognizers;
  }

  /** 识别一帧：返回 0..1 条产物（识别但不成节点的条目亦返回，`node=false`）。 */
  apply(frame: RecognizerFrame): RecognizedNode[] {
    this.total++;

    const rule = this.rules.find((candidate) => matchRule(candidate.match, frame));
    if (!rule) {
      return [this.recordUnrecognized(frame)];
    }

    const kind = normalizeKind(rule.kind, this.kinds);
    const ordinal = (this.ordinals.get(kind) ?? 0) + 1;
    this.ordinals.set(kind, ordinal);
    this.byKind[kind] = (this.byKind[kind] ?? 0) + 1;

    const node = rule.node !== false;
    if (node) {
      this.emitted++;
    } else {
      this.countedOnly++;
    }

    return [
      {
        kind,
        payload: projectPayload(rule, frame),
        ordinal,
        node,
        origin: rule.origin ?? "Live",
        source: { sdk: frame.source, eventType: frame.eventType },
      },
    ];
  }

  /** 未识别帧：计入 `unrecognized` 并按「source/eventType」分桶可见化（禁静默丢）。 */
  private recordUnrecognized(frame: RecognizerFrame): RecognizedNode {
    this.unrecognized++;
    this.byKind[UNRECOGNIZED_KIND] = (this.byKind[UNRECOGNIZED_KIND] ?? 0) + 1;
    const bucket = `${frame.source}/${frame.eventType}`;
    this.unrecognizedBuckets[bucket] = (this.unrecognizedBuckets[bucket] ?? 0) + 1;

    return {
      kind: UNRECOGNIZED_KIND,
      payload: {},
      ordinal: this.byKind[UNRECOGNIZED_KIND],
      node: false,
      origin: "Live",
      source: { sdk: frame.source, eventType: frame.eventType },
    };
  }

  /** 识别账快照（D-3 判据载体）。 */
  stats(): RecognizerStats {
    return {
      total: this.total,
      emitted: this.emitted,
      countedOnly: this.countedOnly,
      unrecognized: this.unrecognized,
      byKind: { ...this.byKind },
      unrecognizedBuckets: { ...this.unrecognizedBuckets },
    };
  }
}
