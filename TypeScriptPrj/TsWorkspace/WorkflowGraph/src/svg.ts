/**
 * WorkflowGraph — SVG 渲染。
 *
 * 把 <see cref="WorkflowLayout"/> 输出为自包含 SVG 字符串。无第三方依赖、无运行时 JS——SVG 可直接
 * 内联进 htmlviewer（WebView2.NavigateToString 整页注入），比运行时内嵌 mermaid.min.js(3.3MB) 轻量得多。
 */

import { WorkflowLayout, LayoutNode } from "./layout.js";

/** 分支边样式：不同 next 分支给不同颜色/线型，便于读懂降级/失败路径。 */
const EDGE_STYLE: Record<string, { stroke: string; dash: string; label: string }> = {
  onSuccess: { stroke: "#2da44e", dash: "", label: "成功" },
  onFail: { stroke: "#cf222e", dash: "", label: "失败" },
  degrade: { stroke: "#bf8700", dash: "6,4", label: "降级" },
  onMax: { stroke: "#8250df", dash: "4,4", label: "重试耗尽" },
  default: { stroke: "#57606a", dash: "", label: "" },
};

function edgeStyle(label: string) {
  return EDGE_STYLE[label] ?? EDGE_STYLE.default;
}

/** HTML 转义（节点 funcName / nodeId 可能含特殊字符）。 */
function escapeXml(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function edgePath(from: LayoutNode, to: LayoutNode): string {
  const x1 = from.x + from.width;
  const y1 = from.y + from.height / 2;
  const x2 = to.x;
  const y2 = to.y + to.height / 2;
  if (y1 === y2) return `M ${x1} ${y1} L ${x2} ${y2}`;
  // 正交折线：水平出、水平入，中间垂直桥接
  const mx = (x1 + x2) / 2;
  return `M ${x1} ${y1} L ${mx} ${y1} L ${mx} ${y2} L ${x2} ${y2}`;
}

function edgeLabelPos(from: LayoutNode, to: LayoutNode) {
  const x1 = from.x + from.width;
  const y1 = from.y + from.height / 2;
  const x2 = to.x;
  const y2 = to.y + to.height / 2;
  return { x: (x1 + x2) / 2 + 4, y: (y1 + y2) / 2 - 4 };
}

/** 渲染整张工作流图为完整 SVG 字符串（含宽高 viewBox、样式块）。 */
export function renderSvg(layout: WorkflowLayout, options?: { title?: string }): string {
  const parts: string[] = [];
  parts.push(
    `<svg xmlns="http://www.w3.org/2000/svg" width="${layout.canvasWidth}" height="${layout.canvasHeight}" ` +
      `viewBox="0 0 ${layout.canvasWidth} ${layout.canvasHeight}" font-family="'Segoe UI','Microsoft YaHei',sans-serif">`,
  );
  parts.push(`<title>${escapeXml(options?.title ?? "工作流结构图")}</title>`);

  // 样式
  parts.push(`
  <style>
    .wfn { fill:#ffffff; stroke:#0969da; stroke-width:1.5; rx:8; ry:8; }
    .wfn.entry { stroke:#cf222e; stroke-width:2; }
    .wfnt { fill:#24292f; font-size:12px; }
    .wfnf { fill:#57606a; font-size:10px; }
    .wfedge { fill:none; marker-end:url(#wfarrow); }
    .wfelt { fill:#57606a; font-size:10px; }
  </style>
  <defs>
    <marker id="wfarrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
      <path d="M 0 0 L 10 5 L 0 10 z" fill="#57606a" />
    </marker>
  </defs>`);

  // 边（先画，节点盖上面）
  for (const e of layout.edges) {
    const from = layout.nodes.find((n) => n.nodeId === e.from);
    const to = layout.nodes.find((n) => n.nodeId === e.to);
    if (!from || !to) continue;
    const style = edgeStyle(e.label);
    const d = edgePath(from, to);
    const lp = edgeLabelPos(from, to);
    if (e.label) {
      parts.push(
        `<path class="wfedge" d="${d}" stroke="${style.stroke}" stroke-dasharray="${style.dash}" stroke-width="1.5"/>`,
        `<text class="wfelt" x="${lp.x}" y="${lp.y}" fill="${style.stroke}">${escapeXml(style.label)}</text>`,
      );
    } else {
      parts.push(`<path class="wfedge" d="${d}" stroke="${style.stroke}" stroke-width="1.5"/>`);
    }
  }

  // 节点
  for (const n of layout.nodes) {
    const cx = n.x + n.width / 2;
    const isEntry = n.nodeId === layout.entry;
    parts.push(
      `<g class="${isEntry ? "wfn entry" : "wfn"}">` +
        `<rect x="${n.x}" y="${n.y}" width="${n.width}" height="${n.height}" rx="8" ry="8" class="wfn" />` +
        (isEntry ? `<rect x="${n.x}" y="${n.y}" width="${n.width}" height="${n.height}" rx="8" ry="8" fill="none" class="wfn entry" />` : "") +
        `<text class="wfnt" x="${cx}" y="${n.y + 18}" text-anchor="middle">${escapeXml(n.nodeId)}</text>` +
        `<text class="wfnf" x="${cx}" y="${n.y + 34}" text-anchor="middle">${escapeXml(n.funcName || "–")}</text>` +
        `</g>`,
    );
  }

  parts.push("</svg>");
  return parts.join("\n");
}

/** 把工作流图包成可直接给 htmlviewer 的完整 HTML（内联 SVG）。 */
export function renderHtml(layout: WorkflowLayout, options?: { title?: string }): string {
  const svg = renderSvg(layout, options);
  return `<!DOCTYPE html><html><head><meta charset="utf-8"></head><body style="margin:0;padding:12px 16px">${svg}</body></html>`;
}