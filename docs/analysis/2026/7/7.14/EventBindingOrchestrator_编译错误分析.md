# EventBindingOrchestrator 编译错误分析报告

> 日期：2026-07-15
> 项目：`CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.csproj`
> 错误数：224 个

---

## 1. 问题根因

**TConfig 类型重构导致 API 不兼容。**

`CsharpPrj/Plate/TConfig/` 中的关键类型在 2026-07-15 被迁移和简化（从 `Share.EventBindingOrchestrator`、`Modules.EventFramework`、`Modules.ConfigurationModels` 等迁入），但 `EventBindingOrchestrator` 中的代码仍使用旧的属性名和方法签名。

---

## 2. 分类修复策略

所有问题分为三类：

| 类别 | 操作 | 数量 |
|------|------|------|
| **A. 补充 TConfig** | TConfig 中缺失的必要属性/方法，补充回去 | ~15 项 |
| **B. 适配新 API** | EventBindingOrchestrator 代码适配 TConfig 的新属性名 | ~10 项 |
| **C. 添加引用/using** | 添加缺失的 using 指令或项目引用 | ~8 项 |

---

## 3. 详细分析

### 3.1 类别 A：必须补充到 TConfig 的属性

这些属性在 EventBindingOrchestrator 中被广泛使用，是运行时状态传递的核心，**必须补充到 `EventBindingContext`**。

#### A1. `EventBindingContext.CurrentControlConfig` (ControlConfig?)

**使用场景：**
- `ConfigBindingStage.Process()` — 读取 `ctx.CurrentControlConfig` 获取事件配置
- `ControlFinderTransformer.Transform()` — 读取 `input.CurrentControlConfig?.Name` 获取控件名
- `ControlFinderTransformer.CheckScriptModeGuard()` — 读取 `input.CurrentControlConfig` 判断脚本模式
- `GenericEventBindingPipeline.WalkAndBind()` — 设置 `context.CurrentControlConfig = controlConfig`

**判断：必须补充** — 这是 Config 路径绑定的核心数据，无法通过新 API 替代。

#### A2. `EventBindingContext.CurrentDeclaration` (EventBindingDeclaration?)

**使用场景：**
- `DeclarationBindingStage.Process()` — 读取 `ctx.CurrentDeclaration` 获取声明信息
- `ControlFinderTransformer.Transform()` — 读取 `input.CurrentDeclaration?.ControlName`
- `ControlFinderTransformer.CheckScriptModeGuard()` — 读取 `input.CurrentDeclaration`

**判断：必须补充** — Declaration 路径绑定的核心数据。

#### A3. `EventBindingContext.CurrentControl` (object?)

**使用场景：**
- `ConfigBindingStage.Process()` — 读取 `ctx.CurrentControl` 获取控件实例
- `DeclarationBindingStage.Process()` — 读取 `ctx.CurrentControl`
- `ControlFinderTransformer.Transform()` — 设置 `input.CurrentControl = ...`

**判断：必须补充** — 控件实例是绑定的目标对象。

#### A4. `EventBindingContext.SearchRoot` (object?)

**使用场景：**
- `ControlFinderTransformer.Transform()` — `input.SearchRoot ?? input.TargetObject`

**判断：必须补充** — 控件查找的搜索起点。

#### A5. `EventBindingContext.TargetObject` (object?)

**使用场景：**
- `ControlFinderTransformer.Transform()` — `input.SearchRoot ?? input.TargetObject`
- `GenericEventBindingPipeline.ExecuteWithWalk()` — 文档注释提到需预先设置

**判断：必须补充** — 控件查找的备选搜索起点。

#### A6. `EventBindingContext.Config` (UIConfiguration?)

**使用场景：**
- `ControlFinderTransformer.ValidateControlInConfig()` — `context.Config?.Views`
- `GenericEventBindingPipeline.ExecuteWithWalk()` — `context.Config = config`

**判断：必须补充** — 用于配置验证和控件树遍历。

#### A7. `EventBindingContext.EventBus` (IEventBus?)

**使用场景：**
- `ConfigBindingStage.Process()` — `ctx.EventBus` 用于绑定事件
- `DeclarationBindingStage.Process()` — `ctx.EventBus` 用于绑定事件

**判断：必须补充** — 事件绑定的核心依赖。

#### A8. `EventBindingContext.SkipCurrentControl` (bool)

**使用场景：**
- `ControlFinderTransformer.CheckScriptModeGuard()` — `input.SkipCurrentControl = true`

**判断：必须补充** — 脚本模式守卫的控制标志。

#### A9. `EventBindingContext.CurrentParameterBindings` (IReadOnlyList<ParameterBinding>?)

**使用场景：**
- `ConfigBindingStage.Process()` — `ctx.CurrentParameterBindings`
- `DeclarationBindingStage.Process()` — `ctx.CurrentParameterBindings`

**判断：必须补充** — 参数绑定传递。

#### A10. `EventBindingContext.CurrentOutput` (string?)

**使用场景：**
- `DeclarationBindingStage.Process()` — `ctx.CurrentOutput ?? declaration.OutputTarget`

**判断：必须补充** — 输出目标传递。

#### A11. `EventBindingContext.CurrentView` (object?)

**使用场景：**
- `GenericEventBindingPipeline.ExecuteWithWalk()` — `context.CurrentView = view`

**判断：必须补充** — 当前视图信息。

#### A12. `EventBindingContext.AddBoundControl()` / `AddSkippedControl()` 方法

**使用场景：**
- `EventBindingStageBase.RecordBoundControl()` — `input.AddBoundControl(...)`
- `ConfigBindingStage.Process()` — `ctx.AddSkippedControl(...)`

**判断：必须补充** — 绑定结果记录。

#### A13. `EventBindingContext.CreateSnapshot()` / `RestoreSnapshot()` 方法

**使用场景：**
- `GenericEventBindingPipeline.WalkAndBind()` — `context.CreateSnapshot()` / `context.RestoreSnapshot(snapshot)`

**判断：必须补充** — 控件遍历时的状态快照/恢复机制。

#### A14. `EventBindingContext.IsScriptExecutionMode` (bool)

**使用场景：**
- `ControlFinderTransformer.CheckScriptModeGuard()` — `input.IsScriptExecutionMode`

**判断：必须补充** — 脚本模式判断。

---

### 3.2 类别 B：适配 TConfig 新 API

这些是 EventBindingOrchestrator 中使用了旧属性名，需要改为 TConfig 新属性名。

#### B1. `ControlBindingInfo.Control` → 删除

**使用场景：** `EventBindingStageBase.RecordBoundControl()` 中 `Control = control`
**操作：** 删除此赋值，TConfig 的 `ControlBindingInfo` 没有 `Control` 属性

#### B2. `ControlBindingInfo.Events` → 删除

**使用场景：** `EventBindingStageBase.RecordBoundControl()` 中 `Events = eventsConfig`
**操作：** 删除此赋值

#### B3. `ControlBindingInfo.IsBound` → `Status`

**使用场景：** `EventBindingStageBase.RecordBoundControl()` 中 `IsBound = true`
**操作：** 改为 `Status = "Bound"`

#### B4. `ControlBindingInfo.EventCount` → `BoundEventCount`

**使用场景：** `EventBindingStageBase.RecordBoundControl()` 中 `EventCount = boundEventCount`
**操作：** 改为 `BoundEventCount = boundEventCount`

#### B5. `ControlBindingInfo.SkipReason` → 删除

**使用场景：** `ConfigBindingStage.Process()` 中 `SkipReason = "..."` 
**操作：** 删除此赋值，或改为 `Status = "Skipped"`

#### B6. `EventBindingDeclaration.ControlName` → `SourceName`

**使用场景：** `ControlFinderTransformer.Transform()` 中 `input.CurrentDeclaration?.ControlName`
**操作：** 改为 `input.CurrentDeclaration?.SourceName`

#### B7. `EventBindingDeclaration.EventName` → `EventType`

**使用场景：** `DeclarationBindingStage.Process()` 中 `declaration.EventName`
**操作：** 改为 `declaration.EventType`

#### B8. `EventBindingDeclaration.OutputMapping` → 删除

**使用场景：** `DeclarationBindingStage.Process()` 中 `declaration.OutputMapping`
**操作：** TConfig 的 `EventBindingDeclaration` 没有此属性，需要重构逻辑

#### B9. `ExecutionResult.Status` → 删除

**使用场景：** `EventBindingStageBase.HandleExecutionResult()` 中 `result.Status.ToString()`
**操作：** TConfig 的 `ExecutionResult` 没有 `Status` 属性，改为 `result.IsSuccess ? "Success" : "Failed"`

#### B10. `EventBindingContext.AddBoundControl()` 参数适配

**使用场景：** `EventBindingStageBase.RecordBoundControl()` 中 `input.AddBoundControl(new TConfig.ControlBindingInfo { ... })`
**操作：** 使用 TConfig 的新属性名构造 `ControlBindingInfo`

---

### 3.3 类别 C：添加引用/using

这些是缺失的 using 指令或项目引用。

#### C1. `IControlFinderStrategy` 找不到

**原因：** 接口在 `TConfig.Abstractions` 命名空间
**操作：** 在 `ReflectionControlFinderStrategy.cs` 中添加 `using TConfig;`

#### C2. `IValueWriter` 找不到

**原因：** 接口在 `Modules.EventFramework.Services` 命名空间
**操作：** 在 `EventTriggerService.cs` 中添加 `using Modules.EventFramework.Services;`

#### C3. `WorkFlowMethod` 找不到

**原因：** 类型在 `Operation.WorkFlowMethod` 项目
**操作：** 在 `EventBindingPipelineFactory.cs` 中添加 `using Operation.WorkFlowMethod;`（需确认命名空间）

#### C4. `StorageCell` 找不到

**原因：** 类型在 `Modules.InternalStorage.Core` 命名空间
**操作：** 在 `EventBindingOrchestrator.cs` 中添加 `using Modules.InternalStorage.Core;`

#### C5. `EventBindingDiagnosticContext` 找不到

**原因：** 类不存在（搜索无结果）
**操作：** 需要确认是否应创建此类，或删除引用

#### C6. `DefaultEventNameNormalizer` 找不到

**原因：** 类型在 `Modules.EventFramework.Core` 命名空间
**操作：** 在 `EventBindingStageBase.cs` 中已有 `using Modules.EventFramework.Core;` — 检查是否被删除

#### C7. `IEventBus` 接口实现不匹配

**原因：** TConfig 的 `IEventBus` 接口有 11 个方法，`WinFormsEventBus` 只实现了部分
**操作：** 需要实现所有缺失的方法

#### C8. `IEventBindingService` 接口实现不匹配

**原因：** TConfig 的 `IEventBindingService` 返回 `Task<BindingResult>` 和 `Task<List<BindingResult>>`
**操作：** 更新 `EventBindingService` 的实现签名

#### C9. `IEventTriggerService` 接口实现不匹配

**原因：** TConfig 的 `IEventTriggerService` 返回 `Task<IEventTriggerResult>`
**操作：** 更新 `EventTriggerService` 的实现签名

#### C10. `IEventBindingResolver` 接口实现不匹配

**原因：** TConfig 的 `IEventBindingResolver.Resolve()` 返回 `IEnumerable<ResolvedEventBinding>`
**操作：** 更新 `EventBindingResolver` 的实现签名

---

## 4. 涉及的文件及修复清单

| 文件 | 类别 | 修复内容 |
|------|------|---------|
| **TConfig/EventBindingContext.cs** | A | 补充 14 项缺失属性/方法 |
| **EventBindingStageBase.cs** | B | `ControlBindingInfo` 属性适配；`ExecutionResult.Status` 适配 |
| **ConfigBindingStage.cs** | A/B | `ctx.CurrentControlConfig` 等属性使用；`ControlBindingInfo` 适配 |
| **DeclarationBindingStage.cs** | A/B | `ctx.CurrentDeclaration` 等属性使用；`EventName`→`EventType`；`OutputMapping` 重构 |
| **ControlFinderTransformer.cs** | A/B | `CurrentDeclaration?.ControlName`→`SourceName`；`IsScriptExecutionMode` 等 |
| **GenericEventBindingPipeline.cs** | A | `CreateSnapshot/RestoreSnapshot`；`CurrentView`；`Config` |
| **WinFormsEventBus.cs** | C | 实现 `IEventBus` 所有缺失方法 |
| **EventBindingService.cs** | C | 更新 `IEventBindingService` 实现签名 |
| **EventTriggerService.cs** | C | 更新 `IEventTriggerService` 实现签名；添加 `IValueWriter` using |
| **EventBindingResolver.cs** | C | 更新 `IEventBindingResolver` 实现签名 |
| **ReflectionControlFinderStrategy.cs** | C | 添加 `IControlFinderStrategy` using |
| **EventBindingPipelineFactory.cs** | C | 添加 `WorkFlowMethod` using |
| **EventBindingOrchestrator.cs** | C | 添加 `StorageCell` using |
| **EventBusStages.cs** | C | `StorageCell` 引用检查 |
| **EventBindingResolverStages.cs** | C | `ParameterMapping` 引用检查 |

---

## 5. 修复优先级

```
第一优先级（阻塞性，必须先完成）：
  A1-A14: 补充 EventBindingContext 属性
  C7-C10: 接口实现不匹配

第二优先级（编译错误）：
  B1-B10: 适配新 API
  C1-C6: 添加 using/引用

第三优先级（运行时正确性）：
  B5: OutputMapping 逻辑重构
  B9: ExecutionResult.Status 替代逻辑
```
