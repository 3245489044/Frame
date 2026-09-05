# 管线整合方案：FileDriven 合并到 CommandFramework

## 1. 概述

### 1.1 现状问题

当前系统存在**两套独立的管线体系**：

| 维度 | CommandFramework 管线（Share） | FileDriven 管线（Execution） |
|------|-------------------------------|------------------------------|
| 项目位置 | `Share/CommandFramework/` | `FileDriven/FileDriven.Execution/` |
| 管线入口 | `PipelineEntry.CreateDefaultPipeline()` | `PipelineFactory.CreateFileDrivenPipeline()` |
| 上下文 | `CommandContext` | `FileDrivenPipelineContext`（继承 CommandContext） |
| 阶段 | `ParseCommand → FindBinding → ExtractValues → ExecuteFunction → WriteResult` | `ScriptLoad → ForEach(ParseCommand → FindBinding → ExtractValues → ExecuteFunction → WriteResult)` |
| 非事件命令 | `NonEventCommandHandler`（echo/sleep/wait + `RegisterFileDrivenCommands()` 注册的 stub） | `NonEventCommandHandler`（switch 分支 + 完整实现） |
| 命令循环 | `ExecuteFileAsync()` 的 foreach 循环 | `ForEachPipeline` 管线内循环 |
| 脚本加载 | 无（直接读取命令文件） | `ScriptLoadSkeleton`（JSON 转换 + 命令缓存） |

### 1.2 整合目标

1. **统一管线入口**：只有一个 `PipelineEntry`，支持 CLI 和 GUI 两种模式
2. **统一非事件命令处理器**：只有一个 `NonEventCommandHandler`，包含所有命令的完整实现
3. **统一命令循环**：`ForEachPipeline` 作为通用能力进入 CommandFramework
4. **统一脚本加载**：`ScriptLoadSkeleton` 作为通用阶段进入 CommandFramework
5. **统一配置状态管理**：消除 `PipelineEntryInstance._loadedConfigs` 和 `PipelineEntry._loadedConfigs` 的状态分裂

---

## 2. 整体架构

### 2.1 三层结构

```
┌─────────────────────────────────────────────────────────┐
│  第1层：CentralPipeline<TContext>  (Share/CentralPipeline) │
│  通用管线执行引擎，支持 Skeleton/ForEach/Switch/Branch    │
└─────────────────────┬───────────────────────────────────┘
                      │ 泛型参数：CommandContext
┌─────────────────────▼───────────────────────────────────┐
│  第2层：CommandFramework  (Share/CommandFramework)       │
│  统一管线入口 + 统一阶段实现 + 统一非事件命令处理器       │
│  新增：ForEachPipeline 支持 + ScriptLoad 阶段             │
└─────────────────────┬───────────────────────────────────┘
                      │ 继承扩展
┌─────────────────────▼───────────────────────────────────┐
│  第3层：FileDriven.Execution  (FileDriven 特有逻辑)       │
│  仅保留 FileDriven 特有逻辑（UICommandManager 交互等）    │
└─────────────────────────────────────────────────────────┘
```

### 2.2 迁移清单

#### 迁移到 CommandFramework（Share 项目）

| 组件 | 源位置 | 目标位置 | 状态 |
|------|--------|---------|------|
| `NonEventCommands` | `FileDriven/FileDriven.Pipeline/Core/` | `Share/CommandFramework/` | ✅ 待迁移 |
| `ScriptLoadSkeleton` | `FileDriven/FileDriven.Execution/Command/Skeletons/` | `Share/CommandFramework/Pipeline/Stages/` | ✅ 已创建 `ScriptLoadStage` |
| `ForEachPipeline` 支持 | `BasicMethod/Pipeline/` | 保留原位，CommandFramework 增加扩展方法 | ⏳ 待实现 |
| `NonEventCommandHandler`（完整版） | `FileDriven/FileDriven.Execution/Command/Skeletons/` | `Share/CommandFramework/Execution/` | ✅ 已通过 `RegisterServiceCommands()` 实现 |
| `PipelineFactory` 的逻辑 | `FileDriven/FileDriven.Execution/Command/` | `Share/CommandFramework/Pipeline/` | ⏳ 待实现 |
| `ExecutionPipelineEntry` 的逻辑 | `FileDriven/FileDriven.Execution/Command/` | `Share/CommandFramework/Pipeline/` | ⏳ 待实现 |

#### 保留在 FileDriven 项目中的逻辑

| 组件 | 位置 | 说明 |
|------|------|------|
| `UICommandManager` | `FileDriven/FileDriven.UI/` | GUI 特有，不迁移 |
| `MainForm.LoadSideContainerAsync()` | `FileDriven/FileDriven.UI/` | GUI 特有，不迁移 |
| `WinFormsEventBinder` | `FunctionStorage/` | WinForms 特有，不迁移 |
| `EventTriggerService` | `FileDriven/FileDriven.UI/Event/` | WinForms 事件触发，不迁移 |
| `FileDrivenExecutor` | `FileDriven/FileDriven.Execution/` | 门面类，简化后保留 |

---

## 3. 详细设计

### 3.1 统一 `NonEventCommandHandler`

#### 改造前

两个 `NonEventCommandHandler`：

- **Share 版本**：通过 `_handlers` 字典注册处理器，默认注册 `echo/sleep/wait`，通过 `RegisterFileDrivenCommands()` 注册存根（无执行器时返回 Failure）
- **FileDriven 版本**：通过 switch 分支直接操作 `FileDrivenPipelineContext`，完整实现所有命令，依赖 `FileDrivenPipelineContext.PipelineEntry` 属性

#### 改造方案

通过 `RegisterServiceCommands()` 方法直接传递通用接口，在 NonEventCommandHandler 内部通过闭包捕获接口实例，实现完整命令逻辑：

```csharp
public void RegisterServiceCommands(
    IConfigLoader? configLoader = null,
    IEventBindingResolver? bindingResolver = null,
    IUIController? uiController = null,
    IStateQuery? stateQuery = null)
{
    // IConfigLoader → load / close / sideload
    if (configLoader != null)
    {
        RegisterHandler("load", cmd => HandleLoad(cmd, configLoader, uiController));
        RegisterHandler("close", cmd => HandleClose(cmd, configLoader));
        RegisterHandler("sideload", cmd => HandleSideload(cmd, configLoader));
    }
    // IStateQuery → look / discover / assert / list / validate
    if (stateQuery != null) { ... }
    // IUIController → show / exit
    if (uiController != null) { ... }
}
```

每个命令处理方法的签名统一为 `static Task<CommandExecutionResult<object?>> HandleXxx(ParsedCommand cmd, IXxxService service)`，通过接口方法完成具体操作，不再依赖 `FileDrivenPipelineContext`。

**保留向后兼容**：`RegisterFileDrivenCommands()` 和 `IFileDrivenCommandExecutor` 接口保留。

### 3.2 统一 `ScriptLoadSkeleton`

将 `ScriptLoadSkeleton` 泛型化，迁移到 CommandFramework：

```csharp
public class ScriptLoadStage : CommandStageBase
{
    private readonly IScriptConverter? _scriptConverter;

    public ScriptLoadStage(IScriptConverter? scriptConverter = null)
        : base("ScriptLoad", "Load script and convert to command file")
    {
        _scriptConverter = scriptConverter;
    }

    protected override Task ExecuteCoreAsync(CommandContext context, CancellationToken ct)
    {
        // 1. 验证 ScriptPath
        // 2. 如果是 .json，调用 IScriptConverter 转换
        // 3. 如果是 .txt，直接读取
        // 4. 缓存命令列表到 context.CommandLines
        // 5. 设置 context.ScriptLoadSuccess
    }
}

public interface IScriptConverter
{
    string? ConvertJsonScriptToCommandFile(string scriptPath);
}
```

### 3.3 统一命令循环（ForEachPipeline）

将 `ForEachPipeline` 作为 CommandFramework 的通用能力，新增 `CommandPipelineBuilder` 扩展方法：

```csharp
public static class CommandPipelineBuilder
{
    /// 创建标准命令管线（单条命令执行）
    public static IStagePipeline<CommandContext> CreateCommandPipeline(...) { }

    /// 创建文件驱动管线（批量命令执行）
    /// 阶段：ScriptLoad → ForEach(ParseCommand → FindBinding → ExtractValues → ExecuteFunction → WriteResult)
    public static CentralPipeline<CommandContext> CreateFileDrivenPipeline(...)
    {
        return new PipelineBuilder<CommandContext>(name)
            .RegisterService<IFunctionExecutor>(functionExecutor)
            .RegisterService<IEventBindingResolver>(bindingResolver)
            .Register(PipelineStage.PreProcessing, new ScriptLoadStage(converter))
            .AddForEach(PipelineStage.Execution,
                itemsSelector: ctx => ctx.CommandLines ?? Array.Empty<string>(),
                itemPipeline: CreateCommandPipeline(), ...)
            .Build();
    }
}
```

### 3.4 统一配置状态管理

#### 现状

- `CommandFramework.PipelineEntryInstance`（Share）有自己的 `_loadedConfigs` 字典
- `FileDriven.Pipeline.PipelineEntry`（FileDriven）有自己的 `_loadedConfigs` 字典
- 两个字典独立，没有同步机制

#### 整合方案

消除 `PipelineEntryInstance`，统一使用 `PipelineEntry`（FileDriven 版本迁移到 Share）：

```csharp
public class PipelineEntry : IDisposable
{
    private readonly ConcurrentDictionary<string, object> _loadedConfigs;
    private readonly EventBindingResolver _bindingResolver;
    private readonly ConfigurationCache _configCache;

    public IReadOnlyDictionary<string, object> LoadedConfigs => ...;
    public IEventBindingResolver BindingResolver => ...;

    public LoadResult LoadConfig(string configPath, bool showForm = false, object? uiCommandManager = null) { }
    public IEventBindingInfo? FindBinding(string controlName, string action) { }
}
```

### 3.5 统一 `FindBindingFunc` 委托传递机制

统一为**服务注册**方式，通过 `PipelineBuilder.RegisterService<T>()` 注册：

```csharp
new PipelineBuilder<CommandContext>(name)
    .RegisterService<IEventBindingResolver>(bindingResolver)
    .RegisterService<IFunctionExecutor>(functionExecutor)
    .Register(PipelineStage.PreProcessing, new ScriptLoadStage())
    .AddForEach(PipelineStage.Execution, ...)
    .Build();
```

---

## 4. 迁移步骤

| 步骤 | 内容 | 状态 |
|------|------|------|
| **第1步** | 在 CommandFramework 中新增接口和类型（IScriptConverter/ScriptLoadStage/CommandPipelineBuilder） | ✅ 已完成 |
| **第2步** | 合并 NonEventCommandHandler（通过 RegisterServiceCommands() 实现完整命令） | ✅ 已完成 |
| **第3步** | 统一 PipelineEntry（迁移 FileDriven 版本到 Share，删除 PipelineEntryInstance） | ⏳ 待实现 |
| **第4步** | 统一管线构建（迁移 PipelineFactory 逻辑到 CommandPipelineBuilder） | ⏳ 待实现 |
| **第5步** | 简化 FileDriven.Execution（ExecutionPipelineEntry 简化为薄包装） | ⏳ 待实现 |

---

## 5. 影响分析

### 5.1 正面影响

| 方面 | 改善 |
|------|------|
| 可维护性 | 消除重复代码，单一职责 |
| 可调试性 | 统一管线阶段，清晰的调用链 |
| 可扩展性 | 新增命令只需在 `NonEventCommandHandler` 注册 |
| 状态一致性 | 消除状态分裂，配置加载路径唯一 |
| 测试性 | 接口抽象便于单元测试 |

### 5.2 风险

| 风险 | 缓解措施 |
|------|---------|
| 接口抽象可能遗漏某些 FileDriven 特有逻辑 | 逐命令审查，确保每个命令的完整实现迁移 |
| 迁移过程中可能破坏现有功能 | 分步迁移，每步完成后运行现有测试 |
| `ScriptLoadStage` 泛型化可能引入复杂性 | 保持简单，只抽象必要的依赖 |

---

## 6. 整合后的架构图

```
┌──────────────────────────────────────────────────────────────────┐
│                    FileDrivenExecutor                            │
│  (门面类，简化后仅做初始化协调和模式分发)                         │
└──────────────────────────┬───────────────────────────────────────┘
                           │
┌──────────────────────────▼───────────────────────────────────────┐
│                    CommandFrameworkHost                          │
│  (统一入口，管理所有组件生命周期)                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  PipelineEntry (统一版本)                                 │   │
│  │  ├── _loadedConfigs (ConcurrentDictionary)               │   │
│  │  ├── EventBindingResolver                                │   │
│  │  ├── ConfigurationCache                                  │   │
│  │  ├── LoadConfig()                                        │   │
│  │  └── FindBinding()                                       │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  NonEventCommandHandler (统一版本)                        │   │
│  │  ├── echo/sleep/wait                                     │   │
│  │  ├── load/close/show/look/assert/discover                │   │
│  │  ├── list/validate/exit/sideload                         │   │
│  │  └── 通过 RegisterServiceCommands() 注册                  │   │
│  └──────────────────────────────────────────────────────────┘   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  CommandPipelineBuilder                                  │   │
│  │  ├── CreateCommandPipeline() (单条命令)                   │   │
│  │  └── CreateFileDrivenPipeline() (文件驱动)                │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
                           │
┌──────────────────────────▼───────────────────────────────────────┐
│                    CentralPipeline<TContext>                     │
│  (通用管线执行引擎)                                               │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  PreProcessing: ScriptLoadStage                          │   │
│  │  Execution: ForEachPipeline                              │   │
│  │    ├── ParseCommandStage                                 │   │
│  │    ├── FindBindingStage                                  │   │
│  │    ├── ExtractValuesStage                                │   │
│  │    ├── ExecuteFunctionStage                              │   │
│  │    └── WriteResultStage                                  │   │
│  └──────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

---

## 7. 通用化改造实施总结

### 7.1 改造目标

将 CommandFramework 的管线改造成能够**完全覆盖 FileDriven 管线逻辑**的通用框架，通过接口抽象消除对 FileDriven 具体实现的依赖。

### 7.2 新增通用接口（Abstractions 目录）

| 接口 | 文件 | 用途 |
|------|------|------|
| `IConfigLoader` | `Abstractions/IConfigLoader.cs` | 配置加载器，提供 LoadConfig/RemoveConfig/ClearConfigs |
| `IEventBindingInfo` | `Abstractions/IEventBindingResolver.cs` | 事件绑定信息模型 |
| `IEventBindingResolver` | `Abstractions/IEventBindingResolver.cs` | 事件绑定解析器，提供 FindBinding/ResolveEventType |
| `IUIController` | `Abstractions/IUIController.cs` | UI 控制器，提供 ShowForm/CloseForm/Shutdown |
| `IScriptConverter` | `Abstractions/IScriptConverter.cs` | 脚本转换器，提供 ConvertJsonScriptToCommandFile |
| `IStateQuery` | `Abstractions/IStateQuery.cs` | 状态查询器，提供 GetLoadedConfigNames/HasControlValue |

### 7.3 新增通用模型（Models 目录）

| 模型 | 文件 | 用途 |
|------|------|------|
| `LoadResult` | `Models/LoadResult.cs` | 配置加载结果，包含 IsSuccess/ConfigName/ErrorMessage |

### 7.4 改造的组件

#### NonEventCommandHandler（CommandRouter.cs）

**新增 `RegisterServiceCommands()` 方法**，通过通用接口注册完整命令实现：

```csharp
public void RegisterServiceCommands(
    IConfigLoader? configLoader = null,
    IEventBindingResolver? bindingResolver = null,
    IUIController? uiController = null,
    IStateQuery? stateQuery = null)
```

- **IConfigLoader** → load / close / sideload（完整实现，非存根）
- **IStateQuery** → look / discover / assert / list / validate（完整实现）
- **IUIController** → show / exit（完整实现）

**保留向后兼容**：`RegisterFileDrivenCommands()` 和 `IFileDrivenCommandExecutor` 接口保留。

#### PipelineEntryInstance（PipelineEntry.cs）

| 新增方法 | 说明 |
|---------|------|
| `RegisterServices()` | 将通用服务注册到 NonEventCommandHandler |
| `LoadConfig()` | 委托给 IConfigLoader |
| `FindBinding()` | 委托给 IEventBindingResolver |

#### CommandFrameworkHost

| 新增方法 | 说明 |
|---------|------|
| `RegisterServices()` | 委托给 NonEventCommandHandler 和 PipelineInstance |
| `LoadConfig()` | 委托给 PipelineInstance |

#### CommandFrameworkUsage

| 新增方法 | 说明 |
|---------|------|
| `RegisterServices()` | 静态方法，委托给 Host |
| `LoadConfig()` | 静态方法，委托给 Host |

### 7.5 新增通用阶段

| 阶段 | 文件 | 用途 |
|------|------|------|
| `ScriptLoadStage` | `Pipeline/Stages/ScriptLoadStage.cs` | 脚本加载阶段，支持 .json（通过 IScriptConverter）和 .txt |

### 7.6 CommandContext 新增字段

| 字段 | 用途 |
|------|------|
| `ScriptPath` | 脚本文件路径 |
| `CommandFilePath` | 转换后的命令文件路径 |
| `CommandLines` | 从脚本文件读取的命令行列表 |
| `ScriptLoadSuccess` | 脚本加载是否成功 |
| `ScriptLoadErrorMessage` | 脚本加载错误信息 |

### 7.7 使用示例

```csharp
// 1. 创建 Host
using var host = CommandFrameworkHost.CreateDefault();

// 2. 注册通用服务（FileDriven 项目实现这些接口）
host.RegisterServices(
    configLoader: new FileDrivenConfigLoader(),
    bindingResolver: new FileDrivenEventBindingResolver(),
    uiController: new FileDrivenUIController(),
    stateQuery: new FileDrivenStateQuery());

// 3. 使用 load 命令加载配置
var result = await host.ExecuteCommandAsync("load myconfig.json");

// 4. 或直接调用 LoadConfig
var loadResult = host.LoadConfig("myconfig.json");

// 5. 使用 look 命令查看已加载配置
var lookResult = await host.ExecuteCommandAsync("look");

// 6. 使用 show 命令显示窗口
var showResult = await host.ExecuteCommandAsync("show MainForm");
```

### 7.8 与 FileDriven 的映射关系

| FileDriven 逻辑 | CommandFramework 通用接口 | 说明 |
|-----------------|--------------------------|------|
| `PipelineEntry.LoadConfig()` | `IConfigLoader.LoadConfig()` | 配置加载 |
| `PipelineEntry.FindBinding()` | `IEventBindingResolver.FindBinding()` | 事件绑定查找 |
| `UICommandManager.ShowForm()` | `IUIController.ShowForm()` | UI 操作 |
| `ScriptLoadSkeleton` | `ScriptLoadStage` + `IScriptConverter` | 脚本加载 |
| `NonEventCommandHandler`（FileDriven 版） | `NonEventCommandHandler.RegisterServiceCommands()` | 非事件命令 |
| `PipelineEntry._loadedConfigs` | `IStateQuery.GetLoadedConfigNames()` | 状态查询 |

### 7.9 总结

本次通用化改造的核心思路是：

1. **接口抽象**：通过 6 个通用接口（IConfigLoader/IEventBindingResolver/IUIController/IScriptConverter/IStateQuery）消除对 FileDriven 具体实现的依赖
2. **服务注册**：通过 `RegisterServiceCommands()` / `RegisterServices()` 方法注入通用服务
3. **完整实现**：NonEventCommandHandler 中的业务命令从存根升级为完整实现
4. **新增阶段**：ScriptLoadStage 作为通用阶段，支持 JSON 和 TXT 脚本
5. **向后兼容**：保留 `RegisterFileDrivenCommands()` 和 `IFileDrivenCommandExecutor` 接口

整合完成后，FileDriven 项目只需实现 6 个通用接口并注入到 CommandFramework，即可获得完整的管线能力。所有通用管线能力都在 CommandFramework 中，FileDriven 项目只保留 GUI 特有逻辑。
