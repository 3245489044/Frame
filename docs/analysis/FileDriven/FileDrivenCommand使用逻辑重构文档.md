# FileDriven CommandFramework 使用逻辑重构文档

> **版本**: 2.0  
> **最后更新**: 2026-06-10  
> **目标**: 将 FileDriven 各子项目中所有使用 CommandFramework 的逻辑完全分离，集中到 `FileDriven.Execution` 中

---

## 目录

1. [现状分析](#一现状分析)
2. [核心设计原则](#二核心设计原则)
3. [改造后依赖关系](#三改造后依赖关系)
4. [代码迁移清单](#四代码迁移清单)
5. [详细改造方案](#五详细改造方案)
6. [csproj 变更清单](#六csproj-变更清单)
7. [实施步骤](#七实施步骤)
8. [风险与注意事项](#八风险与注意事项)

---

## 一、现状分析

### 1.1 当前问题

1. **依赖分散**：FileDriven.Pipeline、FileDriven.UI、FileDriven.Utilities 三个子项目直接 csproj 引用 CommandFramework
2. **入口不统一**：各子项目各自使用 CommandFramework 的不同部分（`PipelineEntry.CreateDefaultPipeline`、`InMemoryStateStore`、`DefaultValueExtractor`、GlobalUsings 别名）
3. **GlobalUsings 分散**：FileDriven.Utilities 定义了 9 个 CommandFramework 接口别名，其他子项目无法复用
4. **重构困难**：CommandFramework API 变化需修改 4 个子项目

### 1.2 当前引用关系

```
FileDriven.Launcher ─→ FileDriven.Execution ─→ CommandFramework (csproj)
FileDriven.Pipeline ──────────────────────────→ CommandFramework (csproj + using + 类型转型)
FileDriven.UI ────────────────────────────────→ CommandFramework (csproj + 继承)
FileDriven.Utilities ─────────────────────────→ CommandFramework (csproj + 9 个 global using)
```

### 1.3 各子项目使用 CommandFramework 的具体位置

#### FileDriven.Pipeline（3 处）

| 文件 | 使用方式 |
|------|---------|
| **FileDrivenPipelineContext.cs** | 继承 `CommandContext` + 使用 `ParsedCommand` 类型 |
| **PipelineFactory.cs** | 调用 `CommandFramework.Pipeline.PipelineEntry.CreateDefaultPipeline()` |
| **PipelineEntry.cs** | `(CommandContext)context` 转型 + `GetStageSuccess/GetStageError/HasError/GetErrorMessage` |

#### FileDriven.UI（2 处）

| 文件 | 使用方式 |
|------|---------|
| **UICommands.cs** | `VirtualControlStore : InMemoryStateStore` + `ValueExtractionHelper : DefaultValueExtractor` |
| **EventTriggerService.cs** | `using CommandFramework.Abstractions`（仅 using，未直接使用类型） |

#### FileDriven.Utilities（1 处）

| 文件 | 使用方式 |
|------|---------|
| **GlobalUsings.cs** | 9 个 `global using` 接口别名指向 `CommandFramework.Abstractions` |

#### FileDriven.Launcher（1 处）

| 文件 | 使用方式 |
|------|---------|
| **Usage.cs** | `using CommandFramework.Abstractions`（仅 `IFunctionExecutor`） |

---

## 二、核心设计原则

1. **完全分离**：Pipeline、UI、Utilities、Launcher **完全不引用** CommandFramework
2. **集中管理**：所有使用 CommandFramework 的逻辑**物理迁移**到 `FileDriven.Execution` 中
3. **代码迁移**：将 Pipeline 和 UI 中依赖 CommandFramework 的类定义迁移到 Execution
4. **桥接封装**：`CommandFrameworkBridge` 封装所有 CommandFramework 调用
5. **GlobalUsings 集中**：所有 CommandFramework 接口别名集中在 Execution 的 `GlobalUsings.cs`

---

## 三、改造后依赖关系

```
FileDriven.Launcher ─→ FileDriven.Execution (唯一入口)
                            │
                            ├── FileDriven.Pipeline (引用，提供 Bridge 服务)
                            ├── FileDriven.UI (引用，提供包装基类)
                            ├── FileDriven.Utilities (引用)
                            └── CommandFramework (唯一 csproj 引用)

FileDriven.Pipeline ─→ FileDriven.Execution (引用，使用 FileDrivenPipelineContext + Bridge)
FileDriven.UI ───────→ FileDriven.Execution (引用，使用 VirtualControlStore + ValueExtractionHelper)
FileDriven.Utilities ─→ FileDriven.Execution (引用，使用 GlobalUsings)
FileDriven.Launcher ──→ FileDriven.Execution (唯一入口)
```

**关键点**：
- `FileDriven.Execution` 是**唯一直接引用 CommandFramework** 的项目
- Pipeline、UI、Utilities、Launcher **只引用 Execution**，不直接引用 CommandFramework
- 所有 CommandFramework 类型通过 Execution 的 GlobalUsings 解析
- 所有 CommandFramework 调用通过 Execution 的 Bridge 完成
- **无循环引用**：Execution → Pipeline（提供 Bridge），Pipeline → Execution（使用 Bridge），方向明确

---

## 四、代码迁移清单

### 4.1 从 Pipeline 迁移到 Execution

| 要迁移的代码 | 源文件 | 目标文件 |
|------------|-------|---------|
| `FileDrivenPipelineContext : CommandContext`（整个类） | `FileDriven.Pipeline/FileDrivenPipelineContext.cs` | `FileDriven.Execution/FileDrivenPipelineContext.cs` |
| `PipelineFactory.BuildCommandPipeline()` 中调用 `CreateDefaultPipeline` 的逻辑 | `FileDriven.Pipeline/PipelineFactory.cs` | `FileDriven.Execution/CommandFrameworkBridge.cs` |
| `PipelineEntry` 中 `(CommandContext)context` 转型 + 阶段状态检查 | `FileDriven.Pipeline/PipelineEntry.cs` | `FileDriven.Execution/CommandFrameworkBridge.cs` |

### 4.2 从 UI 迁移到 Execution

| 要迁移的代码 | 源文件 | 目标文件 |
|------------|-------|---------|
| `VirtualControlStore : InMemoryStateStore`（整个类） | `FileDriven.UI/UICommands.cs` | `FileDriven.Execution/VirtualControlStore.cs` |
| `ValueExtractionHelper : DefaultValueExtractor`（整个类） | `FileDriven.UI/UICommands.cs` | `FileDriven.Execution/ValueExtractionHelper.cs` |

### 4.3 从 Utilities 迁移到 Execution

| 要迁移的代码 | 源文件 | 目标文件 |
|------------|-------|---------|
| 9 个 `global using` 接口别名 | `FileDriven.Utilities/GlobalUsings.cs` | `FileDriven.Execution/GlobalUsings.cs` |

---

## 五、详细改造方案

### 5.1 第一步：FileDriven.Execution 创建 `GlobalUsings.cs` ✅（已完成）

```csharp
// ============================================================
// GlobalUsings.cs — FileDriven.Execution 全局 using 指令
//
// 集中所有 CommandFramework 接口别名、模型类型别名、执行类型别名。
// 其他子项目通过引用 FileDriven.Execution 间接使用 CommandFramework。
//
// ★ 来源: CommandFramework.Abstractions / Models / Execution
// ============================================================

// ---- CommandFramework 接口别名 ----
global using IStateStore = CommandFramework.Abstractions.IStateStore;
global using IValueExtractor = CommandFramework.Abstractions.IValueExtractor;
global using ICommandExecutionResult = CommandFramework.Abstractions.ICommandExecutionResult;
global using ICommandBatchResult = CommandFramework.Abstractions.ICommandBatchResult;
global using IFunctionRegistry = CommandFramework.Abstractions.IFunctionRegistry;
global using IFunctionExecutor = CommandFramework.Abstractions.IFunctionExecutor;
global using IFunctionDescriptor = CommandFramework.Abstractions.IFunctionDescriptor;
global using IFunctionExecutionResult = CommandFramework.Abstractions.IFunctionExecutionResult;
global using IParameterDescriptor = CommandFramework.Abstractions.IParameterDescriptor;
global using INonEventCommandHandler = CommandFramework.Abstractions.INonEventCommandHandler;
global using ICommandEngine = CommandFramework.Abstractions.ICommandEngine;
global using ICommandPipeline = CommandFramework.Abstractions.ICommandPipeline;
global using IPipelineContext = CommandFramework.Abstractions.IPipelineContext;
global using IPipelineStage = CommandFramework.Abstractions.IPipelineStage;
global using IControlValueAccessor = CommandFramework.Abstractions.IControlValueAccessor;
global using IEnvironmentAdapter = CommandFramework.Abstractions.IEnvironmentAdapter;

// ---- CommandFramework 模型类型别名 ----
global using CommandContext = CommandFramework.Models.CommandContext;
global using ParsedCommand = CommandFramework.Models.ParsedCommand;
global using CommandExecutionContext = CommandFramework.Models.CommandExecutionContext;

// ---- CommandFramework 执行类型别名 ----
global using InMemoryStateStore = CommandFramework.Execution.InMemoryStateStore;
global using DefaultValueExtractor = CommandFramework.Execution.DefaultValueExtractor;
```

### 5.2 第二步：FileDriven.Execution 创建 `CommandFrameworkBridge.cs` ✅（已完成）

```csharp
// ============================================================
// CommandFrameworkBridge.cs — CommandFramework 桥接层
//
// 以 CommandFrameworkUsage 为核心，封装 FileDriven 场景下
// 对 CommandFramework 的典型调用。
//
// 设计目标：
//   1. 统一入口：所有 FileDriven 子项目通过此桥接层使用 CommandFramework
//   2. 简化调用：封装 FileDriven 场景的典型调用模式
//   3. 隔离变化：CommandFramework API 变化时只需修改此文件
// ============================================================

using CommandFramework;
using CommandFramework.Abstractions;
using CommandFramework.Models;
using CommandFramework.Pipeline;
using BP = BasicMethod.Pipeline;

namespace FileDriven.Execution;

/// <summary>
/// CommandFramework 桥接层 — 以 CommandFrameworkUsage 为核心
/// <para>封装 FileDriven 场景下对 CommandFramework 的典型调用</para>
/// </summary>
public static class CommandFrameworkBridge
{
    // ================================================================
    // 1. 生命周期管理
    // ================================================================

    public static void Initialize() => CommandFrameworkUsage.Initialize();
    public static void Initialize(Action<CommandFrameworkOptions> configure)
        => CommandFrameworkUsage.Initialize(configure);
    public static void Shutdown() => CommandFrameworkUsage.Shutdown();
    public static bool IsInitialized => CommandFrameworkUsage.IsInitialized;

    // ================================================================
    // 2. 管线创建
    // ================================================================

    /// <summary>
    /// 创建默认命令管线
    /// <para>包含阶段：ParseCommand → FindBinding → ExtractValues → ExecuteFunction → WriteResult</para>
    /// </summary>
    public static BP.IStagePipeline<CommandContext> CreateDefaultPipeline(
        string name = "DefaultCommandPipeline",
        Func<string, string, (string? FunctionName, List<string>? Inputs, string? Output)>? findBindingFunc = null)
    {
        return CommandFrameworkUsage.GetOrCreatePipeline(name, pipeline =>
        {
            if (findBindingFunc != null)
            {
                pipeline.Remove("FindBinding");
                pipeline.Register(new FindBindingStage(findBindingFunc));
            }
        });
    }

    /// <summary>
    /// 创建 CLI 模式管线
    /// </summary>
    public static BP.IStagePipeline<CommandContext> CreateCliPipeline(
        string name = "CliCommandPipeline")
    {
        return CommandFrameworkUsage.GetOrCreateCliPipeline(name);
    }

    // ================================================================
    // 3. 命令执行
    // ================================================================

    public static async Task<CommandContext> ExecuteCommandAsync(string command)
        => await CommandFrameworkUsage.ExecuteCommandAsync(command);

    public static async Task<List<CommandContext>> ExecuteCommandsAsync(IEnumerable<string> commands)
        => await CommandFrameworkUsage.ExecuteCommandsAsync(commands);

    public static async Task<CommandExecutionResult<object?>> ExecuteParsedCommandAsync(
        ParsedCommand parsed, CommandExecutionContext? context = null)
        => await CommandFrameworkUsage.ExecuteParsedCommandAsync(parsed, context);

    public static async Task<(int SuccessCount, int FailCount)> ExecuteFileAsync(string filePath)
        => await CommandFrameworkUsage.ExecuteFileAsync(filePath);

    // ================================================================
    // 4. 命令解析
    // ================================================================

    public static ParsedCommand Parse(string command)
        => CommandFrameworkUsage.Parse(command);

    public static string? Normalize(string command)
        => CommandFrameworkUsage.Normalize(command);

    public static void RegisterNormalization(string shorthand, string fullCommand)
        => CommandFrameworkUsage.RegisterNormalization(shorthand, fullCommand);

    public static void RegisterNonEventCommand(string commandName)
        => CommandFrameworkUsage.RegisterNonEventCommand(commandName);

    // ================================================================
    // 5. 管线状态检查
    // ================================================================

    /// <summary>判断管线执行是否出错</summary>
    public static bool HasPipelineError(CommandContext ctx) => ctx.HasError;

    /// <summary>获取管线错误信息</summary>
    public static string? GetPipelineError(CommandContext ctx) => ctx.GetErrorMessage();

    /// <summary>获取阶段执行状态</summary>
    public static bool GetStageSuccess(CommandContext ctx, string stageName)
        => ctx.GetStageSuccess(stageName);

    /// <summary>获取阶段错误信息</summary>
    public static string? GetStageError(CommandContext ctx, string stageName)
        => ctx.GetStageError(stageName);

    // ================================================================
    // 6. 命令注册
    // ================================================================

    public static void RegisterFunction(string name, Delegate function, string? description = null)
        => CommandFrameworkUsage.RegisterFunction(name, function, description);

    public static void RegisterNonEventHandler(string actionName,
        Func<ParsedCommand, Task<CommandExecutionResult<object?>>> handler)
        => CommandFrameworkUsage.RegisterNonEventHandler(actionName, handler);

    // ================================================================
    // 7. JSON 序列化
    // ================================================================

    public static string SerializeResult(ICommandExecutionResult result, bool prettyPrint = false)
        => CommandFrameworkUsage.SerializeResult(result, prettyPrint);

    public static string SerializeBatchResult(ICommandBatchResult batchResult, bool prettyPrint = false)
        => CommandFrameworkUsage.SerializeBatchResult(batchResult, prettyPrint);

    public static CommandExecutionResult<T>? DeserializeResult<T>(string json)
        => CommandFrameworkUsage.DeserializeResult<T>(json);
}
```

### 5.3 第三步：FileDriven.Execution 新增 `FileDrivenPipelineContext.cs`

**操作**：将 `FileDriven.Pipeline/FileDrivenPipelineContext.cs` 的完整内容迁移到 `FileDriven.Execution/FileDrivenPipelineContext.cs`

**变更**：
- 移除 `using CommandFramework.Abstractions`、`using CommandFramework.Models`、`using CommandFramework.Pipeline`
- `CommandContext` 通过 GlobalUsings 解析
- 保留 `: CommandContext` 继承关系
- 保留所有便捷属性（`ParsedVerb`、`ParsedAction`、`ParsedTarget` 等）
- 保留所有运行时依赖（`BindingResolver`、`FunctionExecutor`、`TargetForm` 等）

**注意**：迁移后，Pipeline 项目中的 Skeleton 需要将 `using FileDriven.Pipeline.FileDrivenPipelineContext` 改为 `using FileDriven.Execution.FileDrivenPipelineContext`

### 5.4 第四步：FileDriven.Execution 新增 `VirtualControlStore.cs`

**操作**：将 `FileDriven.UI/UICommands.cs` 中的 `VirtualControlStore` 类迁移到 `FileDriven.Execution/VirtualControlStore.cs`

**变更**：
- `CommandFramework.Execution.InMemoryStateStore` → `InMemoryStateStore`（通过 GlobalUsings 解析）
- 保留 `ClearAll()`、`GetAllKeysList()`、`SetSelectedItem()`、`GetSelectedItem()` 等方法

### 5.5 第五步：FileDriven.Execution 新增 `ValueExtractionHelper.cs`

**操作**：将 `FileDriven.UI/UICommands.cs` 中的 `ValueExtractionHelper` 类迁移到 `FileDriven.Execution/ValueExtractionHelper.cs`

**变更**：
- `CommandFramework.Execution.DefaultValueExtractor` → `DefaultValueExtractor`（通过 GlobalUsings 解析）
- 保留 `ExtractValue()` 静态方法

### 5.6 第六步：改造 FileDriven.Pipeline

#### PipelineFactory.cs 改造

```csharp
// 改造前：
using CommandFramework.Abstractions;
using CommandFramework.Models;
using CommandFramework.Pipeline;
var stagePipeline = CommandFramework.Pipeline.PipelineEntry.CreateDefaultPipeline(
    "CommandPipeline", findBindingFunc: findBindingFunc);
var cmdCtx = (CommandContext)ctx;
var hasError = cmdCtx.HasError;

// 改造后：
// 移除 using CommandFramework.Abstractions/Models/Pipeline
// FileDrivenPipelineContext 通过引用 FileDriven.Execution 使用
var stagePipeline = FileDriven.Execution.CommandFrameworkBridge.CreateDefaultPipeline(
    "CommandPipeline", findBindingFunc: findBindingFunc);
var hasError = FileDriven.Execution.CommandFrameworkBridge.HasPipelineError(ctx);
```

#### PipelineEntry.cs 改造

```csharp
// 改造前：
using CommandFramework.Abstractions;
using CommandFramework.Models;
using CommandFramework.Pipeline;
var cmdCtx = (CommandContext)context;
var stageSuccess = cmdCtx.GetStageSuccess(stageName);
if (cmdCtx.HasError) { ... }

// 改造后：
// 移除 using CommandFramework.Abstractions/Models/Pipeline
var stageSuccess = FileDriven.Execution.CommandFrameworkBridge.GetStageSuccess(context, stageName);
if (FileDriven.Execution.CommandFrameworkBridge.HasPipelineError(context)) { ... }
```

#### FileDrivenPipelineContext.cs 改造

**删除**此文件（已迁移到 FileDriven.Execution）

#### csproj 变更

```xml
<!-- 移除 -->
<ProjectReference Include="..\..\Share\CommandFramework\CommandFramework.csproj" />

<!-- 新增 -->
<ProjectReference Include="..\FileDriven.Execution\FileDriven.Execution.csproj" />
```

### 5.7 第七步：改造 FileDriven.UI

#### UICommands.cs 改造

```csharp
// 改造前：
public class VirtualControlStore : CommandFramework.Execution.InMemoryStateStore
public class ValueExtractionHelper : CommandFramework.Execution.DefaultValueExtractor

// 改造后：
// VirtualControlStore 和 ValueExtractionHelper 已迁移到 FileDriven.Execution
// UICommands.cs 中移除这两个类的定义
// UI 项目通过引用 FileDriven.Execution 使用它们
```

**注意**：`VirtualControlStore` 和 `ValueExtractionHelper` 的类定义已迁移到 Execution，UI 项目中不再包含这两个类。UI 项目通过引用 Execution 来使用它们。

#### EventTriggerService.cs 改造

```csharp
// 改造前：
using CommandFramework.Abstractions;

// 改造后：
// 移除 using CommandFramework.Abstractions（实际未使用任何 CommandFramework 类型）
```

#### csproj 变更

```xml
<!-- 移除 -->
<ProjectReference Include="..\..\Share\CommandFramework\CommandFramework.csproj" />

<!-- 新增 -->
<ProjectReference Include="..\FileDriven.Execution\FileDriven.Execution.csproj" />
```

### 5.8 第八步：改造 FileDriven.Utilities

#### GlobalUsings.cs 改造

```csharp
// 改造前：
global using IStateStore = CommandFramework.Abstractions.IStateStore;
global using IValueExtractor = CommandFramework.Abstractions.IValueExtractor;
// ...（共 9 个 CommandFramework 接口别名）

// 改造后：
// 移除所有 CommandFramework 接口别名
// 仅保留 EventTriggerFramework 别名
global using EventTriggerFramework.Abstractions;
global using IEventBindingInfo = EventTriggerFramework.Abstractions.IEventBindingInfo;
global using IEventBindingResolver = EventTriggerFramework.Abstractions.IEventBindingResolver;
global using IEventTriggerResult = EventTriggerFramework.Abstractions.IEventTriggerResult;
global using IEventTriggerService = EventTriggerFramework.Abstractions.IEventTriggerService;
```

#### csproj 变更

```xml
<!-- 移除 -->
<ProjectReference Include="..\..\Share\CommandFramework\CommandFramework.csproj" />

<!-- 新增 -->
<ProjectReference Include="..\FileDriven.Execution\FileDriven.Execution.csproj" />
```

### 5.9 第九步：改造 FileDriven.Launcher

#### Usage.cs 改造

```csharp
// 改造前：
using CommandFramework.Abstractions;

// 改造后：
// 移除 using CommandFramework.Abstractions
// IFunctionExecutor 通过 FileDriven.Execution 的 GlobalUsings 解析
```

#### csproj 变更

```xml
<!-- 移除 -->
<ProjectReference Include="..\..\Share\CommandFramework\CommandFramework.csproj" />

<!-- 保留（已有） -->
<ProjectReference Include="..\FileDriven.Execution\FileDriven.Execution.csproj" />
```

---

## 六、csproj 变更清单

| 子项目 | 移除引用 | 新增引用 |
|--------|---------|---------|
| `FileDriven.Pipeline.csproj` | `CommandFramework.csproj` | `FileDriven.Execution.csproj` |
| `FileDriven.UI.csproj` | `CommandFramework.csproj` | `FileDriven.Execution.csproj` |
| `FileDriven.Utilities.csproj` | `CommandFramework.csproj` | `FileDriven.Execution.csproj` |
| `FileDriven.Launcher.csproj` | `CommandFramework.csproj` | 无（已有 Execution 引用） |
| `FileDriven.Execution.csproj` | 无 | 无（已有 CommandFramework 引用） |

---

## 七、实施步骤

### 步骤 1：FileDriven.Execution 创建 GlobalUsings.cs ✅（已完成）

- **文件**：`FileDriven.Execution/GlobalUsings.cs`
- **内容**：集中所有 CommandFramework 接口别名（16 个）、模型类型别名（3 个）、执行类型别名（2 个）
- **验证**：`dotnet build FileDriven.Execution` 编译通过

### 步骤 2：FileDriven.Execution 创建 CommandFrameworkBridge.cs ✅（已完成）

- **文件**：`FileDriven.Execution/CommandFrameworkBridge.cs`
- **内容**：以 `CommandFrameworkUsage` 为核心的桥接层，封装生命周期、管线创建、命令执行、命令解析、状态检查、命令注册、JSON 序列化
- **验证**：`dotnet build FileDriven.Execution` 编译通过

### 步骤 3：FileDriven.Execution 新增 FileDrivenPipelineContext.cs

- **操作**：将 `FileDriven.Pipeline/FileDrivenPipelineContext.cs` 迁移到 `FileDriven.Execution/FileDrivenPipelineContext.cs`
- **变更**：移除 3 个 CommandFramework using，通过 GlobalUsings 解析 `CommandContext`
- **验证**：`dotnet build FileDriven.Execution` 编译通过

### 步骤 4：FileDriven.Execution 新增 VirtualControlStore.cs

- **操作**：将 `FileDriven.UI/UICommands.cs` 中的 `VirtualControlStore` 类迁移到 `FileDriven.Execution/VirtualControlStore.cs`
- **变更**：`CommandFramework.Execution.InMemoryStateStore` → `InMemoryStateStore`（通过 GlobalUsings 解析）
- **验证**：`dotnet build FileDriven.Execution` 编译通过

### 步骤 5：FileDriven.Execution 新增 ValueExtractionHelper.cs

- **操作**：将 `FileDriven.UI/UICommands.cs` 中的 `ValueExtractionHelper` 类迁移到 `FileDriven.Execution/ValueExtractionHelper.cs`
- **变更**：`CommandFramework.Execution.DefaultValueExtractor` → `DefaultValueExtractor`（通过 GlobalUsings 解析）
- **验证**：`dotnet build FileDriven.Execution` 编译通过

### 步骤 6：改造 FileDriven.Pipeline

- **文件**：`FileDriven.Pipeline/FileDrivenPipelineContext.cs` — **删除**（已迁移到 Execution）
- **文件**：`FileDriven.Pipeline/PipelineFactory.cs`
  - 移除 `using CommandFramework.Abstractions`、`using CommandFramework.Models`、`using CommandFramework.Pipeline`
  - `CommandFramework.Pipeline.PipelineEntry.CreateDefaultPipeline()` → `CommandFrameworkBridge.CreateDefaultPipeline()`
  - `(CommandContext)ctx` 转型 → `CommandFrameworkBridge.HasPipelineError(ctx)` / `GetPipelineError(ctx)`
- **文件**：`FileDriven.Pipeline/PipelineEntry.cs`
  - 移除 `using CommandFramework.Abstractions`、`using CommandFramework.Models`、`using CommandFramework.Pipeline`
  - `(CommandContext)context` 转型 → `CommandFrameworkBridge.GetStageSuccess(context, name)` / `HasPipelineError(context)`
- **文件**：`FileDriven.Pipeline/FileDriven.Pipeline.csproj`
  - 移除 `CommandFramework.csproj`，添加 `FileDriven.Execution.csproj`
- **验证**：`dotnet build FileDriven.Pipeline` 编译通过

### 步骤 7：改造 FileDriven.UI

- **文件**：`FileDriven.UI/UICommands.cs`
  - 移除 `VirtualControlStore` 和 `ValueExtractionHelper` 类定义（已迁移到 Execution）
  - 保留 `ControlValueHelper` 和 `UICommandManager` 类
- **文件**：`FileDriven.UI/Event/EventTriggerService.cs`
  - 移除 `using CommandFramework.Abstractions`
- **文件**：`FileDriven.UI/FileDriven.UI.csproj`
  - 移除 `CommandFramework.csproj`，添加 `FileDriven.Execution.csproj`
- **验证**：`dotnet build FileDriven.UI` 编译通过

### 步骤 8：改造 FileDriven.Utilities

- **文件**：`FileDriven.Utilities/GlobalUsings.cs`
  - 移除 9 个 CommandFramework 接口别名，仅保留 EventTriggerFramework 别名
- **文件**：`FileDriven.Utilities/FileDriven.Utilities.csproj`
  - 移除 `CommandFramework.csproj`，添加 `FileDriven.Execution.csproj`
- **验证**：`dotnet build FileDriven.Utilities` 编译通过

### 步骤 9：改造 FileDriven.Launcher

- **文件**：`FileDriven.Launcher/Usage.cs`
  - 移除 `using CommandFramework.Abstractions`
- **文件**：`FileDriven.Launcher/FileDriven.Launcher.csproj`
  - 移除 `CommandFramework.csproj`（只保留 `FileDriven.Execution.csproj`）
- **验证**：`dotnet build FileDriven.Launcher` 编译通过

### 步骤 10：全局验证

- **命令**：`dotnet build WorkFlowPrj.sln`
- **检查**：所有子项目编译通过，无 CommandFramework 相关编译错误

---

## 八、风险与注意事项

### 8.1 编译顺序

`FileDriven.Execution` 必须先编译通过，其他子项目才能引用其 GlobalUsings 和 Bridge。

### 8.2 命名冲突

GlobalUsings 中的别名（如 `CommandContext`）可能与子项目自身的类型冲突，需注意。

### 8.3 PipelineFactory 的转型代码

`(CommandContext)ctx` 改为 `CommandFrameworkBridge.HasPipelineError(ctx)` 时，需确保 `ctx` 参数类型兼容。`FileDrivenPipelineContext` 继承 `CommandContext`，所以可以传入。

### 8.4 PipelineEntry 的阶段状态检查

`foreach (var stageName in new[] { "ParseCommand", ... })` 循环中的转型调用需全部替换为 Bridge 方法。

### 8.5 Skeleton 中的 using 变更

Pipeline 项目中的 Skeleton 文件（`ScriptLoadSkeleton`、`CommandLoopSkeleton` 等）如果使用了 `FileDriven.Pipeline.FileDrivenPipelineContext`，需要改为 `FileDriven.Execution.FileDrivenPipelineContext`。

### 8.6 UI 项目中的 VirtualControlStore 引用

如果 UI 项目中的其他文件引用了 `VirtualControlStore` 或 `ValueExtractionHelper`，需要将 using 从 `FileDriven.UI.Commands` 改为 `FileDriven.Execution`。

---

## 附录：改造前后对比

### 改造前

```
FileDriven.Launcher ─→ FileDriven.Execution ─→ CommandFramework (csproj)
                                                       │
FileDriven.Pipeline ──────────────────────────────────┤ (csproj + using + 类型转型)
                                                       │
FileDriven.UI ────────────────────────────────────────┤ (csproj + 继承)
                                                       │
FileDriven.Utilities ─────────────────────────────────┤ (csproj + 9 个 global using)
```

### 改造后

```
FileDriven.Launcher ─→ FileDriven.Execution ─→ CommandFramework (唯一 csproj 引用)
                            │
FileDriven.Pipeline ────────┤ (引用 Execution，使用 FileDrivenPipelineContext + Bridge)
FileDriven.UI ──────────────┤ (引用 Execution，使用 VirtualControlStore + ValueExtractionHelper)
FileDriven.Utilities ───────┤ (引用 Execution，使用 GlobalUsings)
```

### 代码迁移对照

| 类/逻辑 | 迁移前位置 | 迁移后位置 |
|---------|-----------|-----------|
| `FileDrivenPipelineContext : CommandContext` | `FileDriven.Pipeline` | `FileDriven.Execution` |
| `VirtualControlStore : InMemoryStateStore` | `FileDriven.UI` | `FileDriven.Execution` |
| `ValueExtractionHelper : DefaultValueExtractor` | `FileDriven.UI` | `FileDriven.Execution` |
| `CreateDefaultPipeline` 调用 | `FileDriven.Pipeline/PipelineFactory.cs` | `FileDriven.Execution/CommandFrameworkBridge.cs` |
| 阶段状态检查（`GetStageSuccess` 等） | `FileDriven.Pipeline/PipelineEntry.cs` | `FileDriven.Execution/CommandFrameworkBridge.cs` |
| 9 个 CommandFramework 接口别名 | `FileDriven.Utilities/GlobalUsings.cs` | `FileDriven.Execution/GlobalUsings.cs` |
