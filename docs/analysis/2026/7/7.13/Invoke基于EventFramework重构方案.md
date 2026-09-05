# Invoke 基于 EventFramework 重构方案

> **日期**：2026-07-13  
> **目标**：`CsharpPrj/Share/EventBindingOrchestrator/Invoke/` 基于 `CsharpPrj/Modules/EventFramework/` 进行重构，消除重复代码  
> **原则**：渐进式替换，不破坏现有引用

---

## 1. 现状分析

### 1.1 Invoke 目录（18 个文件）

| 文件 | 命名空间 | 角色 |
|------|----------|------|
| `ControlTreeWalker.cs` | `Share.EventBindingOrchestrator.Utils` | 控件树遍历 |
| `DefaultEventMappingRegistry.cs` | `Share.EventBindingOrchestrator` | 事件映射注册 |
| `EventBindingValidator.cs` | `Share.EventBindingOrchestrator.Parameters` | 声明验证 |
| `ParameterBuilder.cs` | `Share.EventBindingOrchestrator.Parameters` | 参数构建 |
| `EventBindingDeclaration.cs` | `Share.EventBindingOrchestrator.Parameters` | 绑定声明模型 |
| `EventBindingInfo.cs` | `Share.EventBindingOrchestrator.Models` | 绑定信息模型 |
| `EventRequest.cs` | `Share.EventBindingOrchestrator.Parameters` | 事件请求 DTO |
| `WorkflowDefinitionEntry.cs` | `Share.EventBindingOrchestrator.Parameters.Binding` | 工作流定义 |
| `EventBindingHelper.cs` | `Share.EventBindingOrchestrator.Parameters` | 反射辅助方法 |
| `EventBindingResolver.cs` | `Share.EventBindingOrchestrator.Parameters` | 绑定解析器 |
| `EventBindingStageBase.cs` | `Share.EventBindingOrchestrator.Stages` | 管线基类 |
| `EventTriggerStage.cs` | `Share.EventBindingOrchestrator.Stages` | 事件触发 |
| `ControlLookupStage.cs` | `Share.EventBindingOrchestrator.Stages` | 控件查找 |
| `OutputHelper.cs` | `Share.EventBindingOrchestrator.Parameters.Output` | 输出辅助 |
| `ReflectionControlFinderStrategy.cs` | `Share.EventBindingOrchestrator` | 反射查找策略 |
| `ReflectionEventBinderStrategy.cs` | `Share.EventBindingOrchestrator` | 反射绑定策略 |
| `WinFormsEventBus.cs` | `Share.EventBindingOrchestrator` | WinForms 事件总线 |
| `WinFormsValueAccessor.cs` | `Share.EventBindingOrchestrator` | WinForms 值访问器 |

### 1.2 EventFramework 对应实现（8 个文件）

| 文件 | 命名空间 | 对应 Invoke 文件 |
|------|----------|-----------------|
| `ControlTreeWalker.cs` | `Modules.EventFramework` | `Invoke/ControlTreeWalker.cs` |
| `DefaultEventMappingRegistry.cs` | `Modules.EventFramework` | `Invoke/DefaultEventMappingRegistry.cs` |
| `EventBindingValidator.cs` | `Modules.EventFramework` | `Invoke/EventBindingValidator.cs` |
| `ParameterBuilder.cs` | `Modules.EventFramework` | `Invoke/ParameterBuilder.cs` |
| `Models/EventBindingDeclaration.cs` | `Modules.EventFramework.Models` | `Invoke/EventBindingDeclaration.cs` |
| `Models/EventBindingInfo.cs` | `Modules.EventFramework.Models` | `Invoke/EventBindingInfo.cs` |
| `Models/EventRequest.cs` | `Modules.EventFramework.Models` | `Invoke/EventRequest.cs` |
| `Models/WorkflowDefinitionEntry.cs` | `Modules.EventFramework.Models` | `Invoke/WorkflowDefinitionEntry.cs` |

### 1.3 项目依赖关系

`EventBindingOrchestrator.csproj` 已引用 `EventFramework.csproj`（第 25 行），因此 Invoke 可以直接使用 `Modules.EventFramework` 命名空间下的类型。

---

## 2. 差异对比

### 2.1 ControlTreeWalker

| 维度 | Invoke 版本 | EventFramework 版本 |
|------|-------------|-------------------|
| 命名空间 | `Share.EventBindingOrchestrator.Utils` | `Modules.EventFramework` |
| 方法 | 有 `CollectNames`、`CollectControlNames`、`BuildControlMap`、`FindControl`（2 个重载）、`WalkAllViewsAsync`、`WalkControlsAsync` | 有 `WalkAllViews`、`WalkControls`、`WalkControlsAsync`、`FindControl`、`BuildControlMap`、`GetDirectChildren` |
| 差异 | Invoke 多出 `CollectNames`、`CollectControlNames`、`WalkAllViewsAsync`、`FindControl(List<>, string)` | EventFramework 多出 `GetDirectChildren`（public） |

**结论**：EventFramework 版本是 Invoke 版本的子集。Invoke 版本有 4 个额外方法。

### 2.2 DefaultEventMappingRegistry

| 维度 | Invoke 版本 | EventFramework 版本 |
|------|-------------|-------------------|
| 命名空间 | `Share.EventBindingOrchestrator` | `Modules.EventFramework` |
| 内部存储 | `ConcurrentDictionary<string, EventMappingEntry>`（单条目） | `ConcurrentDictionary<string, List<EventMappingEntry>>`（多条目，支持优先级排序） |
| 接口 | `IEventMappingRegistry`（Share 层接口） | `IEventMappingRegistry`（Modules 层接口） |
| 规范化 | 构造函数中不注入 `IEventNameNormalizer`，`GetAccessor` 内部调用 `_normalizer.ToPascalCase()` | 构造函数可选注入 `IEventNameNormalizer`，`RegisterMapping` 时规范化 |
| 优先级 | 不支持优先级排序（后注册覆盖前注册） | 支持优先级排序（高优先级优先匹配） |

**结论**：两个版本实现差异较大，EventFramework 版本更完善（支持优先级排序）。Invoke 版本较简单。

### 2.3 EventBindingValidator

| 维度 | Invoke 版本 | EventFramework 版本 |
|------|-------------|-------------------|
| 命名空间 | `Share.EventBindingOrchestrator.Parameters` | `Modules.EventFramework` |
| 泛型参数 | `EventBindingDeclaration`（Invoke 版本） | `EventBindingDeclaration`（EventFramework 版本） |
| 约束 | ControlName + EventName + FunctionName + Parameters.Target 不重复 | TargetName + EventName + FunctionName |
| 额外方法 | 有 `ValidateAllWithErrors` | 无 |

**结论**：Invoke 版本多一个 `ValidateAllWithErrors` 方法，且约束更严格（检查 Parameters.Target 重复）。

### 2.4 ParameterBuilder

| 维度 | Invoke 版本 | EventFramework 版本 |
|------|-------------|-------------------|
| 命名空间 | `Share.EventBindingOrchestrator.Parameters` | `Modules.EventFramework` |
| 方法签名 | `Build(object sender, List<ParameterBinding>? parameterBindings)` | `Build(object sender, List<ParameterBinding>? parameterBindings, Func<object, string, object?>? controlValueResolver)` |
| WinForms 依赖 | 有（`FindParentFormReflection`、`FindControlByNameReflection`、`GetControlValueReflection`、ListView 特殊处理） | 无（通过 `controlValueResolver` 委托注入） |
| 诊断 | 有 `EventBindingDiagnosticContext.Record` 调用 | 无 |

**结论**：Invoke 版本有 WinForms 反射依赖，EventFramework 版本通过委托解耦。Invoke 版本是 EventFramework 版本的特化（预置了 WinForms 反射解析逻辑）。

### 2.5 EventBindingDeclaration

| 维度 | Invoke 版本 | EventFramework 版本 |
|------|-------------|-------------------|
| 命名空间 | `Share.EventBindingOrchestrator.Parameters` | `Modules.EventFramework.Models` |
| 属性名 | `ControlName` | `TargetName` |
| Name 格式 | `{ControlName}.{EventName}` | `{TargetName}.{EventName}` |
| ParameterMapping 别名 | `using ParameterMapping = ParameterResolver.Abstractions.ParameterMapping` | `using ParameterResolver.Abstractions`（直接使用） |

**结论**：属性名不同（`ControlName` vs `TargetName`），这是最大的兼容性问题。

### 2.6 EventBindingInfo

| 维度 | Invoke 版本 | EventFramework 版本 |
|------|-------------|-------------------|
| 命名空间 | `Share.EventBindingOrchestrator.Models` | `Modules.EventFramework.Models` |
| 接口 | `Share.EventBindingOrchestrator.Abstractions.IEventBindingInfo` | `Modules.EventFramework.Abstractions.IEventBindingInfo` |
| 接口定义 | 相同（FunctionName、EventType、Inputs、Output、FunctionNames） | 相同 |

**结论**：两个接口定义完全相同，但属于不同命名空间。

### 2.7 EventRequest

| 维度 | Invoke 版本 | EventFramework 版本 |
|------|-------------|-------------------|
| 命名空间 | `Share.EventBindingOrchestrator.Parameters` | `Modules.EventFramework.Models` |
| 接口 | 实现 `IDeclaration` | 纯 DTO（无接口） |
| 属性名 | `ControlName` | `TargetName` |
| 额外方法 | 有 `FromResize` | 无 |
| 序列化 | 有 `[JsonIgnore]`、`[JsonPropertyName]` 特性 | 无 |
| 属性 setter | `{ get; set; }`（可变） | `{ get; init; }`（不可变） |

**结论**：Invoke 版本功能更丰富（IDeclaration、FromResize、序列化特性），EventFramework 版本更简洁。

### 2.8 WorkflowDefinitionEntry

| 维度 | Invoke 版本 | EventFramework 版本 |
|------|-------------|-------------------|
| 命名空间 | `Share.EventBindingOrchestrator.Parameters.Binding` | `Modules.EventFramework.Models` |
| 属性 setter | `{ get; set; }`（可变） | `{ get; init; }`（不可变） |
| 方法 | 有 `ToStepDefinition`（在 `WorkflowNodeEntry` 上） | 有 `ToExecutionPlan`（在 `WorkflowDefinitionEntry` 上）+ `ToStepDefinition`（在 `WorkflowNodeEntry` 上） |

**结论**：EventFramework 版本多一个 `ToExecutionPlan` 方法。

---

## 3. 外部引用分析

### 3.1 引用 Invoke 类型的外部文件

| 类型 | 被引用方 |
|------|---------|
| `EventBindingDeclaration` | `EventBindingContext.cs`、`EventBindingResolver.cs`、`EventBindingStageBase.cs`、`EventTriggerStage.cs`、`ControlLookupStage.cs`、`ConfigBindingStage.cs`、`DeclarationBindingStage.cs`、`BindingMetadataTransformer.cs`、`ControlFinderTransformer.cs`、`EventBindingPipelineFactory.cs`、`GenericEventBindingPipeline.cs` |
| `EventBindingInfo` | `EventBindingResolver.cs`、`EventBindingStageBase.cs`、`EventTriggerStage.cs`、`ControlLookupStage.cs`、`ConfigBindingStage.cs`、`DeclarationBindingStage.cs`、`DiagnosticsScopeStage.cs`、`EmptyViewGuardStage.cs`、`StatisticsCollectStage.cs`、`ControlTreeWalkerTransformer.cs`、`EventBindingOrchestrator.cs`、`FrameworkContext.cs`、`EventTriggerServiceBase.cs`、`IEventTriggerService.cs`、`ControlValueAccessorBase.cs`（Operation 层） |
| `EventRequest` | 测试文件（`EventPipelineRegistryTests.cs`）、`ControlValueAccessorBase.cs`（Operation 层） |
| `WorkflowDefinitionEntry` | `EventBindingContext.cs`、`BindingMetadataTransformer.cs`、`EventBindingStageBase.cs`、`ConfigBindingStage.cs`、`DeclarationBindingStage.cs` |
| `ControlTreeWalker` | `ControlTreeWalkerTransformer.cs`、`EventBindingOrchestrator.cs`、`EventBindingResolver.cs`、`ControlFinderTransformer.cs`、`GenericEventBindingPipeline.cs`、`FileDriven.UI/Core/MainForm.cs`、`FileDriven.Pipeline/Contracts/ControlTreeWalker.cs` |

### 3.2 引用链复杂度

```
Invoke/EventBindingDeclaration (Share.EventBindingOrchestrator.Parameters)
  ├── EventBindingContext.CurrentDeclaration (属性类型)
  ├── EventBindingResolver (IDeclarationResolver 泛型参数)
  ├── EventBindingStageBase (CreateEventHandler 参数类型)
  ├── EventTriggerStage (GetEventType 返回值)
  ├── ControlLookupStage (GetControlName 返回值)
  ├── ConfigBindingStage (Process 中读取)
  ├── DeclarationBindingStage (Process 中读取)
  ├── BindingMetadataTransformer (创建实例)
  ├── ControlFinderTransformer (创建实例)
  ├── EventBindingPipelineFactory (创建实例)
  └── GenericEventBindingPipeline (创建实例)
```

---

## 4. 重构方案

### 4.1 方案选择

由于 Invoke 中的类型被大量外部引用，且存在属性名差异（`ControlName` vs `TargetName`），**不建议直接删除 Invoke 文件**。采用以下分层策略：

#### 方案 A：委托包装（推荐）

将 Invoke 中的 8 个 A 类文件改为"委托包装器"——保留类定义和命名空间，内部实现委托给 EventFramework 版本。

**优点**：
- 零外部引用修改
- 渐进式迁移
- 编译安全

**缺点**：
- 代码量未减少（但消除了重复逻辑）

#### 方案 B：using alias + 补充方法

在 EventFramework 中补充 Invoke 特有的方法，然后在 Invoke 中通过 using alias 引用。

**优点**：
- 真正消除重复代码
- EventFramework 更完善

**缺点**：
- 属性名差异（`ControlName` vs `TargetName`）导致编译错误
- 需要修改大量引用方

### 4.2 推荐方案：方案 A（委托包装）

#### 4.2.1 ControlTreeWalker → 委托给 Modules.EventFramework.ControlTreeWalker

```csharp
// Invoke/ControlTreeWalker.cs — 改为委托包装
namespace Share.EventBindingOrchestrator.Utils;

public static class ControlTreeWalker
{
    // 委托给 EventFramework 版本
    public static IEnumerable<ControlConfig> GetDirectChildren(ControlConfig config)
        => Modules.EventFramework.ControlTreeWalker.GetDirectChildren(config);

    public static void WalkAllViews(UIConfiguration config, Action<ControlConfig> action)
        => Modules.EventFramework.ControlTreeWalker.WalkAllViews(config, action);

    public static void WalkControls(List<ControlConfig> controls, Action<ControlConfig> action)
        => Modules.EventFramework.ControlTreeWalker.WalkControls(controls, action);

    public static async Task WalkControlsAsync(List<ControlConfig> controls, Func<ControlConfig, Task> asyncAction)
        => await Modules.EventFramework.ControlTreeWalker.WalkControlsAsync(controls, asyncAction);

    public static ControlConfig? FindControl(UIConfiguration config, string name)
        => Modules.EventFramework.ControlTreeWalker.FindControl(config, name);

    public static Dictionary<string, ControlConfig> BuildControlMap(UIConfiguration config)
        => Modules.EventFramework.ControlTreeWalker.BuildControlMap(config);

    // Invoke 特有方法（保留）
    public static async Task WalkAllViewsAsync(UIConfiguration config, Func<ControlConfig, Task> asyncAction) { ... }
    public static List<string> CollectControlNames(UIConfiguration config, Func<ControlConfig, bool>? predicate = null) { ... }
    public static ControlConfig? FindControl(List<ControlConfig> controls, string name) { ... }
    public static List<string> CollectNames(List<ControlConfig> controls, List<string>? names = null) { ... }
}
```

#### 4.2.2 DefaultEventMappingRegistry → 保留现状

两个版本实现差异较大，Invoke 版本较简单，EventFramework 版本更完善（支持优先级排序）。**建议保留现状**，后续统一迁移到 EventFramework 版本。

#### 4.2.3 EventBindingValidator → 委托包装

```csharp
// Invoke/EventBindingValidator.cs — 改为委托包装
namespace Share.EventBindingOrchestrator.Parameters;

public class EventBindingValidator
{
    private readonly Modules.EventFramework.EventBindingValidator _inner;

    public EventBindingValidator()
        => _inner = new Modules.EventFramework.EventBindingValidator();

    public EventBindingValidator(DeclarationValidator<EventBindingDeclaration> validator)
        => _inner = new Modules.EventFramework.EventBindingValidator(validator);

    public ValidationResult Validate(EventBindingDeclaration declaration)
        => _inner.Validate(declaration);

    public IReadOnlyList<ValidationResult> ValidateAll(IEnumerable<EventBindingDeclaration> declarations)
        => _inner.ValidateAll(declarations);

    public void ValidateOrThrow(EventBindingDeclaration declaration)
        => _inner.ValidateOrThrow(declaration);

    // Invoke 特有方法（保留）
    public IReadOnlyList<string> ValidateAllWithErrors(IEnumerable<EventBindingDeclaration> declarations) { ... }
}
```

#### 4.2.4 ParameterBuilder → 保留现状

Invoke 版本有 WinForms 反射依赖，EventFramework 版本通过委托解耦。**建议保留现状**，Invoke 版本作为 WinForms 特化实现。

#### 4.2.5 EventBindingDeclaration → 保留现状

属性名差异（`ControlName` vs `TargetName`）导致无法直接替换。**建议保留现状**，后续统一属性名后再迁移。

#### 4.2.6 EventBindingInfo → 保留现状

接口命名空间不同。**建议保留现状**，后续统一接口后再迁移。

#### 4.2.7 EventRequest → 保留现状

Invoke 版本功能更丰富（IDeclaration、FromResize、序列化特性）。**建议保留现状**，后续将缺失功能补充到 EventFramework 版本后再迁移。

#### 4.2.8 WorkflowDefinitionEntry → 保留现状

命名空间不同，且被 `EventBindingContext.cs` 直接引用。**建议保留现状**。

### 4.3 实施步骤总结

| 步骤 | 文件 | 操作 | 风险 |
|------|------|------|------|
| 1 | `ControlTreeWalker.cs` | 改为委托包装 | 低 |
| 2 | `EventBindingValidator.cs` | 改为委托包装 | 低 |
| 3 | `DefaultEventMappingRegistry.cs` | 保留现状 | - |
| 4 | `ParameterBuilder.cs` | 保留现状 | - |
| 5 | `EventBindingDeclaration.cs` | 保留现状 | - |
| 6 | `EventBindingInfo.cs` | 保留现状 | - |
| 7 | `EventRequest.cs` | 保留现状 | - |
| 8 | `WorkflowDefinitionEntry.cs` | 保留现状 | - |

### 4.4 后续迁移路线图

```
阶段 1（当前）：委托包装 ControlTreeWalker 和 EventBindingValidator
    ↓
阶段 2：统一 EventBindingDeclaration 属性名（ControlName → TargetName）
    ↓
阶段 3：统一 IEventBindingInfo 接口命名空间
    ↓
阶段 4：将 EventRequest 缺失功能补充到 EventFramework 版本
    ↓
阶段 5：删除 Invoke 中所有委托包装文件，引用方直接使用 EventFramework 版本
```

---

## 5. 风险与注意事项

### 5.1 编译风险
- `EventBindingDeclaration` 的 `ControlName` vs `TargetName` 属性差异会导致 `EventBindingContext.cs` 等 11 个文件编译错误
- `IEventBindingInfo` 接口命名空间不同，但定义相同，可通过 using alias 解决

### 5.2 运行时风险
- 委托包装增加一层调用，性能影响可忽略
- `ControlTreeWalker` 的 `Lazy<TreeWalker<ControlConfig>>` 实例不同，但 `TreeWalker` 是无状态的，不影响功能

### 5.3 测试风险
- 测试文件直接引用 `EventBindingInfo`（`new EventBindingInfo(...)`），修改后需更新 using
- 测试文件直接引用 `EventRequest`（`EventRequest.FromConfig(...)`），修改后需更新 using

---

## 6. 附录

### 6.1 文件路径

```
Invoke 目录：CsharpPrj/Share/EventBindingOrchestrator/Invoke/
EventFramework 目录：CsharpPrj/Modules/EventFramework/
项目文件：CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.csproj
```

### 6.2 关键接口定义

```csharp
// Share 层接口
namespace Share.EventBindingOrchestrator.Abstractions;
public interface IEventBindingInfo
{
    string FunctionName { get; }
    string EventType { get; }
    IReadOnlyList<string> Inputs { get; }
    string? Output { get; }
    IReadOnlyList<string> FunctionNames { get; }
}

// Modules 层接口（定义完全相同）
namespace Modules.EventFramework.Abstractions;
public interface IEventBindingInfo
{
    string FunctionName { get; }
    string EventType { get; }
    IReadOnlyList<string> Inputs { get; }
    string? Output { get; }
    IReadOnlyList<string> FunctionNames { get; }
}
```

### 6.3 外部引用文件清单

需要关注的外部文件（不在 Invoke 目录内，但引用了 Invoke 类型）：

```
CsharpPrj/Share/EventBindingOrchestrator/Contexts/EventBindingContext.cs
CsharpPrj/Share/EventBindingOrchestrator/Stages/ConfigBindingStage.cs
CsharpPrj/Share/EventBindingOrchestrator/Stages/DeclarationBindingStage.cs
CsharpPrj/Share/EventBindingOrchestrator/Stages/DiagnosticsScopeStage.cs
CsharpPrj/Share/EventBindingOrchestrator/Stages/EmptyViewGuardStage.cs
CsharpPrj/Share/EventBindingOrchestrator/Stages/StatisticsCollectStage.cs
CsharpPrj/Share/EventBindingOrchestrator/Transformers/BindingMetadataTransformer.cs
CsharpPrj/Share/EventBindingOrchestrator/Transformers/ControlFinderTransformer.cs
CsharpPrj/Share/EventBindingOrchestrator/Transformers/EventBindingPipelineFactory.cs
CsharpPrj/Share/EventBindingOrchestrator/Transformers/GenericEventBindingPipeline.cs
CsharpPrj/Share/EventBindingOrchestrator/Controls/ControlTreeWalkerTransformer.cs
CsharpPrj/Share/EventBindingOrchestrator/EventBindingOrchestrator.cs
CsharpPrj/Share/EventBindingOrchestrator/Abstractions/EventTriggerServiceBase.cs
CsharpPrj/Share/EventBindingOrchestrator/Abstractions/IEventTriggerService.cs
CsharpPrj/Share/EventBindingOrchestrator/Contexts/FrameworkContext.cs
CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/ControlValueAccessorBase.cs
CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/EventTriggerFrameworkInitializer.cs
CsharpPrj/Operation/BackEndDriven/EventTriggerFramework/FrameworkContext.cs
CsharpPrj/Operation/WdUI/WinForms/EventBindingContextExtensions.cs
FileDriven/FileDriven.UI/Core/MainForm.cs
FileDriven/FileDriven.Pipeline/Contracts/ControlTreeWalker.cs
CentralTest/CSharpTest/Operation.Tests/EventTriggerFramework.Tests/EventBindingCoreTests.cs
CentralTest/CSharpTest/Operation.Tests/EventTriggerFramework.Tests/FrameworkInitializerIntegrationTests.cs
CentralTest/CSharpTest/Operation.Tests/EventTriggerFramework.Tests/EventPipelineRegistryTests.cs
```
