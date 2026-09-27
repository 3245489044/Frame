import type { CoreSessionEvent } from "@cline/sdk";
import type { RecognizerFrame } from "./recognizer-engine.js";

/**
 * `CoreSessionEvent` → {@link RecognizerFrame} 投影（K2：事件源的唯一 SDK 接触面）。
 *
 * 纪律：
 * ① 本函数是**纯投影**（不识别、不判定）——识别规则全部在声明面（`Config\recognizers.json`）；
 * ② `payload` 原样携带事件字段（**不在此层裁剪**：白名单投影是识别规则 `payload` 的职责，
 *    避免"两处裁剪口径不一"）；
 * ③ 无匹配帧由识别层的 `unrecognized` 兜底（D-3 无静默丢），故此处对未知形态返回帧而非 `undefined`
 *    （仅对**非事件**形态返回 `undefined`）。
 */

/** 提取对象字段（容错：非对象 ⇒ 空对象，不猜）。 */
function asRecord(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

export function toRecognizerFrame(ev: CoreSessionEvent): RecognizerFrame | undefined {
  switch (ev.type) {
    case "agent_event": {
      const event = asRecord(ev.payload.event);
      return { source: "agent_event", eventType: String(event["type"] ?? "?"), payload: event };
    }

    case "hook": {
      const payload = asRecord(ev.payload);
      return { source: "hook", eventType: String(payload["hookEventName"] ?? "?"), payload };
    }

    case "chunk": {
      // 非 agent 流（stdout/stderr）不承载语义事件（逐字节噪声）⇒ 不计入识别账，避免刷爆未识别桶。
      if (ev.payload.stream !== "agent") {
        return undefined;
      }
      try {
        const parsed = asRecord(JSON.parse(ev.payload.chunk));
        return { source: "chunk", eventType: String(parsed["type"] ?? "?"), payload: parsed };
      } catch {
        return { source: "chunk", eventType: "unparsable", payload: {} };
      }
    }

    case "status":
      return { source: "status", eventType: String(ev.payload.status ?? "?"), payload: asRecord(ev.payload) };

    case "ended":
      return { source: "ended", eventType: String(ev.payload.reason ?? "?"), payload: asRecord(ev.payload) };

    case "session_snapshot":
      return { source: "session_snapshot", eventType: "snapshot", payload: {} };

    case "team_progress":
      return { source: "team_progress", eventType: "progress", payload: {} };

    case "pending_prompts":
      return { source: "pending_prompts", eventType: "prompts", payload: {} };

    case "pending_prompt_submitted":
      return { source: "pending_prompt_submitted", eventType: "prompt", payload: {} };

    default:
      return undefined;
  }
}
