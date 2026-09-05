# Skeletons.cs 重复逻辑与冗余分析报告

> 分析日期：2026-05-27
> 分析目标：`FileDriven/FileDriven.Logging/Skeletons.cs`（1419 行）
> 关联文件：`Wrappers.cs`、`LogPipeline.cs`、`Models.cs`

---

## 一、已消除的重复（当前代码已做的优化）

### 1.1 公共基类提取 ✅

| 基类 | 被继承者 | 共享字段 |
|------|---------|---------|
| `CommandInputBase` | `CommandSetInput`, `CommandClickInput` | `Target`, `Parameters`, `VirtualStore`, `EventTriggerService` |
| `CommandOutputBase` | `CommandSetOutput`, `CommandClickOutput`, `CommandGetOutput` | `Success`, `Message` |
| `FrameworkInitState` | `FrameworkInitInput`, `FrameworkInitOutput` | `WdUIInitialized`, `BuiltInFunctionsInitialized`, `TemplateEngineInitialized`, `PathAliasesLoaded` |

### 1.2 泛型化诊断日志 ✅

`DiagnosticLogSkeleton<TInput>` 通过 `Action<TInput, SkeletonContext>` 回调，将 6 个结构相同的诊断日志 Skeleton 合并为一个泛型类。

### 1.3 LogEntryBuilder 消除 new LogEntry 重复 ✅

`LogEntryBuilder` 提供了 `Info/Warn/Error/Debug` 静态方法，消除了各处 `new LogEntry { ... }` 的重复构造代码。

### 1.4 FileDrivenPipeline.BuildBusinessPipeline 消除重复 ✅

`Create()` 和 `CreateWithExceptionHandling()` 共享 `BuildBusinessPipeline()` 私有方法，消除了业务管线构建的重复。

### 1.5 CliModeRouterSkeleton.ValidateInput 消除同步/异步验证重复 ✅

`ExecuteCore` 和 `ExecuteCoreAsync` 共享 `ValidateInput()` 静态方法。

---

## 二、仍然存在的重复逻辑

### 2.1 诊断日志工厂中的 LogEntryBuilder 调用模式重复

**位置：** `DiagnosticLogFactories` 类（第 1209-1372 行）

**问题：** 6 个工厂方法中，每个方法都重复了以下模式：
```csharp
LogEntryBuilder.Info(ctx, LogCategory.XXX, "ModuleName", $"message {field}");
```

**具体重复：**
- 每个工厂方法都重复 `new DiagnosticLogSkeleton<TInput>(name, description, (input, ctx) => { ... })` 的构造模式
- 每个回调内部都重复 `if (!string.IsNullOrEmpty(input.Field))` 的空值检查 + `LogEntryBuilder.Info/Error` 调用

**提取建议：**
```csharp
// 提取通用日志记录辅助方法
public static class DiagnosticLogHelper
{
    public static void LogIfNotEmpty(SkeletonContext ctx, LogCategory category, 
        string module, string? value, string prefix)
    {
        if (!string.IsNullOrEmpty(value))
            LogEntryBuilder.Info(ctx, category, module, $"{prefix}{value}");
    }

    public static void LogErrorIfNotEmpty(SkeletonContext ctx, LogCategory category,
        string module, string? error, string prefix)
    {
        if (!string.IsNullOrEmpty(error))
            LogEntryBuilder.Error(ctx, category, module, $"{prefix}{error}");
    }
}
```

### 2.2 CommandGetSkeleton 中的条件分支重复

**位置：** 第 1039-1048 行

**问题：**
```csharp
if (value != null)
{
    LogEntryBuilder.Info(ctx, LogCategory.Command, "CommandGet", $"{input.Entity} = {value}");
    output.Message = $"{input.Entity} = {value}";
}
else
{
    LogEntryBuilder.Info(ctx, LogCategory.Command, "CommandGet", $"{input.Entity} = (null)");
    output.Message = $"{input.Entity} = (null)";
}
```

**提取建议：**
```csharp
var displayValue = value?.ToString() ?? "(null)";
LogEntryBuilder.Info(ctx, LogCategory.Command, "CommandGet", $"{input.Entity} = {displayValue}");
output.Message = $"{input.Entity} = {displayValue}";
```

### 2.3 ConfigLoadSkeleton 中的日志注入模式重复

**位置：** 第 638-660 行

**问题：** 遍历 View 和 Control 时，每个循环体内部都重复 `LogEntryBuilder.Info(ctx, LogCategory.Config, "ConfigLoad", ...)` 调用，且字符串格式化模式高度相似。

**提取建议：**
```csharp
// 提取为扩展方法
public static void LogViewInfo(this SkeletonContext ctx, ViewConfig view)
{
    LogEntryBuilder.Info(ctx, LogCategory.Config, "ConfigLoad", 
        $"View: {view.Name ?? "Unnamed"} ({view.Controls.Count} controls)");
}

public static void LogControlInfo(this SkeletonContext ctx, ControlConfig ctrl)
{
    LogEntryBuilder.Info(ctx, LogCategory.Config, "ConfigLoad", 
        $"Control: {ctrl.Name ?? "Unnamed"} ({ctrl.Type ?? "unknown"})");
}
```

### 2.4 FrameworkInitSkeleton 中的状态复制重复

**位置：** 第 492-495 行

**问题：**
```csharp
output.WdUIInitialized = input.WdUIInitialized;
output.BuiltInFunctionsInitialized = input.BuiltInFunctionsInitialized;
output.TemplateEngineInitialized = input.TemplateEngineInitialized;
output.PathAliasesLoaded = input.PathAliasesLoaded;
```

由于 `FrameworkInitInput : FrameworkInitState` 和 `FrameworkInitOutput : FrameworkInitState`，可以用自动映射替代手动逐字段复制。

**提取建议：**
```csharp
// 在 FrameworkInitState 中添加复制方法
public class FrameworkInitState
{
    // ... 现有属性 ...
    
    public void CopyTo(FrameworkInitState target)
    {
        target.WdUIInitialized = this.WdUIInitialized;
        target.BuiltInFunctionsInitialized = this.BuiltInFunctionsInitialized;
        target.TemplateEngineInitialized = this.TemplateEngineInitialized;
        target.PathAliasesLoaded = this.PathAliasesLoaded;
    }
}

// 使用：
input.CopyTo(output);
```

### 2.5 FileDrivenPipeline.Execute/ExecuteAsync/ExecuteWithExceptionHandling 三重复

**位置：** 第 347-396 行

**问题：** 三个方法的结构完全相同，仅 `Create` vs `CreateWithExceptionHandling` 不同：

```csharp
// Execute
var pipeline = Create(cliRouter, options);
var context = CreateContext(batchId, batchName, source, projectRoot);
var result = pipeline.Execute(context);
BatchManager.Instance.CompleteBatch(batchId);
return result;

// ExecuteAsync
var pipeline = Create(cliRouter, options);
var context = CreateContext(batchId, batchName, source, projectRoot);
var result = await pipeline.ExecuteAsync(context);
BatchManager.Instance.CompleteBatch(batchId);
return result;

// ExecuteWithExceptionHandling
var pipeline = CreateWithExceptionHandling(cliRouter, options);
var context = CreateContext(batchId, batchName, source, projectRoot);
var result = pipeline.Execute(context);
BatchManager.Instance.CompleteBatch(batchId);
return result;
```

**提取建议：**
```csharp
private static PipelineContext ExecuteInternal(
    Func<Flow<PipelineContext, PipelineContext>> pipelineFactory,
    string batchId, string batchName, string source, string? projectRoot,
    bool isAsync = false)
{
    var pipeline = pipelineFactory();
    var context = CreateContext(batchId, batchName, source, projectRoot);
    var result = isAsync 
        ? pipeline.ExecuteAsync(context).GetAwaiter().GetResult()
        : pipeline.Execute(context);
    BatchManager.Instance.CompleteBatch(batchId);
    return result;
}
```

### 2.6 LogPipeline.CreateWithLogCollect / CreateWithLogCollectAndExceptionHandling 重复

**位置：** `LogPipeline.cs` 第 493-549 行

**问题：** 两个方法中，前 6 行（创建 init/collect/flush/emergencyDump/tryFlush/summary）完全相同，仅业务 Skeleton 的包装方式不同。

**提取建议：**
```csharp
private static (LogInitSkeleton init, LogCollectSkeleton collect, LogTryExecuteSkeleton tryFlush, LogSummarySkeleton summary) 
    CreatePipelineComponents()
{
    var init = new LogInitSkeleton();
    var collect = new LogCollectSkeleton();
    var flush = new LogFlushSkeleton();
    var emergencyDump = new LogEmergencyDumpSkeleton();
    var tryFlush = new LogTryExecuteSkeleton(flush, emergencyDump);
    var summary = new LogSummarySkeleton();
    return (init, collect, tryFlush, summary);
}
```

---

## 三、冗余逻辑

### 3.1 LogInjectionSkeletons 与 LogPipeline 的职责重叠

**位置：** 第 1386-1415 行

**问题：** `LogInjectionSkeletons` 类中的 `CreateLogPipeline()` 和 `CreateMinimalLogPipeline()` 仅仅是 `LogPipeline.BuildDefaultPipeline()` 和 `LogPipeline.BuildMinimalPipeline()` 的委托调用，没有增加任何额外逻辑。

```csharp
public static ISkeleton<PipelineContext, PipelineContext> CreateLogPipeline()
{
    return Pipeline.LogPipeline.BuildDefaultPipeline();
}
```

**建议：** 删除 `LogInjectionSkeletons` 类，或将其改为 `LogPipeline` 的扩展方法类（如果确实需要保持 API 兼容性）。

### 3.2 LogInjectionSkeletons.Wrap 与 LoggingSkeletonWrapper 的职责重叠

**位置：** 第 1393-1397 行

**问题：** `Wrap()` 方法仅仅是 `new LoggingSkeletonWrapper(inner)` 的委托，没有增加任何额外逻辑。

**建议：** 直接使用 `new LoggingSkeletonWrapper(inner)` 或将其改为 `LoggingSkeletonWrapper` 的静态工厂方法。

### 3.3 CommandGetInput 未继承 CommandInputBase

**位置：** 第 992-1002 行

**问题：** `CommandGetInput` 没有继承 `CommandInputBase`，而是独立定义了 `Entity`、`QueryParams`、`VirtualStore` 三个属性。其中 `VirtualStore` 与 `CommandInputBase.VirtualStore` 重复。

**建议：** 让 `CommandGetInput` 继承 `CommandInputBase`，或提取 `IHasVirtualStore` 接口。

### 3.4 EventTriggerSkeletonInput/Output 与 CommandInputBase/CommandOutputBase 未统一

**位置：** 第 731-749 行

**问题：** `EventTriggerSkeletonInput` 包含 `EventTriggerService` 字段，与 `CommandInputBase.EventTriggerService` 重复。`EventTriggerSkeletonOutput` 的 `Success`/`Message` 与 `CommandOutputBase` 重复。

**建议：** 让 `EventTriggerSkeletonInput` 继承 `CommandInputBase`，`EventTriggerSkeletonOutput` 继承 `CommandOutputBase`。

### 3.5 LogPipelineContext 的 using 别名歧义

**位置：** 多个文件

**问题：** `Skeletons.cs` 和 `FileDrivenLogManager.cs` 都使用了：
```csharp
using PipelineContext = FileDriven.Logging.Pipeline.LogPipelineContext;
```

而 `Wrappers.cs` 和 `LogPipeline.cs` 也使用了同样的别名。这表明 `LogPipelineContext` 名称本身与 `Common.Diagnostics` 或其他命名空间中的类型冲突，需要别名来消除歧义。

**建议：** 考虑将 `FileDriven.Logging.Pipeline.LogPipelineContext` 重命名为更具体的名称（如 `LogPipelineContext` → `PipelineLogContext`），或确保 `Common.Diagnostics` 中不导出同名类型。

---

## 四、可提取为框架的公共模式

### 4.1 日志注入模式（LogEntry Injection Pattern）

**当前实现：** `LogEntryBuilder` + `LogEntryCollector` + `LogCollectSkeleton`

**可提取为框架的组件：**
- `ILogEntryBuilder` 接口
- `LogEntryBuilder` 默认实现
- `LogEntryCollector` 通用收集器
- `LogCollectSkeleton` 通用收集 Skeleton

**适用场景：** 任何需要"先收集后写入"日志模式的项目。

### 4.2 泛型诊断日志模式（Generic Diagnostic Log Pattern）

**当前实现：** `DiagnosticLogSkeleton<TInput>` + `Action<TInput, SkeletonContext>` 回调

**可提取为框架的组件：**
- `IDiagnosticLogSkeleton<TInput>` 接口
- `DiagnosticLogSkeleton<TInput>` 泛型实现
- `DiagnosticLogHelper` 辅助类（含 `LogIfNotEmpty` 等方法）

**适用场景：** 任何需要快速创建"只记录日志、不执行业务逻辑"的 Skeleton 的项目。

### 4.3 Skeleton 执行统计模式（Skeleton Execution Stats Pattern）

**当前实现：** `LoggingSkeletonWrapper` + `SkeletonExecutionStats`

**可提取为框架的组件：**
- `ISkeletonExecutionStats` 接口
- `SkeletonExecutionStats` 默认实现
- `ISkeletonWrapper` 接口
- `LoggingSkeletonWrapper` 泛型化版本

**适用场景：** 任何需要自动收集 Skeleton 执行统计信息的项目。

### 4.4 命令输入/输出基类模式（Command Input/Output Base Pattern）

**当前实现：** `CommandInputBase` + `CommandOutputBase`

**可提取为框架的组件：**
- `ICommandInput` 接口（含 `Target`, `Parameters`, `VirtualStore`, `EventTriggerService`）
- `ICommandOutput` 接口（含 `Success`, `Message`）
- `CommandInputBase` / `CommandOutputBase` 基类

**适用场景：** 任何需要定义"命令模式" Skeleton 的项目。

### 4.5 框架初始化状态模式（Framework Init State Pattern）

**当前实现：** `FrameworkInitState` + `FrameworkInitInput` + `FrameworkInitOutput`

**可提取为框架的组件：**
- `IInitializationState` 接口
- `InitializationStateBase` 基类（含 `CopyTo` 方法）
- `InitSkeletonBase<TInput, TOutput>` 泛型基类

**适用场景：** 任何需要"初始化-报告状态"模式的项目。

---

## 五、总结

### 已消除的重复（5 处）
| 编号 | 位置 | 优化方式 |
|------|------|---------|
| 1 | CommandSet/Click/Get Input/Output | 提取 CommandInputBase / CommandOutputBase |
| 2 | FrameworkInit Input/Output | 提取 FrameworkInitState |
| 3 | 6 个诊断日志 Skeleton | 泛型化 DiagnosticLogSkeleton<TInput> |
| 4 | new LogEntry 构造 | LogEntryBuilder 静态方法 |
| 5 | Create / CreateWithExceptionHandling | BuildBusinessPipeline 共享方法 |

### 仍存在的重复（6 处）
| 编号 | 位置 | 重复程度 | 建议 |
|------|------|---------|------|
| 1 | DiagnosticLogFactories 中的 LogEntryBuilder 调用 | 中 | 提取 DiagnosticLogHelper |
| 2 | CommandGetSkeleton 条件分支 | 低 | 三元表达式简化 |
| 3 | ConfigLoadSkeleton 日志注入 | 中 | 提取扩展方法 |
| 4 | FrameworkInitSkeleton 状态复制 | 低 | 添加 CopyTo 方法 |
| 5 | FileDrivenPipeline.Execute 三重复 | 高 | 提取 ExecuteInternal |
| 6 | LogPipeline.CreateWithLogCollect 系列 | 高 | 提取 CreatePipelineComponents |

### 冗余逻辑（5 处）
| 编号 | 位置 | 冗余程度 | 建议 |
|------|------|---------|------|
| 1 | LogInjectionSkeletons 委托调用 | 高 | 删除或改为扩展方法 |
| 2 | LogInjectionSkeletons.Wrap 委托 | 高 | 直接使用 LoggingSkeletonWrapper |
| 3 | CommandGetInput 未继承基类 | 中 | 继承 CommandInputBase |
| 4 | EventTriggerSkeleton Input/Output 未统一 | 中 | 继承基类 |
| 5 | LogPipelineContext 别名歧义 | 低 | 重命名类型 |

### 可提取为框架的模式（5 个）
| 编号 | 模式名称 | 核心组件 | 通用性 |
|------|---------|---------|--------|
| 1 | 日志注入模式 | LogEntryBuilder + LogEntryCollector | ★★★★★ |
| 2 | 泛型诊断日志模式 | DiagnosticLogSkeleton<TInput> | ★★★★☆ |
| 3 | Skeleton 执行统计模式 | LoggingSkeletonWrapper | ★★★★☆ |
| 4 | 命令输入/输出基类模式 | CommandInputBase / CommandOutputBase | ★★★☆☆ |
| 5 | 框架初始化状态模式 | FrameworkInitState + CopyTo | ★★★☆☆ |
