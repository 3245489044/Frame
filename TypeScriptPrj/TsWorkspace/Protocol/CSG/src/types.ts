/**
 * CSG 数据模型（TypeScript 镜像）— 对齐 C# `CSG.Unified` 的 CanonicalForm / CanonicalNode / CanonicalEdge。
 *
 * 线格式约定：camelCase + `type` 为字符串（与 `csg-canonical-form.schema.json` 契约一致）。
 * 容错解析：节点/边 `type` 同时接受「字符串」与「数字」（兼容 C# `System.Text.Json` 默认把枚举序列化为数字），
 * 归一化后统一为字符串枚举，从而保证与 C# 侧「直接对接」时无论其发出数字还是字符串都能消费。
 */

/** 规范节点类型（字符串枚举，schema 契约）。 */
export enum CanonicalNodeType {
  Default = "Default",
  State = "State",
  Action = "Action",
  Condition = "Condition",
  Start = "Start",
  End = "End",
  Parallel = "Parallel",
  Merge = "Merge",
  Data = "Data",
  View = "View",
  Control = "Control",
  Table = "Table",
  Field = "Field",
  Entity = "Entity",
  Component = "Component",
}

/** 规范边类型（字符串枚举，schema 契约）。 */
export enum CanonicalEdgeType {
  Default = "Default",
  Transition = "Transition",
  DataFlow = "DataFlow",
  ControlFlow = "ControlFlow",
  Reference = "Reference",
  Composition = "Composition",
  Dependency = "Dependency",
  Navigation = "Navigation",
  Association = "Association",
}

/** CanonicalForm 状态（镜像 C# `FormState`）。 */
export enum FormState {
  Empty = 0,
  Populated = 1,
  Validated = 2,
}

/** 规范节点 — 所有配置元素、状态、图顶点统一用此类型表示。 */
export interface CanonicalNode {
  id: string;
  label?: string;
  type?: CanonicalNodeType | string;
  properties?: Record<string, unknown>;
}

/** 规范边 — 所有配置关系、状态转移、图边统一用此类型表示。 */
export interface CanonicalEdge {
  id: string;
  sourceId: string;
  targetId: string;
  label?: string;
  type?: CanonicalEdgeType | string;
  condition?: string | null;
  weight?: number;
  properties?: Record<string, unknown>;
}

/** CanonicalForm — Configuration-StateMachine-Graph 统一中间表示（与 C# 模型对齐）。 */
export interface CanonicalForm {
  nodes: CanonicalNode[];
  edges: CanonicalEdge[];
  metadata: Record<string, unknown>;
  /** 形式状态（镜像 C# `CanonicalForm.State`，缺省 Empty）。 */
  state?: FormState;
}

/** 规范节点类型的有序名（与 C# 枚举序一致，用于数字→名归一化）。 */
export const NODE_TYPE_NAMES = [
  "Default",
  "State",
  "Action",
  "Condition",
  "Start",
  "End",
  "Parallel",
  "Merge",
  "Data",
  "View",
  "Control",
  "Table",
  "Field",
  "Entity",
  "Component",
] as const;

/** 规范边类型的有序名（与 C# 枚举序一致）。 */
export const EDGE_TYPE_NAMES = [
  "Default",
  "Transition",
  "DataFlow",
  "ControlFlow",
  "Reference",
  "Composition",
  "Dependency",
  "Navigation",
  "Association",
] as const;

/** 容错解析节点类型：字符串（合法名）或数字（C# 枚举序）→ 规范字符串枚举；非法/缺失 → Default。 */
export function parseCanonicalNodeType(value: unknown): CanonicalNodeType {
  if (typeof value === "string") {
    return (NODE_TYPE_NAMES as readonly string[]).includes(value)
      ? (value as CanonicalNodeType)
      : CanonicalNodeType.Default;
  }
  if (typeof value === "number") {
    const name = NODE_TYPE_NAMES[value];
    return name ? (name as CanonicalNodeType) : CanonicalNodeType.Default;
  }
  return CanonicalNodeType.Default;
}

/** 容错解析边类型：字符串或数字 → 规范字符串枚举；非法/缺失 → Default。 */
export function parseCanonicalEdgeType(value: unknown): CanonicalEdgeType {
  if (typeof value === "string") {
    return (EDGE_TYPE_NAMES as readonly string[]).includes(value)
      ? (value as CanonicalEdgeType)
      : CanonicalEdgeType.Default;
  }
  if (typeof value === "number") {
    const name = EDGE_TYPE_NAMES[value];
    return name ? (name as CanonicalEdgeType) : CanonicalEdgeType.Default;
  }
  return CanonicalEdgeType.Default;
}

/** 容错解析 FormState：数字或字符串名 → FormState；非法/缺失 → Empty。 */
export function parseFormState(value: unknown): FormState {
  if (typeof value === "number") return value as FormState;
  if (typeof value === "string") {
    const names = ["Empty", "Populated", "Validated"] as const;
    const idx = (names as readonly string[]).indexOf(value);
    return idx >= 0 ? (idx as FormState) : FormState.Empty;
  }
  return FormState.Empty;
}

/** 规范化一个节点：type 归一为字符串枚举，缺省字段补齐。 */
export function normalizeNode(raw: unknown): CanonicalNode {
  const n = (raw ?? {}) as Record<string, unknown>;
  return {
    id: typeof n.id === "string" ? n.id : String(n.id ?? ""),
    label: typeof n.label === "string" ? n.label : "",
    type: parseCanonicalNodeType(n.type),
    properties: (n.properties && typeof n.properties === "object" ? n.properties : {}) as Record<string, unknown>,
  };
}

/** 规范化一条边：type 归一为字符串枚举，缺省字段补齐。 */
export function normalizeEdge(raw: unknown): CanonicalEdge {
  const e = (raw ?? {}) as Record<string, unknown>;
  return {
    id: typeof e.id === "string" ? e.id : String(e.id ?? ""),
    sourceId: typeof e.sourceId === "string" ? e.sourceId : String(e.sourceId ?? ""),
    targetId: typeof e.targetId === "string" ? e.targetId : String(e.targetId ?? ""),
    label: typeof e.label === "string" ? e.label : "",
    type: parseCanonicalEdgeType(e.type),
    condition: typeof e.condition === "string" ? e.condition : null,
    weight: typeof e.weight === "number" ? e.weight : 1.0,
    properties: (e.properties && typeof e.properties === "object" ? e.properties : {}) as Record<string, unknown>,
  };
}
