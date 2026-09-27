/**
 * K0 事件可达性探针单测（只读实证的机器化判据）。
 *
 * 断言 `EventCollector` 对当前被静默丢弃的事件族（`agent_event` / `hook` / `team_progress` / `pending_prompts`…）
 * 分桶计数正确，且已处理事件（status/ended/session_snapshot/chunk）不进入直方图。
 *
 * 价值：K0 的「类型存在 ≠ 运行时必发」只能在真实 run 里裁决；本用例保证**探针本身**不误报、不漏报
 * （否则真实 run 的直方图读数不可信）。
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { EventCollector } from "../src/transport/sdk-transport.js";
import type { CoreSessionEvent } from "@cline/sdk";

type AnyEvent = CoreSessionEvent;

function agentEvent(inner: Record<string, unknown>): AnyEvent {
  return { type: "agent_event", payload: { sessionId: "s1", event: inner } } as unknown as AnyEvent;
}

function hook(hookEventName: string): AnyEvent {
  return { type: "hook", payload: { sessionId: "s1", hookEventName } } as unknown as AnyEvent;
}

describe("EventCollector K0 探针 — 分桶计数", () => {
  let stderrSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    stderrSpy = vi.spyOn(process.stderr, "write").mockImplementation(() => true);
  });

  afterEach(() => {
    stderrSpy.mockRestore();
  });

  it("空会话 → 直方图为空", () => {
    const c = new EventCollector();
    expect(c.getK0Arrivals().size).toBe(0);
  });

  it("agent_event 按 type + contentType 分桶（tool 三态各成桶）", () => {
    const c = new EventCollector();
    c.onEvent(agentEvent({ type: "content_start", contentType: "tool", toolName: "read_files" }));
    c.onEvent(agentEvent({ type: "content_end", contentType: "tool", toolName: "read_files" }));
    c.onEvent(agentEvent({ type: "content_end", contentType: "text", text: "hi" }));

    expect(Object.fromEntries(c.getK0Arrivals())).toEqual({
      "agent_event:content_start:tool": 1,
      "agent_event:content_end:tool": 1,
      "agent_event:content_end:text": 1,
    });
  });

  it("同桶重复到达累加；content_update（高频）不刷屏（每桶仅首条 stderr）", () => {
    const c = new EventCollector();
    for (let i = 0; i < 5; i++) {
      c.onEvent(agentEvent({ type: "content_update", contentType: "tool", toolCallId: "t1" }));
    }

    expect(c.getK0Arrivals().get("agent_event:content_update:tool")).toBe(5);
    const arrivalLines = stderrSpy.mock.calls.filter((args) =>
      String(args[0]).startsWith("[cline-k0] arrival agent_event:content_update:tool")
    );
    expect(arrivalLines).toHaveLength(1);
  });

  it("hook 按 hookEventName 分桶", () => {
    const c = new EventCollector();
    c.onEvent(hook("tool_call"));
    c.onEvent(hook("tool_result"));
    c.onEvent(hook("tool_call"));

    expect(Object.fromEntries(c.getK0Arrivals())).toEqual({
      "hook:tool_call": 2,
      "hook:tool_result": 1,
    });
  });

  it("形态缺失降级为 '?'（不猜、不崩）", () => {
    const c = new EventCollector();
    c.onEvent({ type: "agent_event", payload: { sessionId: "s1", event: {} } } as unknown as AnyEvent);
    c.onEvent({ type: "hook", payload: { sessionId: "s1" } } as unknown as AnyEvent);

    expect(Object.fromEntries(c.getK0Arrivals())).toEqual({
      "agent_event:?": 1,
      "hook:?": 1,
    });
  });

  it("已处理事件（status/ended/session_snapshot/chunk）不进直方图", () => {
    const c = new EventCollector();
    c.onEvent({ type: "status", payload: { sessionId: "s1", status: "running" } } as unknown as AnyEvent);
    c.onEvent({ type: "status", payload: { sessionId: "s1", status: "completed" } } as unknown as AnyEvent);
    c.onEvent({ type: "ended", payload: { sessionId: "s1", reason: "completed", ts: 0 } } as unknown as AnyEvent);
    c.onEvent({ type: "session_snapshot", payload: { sessionId: "s1", snapshot: { sessionId: "s1" } } } as unknown as AnyEvent);
    c.onEvent({ type: "chunk", payload: { sessionId: "s1", stream: "agent", chunk: '{"type":"say","text":"x"}', ts: 0 } } as unknown as AnyEvent);

    expect(c.getK0Arrivals().size).toBe(0);
    // 既有语义未被探针扰动
    expect(c.status).toBe("completed");
    expect(c.messages).toHaveLength(1);
  });

  it("未识别事件族（team_progress/pending_prompts）按原始 type 分桶", () => {
    const c = new EventCollector();
    c.onEvent({ type: "team_progress", payload: {} } as unknown as AnyEvent);
    c.onEvent({ type: "pending_prompts", payload: {} } as unknown as AnyEvent);
    c.onEvent({ type: "pending_prompt_submitted", payload: {} } as unknown as AnyEvent);

    expect(Object.fromEntries(c.getK0Arrivals())).toEqual({
      team_progress: 1,
      pending_prompts: 1,
      pending_prompt_submitted: 1,
    });
  });
});
