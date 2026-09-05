/**
 * cline-extractor — Cline 会话产物的契约化提取器（替代 DbMirror 里硬编码的解析）。
 *
 * - ClineMessagesExtractor：`.messages.json` → `{ messages: [...] }`（也兼容裸数组）→ 消息数组。
 * - ClineManifestExtractor：`.manifest.json` → 会话元数据。
 * compaction.json 不镜像 DB，无需提取器（路由 mirror:false）。
 */

import type { SnapshotEntry } from "../core/sink.js";
import type { ArtifactExtractor } from "../extract/artifact-extractor.js";

function isMessagesPath(p: string): boolean {
  return /\.messages\.json$/i.test(p);
}

function isManifestPath(p: string): boolean {
  return /\.manifest\.json$/i.test(p);
}

/** 解析快照数据为 JSON；失败返回 undefined（未写完 / 非法）。 */
function parseJson(snapshot: SnapshotEntry): unknown | undefined {
  try {
    return JSON.parse(snapshot.data.toString("utf8"));
  } catch {
    return undefined;
  }
}

/** messages.json 提取器：`{messages:[...]}` 或裸数组 → 消息数组。 */
export class ClineMessagesExtractor implements ArtifactExtractor {
  readonly kind = "messages" as const;

  matches(path: string): boolean {
    return isMessagesPath(path);
  }

  parse(snapshot: SnapshotEntry): unknown | undefined {
    const parsed = parseJson(snapshot);
    if (parsed === undefined) return undefined;
    const msgs = Array.isArray(parsed)
      ? parsed
      : (parsed as { messages?: unknown } | null)?.messages;
    return Array.isArray(msgs) ? msgs : undefined;
  }
}

/** manifest.json 提取器：会话元数据。 */
export class ClineManifestExtractor implements ArtifactExtractor {
  readonly kind = "session" as const;

  matches(path: string): boolean {
    return isManifestPath(path);
  }

  parse(snapshot: SnapshotEntry): unknown | undefined {
    return parseJson(snapshot);
  }
}

/** Cline 的全部提取器（注册进 AgentSinkHost / FsSink 的提取器注册表）。 */
export const clineExtractors: ArtifactExtractor[] = [
  new ClineMessagesExtractor(),
  new ClineManifestExtractor(),
];
