import { describe, expect, it } from "vitest";
import { workflowFromJson, normalizeNode } from "../src/contract.js";
import { layoutWorkflow } from "../src/layout.js";
import { renderSvg } from "../src/svg.js";
import { renderWorkflow } from "../src/index.js";

const imageGenerateJson = `{
  "id": "image-generate",
  "name": "确定性出图链",
  "entryNodeId": "guard_comfy",
  "nodes": [
    { "nodeId": "guard_comfy", "funcName": "produce.guard.comfy",
      "next": { "onSuccess": "build_payload", "onUnavailable": "degrade" } },
    { "nodeId": "build_payload", "funcName": "produce.image.build_payload",
      "parameterKeys": ["prompt", "negativePrompt"], "next": { "onSuccess": "queue_prompt" } },
    { "nodeId": "queue_prompt", "funcName": "produce.image.queue_prompt",
      "next": { "onSuccess": "poll_status", "onFail": "degrade", "onUnavailable": "degrade" } },
    { "nodeId": "poll_status", "funcName": "produce.image.poll_status",
      "next": { "onSuccess": "fetch_output", "onFail": "output_failed" } },
    { "nodeId": "fetch_output", "funcName": "produce.image.fetch_output",
      "resultKeys": ["stored"], "next": { "onSuccess": "output" } },
    { "nodeId": "degrade", "funcName": "produce.flow.output", "next": {} },
    { "nodeId": "output", "funcName": "produce.flow.output", "next": {} },
    { "nodeId": "output_failed", "funcName": "produce.flow.output", "next": {} }
  ]
}`;

describe("contract", () => {
  it("normalizeNode 收敛 next 三分支", () => {
    const n = normalizeNode({
      nodeId: "a",
      next: { onSuccess: "b", onUnavailable: "c" },
    });
    expect(n.next?.onSuccess).toBe("b");
    expect(n.next?.onUnavailable).toBe("c");
    expect(n.next?.onFail).toBeNull();
  });

  it("workflowFromJson 解析节点与入口", () => {
    const wf = workflowFromJson(imageGenerateJson);
    expect(wf.nodes).toHaveLength(8);
    expect(wf.entryNodeId).toBe("guard_comfy");
  });
});

describe("layout", () => {
  it("分层单调递增：每节点层级 = 最长入边链长", () => {
    const wf = workflowFromJson(imageGenerateJson);
    const layout = layoutWorkflow(wf);
    expect(layout.entry).toBe("guard_comfy");
    const byId = new Map(layout.nodes.map((n) => [n.nodeId, n]));
    expect(byId.get("guard_comfy")!.layer).toBe(0);
    expect(byId.get("build_payload")!.layer).toBe(1);
    expect(byId.get("poll_status")!.layer).toBe(3);
    // 终点节点在最后层
    expect(byId.get("output")!.layer).toBe(5);
  });

  it("降级分支 produce.flow.output 走不同层", () => {
    const wf = workflowFromJson(imageGenerateJson);
    const layout = layoutWorkflow(wf);
    expect(layout.edges.some((e) => e.label === "degrade")).toBe(true);
  });
});

describe("svg", () => {
  it("产出合法 SVG 且含节点与边", () => {
    const wf = workflowFromJson(imageGenerateJson);
    const layout = layoutWorkflow(wf);
    const svg = renderSvg(layout, { title: "测试" });
    expect(svg.startsWith("<svg")).toBe(true);
    expect(svg).toContain("guard_comfy");
    expect(svg).toContain("成功");
    expect(svg).toContain("降级");
  });
});

describe("renderWorkflow 管线", () => {
  it("一次渲染出 layout/svg/html", () => {
    const r = renderWorkflow(imageGenerateJson);
    expect(r.layout.nodes.length).toBeGreaterThan(0);
    expect(r.svg.startsWith("<svg")).toBe(true);
    expect(r.html.startsWith("<!DOCTYPE html>")).toBe(true);
  });
});