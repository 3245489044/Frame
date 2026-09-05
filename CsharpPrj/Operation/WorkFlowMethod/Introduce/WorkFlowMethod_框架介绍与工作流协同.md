# WorkFlowMethod 框架介绍与工作流协同

## 一、概述

**WorkFlowMethod** 是一个基于 .NET 10.0 的**工作流编排与执行框架**，提供从工作流定义、构建、执行到监控的全生命周期管理能力。它是 WorkFlow 体系的核心引擎层，负责驱动所有业务流程的自动化执行。

与传统的 BPMN 工作流框架（如 Camunda、Temporal）不同，WorkFlowMethod 的设计理念更接近 **Cline、Trae 等 AI 驱动的智能工作流**——通过 LLM 驱动的循环决策、运行时动态调整、配置即工作流等能力，实现高度灵活和自适应的业务流程编排。

---

## 二、核心功能

### 2.1 工作流构建（三种方式）

| 构建方式 | 说明 | 适用场景 |
|---------|------|---------|
| **编程式构建** | 通过 WorkflowBuilder API 代码定义 | 复杂逻辑、需要编译期检查 |
| **JSON 文件加载** | 从 JSON 配置文件加载管线定义 | 配置驱动、无需重新编译 |
| **简洁字符串定义** | 特定格式字符串快速定义节点和连接 | 快速原型、测试开发 |

```csharp
// 编程式构建
var pipeline = WorkflowUseage.CreatePipeline("MyPipeline")
    .AddStep("Step1", new Func<int, int>(x => x + 1))
    .AddStep("Step2", new Func<int, int>(x => x * 2))
    .Build();

// JSON 文件加载
var result = await WorkflowUseage.ExecuteFromFileAsync("workflow.json");

// 简洁字符串定义
WorkflowBuilder.BuildFromStrings(
    "MergeNode:node_0:MergePrefix:DataProcess,Inputs_:Result_",
    "DataInitial:node_1:WebInvoke:WebSet_,Result_:Output_"
);
```

### 2.2 管线化执行引擎

基于 PipelineExecutor 的步骤编排，支持同步/异步执行、异常重试、超时控制：

```csharp
// 快速单步执行
var result = await WorkflowUseage.ExecuteStepAsync("Add",
    new Func<int, int, int>((a, b) => a + b), new object[] { 1, 2 });

// 快速多步执行
var multiResult = await WorkflowUseage.ExecuteStepsAsync(
    ("Step1", new Func<int, int>(x => x + 1)),
    ("Step2", new Func<int, int>(x => x * 2))
);
```

### 2.3 双注册表桥接

统一 `IWorkflowRegistry`（工作流函数）与 `IFunctionRegistry`（通用委托）两个注册体系：

```csharp
// 工作流函数注册（带 WorkflowContext）
WorkflowUseage.RegisterFunction("Add", ctx => {
    var a = ctx.GetParameter<int>("0");
    var b = ctx.GetParameter<int>("1");
    return a + b;
});

// 通用委托注册（任意签名）
WorkflowUseage.RegisterDelegate("Multiply", new Func<int, int, int>((a, b) => a * b));

// 桥接调用（自动选择注册表）
var result = WorkflowUseage.CallBridgedFunction("Add", context);
```

### 2.4 文件驱动

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

### 2.5 运行时动态更新

支持运行时动态调整工作流结构（插入、删除、替换、重定向节点），无需重启：

```csharp
// Symbol 定位插入子链（推荐方式）
var directive = new InsertChainDirective
{
    InsertAfterSymbol = "AIGenerateImage",
    SymbolMatchMode = SymbolMatchMode.First,
    ChainJsonConfigs = new List<string> { "[{\"Symbol\":\"ImagePostProcess\",...}]" }
};

// 复合更新：插入 + 删除
var composite = new CompositeDirective
{
    Directives = new List<GraphUpdateDirective>
    {
        new InsertChainDirective { InsertAfterSymbol = "Setup", ... },
        new DeleteNodeDirective { NodeKey = "obsolete", ReconnectNeighbors = true }
    }
};

var updatedChain = GraphWorkflowUpdater.ApplyGraphUpdate(originalChain, new[] { composite });
```

### 2.6 图引擎统一

以 GraphFoundation 边体系作为节点连接关系的**单一真实来源**，消除平行指针系统：

```
旧架构：NodeConfig.nextNodeConfig.NodeKey + Edge（两套系统，需手动同步）
新架构：仅 Edge（GraphFoundation 边是唯一真实来源）
       NodeKey 标记为 [Obsolete]，仅作为序列化缓存
```

### 2.7 预定义工作流模板

内置丰富的预定义模板，覆盖多个 AI 应用场景：

| 模板 | 说明 | 典型节点链 |
|------|------|-----------|
| **AI 图像生成** | 从提示词生成图像 | MergePrefix → WebInvoke → DonloadPhoto |
| **AI 代码生成** | 根据需求自动生成代码 | PromptCommandParse → WebInvoke → CompileCheck → DynamicExcute |
| **数据分析** | 数据处理和分析 | MergePrefix → WebInvoke → DataAnalysis → DataConformity |
| **数据存储** | 数据格式转换和持久化 | ToListDictionary → DataValueUpdate → DataValueRead |
| **大模型调用** | 通用 LLM 调用 | CodeInitial → WebInvoke |

### 2.8 状态管理与上下文

类型安全的执行上下文，支持断点续传与状态持久化：

```csharp
public class TypedExecutionContext
{
    public T? Get<T>(string key) where T : class;       // 强类型读取
    public T GetRequired<T>(string key) where T : class; // 强类型读取（不存在则抛异常）
    public void Set<T>(string key, T? value);            // 强类型写入
}
```

---

## 三、与 Cline / Trae 等 AI 工作流的对比

### 3.1 设计理念对比

| 维度 | Cline / Trae | WorkFlowMethod |
|------|-------------|----------------|
| **核心驱动** | LLM 实时推理驱动 | 配置驱动 + LLM 可选增强 |
| **决策方式** | LLM 自主选择工具 | 规则选择 / LLM 选择 / 混合选择 |
| **工作流定义** | 隐式（LLM 动态生成） | 显式（JSON/代码预定义） |
| **确定性** | 低（依赖 LLM 输出） | 高（规则优先，LLM 兜底） |
| **可调试性** | 困难（黑盒推理） | 容易（规则明确、日志完整） |
| **复用性** | 低（每次重新推理） | 高（模板可复用、可组合） |

### 3.2 LLM 驱动的循环工作流（LoopWorkflow）

WorkFlowMethod 借鉴 Cline 的"主循环 + 工具调用"模式，设计了 **LLM 驱动的循环工作流**：

```
Cline:        LLM 主循环 → 选择工具 → 执行工具 → 更新上下文 → 继续循环
                                               ↓
WorkFlowMethod: LoopWorkflow → 选择子工作流 → 执行子工作流 → 更新上下文 → 继续循环
```

#### 三种选择策略

| 策略 | 决策方式 | 确定性 | 性能 | 适用场景 |
|------|---------|--------|------|---------|
| **RuleBased** | 预定义条件匹配 | 100% | 毫秒级 | 标准审批、定时任务、简单 ETL |
| **LLMBased** | LLM 实时推理 | 不确定 | 秒级 | AI 客服、智能文档处理、代码分析 |
| **Hybrid** | 规则优先 + LLM 兜底 | 关键路径确定 | 大部分毫秒级 | 自动化测试、数据处理、运维自动化 |

#### 循环工作流架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        循环工作流 (LoopWorkflow)                  │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    主循环 (Main Loop)                     │    │
│  │                                                          │    │
│  │  ① 检查完成条件 → ② 选择子工作流 → ③ 执行子工作流 → ④ 更新上下文 │    │
│  │  └────────────────────── 循环直到完成 ────────────────────┘    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    选择策略 (SelectionStrategy)           │    │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────────┐     │    │
│  │  │ RuleBased  │  │ LLMBased   │  │ Hybrid         │     │    │
│  │  │ 规则匹配   │  │ LLM 推理   │  │ 规则+LLM 兜底  │     │    │
│  │  └────────────┘  └────────────┘  └────────────────┘     │    │
│  └─────────────────────────────────────────────────────────┘    │
└──────────────────────────┬──────────────────────────────────────┘
                           │
          ┌────────────────┼────────────────┬────────────────┐
          ▼                ▼                ▼                ▼
   ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐
   │ 子工作流 A   │  │ 子工作流 B   │  │ 子工作流 C   │  │ 子工作流 D   │
   │ (数据收集)   │  │ (数据分析)   │  │ (结果生成)   │  │ (错误恢复)   │
   └─────────────┘  └─────────────┘  └─────────────┘  └─────────────┘
```

#### 智能上下文（SmartContext）

类似 Cline 的对话上下文，SmartContext 维护所有执行历史和数据，为 LLM 提供结构化的上下文摘要：

```csharp
public class SmartContext
{
    public string Goal { get; set; }                    // 任务目标
    public List<string> CompletedSteps { get; }          // 已完成步骤
    public List<ExecutionRecord> History { get; }        // 执行历史（滑动窗口）
    public int MaxHistoryLength { get; set; } = 100;     // 最大历史记录数

    public T Get<T>(string key);                         // 数据读取
    public void Set(string key, object value);           // 数据写入
    public string ToLLMContext();                        // 构建 LLM 可理解的上下文摘要
}
```

### 3.3 与 Trae 的对比

Trae 作为 AI 驱动的 IDE 工作流工具，其核心能力是"理解用户意图 → 规划任务 → 执行代码 → 验证结果"。WorkFlowMethod 的 LoopWorkflow 实现了类似的模式：

| Trae 能力 | WorkFlowMethod 对应 |
|-----------|-------------------|
| 理解用户意图 | `LoopWorkflow.Goal` + `CompletionCondition` |
| 规划任务步骤 | `SelectionStrategy` 选择子工作流 |
| 执行代码/工具 | `SubWorkflowDefinition` 执行 ChainInfo 管线 |
| 验证结果 | 完成条件检查 + SmartContext 状态更新 |
| 迭代优化 | 循环机制（Loop）自动迭代 |

### 3.4 差异化优势

| 优势 | 说明 |
|------|------|
| **确定性 + 灵活性平衡** | Hybrid 策略：规则覆盖标准路径，LLM 处理异常，兼顾可靠与灵活 |
| **可组合性** | 子工作流可独立定义、测试、复用，不同循环工作流可引用同一子工作流 |
| **安全边界** | MaxIterations、CancellationToken、重试策略、超时控制等多层保护 |
| **渐进增强** | 从纯规则 → 规则+LLM → 纯LLM，可按需逐步引入 AI 能力 |
| **与现有架构兼容** | 完全复用 ChainInfo、CoreGraph、PipelineExecutor 等现有组件 |

---

## 四、与 WdUI 协同

WorkFlowMethod 作为**后端工作流引擎**，与 WdUI（前端 UI 框架）形成完整的"后端驱动 + 前端渲染"体系：

| 协同场景 | 实现方式 |
|---------|---------|
| **动态参数输入** | 工作流节点通过 JSON 配置定义输入参数，WdUI 动态渲染为表单界面 |
| **执行触发** | WdUI 按钮点击事件通过中介器调用 WorkFlowMethod 执行工作流 |
| **结果回显** | 工作流执行结果通过桥接机制写回 WdUI 控件，实时展示 |
| **配置驱动** | 同一份 JSON 配置既驱动工作流执行逻辑，又驱动 UI 渲染布局 |
| **容器化集成** | WdUI 的 ConfigContainer 作为工作流 UI 载体，动态加载不同工作流的配置界面 |
| **循环工作流可视化** | WdUI 实时展示 LoopWorkflow 的迭代进度、当前步骤、执行历史 |

---

## 五、架构定位

```
WorkFlow 体系架构：
  WdUI (UI 框架层)       → 动态渲染工作流参数输入与结果展示界面
       ↑ 事件/数据
  WorkFlowMethod (引擎层) → 工作流编排、函数注册、管线执行、循环决策
       ↑ 依赖
  BasicMethod/FunctionExecution (基础执行框架)
       ↑ 依赖
  UniversalJsonMiddleware (通用中间件)
       ↑ 依赖
  UniversalLayoutMath (纯数学计算)
```

WorkFlowMethod 是 WorkFlow 体系的**执行核心**，负责将业务逻辑编排为可执行的工作流管线。它借鉴了 Cline/Trae 等 AI 工作流的"循环决策 + 工具调用"模式，通过 LoopWorkflow 实现 LLM 驱动的智能编排，同时保留了传统工作流框架的确定性和可调试性。通过与 WdUI 的深度集成，实现**配置即工作流、界面即交互**的开发体验。

---

## 六、与主流工作流框架对比

| 维度 | Temporal | Camunda | Airflow | Elsa | **WorkFlowMethod** |
|------|----------|---------|---------|------|-------------------|
| **表示模型** | 代码定义 | BPMN 2.0 | Python DAG | 代码+JSON | **链+图双模** |
| **运行时动态更新** | ❌ | ✅ 部分 | ❌ | ✅ 部分 | **✅ 原生支持** |
| **LLM 驱动循环** | ❌ | ❌ | ❌ | ❌ | **✅ LoopWorkflow** |
| **Symbol 定位** | ❌ | ❌ | ❌ | ❌ | **✅ 独有** |
| **AI 工作流** | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | **⭐⭐⭐⭐⭐** |
| **动态编排** | ⭐⭐ | ⭐⭐⭐ | ⭐ | ⭐⭐⭐ | **⭐⭐⭐⭐⭐** |
| **C#/.NET 应用** | ⭐⭐⭐ | ⭐⭐ | ⭐ | ⭐⭐⭐⭐⭐ | **⭐⭐⭐⭐⭐** |
| **快速原型开发** | ⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | **⭐⭐⭐⭐⭐** |

> **一句话总结**：WorkFlowMethod 是一个在架构创新上极具特色的工作流框架，其链-图双模架构、运行时动态更新和 LLM 驱动的循环工作流能力在业界独树一帜，特别适合需要高度灵活性和 AI 编排能力的 C#/.NET 应用场景。
