/**
 * 离线重放（canon-replay）—— 同一份 spec + 同一归档轮次 ⇒ 同一节点集（A-3 / P2-1）。
 *
 * 纪律：
 * ① 复用同一 `RecognizerEngine`（DR-1：禁第二实现）；
 * ② **每轮新建实例**（`ordinal` 是实例内计数，重用实例会让 A/B 永不相等 —— X-6）；
 * ③ 复用同一投影函数 {@link framesFromArchivedMessages}（X-5：禁第二份投影）；
 * ④ 失败可见化（缺 spec / 非法 JSON ⇒ 抛；调用方转 `Degraded`，禁静默空集）。
 */

import * as fs from "fs";
import { createHash } from "crypto";
import { RecognizerEngine, type RecognizedNode } from "./recognizer-engine.js";
import { framesFromArchivedMessages } from "./event-projection.js";
import { loadRecognizerSpec, resolveRecognizerSpecPath } from "./recognizer-spec-loader.js";

/** 单条识别产物（对拍键 = kind + ordinal + payloadHash）。 */
export interface CanonReplayHit {
  readonly kind: string;
  readonly ordinal: number;
  readonly node: boolean;
  readonly origin: string;
  readonly payload: Readonly<Record<string, unknown>>;
  readonly payloadHash: string;
  readonly source: { readonly sdk: string; readonly eventType: string };
}

/** 单轮重放产物。 */
export interface CanonReplayRecord {
  readonly agent: string;
  readonly vocabulary: string;
  readonly specPath: string;
  readonly roundFile: string;
  readonly hits: readonly CanonReplayHit[];
  readonly stats: {
    readonly total: number;
    readonly emitted: number;
    readonly countedOnly: number;
    readonly unrecognized: number;
  };
  /** 完整性账（`emitted + countedOnly + unrecognized === total`）。 */
  readonly balanced: boolean;
}

/** 稳定 JSON（键升序）：payloadHash 的唯一算法（跨运行逐字节可比）。 */
function stableJson(value: unknown): string {
  if (value === null || typeof value !== "object") return JSON.stringify(value) ?? "null";
  if (Array.isArray(value)) return `[${value.map(stableJson).join(",")}]`;
  const record = value as Record<string, unknown>;
  const keys = Object.keys(record).sort();
  return `{${keys.map((k) => `${JSON.stringify(k)}:${stableJson(record[k])}`).join(",")}}`;
}

/** 载荷指纹（SHA256 前 16 位；对拍判据载体 = 内容，非 mtime）。 */
function payloadHashOf(payload: Readonly<Record<string, unknown>>): string {
  return createHash("sha256").update(stableJson(payload)).digest("hex").slice(0, 16);
}

/** 从归档轮次文件提取消息数组（接受 `{messages}` / `{snapshot:{messages}}` / 裸数组）。 */
export function readArchivedMessages(roundFile: string): unknown[] {
  if (!fs.existsSync(roundFile)) {
    throw new Error(`归档轮次文件缺失: ${roundFile}（缺失即抛，禁重跑 LLM 补节点）`);
  }
  const parsed = JSON.parse(fs.readFileSync(roundFile, "utf-8")) as unknown;
  if (Array.isArray(parsed)) return parsed;
  const record = parsed as Record<string, unknown>;
  if (Array.isArray(record["messages"])) return record["messages"] as unknown[];
  const snapshot = record["snapshot"];
  if (snapshot !== null && typeof snapshot === "object") {
    const messages = (snapshot as Record<string, unknown>)["messages"];
    if (Array.isArray(messages)) return messages as unknown[];
  }
  throw new Error(`归档轮次结构非法: ${roundFile}（须含 messages[] 或 snapshot.messages[]）`);
}

/** 重放单轮（纯函数式：每次新建引擎，输出仅依赖输入）。 */
export function replayRoundFile(
  agent: string,
  roundFile: string,
  specPath?: string
): CanonReplayRecord {
  const resolvedSpec = specPath ?? resolveRecognizerSpecPath(agent);
  if (!resolvedSpec) {
    throw new Error("未解析到识别声明路径（agent / CLINE_RECOGNIZER_SPEC / recognizerSpecFile 皆缺）");
  }

  const spec = loadRecognizerSpec(resolvedSpec, agent);
  if (!spec) {
    throw new Error(`识别声明未装载: ${resolvedSpec}（空 spec ⇒ 重放无意义）`);
  }

  const engine = new RecognizerEngine(spec);
  const frames = framesFromArchivedMessages(readArchivedMessages(roundFile));
  const hits: CanonReplayHit[] = [];
  for (const frame of frames) {
    for (const node of engine.apply(frame) as RecognizedNode[]) {
      hits.push({
        kind: node.kind,
        ordinal: node.ordinal,
        node: node.node,
        origin: node.origin,
        payload: node.payload,
        payloadHash: payloadHashOf(node.payload),
        source: node.source,
      });
    }
  }

  const stats = engine.stats();
  return {
    agent,
    vocabulary: spec.vocabulary,
    specPath: resolvedSpec,
    roundFile,
    hits,
    stats: {
      total: stats.total,
      emitted: stats.emitted,
      countedOnly: stats.countedOnly,
      unrecognized: stats.unrecognized,
    },
    balanced: stats.emitted + stats.countedOnly + stats.unrecognized === stats.total,
  };
}
