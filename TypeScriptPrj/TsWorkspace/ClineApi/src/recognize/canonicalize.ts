/**
 * Canonicalize —— `RuleHit[] → CSG CanonicalForm`（P3-1/P3-2，唯一实现）。
 *
 * 纪律：
 * ① 映射**只读** `Config/conversation-canon.ushar`（DR-3：agent spec 禁写 CanonicalNodeType）；
 * ② 未映射 kind ⇒ 产出 `unrecognized` 节点（`slot=unknown`），**禁落 `Default` 冒充已知**（AX.NoSilentOutlet / A-4）；
 * ③ 三态显式：`applicable ∈ {Known, Unknown, NotApplicable}`，禁用 `FormState`/缺省值压平；
 * ④ 本模块不做业务成败判定，也不静默补节点（缺配 ⇒ `fault` 节点可见）。
 */

import * as fs from "fs";
import type { RecognizerRule, RecognizerSpec } from "./vocabulary.js";

/** canon 单条映射（kind → CSG 节点类型 + 槽位）。 */
export interface CanonMapping {
  readonly type: string;
  readonly slot: string;
  readonly retention?: string;
  readonly visible: boolean;
}

/** `conversation-canon.ushar` 解析结果（全仓唯一 CSG 映射面）。 */
export interface ConversationCanon {
  readonly mappings: ReadonlyMap<string, CanonMapping>;
  readonly edgeTypes: ReadonlyMap<string, string>;
  readonly keys: ReadonlySet<string>;
  /** `unmapped = emit(unrecognized)`：未登记 kind 的显式末路（禁 Default）。 */
  readonly unmapped: boolean;
}

/** 规范节点（CSG 镜像；properties 受控键见 canon.keys）。 */
export interface CanonNode {
  readonly id: string;
  readonly type: string;
  readonly properties: Readonly<Record<string, unknown>>;
}

/** 规范边（CSG 镜像）。 */
export interface CanonEdge {
  readonly id: string;
  readonly sourceId: string;
  readonly targetId: string;
  readonly type: string;
}

/** 规范形（CSG CanonicalForm 的最小镜像；结构校验由 CSG ValidationEngine 承担）。 */
export interface CanonicalForm {
  readonly nodes: readonly CanonNode[];
  readonly edges: readonly CanonEdge[];
  readonly metadata: Readonly<Record<string, unknown>>;
}

/** 轮次形状契约（`round-shape.ushar` 解析结果）。 */
export interface RoundShape {
  readonly required: readonly string[];
  /** `exactly(n)` 的 n（缺声明 ⇒ 0 = ⊥，不放行）。 */
  readonly terminalCount: number;
  readonly ordered: string;
  readonly pairings: readonly { readonly from: string; readonly to: string; readonly by: string }[];
  readonly block: boolean;
}

/** 解析属性尾缀 `| k=v | k=v`。 */

/** 解析 `conversation-canon.ushar`（结构事实；解析失败即抛，禁静默给空映射）。 */
export function parseConversationCanon(text: string): ConversationCanon {
  const mappings = new Map<string, CanonMapping>();
  const edgeTypes = new Map<string, string>();
  const keys = new Set<string>();
  let unmapped = false;
  let section: "mapping" | "edges" | "other" = "mapping";

  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (line.length === 0 || line.startsWith("#") || line.startsWith("[") || line.startsWith("---")) continue;

    if (/^edges\b/i.test(line)) { section = "edges"; continue; }
    if (/^keys\s*=/i.test(line)) {
      section = "other";
      for (const key of line.slice(line.indexOf("=") + 1).split(/[,;]/)) {
        const trimmed = key.trim();
        if (trimmed.length > 0) keys.add(trimmed);
      }
      continue;
    }
    if (/^unmapped\s*=/i.test(line)) { unmapped = /emit\(\s*unrecognized\s*\)/i.test(line); continue; }
    if (/^applicable\s*=/i.test(line)) { section = "other"; continue; }

    const arrow = line.indexOf("→");
    if (arrow < 0) continue;
    const left = line.slice(0, arrow).trim();
    const right = line.slice(arrow + 1).trim();

    if (section === "edges") {
      const type = right.split(/\s+/)[0];
      if (left && type) edgeTypes.set(left, type);
      continue;
    }

    if (section !== "mapping") continue;
    const pipe = right.indexOf("|");
    const typeText = (pipe >= 0 ? right.slice(0, pipe) : right).trim();
    const attrs = parseAttrs(pipe >= 0 ? right.slice(pipe + 1) : undefined);
    const type = typeText.replace(/^CanonicalNodeType\./, "");
    if (!left || !type) continue;
    mappings.set(left, {
      type,
      slot: attrs["slot"] ?? "unknown",
      retention: attrs["retention"],
      visible: attrs["visible"] === "true",
    });
  }

  return { mappings, edgeTypes, keys, unmapped };
}

/** 读取并解析 canon 声明（缺失即抛）。 */
export function loadConversationCanon(filePath: string): ConversationCanon {
  if (!fs.existsSync(filePath)) {
    throw new Error(`conversation-canon 缺失: ${filePath}（映射单源不可缺；禁 Default 兜底）`);
  }
  return parseConversationCanon(fs.readFileSync(filePath, "utf-8"));
}

/** 解析 `round-shape.ushar`。 */
export function parseRoundShape(text: string): RoundShape {
  const required: string[] = [];
  let terminalCount = 0;
  let ordered = "";
  const pairings: { from: string; to: string; by: string }[] = [];
  let block = false;

  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (line.length === 0 || line.startsWith("#") || line.startsWith("[")) continue;
    const idx = line.indexOf("=");
    if (idx <= 0) continue;
    const key = line.slice(0, idx).trim().toLowerCase();
    const value = line.slice(idx + 1).trim();

    if (key === "required") required.push(...value.split(/[,;]/).map((s) => s.trim()).filter(Boolean));
    else if (key === "terminal") {
      const matched = /exactly\(\s*(\d+)\s*\)/i.exec(value);
      terminalCount = matched ? Number(matched[1]) : 0; // 未声明可判形态 ⇒ 0（⊥，不放行）
    } else if (key === "ordered") ordered = value;
    else if (key === "default") block = value.toLowerCase() === "block";
    else if (key === "pairings") {
      for (const part of value.split(/[,;]/)) {
        const matched = /(\S+)\s*⇄\s*(\S+)\s+by\s+(\S+)/.exec(part.trim());
        if (matched) pairings.push({ from: matched[1], to: matched[2], by: matched[3] });
      }
    }
  }

  return { required, terminalCount, ordered, pairings, block };
}

/** 读取并解析轮次形状契约（缺失即抛）。 */
export function loadRoundShape(filePath: string): RoundShape {
  if (!fs.existsSync(filePath)) {
    throw new Error(`round-shape 缺失: ${filePath}（轮次完备性判据不可缺）`);
  }
  return parseRoundShape(fs.readFileSync(filePath, "utf-8"));
}

function parseAttrs(text: string | undefined): Record<string, string> {
  const attrs: Record<string, string> = {};
  if (!text) return attrs;
  for (const part of text.split("|")) {
    const idx = part.indexOf("=");
    if (idx <= 0) continue;
    attrs[part.slice(0, idx).trim()] = part.slice(idx + 1).trim();
  }
  return attrs;
}

/** 识别产物（结构子集；与 `CanonReplayHit` / `RecognizedNode` 同形，禁二次解释）。 */
export interface RuleHit {
  readonly kind: string;
  readonly ordinal: number;
  /** `false` = 识别但不成节点（仅入账；仍参与配对/边构建，物化由写侧过滤）。 */
  readonly node: boolean;
  readonly payload: Readonly<Record<string, unknown>>;
}

/** Canonicalize 上下文（身份/来源；不含识别规则）。 */
export interface CanonicalizeOptions {
  readonly agent: string;
  readonly container: string;
  readonly round: number;
  readonly source: string;
  readonly vocabulary: string;
}

/** 从声明面取 kind 的终结标记（`Terminal` / `TerminalWhenLast` / `None`；缺省 None）。 */

/**
 * `RuleHit[] → CanonicalForm`（唯一实现）。
 *
 * @param hits 识别产物（到达序；ordinal 已按 kind 计）
 * @param canon `conversation-canon.ushar` 解析结果（唯一映射来源）
 * @param spec agent 声明（仅取 `mark`；不取识别谓词）
 * @param opts 身份与来源
 */
export function canonicalize(
  hits: readonly RuleHit[],
  canon: ConversationCanon,
  spec: RecognizerSpec | undefined,
  opts: CanonicalizeOptions
): CanonicalForm {
  const fallback = canon.mappings.get("unrecognized");
  if (!fallback) {
    throw new Error("conversation-canon 缺 unrecognized 末路（禁 Default 静默归一）");
  }

  const lastIndexOfKind = new Map<string, number>();
  hits.forEach((hit, index) => lastIndexOfKind.set(hit.kind, index));

  const nodes: CanonNode[] = [];
  const nodeByIndex = new Map<number, CanonNode>();

  hits.forEach((hit, index) => {
    const mapping = canon.mappings.get(hit.kind);
    const kind = mapping ? hit.kind : "unrecognized";
    const effective = mapping ?? fallback;
    const mark = markOf(spec, hit.kind);
    const isTerminal = mark === "Terminal"
      || (mark === "TerminalWhenLast" && lastIndexOfKind.get(hit.kind) === index);

    const properties: Record<string, unknown> = {
      kind,
      slot: effective.slot,
      mark: isTerminal ? "Terminal|Port" : "None",
      applicable: kind === "unrecognized" ? "Unknown" : hit.node ? "Known" : "NotApplicable",
      // 物化谓词（写侧唯一过滤依据；node=false 仍参与边构建）
      materialize: hit.node,
      ordinal: hit.ordinal,
    };
    for (const [key, value] of Object.entries(hit.payload)) {
      if (canon.keys.has(key) && value !== undefined && value !== null) properties[key] = value;
    }
    if (effective.retention) properties["retention"] = effective.retention;

    const node: CanonNode = {
      id: `${opts.container}.r${opts.round}.${kind}.${hit.ordinal}`,
      type: effective.type,
      properties,
    };
    nodes.push(node);
    nodeByIndex.set(index, node);
  });

  const edges: CanonEdge[] = [];
  const edgeType = (name: string, fallbackType: string): string => canon.edgeTypes.get(name) ?? fallbackType;
  const addEdge = (source: CanonNode, target: CanonNode, type: string): void => {
    edges.push({ id: `e${edges.length + 1}`, sourceId: source.id, targetId: target.id, type });
  };

  // turn：按到达序串链（控制流；fault 不参与串链，见 parent）
  const failureSlot = "failure"; // 槽位词表（canon 声明），非 kind 字面量
  const isFailure = (node: CanonNode): boolean => node.properties["slot"] === failureSlot;
  const orderedNodes = nodes.filter((node) => !isFailure(node));
  for (let i = 1; i < orderedNodes.length; i += 1) {
    addEdge(orderedNodes[i - 1], orderedNodes[i], edgeType("turn", "ControlFlow"));
  }

  // calls：请求槽（slot=prompt）→ 调用槽（slot=invoke）—— 槽位来自 canon，禁 kind 字面量（W-2）
  const slotOfKind = (kind: string): string | undefined => canon.mappings.get(kind)?.slot;
  let lastPrompt: CanonNode | undefined;
  hits.forEach((hit, index) => {
    const node = nodeByIndex.get(index);
    if (!node) return;
    const slot = slotOfKind(hit.kind);
    if (slot === "prompt") lastPrompt = node;
    else if (slot === "invoke" && lastPrompt) {
      addEdge(lastPrompt, node, edgeType("calls", "ControlFlow"));
    }
  });

  return finalizeEdges(hits, nodeByIndex, nodes, edges, canon, spec, opts, addEdge, edgeType);
}

/** 尾段：配对边（声明面 `edge` 驱动：pairWith / by / type）+ 缺配 fault 节点（可见，禁丢弃）。 */
function finalizeEdges(
  hits: readonly RuleHit[],
  nodeByIndex: ReadonlyMap<number, CanonNode>,
  nodes: CanonNode[],
  edges: CanonEdge[],
  canon: ConversationCanon,
  spec: RecognizerSpec | undefined,
  opts: CanonicalizeOptions,
  addEdge: (source: CanonNode, target: CanonNode, type: string) => void,
  edgeType: (name: string, fallbackType: string) => string
): CanonicalForm {
  const failureEntry = [...canon.mappings.entries()].find(([, mapping]) => mapping.slot === "failure");
  const failureKind = failureEntry?.[0] ?? "fault";
  const failureMapping = failureEntry?.[1];

  // 配对规则去重（同一 kind 的多条来源规则可各带 `edge`；配对语义只按 (kind,pairWith,by) 计一次）
  const pairRules: RecognizerRule[] = [];
  const seenPairs = new Set<string>();
  for (const rule of spec?.recognizers ?? []) {
    if (!rule.edge) continue;
    const key = `${rule.kind}|${rule.edge.pairWith}|${rule.edge.by}`;
    if (seenPairs.has(key)) continue;
    seenPairs.add(key);
    pairRules.push(rule);
  }
  const paired = new Map<string, CanonNode>();
  hits.forEach((hit, index) => {
    const node = nodeByIndex.get(index);
    if (!node) return;
    for (const rule of pairRules) {
      if (!rule.edge || hit.kind !== rule.edge.pairWith) continue;
      const value = hit.payload[rule.edge.by];
      if (typeof value === "string") paired.set(`${hit.kind}|${value}`, node);
    }
  });

  let faultSeq = 0;
  hits.forEach((hit, index) => {
    const callNode = nodeByIndex.get(index);
    if (!callNode) return;
    for (const rule of pairRules) {
      if (!rule.edge || hit.kind !== rule.kind) continue;
      const value = hit.payload[rule.edge.by];
      const target = typeof value === "string" ? paired.get(`${rule.edge.pairWith}|${value}`) : undefined;
      if (target) {
        addEdge(callNode, target, rule.edge.type);
        continue;
      }
      faultSeq += 1;
      const fault: CanonNode = {
        id: `${opts.container}.r${opts.round}.${failureKind}.${faultSeq}`,
        type: failureMapping?.type ?? "Condition",
        properties: {
          kind: failureKind,
          slot: failureMapping?.slot ?? "failure",
          mark: "None",
          applicable: "Unknown",
          materialize: true,
          ordinal: faultSeq,
          reason: "pairing-missing",
          ...(typeof value === "string" ? { [rule.edge.by]: value } : {}),
        },
      };
      nodes.push(fault);
      addEdge(callNode, fault, edgeType("parent", "Composition"));
      addEdge(fault, callNode, edgeType("returns", "DataFlow"));
    }
  });

  return {
    nodes,
    edges,
    metadata: {
      agent: opts.agent,
      round: opts.round,
      source: opts.source,
      vocabulary: opts.vocabulary,
      container: opts.container,
      total: hits.length,
      faults: faultSeq,
    },
  };
}

/**
 * 轮次形状校验（P3-3 第二段；第一段 = CSG 结构校验）。
 * 返回违规清单；空 = 通过（`crit:micro`）。
 */
export function validateRoundShape(
  shape: RoundShape,
  hits: readonly RuleHit[],
  spec: RecognizerSpec | undefined
): string[] {
  const issues: string[] = [];

  for (const required of shape.required) {
    if (!hits.some((hit) => hit.kind === required)) {
      issues.push(`[RoundShape] 缺必需 kind '${required}'（required）`);
    }
  }

  const lastIndexOfKind = new Map<string, number>();
  hits.forEach((hit, index) => lastIndexOfKind.set(hit.kind, index));
  const terminals = hits.filter((hit, index) => {
    const mark = markOf(spec, hit.kind);
    return mark === "Terminal" || (mark === "TerminalWhenLast" && lastIndexOfKind.get(hit.kind) === index);
  });
  if (shape.terminalCount <= 0 || terminals.length !== shape.terminalCount) {
    issues.push(`[RoundShape] 终答数 ${terminals.length} ≠ exactly(${shape.terminalCount})（terminal）`);
  }

  for (const pairing of shape.pairings) {
    const targets = new Set<string>();
    for (const hit of hits) {
      if (hit.kind !== pairing.to) continue;
      const value = hit.payload[pairing.by];
      if (typeof value === "string") targets.add(value);
    }
    for (const hit of hits) {
      if (hit.kind !== pairing.from) continue;
      const value = hit.payload[pairing.by];
      if (typeof value !== "string" || !targets.has(value)) {
        issues.push(`[RoundShape] '${pairing.from}' 缺配对 '${pairing.to}'（by=${pairing.by}: ${String(value)}）`);
      }
    }
  }

  if (shape.ordered && hits.length === 0) {
    issues.push("[RoundShape] 无任何识别产物（ordered 无法成立）");
  }

  return issues;
}


function markOf(spec: RecognizerSpec | undefined, kind: string): string {
  const rule = spec?.recognizers.find((candidate) => candidate.kind === kind);
  return rule?.mark ?? "None";
}

