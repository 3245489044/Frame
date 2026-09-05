# EventBindingOrchestrator 编译错误分类文档

> 生成日期：2026-07-15
> 项目：`EventBindingOrchestrator.csproj`
> 总错误数：38 个（去重后约 10 类）

---

## 1. 命名空间 `Models` 不存在（CS0234 × 8 文件）

**涉及文件：**
- `Stages/DiagnosticsScopeStage.cs:14`
- `Stages/EmptyViewGuardStage.cs:14`
- `Stages/StatisticsCollectStage.cs:18`
- `Stages/ValidateInputStage.cs:12`
- `Transformers/BindingMetadataTransformer.cs:15`
- `Transformers/ControlFinderTransformer.cs:23`
- `Transformers/EventBindingPipelineFactory.cs:29`
- `Transformers/GenericEventBindingPipeline.cs:25`

**原因：** `Share.EventBindingOrchestrator.Models` 命名空间已不存在，其中的类型（`EventBindingContext` 等）已迁移到 `TConfig`。

**修复：** 将 `using Share.EventBindingOrchestrator.Models;` 改为 `using TConfig;`

---

## 2. 命名空间 `Abstractions` 不存在（CS0234 × 2 文件）

**涉及文件：**
- `Transformers/ControlFinderTransformer.cs:21`
- `Transformers/EventBindingPipelineFactory.cs:27`

**原因：** `Share.EventBindingOrchestrator.Abstractions` 命名空间已不存在。

**修复：** 移除该 using，相关类型（`IControlFinderStrategy` 等）需从 TConfig 或 Modules.EventFramework 引用。

---

## 3. 类型 `BackEndDriven` 找不到（CS0246 × 2 文件）

**涉及文件：**
- `Transformers/EventBindingPipelineFactory.cs:17`
- `Operation/WorkFlowMethod/EventBinding/EventBindingStageBase.cs:15`

**原因：** `BackEndDriven` 命名空间/类型在当前项目引用中不存在。

**修复：** 添加 `using Operation.BackEndDriven;` 或确认项目引用是否包含 BackEndDriven 项目。

---

## 4. `IEventMappingRegistry` 不明确引用（CS0104 × 1 文件）

**涉及文件：**
- `Transformers/EventBindingPipelineFactory.cs:60`

**原因：** `IEventMappingRegistry` 同时在 `Modules.EventFramework.Abstractions` 和 `TConfig` 中定义。

**修复：** 添加 using 别名：`using IEventMappingRegistry = TConfig.IEventMappingRegistry;`

---

## 5. `IEventTriggerService` 不明确引用（CS0104 × 1 文件）

**涉及文件：**
- `Services/EventTriggerService.cs:32`

**原因：** `IEventTriggerService` 同时在 `Modules.EventFramework.Services` 和 `TConfig` 中定义。

**修复：** 添加 using 别名：`using IEventTriggerService = TConfig.IEventTriggerService;`

---

## 6. 类型 `IControlFinderStrategy` 找不到（CS0246 × 1 文件）

**涉及文件：**
- `Invoke/ReflectionControlFinderStrategy.cs:33`

**原因：** `IControlFinderStrategy` 接口未在当前项目引用中定义。

**修复：** 确认该接口定义位置（可能在 `Modules.EventFramework.Abstractions`），添加对应 using。

---

## 7. 类型 `IStorageCell` 找不到（CS0246 × 6 处）

**涉及文件：**
- `Invoke/Pipeline/EventBusStages.cs:39,45,54`
- `Invoke/Pipeline/EventBindingResolverStages.cs:39,45,54`

**原因：** `IStorageCell` 接口未在当前项目引用中定义。

**修复：** 确认该接口定义位置，添加对应 using 或项目引用。

---

## 8. 类型 `IEventNameNormalizer` 找不到（CS0246 × 4 处）

**涉及文件：**
- `Invoke/Pipeline/EventBusStages.cs:149,155`
- `Invoke/Pipeline/WinFormsEventBus.cs:36,47`

**原因：** `IEventNameNormalizer` 接口未在当前项目引用中定义。

**修复：** 确认该接口定义位置（可能在 `Modules.EventFramework.Abstractions`），添加对应 using。

---

## 9. 类型 `DefaultEventBus` 找不到（CS0246 × 1 处）

**涉及文件：**
- `Invoke/Pipeline/WinFormsEventBus.cs:35`

**原因：** `DefaultEventBus` 类未在当前项目引用中定义。

**修复：** 添加 `using Modules.EventFramework.Bus;` 或确认类名是否已变更。

---

## 10. 接口实现不匹配（CS0535 × 6 个类）

| 类 | 未实现的接口成员 |
|---|---|
| `WinFormsEventBus` | `IEventBus.Publish<TEvent>`、`Subscribe<TEvent>`、`Unsubscribe<TEvent>`、`Publish(string,object?)`、`Subscribe(string,Action<object?>)`、`Unsubscribe(string,Action<object?>)`、`CanHandleControl(object)` |
| `EventBindingOrchestrator` | `IEventBindingService.BindControlAsync(object,EventConfig)`、`BindControlsAsync(...)` |
| `EventBindingService` | `IEventBindingService.BindControlAsync(object,EventConfig)`、`BindControlsAsync(...)` |
| `EventBindingStageBase` | `IEventHandlerFactory.CreateHandler(EventConfig,IFunctionExecutor)`、`CreateHandler(EventConfig,IFunctionExecutor,object)`、`IEventBinder.BindAsync(object,EventConfig,EventBindingContext)`、`BindBatchAsync(...)` |
| `EventBindingResolver` | `IEventBindingResolver.Resolve(object,EventConfig)` |

**原因：** TConfig 中的接口方法签名已变更，但实现类未同步更新。

**修复：** 需要对照 TConfig 中接口定义，更新各实现类的方法签名。

---

## 总结

| 类别 | 错误数 | 严重程度 |
|---|---|---|
| 命名空间不存在（CS0234） | 10 | ⚠️ 简单修复（改 using） |
| 类型找不到（CS0246） | 14 | ⚠️ 需确认类型位置 |
| 不明确引用（CS0104） | 2 | ⚠️ 简单修复（using 别名） |
| 接口未实现（CS0535） | 12 | 🔴 需修改方法签名 |

**核心根因：** TConfig 迁移后，`Share.EventBindingOrchestrator.Models` 和 `Share.EventBindingOrchestrator.Abstractions` 子命名空间被移除，类型迁移到 `TConfig` 命名空间，但 `EventBindingOrchestrator` 项目中的 using 和接口实现未同步更新。
