# FileDriven.Execution 基于 BackEndDriven 优化重构方案

> **目标**：消除 FileDriven.Execution 与 BackEndDriven 之间的代码重复，将通用管线框架下沉到 BackEndDriven，FileDriven.Execution 专注于 FileDriven 领域特有逻辑（WinForms 控件操作、FileDriven 管线骨架、FileDriven 日志等）。

---

## 1. 现状分析

### 1.1 BackEndDriven 已有通用代码（~1234 行）

| 文件 | 行数 | 职责 |
|------|------|------|
| `Execution/ExecutionEngine.cs` | 287 | 通用执行引擎：封装管线创建→执行→结果收集 |
| `Execution/ExecutionPoolLifecycle.cs` | 176 | 执行池生命周期管理器 |
| `Executor/CommandExecutor.cs` | 123 | 单条命令执行器 |
| `Executor/BatchCommandExecutor.cs` | 280 | 批量命令执行器 |
| `Pipeline/PipelineEntry.cs` | 170 | 通用管线入口（sleep/echo/exit 内置命令） |
| `Pipeline/PipelineFactory.cs` | 73 | 通用管线工厂 |
| `Pipeline/PipelineContext.cs` | 125 | 通用管线上下文基类（泛型 TContext） |
| `Abstractions/*.cs` | ~100 | 接口定义（IExecutionResult, ICommandRegistry 等） |
| `Models/*.cs` | ~100 | 模型定义（ExecutionResult, CommandLineResult 等） |

### 1.2 FileDriven.Execution 仍然存在的代码（~3455 行）

| 文件 | 行数 | 职责 | 可下沉度 |
|------|------|------|----------|
| `Command/ExecutionPipelineEntry.cs` | 583 | FileDriven 执行管线入口 | ★★☆ |
| `Command/PipelineFactory.cs` | 252 | FileDriven 管线构建 | ★☆☆ |
| `Command/FileDrivenPipelineContext.cs` | 541 | FileDriven 管线上下文 | ★★☆ |
| `Command/ControlFinder.cs` | 534 | WinForms 控件查找 | ★☆☆（领域特有） |
| `Command/ControlServices.cs` | 490 | 控件服务 | ★☆☆（领域特有） |
| `Command/ControlValueWriter.cs` | 596 | 控件值写入 | ★☆☆（领域特有） |
| `Command/CommandRegistry.cs` | 263 | 命令注册 | ★☆☆（领域特有） |
| `Command/Skeletons/ScriptLoadSkeleton.cs` | 107 | 脚本加载骨架 | ★☆☆（领域特有） |
| `ExecutionResult.cs` | 89 | 执行结果 | ★★★（完全重复） |
| `FileDrivenExecutor.cs` | ~100 | 执行器入口 | ★☆☆ |

### 1.3 代码重复明细

#### 重复 1：ExecutionResult（完全重复）

**FileDriven.Execution.ExecutionResult** 与 **BackEndDriven.Models.ExecutionResult** 几乎完全一样：

| 成员 | FileDriven.Execution | BackEndDriven |
|------|---------------------|---------------|
| `Success` | ✅ | ✅ |
| `TotalCommands` | ✅ | ✅ |
| `SuccessCount` | ✅ | ✅ |
| `FailCount` | ✅ | ✅ |
| `CommandResults` | `List<CommandFileLineResult>` | `List<CommandLineResult>` |
| `ExecutionTime` | ✅ | ✅ |
| `ErrorMessage` | ✅ | ✅ |
| `Mode` | ✅ | ✅ |
| `BatchId` | ✅ | ✅ |
| `EmptySuccess()` | ✅ | ✅ |
| `Failure()` | ✅ | ✅ |

差异仅在于 `CommandResults` 的类型：FileDriven 使用 `CommandFileLineResult`，BackEndDriven 使用 `CommandLineResult`。由于 `CommandFileLineResult` 已定义隐式转换到 `CommandLineResult`，FileDriven 可以直接使用 BackEndDriven 的 `ExecutionResult`。

#### 重复 2：FileDrivenPipelineContext 统计字段

FileDrivenPipelineContext 中定义了与 BackEndDriven.PipelineContext 重复的统计字段：

| 字段 | FileDrivenPipelineContext | BackEndDriven.PipelineContext |
|------|--------------------------|-------------------------------|
| `TotalCommands` | ✅（在 CommandResults 上方） | ✅ |
| `SuccessCount` | ✅（在 CommandResults 上方） | ✅ |
| `FailCount` | ✅（在 CommandResults 上方） | ✅ |
| `SkipCount` | ✅（第 240 行） | ✅ |
| `CommandResults` | `List<CommandFileLineResult>` | `List<CommandLineResult>` |

#### 重复 3：ExecutionPipelineEntry.RunPipeline 执行流程

ExecutionPipelineEntry.RunPipeline() 的流程（创建上下文→创建管线→执行→返回结果）与 BackEndDriven.ExecutionEngine.ExecuteFile() 高度相似：

```
ExecutionPipelineEntry.RunPipeline:          BackEndDriven.ExecutionEngine.ExecuteFile:
  CreateContext()                              （内部创建 PipelineContext）
  CreatePipeline()                             使用注入的 pipeline
  pipeline.Execute(context)                    _pipeline.Execute(commandFile)
  return (context.CommandResults, ...)         CollectResult(result, ...)
```

#### 重复 4：PipelineFactory 中 ForEach 统计逻辑

PipelineFactory.CreateFileDrivenPipeline() 的 onItemAfter 回调中的统计收集逻辑：

```csharp
onItemAfter: (ctx, line, index, success) =>
{
    ctx.TotalCommands++;
    // 成功/失败计数
    // 日志输出
    // CommandResults.Add(...)
}
```

与 BackEndDriven.BatchCommandExecutor.ExecuteFile() 中的逐条执行+统计收集逻辑重复。

---

## 2. 重构方案

### 2.1 总体架构

```
┌─────────────────────────────────────────────────────────┐
│                    BackEndDriven                         │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ PipelineEntry │  │ PipelineFactory│ │ ExecutionEngine│  │
│  │ (通用管线入口) │  │ (通用管线工厂) │ │ (通用执行引擎) │  │
│  └──────────────┘  └──────────────┘  └───────────────┘  │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ PipelineContext│ │ ExecutionResult│ │ BatchCommand  │  │
│  │ (泛型上下文基类)│ │ (通用执行结果) │ │ Executor      │  │
│  └──────────────┘  └──────────────┘  └───────────────┘  │
│  ┌──────────────────────────────────────────────────┐    │
│  │ Abstractions (接口层)                             │    │
│  │ IExecutionResult / ICommandRegistry /            │    │
│  │ IExecutionPoolManager / IBindingFinderStrategy   │    │
│  │ IBackEndPipelineContext                          │    │
│  └──────────────────────────────────────────────────┘    │
└──────────────────────┬──────────────────────────────────┘
                       │ 继承/实现
┌──────────────────────▼──────────────────────────────────┐
│                  FileDriven.Execution                    │
│  ┌──────────────────┐  ┌──────────────────────────────┐ │
│  │ FileDrivenPipeline│  │ ExecutionPipelineEntry       │ │
│  │ Context           │  │ (委托给 ExecutionEngine)     │ │
│  │ (继承 PipelineCtx)│  └──────────────────────────────┘ │
│  └──────────────────┘  ┌──────────────────────────────┐ │
│  ┌──────────────────┐  │ PipelineFactory              │ │
│  │ ControlFinder     │  │ (FileDriven 管线构建)        │ │
│  │ (WinForms 特有)   │  └──────────────────────────────┘ │
│  └──────────────────┘  ┌──────────────────────────────┐ │
│  ┌──────────────────┐  │ ScriptLoadSkeleton           │ │
│  │ ControlServices   │  │ (FileDriven 脚本加载)        │ │
│  │ (WinForms 特有)   │  └──────────────────────────────┘ │
│  └──────────────────┘                                   │
│  ┌──────────────────┐  ┌──────────────────────────────┐ │
│  │ ControlValueWriter│  │ CommandRegistry              │ │
│  │ (WinForms 特有)   │  │ (FileDriven 命令注册)        │ │
│  └──────────────────┘  └──────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

### 2.2 阶段 A：消除 ExecutionResult 重复

**改动**：删除 `FileDriven.Execution.ExecutionResult.cs`，所有使用方改用 `BackEndDriven.Models.ExecutionResult`。

**影响范围**：
- `FileDrivenExecutor.cs` — 返回类型改为 `BackEndDriven.Models.ExecutionResult`
- `ExecutionReportPrinter.cs` — 参数类型改为 `BackEndDriven.Models.ExecutionResult`
- 其他引用 `FileDriven.Execution.ExecutionResult` 的代码

**风险**：低。`CommandFileLineResult` 已有隐式转换到 `CommandLineResult`，BackEndDriven.ExecutionResult.CommandResults 可接受 `CommandLineResult`。

### 2.3 阶段 B：消除 FileDrivenPipelineContext 统计字段重复

**改动**：让 `FileDrivenPipelineContext` 继承 `BackEndDriven.Pipeline.PipelineContext<FileDrivenPipelineContext>`，消除 `TotalCommands`/`SuccessCount`/`FailCount`/`SkipCount` 的重复定义。

**当前继承链**：
```
CommandContext (EventTriggerFramework)
  └── FileDrivenPipelineContext
```

**目标继承链**：
```
CommandContext (EventTriggerFramework)
  └── FileDrivenPipelineContext  ← 同时实现 IBackEndPipelineContext
       └── (内部组合 BackEndDriven.Pipeline.PipelineContext 的统计字段)
```

由于 C# 不支持多重继承，有两种方案：

**方案 B1（推荐）**：FileDrivenPipelineContext 保留 `CommandResults` 为 `List<CommandFileLineResult>`，通过显式接口实现 `IBackEndPipelineContext.CommandResults` 做类型转换（当前已实现）。删除 FileDrivenPipelineContext 中重复的 `TotalCommands`/`SuccessCount`/`FailCount`/`SkipCount` 字段，改用 `BackEndDriven.Pipeline.PipelineContext` 的版本。

**方案 B2**：FileDrivenPipelineContext 不再继承 `CommandContext`，改为继承 `BackEndDriven.Pipeline.PipelineContext<FileDrivenPipelineContext>`，通过组合方式持有 `CommandContext` 的功能。

**推荐方案 B1 的具体改动**：
1. 删除 FileDrivenPipelineContext 中的 `TotalCommands`/`SuccessCount`/`FailCount`/`SkipCount` 属性
2. 删除 FileDrivenPipelineContext 中的 `CommandResults` 属性（保留 `List<CommandFileLineResult>` 版本）
3. 保留 `IBackEndPipelineContext.CommandResults` 显式接口实现（已存在）
4. 更新 `Reset()` 方法，不再保存/恢复已删除的统计字段
5. 更新 `PipelineFactory.cs` 中 `onItemAfter` 回调，使用 `BackEndDriven.Pipeline.PipelineContext` 的统计方法

### 2.4 阶段 C：ExecutionPipelineEntry 委托给 ExecutionEngine

**改动**：`ExecutionPipelineEntry.RunPipeline()` 内部委托给 `BackEndDriven.Execution.ExecutionEngine`，消除管线创建→执行→结果收集的重复流程。

**具体实现**：

```csharp
// ExecutionPipelineEntry 中新增
private BackEndDriven.Execution.ExecutionEngine? _backEndEngine;

public (List<CommandFileLineResult> Results, bool Success) RunPipeline(
    CliOptions options,
    string commandFile,
    Form? targetForm = null,
    object? uiCommandManager = null,
    IWinFormsEventBinder? winFormsEventBinder = null)
{
    // 委托给 BackEndDriven ExecutionEngine
    if (_backEndEngine != null)
    {
        var result = _backEndEngine.ExecuteFile(commandFile, options.ConfigPath);
        // 将 BackEndDriven 结果映射回 FileDriven 格式
        var results = result.CommandResults
            .Select(r => CommandFileLineResult.FromCommandLineResult(r))
            .ToList();
        return (results, result.Success);
    }
    
    // 回退到原有逻辑
    // ...
}
```

**影响范围**：
- `ExecutionPipelineEntry.cs` — 新增 `_backEndEngine` 字段和委托逻辑
- `FileDrivenExecutor.cs` — 创建 `ExecutionPipelineEntry` 时传入 `ExecutionEngine` 实例

### 2.5 阶段 D：PipelineFactory 中 ForEach 统计逻辑提取

**改动**：将 `onItemAfter` 回调中的统计收集逻辑提取到 `BackEndDriven` 的通用辅助方法中。

**具体实现**：

在 `BackEndDriven` 中新增 `ForEachHelper.cs`：

```csharp
namespace BackEndDriven.Utilities;

public static class ForEachHelper
{
    /// <summary>
    /// 创建标准的 onItemAfter 回调
    /// </summary>
    public static Action<TContext, string, int, bool> CreateOnItemAfter<TContext>(
        ILogger? logger = null)
        where TContext : IBackEndPipelineContext
    {
        return (ctx, line, index, success) =>
        {
            ctx.TotalCommands++;
            if (/* hasError */)
                ctx.FailCount++;
            else
                ctx.SuccessCount++;
            
            // 记录命令结果
            ctx.RecordCommandResult(new CommandLineResult
            {
                CommandText = line,
                Success = !hasError,
                Message = hasError ? errorMsg : "OK"
            });
        };
    }
}
```

**影响范围**：
- `BackEndDriven` 新增 `Utilities/ForEachHelper.cs`
- `FileDriven.Execution.PipelineFactory` 中的 `onItemAfter` 回调简化为委托调用

---

## 3. 实施计划

| 阶段 | 改动文件 | 预计减少代码 | 风险 |
|------|---------|-------------|------|
| A | 删除 `ExecutionResult.cs` + 修改引用方 | -89 行 | 低 |
| B | 修改 `FileDrivenPipelineContext.cs` | -50 行 | 中 |
| C | 修改 `ExecutionPipelineEntry.cs` + `FileDrivenExecutor.cs` | -80 行 | 中 |
| D | 新增 `ForEachHelper.cs` + 修改 `PipelineFactory.cs` | -30 行 | 低 |

**总计预计减少代码**：约 **250 行**（从 ~3455 行减少到 ~3200 行）

### 3.1 依赖关系

```
阶段 A（ExecutionResult 删除）
  └── 阶段 B（FileDrivenPipelineContext 简化）
       └── 阶段 D（ForEachHelper 提取）
            └── 阶段 C（ExecutionPipelineEntry 委托）
```

阶段 A 和 B 可以并行进行，阶段 C 和 D 可以并行进行，但阶段 C/D 依赖阶段 A/B 完成。

### 3.2 验证步骤

1. 每个阶段完成后执行 `dotnet build` 验证编译通过
2. 运行 `CentralTest/CSharpTest/` 中的单元测试
3. 运行 `FileDriven` 的 CLI 模式测试，确保功能正常

---

## 4. 不重构的代码（领域特有）

以下代码是 FileDriven 领域特有的，不适合下沉到 BackEndDriven：

| 文件 | 原因 |
|------|------|
| `ControlFinder.cs` | WinForms 控件查找逻辑，BackEndDriven 不应依赖 WinForms |
| `ControlServices.cs` | WinForms 控件服务，领域特有 |
| `ControlValueWriter.cs` | WinForms 控件值写入，领域特有 |
| `CommandRegistry.cs` | FileDriven 特有命令注册（load/sideload/show 等） |
| `ScriptLoadSkeleton.cs` | FileDriven 脚本加载骨架 |
| `FileDrivenExecutor.cs` | FileDriven 执行器入口，整合五层架构 |

---

## 5. 附录：当前代码行数统计

| 模块 | 文件数 | 总行数 |
|------|--------|--------|
| BackEndDriven | 12 | ~1,234 |
| FileDriven.Execution | 10 | ~3,455 |
| 重构后 FileDriven.Execution（预计） | 9 | ~3,200 |
| 重构后 BackEndDriven（预计） | 13 | ~1,300 |
