# FileDrivenFramework 文档

> **定位**：文件驱动的 UI 自动化执行框架
>
> **相关文档**：[统一函数体系架构设计.md](./统一函数体系架构设计.md) | [workflow_pipeline_migration.md](./workflow_pipeline_migration.md) | [调用链综合分析报告.md](./FileDriven.Execution/调用链综合分析报告.md)

---

## 目录

- [1. 项目概述](#1-项目概述)
- [2. 快速开始](#2-快速开始)
- [3. 架构全景](#3-架构全景)
- [4. 项目结构](#4-项目结构)
- [5. 执行流程](#5-执行流程)
- [6. 设计决策](#6-设计决策)
- [7. 迁移状态](#7-迁移状态)

---

## 1. 项目概述

**FileDrivenFramework（FDF）** 核心能力：

1. **JSON 配置驱动 UI** — 动态生成 WinForms 窗体，无需编码
2. **命令文件驱动自动化** — CLI 模式批量执行命令序列
3. **AI 模式集成** — 支持 AI 智能体通过标准化命令接口操作 GUI

### 设计哲学

```
配置文件 (JSON) ──▶  WdUI ──▶  WinForms 窗体
命令文件 (.txt) ──▶  FDF CLI ──▶ UI 自动化操作
AI 智能体 ────────▶  FDF 命令框架 ──▶ 环境感知与操作
                              │
                              ▼
                    FunctionExecution 引擎
```

### 依赖的外部框架

| 框架 | 用途 | 位置 |
|------|------|------|
| **WdUI** | UI 编排引擎 | `BasicMethod/` |
| **FunctionExecution** | 函数注册与执行引擎 | `BasicMethod/FunctionExecution/` |
| **BackEndDriven** | 通用后端驱动框架（`FrameworkExecutor` 基类、`IBackEndPipelineContext` 接口、`PipelineStageBase` 基类） | `Operation/BackEndDriven/` |

---

## 2. 快速开始

### 构建

```bash
dotnet build WorkFlowPrj.sln
```

### 运行模式

| 模式 | 触发条件 | 示例 |
|------|---------|------|
| **配置选择器** | 无参数 | `dotnet run --project FileDriven/FileDriven.Launcher` |
| **直接加载** | `--config <file.json> [--show]` | `--config MathFunctions.json --show` |
| **命令文件** | `--file <commands.txt>` | `--file test_commands.txt` |
| **单命令** | `--exec "<cmd>"` | `--exec "load MathFunctions.json show"` |

### CLI 命令一览

| 命令 | 功能 |
|------|------|
| `load <path> [name] [show]` | 加载 JSON 配置并生成窗体 |
| `show [formName]` | 显示已注册的窗体 |
| `close <formName>` | 关闭指定窗体 |
| `list` | 列出所有已注册的窗体 |
| `set <control> <value>` | 设置控件值 |
| `click <controlName>` | 模拟点击控件 |
| `get <control>` | 获取控件的值 |
| `sleep <ms>` | 等待指定毫秒数 |
| `echo <text>` | 回显文本 |
| `wait <condition>` | 等待指定条件满足 |
| `assert <condition>` | 断言验证条件 |
| `discover` | 扫描发现配置文件 |
| `look` | 查看已加载配置 |
| `log <subcommand>` | 日志查询/管理 |
| `help` | 显示帮助信息 |

---

## 3. 架构全景

### 3.1 六层架构

```
┌─────────────────────────────────────────────────────────────────────┐
│  第一层：JSON 配置层（FileDriven/DrivenConfig/JsonConfig/）           │
│  MathFunctions.json — 定义 UI 布局、控件、事件绑定、函数映射           │
├─────────────────────────────────────────────────────────────────────┤
│  第二层：管线编排层（FileDriven.Pipeline）                             │
│  ScriptLoadSkeleton → ForEachPipeline(ParseCommand → FindBinding →  │
│    ExtractValues → ExecuteFunction → WriteResult)                   │
│  上下文：FileDrivenPipelineContext                                   │
├─────────────────────────────────────────────────────────────────────┤
│  第三层：通用管线框架（Share/CentralPipeline）                         │
│  CentralPipeline<TContext> / PipelineBuilder<TContext>              │
│  ForEachPipeline / PipelineBranch / PipelineTryCatch /              │
│  ParallelPipeline / ConditionalSkeleton / PipelineStage             │
├─────────────────────────────────────────────────────────────────────┤
│  第四层：SkeletonFlow 框架（BasicMethod/FrameMethod）                 │
│  SkeletonBase<TIn, TOut> / SyncSkeletonBase<TIn, TOut>             │
│  Flow<TIn, TOut> / ISkeleton<TIn, TOut>                            │
├─────────────────────────────────────────────────────────────────────┤
│  第五层：函数注册/执行层（Share/FunctionStorage + EventTriggerFramework）│
│  FunctionStorageRegistry → FunctionRegistry + FunctionExecutor      │
│  Library: MathLibrary / UIToolbox / WorkflowLibrary / CodePathLibrary│
├─────────────────────────────────────────────────────────────────────┤
│  第六层：BackEndDriven 通用框架（Operation/BackEndDriven）            │
│  FrameworkExecutor<TContext> — 9 步执行骨架基类                      │
│  IBackEndPipelineContext — 通用管线上下文接口                        │
│  PipelineStageBase<TContext> — 管线阶段基类                         │
│  ExecutionResult<T> — 统一结果模型                                  │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 通用管线框架核心组件

| 组件 | 说明 |
|------|------|
| `CentralPipeline<TContext>` | 通用管线执行引擎，按阶段组合 Skeleton |
| `PipelineBuilder<TContext>` | Fluent API 构建器（线程安全） |
| `PipelineStageRegistry<TContext>` | 按阶段管理 Skeleton 的注册表 |
| `ForEachPipeline<TContext, TItem>` | 集合遍历管线（单元素异常不中断整体遍历） |
| `PipelineBranch<TContext>` | 双路条件分支管线 |
| `PipelineSwitch<TContext, TKey>` | 多路条件分支管线 |
| `PipelineTryCatch<TContext>` | 异常处理/降级管线 |
| `PipelineRetry<TContext>` | 重试管线 |
| `ParallelPipeline<TContext>` | 并行执行管线 |
| `ConditionalSkeleton<TContext>` | 条件跳过包装器 |

### 3.3 业务 Skeleton

| Skeleton | 阶段 | 职责 |
|----------|------|------|
| `ScriptLoadSkeleton` | PreProcessing | 加载脚本文件 |
| `NonEventCommandHandler` | Execution | 处理非事件命令 |
| `ControlTreeWalker` | 工具类 | 遍历控件树 |
| `FindBindingStage` | Execution | 查找事件绑定 |
| `ExtractValuesStage` | Execution | 提取输入值 |
| `ExecuteFunctionStage` | Execution | 执行函数 |
| `WriteResultStage` | Execution | 写入结果 |

### 3.4 FileDrivenPipelineContext 统一上下文

继承自 `CommandContext`（`EventTriggerFramework.Pipeline.CommandContext`），通过 `BatchContextData` 组合模式共享日志字段。

**字段分组**：
- **日志相关**：`BatchId` / `BatchName` / `Source` / `Description` / `WorkflowId` / `ProjectRoot`
- **脚本加载**：`ScriptPath` / `CommandFilePath` / `CommandLines` / `ScriptLoadSuccess`
- **命令执行**：`CurrentCommand` / `ParsedCommand` / `ParsedVerb` / `ParsedAction` / `ParsedTarget` / `ParsedParameters`
- **绑定查找**：`BoundFunctionName` / `BoundInputs` / `BoundOutputTarget` / `BindingFound`
- **值提取**：`ExtractedValues` / `ExtractSuccess`
- **函数执行**：`FunctionResultValue` / `FunctionExecutionTime` / `FunctionExecuteSuccess`
- **结果写入**：`WriteResultSuccess`
- **执行统计**：`TotalCommands` / `SuccessCount` / `FailCount` / `CommandResults`
- **运行时依赖**：`Options` / `BindingResolver` / `FunctionExecutor` / `TargetForm` / `VirtualStore` / `LoadedConfigs`

**便捷属性**：
- `IsGuiMode` → `TargetForm != null`
- `IsCliMode` → `VirtualStore != null && TargetForm == null`

### 3.5 函数体系

| 体系 | 位置 | 状态 |
|------|------|------|
| **旧** `FunctionExecutionFramework` | `BasicMethod/FunctionExecution/` | ✅ 已整合 |
| **新** `EventTriggerFramework` | `Share/EventTriggerFramework/` | ✅ 统一底座 |

**统一入口**：`FunctionStorageRegistry`（内部委托给 `FunctionRegistry` + `FunctionExecutor`）

**函数库**：

| Library | 函数 | 用途 |
|---------|------|------|
| `MathLibrary` | Add/Plus, Subtract/Minus, Multiply, Divide | 数学运算 |
| `UIToolbox` | 窗口操作、控件操作等 | UI 自动化 |
| `WorkflowLibrary` | 工作流相关函数 | 工作流引擎 |
| `CodePathLibrary` | 代码路径分析函数 | 代码分析 |

### 3.6 日志框架

**架构链路**：
```
LogEntryBuilder.Info/Warn/Error()
  → PlaceholderLogHelper.Log()
    → LogRepository（SQLite + Console 双通道）
      → LoggingSkeletonWrapper（自动包装 Skeleton）
```

**日志分类**：`Lifecycle` / `Config` / `Command` / `Batch_Routing` / `FileDriven_General`

**日志级别**：`INFO` / `WARN` / `ERROR` / `DEBUG`

**输出格式**：`[级别] [分类] [模块] 消息内容`

---

## 4. 项目结构

```
FileDriven/
├── FileDriven.Launcher/                       # 启动器（主入口）
│   ├── Program.cs                             # 程序集入口
│   ├── FileDrivenBootstrapper.cs              # ★ 唯一启动入口（全生命周期管理）
│   └── Usage.cs
│
├── FileDriven.Utilities/                      # 工具与 CLI
│   ├── CLI/Services/                          # 命令执行引擎、事件绑定解析器
│   ├── CLI/Adapters/                          # 后端命令适配器
│   ├── CLI/CliModeRouter.cs                  # CLI 模式路由
│   ├── CLI/Models/                            # AI 模式命令框架
│   └── Helpers/ControlHelper.cs
│
├── FileDriven.UI/                             # UI 项目
│   └── Core/
│       ├── MainForm.cs                        # 控制台主窗体
│       ├── ConfigSelectorForm.cs              # 配置选择器窗体
│       └── MessageLoopManager.cs              # 消息循环管理器
│
├── FileDriven.Pipeline/                       # 事件管线（纯管线逻辑）
│   ├── PipelineEntry.cs                       # ★ 管线入口 + IEventPipelineExecutor
│   ├── PipelineFactory.cs                     # ★ 管线工厂
│   ├── FileDrivenPipelineContext.cs           # 统一上下文
│   ├── EventCore.cs                           # 事件核心层
│   ├── ControlHelper.cs                       # 控件辅助方法
│   ├── NonEventCommands.cs                    # 非事件命令定义
│   └── Skeletons/
│       ├── ScriptLoadSkeleton.cs              # 脚本加载骨架
│       ├── NonEventCommandHandler.cs          # 非事件命令处理器
│       └── ControlTreeWalker.cs               # 控件树遍历器
│
├── FileDriven.Execution/                      # ★ 执行引擎（基于 BackEndDriven）
│   ├── FileDrivenExecutor.cs                  # 继承 FrameworkExecutor
│   ├── FileDrivenExecutionResult.cs           # 统一执行结果模型
│   ├── Command/
│   │   ├── ExecutionHelper.cs                 # 共享辅助方法
│   │   ├── FileDrivenCommandRegistrar.cs      # 非事件命令注册
│   │   ├── FileDrivenComponentRegistrar.cs    # 组件注册
│   │   ├── FileDrivenPipelineContext.cs       # 实现 IBackEndPipelineContext
│   │   ├── ControlValueWriter.cs              # 控件值写入
│   │   ├── WinFormsControlHelper.cs           # WinForms 辅助方法
│   │   ├── FileDrivenTemplateEngineInitializer.cs # 模板引擎初始化
│   │   ├── FileDrivenGuiInitializer.cs        # GUI 模式初始化
│   │   └── Stages/
│   │       └── ExecuteCommandStage.cs         # 继承 PipelineStageBase
│   └── 调用链综合分析报告.md
│
├── FileDriven.Logging/                        # 日志框架
│   └── Wrappers/
│       ├── LogEntryBuilder.cs
│       └── LoggingSkeletonWrapper.cs
│
├── FileDriven.JsonRefer/                      # JSON 引用处理
│   └── JsonProcessing/JsonTemplateProcessor.cs
│
├── DrivenConfig/                              # JSON 配置文件
│   ├── JsonConfig/MathFunctions.json
│   ├── Scripts/
│   ├── TempletConfig/
│   └── WorkFlow/
│
├── filedriven_log_messages.json               # 日志消息模板
├── framework.config.json                      # 框架配置
└── test_commands.txt                          # 测试命令文件
```

---

## 5. 执行流程

### 5.1 初始化流程

```
FileDrivenBootstrapper.Run(args)
├── [0] SetupGlobalExceptionHandling()
├── [1] ParseArguments(args) → CliOptions
├── [2] NamedPipeClient.Connect()（可选）
├── [3] SetupLogging(options)
├── [4] SetWorkingDirectory()
├── [5] InitializeFramework(cliMode)
│   ├── AutoInitializeLogging()
│   ├── JsonConverterRegistrar.RegisterAll()
│   ├── FileDrivenWdUIBridge.Initialize()（GUI 模式）
│   ├── ProjectPathManager.FindProjectRoot()
│   ├── LoadPathAliasesFromEnvironment()
│   ├── MathLibrary.RegisterAll() / UIToolbox.RegisterAll()
│   ├── CommandManager = new UICommandManager()
│   ├── RegisterComponents(cliMode)
│   ├── InitializeTemplateEngine()
│   ├── InitializeBatchLogging()
│   ├── PerformComponentValidation()
│   └── InjectEventBinderToRenderPipeline()（GUI 模式）
├── [6] 模式分发（GUI → StartGuiMode / CLI → StartCliMode）
├── [7] ShutdownFramework(cliMode)
├── [8] ShutdownLogging()
└── [9] NamedPipeClient.Dispose()
```

### 5.2 执行路径

**GUI 模式 + CLI click**（通过 WinForms 模拟点击）：
```
用户输入 "click addButton"
  → ScriptLoadSkeleton → ForEachPipeline 遍历命令
    → ParseCommandSkeleton → HandleClickViaWinForms()
      → Button.Click 事件触发
        → DefaultEventBindingService → WinFormsEventBinder
          → IEventPipelineExecutor.Execute()
            → PipelineEntry.Execute() → BuildCommandPipeline()
              → FindBindingStage → ExtractValuesStage → ExecuteFunctionStage → WriteResultStage
```

**GUI 模式 + 手动点击**：
```
用户点击 addButton
  → DefaultEventBindingService → WinFormsEventBinder
    → IEventPipelineExecutor.Execute()
      → PipelineEntry.Execute() → BuildCommandPipeline()
        → FindBindingStage → ExtractValuesStage → ExecuteFunctionStage → WriteResultStage
```

**纯 CLI 模式**（无 GUI）：
```
用户输入 "click addButton"
  → ScriptLoadSkeleton → ForEachPipeline 遍历命令
    → ParseCommandSkeleton → FindBindingStage（从 dataBindings 查找）
      → ExtractValuesStage（从 VirtualStore 读取）
        → ExecuteFunctionStage → WriteResultStage
```

> **当前状态**：GUI 模式下 CLI click 和手动点击已完全统一，区别仅在于触发方式。

---

## 6. 设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| **泛型 vs 非泛型** | 泛型 `CentralPipeline<TContext>` | 不同场景上下文类型不同，泛型提供最大灵活性 |
| **适配器 vs 直接实现** | Skeleton 直接实现 `ISkeleton<TContext, TContext>` | 消除适配器层维护成本 |
| **动态注册 vs 静态编排** | 两者互补 | `PipelineBuilder` 支持静态编排，`PipelineStageRegistry` 支持动态注册 |
| **上下文设计** | 统一 `FileDrivenPipelineContext` | 避免多模型间数据拷贝和转换 |
| **函数执行入口** | 统一 `FunctionStorageRegistry` | 便于审计、监控、扩展 |
| **日志管线** | 从事件管线分离，独立生命周期管理 | 避免日志刷入双重执行 |
| **条件跳过** | `ConditionalSkeleton<TContext>` 包装器 | 消除业务 Skeleton 中重复的条件检查 |

---

## 7. 迁移状态

### 7.1 已完成 ✅

| 类别 | 任务 |
|------|------|
| **管线框架** | CentralPipeline 全部组件、BasicMethod.Pipeline 基础类型、PipelineEntry 便捷入口、ForEachPipeline 异常保护、PipelineBuilder 线程安全、PipelineTryCatch 异常过滤、PipelineStage 枚举统一 |
| **业务 Skeleton** | ScriptLoadSkeleton、ParseCommandSkeleton 扩展（非事件命令处理）、ExtractValuesStage + WriteResultStage 扩展（CLI 模式 VirtualStore） |
| **函数体系** | FunctionStorageRegistry 统一入口、整理 EventTriggerService |
| **运行模式** | FileDrivenBootstrapper 统一启动入口、消除 CliModeRouter/GuiCommandAdapter/BackendCommandAdapter 中间层 |
| **命令增强** | 事件触发逻辑迁移、`load`/`close`/`set`/`show`/`sleep` 命令增强、GUI 模式特有命令（list/validate/exit） |
| **上下文** | FileDrivenPipelineContext 继承 CommandContext + BatchContextData 组合模式 |
| **日志** | 日志管线独立、标记 `[Obsolete]`、Launcher 瘦身、BindingResolver 接口化 |
| **手动点击复用管线** | IEventPipelineExecutor 接口 + PipelineEntry 实现、WinFormsEventBinder 废弃旧路径 |
| **CLI click 统一** | GUI 模式下通过 WinForms 模拟点击触发 |
| **EventCore 迁移** | 从 `FileDriven.UI.Event` 迁移到 `FileDriven.Pipeline.EventCore` |
| **PipelineFactory 分离** | 静态工厂方法从 `PipelineEntry` 分离 |
| **BackEndDriven 优化重构** | 基于 `Operation/BackEndDriven` 对 `FileDriven.Execution` 全面优化，消除 10 处重复代码 |

### 7.2 待完成 ❌

| 优先级 | 任务 | 说明 |
|--------|------|------|
| P6 | 统一日志 | 所有 Skeleton 通过 `LoggingSkeletonWrapper` 自动产生日志 |
| P7 | 编写集成测试 | 新管线的端到端测试 |
| P11 | 使用 `PipelineStage.PostProcessing` 阶段 | 将执行统计迁移到独立 Skeleton |
| F2 | 消除 SkeletonToPipelineAdapter | 让 ISkeleton 直接实现 IPipeline |

### 7.3 绕开 FunctionStorage 的代码

| # | 位置 | 绕开内容 | 优先级 |
|---|------|---------|--------|
| 1 | `ParseCommandSkeleton.HandleSetValue()` | `set` 命令值设置 | 🔴 P1 |
| 2 | `NonEventCommandHandler` | 12 个非事件命令 | 🟡 P2 |
| 3 | `BackendCommandAdapter.DoSetAsync()` | `set` 命令值设置（已废弃） | 🟢 P3 |

---

## 附录：配置文件模板

```json
{
  "form": {
    "title": "示例表单",
    "width": 600, "height": 400,
    "maximizeBox": true, "minimizeBox": true,
    "windowState": "Normal", "startPosition": "CenterScreen"
  },
  "views": [{
    "name": "mainView",
    "controls": [
      { "type": "label", "name": "inputLabel", "text": "请输入内容：",
        "layout": { "left": 0.05, "top": 0.1, "width": 0.2, "height": 0.08 } },
      { "type": "textbox", "name": "inputText", "placeholder": "请输入内容",
        "layout": { "left": 0.3, "top": 0.1, "width": 0.6, "height": 0.08 } },
      { "type": "button", "name": "submitButton", "text": "提交",
        "events": { "click": "loadgraphdata" },
        "layout": { "left": 0.5, "top": 0.3, "width": 0.2, "height": 0.1 } }
    ]
  }],
  "dataBindings": [],
  "styles": { "fontFamily": "Microsoft YaHei", "fontSize": 9 }
}
```
