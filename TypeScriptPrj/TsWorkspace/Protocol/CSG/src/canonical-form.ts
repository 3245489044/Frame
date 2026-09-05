/**
 * CanonicalForm 序列化 / URI 寻址（镜像 C# `CanonicalForm.ToJson/FromJson/ResolveUri/ListUriTemplates`）。
 */
import {
  CanonicalForm,
  FormState,
  normalizeNode,
  normalizeEdge,
  parseFormState,
} from "./types.js";

/** CSG URI 方案前缀（对齐 C# `CanonicalForm.UriScheme`）。 */
export const CSG_URI_SCHEME = "csg";

/** 序列化 CanonicalForm 为 JSON（camelCase；indented=true 时 2 空格缩进）。 */
export function canonicalFormToJson(form: CanonicalForm, indented = true): string {
  return JSON.stringify(form, null, indented ? 2 : undefined);
}

/** 反序列化 CanonicalForm；容错：type 接受字符串或数字，state 归一为 FormState。解析失败返回 null。 */
export function canonicalFormFromJson(json: string): CanonicalForm | null {
  try {
    const raw = JSON.parse(json) as Record<string, unknown>;
    const nodes = (Array.isArray(raw.nodes) ? raw.nodes : []).map((n) => normalizeNode(n));
    const edges = (Array.isArray(raw.edges) ? raw.edges : []).map((e) => normalizeEdge(e));
    const metadata =
      raw.metadata && typeof raw.metadata === "object"
        ? (raw.metadata as Record<string, unknown>)
        : {};
    return {
      nodes,
      edges,
      metadata,
      state: parseFormState(raw.state),
    };
  } catch {
    return null;
  }
}

/** 创建空 CanonicalForm（缺省 FormState.Empty）。 */
export function createCanonicalForm(): CanonicalForm {
  return { nodes: [], edges: [], metadata: {}, state: FormState.Empty };
}

/** URI 寻址：`csg://nodes` / `csg://node/{id}` / `csg://edges` / `csg://edge/{id}` / `csg://metadata[/{key}]`。 */
export function resolveUri(form: CanonicalForm, uri: string): unknown {
  if (!uri) throw new Error("URI 不能为空");
  if (!uri.toLowerCase().startsWith(`${CSG_URI_SCHEME}://`)) {
    throw new Error(`URI 必须以 '${CSG_URI_SCHEME}://' 开头`);
  }

  const path = uri.slice(CSG_URI_SCHEME.length + 3);
  const segments = path.split("/").filter((s) => s.length > 0);

  if (segments.length === 0) return form;

  switch (segments[0]) {
    case "nodes":
    case "node":
      if (segments.length === 1) return form.nodes;
      return form.nodes.find((n) => n.id === segments[1]) ?? null;
    case "edges":
    case "edge":
      if (segments.length === 1) return form.edges;
      return form.edges.find((e) => e.id === segments[1]) ?? null;
    case "metadata":
      if (segments.length === 1) return form.metadata;
      return (form.metadata[segments[1]] as unknown) ?? null;
    default:
      return null;
  }
}

/** URI 模板定义（对齐 C# `ListUriTemplates`）。 */
export interface UriTemplate {
  uri: string;
  description: string;
}

/** 枚举本实现的全部 URI 模板。 */
export function listUriTemplates(): UriTemplate[] {
  return [
    { uri: "csg://nodes", description: "所有节点列表" },
    { uri: "csg://node/{id}", description: "指定节点" },
    { uri: "csg://edges", description: "所有边列表" },
    { uri: "csg://edge/{id}", description: "指定边" },
    { uri: "csg://metadata", description: "所有元数据" },
    { uri: "csg://metadata/{key}", description: "指定元数据" },
  ];
}
