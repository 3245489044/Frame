# CommandFramework 与 WorkFlowMethod 对比分析

> **分析日期**：2026-07-03  
> **分析范围**：`Share/CommandFramework/` vs `Operation/WorkFlowMethod/`

---

## 一、定位概览

| 维度 | CommandFramework | WorkFlowMethod |
|------|-----------------|----------------|
| **所在目录** | `Share/CommandFramework/` | `Operation/WorkFlowMethod/` |
| **架构层级** | **Share 层**（共享基础设施） | **Operation 层**（操作/业务引擎） |
| **程序集名称** | `CommandFramework.dll` | `WorkFlowMethod.dll` |
| **命名空间** | `CommandFramework.*` | `WorkFlowMethod.*` / `WorkFlow.*` |
| **项目描述** | 通用命令执行框架 | 工作流编排与执行引擎 |
| **一句话定位** | 解析并执行单条命令的通用框架 | 编排并执行完整工作流的核心引擎 |

---

## 二、相同点

### 2.1 都提供"命令/函数"的注册与执行能力

**CommandFramework** 通过 `FunctionRegistry` + `FunctionExecutor` 注册和执行委托函数，支持参数自动匹配与类型转换：

```csharp
// CommandFramework 注册函数
registry.Register("add", (int a, int b) => a + b);

// 按名称执行
var result = await executor.ExecuteAsync("add", new Dictionary<string, object>
{
    ["a"] = 1,
    ["b"] = 2
});
```

**WorkFlowMethod** 通过 `IWorkflowRegistry` + `FunctionExecutor` 注册和执行工作流函数/通用委托，同样支持参数解析：

```csharp
// WorkFlowMethod 注册工作流函数
WorkflowUseage.RegisterFunction("Add", ctx => {
    var a = ctx.GetParameter<int>("0");
    var b = ctx.GetParameter<int>("1");
    return a + b;
});

// 注册通用委托
WorkflowUseage.RegisterDelegate("Multiply", new Func<int, int, int>((a, b) => a * b));
```

### 2.2 都采用管线化（Pipeline）执行模式

| 项目 | 管线实现 | 阶段/步骤 |
|------|---------|----------|
| **CommandFramework** | `StagePipeline<CommandContext>` 线性阶段管线 + 洋葱模型中间件 | 5 个默认阶段：Parse → FindBinding → ExtractValues → ExecuteFunction → WriteResult |
| **WorkFlowMethod** | `PipelineBuilder` / `ExecutionPipelineRunner` / `NodeProcessPipelineBuilder` | 支持图定义、节点链、提取、执行、循环、指标等多阶段 |

### 2.3 都支持文件驱动执行

- **CommandFramework**：`ExecuteFileAsync()` 从文本文件逐行执行命令
- **WorkFlowMethod**：`ExecuteFromFileAsync()` / `LoadPipelineFromFile()` 从 JSON 文件加载并执行工作流

### 2.4 都提供统一静态入口

- **CommandFramework**：`CommandFrameworkHost`（外观模式）+ `CommandFrameworkUsage`（全局单例）
- **WorkFlowMethod**：`WorkflowUseage` + `WorkFlowMethodUsage`（统一静态入口）

### 2.5 都依赖 Share 层的 Pipeline 基础模块

两者都引用了 `Modules/Pipeline/Pipeline.csproj` 作为管线基础类型。

### 2.6 都支持状态管理与执行追踪

- **CommandFramework**：内置 `UndoAsync()` / `RedoAsync()` 命令历史栈，`InMemoryStateStore` 状态存储
- **WorkFlowMethod**：通过 `StateManagement` 和 `ExecutionStateManager` 管理执行状态（Idle → Running → Paused/Completed/Failed/Canceled）

---

## 三、不同点

### 3.1 架构定位与抽象层次（最核心差异）

| 维度 | CommandFramework | WorkFlowMethod |
|------|-----------------|----------------|
| **定位** | **通用命令执行框架** — 解决"如何解析和执行一条命令" | **工作流编排引擎** — 解决"如何编排和执行一个完整工作流" |
| **抽象层次** | 底层基础设施（命令级） | 上层业务引擎（工作流级） |
| **输入粒度** | 单条命令字符串（`"do click addButton"`） | JSON 工作流定义（多节点、多步骤） |
| **关注点** | 命令解析 → 注册 → 执行 → 撤销/重做 | 工作流定义 → 函数注册 → 管线编排 → 运行时动态更新 |

### 3.2 功能复杂度与子模块规模

#### CommandFramework（简洁，~15 个核心文件）

```
Share/CommandFramework/
├── CommandFramework.csproj          # 项目文件
├── ICommand.cs                      # ★ 核心接口定义（18 个接口/抽象类型）
├── CommandFrameworkHost.cs          # 统一入口（Host 模式）
├── CommandFrameworkOptions.cs       # 配置选项模型
├── CommandFrameworkUsage.cs         # 全局单例操作入口
├── CommandParser.cs                 # 命令解析器
├── Registries.cs                    # 命令注册表（工厂模式）
│
├── Abstractions/                    # 抽象接口层
│   ├── CommandCriticality.cs
│   ├── ControlFinderOptions.cs
│   ├── EventBindingInfo.cs
│   ├── IConfigLoader.cs
│   ├── IControlFinder.cs
│   ├── IControlValueWriter.cs
│   ├── IEventBindingResolver.cs
│   ├── IEventTypeMapper.cs
│   ├── IExecutionContext.cs
│   ├── INonEventCommandLookup.cs
│   ├── INonEventCommandRegistry.cs
│   ├── IScriptConverter.cs
│   ├── IStateQuery.cs
│   ├── IUIController.cs
│   └── ListData.cs
│
├── Execution/                       # 执行引擎
│   ├── ArgumentBuilder.cs           # 参数构建器
│   ├── CommandEngine.cs             # 命令引擎核心
│   ├── CommandResults.cs            # 执行结果模型
│   ├── ControlValueAccessorBase.cs  # 控件值读写器基类
│   ├── EnvironmentAdapterBase.cs    # 环境适配器基类
│   ├── FunctionExecution.cs         # 函数执行体系
│   ├── InMemoryStateStore.cs        # 内存状态存储
│   ├── StateStoreExtensions.cs      # 状态存储扩展
│   └── StringHelper.cs              # 字符串工具
│
├── Libraries/
│   └── NonEventCommands.cs          # 非事件命令库
│
├── Models/                          # 数据模型
│   ├── CommandContext.cs            # 管线执行上下文
│   ├── CommandExecutionContext.cs   # 引擎执行上下文
│   ├── CommandExecutionReport.cs    # 执行报告
│   ├── JsonCommandResult.cs         # JSON 命令结果
│   └── LoadResult.cs                # 加载结果
│
├── Pipeline/                        # 管线系统
│   ├── PipelineEntry.cs             # 管线入口
│   ├── PipelineStageBase.cs         # 管线阶段基类
│   └── Stages/                      # 管线阶段实现
│       ├── ParseCommandStage.cs     # 命令解析阶段
│       ├── FindBindingStage.cs      # 事件绑定查找阶段
│       ├── ExtractValuesStage.cs    # 值提取阶段
│       ├── ExecuteFunctionStage.cs  # 函数执行阶段
│       ├── WriteResultStage.cs      # 结果写入阶段
│       ├── ValidationStage.cs       # 验证阶段
│       ├── RetryStage.cs            # 重试阶段
│       └── LoggingStage.cs          # 日志阶段
│
├── Validators/                      # 验证器
│   ├── CommandContextValidator.cs
│   └── ParsedCommandValidator.cs
│
└── doc/                             # 文档
    ├── CommandFramework_设计文档.md
    ├── CommandFramework_控件处理方案.md
    ├── CommandFramework_重构方案.md
    └── CommandFramework_补充方案.md
```

#### WorkFlowMethod（庞大，12 个子模块，数十个文件）

```
Operation/WorkFlowMethod/
├── WorkFlowMethod.csproj                    # 项目文件
├── WorkflowMethodUseage.cs                  # 统一调用入口（930 行）
├── WorkflowUseage_Architecture.md           # 架构文档
│
├── FunctionExecutionFramework/              # 函数执行框架
│   ├── BasicMethodAdapter.cs                # BasicMethod 双向同步适配器
│   ├── DefaultWorkflowRegistry.cs           # 默认工作流注册表
│   ├── FunctionExecutionFramework.cs        # 框架静态入口
│   ├── FunctionExecutionFrameworkExtensions.cs
│   ├── ExecutionPipeline/                   # 执行管线
│   ├── Models/                              # 模型
│   └── Registrars/                          # 注册器
│
├── Execution/                               # 执行引擎
│   ├── RefactoredExecution.cs               # 重构执行器
│   ├── WorkflowBuilder.cs                   # 工作流构建工具
│   ├── Adapters/                            # 适配器
│   ├── Builders/                            # 构建器
│   ├── Chain/                               # 链处理
│   ├── Data/                                # 数据处理
│   ├── Engine/                              # 引擎核心
│   ├── Serialization/                       # 序列化
│   └── Session/                             # 会话管理
│
├── WorkFlowPipeline/                        # 管线系统
│   ├── PipelineBuilder.cs                   # 管线构建器
│   ├── PipelineContextAccessor.cs           # 管线上下文访问器
│   └── PipelineStages.cs                    # 管线阶段定义
│
├── NodeProcess/                             # ★ 节点处理系统（核心）
│   ├── NodeChainSkeleton.cs                 # 节点链骨架
│   ├── NodeConfigFactory.cs                 # 节点配置工厂
│   ├── NodeProcessPipelineBuilder.cs        # 节点管线构建器
│   ├── Storage.cs                           # 工作流存储管理器
│   ├── Abstractions/                        # 抽象接口
│   ├── Adapters/                            # 适配器
│   ├── Execution/                           # 执行
│   ├── Graph/                               # 图处理
│   ├── KeyMapping/                          # 键位映射
│   ├── Lifecycle/                           # 生命周期
│   ├── Models/                              # 模型
│   ├── PlaceholderResolution/               # 占位符解析
│   ├── ReferenceRegistration/               # 引用注册
│   └── Runtime/                             # 运行时
│
├── Orchestration/                           # 编排层
│   ├── Validation.cs                        # 模块化验证器
│   ├── Content/                             # 内容管理
│   └── State/                               # 状态管理
│
├── CSG/                                     # ★ CSG 协议集成
│   ├── CsgIntegrationService.cs             # CSG 集成服务
│   └── Adapters/                            # 适配器
│
├── CallFrame/                               # ★ 调用帧框架
│   ├── Abstractions/
│   ├── Core/
│   ├── Extensions/
│   └── Models/
│
├── LoopFramework/                           # ★ LLM 驱动的循环工作流
│   ├── Abstractions/
│   ├── Core/
│   ├── Extensions/
│   ├── Observability/
│   └── StateMachine/
│
├── GraphUsing/                              # ★ 图使用服务
│   ├── Deduplication/                       # 去重处理器
│   ├── Directives/                          # 指令应用器
│   ├── Pipeline/                            # 管线
│   ├── Services/                            # 服务
│   └── SkeletonBridge/                      # 骨架桥接
│
├── Config/                                  # ★ 预定义工作流模板
│   └── WorkFlowsSet.cs                      # 工作流配置集
│
├── Infrastructure/                          # 基础设施
│   ├── DefaultBridgeContext.cs
│   ├── ExecutionContext.cs
│   ├── ExpandoObjectHelper.cs
│   ├── ServiceDefaults.cs
│   ├── ValidationResult.cs
│   ├── WorkflowConstants.cs
│   ├── WorkflowLogger.cs
│   ├── WorkflowLoggerAdapter.cs
│   └── Abstractions/
│
├── Context/                                 # 上下文管理
│   ├── Abstractions/
│   ├── Execution/
│   ├── Models/
│   └── Runtime/
│
├── Introduce/                               # 介绍文档
│
└── docs/                                    # 详细文档
    ├── 管线分析_综合现状文档.md
    ├── 管线执行体系与结果类型重复分析.md
    ├── 架构优化_当前状态与下一步计划.md
    ├── 架构优化_框架与执行状态分离方案.md
    ├── 未迁移逻辑的SkeletonFlow重构方案.md
    ├── analysis_report_comprehensive.md
    ├── analysis_report_detailed.md
    ├── analysis_report.md
    ├── ECS_Integration_Analysis.md
    ├── LogicComposition优化实施计划.md
    ├── Orchestration_代码现状分析与重构规划.md
    ├── Orchestration_综合重构方案.md
    ├── OrchestrationConnect_SkeletonFlow重构方案.md
    ├── OrchGraph_拆分重构_下一步执行计划.md
    └── task_progress_code_cleanup.md
```

### 3.3 核心概念与抽象模型对比

| 概念 | CommandFramework | WorkFlowMethod |
|------|-----------------|----------------|
| **执行单元** | `ICommand` / `Delegate`（命令/委托） | `IFunctionUnit` / `WorkflowContext`（函数单元/工作流上下文） |
| **上下文** | `CommandContext` / `CommandExecutionContext` | `WorkflowContext` / `PipelineContext` / `ExecutionContext` |
| **注册体系** | 单一 `FunctionRegistry` | **双注册表桥接**：`IWorkflowRegistry` + `IFunctionRegistry` |
| **执行路径** | Engine 路径（轻量）+ Pipeline 路径（阶段化） | Pipeline 路径（管线化）+ NodeProcess 路径（节点化） |
| **配置驱动** | 文本命令文件（`.txt`） | **JSON 配置文件**（声明式工作流定义） |
| **参数匹配** | 4 种策略链（精确/位置/单值/默认值） | 委托给 BasicMethod 的 ParameterResolver |
| **类型转换** | 内置 15 种类型 TryParse 转换 | 委托给 BasicMethod 的类型系统 |

### 3.4 WorkFlowMethod 独有的高级能力

CommandFramework **没有**以下能力：

| 能力 | 说明 | 所在子模块 |
|------|------|-----------|
| **CSG 三投影一致性** | 配置/状态机/图结构三视图交叉验证 | `CSG/` |
| **运行时动态更新** | 工作流结构可在运行中动态调整（Symbol 定位插入、节点删除与重连） | `NodeProcess/`、`GraphUsing/` |
| **LLM 驱动的循环工作流** | RuleBased / LLMBased / Hybrid 三种决策模式 | `LoopFramework/` |
| **键位映射（KeyMapping）** | 异构工作流之间的数据对接（Direct/Regex/MultiLevel/Prefix 映射） | `NodeProcess/KeyMapping/` |
| **去重逻辑（Deduplication）** | 防止工作流重复注册，支持增量更新 | `GraphUsing/Deduplication/` |
| **数据流规则（Dataflows）** | 声明式定义数据在节点间的流转 | `NodeProcess/` |
| **跨链连接（ConnectChainInfoConfig）** | 运行时动态拼接多个工作流 | `NodeProcess/` |
| **调用帧追踪（CallFrame）** | 每个节点的输入输出快照，完整的执行轨迹记录 | `CallFrame/` |
| **预定义工作流模板** | AI 图像生成、代码生成、数据分析等开箱即用模板 | `Config/WorkFlowsSet.cs` |
| **暂停/恢复** | 真正的断点续传（ManualResetEventSlim） | `Execution/` |
| **双注册表桥接** | 工作流函数注册表与通用委托注册表的统一 | `FunctionExecutionFramework/` |
| **BasicMethod 适配器** | 与 BasicMethod 基础框架的双向同步 | `FunctionExecutionFramework/BasicMethodAdapter.cs` |

### 3.5 依赖关系差异

#### CommandFramework 依赖（轻量，5 个 ProjectReference）

```
Share/CommandFramework.csproj
├── Share/ParameterResolver/ParameterResolver.csproj      # 参数解析
├── Modules/Pipeline/Pipeline.csproj                      # 管线基础类型
├── Modules/Registry/Registry.csproj                      # 注册表
├── Share/FrameworkAbstractions/FrameworkAbstractions.csproj  # 框架抽象
└── Modules/Parsing/Parsing.csproj                        # 解析模块
```

NuGet 依赖：
- `Microsoft.Extensions.Logging.Abstractions` — 统一日志接口
- `Polly` — 重试标准化
- `FluentValidation` — 声明式验证
- `Microsoft.Extensions.DependencyInjection.Abstractions` — DI 抽象

#### WorkFlowMethod 依赖（重量，11 个 ProjectReference）

```
Operation/WorkFlowMethod.csproj
├── BasicMethod/BasicMethod.csproj                        # 基础方法库
├── Modules/GraphProcessing/GraphProcessing.csproj        # 图处理
├── Modules/LogicComposition/LogicComposition.csproj      # 逻辑组合
├── Share/GraphFramework/GraphFramework.csproj            # 图框架
├── Share/TreeTraceFramework/TreeTraceFramework.csproj    # 树追踪框架
├── Modules/Pipeline/Pipeline.csproj                      # 管线框架
├── Share/UniversalWorkflowFramework/UniversalWorkflowFramework.csproj  # 通用工作流框架
├── Share/StateManagement/StateManagement.csproj          # 状态管理
├── Share/WorkflowExecution/WorkflowExecution.csproj      # 统一工作流执行框架
├── Share/UniversalMiddleware/UniversalMiddleware.csproj  # 通用中间件
└── Protocol/CSG/CSG.Unified/CSG.Unified.csproj           # CSG 协议
```

NuGet 依赖：
- `Microsoft.Extensions.Caching.Memory` — 内存缓存

### 3.6 设计哲学差异

| 维度 | CommandFramework | WorkFlowMethod |
|------|-----------------|----------------|
| **设计模式** | 命令模式、策略模式、模板方法、工厂模式、外观模式 | 管线-过滤器、桥接模式、适配器模式、状态机模式 |
| **扩展方式** | 自定义管线阶段、自定义环境适配器、自定义控件读写器 | 自定义函数注册、自定义节点处理、自定义编排逻辑 |
| **AI 集成** | ❌ 无 | ✅ 可选能力（LLM 函数节点、Hybrid 决策） |
| **企业级特性** | 基础（Undo/Redo、状态存储） | 高级（暂停/恢复、审计追踪、三投影验证） |
| **配置方式** | 代码内嵌 + 文本命令文件 | JSON 配置文件驱动（声明式） |
| **运行时灵活性** | 低（命令注册后固定） | 高（运行时动态更新工作流结构） |

### 3.7 接口抽象层次对比

#### CommandFramework 接口体系（18 个接口/抽象类型）

```
ICommand.cs 定义的接口：
├── ICommandEngine              — 命令引擎契约（注册/执行/撤销/重做）
├── ICommand                    — 单个命令契约
├── ICommandExecutionResult     — 执行结果契约
├── ICommandBatchResult         — 批量结果契约
├── ICommandParser              — 命令解析器契约
├── ICommandPipeline<TContext>  — 管线契约
├── IPipelineContext            — 管线上下文契约
├── IPipelineStage<TContext>    — 管线阶段契约
├── IControlValueAccessor       — 控件值读写器契约
├── IEnvironmentAdapter         — 环境适配器契约
├── IFunctionRegistry           — 函数注册表契约（已迁移至 Registry）
├── IFunctionExecutor           — 函数执行器契约（已迁移至 Registry）
├── INonEventCommandHandler     — 非事件命令处理器契约
├── IStateStore                 — 状态存储契约
├── IValueExtractor             — 值提取器契约（已移除）
├── IConfigLoader               — 配置加载器
├── IControlFinder              — 控件查找器
├── IControlValueWriter         — 控件值写入器
├── IEventBindingResolver       — 事件绑定解析器
├── IUIController               — UI 控制器
└── IStateQuery                 — 状态查询器
```

#### WorkFlowMethod 接口体系（更丰富，跨多个子模块）

```
核心接口：
├── IWorkflowRegistry           — 工作流函数注册表（继承 IFunctionRegistry）
├── IFunctionRegistry           — 通用委托注册表（来自 BasicMethod）
├── IFunctionExecutor           — 函数执行器
├── IFunctionUnit               — 函数单元接口
├── IPipelineBuilder            — 管线构建器
├── IPipelineExecutor           — 管线执行器
├── IWorkflowMediator           — 工作流中介者
├── IModuleCreator              — 模块创建器
├── IModuleCreationManager      — 模块创建管理器
├── IWorkflowLogger             — 工作流日志记录器
├── IWorkflowRegistryAdapter    — 注册表适配器
│
NodeProcess 接口：
├── INodeProcessor              — 节点处理器
├── INodeConfigFactory          — 节点配置工厂
├── INodeChainSkeleton          — 节点链骨架
├── IStorageManager             — 存储管理器
├── IKeyMapper                  — 键位映射器
├── IDeduplicationProcessor     — 去重处理器
│
CSG 接口：
├── ICsgIntegrationService      — CSG 集成服务
├── IConfiguration              — 配置投影
├── IStateMachine               — 状态机投影
├── IGraph                      — 图投影
│
LoopFramework 接口：
├── ILoopEngine                 — 循环引擎
├── ILoopController             — 循环控制器
├── IStateMachine               — 状态机
├── ILLMService                 — LLM 服务
│
CallFrame 接口：
├── ICallFrameService           — 调用帧服务
├── ICallFrameRecord            — 调用帧记录
│
Orchestration 接口：
├── ICompositionValidator       — 组合验证器
├── IContentManager             — 内容管理器
├── IStateManager               — 状态管理器
```

---

## 四、适用场景对比

| 场景 | 推荐方案 | 说明 |
|------|---------|------|
| **CLI 工具命令解析与执行** | ✅ CommandFramework | 轻量级命令解析，支持 echo/sleep/wait 等内置命令 |
| **自动化测试框架** | ✅ CommandFramework | 从文件逐行执行命令，支持断言和等待 |
| **GUI 自动化（WinForms）** | ✅ CommandFramework | 控件值读写、事件绑定、环境适配 |
| **需要 Undo/Redo 的场景** | ✅ CommandFramework | 内置命令历史栈 |
| **确定性业务流程编排** | ✅ WorkFlowMethod | 步骤固定、路径明确的业务逻辑 |
| **多系统数据集成** | ✅ WorkFlowMethod | 需要键位映射、数据转换的异构系统对接 |
| **JSON 配置驱动的工作流** | ✅ WorkFlowMethod | 业务人员可调整 JSON 配置无需改代码 |
| **运行时动态调整工作流** | ✅ WorkFlowMethod | Symbol 定位插入、节点删除与重连 |
| **AI 增强的工作流** | ✅ WorkFlowMethod | LLM 函数节点、Hybrid 决策模式 |
| **需要 CSG 一致性验证** | ✅ WorkFlowMethod | 配置/状态机/图三投影交叉验证 |
| **纯命令执行（无工作流）** | ✅ CommandFramework | 更轻量，无额外依赖开销 |

---

## 五、总结

### 5.1 核心差异总结

```
┌─────────────────────────────────────────────────────────────────┐
│                     WorkFlow 体系架构                             │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Operation 层                                              │   │
│  │  ┌────────────────────────────────────────────────────┐   │   │
│  │  │  WorkFlowMethod  (工作流编排与执行引擎)              │   │   │
│  │  │  • 工作流定义与编排  • 函数注册与执行                │   │   │
│  │  │  • 管线化处理        • 运行时动态更新                │   │   │
│  │  │  • CSG 一致性验证    • LLM 增强循环工作流            │   │   │
│  │  │  • 调用帧追踪        • 预定义工作流模板              │   │   │
│  │  └────────────────────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Share 层                                                 │   │
│  │  ┌────────────────────────────────────────────────────┐   │   │
│  │  │  CommandFramework  (通用命令执行框架)                │   │   │
│  │  │  • 命令解析    • 命令注册/执行                      │   │   │
│  │  │  • 撤销/重做   • 管线阶段化执行                     │   │   │
│  │  │  • 环境适配    • 控件值读写                         │   │   │
│  │  │  • 状态存储    • 函数执行体系                       │   │   │
│  │  └────────────────────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ⚠️ 注意：CommandFramework 与 WorkFlowMethod 之间无直接依赖关系   │
│     两者是平级且独立的模块，各自依赖不同的 Share/Modules 基础库    │
└─────────────────────────────────────────────────────────────────┘
```

### 5.2 一句话总结

| 项目 | 一句话总结 |
|------|-----------|
| **CommandFramework** | **通用命令执行框架** — 解决"如何解析和执行一条命令"的底层基础设施 |
| **WorkFlowMethod** | **工作流编排与执行引擎** — 解决"如何编排和执行一个完整业务流程"的核心引擎 |

### 5.3 关系总结

- **CommandFramework 与 WorkFlowMethod 之间无直接依赖关系**，两者是平级且独立的模块
- CommandFramework 关注**命令级**的通用执行，WorkFlowMethod 关注**工作流级**的业务编排
- WorkFlowMethod 在功能复杂度上远超 CommandFramework（12 个子模块 vs 1 个模块）
- WorkFlowMethod 拥有 CommandFramework 不具备的大量企业级能力（CSG、LLM、运行时动态更新等）
- 两者都采用了管线化执行模式，但抽象层次不同

---

## 六、整合方案

### 6.1 整合原则

1. **CommandFramework 不能反向引用 WorkFlowMethod** — Share 层不依赖 Operation 层，保持架构分层
2. **下迁范围仅限于函数执行、状态管理和参数路径相关逻辑** — 不涉及命令解析、控件读写等 CommandFramework 独有能力
3. **下迁目标为 Share 层公共模块** — 下迁到 `Share/` 下的通用模块（如 `ParameterResolver`、`StateManagement`），而非 WorkFlowMethod 本身

### 6.2 整合架构图

```
整合前：                             整合后：
┌──────────────┐                    ┌──────────────────┐
│ CommandFrame │                    │  CommandFrame    │（保留独有能力）
│  work        │                    │  ├─ 命令解析      │
│  ├─ 函数执行  │  ──下迁──→        │  ├─ 命令执行路由  │
│  ├─ 状态管理  │  ──下迁──→        │  ├─ 控件读写      │
│  ├─ 参数路径  │  ──下迁──→        │  └─ 非事件命令库  │
│  ├─ 命令解析  │（保留）            └──────────────────┘
│  ├─ 控件读写  │（保留）                    │引用
│  └─ 非事件库  │（保留）            ┌──────┴───────────┐
└──────────────┘                    │  Share 公共模块    │
                                    │  ├─ Parameter     │
┌──────────────┐                    │  │   Resolver     │（增强）
│ WorkFlow     │                    │  ├─ State         │
│   Method     │                    │  │   Management   │（增强）
│  (引用公共模块)│                    │  └─ PathManager   │（增强）
└──────────────┘                    └──────────────────┘
                                           │引用
                                    ┌──────┴───────────┐
                                    │  WorkFlowMethod   │（引用增强后的公共模块）
                                    └──────────────────┘
```

### 6.3 工作项 1：函数执行逻辑 → 下迁到 Share/ParameterResolver

CommandFramework 的 `FunctionExecutor` + `ArgumentBuilder` 中的参数匹配和类型转换逻辑，与 WorkFlowMethod 的 `ParameterResolver` 功能重叠。

**下迁内容**：

| 当前在 CommandFramework | 当前在 WorkFlowMethod | 下迁目标 |
|----------------------|---------------------|---------|
| `ArgumentBuilder.BuildFromDescriptor()` | `ParameterResolver` 的参数构建 | **Share/ParameterResolver**（增强，统一参数构建入口） |
| `ArgumentBuilder` 中的类型转换逻辑 | `TypeConverter.ConvertArguments()` | **Share/ParameterResolver**（统一类型转换） |
| `FunctionExecutor.ExecuteDelegateAsync()` 中的参数构建调用 | `FunctionExecutor` 中的参数构建调用 | 统一委托给增强后的 `ParameterResolver` |

**下迁后**：
- CommandFramework 和 WorkFlowMethod 都引用 `Share/ParameterResolver`，使用同一套参数构建逻辑
- CommandFramework 的 `FunctionExecutor` 保留执行调度逻辑，参数构建委托给 `ParameterResolver`

### 6.4 工作项 2：状态管理逻辑 → 下迁到 Share/StateManagement

CommandFramework 的 `InMemoryStateStore` + `CommandEngine` 历史栈，与 WorkFlowMethod 的 `StateManagement` + `CallFrame` 功能重叠。

**下迁内容**：

| 当前在 CommandFramework | 当前在 WorkFlowMethod | 下迁目标 |
|----------------------|---------------------|---------|
| `InMemoryStateStore`（内存状态存储） | `StateManagement` 状态管理 | **Share/StateManagement**（增强，统一状态存储接口） |
| `CommandEngine` 的 Undo/Redo 历史栈 | `CallFrame` 调用帧追踪 | **Share/StateManagement**（新增历史栈接口） |
| `IStateStore` 接口 | `IStateManager` 接口 | **Share/StateManagement**（统一状态接口） |

**下迁后**：
- CommandFramework 和 WorkFlowMethod 都引用 `Share/StateManagement`，使用同一套状态管理
- CommandFramework 的 `CommandEngine` 保留命令级调度，状态存储委托给 `StateManagement`

### 6.5 工作项 3：参数路径逻辑 → 下迁到 Share/PathManager

CommandFramework 中的路径解析逻辑（如配置文件路径、命令文件路径），与 WorkFlowMethod 的路径管理功能重叠。

**下迁内容**：

| 当前在 CommandFramework | 当前在 WorkFlowMethod | 下迁目标 |
|----------------------|---------------------|---------|
| 路径解析工具方法 | `PathManager` 路径管理 | **Share/PathManager**（增强，统一路径解析） |
| 配置加载路径逻辑 | 工作流文件路径管理 | **Share/PathManager**（统一路径接口） |

### 6.6 工作项 4：控件值读写体系 → 单独抽象为通用框架

将 CommandFramework 中的控件相关能力提取为独立的 Share 层模块（与之前方案一致）：

```
Share/ControlAccess/（新建）
├── IControlFinder              — 控件查找接口
├── IControlValueAccessor       — 控件值读写接口
├── IControlValueWriter         — 控件值写入接口
├── ControlValueAccessorBase    — 基类实现
├── CompositeControlFinder      — 复合查找器（多框架适配器共存）
├── CompositeControlWriter      — 复合写入器
└── 适配器注册机制
```

### 6.7 CommandFramework 保留的独有能力

下迁后 CommandFramework 保留以下内容（不涉及 WorkFlowMethod）：

- **命令解析** — `CommandParser`、`ParseCommandStage`（命令字符串 → ParsedCommand）
- **命令执行路由** — `ExecuteCommandStage`（set/click/非事件命令的路由逻辑）
- **控件值读写** — `IControlFinder`、`IControlValueWriter`、`ControlValueAccessorBase`（引用 `Share/ControlAccess`）
- **非事件命令库** — `echo`、`sleep`、`wait` 等内置命令
- **环境适配** — `IEnvironmentAdapter`、`EnvironmentAdapterBase`
- **统一入口** — `CommandFrameworkHost`、`CommandFrameworkUsage`

### 6.8 整合收益

| 收益 | 说明 |
|------|------|
| **消除功能重叠** | 不再有两套 `ArgumentBuilder`、`InMemoryStateStore`、路径解析 |
| **统一参数构建** | CommandFramework 和 WorkFlowMethod 共用 `Share/ParameterResolver` |
| **统一状态管理** | CommandFramework 和 WorkFlowMethod 共用 `Share/StateManagement` |
| **保持架构分层** | CommandFramework（Share 层）不反向依赖 WorkFlowMethod（Operation 层） |
| **控件框架通用化** | 不再耦合在 CommandFramework 中，所有模块均可复用 |
