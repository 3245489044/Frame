/**
 * CoreSessionEvent → RecognizerFrame 投影单测（K2 / K3-A 契约）。
 *
 * 断言：投影是**纯结构映射**（不识别），且 `agent_event`（K0 实测主源）、`hook`、`chunk(agent)` 三类
 * 均产出可识别帧；非 agent 流不产生帧（防逐字节噪声刷爆未识别账）；
 * `session_snapshot` 逐消息 × 逐内容块展开（A：对话内容全量），非法项**逐条跳过**。
 */

import { describe, it, expect } from "vitest";
import { toRecognizerFrames } from "../src/recognize/event-projection.js";
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
    const frames = toRecognizerFrames(
      ev("agent_event", { sessionId: "s", event: { type: "content_start", contentType: "tool", toolName: "read_files" } })
    );
    expect(frames).toHaveLength(1);
    expect(frames[0].source).toBe("agent_event");
    expect(frames[0].eventType).toBe("content_start");
    expect(frames[0].payload["toolName"]).toBe("read_files");
  });

  it("hook → hookEventName 作为 eventType", () => {
    const frames = toRecognizerFrames(ev("hook", { sessionId: "s", hookEventName: "tool_call", toolName: "t" }));
    expect(frames).toHaveLength(1);
    expect(frames[0].source).toBe("hook");
    expect(frames[0].eventType).toBe("tool_call");
  });

  it("chunk(agent) → 解析 NDJSON，type 作为 eventType", () => {
    const frames = toRecognizerFrames(
      ev("chunk", { sessionId: "s", stream: "agent", chunk: '{"type":"say","say":"text","text":"hi"}', ts: 1 })
    );
    expect(frames).toHaveLength(1);
    expect(frames[0].source).toBe("chunk");
    expect(frames[0].eventType).toBe("say");
    expect(frames[0].payload["say"]).toBe("text");
  });

  it("chunk(agent) 非 JSON → 归 unparsable（不抛）", () => {
    const frames = toRecognizerFrames(ev("chunk", { sessionId: "s", stream: "agent", chunk: "not-json", ts: 1 }));
    expect(frames).toHaveLength(1);
    expect(frames[0].eventType).toBe("unparsable");
  });

  it("非 agent 流（stdout/stderr）不产生帧（防噪声刷账）", () => {
    expect(
      toRecognizerFrames(ev("chunk", { sessionId: "s", stream: "stdout", chunk: "x", ts: 1 }))
    ).toHaveLength(0);
  });
});

describe("K3-A · 快照逐消息展开（对话内容全量）", () => {
  it("user + assistant（thinking+text）⇒ 逐块帧，role/messageId/index 进载荷", () => {
    const frames = toRecognizerFrames(
      ev("session_snapshot", {
        snapshot: {
          sessionId: "s",
          messages: [
            { id: "msg_u", role: "user", content: [{ type: "text", text: "hi" }] },
            {
              id: "msg_a",
              role: "assistant",
              content: [{ type: "thinking", thinking: "t" }, { type: "text", text: "ok" }],
            },
          ],
        },
      })
    );

    expect(frames.map((f) => f.eventType)).toEqual(["text", "thinking", "text"]);
    expect(frames.map((f) => f.source)).toEqual(["session_snapshot", "session_snapshot", "session_snapshot"]);
    expect(frames.map((f) => f.payload["role"])).toEqual(["user", "assistant", "assistant"]);
    expect(frames[2].payload["messageId"]).toBe("msg_a");
    expect(frames[2].payload["messageIndex"]).toBe(1);
    expect(frames[2].payload["blockIndex"]).toBe(1);
    expect(frames[2].payload["text"]).toBe("ok");
  });

  it("裸字符串 content 视作单个 text 块（SDK 允许 Message.content 为 string）", () => {
    const frames = toRecognizerFrames(
      ev("session_snapshot", { snapshot: { sessionId: "s", messages: [{ role: "user", content: "plain" }] } })
    );
    expect(frames).toHaveLength(1);
    expect(frames[0].eventType).toBe("text");
    expect(frames[0].payload["text"]).toBe("plain");
  });

  it("非法项逐条跳过（缺 content / 块缺 type），不整批丢", () => {
    const frames = toRecognizerFrames(
      ev("session_snapshot", {
        snapshot: {
          sessionId: "s",
          messages: [
            { role: "user", content: [{ text: "no-type" }] },
            { role: "assistant", content: [{ type: "text", text: "kept" }] },
            { role: "user" },
          ],
        },
      })
    );
    expect(frames).toHaveLength(1);
    expect(frames[0].payload["text"]).toBe("kept");
  });

  it("无 messages ⇒ 0 帧（不抛，不伪造）", () => {
    expect(
      toRecognizerFrames(ev("session_snapshot", { sessionId: "s", snapshot: { sessionId: "s" } }))
    ).toHaveLength(0);
  });
});

describe("K3-A · 快照识别（分工：live 管事件级 / snapshot 管消息级；D-8 不双写）", () => {
  it("user 成节点（origin=Snapshot）；tool_use 仅记账（node:false）", () => {
    const engine = new RecognizerEngine(realSpec());
    const produced = toRecognizerFrames(
      ev("session_snapshot", {
        snapshot: {
          messages: [
            { id: "u", role: "user", content: [{ type: "text", text: "hi" }] },
            { id: "a", role: "assistant", content: [{ type: "tool_use", id: "c1", name: "read_files", input: {} }] },
          ],
        },
      })
    ).flatMap((frame) => engine.apply(frame));

    const nodes = produced.filter((n) => n.node);
    expect(nodes.map((n) => n.kind)).toEqual(["user-message"]);
    expect(nodes[0].origin).toBe("Snapshot");
    expect(nodes[0].payload["role"]).toBe("user");

    const counted = produced.filter((n) => !n.node);
    expect(counted.map((n) => n.kind)).toEqual(["tool-call"]);
  });

  it("image 块 ⇒ attachment 成节点（live 路径从不产出）", () => {
    const engine = new RecognizerEngine(realSpec());
    const produced = toRecognizerFrames(
      ev("session_snapshot", {
        snapshot: {
          messages: [{ role: "user", content: [{ type: "image", data: "b64", mediaType: "image/png" }] }],
        },
      })
    ).flatMap((frame) => engine.apply(frame));

    const nodes = produced.filter((n) => n.node);
    expect(nodes.map((n) => n.kind)).toEqual(["attachment"]);
    expect(nodes[0].origin).toBe("Snapshot");
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
      ev("session_snapshot", {
        sessionId: "s",
        snapshot: {
          sessionId: "s",
          messages: [{ id: "a", role: "assistant", content: [{ type: "text", text: "ok" }] }],
        },
      }),
    ];

    const produced = events.flatMap((e) => toRecognizerFrames(e).flatMap((frame) => engine.apply(frame)));

    const stats = engine.stats();
    expect(stats.total).toBe(8); // stdout chunk 不计数（不产生帧）
    expect(stats.unrecognized).toBe(0);
    expect(stats.emitted + stats.countedOnly + stats.unrecognized).toBe(stats.total);
    expect(stats.byKind["llm-iteration"]).toBe(1);
    expect(stats.byKind["done"]).toBe(1);
    expect(stats.byKind["say-text"]).toBe(1); // 更具体的 say 规则先命中
    expect(stats.byKind["assistant-message"]).toBe(1); // 快照逐消息成节点（A）
    expect(stats.byKind["generic"]).toBe(2); // status / ended（快照不再落 generic）
    expect(produced.filter((n) => n.node).length).toBe(stats.emitted);
  });
});
