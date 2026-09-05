/**
 * WorkflowGraph — 工作流 JSON 契约类型。
 *
 * 直接消费 Produce 链的 JSON 形状：`ProduceNode`/`ProduceNodeNext`（见
 * Projects/Produce/src/Produce.Contract/ProduceWorkflow.cs）。这里镜像其字段而非
 * 依赖 C# 契约，保证本包可独立解析任意同形工作流 JSON。
 */

/** 节点出边 —— 四种确定性边（对应 C# `ProduceNodeNext`）。 */
export interface WorkflowNodeNext {
  onSuccess?: string | null;
  onFail?: string | null;
  onUnavailable?: string | null;
  onMax?: string | null;
  /** onSuccess 别名（兼容小写/简写）。 */
  success?: string | null;
}

/** 单个确定性节点（对应 C# `ProduceNode`）。 */
export interface WorkflowNode {
  nodeId: string;
  funcName?: string;
  parameterKeys?: string[];
  resultKeys?: string[];
  next?: WorkflowNodeNext | null;
  options?: Record<string, unknown>;
}

/** 一个工作流（对应 C# `ProduceWorkflow`）。 */
export interface Workflow {
  id?: string;
  name?: string;
  entryNodeId?: string | null;
  nodes: WorkflowNode[];
  input?: Record<string, unknown>;
}

/**
 * 节点规范化：把原始 JSON 项收敛为结构稳定的 <see cref="WorkflowNode"/>。
 * @param raw 从 JSON 解析出来的任意节点对象。
 */
export function normalizeNode(raw: unknown): WorkflowNode {
  const obj = (raw ?? {}) as Record<string, unknown>;
  const nodeId = typeof obj.nodeId === "string" ? obj.nodeId : String(obj.nodeId ?? "");
  const funcName = typeof obj.funcName === "string" ? obj.funcName : "";
  const nextObj = (obj.next ?? {}) as Record<string, unknown>;
  const str = (v: unknown): string | null => (typeof v === "string" && v.length > 0 ? v : null);
  return {
    nodeId,
    funcName,
    parameterKeys: Array.isArray(obj.parameterKeys) ? obj.parameterKeys.map(String) : undefined,
    resultKeys: Array.isArray(obj.resultKeys) ? obj.resultKeys.map(String) : undefined,
    next: {
      onSuccess: str(nextObj.onSuccess) ?? str(nextObj.success),
      onFail: str(nextObj.onFail),
      onUnavailable: str(nextObj.onUnavailable),
      onMax: str(nextObj.onMax),
    },
    options:
      obj.options && typeof obj.options === "object"
        ? (obj.options as Record<string, unknown>)
        : undefined,
  };
}

/**
 * 解析工作流 JSON 文本。
 * @param json 完整的工作流 JSON 字符串。
 * @returns 规范化后的 <see cref="Workflow"/>。
 */
export function workflowFromJson(json: string): Workflow {
  const raw = JSON.parse(json) as Record<string, unknown>;
  const nodesRaw = Array.isArray(raw.nodes) ? raw.nodes : [];
  const id = typeof raw.id === "string" ? raw.id : "";
  const name = typeof raw.name === "string" ? raw.name : "";
  const entryNodeId =
    typeof raw.entryNodeId === "string" && raw.entryNodeId.length > 0 ? raw.entryNodeId : null;
  return {
    id,
    name,
    entryNodeId,
    nodes: nodesRaw.map(normalizeNode),
    input:
      raw.input && typeof raw.input === "object"
        ? (raw.input as Record<string, unknown>)
        : undefined,
  };
}