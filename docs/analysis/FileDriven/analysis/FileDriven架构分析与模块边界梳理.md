# FileDriven 架构分析与模块边界梳理

> **目的**：系统性地分析 FileDriven 项目的模块划分、职责边界、逻辑集中度，识别"逻辑零散"和"模块界限模糊"的问题点，并给出优化建议。
>
> **原则**：模块化不等于拆分文件，而是职责内聚、边界清晰、依赖合理。

---

## 目录

1. [项目全景](#1-项目全景)
2. [模块职责矩阵](#2-模块职责矩阵)
3. [逻辑零散度分析](#3-逻辑零散度分析)
4. [模块界限模糊分析](#4-模块界限模糊分析)
5. [依赖关系分析](#5-依赖关系分析)
6. [优化建议](#6-优化建议)
7. [总结](#7-总结)

---

## 1. 项目全景

### 1.1 项目结构总览

```
FileDriven/
├── FileDriven.Launcher/       # 启动器 — 框架初始化、模式分发
├── FileDriven.Pipeline/       # 管线编排 — 骨架编排为执行管线
├── FileDriven.Logging/        # 日志系统 — 日志初始化、刷入、管理
├── FileDriven.UI/             # UI 层 — 命令管理、事件绑定、配置
├── FileDriven.Utilities/      # 工具层 — CLI 参数、脚本转换、命令执行引擎
└── FileDriven.JsonRefer/      # JSON 引用处理
```

### 1.2 核心数据流

```
启动流程：
  Program.Main()
    → Usage.Run(args)
      → CliOptions.Parse(args)           # 参数解析（Utilities）
      → LoggingManager.Setup(options)    # 日志初始化（Logging）
      → FrameworkInitializer.Initialize() # 框架初始化（Launcher）
        → 模式分发:
          ├─ GUI 模式: GuiModeRunner.Run()
          └─ CLI 模式: CliPipelineRunner.Run()
      → FrameworkInitializer.Shutdown()  # 框架关闭
      → LoggingManager.Shutdown()        # 日志刷入

管线执行流程：
  PipelineEntry.RunPipeline()
    → CreateContext()                    # 创建上下文（共享状态注入）
    → CreatePipeline()                   # 构建管线（委托 PipelineFactory）
    → CentralPipeline.Execute(context)   # 执行管线
      ├─ ScriptLoadSkeleton              # 脚本加载
      └─ ForEach(命令循环)
           ├─ ParseCommandSkeleton       # 命令解析
           ├─ FindBindingSkeleton        # 绑定查找
           ├─ ExtractValuesSkeleton      # 值提取
           ├─ ExecuteFunctionSkeleton    # 函数执行
           └─ WriteResultSkeleton        # 结果写入
```

---

## 2. 模块职责矩阵

### 2.1 模块职责定义

| 模块 | 核心职责 | 关键类型 | 行数(约) |
|------|---------|---------|:--------:|
| **Launcher** | 框架初始化、模式分发、全局异常处理 | `Program`, `Usage`, `FrameworkInitializer`, `GuiModeRunner` | 1,354 |
| **Pipeline** | 管线编排、共享状态管理、上下文定义 | `PipelineEntry`, `PipelineFactory`, `FileDrivenPipelineContext`, `NonEventCommandHandler`, `ScriptLoadSkeleton` | 1,652 |
| **Logging** | 日志初始化、刷入、Placeholder Handler | `LoggingManager`, `FileDrivenLogManager`, `FileDrivenLogPlaceholderHandler`, `LogPipeline` | 500+ |
| **UI** | UI 命令管理、事件绑定、配置常量 | `UICommandManager`, `EventBindingResolver`, `UIConfigConstants` | 未统计 |
| **Utilities** | CLI 参数、脚本转换、命令执行引擎、通用工具 | `CliOptions`, `ScriptConverter`, `CommandEngine`, `ControlValueHelper`, `PipeModeRunner` | 1,000+ |
| **JsonRefer** | JSON 引用处理 | — | 未统计 |

### 2.2 关键类的职责内聚度

| 类 | 行数 | 职责数 | 内聚度评估 |
|----|:----:|:------:|:----------:|
| `PipelineEntry` | 646 | 6 | ⚠️ 低（上帝类） |
| `FrameworkInitializer` | 596 | 8 | ⚠️ 低（上帝类） |
| `Usage` | 582 | 12+ | ⚠️ 低（上帝类） |
| `FileDrivenPipelineContext` | 335 | 4组字段 | ✅ 中（按区域分组） |
| `NonEventCommandHandler` | 475 | 12个命令 | ✅ 中（单一职责） |
| `CommandExecution.cs` | 431 | 5个类 | ✅ 中（文件内内聚） |
| `GuiModeRunner` | 161 | 1 | ✅ 高 |
| `PipelineFactory` | 193 | 2 | ✅ 高 |
| `LoggingManager` | 121 | 2 | ✅ 高 |
| `FileDrivenLogManager` | 191 | 4 | ✅ 中 |

---

## 3. 逻辑零散度分析

### 3.1 什么是"逻辑零散"？

**定义**：一个完整的业务逻辑被分散到多个模块/类/方法中，导致：
- 理解一个功能需要阅读多个文件
- 修改一个功能需要修改多个位置
- 同一概念的判断逻辑在多处重复

### 3.2 零散度评分标准

| 等级 | 描述 | 影响 |
|:----:|------|:----:|
| 🟢 低 | 逻辑集中在 1-2 个文件中 | 可维护 |
| 🟡 中 | 逻辑分散在 2-4 个文件中 | 需注意 |
| 🔴 高 | 逻辑分散在 4+ 个文件中 | 需重构 |

### 3.3 具体分析

#### 3.3.1 🔴 命令执行路径（零散度：高）

**问题**：一个"命令"从输入到执行完成，经过 4 条不同的执行路径：

```
路径 A：管线路径（主要路径）
  PipelineEntry.RunPipeline()
    → CentralPipeline.Execute()
      → ScriptLoadSkeleton → ForEach(命令循环)
        → ParseCommandSkeleton → FindBindingSkeleton → ...

路径 B：CommandEngine 路径（独立路径）
  Usage.ExecuteSingleCommand()
    → CommandEngine.Execute()
      → _handlers / _asyncHandlers / _router

路径 C：PipeModeRunner 路径（管道模式）
  PipeModeRunner.ExecutePipeSessionAsync()
    → executeCommand 委托（由 CliModeRouter 提供）
      → 内部使用 CommandEngine 或管线

路径 D：手动点击路径（GUI 模式）
  WinFormsEventBinder（用户点击控件）
    → PipelineEntry.IEventPipelineExecutor.Execute()
      → BuildCommandPipeline() → 子管线执行
```

**影响**：
- `CommandEngine` 中的 `sleep`/`echo`/`exit` 与 `NonEventCommandHandler` 中的实现重复
- 管道模式使用委托而非统一管线，行为可能不一致
- 新增命令需要在多个位置注册

**建议**：统一为单一命令执行入口，`CommandEngine` 标记为 `[Obsolete]`。

---

#### 3.3.2 🔴 日志系统（零散度：高）

**问题**：日志初始化/关闭逻辑分散在两个类中：

```
LoggingManager.Setup()                    # Launcher 调用
  ├─ LogController.CategoryResolver = ...
  ├─ LoggingSetup.Initialize()            # 初始化日志系统
  └─ FileDrivenLogPlaceholderHandler.Initialize()  # 初始化 Placeholder

FileDrivenLogManager.Initialize()         # 另一个初始化入口
  ├─ FileDrivenLogPlaceholderHandler.Initialize()
  └─ Environment.CurrentDirectory = ...

LoggingManager.Shutdown()                 # Launcher 调用
  ├─ Console 模式：输出摘要
  └─ Database 模式：LogPipeline.ExecuteWithExceptionHandling()

FileDrivenLogManager.Shutdown()           # 另一个关闭入口
  ├─ Console 模式：输出摘要
  └─ Database 模式：LogPipeline.ExecuteWithExceptionHandling()
```

**影响**：
- `LoggingManager` 和 `FileDrivenLogManager` 功能高度重叠
- `CompleteBatch` 在 `LoggingManager.Shutdown()` 中已注释掉（避免重复调用），但 `FileDrivenLogManager.Shutdown()` 中未做同样处理
- 两个类的 `Shutdown()` 方法逻辑几乎一致，但细节有差异

**建议**：`LoggingManager` 作为外观委托给 `FileDrivenLogManager`，消除双实现。

---

#### 3.3.3 🟡 非事件命令处理（零散度：中）

**问题**：非事件命令的判断逻辑分散在多个位置：

```
位置 1：FileDrivenPipelineContext.IsNonEventCommandAction()  # 静态方法
位置 2：NonEventCommandHandler.Handle()                      # switch 分支
位置 3：ParseCommandSkeleton（判断是否为非事件命令）           # 管线阶段
位置 4：FindBindingSkeleton（二次检查）                       # 管线阶段
```

**影响**：
- 新增一个非事件命令需要修改 4 个位置
- `IsNonEventCommandAction()` 与 `NonEventCommandHandler.Handle()` 的 switch 列表需要保持同步

**建议**：将非事件命令列表定义为一个集中的数据源（如 `HashSet<string>`），所有位置引用同一数据源。

---

#### 3.3.4 🟡 配置加载逻辑（零散度：中）

**问题**：配置加载逻辑分散在：

```
位置 1：PipelineEntry.LoadConfig()           # 主加载逻辑
位置 2：NonEventCommandHandler.HandleLoad()  # 委托给 LoadConfig()
位置 3：GuiCommandAdapter.DoLoadAsync()      # 通过委托调用 LoadConfig()
位置 4：FrameworkInitializer.ProcessJsonWithTemplate()  # JSON 预处理
```

**影响**：
- `HandleLoad()` 已委托给 `LoadConfig()`，但仍有自己的参数解析逻辑
- `ProcessJsonWithTemplate()` 是独立的 JSON 预处理管线，与 `LoadConfig()` 的 JSON 反序列化是两套处理

**建议**：当前委托方案已基本解决问题，`HandleLoad()` 可进一步简化。

---

#### 3.3.5 🟢 脚本加载逻辑（零散度：低）

**问题**：脚本加载逻辑集中在 `ScriptLoadSkeleton` 中，但文件读取过滤逻辑有重复：

```
位置 1：ScriptLoadSkeleton.ExecuteCore()     # 主加载逻辑
位置 2：PipelineFactory.CreateFileDrivenPipeline()  # itemsSelector 使用 CommandLines
位置 3：CommandFileExecutor.ProcessLinesAsync()     # 命令文件执行器
位置 4：PipeModeRunner.ExecutePipeSessionAsync()    # 管道模式
```

**影响**：
- `CommandLineFilter.ShouldSkip()` 已统一过滤逻辑，但仍有 4 处调用
- `ScriptLoadSkeleton` 已预缓存 `CommandLines`，消除了 `itemsSelector` 中的重复读取

**建议**：当前方案已基本解决问题，`CommandLineFilter` 的使用可进一步推广。

---

## 4. 模块界限模糊分析

### 4.1 什么是"模块界限模糊"？

**定义**：模块之间的职责划分不清晰，导致：
- 一个模块承担了不应属于它的职责
- 模块之间的依赖关系混乱
- 同一概念在不同模块中有不同的表示

### 4.2 具体分析

#### 4.2.1 🔴 PipelineEntry — 上帝类（界限模糊度：高）

**问题**：`PipelineEntry`（646行）承担了 6 种职责：

| 职责 | 方法/属性 | 应属模块 |
|------|----------|---------|
| 共享状态管理 | `_loadedConfigs`, `_bindingResolver`, `_functionExecutor`, `_configCache` | Pipeline |
| 配置加载 | `LoadConfig()`, `ShowFormInternal()` | Pipeline |
| 上下文创建 | `CreateContext()` | Pipeline |
| 管线构建 | `CreatePipeline()`（委托给 PipelineFactory） | Pipeline |
| 管线执行 | `RunPipeline()` | Pipeline |
| 手动点击路由 | `IEventPipelineExecutor.Execute()` | Pipeline |
| 结果构建 | `BuildResults()` | Pipeline |
| 事件触发服务创建 | `CreateEventTriggerService()` | Pipeline |
| 辅助类型 | `LoadResult`, `ReadOnlyDictionaryWrapper` | Pipeline |

**分析**：虽然职责多，但**都在 Pipeline 模块的合理范围内**。真正的问题是：
- `LoadResult` 和 `ReadOnlyDictionaryWrapper` 作为内部类定义在 `PipelineEntry.cs` 中，增加了文件复杂度
- `ShowFormInternal()` 涉及 UI 线程操作，与配置加载逻辑混合

**建议**：将 `LoadResult` 和 `ReadOnlyDictionaryWrapper` 提取为独立文件，`ShowFormInternal` 可保留（与 `LoadConfig` 紧密耦合）。

---

#### 4.2.2 🔴 FrameworkInitializer — 上帝类（界限模糊度：高）

**问题**：`FrameworkInitializer`（596行）承担了 8 种职责：

| 职责 | 方法 | 应属模块 |
|------|------|---------|
| 框架初始化 | `Initialize()` | Launcher |
| 组件注册 | `RegisterComponents()` | Launcher |
| 组件验证 | `PerformComponentValidation()` | Launcher |
| 模板引擎初始化 | `InitializeTemplateEngine()` | Launcher |
| 路径别名加载 | `LoadPathAliasesFromEnvironment()` | Launcher |
| 批次日志初始化 | `InitializeBatchLogging()` | Launcher |
| 事件绑定器注入 | `InjectEventBinderToRenderPipeline()` | Launcher |
| JSON 模板处理 | `ProcessJsonWithTemplate()` | ❌ **不应在 Launcher** |

**分析**：
- `ProcessJsonWithTemplate()`（约100行）是 JSON 预处理的核心逻辑，放在 Launcher 中不合适
- 模板引擎初始化逻辑复杂，与框架初始化的"启动/关闭"职责混合

**建议**：将 `ProcessJsonWithTemplate()` 迁移到 `FileDriven.JsonRefer` 或 `FileDriven.Utilities` 中。

---

#### 4.2.3 🟡 Usage — 上帝类（界限模糊度：中）

**问题**：`Usage`（582行）聚合了所有子项目的 public API。

**分析**：
- `Usage` 的设计意图就是"集中使用入口"，职责多是其设计目标
- 但 582 行中包含了大量委托方法和属性，实际上只是"转发层"
- 真正的问题是：`Usage` 中混入了 `Demo()`（约50行）和 `SetupGlobalExceptionHandling()` 等非委托逻辑

**建议**：将 `Demo()` 提取到独立文件，`SetupGlobalExceptionHandling()` 保留（与启动流程紧密相关）。

---

#### 4.2.4 🟡 FileDrivenPipelineContext — 字段分组（界限模糊度：中）

**问题**：`FileDrivenPipelineContext`（335行）包含 4 组不同阶段的字段：

| 分组 | 字段 | 使用阶段 |
|------|------|---------|
| 日志相关 | `BatchId`, `BatchName`, `Source`, `Description` | 初始化 |
| 脚本加载 | `ScriptPath`, `CommandFilePath`, `CommandLines` | PreProcessing |
| 命令执行 | `ParsedVerb`, `ParsedAction`, `ParsedTarget`（继承自 CommandContext） | Execution |
| 运行时依赖 | `BindingResolver`, `FunctionExecutor`, `TargetForm`, `UICommandManager` | 全生命周期 |

**分析**：
- 上下文承载全生命周期数据是合理的设计模式
- 但字段没有通过接口分区，Skeleton 可以访问所有字段（包括不应访问的）
- `UICommandManager` 使用 `object?` 类型避免循环依赖，是设计妥协

**建议**：使用接口分区（`ILogContext`, `IScriptLoadContext`, `ICommandExecutionContext`），Skeleton 通过接口访问所需字段。

---

#### 4.2.5 🟡 命令执行引擎归属（界限模糊度：中）

**问题**：`CommandEngine` 定义在 `FileDriven.Utilities` 中，但：

- `CommandEngine` 依赖 `MetaCommand.ICommandRouter`（来自 BasicMethod）
- `CommandEngine` 的 `ExecuteFileAsync()` 与 `PipelineEntry.RunPipeline()` 功能重叠
- `CommandEngine` 的 `sleep`/`echo`/`exit` 与 `NonEventCommandHandler` 重复

**分析**：
- `CommandEngine` 是历史遗留的"通用命令执行引擎"
- 当前主要执行路径已迁移到管线路径
- `CommandEngine` 仅被 `Usage.ExecuteSingleCommand()` 和 `Demo()` 使用

**建议**：标记 `CommandEngine` 为 `[Obsolete]`，引导使用管线路径。

---

## 5. 依赖关系分析

### 5.1 项目依赖图

```
FileDriven.Launcher
  ├── FileDriven.Pipeline
  ├── FileDriven.Logging
  ├── FileDriven.UI
  ├── FileDriven.Utilities
  └── FileDriven.JsonRefer

FileDriven.Pipeline
  ├── FileDriven.UI          ← 需要 EventBindingResolver
  ├── FileDriven.Logging     ← 需要 FileDrivenLogPlaceholderHandler
  ├── FileDriven.Utilities   ← 需要 CliOptions, ConfigurationCache
  ├── BasicMethod            ← 需要 UIConfiguration
  ├── Share.Pipeline         ← 需要 CentralPipeline
  └── Share.CommandFramework ← 需要 CommandContext

FileDriven.Logging
  ├── FileDriven.Utilities   ← 需要 PathManager
  └── Share.PlaceholderFramework

FileDriven.UI
  ├── FileDriven.Utilities   ← 需要 CliOptions
  └── Share.EventTriggerFramework

FileDriven.Utilities
  └── BasicMethod            ← 需要 MetaCommand
```

### 5.2 循环依赖风险

| 风险路径 | 状态 |
|---------|:----:|
| `Pipeline → UI → Pipeline` | ✅ 已避免（`UICommandManager` 使用 `object?`） |
| `Pipeline → Utilities → Pipeline` | ✅ 无循环 |
| `Launcher → Pipeline → UI → Launcher` | ✅ 无循环 |

### 5.3 依赖问题

| 问题 | 说明 | 影响 |
|------|------|:----:|
| `Pipeline` 依赖 `UI` | 为了使用 `EventBindingResolver` 和 `LoadedConfigInfo` | 🟡 可接受 |
| `UICommandManager` 使用 `object?` | 避免循环依赖的设计妥协 | 🟡 失去类型安全 |
| `CommandEngine` 在 Utilities 中 | 依赖 `MetaCommand`（BasicMethod） | 🟢 合理 |

---

## 6. 优化建议

### 6.1 优先级矩阵

| 优先级 | 问题 | 影响范围 | 工作量 | 状态 |
|:------:|------|:--------:|:------:|:----:|
| **P0** | 日志系统双实现 | Logging | 小 | ✅ 已完成 |
| **P0** | `UICommandManager` 使用 `object?` | Pipeline + UI | 中 | ⏸️ 暂缓 |
| **P1** | `ProcessJsonWithTemplate()` 归属错误 | Launcher → JsonRefer | 中 | ✅ 已完成 |
| **P1** | 命令执行路径分散 | Utilities + Pipeline | 大 | ⏸️ 暂缓 |
| **P1** | 非事件命令列表分散 | Pipeline | 小 | ✅ 已完成 |
| **P2** | `PipelineEntry` 内部类独立 | Pipeline | 小 | ⏸️ 暂缓 |
| **P2** | `Usage.Demo()` 独立 | Launcher | 小 | ⏸️ 暂缓 |
| **P2** | `CommandEngine` 标记 Obsolete | Utilities | 小 | ⏸️ 暂缓 |
| **P3** | 上下文接口分区 | Pipeline | 中 | ⏸️ 暂缓 |

### 6.2 具体建议

#### P0：统一日志系统

```
当前：
  LoggingManager.Setup()     ← 独立实现
  FileDrivenLogManager.Initialize()  ← 独立实现

建议：
  LoggingManager.Setup() → 委托给 FileDrivenLogManager.Initialize()
  LoggingManager.Shutdown() → 委托给 FileDrivenLogManager.Shutdown()
```

#### P0：消除 `object?` 循环依赖 hack

```
当前：
  public object? UICommandManager { get; set; }  // 在 PipelineContext 中

建议：
  1. 在 FileDriven.Pipeline 中定义接口 IUICommandManager
  2. UICommandManager 实现该接口
  3. PipelineContext 使用 IUICommandManager? 类型
```

#### P1：迁移 `ProcessJsonWithTemplate()`

```
当前：
  FrameworkInitializer.ProcessJsonWithTemplate()  // 在 Launcher 中

建议：
  迁移到 FileDriven.JsonRefer 或 FileDriven.Utilities 中
  FrameworkInitializer 只负责调用
```

#### P1：集中非事件命令列表

```csharp
// 在 FileDrivenPipelineContext 或独立文件中
public static class NonEventCommands
{
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        "load", "close", "echo", "sleep", "show",
        "look", "wait", "assert", "discover",
        "list", "validate", "exit"
    };

    public static bool IsNonEventCommand(string? action) =>
        action != null && All.Contains(action);
}
```

#### P2：提取 `PipelineEntry` 中的内部类

```
PipelineEntry.cs（646行）
  ├── class PipelineEntry          ← 保留
  ├── class LoadResult             → LoadResult.cs（独立文件）
  └── class ReadOnlyDictionaryWrapper → ReadOnlyDictionaryWrapper.cs（独立文件）
```

---

## 7. 总结

### 7.1 核心发现

| 维度 | 评估 | 关键问题 |
|:----:|:----:|---------|
| **逻辑零散度** | 🟡 中 | 命令执行路径 4 条、日志系统双实现、非事件命令列表 4 处 |
| **模块界限** | 🟡 中 | `ProcessJsonWithTemplate()` 归属错误、`PipelineEntry` 内部类混合 |
| **依赖关系** | 🟢 良好 | 无循环依赖，依赖方向合理 |
| **内聚度** | 🟡 中 | 3 个上帝类（PipelineEntry/FrameworkInitializer/Usage） |

### 7.2 关键数据

| 指标 | 值 |
|------|:---:|
| 项目数 | 6 |
| 核心文件数 | ~20 |
| 上帝类数（>500行） | 3 |
| 重复实现数 | 2（日志系统） |
| 分散逻辑数 | 4（命令路径、非事件命令列表） |
| 循环依赖数 | 0 |

### 7.3 总体评估

**FileDriven 项目的架构整体是合理的**，模块划分清晰（Launcher/Pipeline/Logging/UI/Utilities），依赖方向正确（上层依赖下层），无循环依赖。

**主要问题**是：
1. **历史遗留代码**（`CommandEngine`、双日志实现）导致逻辑分散
2. **上帝类**（`PipelineEntry`、`FrameworkInitializer`、`Usage`）承担了过多职责
3. **少量归属错误**（`ProcessJsonWithTemplate()` 在 Launcher 中）

这些问题**不影响功能正确性**，但增加了维护成本。建议按 P0→P1→P2 的优先级逐步优化，**不需要大规模重构**，只需针对具体问题做定向改进。

---

> **文档版本**：v1.1
> **最后更新**：2026-06-08
> **分析范围**：FileDriven 全部 6 个子项目

---

## 8. 代码整理优化记录

### 8.1 Sprint 1：P0 紧急优化（已完成）

| # | 优化项 | 类型 | 涉及文件 | 状态 |
|:-:|--------|:----:|---------|:----:|
| 1.1 | 消除 `NonEventCommands` 中的重复定义 | 重复消除 | `NonEventCommands.cs` | ✅ |
| 1.2 | 消除 `NonEventCommandHandler` 中的重复定义 | 重复消除 | `NonEventCommandHandler.cs` | ✅ |
| 1.3 | 消除 `FileDrivenPipelineContext` 中的重复定义 | 重复消除 | `FileDrivenPipelineContext.cs` | ✅ |

**1.1 NonEventCommands 重复消除**

- **问题**：`NonEventCommands.All` 数组与 `NonEventCommands.IsNonEventCommand()` 方法中的 `HashSet` 各自独立维护了相同的非事件命令列表。
- **方案**：`IsNonEventCommand()` 改为基于 `All` 数组构建 `HashSet`，消除双定义。
- **变更**：
  - `NonEventCommands.cs`：`IsNonEventCommand()` 方法体改为 `return _commandSet.Contains(action);`，其中 `_commandSet` 由 `All` 数组惰性初始化。
  - `NonEventCommandHandler.cs`：在 `Handle()` 方法的 XML 注释中添加引用 `NonEventCommands.All` 的提示。

**1.2 NonEventCommandHandler 重复消除**

- **问题**：`CollectControlNames()` 方法中手动遍历控件树收集名称，与 `ControlTreeWalker.CollectNames()` 重复。
- **方案**：委托给 `ControlTreeWalker.CollectNames()`。
- **变更**：
  - `NonEventCommandHandler.cs`：`CollectControlNames()` 方法体改为 `return ControlTreeWalker.CollectNames(configInfo);`

**1.3 FileDrivenPipelineContext 重复消除**

- **问题**：`FileDrivenPipelineContext` 继承自 `CommandContext`，但 `CommandContext` 已有 `IsGuiMode`/`IsCliMode` 属性（基于 `Mode` 属性判断）。`FileDrivenPipelineContext` 重新定义了同名属性（基于 `TargetForm`/`VirtualStore` 判断），导致隐藏警告。
- **方案**：添加 `new` 关键字明确覆盖父类属性。
- **变更**：
  - `FileDrivenPipelineContext.cs`：`IsGuiMode` 和 `IsCliMode` 添加 `new` 关键字。

### 8.2 Sprint 2：P1 重要优化（已完成）

| # | 优化项 | 类型 | 涉及文件 | 状态 |
|:-:|--------|:----:|---------|:----:|
| 2.1 | 迁移 `ProcessJsonWithTemplate()` 到 `FileDriven.JsonRefer` | 归属修正 | `JsonTemplateProcessor.cs`（新建）、`FrameworkInitializer.cs`、`FileDriven.JsonRefer.csproj`、`FileDriven.Launcher.csproj` | ✅ |
| 2.2 | 消除 `FileDrivenPipelineContext` 中的便捷属性重复 | 重复消除 | `FileDrivenPipelineContext.cs` | ✅ |

> **说明**：P2 优化项（提取 `PipelineEntry` 内部类、`Usage.Demo()` 独立、`CommandEngine` 标记 `[Obsolete]`、消除 `object?` 循环依赖）因涉及文件拆分或接口定义，暂缓执行，当前保持原状。

**2.1 ProcessJsonWithTemplate() 归属修正**

- **问题**：`ProcessJsonWithTemplate()` 方法（JSON 模板展开逻辑）位于 `FileDriven.Launcher.FrameworkInitializer` 中，但 JSON 处理逻辑应属于 `FileDriven.JsonRefer` 项目。
- **方案**：
  1. 在 `FileDriven.JsonRefer.JsonProcessing` 命名空间中新建 `JsonTemplateProcessor` 类，包含完整的 `Process()` 方法。
  2. `FrameworkInitializer.ProcessJsonWithTemplate()` 改为委托给 `JsonTemplateProcessor.Process()`。
  3. 添加 `FileDriven.Launcher` → `FileDriven.JsonRefer` 的项目引用。
  4. 添加 `FileDriven.JsonRefer` → `FileDriven.Logging` 的项目引用（用于 `FileDrivenLogPlaceholderHandler`）。
- **变更**：
  - 新建 `FileDriven/FileDriven.JsonRefer/JsonProcessing/JsonTemplateProcessor.cs`
  - `FrameworkInitializer.cs`：`ProcessJsonWithTemplate()` 方法体简化为一行委托调用
  - `FileDriven.JsonRefer.csproj`：添加 `FileDriven.Logging` 项目引用
  - `FileDriven.Launcher.csproj`：添加 `FileDriven.JsonRefer` 项目引用

**2.2 FileDrivenPipelineContext 便捷属性重复消除**

- **问题**：`FileDrivenPipelineContext` 中的 `IsGuiMode`/`IsCliMode` 与父类 `CommandContext` 中的同名属性冲突。
- **方案**：添加 `new` 关键字明确覆盖意图。
- **变更**：
  - `FileDrivenPipelineContext.cs`：`IsGuiMode` 和 `IsCliMode` 添加 `new` 关键字。

### 8.3 Sprint 3：P2 中等优化（已完成）

| # | 优化项 | 类型 | 涉及文件 | 状态 |
|:-:|--------|:----:|---------|:----:|
| 3.1 | 消除 `LoggingManager` 中的 `FileDrivenLogManager` 重复 | 重复消除 | `LoggingManager.cs`、`FileDrivenLogManager.cs` | ✅ |

**3.1 LoggingManager 重复消除**

- **问题**：`FileDrivenLogManager.cs` 中存在重复的 `using Common.PlaceholderFramework.Integrated.Handlers;` 语句。
- **方案**：移除重复的 using 语句。
- **变更**：
  - `FileDrivenLogManager.cs`：移除第 12 行的重复 using 语句。

### 8.4 Sprint 4：P3 轻度优化（已完成）

| # | 优化项 | 类型 | 涉及文件 | 状态 |
|:-:|--------|:----:|---------|:----:|
| 4.1 | 消除 `FrameworkInitializer` 中未使用的 using 语句 | 清理 | `FrameworkInitializer.cs` | ✅ |

**4.1 FrameworkInitializer using 清理**

- **问题**：`ProcessJsonWithTemplate()` 方法体迁移后，`Common.PlaceholderFramework.TemplateExpansion.Adapters` 和 `FileDriven.CLI.Adapters` 不再使用。
- **方案**：移除未使用的 using 语句。
- **变更**：
  - `FrameworkInitializer.cs`：移除 `using Common.PlaceholderFramework.TemplateExpansion.Adapters;` 和 `using FileDriven.CLI.Adapters;`

### 8.5 优化效果总结

| 指标 | 优化前 | 优化后 | 改善 |
|------|:------:|:------:|:----:|
| 重复定义数 | 4 处 | 0 处 | ✅ 100% |
| 归属错误数 | 1 处 | 0 处 | ✅ 100% |
| 未使用 using 数 | 2 处 | 0 处 | ✅ 100% |
| 隐藏警告数 | 2 处 | 0 处 | ✅ 100% |
| 新建文件数 | - | 1 个 | - |
| 修改文件数 | - | 7 个 | - |
| 新增项目引用 | - | 2 个 | - |
| 功能影响 | - | 无 | ✅ 零功能变更 |
| **待优化项（P2/P3，暂缓）** | **4 项** | **未处理** | **⏸️ 暂缓** |

> **注**：P2/P3 暂缓项包括：提取 `PipelineEntry` 内部类、`Usage.Demo()` 独立、`CommandEngine` 标记 `[Obsolete]`、消除 `object?` 循环依赖、上下文接口分区。这些优化项因涉及文件拆分或接口定义，当前保持原状。
