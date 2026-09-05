/**
 * WorkflowGraph — 编排入口。
 *
 * 语义：工作流 JSON 文本 → 规范契约 → DAG 分层布局 → SVG/HTML。这是本包对外唯一入口，
 * 把 contract/layout/svg 三段串成一条可直接调用的管线。
 */

import { workflowFromJson } from "./contract.js";
import { layoutWorkflow, WorkflowLayout } from "./layout.js";
import { renderHtml, renderSvg } from "./svg.js";

export interface RenderResult {
  layout: WorkflowLayout;
  svg: string;
  html: string;
}

/** 从工作流 JSON 字符串一次渲染出 {布局, SVG, HTML}。 */
export function renderWorkflow(json: string, options?: { title?: string }): RenderResult {
  const wf = workflowFromJson(json);
  const layout = layoutWorkflow(wf);
  return {
    layout,
    svg: renderSvg(layout, options),
    html: renderHtml(layout, options),
  };
}