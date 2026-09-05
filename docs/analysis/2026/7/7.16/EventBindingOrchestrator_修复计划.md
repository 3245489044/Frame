# EventBindingOrchestrator 编译错误修复计划

> **基于**：`EventBindingOrchestrator_编译错误分析.md`
> **状态**：`BindingResult` 相关错误已修复（0 个），剩余 91 个错误
> **日期**：2026-07-15

---

## 错误分类总览

| 类别 | 错误类型 | 数量 | 优先级 | 涉及文件 |
|------|---------|------|--------|---------|
| 1 | `EventBindingDiagnosticContext` 不存在 | ~50 | P0 | 12 个文件 |
| 2 | `EventBindingHelper` 不存在 | 3 | P2 | ConfigBindingStage, DeclarationBindingStage |
| 3 | `EventBinding.Output`/`OutputMapping`/`Workflows` 不存在 | 6 | P1 | EventBindingHelper, BindingMetadataTransformer, EventBindingOrchestrator |
| 4 | `ControlConfig.Panel1Controls`/`Panel2Controls`/`ExtensionData` 不存在 | 4 | P1 | ControlTreeWalker, EventBindingHelper |
| 5 | `IEventBindingResolver.FindEventBinding` 不存在 | 4 | P1 | EventTriggerService, EventBindingService |
| 6 | 杂项错误（泛型不匹配、类型缺失等） | ~24 | P2 | 多个文件 |

---

## 第 1 类：`EventBindingDiagnosticContext` 不存在（P0）

### 根因
`EventBindingDiagnosticContext` 类在 `Share.EventBindingOrchestrator.Diagnostics` 命名空间中被引用，但该类可能已被删除或引用了已删除的类型。

### 修复方案
创建 `EventBindingDiagnosticContext` 的**最小化 stub 实现**，仅保留被调用的静态方法签名，内部为空实现。诊断日志是辅助功能，不影响核心业务逻辑。

### 涉及文件
| 文件 | 行号 |
|------|------|
| `EventBindingStageBase.cs` | 73, 77, 85, 92, 107, 119, 132, 148, 159, 202, 212, 224, 233, 286, 297, 320, 345 |
| `EventBindingHelper.cs` | 48 |
| `EventBindingOrchestrator.cs` | 161, 183, 191, 238, 650, 663 |
| `ConfigBindingStage.cs` | 69, 97, 128 |
| `DeclarationBindingStage.cs` | 67 |
| `ControlFinderTransformer.cs` | 96 |
| `GenericEventBindingPipeline.cs` | 137 |
| `EventBusStages.cs` | 170, 208, 322, 377 |
| `EventBindingPipelineFactory.cs` | 143, 155, 164 |
| `ControlLookupStage.cs` | 76, 85 |
| `EventTriggerStage.cs` | 75, 84 |
| `EmptyViewGuardStage.cs` | 60, 73 |
| `DiagnosticsScopeStage.cs` | 48, 54 |
| `BindingMetadataTransformer.cs` | 127, 135, 207 |

### 具体修改

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Diagnostics/EventBindingDiagnosticContext.cs`

```csharp
namespace Share.EventBindingOrchestrator.Diagnostics
{
    public static class EventBindingDiagnosticContext
    {
        public static IDisposable BeginScope() => new Scope();
        public static void Record(string eventName, object data) { }
        public static object CaptureSnapshot() => new object();
        public static string GetErrorReport(this object errors) => string.Empty;

        private class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
```

---

## 第 2 类：`EventBindingHelper` 不存在（P2）

### 根因
`EventBindingHelper` 类在 `Share.EventBindingOrchestrator.Parameters` 命名空间中，但引用了 `EventBindingDiagnosticContext`（第 1 类修复后自动解决）和 `EventBinding.Output`/`OutputMapping`/`ExtensionData`（第 3/4 类修复后自动解决）。

### 修复方案
修复第 1/3/4 类后，`EventBindingHelper` 的错误会自动消失。如果仍有问题，检查 `EventBindingHelper.cs` 中的 `using` 语句是否正确。

---

## 第 3 类：`EventBinding.Output`/`OutputMapping`/`Workflows` 不存在（P1）

### 根因
TConfig 的 `EventBinding` 类缺少 `Output`、`OutputMapping`、`Workflows` 属性。

### 修复方案
在 TConfig 的 `EventBinding` 类中添加这些属性。

### 涉及文件
| 文件 | 行号 | 缺失属性 |
|------|------|---------|
| `EventBindingHelper.cs` | 51, 64 | `Output` |
| `EventBindingHelper.cs` | 57, 58 | `OutputMapping` |
| `BindingMetadataTransformer.cs` | 111, 114 | `Workflows` |
| `BindingMetadataTransformer.cs` | 202 | `Output` |
| `EventBindingOrchestrator.cs` | 332 | `Output` |

### 具体修改

**文件**：`CsharpPrj/Plate/TConfig/Contexts/EventBinding.cs`

```csharp
// 在 EventBinding 类中添加以下属性
public string? Output { get; set; }
public Dictionary<string, string>? OutputMapping { get; set; }
public Dictionary<string, WorkflowDefinitionEntry>? Workflows { get; set; }
```

---

## 第 4 类：`ControlConfig.Panel1Controls`/`Panel2Controls`/`ExtensionData` 不存在（P1）

### 根因
TConfig 的 `ControlConfig` 类缺少 `Panel1Controls`、`Panel2Controls`、`ExtensionData` 属性。

### 修复方案
在 TConfig 的 `ControlConfig` 类中添加这些属性。

### 涉及文件
| 文件 | 行号 | 缺失属性 |
|------|------|---------|
| `ControlTreeWalker.cs` | 46, 47 | `Panel1Controls` |
| `ControlTreeWalker.cs` | 50, 51 | `Panel2Controls` |
| `EventBindingHelper.cs` | 96, 99 | `ExtensionData` |

### 具体修改

**文件**：`CsharpPrj/Plate/TConfig/Contexts/ControlConfig.cs`

```csharp
// 在 ControlConfig 类中添加以下属性
public List<ControlConfig>? Panel1Controls { get; set; }
public List<ControlConfig>? Panel2Controls { get; set; }
public Dictionary<string, string>? ExtensionData { get; set; }
```

---

## 第 5 类：`IEventBindingResolver.FindEventBinding` 不存在（P1）

### 根因
TConfig 的 `IEventBindingResolver` 接口缺少 `FindEventBinding` 方法。

### 修复方案
在 TConfig 的 `IEventBindingResolver` 接口中添加 `FindEventBinding` 方法。

### 涉及文件
| 文件 | 行号 |
|------|------|
| `EventTriggerService.cs` | 73, 137 |
| `EventBindingService.cs` | 64, 119 |

### 具体修改

**文件**：`CsharpPrj/Plate/TConfig/Contexts/IEventBindingResolver.cs`

```csharp
// 在 IEventBindingResolver 接口中添加以下方法
EventBinding? FindEventBinding(string sourceName, string? sourceType = null);
```

---

## 第 6 类：杂项错误（P2）

### 6.1 `DefaultEventNameNormalizer` 找不到
- **文件**：`DefaultEventMappingRegistry.cs:40`
- **修复**：添加 `using Modules.EventFramework.Core;` 或创建 stub

### 6.2 `EventBindingContext.CurrentControls` 不存在
- **文件**：`ControlTreeWalkerTransformer.cs:36,42`
- **修复**：在 `EventBindingContext` 中添加 `CurrentControls` 属性，或修改调用方

### 6.3 `EventBindingDeclaration` 泛型参数不匹配
- **文件**：`BindingMetadataTransformer.cs:46,56`、`EventBindingResolver.cs:155,190`
- **修复**：统一使用 `TConfig.ParameterMapping` 或 `ParameterResolver.Abstractions.ParameterMapping`

### 6.4 `StorageCell` 找不到
- **文件**：`EventBusStages.cs:51`、`EventBindingResolverStages.cs:49`
- **修复**：添加 `using Storage;` 或创建 stub

### 6.5 `IEventExecutionService.RegisterWorkflows` 不存在
- **文件**：`EventBindingOrchestrator.cs:182`
- **修复**：在 `IEventExecutionService` 接口中添加 `RegisterWorkflows` 方法

### 6.6 `ExecutionResult.Status` 不存在
- **文件**：`EventBindingStageBase.cs:340`
- **修复**：在 `ExecutionResult` 类中添加 `Status` 属性

### 6.7 `ControlBindingInfo.SkipReason` 不存在
- **文件**：`StatisticsCollectStage.cs:87,88`
- **修复**：在 `ControlBindingInfo` 类中添加 `SkipReason` 属性

### 6.8 `object.Key`/`Value` 不存在
- **文件**：`BindingMetadataTransformer.cs:121,122`
- **修复**：将 `object` 显式转换为 `KeyValuePair<string, object>` 或 `DictionaryEntry`

### 6.9 `??` 运算符类型不匹配
- **文件**：`BindingMetadataTransformer.cs:160`
- **修复**：将 `List<object>` 改为 `Dictionary<string, WorkflowDefinitionEntry>`

---

## 修复执行顺序

```
Step 1: 创建 EventBindingDiagnosticContext stub（P0，消除 ~50 个错误）
Step 2: 在 TConfig 中添加缺失属性（P1，消除 ~14 个错误）
  2a: EventBinding.Output / OutputMapping / Workflows
  2b: ControlConfig.Panel1Controls / Panel2Controls / ExtensionData
  2c: IEventBindingResolver.FindEventBinding
  2d: ControlBindingInfo.SkipReason
  2e: ExecutionResult.Status
Step 3: 修复杂项错误（P2，消除 ~24 个错误）
  3a: DefaultEventNameNormalizer using
  3b: EventBindingContext.CurrentControls
  3c: 泛型参数统一
  3d: StorageCell using
  3e: IEventExecutionService.RegisterWorkflows
  3f: object 显式类型转换
  3g: ?? 运算符类型修复
Step 4: 构建验证
```

---

## 预期结果

| 步骤 | 消除错误数 | 累计消除 | 剩余错误 |
|------|-----------|---------|---------|
| Step 1 | ~50 | ~50 | ~41 |
| Step 2 | ~14 | ~64 | ~27 |
| Step 3 | ~24 | ~88 | ~3 |
| Step 4 | 验证 | ~91 | 0 |
