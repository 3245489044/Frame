/**
 * CoreSessionEvent → RecognizerFrame 投影单测（K2 契约）。
 *
 * 断言：投影是**纯结构映射**（不识别），且 `agent_event`（K0 实测主源）、`hook`、`chunk(agent)` 三类
 * 均产出可识别帧；非 agent 流不产生帧（防逐字节噪声刷爆未识别账）。
 */

import { describe, it, expect } from "vitest";
import { toRecognizerFrame } from "../src/recognize/event-projection.js";
import { RecognizerEngine } from "../src/recognize/recognizer-engine.js";
import { loadRecognizerSpec } from "../src/recognize/recognizer-spec-loader.js";
import type { RecognizerSpec } from "../src/recognize/vocabulary.js";
import * as path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SPEC_PATH = path.resolve(
  HERE, "..", "..", "..", "..", "..", "Projects", "Plug", "Agent", "Config", "recognizers.json"
);

function realSpec(): RecognizerSpec {
  const spec = loadRecognizerSpec(SPEC_PATH);
  if (!spec) throw new Error("声明面未加载");
  return spec;
}

/* eslint-disable @typescript-eslint/no-explicit-any */
const ev = (type: string, payload: unknown): any => ({ type, payload });

describe("K2 · 事件投影（纯结构映射）", () => {
  it("agent_event → source/eventType/payload 三面齐备", () => {
    const frame = toRecognizerFrame(
      ev("agent_event", { sessionId: "s", event: { type: "content_start", contentType: "tool", toolName: "read_files" } })
    );
    expect(frame?.source).toBe("agent_event");
    expect(frame?.eventType).toBe("content_start");
    expect(frame?.payload["toolName"]).toBe("read_files");
  });

  it("hook → hookEventName 作为 eventType", () => {
    const frame = toRecognizerFrame(ev("hook", { sessionId: "s", hookEventName: "tool_call", toolName: "t" }));
    expect(frame?.source).toBe("hook");
    expect(frame?.eventType).toBe("tool_call");
  });

  it("chunk(agent) → 解析 NDJSON，type 作为 eventType", () => {
    const frame = toRecognizerFrame(
      ev("chunk", { sessionId: "s", stream: "agent", chunk: '{"type":"say","say":"text","text":"hi"}', ts: 1 })
    );
    expect(frame?.source).toBe("chunk");
    expect(frame?.eventType).toBe("say");
    expect(frame?.payload["say"]).toBe("text");
  });

  it("chunk(agent) 非 JSON → 归 unparsable（不抛）", () => {
    const frame = toRecognizerFrame(ev("chunk", { sessionId: "s", stream: "agent", chunk: "not-json", ts: 1 }));
    expect(frame?.eventType).toBe("unparsable");
  });

  it("非 agent 流（stdout/stderr）不产生帧（防噪声刷账）", () => {
    expect(toRecognizerFrame(ev("chunk", { sessionId: "s", stream: "stdout", chunk: "x", ts: 1 }))).toBeUndefined();
  });
});

describe("K2 · 投影 + 识别端到端（D-3 账闭合）", () => {
  it("全类型事件序列 → 零未识别（generic 兜底吸收生命周期帧）", () => {
    const engine = new RecognizerEngine(realSpec());
    const events = [
      ev("status", { sessionId: "s", status: "running" }),
      ev("agent_event", { sessionId: "s", event: { type: "iteration_start", iteration: 1 } }),
      ev("agent_event", { sessionId: "s", event: { type: "content_start", contentType: "text" } }),
      ev("agent_event", { sessionId: "s", event: { type: "content_end", contentType: "text", text: "ok" } }),
      ev("agent_event", { sessionId: "s", event: { type: "done", reason: "completed", iterations: 1, text: "ok" } }),
      ev("chunk", { sessionId: "s", stream: "stdout", chunk: "noise", ts: 1 }),
      ev("chunk", { sessionId: "s", stream: "agent", chunk: '{"type":"say","say":"text","text":"ok"}', ts: 2 }),
      ev("ended", { sessionId: "s", reason: "completed", ts: 3 }),
      ev("session_snapshot", { sessionId: "s", snapshot: { sessionId: "s" } }),
    ];

    const produced = events.flatMap((e) => {
      const frame = toRecognizerFrame(e);
      return frame ? engine.apply(frame) : [];
    });

    const stats = engine.stats();
    expect(stats.total).toBe(8); // stdout chunk 不计数（不产生帧）
    expect(stats.unrecognized).toBe(0);
    expect(stats.emitted + stats.countedOnly + stats.unrecognized).toBe(stats.total);
    expect(stats.byKind["llm-iteration"]).toBe(1);
    expect(stats.byKind["done"]).toBe(1);
    expect(stats.byKind["say-text"]).toBe(1); // 更具体的 say 规则先命中
    expect(stats.byKind["generic"]).toBe(3); // status / ended / session_snapshot
    expect(produced.filter((n) => n.node).length).toBe(stats.emitted);
  });
});
