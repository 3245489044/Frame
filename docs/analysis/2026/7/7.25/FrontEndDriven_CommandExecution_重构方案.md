# FrontEndDriven.CommandExecution 重构方案

> **版本**：v1.0  
> **日期**：2026-07-23  
> **状态**：方案设计  
> **关联**：`FileDriven.Execution` → `FrontEndDriven.CommandExecution` 重构迁移

---

## 目录

1. [背景与动机](#1-背景与动机)
2. [现状分析](#2-现状分析)
3. [目标架构](#3-目标架构)
4. [迁移清单](#4-迁移清单)
5. [详细设计](#5-详细设计)
6. [迁移步骤](#6-迁移步骤)
7. [依赖关系变化](#7-依赖关系变化)
8. [风险与应对](#8-风险与应对)

---

## 1. 背景与动机

### 1.1 当前问题

FrontEndDriven 已完成 UI 渲染子系统的重构（三收拢点：EcsInputPort / CsgEntryPoint / WinFormsOutputPort），但**命令执行逻辑仍然留在 FileDriven.Execution 中**，导致：

1. **职责分散**：UI 渲染在 FrontEndDriven，命令执行在 FileDriven.Execution，两者分离
2. **架构不一致**：FrontEndDriven 已建立三收拢点架构，但命令执行未纳入此体系
3. **依赖混乱**：FileDriven.Execution 同时依赖 FrontEndDriven 和 BackEndDriven，职责边界模糊
4. **复用困难**：其他前端框架（如 WdUI）无法复用命令执行能力

### 1.2 重构目标

将 FileDriven.Execution 中的**命令执行逻辑**整体迁移到 FrontEndDriven 中，作为 **CommandExecution 子系统**，遵循三收拢点设计原则：

```
FrontEndDriven/
├── UI 渲染子系统（已重构）
│   ├── EcsInputPort（输入）
│   ├── CsgEntryPoint（收集）
│   └── WinFormsOutputPort（输出）
│
└── 命令执行子系统（待迁移）
    ├── CommandInputPort（输入）
    ├── CommandPipeline（管线）
    └── CommandOutputPort（输出）
```

---

## 2. 现状分析

### 2.1 FileDriven.Execution 当前架构

```
FileDriven.Execution/
├── FileDrivenExecutor.cs          — 五层架构统一执行门面
├── FileDrivenExecutionEngine.cs   — Container 桥接引擎
├── ServiceCollectionExtensions.cs — DI 服务扩展
├── TemplatePreprocessSkeleton.cs  — 模板预处理
│
├── Pipeline/                      — 管线层（核心迁移目标）
│   ├── Stages/
│   │   ├── ParseCommandStage.cs   — 命令解析阶段
│   │   ├── ExecuteCommandStage.cs — 命令执行阶段
│   │   └── ResultCollectStage.cs  — 结果收集阶段
│   ├── Factory/
│   │   ├── PipelineFactory.cs     — 管线工厂
│   │   └── ScriptLoadSkeleton.cs  — 脚本加载骨架
│   ├── Runner/
│   │   ├── FileDrivenPipelineRunner.cs — 管线执行器
│   │   └── FileDrivenResultBuilder.cs  — 结果构建器
│   ├── Entry/
│   │   ├── ExecutionPipelineEntry.cs   — 执行入口
│   │   └── PipelineEntryExtensions.cs  — 扩展方法
│   └── PipelineEntryFactory.cs         — 入口工厂
│
├── Context/                      — 上下文层（核心迁移目标）
│   ├── FileDrivenPipelineContext.cs    — 管线上下文
│   ├── FileDrivenRuntimeContext.cs     — 运行时上下文
│   ├── ContextKeys.cs                  — 上下文键
│   ├── PipelineContextExtensions.cs    — 上下文扩展
│   ├── PipelineContextUtility.cs       — 上下文工具
│   ├── HandlerReentrancyGuard.cs       — 重入保护
│   └── NonEventCommandLookupAdapter.cs — 命令查找适配器
│
├── EventPipeline/                — 事件管线
│   └── EventHub.cs               — 事件中心
│
├── Diagnostics/                  — 诊断日志
│   ├── ExecutionLog.cs
│   └── execution_log_messages.json
│
├── Gui/                          — GUI 相关（保留）
│   ├── MainForm.cs
│   ├── FileDrivenDependencyInjector.cs
│   ├── FileDrivenGuiInitializerOrchestrator.cs
│   ├── FileDrivenScriptExecutor.cs
│   ├── FileDrivenStageConfigProvider.cs
│   └── Blocks/
│
└── Lifecycle/                    — 生命周期（保留）
    ├── FrameworkInitializer.cs
    ├── FrameworkShutdown.cs
    ├── FileDrivenLifecycle.cs
    └── LoggingInitializer.cs
```

### 2.2 FrontEndDriven 当前架构

```
FrontEndDriven/
├── Ports.cs                      — 三收拢点定义
│   ├── EcsInputPort              — ECS 输入端点
│   ├── CsgEntryPoint             — CSG 入口
│   └── WinFormsOutputPort        — WinForms 输出端点
│
├── RenderPipeline/               — 渲染管线
│   ├── EcsRenderPipeline.cs      — ECS 渲染管线
│   ├── UIReconciler.cs           — UI 差异比较引擎
│   └── EntityRegistry.cs         — Entity 注册中心
│
├── ControlRender/                — 控件渲染
├── ControlLookup/                — 控件查找
├── ControlInteraction/           — 控件交互
├── WinForms/                     — WinForms 适配
├── Protocol/                     — 协议层
├── Shared/                       — 共享工具
└── ComponentDataIntegration/     — 组件数据集成
```

### 2.3 核心依赖关系

**FileDriven.Execution 的依赖链：**
```
FileDriven.Execution
├── FrontEndDriven（UI 渲染）
├── BackEndDriven（通用抽象）
├── ExecutionCenter（任务调度）
├── CentralPipeline（管线编排）
├── Container（弹性执行）
├── CommandFramework（命令框架）
├── FileDriven.Pipeline（共享状态）
├── FileDriven.Abstractions（抽象接口）
└── FileDriven.Utilities（工具）
```

**FrontEndDriven 的依赖链：**
```
FrontEndDriven
├── BackEndDriven（通用抽象）
├── CommandFramework（命令框架）
├── EventBindingOrchestrator（事件绑定）
├── TConfig（统一配置）
├── InternalStorage（内部存储）
├── ComponentData（组件数据）
└── BasicMethod（基础框架）
```

---

## 3. 目标架构

### 3.1 整体架构

```
FrontEndDriven/
├── UI 渲染子系统（已重构）
│   ├── EcsInputPort（输入收拢点）
│   │   └── 加载 UI 配置 → 创建 Entity 树
│   ├── ECS World（核心）
│   │   ├── EntityManager
│   │   ├── ControlPool
│   │   └── Systems（Layout/Style/EventBinding）
│   ├── CsgEntryPoint（收集收拢点）
│   │   └── 读取 Component → CanonicalForm
│   └── WinFormsOutputPort（输出收拢点）
│       └── CanonicalForm → 控件差异更新
│
├── 命令执行子系统（待迁移）
│   ├── CommandInputPort（输入收拢点）★ 新增
│   │   ├── 接收命令文件路径
│   │   ├── 接收命令文本
│   │   └── 触发管线执行
│   ├── CommandPipeline（核心管线）
│   │   ├── ScriptLoadSkeleton（脚本加载）
│   │   ├── ForEach（命令遍历）
│   │   │   ├── ParseCommandStage（解析）
│   │   │   ├── ExecuteCommandStage（执行）
│   │   │   └── ResultCollectStage（收集）
│   │   └── PipelineFactory（管线工厂）
│   ├── CommandContext（上下文）
│   │   ├── CommandPipelineContext
│   │   ├── CommandRuntimeContext
│   │   └── ContextKeys
│   └── CommandOutputPort（输出收拢点）★ 新增
│       ├── 收集执行结果
│       ├── 构建 ExecutionResult
│       └── 输出到调用方
│
└── 共享基础设施
    ├── Ports.cs（五收拢点定义）
    ├── Diagnostics（诊断日志）
    └── Abstractions（抽象接口）
```

### 3.2 五收拢点设计

遵循 FrontEndDriven 的**关注点分离**原则，命令执行子系统新增两个收拢点，与 UI 渲染子系统的三个收拢点对称：

| 收拢点 | 子系统 | 方向 | 职责 |
|---|---|---|---|
| `EcsInputPort` | UI 渲染 | 输入 | 加载 UI 配置，创建 Entity 树 |
| `CsgEntryPoint` | UI 渲染 | 收集 | 读取 Component，构建 CanonicalForm |
| `WinFormsOutputPort` | UI 渲染 | 输出 | CanonicalForm → 控件差异更新 |
| **`CommandInputPort`** | **命令执行** | **输入** | **接收命令，触发管线执行** |
| **`CommandOutputPort`** | **命令执行** | **输出** | **收集结果，构建 ExecutionResult** |

### 3.3 命令执行管线流程

```
CommandInputPort
    │
    ▼
ScriptLoadSkeleton        — 加载 JSON/文本脚本 → string[]
    │
    ▼
ForEach (每条命令)
    │
    ├── ParseCommandStage  — 解析命令文本 → ParsedCommand
    │
    ├── ExecuteCommandStage — 分发执行（事件命令/非事件命令）
    │   └── IExecutionCell  — Container 弹性策略链（重试/熔断/超时）
    │
    └── ResultCollectStage — 收集执行结果 → CommandLineResult
    │
    ▼
CommandOutputPort          — 构建 ExecutionResult → 返回调用方
```

### 3.4 目录结构

```
FrontEndDriven/
├── CommandExecution/               # 命令执行子系统
│   ├── CommandInputPort.cs         # 输入收拢点 ★ 新增
│   ├── CommandOutputPort.cs        # 输出收拢点 ★ 新增
│   ├── CommandExecutionEngine.cs   # 执行引擎（从 FileDrivenExecutionEngine 迁移）
│   ├── CommandPipelineRunner.cs    # 管线执行器（从 FileDrivenPipelineRunner 迁移）
│   ├── CommandExecutionEntry.cs    # 执行入口（从 ExecutionPipelineEntry 迁移）
│   ├── PipelineEntryFactory.cs     # 入口工厂（从 PipelineEntryFactory 迁移）
│   ├── PipelineFactory.cs          # 管线工厂（从 PipelineFactory 迁移）
│   ├── ScriptLoadSkeleton.cs       # 脚本加载骨架（从 ScriptLoadSkeleton 迁移）
│   ├── ResultBuilder.cs            # 结果构建器（从 FileDrivenResultBuilder 迁移）
│   ├── TemplatePreprocessSkeleton.cs # 模板预处理（从 TemplatePreprocessSkeleton 迁移）
│   ├── ServiceCollectionExtensions.cs # DI 服务扩展（从 ServiceCollectionExtensions 迁移）
│   ├── EventHub.cs                 # 事件中心（从 EventHub 迁移）
│   │
│   ├── Stages/                     # 管线阶段
│   │   ├── ParseCommandStage.cs    # 命令解析阶段
│   │   ├── ExecuteCommandStage.cs  # 命令执行阶段
│   │   └── ResultCollectStage.cs   # 结果收集阶段
│   │
│   ├── Context/                    # 上下文
│   │   ├── CommandPipelineContext.cs    # 管线上下文
│   │   ├── CommandRuntimeContext.cs     # 运行时上下文
│   │   ├── ContextKeys.cs               # 上下文键
│   │   ├── PipelineContextExtensions.cs # 上下文扩展
│   │   ├── PipelineContextUtility.cs    # 上下文工具
│   │   ├── HandlerReentrancyGuard.cs    # 重入保护
│   │   └── NonEventCommandLookupAdapter.cs # 命令查找适配器
│   │
│   ├── Abstractions/              # 抽象接口
│   │   ├── ICommandPipeline.cs     # 命令管线接口
│   │   ├── ICommandContext.cs      # 命令上下文接口
│   │   ├── ICommandEntry.cs        # 命令入口接口
│   │   └── ICommandRunner.cs       # 命令执行器接口
│   │
│   └── Diagnostics/               # 诊断日志
│       ├── ExecutionLog.cs
│       └── execution_log_messages.json
│
├── Ports.cs                       # ★ 更新：新增 CommandInputPort / CommandOutputPort
│
└── ...（其余现有目录保持不变）
```

---

## 4. 迁移清单

### 4.1 迁移到 FrontEndDriven.CommandExecution 的文件

| # | 源文件 (FileDriven.Execution) | 目标文件 (FrontEndDriven.CommandExecution) | 变更类型 |
|---|---|---|---|
| 1 | `Pipeline/Stages/ParseCommandStage.cs` | `CommandExecution/Stages/ParseCommandStage.cs` | 重命名 + 消除 FileDriven 依赖 |
| 2 | `Pipeline/Stages/ExecuteCommandStage.cs` | `CommandExecution/Stages/ExecuteCommandStage.cs` | 重命名 + 消除 FileDriven 依赖 |
| 3 | `Pipeline/Stages/ResultCollectStage.cs` | `CommandExecution/Stages/ResultCollectStage.cs` | 重命名 + 消除 FileDriven 依赖 |
| 4 | `Pipeline/Factory/PipelineFactory.cs` | `CommandExecution/PipelineFactory.cs` | 重命名 + 消除 FileDriven 依赖 |
| 5 | `Pipeline/Factory/ScriptLoadSkeleton.cs` | `CommandExecution/ScriptLoadSkeleton.cs` | 重命名 + 消除 FileDriven 依赖 |
| 6 | `Pipeline/Runner/FileDrivenPipelineRunner.cs` | `CommandExecution/CommandPipelineRunner.cs` | 重命名 + 消除 FileDriven 依赖 |
| 7 | `Pipeline/Runner/FileDrivenResultBuilder.cs` | `CommandExecution/ResultBuilder.cs` | 重命名 + 消除 FileDriven 依赖 |
| 8 | `Pipeline/Entry/ExecutionPipelineEntry.cs` | `CommandExecution/CommandExecutionEntry.cs` | 重命名 + 消除 FileDriven 依赖 |
| 9 | `Pipeline/PipelineEntryFactory.cs` | `CommandExecution/PipelineEntryFactory.cs` | 重命名 + 消除 FileDriven 依赖 |
| 10 | `Pipeline/Entry/PipelineEntryExtensions.cs` | `CommandExecution/PipelineEntryExtensions.cs` | 重命名 + 消除 FileDriven 依赖 |
| 11 | `Context/FileDrivenPipelineContext.cs` | `CommandExecution/Context/CommandPipelineContext.cs` | 重命名 + 消除 FileDriven 依赖 |
| 12 | `Context/FileDrivenRuntimeContext.cs` | `CommandExecution/Context/CommandRuntimeContext.cs` | 重命名 + 消除 FileDriven 依赖 |
| 13 | `Context/ContextKeys.cs` | `CommandExecution/Context/ContextKeys.cs` | 重命名 |
| 14 | `Context/PipelineContextExtensions.cs` | `CommandExecution/Context/PipelineContextExtensions.cs` | 重命名 |
| 15 | `Context/PipelineContextUtility.cs` | `CommandExecution/Context/PipelineContextUtility.cs` | 重命名 + 消除 FileDriven 依赖 |
| 16 | `Context/HandlerReentrancyGuard.cs` | `CommandExecution/Context/HandlerReentrancyGuard.cs` | 重命名 |
| 17 | `Context/NonEventCommandLookupAdapter.cs` | `CommandExecution/Context/NonEventCommandLookupAdapter.cs` | 重命名 |
| 18 | `FileDrivenExecutionEngine.cs` | `CommandExecution/CommandExecutionEngine.cs` | 重命名 + 消除 FileDriven 依赖 |
| 19 | `ServiceCollectionExtensions.cs` | `CommandExecution/ServiceCollectionExtensions.cs` | 重命名 + 消除 FileDriven 依赖 |
| 20 | `TemplatePreprocessSkeleton.cs` | `CommandExecution/TemplatePreprocessSkeleton.cs` | 重命名 + 消除 FileDriven 依赖 |
| 21 | `EventPipeline/EventHub.cs` | `CommandExecution/EventHub.cs` | 重命名 |
| 22 | `Diagnostics/ExecutionLog.cs` | `CommandExecution/Diagnostics/ExecutionLog.cs` | 重命名 |
| 23 | `Diagnostics/execution_log_messages.json` | `CommandExecution/Diagnostics/execution_log_messages.json` | 复制 |

### 4.2 新增的文件

| # | 文件 | 说明 |
|---|---|---|
| 24 | `CommandExecution/CommandInputPort.cs` | 命令输入收拢点 |
| 25 | `CommandExecution/CommandOutputPort.cs` | 命令输出收拢点 |
| 26 | `CommandExecution/Abstractions/ICommandPipeline.cs` | 命令管线接口 |
| 27 | `CommandExecution/Abstractions/ICommandContext.cs` | 命令上下文接口 |
| 28 | `CommandExecution/Abstractions/ICommandEntry.cs` | 命令入口接口 |
| 29 | `CommandExecution/Abstractions/ICommandRunner.cs` | 命令执行器接口 |

### 4.3 保留在 FileDriven.Execution 中的文件

| 文件 | 保留原因 |
|---|---|
| `FileDrivenExecutor.cs` | FileDriven 特有执行门面（继承 FrameworkExecutor） |
| `Gui/MainForm.cs` | FileDriven 主窗口（WinForms 特有） |
| `Gui/FileDrivenDependencyInjector.cs` | FileDriven 依赖注入 |
| `Gui/FileDrivenGuiInitializerOrchestrator.cs` | GUI 初始化编排 |
| `Gui/FileDrivenScriptExecutor.cs` | 脚本执行器 |
| `Gui/FileDrivenStageConfigProvider.cs` | 阶段配置提供器 |
| `Gui/Blocks/*` | GUI 启动 Block 管线 |
| `Lifecycle/*` | 生命周期管理 |

### 4.4 迁移后 FileDriven.Execution 的简化结构

```
FileDriven.Execution/
├── FileDrivenExecutor.cs              — 门面（委托给 FrontEndDriven.CommandExecution）
├── Gui/                               — GUI 特有逻辑
│   ├── MainForm.cs
│   ├── FileDrivenDependencyInjector.cs
│   ├── FileDrivenGuiInitializerOrchestrator.cs
│   ├── FileDrivenScriptExecutor.cs
│   ├── FileDrivenStageConfigProvider.cs
│   └── Blocks/
├── Lifecycle/                         — 生命周期
│   ├── FrameworkInitializer.cs
│   ├── FrameworkShutdown.cs
│   ├── FileDrivenLifecycle.cs
│   └── LoggingInitializer.cs
└── FileDriven.Execution.csproj        — 简化依赖
```

---

## 5. 详细设计

### 5.1 CommandInputPort（命令输入收拢点）

```csharp
// Ports.cs 中新增

/// <summary>
/// 命令输入端口 — 所有命令进入 FrontEndDriven 的唯一入口
/// <para>★ 2026-07-23 新增：命令执行子系统输入收拢点。</para>
///
/// 职责：
///   1. 接收命令文件路径，加载脚本并执行
///   2. 接收命令文本，直接执行
///   3. 触发命令管线执行
///
/// 设计原则：
///   - 唯一输入入口：所有命令执行都经过这里
///   - 不直接操作控件：只更新 CommandContext，不操作 WinForms 控件
///   - 事件驱动：执行结果通过 CommandOutputPort 输出
///
/// 管线位置：管线外部调用，触发管线执行
/// 依赖：CommandPipelineRunner, CommandExecutionEntry
/// </summary>
public sealed class CommandInputPort
{
    private readonly CommandPipelineRunner _runner;
    private readonly CommandExecutionEntry _entry;

    public CommandInputPort(
        CommandPipelineRunner runner,
        CommandExecutionEntry entry)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    /// <summary>
    /// 执行命令文件
    /// </summary>
    public Task<CommandExecutionResult> ExecuteFileAsync(
        string filePath,
        ExecutionOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// 执行命令文本
    /// </summary>
    public Task<CommandExecutionResult> ExecuteTextAsync(
        string commandText,
        ExecutionOptions? options = null,
        CancellationToken ct = default);
}
```

### 5.2 CommandOutputPort（命令输出收拢点）

```csharp
// Ports.cs 中新增

/// <summary>
/// 命令输出端口 — 所有命令执行结果输出的唯一出口
/// <para>★ 2026-07-23 新增：命令执行子系统输出收拢点。</para>
///
/// 职责：
///   1. 收集 CommandPipelineContext 中的执行结果
///   2. 构建统一的 ExecutionResult
///   3. 输出到调用方
///
/// 设计原则：
///   - 唯一输出出口：所有命令执行结果都经过这里
///   - 只读操作：只读取 CommandContext，不修改任何状态
///   - 统一格式：所有结果统一为 ExecutionResult 格式
///
/// 管线位置：管线执行完毕后调用
/// 依赖：ResultBuilder
/// </summary>
public sealed class CommandOutputPort
{
    private readonly ResultBuilder _resultBuilder;

    public CommandOutputPort(ResultBuilder resultBuilder)
    {
        _resultBuilder = resultBuilder ?? throw new ArgumentNullException(nameof(resultBuilder));
    }

    /// <summary>
    /// 从 CommandPipelineContext 构建 ExecutionResult
    /// </summary>
    public ExecutionResult Collect(CommandPipelineContext context, TimeSpan executionTime);
}
```

### 5.3 CommandPipelineContext（命令管线上下文）

**关键变更**：消除对 `FileDriven.CLI`、`FileDriven.Pipeline` 的依赖

```csharp
// 迁移前：FileDrivenPipelineContext
public class FileDrivenPipelineContext : CommandContext, IBackEndPipelineContext
{
    public CliOptions? Options { get; set; }                    // ← 依赖 FileDriven.CLI
    public FileDriven.Pipeline.PipelineEntry? PipelineEntry { get; set; } // ← 依赖 FileDriven.Pipeline
    // ...
}

// 迁移后：CommandPipelineContext
public class CommandPipelineContext : CommandContext, IBackEndPipelineContext
{
    public ExecutionOptions? Options { get; set; }              // ← 通用选项类型
    // PipelineEntry 引用 → 通过 IContext 存储，不直接依赖
    // ...
}
```

### 5.4 ExecuteCommandStage（命令执行阶段）

**关键变更**：消除对 `FileDriven.Execution.Core`、`FileDriven.Execution.Context` 的依赖

```csharp
// 迁移前：ExecuteCommandStage
public class ExecuteCommandStage : PipelineStageBase<FileDrivenPipelineContext>
{
    private readonly IExecutionCell _executionCell;
    private readonly Func<string, string, (string?, List<string>?, string?)> _findBindingFunc;
    private readonly IFunctionExecutor _functionExecutor;
    // ...
}

// 迁移后：ExecuteCommandStage（命名空间变更，逻辑不变）
public class ExecuteCommandStage : PipelineStageBase<CommandPipelineContext>
{
    // 逻辑不变，仅泛型参数从 FileDrivenPipelineContext 改为 CommandPipelineContext
    // ...
}
```

### 5.5 ExecutionOptions（通用执行选项）

```csharp
// CommandExecution/Context/ExecutionOptions.cs

/// <summary>
/// 通用执行选项
/// <para>替代 FileDriven.CLI.CliOptions，消除对 FileDriven.CLI 的依赖。</para>
/// </summary>
public class ExecutionOptions
{
    public string? ConfigPath { get; set; }
    public string? CommandFile { get; set; }
    public bool AutoClose { get; set; }
    public string? PipeName { get; set; }
    public bool PipeMode { get; set; }
    public string Mode { get; set; } = "CLI"; // "CLI" / "GUI"
}
```

### 5.6 FrontEndDriven.csproj 新增依赖

```xml
<!-- 新增依赖 -->
<ProjectReference Include="..\..\Modules\ExecutionCenter\ExecutionCenter.csproj" />
<ProjectReference Include="..\..\Modules\Pipeline\Pipeline.csproj" />
<ProjectReference Include="..\..\BasicMethod\Container\Container.csproj" />
<ProjectReference Include="..\..\Modules\Registry\Registry.csproj" />
<ProjectReference Include="..\..\Modules\ExecutionFramework\ExecutionFramework.csproj" />
```

---

## 6. 迁移步骤

### Step 1：创建目录结构

在 FrontEndDriven 中创建 `CommandExecution/` 及其子目录：
- `CommandExecution/Stages/`
- `CommandExecution/Context/`
- `CommandExecution/Abstractions/`
- `CommandExecution/Diagnostics/`

### Step 2：迁移上下文（Context）

1. 复制 `FileDrivenPipelineContext.cs` → `CommandPipelineContext.cs`
2. 消除对 `FileDriven.CLI` 的依赖（`CliOptions` → `ExecutionOptions`）
3. 消除对 `FileDriven.Pipeline` 的依赖（`PipelineEntry` 通过 `IContext` 存储）
4. 复制 `FileDrivenRuntimeContext.cs` → `CommandRuntimeContext.cs`
5. 复制 `ContextKeys.cs` → 保持不变
6. 复制 `PipelineContextExtensions.cs` → 保持不变
7. 复制 `PipelineContextUtility.cs` → 消除 FileDriven 依赖
8. 复制 `HandlerReentrancyGuard.cs` → 保持不变
9. 复制 `NonEventCommandLookupAdapter.cs` → 保持不变

### Step 3：迁移管线阶段（Stages）

1. 复制 `ParseCommandStage.cs` → 泛型参数改为 `CommandPipelineContext`
2. 复制 `ExecuteCommandStage.cs` → 泛型参数改为 `CommandPipelineContext`
3. 复制 `ResultCollectStage.cs` → 泛型参数改为 `CommandPipelineContext`

### Step 4：迁移管线工厂和执行器

1. 复制 `PipelineFactory.cs` → 泛型参数改为 `CommandPipelineContext`
2. 复制 `ScriptLoadSkeleton.cs` → 泛型参数改为 `CommandPipelineContext`
3. 复制 `FileDrivenPipelineRunner.cs` → `CommandPipelineRunner.cs`
4. 复制 `ExecutionPipelineEntry.cs` → `CommandExecutionEntry.cs`
5. 复制 `PipelineEntryFactory.cs` → 保持不变
6. 复制 `PipelineEntryExtensions.cs` → 保持不变
7. 复制 `FileDrivenResultBuilder.cs` → `ResultBuilder.cs`

### Step 5：迁移执行引擎和辅助类

1. 复制 `FileDrivenExecutionEngine.cs` → `CommandExecutionEngine.cs`
2. 复制 `ServiceCollectionExtensions.cs` → 消除 FileDriven 依赖
3. 复制 `TemplatePreprocessSkeleton.cs` → 消除 FileDriven 依赖
4. 复制 `EventHub.cs` → 保持不变
5. 复制 `ExecutionLog.cs` → 保持不变
6. 复制 `execution_log_messages.json` → 保持不变

### Step 6：新增 Ports

1. 在 `Ports.cs` 中新增 `CommandInputPort` 类
2. 在 `Ports.cs` 中新增 `CommandOutputPort` 类

### Step 7：新增抽象接口

1. 创建 `ICommandPipeline.cs`
2. 创建 `ICommandContext.cs`
3. 创建 `ICommandEntry.cs`
4. 创建 `ICommandRunner.cs`

### Step 8：更新 FrontEndDriven.csproj

新增必要的 ProjectReference：
- `ExecutionCenter.csproj`
- `Pipeline.csproj`
- `Container.csproj`
- `Registry.csproj`
- `ExecutionFramework.csproj`

### Step 9：FileDriven.Execution 简化为薄包装

1. `FileDrivenExecutor.cs` 中的命令执行逻辑委托给 `FrontEndDriven.CommandExecution`
2. 删除已迁移的文件
3. 更新 `FileDriven.Execution.csproj` 移除不必要的依赖

### Step 10：编译验证

1. 编译 `FrontEndDriven.csproj` 验证迁移后的代码
2. 编译 `FileDriven.Execution.csproj` 验证简化后的代码
3. 编译 `MainProj.sln` 验证整体编译

---

## 7. 依赖关系变化

### 7.1 迁移前

```
FileDriven.Execution
├── FrontEndDriven（UI 渲染）
├── BackEndDriven（通用抽象）
├── ExecutionCenter（任务调度）
├── CentralPipeline（管线编排）
├── Container（弹性执行）
├── CommandFramework（命令框架）
├── FileDriven.Pipeline（共享状态）
├── FileDriven.Abstractions（抽象接口）
└── FileDriven.Utilities（工具）
```

### 7.2 迁移后

```
FrontEndDriven
├── BackEndDriven（通用抽象）
├── CommandFramework（命令框架）
├── EventBindingOrchestrator（事件绑定）
├── TConfig（统一配置）
├── InternalStorage（内部存储）
├── ComponentData（组件数据）
├── BasicMethod（基础框架）
├── ExecutionCenter（任务调度）          ★ 新增
├── Pipeline（管线编排）                  ★ 新增
├── Container（弹性执行）                 ★ 新增
├── Registry（函数注册）                  ★ 新增
└── ExecutionFramework（执行框架）        ★ 新增

FileDriven.Execution
├── FrontEndDriven（UI 渲染 + 命令执行）  ★ 依赖简化
├── FileDriven.Pipeline（共享状态）
├── FileDriven.Abstractions（抽象接口）
└── FileDriven.Utilities（工具）
```

### 7.3 依赖关系图

```
                    ┌─────────────────────┐
                    │   FileDriven.Launcher │
                    └─────────┬───────────┘
                              │
                    ┌─────────▼───────────┐
                    │ FileDriven.Execution │
                    │  (门面 + GUI + 生命周期) │
                    └──┬──────────────┬──┘
                       │              │
              ┌────────▼──┐    ┌──────▼────────┐
              │ FrontEnd  │    │ FileDriven     │
              │ Driven    │    │ .Pipeline      │
              └──┬─────┬──┘    └───────────────┘
                 │     │
        ┌────────▼─┐ ┌─▼──────────────┐
        │ UI 渲染  │ │ 命令执行       │
        │ 子系统   │ │ 子系统         │
        └──────────┘ └──┬─────────────┘
                        │
          ┌─────────────┼─────────────┐
          │             │             │
    ┌─────▼──┐   ┌──────▼──┐   ┌─────▼──┐
    │Execution│   │Pipeline │   │Container│
    │Center   │   │         │   │         │
    └─────────┘   └─────────┘   └─────────┘
```

---

## 8. 风险与应对

### 8.1 编译风险

| 风险 | 影响 | 应对 |
|---|---|---|
| `CommandPipelineContext` 替换 `FileDrivenPipelineContext` 导致外部引用编译失败 | 高 | 在 FileDriven.Execution 中保留类型别名或兼容包装 |
| `ExecutionOptions` 替换 `CliOptions` 导致属性缺失 | 中 | 确保 `ExecutionOptions` 覆盖 `CliOptions` 的所有必要属性 |
| 泛型参数变更导致 CentralPipeline 注册失败 | 中 | 保持 `IBackEndPipelineContext` 接口不变 |

### 8.2 运行时风险

| 风险 | 影响 | 应对 |
|---|---|---|
| 上下文数据丢失（`_contextFrame` 中的运行时依赖） | 高 | 确保 `CommandPipelineContext` 的 `IContext` 初始化逻辑与迁移前一致 |
| PipelineEntry 引用方式变更导致共享状态失效 | 高 | 通过 `IContext.SetData/GetData` 存储 PipelineEntry 引用 |
| 事件绑定查找委托失效 | 中 | 保持 `ToFindBindingFunc` 扩展方法不变 |

### 8.3 兼容性策略

1. **渐进式迁移**：先复制文件到新位置，再逐步消除 FileDriven 依赖
2. **类型别名**：在 FileDriven.Execution 中保留 `using CommandPipelineContext = FrontEndDriven.CommandExecution.Context.CommandPipelineContext;`
3. **接口不变**：`IBackEndPipelineContext` 接口保持不变，确保 CentralPipeline 兼容
4. **分步编译**：每完成一个 Step 就编译验证，避免大规模编译错误

---

## 附录 A：文件映射表

| 迁移前命名空间 | 迁移后命名空间 |
|---|---|
| `FileDriven.Execution.Pipeline.Stages` | `FrontEndDriven.CommandExecution.Stages` |
| `FileDriven.Execution.Pipeline` | `FrontEndDriven.CommandExecution` |
| `FileDriven.Execution.Context` | `FrontEndDriven.CommandExecution.Context` |
| `FileDriven.Execution.Diagnostics` | `FrontEndDriven.CommandExecution.Diagnostics` |
| `FileDriven.Execution.Core` | `FrontEndDriven.CommandExecution` |
| `FileDriven.Execution.EventPipeline` | `FrontEndDriven.CommandExecution` |

## 附录 B：类型映射表

| 迁移前类型 | 迁移后类型 |
|---|---|
| `FileDrivenPipelineContext` | `CommandPipelineContext` |
| `FileDrivenRuntimeContext` | `CommandRuntimeContext` |
| `FileDrivenPipelineRunner` | `CommandPipelineRunner` |
| `ExecutionPipelineEntry` | `CommandExecutionEntry` |
| `FileDrivenResultBuilder` | `ResultBuilder` |
| `FileDrivenExecutionEngine` | `CommandExecutionEngine` |
| `CliOptions` | `ExecutionOptions` |
