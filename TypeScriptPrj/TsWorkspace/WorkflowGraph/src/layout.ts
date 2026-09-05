/**
 * WorkflowGraph — DAG 分层布局。
 *
 * 对窄工作流（<20 节点）做 Suygiyama 简化版：先入度过零 → 拓扑分层（层级 = 最长入边链长），
 * 再在同层内排 x 坐标，产出每个节点的 {x, y, layer} 与像素坐标（供 SVG 渲染）。
 */

import { Workflow, WorkflowNode } from "./contract.js";

/** 布局后节点——含网格层与像素坐标。 */
export interface LayoutNode {
  nodeId: string;
  funcName: string;
  layer: number;
  index: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

/** 布局后边——把 next 分支收敛为边的源/目标引用。 */
export interface LayoutEdge {
  from: string;
  to: string;
  label: string;
}

export interface WorkflowLayout {
  nodes: LayoutNode[];
  edges: LayoutEdge[];
  canvasWidth: number;
  canvasHeight: number;
  /** 入口节点（entryNodeId 或入度为 0 的首个节点）。 */
  entry: string | null;
}

const NODE_W = 180;
const NODE_H = 44;
const GAP_X = 40;
const GAP_Y = 30;
const PAD_X = 20;
const PAD_Y = 20;

/** 先作一次入边解析，收集 (from, to, label)。 */
function collectEdges(wf: Workflow): { from: string; to: string; label: string }[] {
  const edges: { from: string; to: string; label: string }[] = [];
  const ids = new Set(wf.nodes.map((n) => n.nodeId));
  for (const node of wf.nodes) {
    const next = node.next ?? {};
    const push = (to: string | null | undefined, label: string) => {
      if (to && ids.has(to)) edges.push({ from: node.nodeId, to, label });
    };
    push(next.onSuccess, "onSuccess");
    push(next.onFail, "onFail");
    push(next.onUnavailable, "degrade");
    push(next.onMax, "onMax");
  }
  return edges;
}

/**
 * 计算分层：层级 = 从入口/任意入度为 0 节点到该节点的最长下路径长度（最长路径分层最稳）。
 * 用递推：layer[n] = 0, 若无入边；否则 max(layer[p]+1)。因是 DAG，可任取一个无环的固定点迭代。
 */
function computeLayers(wf: Workflow, nodes: WorkflowNode[], edges: { from: string; to: string; label: string }[]): Map<string, number> {
  const layer = new Map<string, number>();
  for (const n of nodes) layer.set(n.nodeId, 0);

  // 入边表
  const fromMap = new Map<string, string[]>();
  for (const e of edges) {
    if (!fromMap.has(e.to)) fromMap.set(e.to, []);
    fromMap.get(e.to)!.push(e.from);
  }

  // 迭代松弛（DAG 无环，有限次收敛）
  let changed = true;
  while (changed) {
    changed = false;
    for (const n of nodes) {
      const parents = fromMap.get(n.nodeId) ?? [];
      let max = -1;
      for (const p of parents) {
        const pl = layer.get(p) ?? 0;
        if (pl > max) max = pl;
      }
      const target = max + 1;
      if (target > (layer.get(n.nodeId) ?? 0)) {
        layer.set(n.nodeId, target);
        changed = true;
      }
    }
  }
  return layer;
}

/** 主入口：把规范化工作流布局成节点/边的像素网格。 */
export function layoutWorkflow(wf: Workflow, options?: { width?: number }): WorkflowLayout {
  const allEdges = collectEdges(wf);
  const layers = computeLayers(wf, wf.nodes, allEdges);

  // 按 layer 分组
  const buckets = new Map<number, WorkflowNode[]>();
  for (const n of wf.nodes) {
    const l = layers.get(n.nodeId) ?? 0;
    if (!buckets.has(l)) buckets.set(l, []);
    buckets.get(l)!.push(n);
  }

  const byLayer = [...buckets.entries()].sort((a, b) => a[0] - b[0]).map(([, group]) => group);

  const nodes: LayoutNode[] = [];
  let canvasWidth = PAD_X * 2;
  let canvasHeight = PAD_Y * 2;

  byLayer.forEach((group, layerIdx) => {
    const y = PAD_Y + layerIdx * (NODE_H + GAP_Y);
    group.forEach((n, i) => {
      const x = PAD_X + i * (NODE_W + GAP_X);
      nodes.push({
        nodeId: n.nodeId,
        funcName: n.funcName ?? "",
        layer: layerIdx,
        index: i,
        x,
        y,
        width: NODE_W,
        height: NODE_H,
      });
    });
    const layerWidth = PAD_X + group.length * (NODE_W + GAP_X);
    if (layerWidth > canvasWidth) canvasWidth = layerWidth;
  });

  canvasHeight = PAD_Y + byLayer.length * (NODE_H + GAP_Y);

  // 入口：entryNodeId 存在则用之；否则取入度为 0 的首个节点。
  let entry: string | null = wf.entryNodeId ?? null;
  if (entry && !wf.nodes.some((n) => n.nodeId === entry)) entry = null;
  if (!entry) {
    const indegree = new Set<string>();
    for (const e of allEdges) indegree.add(e.to);
    entry = wf.nodes.find((n) => !indegree.has(n.nodeId))?.nodeId ?? wf.nodes[0]?.nodeId ?? null;
  }

  const edges: LayoutEdge[] = allEdges.map((e) => ({ from: e.from, to: e.to, label: e.label }));

  return { nodes, edges, entry, canvasWidth, canvasHeight };
}