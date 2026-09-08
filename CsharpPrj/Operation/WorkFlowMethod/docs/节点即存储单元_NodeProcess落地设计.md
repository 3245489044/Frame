# 「节点即存储单元」NodeProcess 落地设计

> 日期：2026-09-08
> 目标框架：`WorkFlowMethod\NodeProcess`
> 分层约束：`HierarchyMethod` 引用 `WorkFlowMethod`（上层）→ NodeProcess **不能**反向复用 `Operation.HierarchyMethod.Nodes.INodeOutputStore`（否则循环依赖）。故 NodeProcess **自持最小存储抽象**（`INodeProcessOutputStore`），上层经适配器对接 SQLite 实现。

## 一、背景与目标

Agent 骨架（`Agent.Core\Nodes`）已实现「节点即存储单元」：每个节点结果按 `(workflowId, nodeKey, port)`
持久化为输出端口，成为下游节点入参的唯一顺序化读源。该能力目前在通用 NodeProcess 管线中缺失。

本方案目标：让 NodeProcess 的节点执行管线补齐「提取 → 横切持久化 → 单写协调 → 读视图」四件套，
并复用已共享的 `INodeOutputStore`/`NodeOutput` 抽象，**不重造存储抽象**。

不覆盖（预留旋钮，后续阶段）：KeyMapping 分类写、引用边推导、Outbox 异步化。

## 二、现状差距

| 能力 | Agent.Core\Nodes | NodeProcess |
|---|---|---|
| 节点结果落端口 | `NodeExecutionService.PersistAsync` | 缺失 |
| 结果→端口提取 | `ExtractProps` | 缺失 |
| 单写者协调 | `WriteCoordinator` + `SingleWriteGuard` | 缺失 |
| 节点端口读源 | `NodeReadView.ListTaskOutputsAsync` | 仅内存变量（IParameterResolver） |
| 存储抽象 | 共享 `INodeOutputStore` | 未消费 |

## 三、核心抽象（NodeProcess 自持最小契约）

因分层约束，NodeProcess **不**复用上层 `INodeOutputStore`，改在其 `Abstractions` 定义最小契约：

```csharp
public interface INodeProcessOutputStore
{
    Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default);
    Task<NodeOutputResult?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default);
    Task<IReadOnlyList<NodeOutputResult>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default);
}
```

- 传递模型：`Models.NodeOutputResult`（NodeKey / WorkflowId / OutputPorts）。
- 内存实现：`Persistence.InMemoryNodeProcessOutputStore`（S1 测试用；枚举按 NodeKey 确定性排序，保障裸端口"先到者胜"契约）。
- S5 由上层 HierarchyMethod 提供适配器：将 `Operation.HierarchyMethod.Nodes.INodeOutputStore`（ur_nodes/SQLite）映射到本接口，**依赖方向保持 WorkFlowMethod ← HierarchyMethod 无环**。

## 四、新增组件（均在 NodeProcess 下）

### 1）结果→端口提取器 `Execution/NodeResultExtractor.cs`
泛化 Agent `ExtractProps`：

```csharp
public static class NodeResultExtractor
{
    // 按 NodeConfig.funcPipelineConfig.ResultKeys 定向全写；其余按结果类型公共属性名反射兜底。
    // null 结果 → 空端口（对齐 InitialStateNullity）。
    public static Dictionary<string, object?> ExtractResultKeys(NodeConfig config, object? result);
}
```

### 2）横切持久化 stage `Execution/NodeOutputPersistStage.cs`
```csharp
public sealed class NodeOutputPersistStage : PipelineStageBase<WfPipelineContext>
{
    // 构造注入：可空 INodeOutputStore + 可空 NodePersistCoordinator + 可空 ITransactionProvider
    // 执行语义（对齐 Agent OnAfterStepAsync）：
    //   · 取当前节点 NodeConfig + 上次执行结果（自 ctx 读取），经 NodeResultExtractor 提取端口
    //   · 逐 port 经 WriteOutputAsync（null 值跳过，空节点不落）
    //   · 单写门：NodePersistCoordinator.RunWriteAsync 覆盖
    //   · 事务合并（可选）：provider 非空 → 全端口一次 commit，消除中间态
    //   · 失败单一归类 + AgentLog 归因，不翻转业务 ok
}
```

### 3）单写者协调器 `Execution/NodePersistCoordinator.cs`
泛化 Agent `WriteCoordinator` + `SingleWriteGuard`：

```csharp
public sealed class NodePersistCoordinator
{
    // R3 单一确定性写门：串行单写 + 有界等待；同步路径与未来 Outbox 后台共用同一实例。
    // 固定写者名注册 → 平行实现写同 target fail-fast（AssertSingleWriter 语义）。
    public Task RunWriteAsync(string target, Func<CancellationToken, Task> body, CancellationToken ct = default);
}
```

### 4）读视图 `Abstractions/INodeOutputReadView.cs`
衔接现有 `IParameterResolver` / `KeyProcessor` / KeyMapping 入参解析：

```csharp
public interface INodeOutputReadView
{
    // 构建入参源键空间：{node}.{port} 恒有；裸 {port} 仅未被任一节点输出占用时并入（防跨节点互覆盖）。
    Task<IReadOnlyDictionary<string, object?>> BuildInputSourceAsync(string workflowId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, (string Node, string Port)>> BuildProvenanceAsync(string workflowId, CancellationToken ct = default);
}
```

## 五、装配点

在 `NodeProcessPipelineBuilder` 新增插件式 stage，插在 `NodeExecution` 之后、`NodeLoop` 循环体内复用：

```csharp
public NodeProcessPipelineBuilder AddNodeOutputStore(
    WorkFlowMethod.Skeletons.NodeProcess.Abstractions.INodeProcessOutputStore? store = null,
    WorkFlowMethod.Skeletons.NodeProcess.Execution.NodePersistCoordinator? coordinator = null)
```
> store 缺省 null = 未装配（零回归）；S5 由上层 HierarchyMethod 适配器填充其 SQLite 实现。
> `coordinator` 非空 = 同 workflowId 写入套单写门（fail-fast + 有界等待，S3 已接入）；`ITransactionProvider`（事务合并）后续确认实际契约后扩。

装配（在 `AddNodeLoop` 或 `AddAllStages` 之前调用 `AddNodeOutputStore`，由循环体读入；非独立 stage 顺序）：
`GraphDefinition → NodeChain → NodeExtractor → GraphChainUpdate → NodeExecution → {NodeLoop 体内: 节点执行步 → NodeOutputPersist} → NodeMetrics`

## 六、迁移步骤

- **S1 骨架**（已完成，13/13 单测过）：`NodeResultExtractor` + `Models.NodeOutputResult` + `Abstractions.INodeProcessOutputStore` + `INodeOutputReadView` + 内存版实现。
- **S2 横切**（已完成）：`NodeOutputPersistStage` + `AddNodeOutputStore`；**实际接线在 NodeLoop 循环体内、单节点执行步之后**（非独立 stage 顺序）。`WfPipelineContext.CurrentOutput` 即单节点产出；WorkflowId/NodeKey 取自上下文。未装配 store → 循环体内跳过，零行为回归。原型的 4 个语义点（空结果键=反射兜底、简单值整存默认端口、null 端口跳过、config 可空）已固化并有单测保护。事务合并（`ITransactionProvider`）推迟到 S3/S4 接入（需确认实际 Provider API）。
- **S3 单写**（已完成）：`NodePersistCoordinator`（R3 单写门：串行 + 有界等待 + 平行写同 workflowId fail-fast），经 `AddNodeOutputStore(store, coordinator)` 接入 `NodeOutputPersistStage`。`ITransactionProvider`（事务合并）仍待后续确认实际 Provider API 后扩。
- **S4 读源**：`INodeOutputReadView` 接入 `KeyProcessing`/`IParameterResolver`，节点端口成为入参源。
- **S5 上层适配器**：HierarchyMethod 将 `Operation.HierarchyMethod.Nodes.INodeOutputStore`（ur_nodes/SQLite）经适配器映射到 `INodeProcessOutputStore`，依赖方向无环；回归 Agent 非 LLM 测试。

## 七、校验标准

- **单测**：
  - `NodeResultToPorts`：ResultKeys 定向 / 反射兜底 / null 空端口
  - `Persist_WritePortsAndEnumerate`
  - `NullPortSkipped`
  - `SingleWriter_Serialized`
  - `ParallelWriters_SameTarget_FailFast`
  - `EmptyStore_ReadViewEmptySource`
- **契约断言**：
  - 同一 `(workflowId, nodeKey, port)` 幂等
  - null/空端口不产生槽位
  - 持久化失败单一归类、不翻转业务 ok
  - 未装配 store 时管线行为与原一致
- **回归**：Agent.Core.Tests 非 LLM 零行为漂移（对齐既有 Batch 回归门槛）。

## 八、风险与决策点

1. **同步/异步桥接**：NodeProcess 现管线以 `GetAwaiter().GetResult()` 同步推进，持久化 stage 为异步 I/O。建议 stage 内部异步执行 + 装配点同步包装（对齐现有 `NodeExecutionStage` 风格），单测覆盖死锁风险。
2. **读源收敛**：S4 将 `IParameterResolver` 从「仅内存变量」升级到「节点端口 ∪ 数据空间」，须保证裸端口与数据空间键冲突时溯源一致（对齐 Agent `sourceNode` 优先语义）。
3. **事务介质**：跨 ur_nodes 与 KeyMapping 的原子性依赖注入 `ITransactionProvider`；未注入时逐条直写（兼容未装配路径）。