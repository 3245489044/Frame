# EventBindingOrchestrator 编译错误分析报告

> 日期：2026-07-15
> 项目：`CsharpPrj\Share\EventBindingOrchestrator\EventBindingOrchestrator.csproj`
> 总计：1 个警告，75 个错误

---

## 一、已修复的问题

### 1. CS0105 — UniversalMiddleware.cs 重复 using

**文件：** `CsharpPrj/Share/UniversalMiddleware/UniversalMiddleware.cs(21,7)`

**问题：** `using TConfig;` 指令出现两次。

**修复：** 已删除重复的 `using TConfig;`。

---

## 二、错误分类分析

### 类别 A：命名空间不存在（CS0234）— 17 个错误

#### A1. `TConfig.Contexts` 不存在（2 个错误）

| 文件 | 行 | 错误 |
|------|-----|------|
| `Controls/ControlTreeWalkerTransformer.cs` | 17 | `TConfig` 中不存在 `Contexts` |
| `Invoke/Pipeline/ControlLookupStage.cs` | 18 | `TConfig` 中不存在 `Contexts` |

**原因：** `TConfig` 项目中有 `Contexts/` 目录（包含 `EventBindingContext.cs`、`BindingResult.cs` 等），但命名空间声明为 `namespace TConfig;`（文件范围），而非 `namespace TConfig.Contexts;`。需要检查这些文件的命名空间声明。

**建议修复：** 检查 `CsharpPrj/Plate/TConfig/Contexts/` 下的文件，确认命名空间是 `TConfig` 还是 `TConfig.Contexts`。如果是 `TConfig`，则 using 应改为 `using TConfig;`。

#### A2. `TConfig.Models` 不存在（2 个错误）

| 文件 | 行 | 错误 |
|------|-----|------|
| `Invoke/Pipeline/EventBindingResolver.cs` | 29 | `TConfig` 中不存在 `Models` |
| `Invoke/Pipeline/EventBindingResolverStages.cs` | 23 | `TConfig` 中不存在 `Models` |

**原因：** 同上，`TConfig` 项目中有 `Models/` 目录，但命名空间声明可能为 `namespace TConfig;` 而非 `namespace TConfig.Models;`。

**建议修复：** 检查 `CsharpPrj/Plate/TConfig/Models/` 下的文件命名空间。

#### A3. `Share.EventBindingOrchestrator.Models` / `Abstractions` 不存在（9 个错误）

| 文件 | 行 | 错误 |
|------|-----|------|
| `Invoke/Pipeline/EventTriggerStage.cs` | 19 | `Share.EventBindingOrchestrator` 中不存在 `Models` |
| `Stages/DiagnosticsScopeStage.cs` | 14 | 同上 |
| `Stages/EmptyViewGuardStage.cs` | 14 | 同上 |
| `Stages/StatisticsCollectStage.cs` | 18 | 同上 |
| `Stages/ValidateInputStage.cs` | 12 | 同上 |
| `Transformers/BindingMetadataTransformer.cs` | 15 | 同上 |
| `Transformers/ControlFinderTransformer.cs` | 21 | 不存在 `Abstractions` |
| `Transformers/ControlFinderTransformer.cs` | 23 | 不存在 `Models` |
| `Transformers/EventBindingPipelineFactory.cs` | 27 | 不存在 `Abstractions` |
| `Transformers/EventBindingPipelineFactory.cs` | 29 | 不存在 `Models` |
| `Transformers/GenericEventBindingPipeline.cs` | 25 | 不存在 `Models` |

**原因：** `Share.EventBindingOrchestrator` 项目中没有 `Models` 或 `Abstractions` 子命名空间。这些类型可能已被迁移到 `TConfig`。

**建议修复：** 将 using 改为引用 `TConfig` 中对应的类型。

#### A4. `Modules.EventFramework.Abstractions` 中不存在 `IEventBindingResolver`（1 个错误）

| 文件 | 行 | 错误 |
|------|-----|------|
| `EventBindingOrchestrator.cs` | 561 | `Modules.EventFramework.Abstractions` 中不存在 `IEventBindingResolver` |

**原因：** `IEventBindingResolver` 已从 `Modules.EventFramework.Abstractions` 迁移到 `TConfig`。

**建议修复：** 引用 `TConfig.IEventBindingResolver`。

---

### 类别 B：类型/命名空间找不到（CS0246）— 28 个错误

#### B1. `BackEndDriven` 找不到（2 个错误）

| 文件 | 行 | 错误 |
|------|-----|------|
| `Transformers/EventBindingPipelineFactory.cs` | 17 | `BackEndDriven` 找不到 |
| `Operation/WorkFlowMethod/EventBinding/EventBindingStageBase.cs` | 15 | `BackEndDriven` 找不到 |

**原因：** `BackEndDriven` 项目可能未被引用或已改名。

#### B2. `EventBindingContext` 找不到（3 个错误）

| 文件 | 行 | 错误 |
|------|-----|------|
| `Controls/ControlTreeWalkerTransformer.cs` | 27, 31 | `EventBindingContext` 找不到 |

**原因：** `EventBindingContext` 在 `TConfig.Contexts` 中，但命名空间引用不正确。

#### B3. `IPipelineContext` / `IPipelineStage` / `PipelineStageBase<>` / `PipelineContext` / `IStagePipeline<>` / `StagePipeline<>` 找不到（15 个错误）

涉及文件：
- `Invoke/Pipeline/PipelineContextBase.cs`
- `Invoke/Pipeline/ControlLookupStage.cs`
- `Invoke/Pipeline/EventTriggerStage.cs`
- `Invoke/Pipeline/EventBindingResolver.cs`
- `Invoke/Pipeline/EventBindingResolverStages.cs`
- `Invoke/Pipeline/EventBusStages.cs`
- `Invoke/Pipeline/WinFormsEventBus.cs`

**原因：** 这些类型来自 `Modules.Pipeline` 命名空间，但可能缺少 `using Modules.Pipeline;` 或项目引用。

#### B4. `DefaultEventBus` / `IEventNameNormalizer` / `IStorageCell` / `IControlFinderStrategy` 找不到（8 个错误）

涉及文件：
- `Invoke/Pipeline/WinFormsEventBus.cs`
- `Invoke/Pipeline/EventBusStages.cs`
- `Invoke/Pipeline/EventBindingResolverStages.cs`
- `Invoke/ReflectionControlFinderStrategy.cs`

**原因：** 这些类型来自 `Modules.EventFramework` 或 `TConfig`，但 using 指令缺失或不正确。

---

### 类别 C：歧义引用（CS0104）— 4 个错误

| 文件 | 行 | 歧义类型 |
|------|-----|---------|
| `Transformers/ControlFinderTransformer.cs` | 40, 50 | `IControlFinderStrategy` — `Modules.EventFramework.Abstractions` vs `TConfig` |
| `Transformers/EventBindingPipelineFactory.cs` | 60, 63 | `IEventMappingRegistry` / `IControlFinderStrategy` — 同上 |
| `Services/EventTriggerService.cs` | 32 | `IEventTriggerService` — `Modules.EventFramework.Services` vs `TConfig` |

**原因：** 接口已从 `Modules.EventFramework` 迁移到 `TConfig`，但旧 using 未移除，导致两个命名空间中都有同名类型。

**建议修复：** 移除 `Modules.EventFramework.Abstractions` 的 using，只保留 `TConfig` 的引用。

---

### 类别 D：接口未实现（CS0535）— 24 个错误

| 文件 | 类 | 未实现的接口成员 |
|------|-----|-----------------|
| `EventBindingOrchestrator.cs` | `EventBindingOrchestrator` | `IEventBindingService.BindControlAsync` / `BindControlsAsync` |
| `Services/EventBindingService.cs` | `EventBindingService` | 同上 |
| `Operation/WorkFlowMethod/EventBinding/EventBindingStageBase.cs` | `EventBindingStageBase` | `IEventHandlerFactory.CreateHandler`（2个重载）/ `IEventBinder.BindAsync` / `BindBatchAsync` |
| `Invoke/Pipeline/EventBindingResolver.cs` | `EventBindingResolver` | `IEventBindingResolver.Resolve` |
| `Invoke/Pipeline/WinFormsEventBus.cs` | `WinFormsEventBus` | `IEventBus.Publish<TEvent>` / `Subscribe<TEvent>` / `Unsubscribe<TEvent>` / `Publish(string, object?)` / `Subscribe(string, Action<object?>)` / `Unsubscribe(string, Action<object?>)` / `CanHandleControl` |

**原因：** `TConfig` 中的接口定义与 `EventBindingOrchestrator` 中的实现签名不匹配。接口已迁移到 `TConfig`，但实现类未更新以匹配新签名。

**详细分析：**

1. **`IEventBindingService`**（TConfig 定义）：
   - `BindControlAsync(object control, EventConfig eventConfig)` → 返回 `Task<BindingResult>`
   - `BindControlsAsync(IEnumerable<object> controls, IEnumerable<EventConfig> eventConfigs)` → 返回 `Task<List<BindingResult>>`
   - 实现类缺少这两个方法

2. **`IEventHandlerFactory`**（TConfig 定义）：
   - `CreateHandler(EventConfig, IFunctionExecutor)` → 返回 `Delegate?`
   - `CreateHandler(EventConfig, IFunctionExecutor, object)` → 返回 `Delegate?`
   - `EventBindingStageBase` 缺少这两个实现

3. **`IEventBinder`**（TConfig 定义）：
   - `BindAsync(object, EventConfig, EventBindingContext)` → 返回 `Task<BindingResult>`
   - `BindBatchAsync(IEnumerable<object>, IEnumerable<EventConfig>, EventBindingContext)` → 返回 `Task<List<BindingResult>>`
   - `EventBindingStageBase` 缺少这两个实现

4. **`IEventBindingResolver`**（TConfig 定义）：
   - `Resolve(object control, EventConfig eventConfig)` → 返回 `IEnumerable<ResolvedEventBinding>`
   - `EventBindingResolver` 缺少此方法

5. **`IEventBus`**（TConfig 定义）：
   - `WinFormsEventBus` 缺少 `Publish<TEvent>`、`Subscribe<TEvent>`、`Unsubscribe<TEvent>`、`Publish(string, object?)`、`Subscribe(string, Action<object?>)`、`Unsubscribe(string, Action<object?>)`、`CanHandleControl` 等 7 个成员

---

## 三、修复优先级建议

### 第一优先级：命名空间修复（解决 CS0234 + CS0246 + CS0104）

这些是"找不到类型"的问题，修复后可以大幅减少错误数量。

1. **修复 `TConfig` 子命名空间** — 检查 `Contexts/`、`Models/`、`Abstractions/` 下的文件命名空间声明
2. **移除歧义 using** — 删除 `Modules.EventFramework.Abstractions` 的 using，改用 `TConfig`
3. **补充缺失 using** — 添加 `using Modules.Pipeline;`、`using TConfig;` 等

### 第二优先级：接口实现修复（解决 CS0535）

在命名空间修复完成后，逐个实现缺失的接口成员。

1. **`WinFormsEventBus`** — 实现 `IEventBus` 缺失的 7 个成员
2. **`EventBindingOrchestrator` / `EventBindingService`** — 实现 `IEventBindingService` 缺失的 2 个成员
3. **`EventBindingStageBase`** — 实现 `IEventHandlerFactory` 和 `IEventBinder` 缺失的 4 个成员
4. **`EventBindingResolver`** — 实现 `IEventBindingResolver.Resolve` 方法

### 第三优先级：`BackEndDriven` 引用修复

确认 `BackEndDriven` 项目是否仍存在，或是否需要替换引用。

---

## 四、涉及文件清单

| # | 文件路径 | 错误数 |
|---|---------|--------|
| 1 | `CsharpPrj/Share/UniversalMiddleware/UniversalMiddleware.cs` | 1 警告 |
| 2 | `CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs` | 3 |
| 3 | `CsharpPrj/Share/EventBindingOrchestrator/Controls/ControlTreeWalkerTransformer.cs` | 5 |
| 4 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/ControlLookupStage.cs` | 2 |
| 5 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/EventBindingResolver.cs` | 5 |
| 6 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/EventBindingResolverStages.cs` | 8 |
| 7 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/EventBusStages.cs` | 8 |
| 8 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/EventTriggerStage.cs` | 2 |
| 9 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/PipelineContextBase.cs` | 1 |
| 10 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/Pipeline/WinFormsEventBus.cs` | 10 |
| 11 | `CsharpPrj/Share/EventBindingOrchestrator/Invoke/ReflectionControlFinderStrategy.cs` | 1 |
| 12 | `CsharpPrj/Share/EventBindingOrchestrator/Services/EventBindingService.cs` | 2 |
| 13 | `CsharpPrj/Share/EventBindingOrchestrator/Services/EventTriggerService.cs` | 1 |
| 14 | `CsharpPrj/Share/EventBindingOrchestrator/Stages/DiagnosticsScopeStage.cs` | 1 |
| 15 | `CsharpPrj/Share/EventBindingOrchestrator/Stages/EmptyViewGuardStage.cs` | 1 |
| 16 | `CsharpPrj/Share/EventBindingOrchestrator/Stages/StatisticsCollectStage.cs` | 1 |
| 17 | `CsharpPrj/Share/EventBindingOrchestrator/Stages/ValidateInputStage.cs` | 1 |
| 18 | `CsharpPrj/Share/EventBindingOrchestrator/Transformers/BindingMetadataTransformer.cs` | 1 |
| 19 | `CsharpPrj/Share/EventBindingOrchestrator/Transformers/ControlFinderTransformer.cs` | 4 |
| 20 | `CsharpPrj/Share/EventBindingOrchestrator/Transformers/EventBindingPipelineFactory.cs` | 6 |
| 21 | `CsharpPrj/Share/EventBindingOrchestrator/Transformers/GenericEventBindingPipeline.cs` | 1 |
| 22 | `CsharpPrj/Operation/WorkFlowMethod/EventBinding/EventBindingStageBase.cs` | 5 |
