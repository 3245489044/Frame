/**
 * Cline worker 生命周期 → CSG CanonicalForm 投影。
 *
 * 用 `csg-protocol` 把 daemon/engine 的 ClineCore 生命周期（spawned→loading→ready→api）
 * 投影为 CSG 状态机 CanonicalForm，作为跨语言状态契约上报。
 * 原则：Node 侧只「生成/消费」CanonicalForm 事实，权威状态仍由 C# 侧持有。
 */
import {
  CanonicalEdgeType,
  CanonicalForm,
  CanonicalFormBuilder,
  CanonicalNodeType,
  canonicalFormToJson,
} from "csg-protocol";

/** ClineCore 生命周期阶段（对齐 engine 的 stage 轨迹）。 */
export const CLINE_LIFECYCLE_STATES = ["spawned", "loading", "ready", "api"] as const;
export type ClineLifecycleState = (typeof CLINE_LIFECYCLE_STATES)[number];

/** 将 engine 的 trace stage 名映射到统一生命周期阶段；未知 stage 归为 spawned。 */
export function mapStageToLifecycle(stage: string): ClineLifecycleState {
  if (stage.includes("warmup-ok") || stage.includes("ensure-core-ok")) return "ready";
  if (stage.includes("phase2-core-start") || stage.includes("engine-run-start")) return "api";
  if (stage.includes("ensure-core-start") || stage.includes("load-sdk")) return "loading";
  return "spawned";
}

/** 构建 Cline worker 生命周期状态机 CanonicalForm（CSG 状态机投影）。 */
export function buildClineLifecycleForm(current: ClineLifecycleState = "spawned"): CanonicalForm {
  return new CanonicalFormBuilder()
    .withMetadata("csgVersion", "1.0.0")
    .withMetadata("source", "ClineLifecycleStateMachine")
    .withMetadata("currentState", current)
    .addNode("spawned", "Spawned", CanonicalNodeType.Start)
    .addNode("loading", "Loading", CanonicalNodeType.State)
    .addNode("ready", "Ready", CanonicalNodeType.State)
    .addNode("api", "Api", CanonicalNodeType.Action)
    .addEdge("e_spawned_loading", "spawned", "loading", "ensureCore", CanonicalEdgeType.Transition)
    .addEdge("e_loading_ready", "loading", "ready", "warmupOk", CanonicalEdgeType.Transition)
    .addEdge("e_ready_api", "ready", "api", "run", CanonicalEdgeType.Transition)
    .addEdge("e_api_ready", "api", "ready", "complete", CanonicalEdgeType.Transition)
    .build();
}

/** 序列化为跨语言契约 JSON（camelCase + 字符串枚举）。 */
export function clineLifecycleToJson(current: ClineLifecycleState): string {
  return canonicalFormToJson(buildClineLifecycleForm(current), false);
}
