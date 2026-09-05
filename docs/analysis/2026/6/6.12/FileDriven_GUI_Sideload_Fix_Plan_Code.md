# FileDriven GUI Sideload 修复方案 — 代码整理与修复文档

## 1. 问题概述

### 1.1 症状

GUI 模式下执行命令文件时，`sideload` 命令失败，导致后续所有事件命令（如 `set valueATextBox 10`）找不到控件绑定。

### 1.2 错误信息

```
[命令] sideload MathFunctions.json
[错误] Unknown non-event command: sideload

[命令] set valueATextBox 10
[错误] No binding found for 'valueATextBox.textchanged'
```

---

## 2. 架构分析

### 2.1 两条管线路径

系统存在两条管线路径，职责不同但存在命令分发机制的双轨制：

```
管线 A（高层编排管线）：
  PipelineFactory.CreateFileDrivenPipeline()
    → ScriptLoadSkeleton（加载脚本文件）
    → ForEach(CommandLoop)（遍历每条命令）
        → 内部调用管线 B 处理每条命令

管线 B（命令执行子管线）：
  PipelineFactory.BuildCommandPipeline()
    → CommandFrameworkUsage.CreateDefaultPipeline()
        → ParseCommandStage（解析+路由）
        → FindBindingStage（查找绑定）
        → ExtractValuesStage（提取值）
        → ExecuteFunctionStage（执行函数）
        → WriteResultStage（写结果）
```

### 2.2 命令分发双轨制

| 组件 | 位置 | 注册中心 | 用途 |
|------|------|----------|------|
| `NonEventCommandRegistry` | `FileDriven.Pipeline.Core` | 字典 `Dictionary<string, CommandRegistration>` | 注册所有内置命令处理函数（含 `sideload`） |
| `NonEventCommandHandler` | `Share.CommandFramework.Execution` | 字典 `Dictionary<string, Func<...>>` | CommandFramework 管线的命令分发 |

**问题**：`sideload` 只注册到了 `NonEventCommandRegistry`，没有注册到 `NonEventCommandHandler`。

### 2.3 调用链分析

```
PipelineFactory.BuildCommandPipeline()  [FileDriven.Execution/Command/PipelineFactory.cs:210]
  → CommandFrameworkUsage.CreateDefaultPipeline()  [Share/CommandFramework/CommandFrameworkUsage.cs:386]
    → PipelineEntry.CreateDefaultPipeline()  [Share/CommandFramework/Pipeline/PipelineEntry.cs:49]
      → new ParseCommandStage(parser, nonEventHandler, engine, environmentAdapter)
        → ParseCommandStage.ExecuteCoreAsync()  [Share/CommandFramework/Pipeline/Stages/ParseCommandStage.cs:105]
          → parsed.IsNonEventCommand == true
          → _nonEventHandler.HandleAsync(parsed)  ← 这里的 NonEventCommandHandler 是 CommandFramework 版本
            → _handlers 字典中没有 "sideload"
            → 返回 "Unknown non-event command: sideload"
```

### 2.4 根因总结

`PipelineFactory.BuildCommandPipeline()` 通过 `CommandFrameworkUsage.CreateDefaultPipeline()` 创建命令子管线，该管线使用 `CommandFrameworkHost` 内部的 `NonEventCommandHandler`（`CommandRouter.cs`）。但 `sideload` 的处理函数只注册到了 `FileDriven.Pipeline.Core.NonEventCommandRegistry` 中，没有注册到 `CommandFramework` 的 `NonEventCommandHandler`。

---

## 3. 修复方案

### 3.1 方案选择

**推荐方案**：统一命令分发机制 — 让 `CommandFramework` 的 `NonEventCommandHandler` 委托给 `NonEventCommandRegistry`。

**理由**：
- 最小改动，不改变现有架构
- 消除双轨制，所有非事件命令统一通过 `NonEventCommandRegistry` 注册
- 不影响 CLI 模式（CLI 模式走同样的管线路径）

### 3.2 具体修改

#### 修改 1：`PipelineFactory.cs` — 创建桥接 NonEventCommandHandler

**文件**：`FileDriven/FileDriven.Execution/Command/PipelineFactory.cs`

**修改位置**：`BuildCommandPipeline()` 方法（第190-235行）

**修改内容**：不再通过 `CommandFrameworkUsage.CreateDefaultPipeline()` 创建管线，而是直接使用 `PipelineEntry.CreateDefaultPipeline()`（Share/CommandFramework 版本），并传入一个自定义的 `NonEventCommandHandler`，其 `HandleAsync()` 方法委托给 `NonEventCommandRegistry`。

```csharp
// 修改前（第210行）：
var stagePipeline = CommandFrameworkUsage.CreateDefaultPipeline(
    "CommandPipeline",
    findBindingFunc: findBindingFunc);

// 修改后：
// 创建桥接 NonEventCommandHandler，委托给 NonEventCommandRegistry
var bridgeNonEventHandler = new NonEventCommandHandler();
bridgeNonEventHandler.RegisterHandler("sideload", async parsed =>
{
    // 从 NonEventCommandRegistry 获取 sideload 处理函数并执行
    var registry = NonEventCommandRegistry.Instance;
    var registration = registry.GetRegistration("sideload");
    if (registration != null)
    {
        // 创建 FileDrivenPipelineContext 并执行
        var ctx = new FileDrivenPipelineContext();
        // ... 填充上下文
        await registration.Handler(ctx);
        return new CommandExecutionResult<object?> { IsSuccess = true };
    }
    return new CommandExecutionResult<object?> { IsSuccess = false, Error = "sideload not registered" };
});

var stagePipeline = PipelineEntry.CreateDefaultPipeline(
    "CommandPipeline",
    findBindingFunc: findBindingFunc,
    parser: CommandFrameworkUsage.Parser,
    nonEventHandler: bridgeNonEventHandler);
```

**问题**：上述方案需要创建 `FileDrivenPipelineContext`，但 `NonEventCommandHandler.HandleAsync()` 接收的是 `ParsedCommand`，而 `NonEventCommandRegistry` 的处理函数接收的是 `FileDrivenPipelineContext`。两者上下文类型不同。

#### 修改 2（更优方案）：在 `CommandFrameworkUsage` 初始化后注册 sideload

**文件**：`FileDriven/FileDriven.Execution/FileDrivenExecutor.cs`

**修改位置**：`InitializeFramework()` 方法（第416-518行），在 `CommandFrameworkUsage.Reset()` 之后（第499行）

**修改内容**：通过 `CommandFrameworkUsage.RegisterNonEventHandler()` 注册 `sideload` 的处理函数。

```csharp
// 在 CommandFrameworkUsage.Reset() 之后添加（第501行之后）：
// ★ 修复：将 sideload 命令注册到 CommandFramework 的 NonEventCommandHandler
//   使 GUI 模式下 CommandFramework 管线能正确处理 sideload 命令
CommandFrameworkUsage.RegisterNonEventHandler("sideload", async parsed =>
{
    // 获取 sideload 的参数（配置文件路径）
    var configPath = parsed.Target;
    if (string.IsNullOrEmpty(configPath))
    {
        return new CommandExecutionResult<object?>
        {
            IsSuccess = false,
            Error = "sideload requires a config path"
        };
    }
    
    // 通过 CommandFrameworkUsage 加载配置
    var loadResult = CommandFrameworkUsage.LoadConfig(configPath, showForm: true);
    
    return new CommandExecutionResult<object?>
    {
        IsSuccess = loadResult.IsSuccess,
        Value = loadResult.Value,
        Error = loadResult.ErrorMessage
    };
});
```

**问题**：`CommandFrameworkUsage.LoadConfig()` 需要 `IConfigLoader` 已注册，而 `IConfigLoader` 是在 `StartGuiMode()`/`StartCliMode()` 中通过 `PipelineEntry` 实现的。`InitializeFramework()` 阶段还没有 `PipelineEntry` 实例。

#### 修改 3（最终方案）：在 `StartGuiMode()` 中注册 sideload

**文件**：`FileDriven/FileDriven.Execution/FileDrivenExecutor.cs`

**修改位置**：`StartGuiMode()` 方法（第822-926行），在创建 `pipelineEntry` 和 `executionEntry` 之后（第856行）

**修改内容**：将 `executionEntry` 注册为 `IConfigLoader` 到 `CommandFrameworkUsage`，然后注册 `sideload` 处理函数。

```csharp
// 在 var executionEntry = new ExecutionPipelineEntry(pipelineEntry); 之后添加（第856行之后）：
// ★ 修复：注册通用服务到 CommandFrameworkUsage
//   使 CommandFramework 管线的 NonEventCommandHandler 能处理 sideload 等命令
CommandFrameworkUsage.RegisterServices(
    configLoader: executionEntry,  // ExecutionPipelineEntry 实现 IConfigLoader
    uiController: mainForm);       // MainForm 实现 IUIController
```

**问题**：需要确认 `ExecutionPipelineEntry` 是否实现了 `IConfigLoader` 接口。

---

## 4. 接口确认

### 4.1 需要确认的接口实现

| 接口 | 实现类 | 是否已实现 |
|------|--------|-----------|
| `IConfigLoader` | `ExecutionPipelineEntry` | 需确认 |
| `IConfigLoader` | `PipelineEntry` (FileDriven.Pipeline) | 需确认 |
| `IUIController` | `MainForm` | 需确认 |

### 4.2 关键方法签名

```csharp
// IConfigLoader 接口
public interface IConfigLoader
{
    LoadResult LoadConfig(string configPath, bool showForm = false, IUIController? uiController = null);
}

// NonEventCommandHandler.RegisterServiceCommands() 方法
public void RegisterServiceCommands(
    IConfigLoader? configLoader = null,
    IEventBindingResolver? bindingResolver = null,
    IUIController? uiController = null,
    IStateQuery? stateQuery = null)
```

---

## 5. 修复步骤

### 步骤 1：确认接口实现

检查以下文件，确认接口实现关系：
- [ ] `FileDriven/FileDriven.Pipeline/PipelineEntry.cs` — 是否实现 `IConfigLoader`
- [ ] `FileDriven/FileDriven.Pipeline/ExecutionPipelineEntry.cs` — 是否实现 `IConfigLoader`
- [ ] `FileDriven/FileDriven.UI/Core/MainForm.cs` — 是否实现 `IUIController`

### 步骤 2：修改 `StartGuiMode()` 注册服务

在 `FileDrivenExecutor.cs` 的 `StartGuiMode()` 方法中，创建 `executionEntry` 之后，调用 `CommandFrameworkUsage.RegisterServices()` 注册服务。

### 步骤 3：修改 `StartCliMode()` 注册服务（可选）

在 `FileDrivenExecutor.cs` 的 `StartCliMode()` 方法中，同样注册服务，确保 CLI 模式也能正常工作。

### 步骤 4：构建验证

执行构建，确认无编译错误。

### 步骤 5：功能验证

执行 GUI 模式测试，确认 `sideload` 命令能正确加载配置，后续事件命令能正常找到控件绑定。

---

## 6. 回滚方案

如果修复引入新问题，可以：

1. 回滚 `FileDrivenExecutor.cs` 的修改
2. 或者注释掉 `RegisterServices()` 调用，恢复原有行为

---

## 7. 附录：相关文件列表

| 文件 | 作用 |
|------|------|
| `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` | 框架执行入口，需要修改 |
| `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs` | 管线工厂，创建命令子管线 |
| `FileDriven/FileDriven.Pipeline/PipelineEntry.cs` | 共享状态管理，可能实现 IConfigLoader |
| `FileDriven/FileDriven.Pipeline/ExecutionPipelineEntry.cs` | 执行管线入口，可能实现 IConfigLoader |
| `FileDriven/FileDriven.Pipeline/Core/NonEventCommandRegistry.cs` | 非事件命令注册中心 |
| `FileDriven/FileDriven.Execution/Command/Skeletons/NonEventCommandHandler.cs` | FileDriven 版本的非事件命令处理器 |
| `Share/CommandFramework/Execution/CommandRouter.cs` | CommandFramework 版本的非事件命令处理器 |
| `Share/CommandFramework/Pipeline/Stages/ParseCommandStage.cs` | 命令解析管线阶段 |
| `Share/CommandFramework/Pipeline/PipelineEntry.cs` | CommandFramework 管线入口 |
| `Share/CommandFramework/CommandFrameworkUsage.cs` | CommandFramework 全局单例 |
| `Share/CommandFramework/CommandFrameworkHost.cs` | CommandFramework 主机 |
| `Share/CommandFramework/Abstractions/IConfigLoader.cs` | 配置加载器接口 |
| `Share/CommandFramework/Abstractions/IUIController.cs` | UI 控制器接口 |
