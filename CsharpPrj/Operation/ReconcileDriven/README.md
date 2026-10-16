# ReconcileDriven · UI 触发信号对账框架

> 把「UI 触发是否完整走完声明式信号回环」提升为**框架能力**：给每个触发绑定声明一份**信号契约**，
> 触发时开**触发域信号账**，执行后核对「实际信号流 ⊇ 声明契约、顺序成链、终止出现、归属正确」，
> 任一环断裂即结构化抛错（`strict`）或落事件（`warn`）。
>
> 参考设计：`Docs/analysis/2026/10/10.16/2026-10-16-UI触发信号对账架构-设计与施工单.md`

## 对账轴（关键校正）

对账面是**信号**，不是期望值。主干只看信号流四判：

| 判据 | 含义 | 判据号 |
|---|---|---|
| 完整性 | 应发尽发（缺失项即断点） | R1 |
| 顺序 | `order=strict` 时按声明序成链 | R2 |
| 终止 | 终止信号（`terminal:true`）须出现 | R3 |
| 归属 | `signals[].scope` 与 `ScopeId`/载荷一致 | R4 |

值层 `payload` 断言为**可选降级**，不作主干，也不与命令层 `AssertSystem` 相互复用。

## 读源（结构事实）

- **唯一读源** = `Strategy.Observability` 语义观测总线（`ObservabilityBus.Emit` 在锁内同步串行 Push，顺序即时序）。
- **禁**取 `EventSink`/`ur_events`（异步批写 + at-least-once ⇒ 实时对账假红/假绿）；落库仅审计/重放。

## 接线（依赖倒置，不反向依赖）

```
UI 触发 → EcsEventBus/命令入口
        → InvocationKernel.ExecuteAsync        ← 所有函数调用唯一入口
             └─ IEventReconcileHook（Share 定义，本模块实现，组合根注入）
                  └─ ReconcileStage: 开账 → 执行 → 摘账 → 对账 → 裁决
                       ├─ TriggerScopedSignalCollector（Bus Sink，按触发域收信号）
                       └─ EventReconciler（四判）
```

- 契约声明：`Plate/TConfig/Models/ReconcileConfig.cs` + `EventBinding.Reconcile`（JSON `events.bindings.reconcile`）。
- 绑定期登记：`Share/.../EventBindingPipelineFactory.ExtractFromConfig` 按函数名登记契约。
- 触发期包裹：`Share/.../Invocation/InvocationKernel.cs`（`ReconcileHook` 静态属性）。
- 组合根注入：`ReconcileBootstrap.Install(bus, collector)`。

## 目录

| 目录 | 内容 |
|---|---|
| `Contracts/` | `ReconcileContract`（声明面运行时视图 + 缺省补齐） |
| `Trace/` | `SignalRecord` / `SignalTrace` / `TriggerScope` / `TriggerScopedSignalCollector` |
| `Reconcile/` | `IEventReconciler` / `EventReconciler`（四判） / `ReconcileVerdict` / `ReconcileCodes` / `EventReconcileException` |
| `Gates/` | `ReconcileContractGate`（C1/C2/C6） / `ReconcileVocabulary`（词表 + 邻近候选） |
| `Host/` | `ReconcileStage` / `ReconcileHook` / `ReconcileBootstrap` / `ReconcileObservability` / `ReconcileContractRegistry` / `ReconcileOptions` |
| `Semantics/` | `reconcile_semantics.json`（ReconcilePassed / ReconcileFailed） |

## 配置声明示例

```json
"events": {
  "click": "AgentUi_TaskPoolGetStatus",
  "bindings": {
    "parameters": [ { "target": "taskKey", "source": "taskKeyTextBox", "sourceType": "control" } ],
    "output": "statusLabel",
    "reconcile": {
      "mode": "strict",
      "order": "strict",
      "signals": [
        { "signal": "DecisionResolved" },
        { "signal": "ToolSucceeded" },
        { "signal": "TaskEnqueued",  "scope": "tp_llm_mcp_demo" },
        { "signal": "TaskCompleted", "scope": "tp_llm_mcp_demo", "terminal": true }
      ]
    }
  }
}
```

## 裁决语义

| mode | Pass | Fail（缺/乱/未终止/串台） |
|---|---|---|
| `strict` | 落 `ReconcilePassed` | 抛 `EventReconcileException` + 落 `ReconcileFailed` |
| `warn` | 落 `ReconcilePassed` | 仅落 `ReconcileFailed`（不抛） |
| `off` | 不执行对账（显式退出，可审计） | — |

## 验证

- 编译：`dotnet build Frame/CsharpPrj/Operation/ReconcileDriven/ReconcileDriven.csproj`
- P2 唯一验证命令：`Frame\Invoke\Invoke-Verify.ps1 -Changed <files>`
- 生效面断言：`Frame\Invoke\Invoke-BuildEffective.ps1 -Assert`

## 灰度现状（对应施工单 S1–S6）

- ✅ S0 项目接入（modules.json + 生成 props + 伞引用） / S1 契约模型 / S2 信号账 / S3 对账器 + Hook / S4 装载期判据 C1/C2/C6
- ⏳ S5 TraceId 传播补全（触发→函数→下游 Agent 信号归属）—— 依赖宿主装配把 collector 注入 Bus Sink
- ⏳ S6 切 `strict` + 测试脚本 overlay 化
