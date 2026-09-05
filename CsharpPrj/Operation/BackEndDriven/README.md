# BackEndDriven — 后端驱动执行框架

> **路径**: `D:\Project\Program\WorkFlowPrj\CsharpPrj\Operation\BackEndDriven`
> **命名空间**: `BackEndDriven`
> **设计哲学**: 万物皆函数，一切皆配置

---

## 目录

- [BackEndDriven — 后端驱动执行框架](#backenddriven--后端驱动执行框架)
  - [目录](#目录)
  - [1. 架构概览](#1-架构概览)
    - [架构分层](#架构分层)
  - [2. 目录结构](#2-目录结构)
  - [3. 核心组件](#3-核心组件)
    - [3.1 BackEndDrivenBuilder — 统一构建入口](#31-backenddrivenbuilder--统一构建入口)
      - [核心配置方法](#核心配置方法)
      - [构建方法](#构建方法)
      - [使用示例](#使用示例)
    - [3.2 ExecutionEngine — 通用执行引擎](#32-executionengine--通用执行引擎)
      - [三级执行入口](#三级执行入口)
      - [执行流程](#执行流程)
    - [3.3 PipelineEntry — 管线入口](#33-pipelineentry--管线入口)
      - [默认管线阶段](#默认管线阶段)
      - [静态工厂方法](#静态工厂方法)
      - [PipelineEntryInstance](#pipelineentryinstance)
    - [3.4 ExecuteCommandStage — 统一命令执行阶段](#34-executecommandstage--统一命令执行阶段)
      - [click 命令流程（框架反转）](#click-命令流程框架反转)
    - [3.5 Invoke — 参数执行工作流](#35-invoke--参数执行工作流)
      - [ParameterExecutionWorkflow（默认实现）](#parameterexecutionworkflow默认实现)
      - [WorkflowMethodExecutionWorkflow（基于 WorkFlowMethodUsage）](#workflowmethodexecutionworkflow基于-workflowmethodusage)
      - [关键区别](#关键区别)
  - [4. 框架反转设计](#4-框架反转设计)
    - [设计意图](#设计意图)
    - [反转点](#反转点)
    - [切换方式](#切换方式)
  - [5. 管线架构](#5-管线架构)
    - [简化历程](#简化历程)
    - [当前管线阶段](#当前管线阶段)
    - [ExecuteCommandStage 内部路由](#executecommandstage-内部路由)
  - [6. 执行流程](#6-执行流程)
    - [CLI 模式完整流程](#cli-模式完整流程)
    - [GUI 模式完整流程](#gui-模式完整流程)
  - [7. 使用方式](#7-使用方式)
    - [快速开始（CLI 模式）](#快速开始cli-模式)
    - [快速开始（GUI 模式）](#快速开始gui-模式)
    - [使用 PipelineEntryInstance](#使用-pipelineentryinstance)
  - [8. 扩展指南](#8-扩展指南)
    - [自定义参数执行工作流](#自定义参数执行工作流)
    - [添加自定义管线阶段](#添加自定义管线阶段)
    - [切换工作流实现](#切换工作流实现)
  - [相关文档](#相关文档)

---

## 1. 架构概览

BackEndDriven 是 WorkFlowPrj 的核心执行框架，提供：

- **统一构建入口**：`BackEndDrivenBuilder` Facade，链式配置所有核心组件
- **通用执行引擎**：`ExecutionEngine`，封装管线创建→执行→结果收集的完整流程
- **简化管线架构**：`ParseCommand → ExecuteCommand → ResultCollect` 三阶段管线
- **框架反转**：`IParameterExecutionWorkflow` 接口由 Share 层定义，BackEndDriven 提供实现
- **双模式支持**：CLI 模式（VirtualStore）和 GUI 模式（真实控件）

### 架构分层

```
┌─────────────────────────────────────────────────────────────┐
│                    Share 层（接口定义）                        │
│  IParameterExecutionWorkflow / CommandContext / Pipeline     │
└──────────────────────┬──────────────────────────────────────┘
                       │ 实现
┌──────────────────────▼──────────────────────────────────────┐
│              BackEndDriven（执行框架实现）                     │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐   │
│  │ Builder      │  │ Engine       │  │ Pipeline         │   │
│  │ (Facade)     │→ │ (Execution)  │→ │ (Parse→Exec→Result)│  │
│  └──────────────┘  └──────────────┘  └──────────────────┘   │
│                          │                                    │
│  ┌───────────────────────▼────────────────────────────────┐  │
│  │              Invoke（参数执行工作流）                     │  │
│  │  ┌──────────────────────────────────────────────────┐  │  │
│  │  │ WorkflowMethodExecutionWorkflow（唯一实现）       │  │  │
│  │  │ (基于 WorkFlowMethodUsage)                       │  │  │
│  │  │ ⚠ ParameterExecutionWorkflow 已废弃              │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

---

## 2. 目录结构

```
BackEndDriven/
├── BackEndDrivenBuilder.cs          # 统一构建入口（Facade）
├── BackEndDriven.csproj             # 项目文件
├── backend_log_messages.json        # 日志消息配置
│
├── Abstractions/                    # 抽象接口
│   ├── IBackEndPipelineContext.cs    # 通用管线上下文接口
│   └── ValidatorBase.cs             # 验证器基类
│
├── Adapters/                        # 适配器
│   ├── IResultAdapter.cs            # 结果适配器接口
│   ├── ResultAdapterBase.cs         # 结果适配器基类
│   └── CommandLineResultAdapter.cs  # 命令行结果适配器
│
├── Diagnostics/                     # 诊断日志
│   ├── BackEndLog.cs                # 后端日志
│   ├── IocLog.cs                    # IoC 日志
│   ├── LogBase.cs                   # 日志基类
│   └── EventBindingDiagnosticsService.cs  # 事件绑定诊断服务
│
├── Execution/                       # 执行引擎
│   ├── ExecutionEngine.cs           # 通用执行引擎（核心）
│   ├── ICommandExecutionService.cs  # 命令执行服务接口
│   ├── CommandExecutionService.cs   # 命令执行服务
│   ├── EventExecutionService.cs     # 事件执行服务
│   ├── FrameworkExecutor.cs         # 框架执行器
│   ├── ModeDispatcher.cs            # 模式调度器
│   └── GlobalExceptionHandler.cs    # 全局异常处理
│
├── Init/                            # 初始化
│   └── StageConfigProvider.cs       # 阶段配置提供者
│
├── Invoke/                          # ★ 参数执行工作流（框架反转核心）
│   ├── README.md                    # Invoke 子模块文档
│   ├── ArgumentBuilder.cs           # 统一参数构建器
│   ├── FunctionExecution.cs         # 函数执行器（FunctionExecutor）
│   ├── ~~ParameterExecutionWorkflow.cs~~ # ⚠ 已废弃，文件已删除
│   └── WorkflowMethodExecutionWorkflow.cs  # ★ 基于 WorkFlowMethodUsage 的唯一实现
│
├── Lifecycle/                       # 生命周期
│   ├── FrameworkInitializer.cs      # 框架初始化器
│   └── LifecycleManager.cs          # 生命周期管理器
│
├── Models/                          # 模型
│   ├── BatchContextData.cs          # 批次上下文数据
│   ├── EventTriggerResult.cs        # 事件触发结果
│   └── ExecutionResult.cs           # 执行结果
│
├── Pipeline/                        # ★ 管线系统（核心）
│   ├── PipelineEntry.cs             # 管线入口（静态工厂 + 实例类）
│   ├── BackEndExecutionPipelineEntry.cs  # 通用执行管线入口
│   ├── BackEndPipelineRunner.cs     # 管线运行器
│   ├── BackEndEventHub.cs           # 事件中心
│   ├── BackEndScriptLoadSkeleton.cs # 脚本加载骨架
│   ├── ExecuteCommandStage.cs       # ★ 统一命令执行阶段（核心）
│   ├── ForEachSkeleton.cs           # 循环骨架
│   ├── IRuntimeDependencyAware.cs   # 运行时依赖感知接口
│   ├── LoadConvertCacheSkeleton.cs  # 加载转换缓存骨架
│   ├── PipelineContext.cs           # 管线上下文
│   ├── PipelineStageBase.cs         # 管线阶段基类
│   └── RuntimeDependencyExtensions.cs # 运行时依赖扩展
│
├── Services/                        # 服务（预留）
│
└── Utilities/                       # 工具类
    └── AsyncHelper.cs               # 异步同步等待辅助
```

---

## 3. 核心组件

### 3.1 BackEndDrivenBuilder — 统一构建入口

**文件**: `BackEndDrivenBuilder.cs`

Facade 模式，封装所有核心组件的创建和配置，支持链式调用。

#### 核心配置方法

| 方法 | 用途 |
|------|------|
| `WithPipeline(pipeline)` | 设置管线实例 |
| `WithCommandRegistry(registry)` | 设置命令注册中心 |
| `WithCommandParser(parser)` | 设置命令解析器 |
| `WithBindingFinderStrategy(strategy)` | 设置控件查找策略（GUI 模式） |
| `WithLogger(logger)` | 设置日志记录器 |
| `RegisterCommands(registration)` | 注册额外命令 |
| `WithParameterExecutionWorkflow(workflow)` | ★ 设置自定义参数执行工作流 |
| `WithWorkflowFunctionExecutor(executor)` | 设置函数执行器（默认工作流使用） |
| `WithWorkflowControlFinder(finder)` | 设置控件查找器（GUI 模式） |
| `WithWorkflowControlValueWriter(writer)` | 设置控件值读写器（GUI 模式） |
| `WithWorkflowMethodExecution()` | ★ 使用 WorkFlowMethodUsage 作为工作流实现 |

#### 构建方法

| 方法 | 返回 | 用途 |
|------|------|------|
| `BuildWorkflow()` | `IParameterExecutionWorkflow?` | 构建参数执行工作流 |
| `BuildEngine()` | `ExecutionEngine` | 构建执行引擎 |
| `BuildExecutor()` | `FrameworkExecutor` | 构建框架执行器 |
| `BuildDispatcher()` | `ModeDispatcher` | 构建模式调度器 |

#### 使用示例

```csharp
// 方式一：使用 WorkflowMethodExecutionWorkflow（推荐，唯一实现）
var builder = new BackEndDrivenBuilder()
    .WithPipeline(pipeline)
    .WithCommandRegistry(registry)
    .WithCommandParser(parser)
    .WithWorkflowMethodExecution();

var engine = builder.BuildEngine();
var workflow = builder.BuildWorkflow(); // 返回 WorkflowMethodExecutionWorkflow

// 方式二：完全自定义工作流
var builder2 = new BackEndDrivenBuilder()
    .WithPipeline(pipeline)
    .WithCommandRegistry(registry)
    .WithCommandParser(parser)
    .WithParameterExecutionWorkflow(new MyCustomWorkflow());

var engine2 = builder2.BuildEngine();
var workflow2 = builder2.BuildWorkflow(); // 返回自定义工作流

// ⚠ ParameterExecutionWorkflow 已废弃，请勿使用
```

---

### 3.2 ExecutionEngine — 通用执行引擎

**文件**: `Execution/ExecutionEngine.cs`

封装管线创建 → 上下文创建 → 执行 → 结果收集的完整流程。

#### 三级执行入口

| 方法 | 用途 |
|------|------|
| `ExecuteFile(commandFile)` | 文件级执行（整个命令文件） |
| `ExecuteBatch(commandFile)` | 批量级执行（逐条执行，精确统计） |
| `ExecuteCommand(commandText)` | 单命令执行 |

每个方法都有对应的异步版本（`*Async`）。

#### 执行流程

```
ExecuteFile/ExecuteBatch/ExecuteCommand
    │
    ▼
创建 PipelineContext
    │
    ▼
执行 Pipeline.ExecuteAsync(command)
    │
    ▼
CollectResult → ExecutionResult
```

---

### 3.3 PipelineEntry — 管线入口

**文件**: `Pipeline/PipelineEntry.cs`

提供便捷的静态方法创建和配置标准命令管线。

#### 默认管线阶段

```
ParseCommand → ExecuteCommand → ResultCollect
```

#### 静态工厂方法

```csharp
// 创建默认管线
var pipeline = PipelineEntry.CreateDefaultPipeline(
    parser: parser,
    functionExecutor: executor,
    bindingFinder: findBinding,
    nonEventCommandLookup: lookup,
    controlFinder: finder,
    controlValueWriter: writer,
    parameterExecutionWorkflow: workflow);
```

#### PipelineEntryInstance

实例类，支持共享状态管理和执行历史：

```csharp
var instance = new PipelineEntryInstance(parser, nonEventLookup);
instance.RegisterServices(configLoader, bindingResolver, uiController);
var context = await instance.RunCommandAsync("click Button1");
```

---

### 3.4 ExecuteCommandStage — 统一命令执行阶段

**文件**: `Pipeline/ExecuteCommandStage.cs`

根据 action 类型统一处理所有命令：

| Action | 处理方式 |
|--------|----------|
| `set` | 设置控件值（GUI/CLI） |
| `click` | 查找绑定 → 委托给 `IParameterExecutionWorkflow` |
| 非事件命令 | 委托给 `INonEventCommandLookup` |

#### click 命令流程（框架反转）

```
HandleClick
    │
    ▼
FindBinding(target, eventType) → BindingResult
    │
    ▼
HandleClickWithWorkflow
    │
    ├─ 构建 ParameterExecutionRequest
    │
    ▼
_parameterExecutionWorkflow.ExecuteAsync(request)
    │
    ├─ ParameterExecutionWorkflow（默认）
    │   ReadControlValues → ExecuteFunction → WriteResult
    │
    └─ WorkflowMethodExecutionWorkflow（新）
        ReadControlValues → BuildWorkflowContext → CallBridgedFunctionAsync → WriteResult
```

---

### 3.5 Invoke — 参数执行工作流

**文件**: `Invoke/` 目录

框架反转的核心，提供两种实现：

#### ParameterExecutionWorkflow（默认实现）

```
ReadControlValues → string?[] → FunctionExecutor.ExecuteAsync → WriteResult
```

- 参数传递：`string?[]` 数组
- 执行方式：`IFunctionExecutor.ExecuteAsync`
- 初始化：手动注入 FunctionExecutor
- 函数类型：仅通用函数

#### WorkflowMethodExecutionWorkflow（基于 WorkFlowMethodUsage）

```
ReadControlValues → WorkflowContext.Parameters → CallBridgedFunctionAsync → WriteResult
                                                              │
                                                    ┌─────────▼─────────┐
                                                    │ WorkFlowMethodUsage│
                                                    │ 内部自动选择：      │
                                                    │ ├─ 工作流函数      │
                                                    │ └─ 通用函数（桥接） │
                                                    └───────────────────┘
```

- 参数传递：`WorkflowContext.Parameters` (Dictionary)
- 参数传出：✅ `WriteBackParameters` — 将 Parameters 中修改的值写回控件/VirtualStore
- 执行方式：`WorkFlowMethodUsage.CallBridgedFunctionAsync`
- 初始化：自动调用 `WorkFlowMethodUsage.EnsureInitialized()`
- 函数类型：工作流函数 + 通用函数（自动桥接）

#### 关键区别

| 维度 | ParameterExecutionWorkflow | WorkflowMethodExecutionWorkflow |
|------|---------------------------|--------------------------------|
| 参数传递 | `string?[]` 数组 | `WorkflowContext.Parameters` (Dictionary) |
| 参数传出 | 无（参数数组只读） | ✅ `WriteBackParameters` |
| 执行方式 | `IFunctionExecutor.ExecuteAsync` | `WorkFlowMethodUsage.CallBridgedFunctionAsync` |
| 初始化 | 手动注入 FunctionExecutor | 自动调用 `EnsureInitialized()` |
| 函数类型 | 仅通用函数 | 工作流函数 + 通用函数（自动桥接） |
| 扩展性 | 固定三步流程 | 可升级为多步骤管线 |
| 依赖 | `Modules.Registry.FunctionRegistry` | `WorkFlowMethod.Usage.WorkFlowMethodUsage` |

---

## 4. 框架反转设计

### 设计意图

Share 层定义接口，BackEndDriven 层提供实现，通过 DI 注入解耦。

```
Share/CommandFramework/Abstractions/
    └── IParameterExecutionWorkflow.cs    ← 接口定义
                    ↑
                    │ 实现
                    │
BackEndDriven/Invoke/
    ├── ~~ParameterExecutionWorkflow.cs~~     ← ⚠ 已废弃，文件已删除
    └── WorkflowMethodExecutionWorkflow.cs ← 唯一实现（基于 WorkFlowMethodUsage）
```

### 反转点

1. **ExecuteCommandStage** 不再直接依赖 `IFunctionExecutor`，而是委托给 `IParameterExecutionWorkflow`
2. **BackEndDrivenBuilder** 通过 `WithWorkflowMethodExecution()` 一行切换实现
3. **BuildWorkflow()** 返回 `IParameterExecutionWorkflow?`，由调用方决定是否注入自定义工作流

### 切换方式

```csharp
// 推荐方式（基于 WorkFlowMethodUsage，唯一实现）
builder.WithWorkflowMethodExecution();

// 完全自定义
builder.WithParameterExecutionWorkflow(new MyCustomWorkflow());

// ⚠ ParameterExecutionWorkflow 已废弃，旧方式 builder.WithWorkflowFunctionExecutor(executor) 不再有效
```

---

## 5. 管线架构

### 简化历程

```
旧架构（5 阶段）:
ParseCommand → CommandRouter → FindBinding → ExtractValues → ExecuteFunction → WriteResult → ResultCollect

新架构（3 阶段）:
ParseCommand → ExecuteCommand → ResultCollect
```

### 当前管线阶段

| 阶段 | 类 | 职责 |
|------|-----|------|
| 1. ParseCommand | `ParseCommandStage` | 解析命令文本为结构化命令 |
| 2. ExecuteCommand | `ExecuteCommandStage` | 统一处理 set/click/非事件命令 |
| 3. ResultCollect | `ResultCollectStage` | 收集执行结果 |

### ExecuteCommandStage 内部路由

```
ExecuteCommandStage.ExecuteCoreAsync
    │
    ├─ action == "set"    → HandleSet (设置控件值)
    ├─ action == "click"  → HandleClick (查找绑定 → 执行工作流)
    └─ 其他               → HandleNonEvent (委托给 INonEventCommandLookup)
```

---

## 6. 执行流程

### CLI 模式完整流程

```
命令文件 (commands.txt)
    │
    ▼
ExecutionEngine.ExecuteFile/ExecuteBatch
    │
    ▼
Pipeline.ExecuteAsync(command)
    │
    ├─ ParseCommandStage: 解析命令
    │
    ├─ ExecuteCommandStage:
    │   ├─ set  → VirtualStore[target] = value
    │   ├─ click → FindBinding → IParameterExecutionWorkflow
    │   │           ├─ ReadControlValues (从 VirtualStore)
    │   │           ├─ ExecuteFunction / CallBridgedFunctionAsync
    │   │           └─ WriteResult (写回 VirtualStore)
    │   └─ 非事件 → INonEventCommandLookup
    │
    └─ ResultCollectStage: 收集结果
    │
    ▼
ExecutionResult
```

### GUI 模式完整流程

```
用户操作控件
    │
    ▼
事件触发 → 绑定查找 → ExecuteCommandStage
    │
    ├─ set  → IControlFinder + IControlValueWriter
    ├─ click → FindBinding → IParameterExecutionWorkflow
    │           ├─ ReadControlValues (从真实控件)
    │           ├─ ExecuteFunction / CallBridgedFunctionAsync
    │           └─ WriteResult (写回真实控件)
    └─ 非事件 → INonEventCommandLookup
```

---

## 7. 使用方式

### 快速开始（CLI 模式）

```csharp
// 1. 创建管线
var pipeline = PipelineEntry.CreateDefaultPipeline(
    parser: new CommandParser(),
    nonEventCommandLookup: lookup);

// 2. 构建引擎
var engine = new BackEndDrivenBuilder()
    .WithPipeline(pipeline)
    .WithCommandRegistry(registry)
    .WithCommandParser(parser)
    .WithWorkflowMethodExecution()  // 使用 WorkFlowMethodUsage
    .BuildEngine();

// 3. 执行命令文件
var result = await engine.ExecuteFileAsync("commands.txt");
```

### 快速开始（GUI 模式）

```csharp
var engine = new BackEndDrivenBuilder()
    .WithPipeline(pipeline)
    .WithCommandRegistry(registry)
    .WithCommandParser(parser)
    .WithBindingFinderStrategy(strategy)
    .WithWorkflowControlFinder(finder)
    .WithWorkflowControlValueWriter(writer)
    .WithWorkflowMethodExecution()
    .BuildEngine();
```

### 使用 PipelineEntryInstance

```csharp
var instance = new PipelineEntryInstance(
    parser: new CommandParser(),
    nonEventLookup: lookup,
    controlFinder: finder,
    controlValueWriter: writer);

// 注册通用服务
instance.RegisterServices(
    configLoader: loader,
    bindingResolver: resolver,
    uiController: controller);

// 执行命令
var context = await instance.RunCommandAsync("click Button1");

// 批量执行
var results = await instance.RunCommandsAsync(new[] {
    "set TextBox1|Hello",
    "click Button1"
});
```

---

## 8. 扩展指南

### 自定义参数执行工作流

实现 `IParameterExecutionWorkflow` 接口：

```csharp
public class MyCustomWorkflow : IParameterExecutionWorkflow
{
    public async Task<ParameterExecutionResult> ExecuteAsync(
        ParameterExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        // 自定义执行逻辑
    }
}

// 注入
builder.WithParameterExecutionWorkflow(new MyCustomWorkflow());
```

### 添加自定义管线阶段

```csharp
public class MyStage : CommandStageBase
{
    public MyStage() : base("MyStage", "Custom stage") { }

    protected override Task ExecuteCoreAsync(
        CommandContext context, CancellationToken cancellationToken)
    {
        // 自定义处理逻辑
        context.SetStageResult(Name, true);
        return Task.CompletedTask;
    }
}

// 注册
var pipeline = PipelineEntry.CreateDefaultPipeline(
    parser: parser,
    additionalStages: new MyStage());
```

### 切换工作流实现

```csharp
// 方式一：使用 WorkflowMethodExecutionWorkflow（推荐，唯一实现）
builder.WithWorkflowMethodExecution();

// 方式二：完全自定义
builder.WithParameterExecutionWorkflow(new MyCustomWorkflow());

// ⚠ ParameterExecutionWorkflow 已废弃，请勿使用
// 旧方式 builder.WithWorkflowFunctionExecutor(executor) 不再有效
```

---

## 相关文档

- [Invoke 子模块文档](Invoke/README.md) — 参数执行工作流详细说明
- [WorkFlowMethodUseage.cs](../WorkFlowMethod/WorkflowMethodUseage.cs) — WorkFlowMethod 统一调用入口
- [IParameterExecutionWorkflow](../../Share/CommandFramework/Abstractions/IParameterExecutionWorkflow.cs) — 接口定义
