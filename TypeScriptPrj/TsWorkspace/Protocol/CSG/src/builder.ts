/**
 * CanonicalForm 构建器（镜像 C# `CanonicalFormBuilder`）— 流式构造。
 */
import {
  CanonicalEdge,
  CanonicalEdgeType,
  CanonicalForm,
  CanonicalNode,
  CanonicalNodeType,
  FormState,
} from "./types.js";
import { canonicalFormFromJson } from "./canonical-form.js";

export class CanonicalFormBuilder {
  private readonly _form: CanonicalForm = { nodes: [], edges: [], metadata: {}, state: FormState.Empty };

  /** 产出当前构建的 CanonicalForm；有节点且 state 仍为 Empty 时提升为 Populated。 */
  build(): CanonicalForm {
    if (this._form.nodes.length > 0 && this._form.state === FormState.Empty) {
      this._form.state = FormState.Populated;
    }
    return this._form;
  }

  withMetadata(key: string, value: unknown): this {
    this._form.metadata[key] = value;
    return this;
  }

  withMetadataRange(metadata: Record<string, unknown>): this {
    Object.assign(this._form.metadata, metadata);
    return this;
  }

  addNode(node: CanonicalNode): this;
  addNode(
    id: string,
    label?: string,
    type?: CanonicalNodeType,
    properties?: Record<string, unknown>
  ): this;
  addNode(
    idOrNode: string | CanonicalNode,
    label = "",
    type: CanonicalNodeType = CanonicalNodeType.Default,
    properties?: Record<string, unknown>
  ): this {
    const node: CanonicalNode =
      typeof idOrNode === "string"
        ? { id: idOrNode, label, type, properties: properties ?? {} }
        : idOrNode;
    this._form.nodes.push(node);
    return this;
  }

  addNodes(nodes: CanonicalNode[]): this {
    this._form.nodes.push(...nodes);
    return this;
  }

  addEdge(edge: CanonicalEdge): this;
  addEdge(
    id: string,
    sourceId: string,
    targetId: string,
    label?: string,
    type?: CanonicalEdgeType,
    condition?: string | null,
    weight?: number,
    properties?: Record<string, unknown>
  ): this;
  addEdge(
    idOrEdge: string | CanonicalEdge,
    sourceId = "",
    targetId = "",
    label = "",
    type: CanonicalEdgeType = CanonicalEdgeType.Default,
    condition: string | null = null,
    weight = 1.0,
    properties?: Record<string, unknown>
  ): this {
    const edge: CanonicalEdge =
      typeof idOrEdge === "string"
        ? {
            id: idOrEdge,
            sourceId,
            targetId,
            label,
            type,
            condition,
            weight,
            properties: properties ?? {},
          }
        : idOrEdge;
    this._form.edges.push(edge);
    return this;
  }

  addEdges(edges: CanonicalEdge[]): this {
    this._form.edges.push(...edges);
    return this;
  }

  /** 从 JSON 追加（合并 nodes/edges/metadata）；解析失败静默跳过。 */
  fromJson(json: string): this {
    const parsed = canonicalFormFromJson(json);
    if (parsed) {
      this._form.nodes.push(...parsed.nodes);
      this._form.edges.push(...parsed.edges);
      Object.assign(this._form.metadata, parsed.metadata);
    }
    return this;
  }

  clear(): this {
    this._form.nodes.length = 0;
    this._form.edges.length = 0;
    this._form.metadata = {};
    this._form.state = FormState.Empty;
    return this;
  }
}
