# WorkFlowMethod 模块功能详解

> **版本**：基于 .NET 10.0  
> **命名空间**：`WorkFlow` / `WorkFlowMethod.*`  
> **定位**：WorkFlow 体系的核心引擎层 — 工作流编排、函数执行、管线化处理、运行时动态更新

---

## 一、概述

**WorkFlowMethod** 是 WorkFlow 体系的**工作流编排与执行引擎**，位于 `CsharpPrj/Operation/WorkFlowMethod/`。它提供从工作流定义、函数注册、管线执行到运行时动态更新的全生命周期管理能力，是驱动所有业务流程自动化执行的核心层。

### 1.1 设计哲学

- **万物皆函数** — 工作流中的每个步骤都是一个可注册的函数，支持任意委托签名
- **一切皆配置** — 工作流定义可通过 JSON 配置文件驱动，无需重新编译
- **运行时动态更新** — 工作流结构可在运行中动态调整，无需重启
- **LLM 增强** — 支持规则优先 + LLM 兜底的混合决策模式

### 1.2 架构定位

```
WorkFlow 体系架构（自顶向下）：

  WdUI (UI 框架层)           → 动态渲染工作流参数输入与结果展示界面
       ↑ 事件/数据
  FunctionStorage (桥接层)    → 函数库注册、UI 桥接、JSON 工作流执行
       ↑ 依赖
  FunctionExecution (执行层)  → 函数注册、参数解析、委托调用
       ↑ 依赖
  WorkFlowMethod (引擎层)     → ★ 本文档介绍的核心模块 ★
       ↑ 依赖
  BasicMethod / GraphProcessing / LogicComposition / Pipeline (基础框架)
       ↑ 依赖
  UniversalJsonMiddleware / UniversalLayoutMath (通用基础设施)
```

---

## 二、核心子模块

WorkFlowMethod 由 12 个子模块组成，每个模块聚焦于一个独立的功能领域：

```
WorkFlowMethod/
├── WorkflowUseage.cs              — 统一静态入口（前台使用模块）
├── WorkFlowMethod.Usage.cs        — 统一调用入口文档
├── FunctionExecutionFramework/    — 函数执行框架
├── Execution/                     — 执行引擎
├── WorkFlowPipeline/              — 管线系统
├── NodeProcess/                   — 节点处理系统
├── Orchestration/                 — 编排层
├── CSG/                           — CSG 协议集成
├── CallFrame/                     — 调用帧框架
├── LoopFramework/                 — 循环框架
├── GraphUsing/                    — 图使用服务
├── Config/                        — 工作流配置集
└── Infrastructure/                — 基础设施
```

### 2.1 WorkflowUseage — 统一静态入口

**文件**：`WorkflowUseage.cs`

提供**统一的函数注册、执行、管线编排、文件驱动**入口，桥接两个函数注册体系：

| 注册体系 | 接口 | 函数签名 | 用途 |
|---------|------|---------|------|
| 工作流函数注册表 | `IWorkflowRegistry` | `Func<WorkflowContext, object>` | 工作流编排专用 |
| 通用委托注册表 | `IFunctionRegistry` | 任意 `Delegate` | 通用函数执行 |

**核心能力**：

```csharp
// 初始化
WorkflowUseage.Initialize();

// 注册工作流函数
WorkflowUseage.RegisterFunction("Add", ctx => {
    var a = ctx.GetParameter<int>("0");
    var b = ctx.GetParameter<int>("1");
    return a + b;
});

// 注册通用委托
WorkflowUseage.RegisterDelegate("Multiply", new Func<int, int, int>((a, b) => a * b));

// 执行函数（自动选择注册表）
var result = WorkflowUseage.CallBridgedFunction("Add", context);

// 管线编排
var pipeline = WorkflowUseage.CreatePipeline("MyPipeline")
    .AddStep("Step1", new Func<int, int>(x => x + 1))
    .AddStep("Step2", new Func<int, int>(x => x * 2))
    .Build();
var result = await WorkflowUseage.ExecutePipelineAsync(pipeline);

// 文件驱动
var result = await WorkflowUseage.ExecuteFromFileAsync("workflow.json");
```

### 2.2 FunctionExecutionFramework — 函数执行框架

**文件**：`FunctionExecutionFramework/FunctionExecutionFramework.cs`

提供**框架静态入口**，整合函数注册、执行、管线等核心能力：

| 组件 | 说明 |
|------|------|
| `DefaultWorkflowRegistry` | 默认工作流注册表（Lazy 初始化） |
| `PipelineExecutor` | 管线执行器 |
| `WorkflowFunctionRegistrar` | 工作流函数注册器 |
| `BasicMethodAdapter` | 双向同步适配器（桥接 WorkFlowMethod 与 BasicMethod） |
| `BuiltInFunctionRegistrar` | 内置函数注册器 |

```csharp
// 框架静态入口
FunctionExecutionFramework.Register("MyFunc", ctx => { ... });
FunctionExecutionFramework.Register("MyDelegate", new Func<int, int>(x => x * 2));

// 执行
var result = await FunctionExecutionFramework.ExecuteAsync("MyFunc", parameters);

// 管线
var pipeline = FunctionExecutionFramework.CreatePipeline("MyPipeline")
    .AddStep("Step1", ...)
    .Build();
var result = await FunctionExecutionFramework.ExecutePipelineAsync(pipeline);
```

### 2.3 Execution — 执行引擎

**文件**：`Execution/RefactoredExecution.cs`、`Execution/WorkflowBuilder.cs`

提供**重构后的工作流执行器**和**工作流构建工具**：

#### RefactoredWorkflowExecutor

- 统一异常处理（`SimpleExceptionHandler`）
- 类型安全的执行上下文
- 状态管理（`ExecutionStateManager`）
- 支持取消令牌（`CancellationToken`）

```csharp
var executor = new RefactoredWorkflowExecutor();
var result = await executor.ExecuteWorkflowAsync(
    nodeKey: "DataAnalysis",
    parameters: new Dictionary<string, object> { { "input", data } },
    cancellationToken: cancellationToken
);
```

#### 模块创建系统

- `ModuleCreationManager` — 延迟初始化、批量创建模块
- `DefaultModuleCreator` — 默认模块创建器，委托给 `IWorkflowMediator`
- `ModuleCreationConfig` — 强类型配置（WorkflowId、Symbol、ChainCount）

### 2.4 WorkFlowPipeline — 管线系统

**文件**：`WorkFlowPipeline/PipelineBuilder.cs`、`PipelineContextAccessor.cs`、`PipelineStages.cs`

| 组件 | 说明 |
|------|------|
| `PipelineBuilder` | 管线构建器，支持步骤编排 |
| `PipelineContextAccessor` | 管线上下文访问器，提供节点数据、边信息访问 |
| `PipelineStages` | 管线阶段定义 |

### 2.5 NodeProcess — 节点处理系统

**文件**：`NodeProcess/` 目录（10+ 文件）

节点处理系统是 WorkFlowMethod 的核心执行单元，负责工作流节点的创建、配置、执行、生命周期管理：

| 组件 | 文件 | 说明 |
|------|------|------|
| `NodeChainSkeleton` | `NodeChainSkeleton.cs` | 节点链骨架，继承 `SkeletonBase` |
| `NodeConfigFactory` | `NodeConfigFactory.cs` | 节点配置工厂，支持模板合并 |
| `NodeProcessPipelineBuilder` | `NodeProcessPipelineBuilder.cs` | 节点管线构建器，支持图定义、节点链、提取、更新、执行、循环、指标等阶段 |
| `ChainInfoWorkflowStorageManager` | `Storage.cs` | 工作流存储管理器（CRUD） |
| `WebCallManager` | `Storage.cs` | Web 调用配置管理器 |

**NodeProcessPipelineBuilder 支持阶段**：

```
AddGraphDefinition()    → 图定义
AddNodeChain()          → 节点链
AddNodeExtractor()      → 节点提取
AddGraphChainUpdate()   → 图链更新
AddNodeExecution()      → 节点执行
AddNodeLoop()           → 节点循环
AddNodeMetrics()        → 节点指标
```

### 2.6 Orchestration — 编排层

**文件**：`Orchestration/Validation.cs`、`Orchestration/Content/`、`Orchestration/State/`

| 组件 | 说明 |
|------|------|
| `ModularValidator` | 模块化验证器 |
| `ContentManager` | 内容管理 |
| `StateManager` | 状态管理 |

### 2.7 CSG — CSG 协议集成

**文件**：`CSG/CsgIntegrationService.cs`

集成 **CSG 协议**（Configuration-StateMachine-Graph 统一中间表示），实现工作流的三投影一致性验证：

```
┌─────────────────────────────────────────────┐
│              CSG 三投影模型                    │
│                                              │
│  C 投影 (Configuration)  → 工作流配置视图     │
│  S 投影 (StateMachine)   → 状态机视图         │
│  G 投影 (Graph)          → 图结构视图         │
└─────────────────────────────────────────────┘
```

**核心能力**：

| 功能 | 说明 |
|------|------|
| `ChainToCanonical()` | 将 `ChainInfo` 转换为标准中间表示（CanonicalForm） |
| `ValidateWorkflow()` | 三投影一致性验证（C/S/G 视图交叉检查） |
| `ExportToJson()` | 执行结果的标准 JSON 导出 |
| `ResolveUri()` | URI 寻址（`csg://` 协议） |

**适配器注册**：

```csharp
// C 投影：PipelineContext → IConfiguration
_csg.RegisterAdapter<PipelineContext, IConfiguration>(
    ctx => new PipelineContextConfigAdapter(ctx));

// S 投影：PipelineContext → IStateMachine
_csg.RegisterAdapter<PipelineContext, IStateMachine>(
    ctx => new PipelineContextStateMachineAdapter(ctx));

// G 投影：GraphSnapshot → IGraph
_csg.RegisterAdapter<GraphSnapshot<string, NodeConfig, object>, IGraph>(
    snap => new GraphSnapshotGraphAdapter(snap));
```

### 2.8 CallFrame — 调用帧框架

**文件**：`CallFrame/` 目录

提供工作流调用链的追踪与调试能力：

| 组件 | 说明 |
|------|------|
| `CallFrameService` | 调用帧服务 |
| `CallFrameRecord` | 调用帧记录模型 |
| `Abstractions/` | 抽象接口 |
| `Core/` | 核心实现 |
| `Extensions/` | 扩展方法 |
| `Models/` | 数据模型 |

### 2.9 LoopFramework — 循环框架

**文件**：`LoopFramework/` 目录

实现 **LLM 驱动的循环工作流**，借鉴 Cline/Trae 的"主循环 + 工具调用"模式：

```
┌─────────────────────────────────────────────────────────────┐
│                        循环工作流 (LoopWorkflow)              │
│                                                              │
│  ① 检查完成条件 → ② 选择子工作流 → ③ 执行子工作流 → ④ 更新上下文 │
│  └────────────────────── 循环直到完成 ────────────────────────┘
│                                                              │
│  选择策略：                                                   │
│  ┌────────────┐  ┌────────────┐  ┌────────────────┐         │
│  │ RuleBased  │  │ LLMBased   │  │ Hybrid         │         │
│  │ 规则匹配   │  │ LLM 推理   │  │ 规则+LLM 兜底  │         │
│  └────────────┘  └────────────┘  └────────────────┘         │
└─────────────────────────────────────────────────────────────┘
```

| 组件 | 说明 |
|------|------|
| `HybridStateMachine` | 混合状态机（规则优先，LLM 兜底） |
| `LoopEngine` | 循环引擎 |
| `StateMachine/` | 状态机实现 |
| `Observability/` | 可观测性 |

### 2.10 GraphUsing — 图使用服务

**文件**：`GraphUsing/` 目录

提供基于图引擎的工作流操作服务：

| 组件 | 说明 |
|------|------|
| `GraphValidator` | 图验证器 |
| `NodeChainService` | 节点链服务 |
| `ConfigDeduplicationProcessor` | 配置去重处理器 |
| `DefaultDirectiveApplier` | 默认指令应用器 |
| `SkeletonBridge/` | 骨架桥接 |

### 2.11 Config — 工作流配置集

**文件**：`Config/WorkFlowsSet.cs`

内置丰富的预定义工作流模板：

| 模板 | 说明 | 典型节点链 |
|------|------|-----------|
| **AI 图像生成** | 从提示词生成图像 | MergePrefix → WebInvoke → DonloadPhoto |
| **AI 代码生成** | 根据需求自动生成代码 | PromptCommandParse → WebInvoke → CompileCheck → DynamicExcute |
| **代码综合生成** | 完整代码生成管线 | PromptCommandParse → WebInvoke → CompileCheck → RecordSpaceGenerator → OptimizeFunction → SequentialStorage |
| **数据分析** | 数据处理和分析 | MergePrefix → WebInvoke → DataAnalysis → DataConformity |
| **数据存储** | 数据格式转换和持久化 | ToListDictionary → DataValueUpdate → DataValueRead |
| **大模型调用** | 通用 LLM 调用 | CodeInitial → WebInvoke |

### 2.12 Infrastructure — 基础设施

**文件**：`Infrastructure/` 目录

| 组件 | 说明 |
|------|------|
| `ExecutionContext` | 类型安全执行上下文 |
| `WorkflowConstants` | 工作流常量 |
| `ExpandoObjectHelper` | ExpandoObject 工具 |
| `ValidationResult` | 验证结果 |
| `WorkflowLogger` | 日志记录器 |
| `ServiceDefaults` | 服务默认值 |
| `DefaultBridgeContext` | 默认桥接上下文 |

---

## 三、核心机制

### 3.1 双注册表桥接

WorkFlowMethod 统一了两个函数注册体系：

```
┌──────────────────────┐         ┌──────────────────────────┐
│  IFunctionRegistry   │ ◄─────► │  IWorkflowRegistry       │
│  (通用委托注册)       │  桥接   │  (工作流上下文注册)       │
│                      │         │                          │
│  PipelineExecutor    │         │  DefaultWorkflowRegistry │
│  FunctionExecutor    │         │  OverridableWorkflowReg  │
└──────────────────────┘         └──────────────────────────┘
         ▲                                    ▲
         │                                    │
         │  WorkflowFunctionUnitAdapter       │
         │  (IWorkflowRegistry → IFunctionUnit)│
         │                                    │
         │  BasicToWorkflowRegistryAdapter    │
         │  (IFunctionRegistry → IWorkflowRegistry)│
         └────────────────────────────────────┘
```

### 3.2 文件驱动

从 JSON 文件加载管线定义并执行，实现**配置即工作流**：

```json
{
  "name": "MyPipeline",
  "steps": [
    { "name": "Step1", "useExplicitParameters": true, "parameters": [1, 2] },
    { "name": "Step2", "useExplicitParameters": false }
  ]
}
```

### 3.3 运行时动态更新

基于 GraphFoundation 边体系作为节点连接关系的**单一真实来源**，支持运行时动态调整工作流结构：

- **Symbol 定位插入** — 通过 Symbol 标识定位插入点
- **节点删除与重连** — 删除节点时自动重连邻居
- **复合更新指令** — 一次应用多个更新操作

### 3.4 CSG 三投影一致性

通过 CSG 协议将工作流的配置、状态机、图结构三个视图统一为规范形式，并交叉验证一致性：

```
验证流程：
1. 获取 C 投影（配置视图）→ CanonicalForm
2. 获取 S 投影（状态机视图）→ CanonicalForm
3. 获取 G 投影（图结构视图）→ CanonicalForm
4. 交叉验证：
   - C 投影的 Action 节点应在 S 投影中存在
   - S 投影的当前状态应在 G 投影中存在
   - G 投影的边应在 S 投影中有对应的 Transition
   - C/S 投影的 ContextId 应一致
```

### 3.5 LLM 驱动的循环工作流

三种选择策略：

| 策略 | 决策方式 | 确定性 | 性能 | 适用场景 |
|------|---------|--------|------|---------|
| **RuleBased** | 预定义条件匹配 | 100% | 毫秒级 | 标准审批、定时任务、简单 ETL |
| **LLMBased** | LLM 实时推理 | 不确定 | 秒级 | AI 客服、智能文档处理、代码分析 |
| **Hybrid** | 规则优先 + LLM 兜底 | 关键路径确定 | 大部分毫秒级 | 自动化测试、数据处理、运维自动化 |

---

## 四、四层定位：AI 时代工作流光谱中的独特坐标

在 AI 时代的工作流图谱中，大多数框架都在追逐"更智能"——让 LLM 做路由决策、动态生成执行图、多 Agent 自主协商。但 WorkFlowMethod 选择了一条不同的路：**用声明式配置解决企业集成问题，把 AI 作为可选能力而非核心依赖。**

根据[四层演进图谱](https://github.com/3245489044/ZhiLianStorage)的分级框架：

| 层级 | 名称 | 核心特征 | 代表框架 |
|------|------|---------|---------|
| 第一层 | 硬编码管道 | 步骤在代码中写死 | 传统代码 |
| **第二层** | **声明式管道** | **步骤在配置中声明，路径固定** | **WorkFlowMethod** |
| 第三层 | 预置图状态机 | 拓扑固定，路径由 LLM 运行时选择 | LangGraph |
| 第四层 | 动态图生成 | 拓扑与路径均由 AI 动态生成 | AutoGen |

**WorkFlowMethod 的核心定位是第二层（声明式管道）**，但它在第二层的基础上做了大量企业级增强，使其在"配置时组合"这个方向上达到了远超 LangChain 旧版 Chain 的能力。

### 4.1 与 LangChain 的对比

| 维度 | LangChain 旧版 Chain | WorkFlowMethod |
|------|---------------------|----------------|
| 执行模型 | 线性链 | 线性链（ChainInfo） |
| 步骤定义 | Python 对象参数 | JSON 配置文件 |
| LLM 角色 | 节点内部的黑盒调用 | 节点内部的函数调用（可包含 LLM） |
| 路由决策 | 无，路径固定 | NextNodeKey，路径固定 |
| 配置形式 | 代码内嵌 | 独立 JSON 文件 |
| 组合方式 | SequentialChain | ChainInfo + ConnectChainInfoConfig |
| 键位映射 | ❌ 无 | ✅ 原生支持 |
| 去重逻辑 | ❌ 无 | ✅ 原生支持 |
| 数据流规则 | ❌ 无 | ✅ 原生支持 |
| 暂停/恢复 | ❌ 不支持 | ✅ 支持 |
| 企业集成 | ❌ 弱 | ✅ 强 |

### 4.2 与 LangGraph 的对比

| 维度 | LangGraph | WorkFlowMethod |
|------|-----------|----------------|
| 执行模型 | 图状态机 | 线性链 |
| 路径确定性 | 拓扑固定，路径运行时选择 | 100% 固定 |
| 路由决策者 | LLM 输出 + 条件边 | JSON 配置中的 NextNodeKey |
| 分支能力 | 原生支持条件/并行分支 | 无原生分支 |
| 状态持久化 | 检查点持久化 | 内存字典 |
| AI 角色 | 框架核心组件 | 外部调用者或被封装在节点内 |
| 运行时动态更新 | ❌ 不支持 | ✅ 原生支持 |
| Symbol 定位插入 | ❌ 无 | ✅ 独有 |

### 4.3 与 AutoGen 的对比

| 维度 | AutoGen | WorkFlowMethod |
|------|---------|----------------|
| 层级 | 第四层（动态图生成） | 第二层（声明式管道） |
| 语言 | Python | C# .NET |
| 执行模型 | 动态图（Agent 自主协商） | 线性链 |
| 路由决策 | Agent 自主协商 | 配置时确定 |
| AI 依赖 | 核心依赖 | 可选 |
| 企业集成 | ❌ 弱 | ✅ 强 |
| 确定性 | 低（依赖 LLM 输出） | 高（配置确定） |
| 适用场景 | 纯 AI 探索、任务目标模糊 | 确定性业务流程、多系统集成 |

### 4.4 核心设计哲学差异：配置时组合 vs 运行时路由

WorkFlowMethod 最根本的设计理念是：**多个 JSON 工作流在配置时通过声明式规则合并成一个确定性的执行链，而不是在运行时由 LLM 动态选择路径。**

```
WorkFlowMethod:    [JSON-A] ──映射规则──→ [JSON-AB] ──执行──→ [结果]
                    [JSON-B] ──┘            ↑
                    配置时合并              确定性执行

LangGraph:         [图定义] ──LLM决策──→ [路径A] ──→ [结果A]
                                ──→ [路径B] ──→ [结果B]
                    运行时路由            每次执行可能不同
```

这种设计在工程上的优势：
- **可审计** — 合并后的 JSON 是确定的，可以审查、测试、版本控制
- **可复用** — 基础工作流片段可以像函数库一样被多个上层工作流引用
- **无黑盒** — 不依赖 LLM，执行结果 100% 可预测
- **低延迟** — 没有 LLM 调用开销，执行速度快

### 4.5 综合对比总结

| 维度 | WorkFlowMethod | LangChain (旧版) | LangGraph | AutoGen |
|------|---------------|-----------------|-----------|---------|
| **层级** | 第二层 | 第二层 | 第三层 | 第四层 |
| **语言** | C# .NET | Python | Python | Python |
| **执行模型** | 线性链 | 线性链 | 图状态机 | 动态图 |
| **路由决策** | 配置时确定 | 配置时确定 | LLM 运行时决策 | Agent 自主协商 |
| **键位映射** | ✅ 原生支持 | ❌ 无 | ❌ 无 | ❌ 无 |
| **去重逻辑** | ✅ 原生支持 | ❌ 无 | ❌ 无 | ❌ 无 |
| **数据流规则** | ✅ 原生支持 | ❌ 无 | ❌ 无 | ❌ 无 |
| **运行时动态更新** | ✅ 原生支持 | ❌ 不支持 | ❌ 不支持 | ❌ 不支持 |
| **暂停/恢复** | ✅ 支持 | ❌ 不支持 | ✅ 检查点 | ❌ 不支持 |
| **AI 依赖** | 可选 | 可选 | 核心依赖 | 核心依赖 |
| **企业集成** | ✅ 强 | ❌ 弱 | ❌ 弱 | ❌ 弱 |

---

## 五、独特能力：LangGraph 没有的，WorkFlowMethod 有

### 5.1 键位映射（KeyMapping）

解决"多个异构工作流如何数据对接"的问题：

```json
{
  "KeyMapping": {
    "DataInitial_Result": "Value1",
    "Outputs_*": "Inputs_*"
  }
}
```

支持多种映射规则类型：
- **Direct** — 直接映射
- **Regex** — 正则表达式映射
- **MultiLevel** — 多层级映射（如 `A.B.C` → `X.Y.Z`）
- **Prefix/Suffix** — 前缀/后缀映射

### 5.2 去重逻辑（Deduplication）

防止工作流重复注册，支持增量更新：

```csharp
public class DefaultDeduplicationProcessor : IDeduplicationProcessor
{
    // 基于 Callkey 的去重
    // 支持 forceUpdate 强制更新模式
    // 支持与已有工作流对比去重
}
```

### 5.3 数据流规则（Dataflows）

声明式定义数据如何在节点间流转：

```json
{
  "Dataflows": {
    "VALUESET.1": {"Key": "DataProcess", "Value": "DataProcess"},
    "VALUESET.2": {"Key": "WebSet_", "Value": "DeepSeek能力增强版"}
  }
}
```

### 5.4 跨链连接（ConnectChainInfoConfig）

在运行时动态拼接多个工作流：

```json
{
  "nextNodeConfig": {
    "ConnectChainInfoKey": "OBJSET:{...}, VALUESET:{...}",
    "ConnectChainInfoConfig": "数据分析_2",
    "UpdateChain": "true",
    "AfterNodeCallkey": "DataAnalysis"
  }
}
```

### 5.5 企业级运行时能力

| 能力 | 实现 | 说明 |
|------|------|------|
| 暂停/恢复 | ManualResetEventSlim | 真正的断点续传 |
| 取消执行 | CancellationTokenSource | 支持超时和手动取消 |
| 执行历史 | HistoryEntry 列表 | 完整的执行轨迹记录 |
| 状态管理 | WorkflowStatus 枚举 | Idle → Running → Paused/Completed/Failed/Canceled |
| 调用帧记录 | CallFrameRecord | 每个节点的输入输出快照 |

---

## 六、AI 集成：作为可选能力而非核心依赖

WorkFlowMethod 对 AI 的态度是"集成优先，AI 可选"——AI 不是框架的核心依赖，而是可以被灵活集成的可选能力。

### 6.1 AI 作为 JSON 生成器

当 AI 在 WorkFlowMethod 的上游生成 JSON 配置时，整个系统在功能上达到了第四层（动态图生成）：

```
AI（生成 JSON 配置） → WorkFlowMethod（执行 JSON 配置）
         ↑ 第四层                     ↑ 第二层
```

WorkFlowMethod 在其中扮演的是"可靠执行引擎"的角色——AI 可以天马行空地规划，但执行必须可靠、可追溯、可恢复。

### 6.2 AI 作为函数节点

LangGraph/AutoGen 等 AI 框架可以作为函数节点注册到 WorkFlowMethod 中：

```csharp
// 注册一个调用 LangGraph 的函数节点
WorkflowUseage.RegisterFunction("LangGraph_Agent", ctx =>
{
    var config = ctx.GetParameter<string>("config");
    return CallLangGraphViaHttp(config);  // HTTP 桥接
});
```

在 JSON 配置中直接引用：

```json
{
  "Symbol": "LangGraph_Agent",
  "funcPipelineConfig": {
    "FuncName": "LangGraph_Agent",
    "ParameterKeys": ["config"],
    "ResultKeys": ["LangGraph_Result"]
  }
}
```

### 6.3 适用场景分析

| 场景类型 | 推荐方案 | 说明 |
|---------|---------|------|
| **确定性业务流程编排** | WorkFlowMethod 原生 | 步骤固定、路径明确的业务逻辑 |
| **多系统数据集成** | WorkFlowMethod 原生 | 需要键位映射、数据转换的异构系统对接 |
| **企业级 .NET 应用** | WorkFlowMethod 原生 | 需要暂停/恢复、历史审计、事务保障的场景 |
| **配置驱动的工作流** | WorkFlowMethod 原生 | 业务人员需要频繁调整参数但无需改代码 |
| **AI 辅助决策** | WorkFlowMethod + LLM 函数节点 | 在特定节点内部调用 LLM 进行内容审核、分类等 |
| **混合工作流** | WorkFlowMethod + LangGraph/AutoGen 桥接 | 确定性流程由 WorkFlowMethod 执行，AI 驱动流程桥接到外部框架 |
| **纯 AI 探索** | AutoGen | 任务目标模糊、需要完全自主规划的场景 |
| **高动态路由** | LangGraph | 路径高度不确定、需要 LLM 每步决策的场景 |

### 6.4 最稳妥的生产级策略

> **用 WorkFlowMethod 的 JSON 配置搭建刚性骨架，在骨架上预留柔性接口，将 LangGraph/AutoGen 作为"特种兵"以函数节点的形式空降到局部复杂战场。**

---

## 七、依赖关系

### 7.1 项目依赖

| 依赖项目 | 说明 |
|---------|------|
| `BasicMethod.csproj` | 基础方法库（FunctionExecution 核心） |
| `GraphProcessing.csproj` | 图处理 |
| `LogicComposition.csproj` | 逻辑组合（合并策略、模板提供器） |
| `GraphFramework.csproj` | 图框架 |
| `TreeTraceFramework.csproj` | 树追踪框架 |
| `Pipeline.csproj` | 管线框架 |
| `UniversalWorkflowFramework.csproj` | 通用工作流框架（别名 `ShareAlias`） |
| `StateManagement.csproj` | 状态管理 |
| `WorkflowExecution.csproj` | 统一工作流执行框架 |
| `UniversalMiddleware.csproj` | 通用中间件 |
| `CSG.Unified.csproj` | CSG 协议统一中间表示 |

### 7.2 NuGet 依赖

| 包 | 用途 |
|----|------|
| `Microsoft.Extensions.Caching.Memory` | 内存缓存 |

---

## 八、使用示例

### 8.1 完整工作流示例

```csharp
using WorkFlow;
using WorkFlowMethod.FunctionExecutionFramework;
using WorkFlowMethod.FunctionExecutionFramework.Core;

// 1. 初始化
WorkflowUseage.Initialize();

// 2. 注册工作流函数
WorkflowUseage.RegisterFunction("Add", ctx =>
{
    var a = ctx.GetParameter<int>("0");
    var b = ctx.GetParameter<int>("1");
    return a + b;
});

WorkflowUseage.RegisterAsyncFunction("FetchData", async ctx =>
{
    var url = ctx.GetParameter<string>("url");
    using var http = new HttpClient();
    return await http.GetStringAsync(url);
});

// 3. 注册通用委托
WorkflowUseage.RegisterDelegate("Multiply", new Func<int, int, int>((a, b) => a * b));

// 4. 创建并执行管线
var pipeline = WorkflowUseage.CreatePipeline("CalcPipeline")
    .AddStep("Add", new Func<int, int, int>((a, b) => a + b))
    .AddStep("Multiply", new Func<int, int>((x) => x * 2))
    .Build();

var result = await WorkflowUseage.ExecutePipelineAsync(pipeline,
    new ExecutionContext { FunctionName = "CalcPipeline" });

// 5. 文件驱动
await WorkflowUseage.ExecuteFromFileAsync("workflow.json");
```

### 8.2 使用预定义工作流模板

```csharp
// 从配置集获取预定义工作流
var workflowJson = WorkFlowsSet.BaseVersion.baseVersion_String["数据分析"];

// 加载并执行
var pipeline = WorkflowUseage.LoadPipelineFromJson(workflowJson);
var result = await WorkflowUseage.ExecutePipelineAsync(pipeline);
```

### 8.3 CSG 集成示例

```csharp
using WorkFlowMethod.CSG;

var csgService = new CsgIntegrationService();

// ChainInfo → CanonicalForm 转换
var canonicalForm = csgService.ChainToCanonical(chainInfo);

// 三投影一致性验证
var validationResult = csgService.ValidateWorkflow(pipelineContext);

// 导出为 JSON
var json = csgService.ExportToJson(pipelineContext);

// URI 寻址
var node = csgService.ResolveUri("csg://node/DataAnalysis", pipelineContext);
```

### 8.4 AI 桥接示例

```csharp
// 将 LangGraph Agent 注册为工作流函数节点
WorkflowUseage.RegisterFunction("LangGraph_Analysis", ctx =>
{
    var inputData = ctx.GetParameter<string>("input");
    // 通过 HTTP 桥接调用 LangGraph 服务
    var result = CallExternalAIService(inputData);
    return result;
});

// 在 JSON 配置中引用
// { "Symbol": "LangGraph_Analysis", "funcPipelineConfig": { ... } }
```

---

## 九、与 WorkFlow 体系其他模块的协同

| 协同模块 | 协同方式 |
|---------|---------|
| **WdUI (UI 框架层)** | WorkFlowMethod 作为后端引擎，WdUI 动态渲染工作流参数输入与结果展示 |
| **FunctionStorage (桥接层)** | FunctionStorage 的函数库通过 `RegisterDelegate()` 注册到 WorkFlowMethod |
| **BasicMethod (基础执行框架)** | WorkFlowMethod 基于 BasicMethod 的 FunctionExecution 管线引擎 |
| **FileDriven (文件驱动框架)** | WorkFlowMethod 的文件驱动能力与 FileDriven 框架配合使用 |

---

## 十、总结

**WorkFlowMethod** 是一个功能完备的工作流编排与执行引擎，其核心价值在于：

1. **统一函数注册** — 桥接工作流函数与通用委托两个注册体系
2. **管线化执行** — 通过 PipelineExecutor 编排工作流步骤
3. **文件驱动** — 从 JSON 配置文件加载工作流，实现配置即工作流
4. **运行时动态更新** — 工作流结构可在运行中动态调整
5. **CSG 协议集成** — 配置-状态机-图三投影一致性验证
6. **LLM 驱动的循环工作流** — 规则优先 + LLM 兜底的混合决策
7. **丰富的预置模板** — 数据分析、代码生成、图像生成等开箱即用
8. **调用帧追踪** — 工作流调用链的追踪与调试

### 10.1 一句话总结

> **WorkFlowMethod 不是另一个 LangGraph，而是企业级 .NET 生态中"配置时组合"工作流引擎的最优解。**

### 10.2 没有优劣，只有视点

回到四层演进图谱的核心观点：**这四层不是代际更替，而是一条控制权光谱。**

- LangGraph 解决的是"智能决策"问题——"下一步该做什么？"
- WorkFlowMethod 解决的是"企业集成"问题——"多个系统怎么对接？"

两者不是竞争关系，而是**不同视点下的不同选择**。当你的核心痛点是"多个异构 JSON 工作流如何无缝合并"时，WorkFlowMethod 的键位映射、去重逻辑、数据流规则是 LangGraph 无法替代的。而当你的核心痛点是"LLM 如何动态选择执行路径"时，LangGraph 的条件边是 WorkFlowMethod 需要桥接的。

WorkFlowMethod 是 WorkFlow 体系的**执行核心**，它将业务逻辑编排为可执行的工作流管线，通过配置驱动、运行时动态更新和 LLM 增强等能力，实现了高度灵活和自适应的业务流程编排。
