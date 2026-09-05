# OrchestrationStages 优化重构方案

## 概述

本文档记录 `OrchestrationStages.cs` 基于 `Share/StateManagement` 的优化重构方案。

## 核心问题

`OrchestrationStages.cs` 中定义了与 `Share/StateManagement` 功能重叠的类型和接口：

| 重复类型 | OrchestrationStages 定义 | StateManagement 定义 |
|---------|------------------------|---------------------|
| `IErrorHandler` | `HandleErrorAsync(ErrorContext, WorkflowContext, CancellationToken)` | `HandleErrorAsync(ErrorContext, IStateStore?, CancellationToken)` |
| `ErrorContext` | `WorkflowId` 属性 | `ExecutionId` 属性 |
| `RetryPolicy` | 基本一致 | 增加了 `GetDelayMs()` 方法 |
| `IMetricsCollector` | `CollectAsync(WorkflowContext, CancellationToken)` | `CollectAsync(IStateStore, CancellationToken)` |
| `IWorkflowAdapter` | `Adapt(WorkflowContext)` | `Adapt(IStateStore)` |
| `NodeExecutionRecord` | 基本一致 | 增加了 `Duration` 计算属性 |

## 重构策略：适配器桥接模式

由于接口签名不同（StateManagement 使用 `IStateStore`，OrchestrationStages 使用 `WorkflowContext`），**不能直接删除重复定义**。采用**适配器桥接模式**：

```
StateManagement 接口 (IStateStore-based)
        ↓ 适配器桥接
OrchestrationStages 接口 (WorkflowContext-based)
        ↓ 具体实现
NodeExtractorAdapter / LogErrorHandler / DurationMetricsCollector ...
```

### 新增文件

#### 1. `WorkflowContextStateStoreAdapter` — `IStateStore` 适配器

**路径**: `Operation/WorkFlowMethod/Skeletons/Adapters/WorkflowContextStateStoreAdapter.cs`

将 `WorkflowContext` 适配为 `IStateStore` 接口，使 StateManagement 的通用组件可在编排层使用。

**核心设计**：
- 所有读写操作委托给 `session.Data`（`DataContext` 实例）
- 当 `session` 为 null 时，`Get/Has` 返回默认值，`Set/Remove` 静默跳过
- `DataContext.Get()` 返回 `object`，适配器通过 `ToString()` 转换为 `string?`

**实现的方法**：
| IStateStore 方法 | 委托目标 |
|-----------------|---------|
| `Get(key)` | `DataContext.Get(key)?.ToString()` |
| `Has(key)` | `DataContext.ContainsKey(key)` |
| `Set(key, value)` | `DataContext.Set(key, value)` |
| `Remove(key)` | `DataContext.Remove(key)` |
| `GetAllKeys()` | `DataContext.Keys.ToArray()` |
| `GetAll()` | 遍历 `DataContext.Keys` |
| `RemoveByPrefix(prefix)` | 过滤 + 遍历 `Remove` |
| `SetRange(values)` | 遍历 `Set` |
| `Clear()` | `DataContext.Clear()` |
| `Count` | `DataContext.Count` |
| `IsEmpty` | `Count == 0` |
| `GetOrAdd(key, defaultValue)` | `ContainsKey` 检查 + `Set` |

### 修改的文件

#### 2. `OrchestrationStages.cs` — 新增桥接类

在 `#region 适配器接口与实现` 中新增：

**`StateManagementWorkflowAdapterBridge`**：
- 实现本地 `IWorkflowAdapter`
- 包装 `Share.StateManagement.IWorkflowAdapter`
- `Adapt(WorkflowContext)` 中创建 `WorkflowContextStateStoreAdapter` 并调用 `_inner.Adapt(adapter)`

#### 3. `OrchestrationStateManager.cs` — 新增 StateManagement 注册表

新增三个注册表属性：
- `StateManagementAdapterRegistry` — `IHandlerRegistry<string, Share.StateManagement.IWorkflowAdapter>`
- `StateManagementErrorHandlerRegistry` — `IHandlerRegistry<string, Share.StateManagement.IErrorHandler>`
- `StateManagementMetricsCollectorRegistry` — `IHandlerRegistry<string, Share.StateManagement.IMetricsCollector>`

## 使用示例

### 注册 StateManagement 适配器

```csharp
var manager = OrchestrationStateManager.CreateDefault();

// 注册 StateManagement 的 IWorkflowAdapter
var smAdapter = new Share.StateManagement.SomeWorkflowAdapter();
manager.StateManagementAdapterRegistry.Register(smAdapter.Name, smAdapter);

// 在 OrchestrationAdapterStage 中使用时，通过桥接自动适配
var adapterStage = new OrchestrationAdapterStage(manager.AdapterRegistry);
// 注意：StateManagementAdapterRegistry 中的适配器需要手动桥接
foreach (var kvp in manager.StateManagementAdapterRegistry.GetAll())
{
    manager.AdapterRegistry.Register(
        kvp.Key,
        new StateManagementWorkflowAdapterBridge(kvp.Value));
}
```

### 使用 WorkflowContextStateStoreAdapter

```csharp
var context = new WorkflowContext(session, graphSnapshot);
var adapter = new WorkflowContextStateStoreAdapter(context);

// 现在可以传递给任何需要 IStateStore 的组件
var errorHandler = new Share.StateManagement.SomeErrorHandler();
await errorHandler.HandleErrorAsync(errorContext, adapter, cancellationToken);
```

## 后续优化方向

1. **逐步迁移本地实现到 StateManagement**：当 StateManagement 的接口稳定后，可将本地实现（如 `LogErrorHandler`、`DurationMetricsCollector`）迁移到 StateManagement 层
2. **统一 ErrorContext**：考虑将 `WorkflowId` 改为 `ExecutionId` 以对齐 StateManagement
3. **移除重复的 RetryPolicy**：直接使用 `Share.StateManagement.RetryPolicy`
4. **移除重复的 NodeExecutionRecord**：直接使用 `Share.StateManagement.NodeExecutionRecord`
