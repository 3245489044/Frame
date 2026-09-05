/**
 * 生命周期状态机适配器（镜像 C# `LifecycleStateMachineAdapter`）。
 *
 * 通过委托注入把任意外部状态机包装为 CSG `IStateMachine`，并将其当前语义投影为 CanonicalForm，
 * 从而与 C# 侧 `LifecycleStateMachineAdapter` 共享同一 CanonicalForm 契约。
 */
import { CanonicalForm, FormState } from "./types.js";
import type { IStateMachine } from "./contracts.js";

type StateMachineRef = unknown;

/** 委托签名（镜像 C# 构造参数）。 */
export type GetCurrentState = (stateMachine: StateMachineRef) => string;
export type CanTransitionTo = (stateMachine: StateMachineRef, state: string) => boolean;
export type TryTransitionTo = (
  stateMachine: StateMachineRef,
  state: string,
  trigger?: unknown
) => boolean;
export type ToCanonical = (stateMachine: StateMachineRef) => CanonicalForm;
export type StateChangedHandler = (from: string, to: string, trigger: string) => void;

export class LifecycleStateMachineAdapter<TOperation extends string | number = string>
  implements IStateMachine
{
  private readonly _stateMachine: StateMachineRef;
  private readonly _getCurrentState: GetCurrentState;
  private readonly _canTransitionTo: CanTransitionTo;
  private readonly _tryTransitionTo: TryTransitionTo;
  private readonly _toCanonical: ToCanonical;
  private readonly _onStateChanged?: StateChangedHandler;

  constructor(
    stateMachine: StateMachineRef,
    getCurrentState: GetCurrentState,
    canTransitionTo: CanTransitionTo,
    tryTransitionTo: TryTransitionTo,
    toCanonical?: ToCanonical,
    onStateChanged?: StateChangedHandler
  ) {
    if (stateMachine == null) throw new Error("stateMachine 不能为空");
    this._stateMachine = stateMachine;
    this._getCurrentState = getCurrentState ?? (() => "");
    this._canTransitionTo = canTransitionTo ?? (() => false);
    this._tryTransitionTo = tryTransitionTo ?? (() => false);
    this._toCanonical = toCanonical ?? LifecycleStateMachineAdapter.defaultToCanonical;
    this._onStateChanged = onStateChanged;
  }

  get currentState(): string {
    return this._getCurrentState(this._stateMachine);
  }

  get onStateChanged(): StateChangedHandler | undefined {
    return this._onStateChanged;
  }

  canTransitionTo(state: string): boolean {
    return this._canTransitionTo(this._stateMachine, state);
  }

  tryTransitionTo(state: string, trigger?: unknown): boolean {
    return this._tryTransitionTo(this._stateMachine, state, trigger);
  }

  toCanonical(): CanonicalForm {
    return this._toCanonical(this._stateMachine);
  }

  /** 缺省投影：空 Form + 来源元数据（对齐 C# `DefaultToCanonical`）。 */
  private static defaultToCanonical(): CanonicalForm {
    return {
      nodes: [],
      edges: [],
      metadata: {
        source: "LifecycleStateMachine",
        adapter: "LifecycleStateMachineAdapter",
      },
      state: FormState.Empty,
    };
  }
}
