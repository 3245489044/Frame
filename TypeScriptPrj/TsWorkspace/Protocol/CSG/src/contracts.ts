/**
 * CSG 三投影契约（镜像 C# `IStateMachine` / `IConfiguration` / `IGraph`）。
 */
import { CanonicalForm } from "./types.js";
import { CsgProtocol } from "./protocol.js";

/** 状态机契约 — 任何状态机的统一接口（对应 Configuration-StateMachine-Graph 的状态机投影）。 */
export interface IStateMachine {
  /** 当前状态标识。 */
  readonly currentState: string;

  /** 是否可以转移到目标状态。 */
  canTransitionTo(state: string): boolean;

  /** 尝试转移到目标状态；成功返回 true。 */
  tryTransitionTo(state: string, trigger?: unknown): boolean;

  /** 状态变更回调（from → to, trigger）。 */
  onStateChanged?: (from: string, to: string, trigger: string) => void;

  /** 投影为 CanonicalForm。 */
  toCanonical(): CanonicalForm;
}

/** 配置契约 — 任何配置式结构的统一接口（配置投影）。 */
export interface IConfiguration {
  /** 配置类型标识（"UI", "Workflow", "Database", "FileDriven" 等）。 */
  readonly configType: string;

  /** 配置 schema 版本。 */
  readonly schemaVersion: string;

  /** 协议版本（缺省取当前 CSG 版本）。 */
  readonly protocolVersion: string;

  /** 投影为 CanonicalForm。 */
  toCanonical(): CanonicalForm;
}

/** 图契约 — 任何图结构的统一接口（图投影）。 */
export interface IGraph {
  /** 所有节点 ID。 */
  readonly nodeIds: Iterable<string>;

  /** 所有边（source → target）对。 */
  readonly edgePairs: Iterable<[string, string]>;

  /** 投影为 CanonicalForm。 */
  toCanonical(): CanonicalForm;
}

/** IConfiguration 缺省 protocolVersion 实现辅助。 */
export function defaultProtocolVersion(): string {
  return CsgProtocol.CurrentVersion;
}
