# FileDriven.Pipeline CommandFramework 依赖分析

> 分析 FileDriven.Pipeline 中每个文件与 CommandFramework 的依赖关系，
> 区分"依赖 CommandFramework 的部分"和"不依赖 CommandFramework 的部分"，
> 为重构提供依据。

---

## 文件清单（共 7 个源文件）

| # | 文件 | 行数 | 依赖 CommandFramework? |
|---|------|------|----------------------|
| 1 | `ControlHelper.cs` | 66 | ❌ 否 |
| 2 | `EventCore.cs` | 460 | ❌ 否 |
| 3 | `NonEventCommands.cs` | 44 | ❌ 否 |
| 4 | `FileDrivenPipelineContext.cs` | 371 | ✅ 是 |
| 5 | `PipelineFactory.cs` | 194 | ✅ 是 |
| 6 | `PipelineEntry.cs` | 538 | ✅ 是 |
| 7 | `Skeletons/ControlTreeWalker.cs` | 104 | ❌ 否 |
| 8 | `Skeletons/NonEventCommandHandler.cs` | 489 | ✅ 是 |
| 9 | `Skeletons/ScriptLoadSkeleton.cs` | 103 | ✅ 是 |

---

## 详细分析

### 1. ControlHelper.cs — ❌ 不依赖 CommandFramework

```csharp
using System.Windows.Forms;
namespace FileDriven.Pipeline.Helpers;
```

- 仅依赖 `System.Windows.Forms`
- 纯工具类，递归查找控件
- **无需迁移**

### 2. EventCore.cs — ❌ 不依赖 CommandFramework

```csharp
using BasicMethod.ConfigurationModels;
using EventTriggerFramework.Abstractions;
namespace FileDriven.Pipeline.Event;
```

- 依赖 `BasicMethod.ConfigurationModels`（`UIConfiguration`、`ControlConfig`）
- 依赖 `EventTriggerFramework.Abstractions`（`IEventBindingResolver`、`IEventBindingInfo`）
- **不依赖 CommandFramework 的任何类型**
- **无需迁移**

### 3. NonEventCommands.cs — ❌ 不依赖 CommandFramework

```csharp
using System.Collections.Frozen;
namespace FileDriven.Pipeline;
```

- 仅依赖 `System.Collections.Frozen`
- 纯数据类，定义非事件命令列表
- **无需迁移**

### 4. FileDrivenPipelineContext.cs — ✅ 依赖 CommandFramework

```csharp
using CommandFramework.Abstractions;   // IFunctionExecutor, IEventBindingResolver
using CommandFramework.Models;         // ParsedCommand, CommandFileLineResult
using CommandFramework.Pipeline;       // CommandContext（父类）
```

**依赖的 CommandFramework 类型：**
| 类型 | 用途 | 可否替代？ |
|------|------|-----------|
| `CommandContext` | 父类（继承） | ❌ 必须 |
| `IFunctionExecutor` | 属性类型 | ❌ 必须 |
| `IEventBindingResolver` | 属性类型 | ❌ 必须 |
| `ParsedCommand` | 便捷属性映射 | ❌ 必须 |
| `CommandFileLineResult` | `CommandResults` 列表元素 | ❌ 必须 |

**结论：** 整个类深度绑定 CommandFramework，**必须保留在能引用 CommandFramework 的项目中**。

### 5. PipelineFactory.cs — ✅ 依赖 CommandFramework

```csharp
using CommandFramework.Abstractions;   // IFunctionExecutor, IEventBindingResolver
using CommandFramework.Models;         // CommandFileLineResult
using CommandFramework.Pipeline;       // PipelineEntry.CreateDefaultPipeline()
```

**依赖的 CommandFramework 类型：**
| 类型 | 用途 | 可否替代？ |
|------|------|-----------|
| `IFunctionExecutor` | 参数/泛型 | ❌ 必须 |
| `IEventBindingResolver` | 参数 | ❌ 必须 |
| `CommandContext` | 类型转换 `(CommandContext)ctx` | ❌ 必须 |
| `CommandFileLineResult` | `ctx.CommandResults.Add(...)` | ❌ 必须 |
| `CommandFramework.Pipeline.PipelineEntry.CreateDefaultPipeline()` | 创建 StagePipeline | ❌ 必须 |
| `FileDrivenPipelineContext` | 泛型参数 `CentralPipeline<FileDrivenPipelineContext>` | ❌ 必须 |

**结论：** 整个类深度绑定 CommandFramework + FileDrivenPipelineContext，**必须保留在能引用两者的项目中**。

### 6. PipelineEntry.cs — ✅ 依赖 CommandFramework

```csharp
using CommandFramework.Abstractions;   // IFunctionExecutor
using CommandFramework.Models;         // CommandFileLineResult
using CommandFramework.Pipeline;       // CommandContext
```

**依赖的 CommandFramework 类型：**
| 类型 | 用途 | 可否替代？ |
|------|------|-----------|
| `IFunctionExecutor` | 字段/属性/构造函数参数 | ❌ 必须 |
| `CommandContext` | 类型转换 `(CommandContext)context` | ❌ 必须 |
| `CommandFileLineResult` | `BuildResults()` 返回类型 | ❌ 必须 |
| `FileDrivenPipelineContext` | `CreateContext()` 返回类型 / `CreatePipeline()` 泛型参数 | ❌ 必须 |

**结论：** 整个类深度绑定 CommandFramework + FileDrivenPipelineContext，**必须保留在能引用两者的项目中**。

### 7. Skeletons/ControlTreeWalker.cs — ❌ 不依赖 CommandFramework

```csharp
using BasicMethod.ConfigurationModels;
namespace FileDriven.Pipeline.Skeletons;
```

- 仅依赖 `BasicMethod.ConfigurationModels`（`UIConfiguration`、`ControlConfig`）
- **不依赖 CommandFramework 的任何类型**
- **无需迁移**

### 8. Skeletons/NonEventCommandHandler.cs — ✅ 依赖 CommandFramework

```csharp
using BasicMethod.ConfigurationModels;
using FileDriven.Pipeline.Event;
namespace FileDriven.Pipeline.Skeletons;
```

**表面上不直接 using CommandFramework**，但所有方法的参数类型都是 `FileDrivenPipelineContext`：

```csharp
public static void Handle(FileDrivenPipelineContext context)
internal static void HandleLoad(FileDrivenPipelineContext context)
// ... 所有 HandleXxx 方法都依赖 FileDrivenPipelineContext
```

而 `FileDrivenPipelineContext` 继承自 `CommandContext`，且使用了 `ParsedCommand`、`CommandFileLineResult` 等 CommandFramework 类型。

**结论：** 通过 `FileDrivenPipelineContext` 间接依赖 CommandFramework。**如果 FileDrivenPipelineContext 迁移，此文件也需要迁移或改造**。

### 9. Skeletons/ScriptLoadSkeleton.cs — ✅ 依赖 CommandFramework

```csharp
using FileDriven.CLI;
using FileDriven.Logging;
using SkeletonFlow;
namespace FileDriven.Pipeline.Skeletons;
```

**表面上不直接 using CommandFramework**，但：

```csharp
public class ScriptLoadSkeleton : SyncSkeletonBase<FileDrivenPipelineContext, FileDrivenPipelineContext>
```

整个类的泛型参数绑定到 `FileDrivenPipelineContext`。

**结论：** 通过 `FileDrivenPipelineContext` 间接依赖 CommandFramework。**如果 FileDrivenPipelineContext 迁移，此文件也需要迁移或改造**。

---

## 总结

### 不依赖 CommandFramework 的文件（可保留在 Pipeline）

| 文件 | 说明 |
|------|------|
| `ControlHelper.cs` | 纯 WinForms 控件查找工具 |
| `EventCore.cs` | 事件核心类型（LoadedConfigInfo、EventBindingResolver 等） |
| `NonEventCommands.cs` | 非事件命令列表定义 |
| `Skeletons/ControlTreeWalker.cs` | 控件树遍历工具 |

### 依赖 CommandFramework 的文件（需要处理）

| 文件 | 依赖方式 | 处理方案 |
|------|---------|---------|
| `FileDrivenPipelineContext.cs` | 直接 using + 继承 CommandContext | **迁移到 Execution** |
| `PipelineFactory.cs` | 直接 using + 泛型绑定 FileDrivenPipelineContext | **随 FileDrivenPipelineContext 迁移** |
| `PipelineEntry.cs` | 直接 using + 泛型绑定 FileDrivenPipelineContext | **随 FileDrivenPipelineContext 迁移** |
| `Skeletons/NonEventCommandHandler.cs` | 间接通过 FileDrivenPipelineContext | **随 FileDrivenPipelineContext 迁移** |
| `Skeletons/ScriptLoadSkeleton.cs` | 间接通过 FileDrivenPipelineContext | **随 FileDrivenPipelineContext 迁移** |

### 核心结论

**FileDrivenPipelineContext 是整个依赖链的枢纽**。所有依赖 CommandFramework 的文件都直接或间接通过它绑定。

如果 `FileDrivenPipelineContext` 迁移到 `FileDriven.Execution`，那么：
- `PipelineFactory.cs`、`PipelineEntry.cs`、`NonEventCommandHandler.cs`、`ScriptLoadSkeleton.cs` 也必须迁移
- 迁移后，`FileDriven.Pipeline` 只剩下 4 个不依赖 CommandFramework 的文件
- `FileDriven.Pipeline.csproj` 可以移除 `CommandFramework` 引用
