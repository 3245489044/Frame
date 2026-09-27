import type { CoreSessionEvent } from "@cline/sdk";
import type { RecognizerFrame } from "./recognizer-engine.js";

/**
 * `CoreSessionEvent` → {@link RecognizerFrame} 投影（K2：事件源的唯一 SDK 接触面）。
 *
 * 纪律：
 * ① 本函数是**纯投影**（不识别、不判定）——识别规则全部在声明面（`Config\recognizers.json`）；
 * ② `payload` 原样携带事件字段（**不在此层裁剪**：白名单投影是识别规则 `payload` 的职责，
 *    避免"两处裁剪口径不一"）；
 * ③ 一事件可展开为 **0..N 帧**（`session_snapshot` 逐消息 × 逐内容块展开，A：对话内容全量）；
 *    非事件形态 / 非 agent 流返回 **0 帧**（不计入识别账）；
 * ④ 无匹配帧由识别层的 `unrecognized` 兜底（D-3 无静默丢）。
 */

/** 提取对象字段（容错：非对象 ⇒ 空对象，不猜）。 */
function asRecord(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

/**
 * 会话终态快照 → **逐消息 × 逐内容块**帧（A：对话内容全量进过程面）。
 *
 * 快照是结果权威源（`payload.snapshot.messages: MessageWithMetadata[]`，块面 = `ContentBlock`）；
 * 过程面只是它的**增量视图**（过程 ⊆ 归档），故：`role` / `messageId` 进载荷、块类型作 `eventType`，
 * 类别分工完全由声明面规则决定（本层不判定）。展开项非法（缺 `content` / 块缺 `type`）**逐条跳过**，
 * 不整批丢（R5：逐条投影 + 逐条跳过）。`Message.content` 允许为裸字符串 ⇒ 视作单个 `text` 块。
 */
function snapshotFrames(payload: unknown): RecognizerFrame[] {
  const messages = asRecord(asRecord(payload)["snapshot"])["messages"];
  if (!Array.isArray(messages)) {
    return [];
  }

  const frames: RecognizerFrame[] = [];
  messages.forEach((message, messageIndex) => {
    const record = asRecord(message);
    const role = record["role"];
    const messageId = record["id"];
    const content = record["content"];
    const blocks: unknown[] =
      typeof content === "string" ? [{ type: "text", text: content }] : Array.isArray(content) ? content : [];

    blocks.forEach((block, blockIndex) => {
      const blockRecord = asRecord(block);
      const blockType = blockRecord["type"];
      if (typeof blockType !== "string") {
        return; // 该块缺 type ⇒ 逐条跳过（不整批丢）
      }

      frames.push({
        source: "session_snapshot",
        eventType: blockType,
        payload: {
          ...blockRecord,
          ...(typeof role === "string" ? { role } : {}),
          ...(typeof messageId === "string" ? { messageId } : {}),
          messageIndex,
          blockIndex,
        },
      });
    });
  });

  return frames;
}

export function toRecognizerFrames(ev: CoreSessionEvent): RecognizerFrame[] {
  switch (ev.type) {
    case "agent_event": {
      const event = asRecord(ev.payload.event);
      return [{ source: "agent_event", eventType: String(event["type"] ?? "?"), payload: event }];
    }

    case "hook": {
      const payload = asRecord(ev.payload);
      return [{ source: "hook", eventType: String(payload["hookEventName"] ?? "?"), payload }];
    }

    case "chunk": {
      // 非 agent 流（stdout/stderr）不承载语义事件（逐字节噪声）⇒ 不计入识别账，避免刷爆未识别桶。
      if (ev.payload.stream !== "agent") {
        return [];
      }
      try {
        const parsed = asRecord(JSON.parse(ev.payload.chunk));
        return [{ source: "chunk", eventType: String(parsed["type"] ?? "?"), payload: parsed }];
      } catch {
        return [{ source: "chunk", eventType: "unparsable", payload: {} }];
      }
    }

    case "status":
      return [{ source: "status", eventType: String(ev.payload.status ?? "?"), payload: asRecord(ev.payload) }];

    case "ended":
      return [{ source: "ended", eventType: String(ev.payload.reason ?? "?"), payload: asRecord(ev.payload) }];

    case "session_snapshot":
      return snapshotFrames(ev.payload);

    case "team_progress":
      return [{ source: "team_progress", eventType: "progress", payload: {} }];

    case "pending_prompts":
      return [{ source: "pending_prompts", eventType: "prompts", payload: {} }];

    case "pending_prompt_submitted":
      return [{ source: "pending_prompt_submitted", eventType: "prompt", payload: {} }];

    default:
      return [];
  }
}
