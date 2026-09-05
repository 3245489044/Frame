# LLM 驱动的循环工作流设计方案

> 文档版本：v3.0
> 更新日期：2026-05-16
> 设计目标：为 WorkFlowMethod 引入类似 Cline 的 LLM 驱动的实时推理和决策能力
> 聚焦范围：**循环机制（Loop）** 与 **选择机制（Selection）**

---

## 目录

- [LLM 驱动的循环工作流设计方案](#llm-驱动的循环工作流设计方案)
  - [目录](#目录)
  - [1. 设计理念](#1-设计理念)
    - [核心思想](#核心思想)
    - [为什么选择这个方案？](#为什么选择这个方案)
    - [与 Cline 的类比](#与-cline-的类比)
  - [2. 架构总览](#2-架构总览)
  - [3. 循环机制（Loop）](#3-循环机制loop)
    - [3.1 主循环流程](#31-主循环流程)
    - [3.2 循环工作流定义（LoopWorkflow）](#32-循环工作流定义loopworkflow)
    - [3.3 循环状态机](#33-循环状态机)
    - [3.4 完成条件检查](#34-完成条件检查)
    - [3.5 循环安全边界](#35-循环安全边界)
  - [4. 选择机制（Selection）](#4-选择机制selection)
    - [4.1 选择策略总览](#41-选择策略总览)
    - [4.2 规则选择（RuleBased）](#42-规则选择rulebased)
    - [4.3 LLM 选择（LLMBased）](#43-llm-选择llmbased)
    - [4.4 混合选择（Hybrid）](#44-混合选择hybrid)
    - [4.5 选择策略对比](#45-选择策略对比)
  - [5. 子工作流定义（SubWorkflowDefinition）](#5-子工作流定义subworkflowdefinition)
  - [6. 智能上下文（SmartContext）](#6-智能上下文smartcontext)
  - [7. 与 WorkFlowMethod 现有架构的集成](#7-与-workflowmethod-现有架构的集成)
    - [7.1 集成方式](#71-集成方式)
    - [7.2 新增组件](#72-新增组件)
  - [8. 使用示例](#8-使用示例)
    - [8.1 规则选择：数据处理管线](#81-规则选择数据处理管线)
    - [8.2 LLM 选择：AI 客服工作流](#82-llm-选择ai-客服工作流)
    - [8.3 混合选择：自动化测试工作流](#83-混合选择自动化测试工作流)
  - [9. 设计原则](#9-设计原则)
    - [9.1 单一职责](#91-单一职责)
    - [9.2 关注点分离](#92-关注点分离)
    - [9.3 可组合性](#93-可组合性)
    - [9.4 渐进增强](#94-渐进增强)
  - [10. 选择策略指南](#10-选择策略指南)
    - [如何选择策略？](#如何选择策略)
    - [策略迁移路径](#策略迁移路径)
    - [性能考量](#性能考量)
    - [最佳实践](#最佳实践)

---

## 1. 设计理念

### 核心思想

> **用循环工作流（Loop Workflow）作为"大脑"，用子分支工作流（Sub Workflow）作为"手脚"，由循环工作流根据当前上下文动态选择执行哪个子工作流。**

### 为什么选择这个方案？

| 方案 | 复杂度 | 复用性 | 可测试性 | 与现有架构兼容性 |
|------|--------|--------|---------|----------------|
| **SmartNode（智能节点）** | 高 - 需引入新节点类型 | 低 - 逻辑耦合 | 困难 | 低 - 需大量改造 |
| **循环+子工作流（本方案）** | **低** - 复用现有机制 | **高** - 子工作流独立复用 | **容易** - 独立测试 | **高** - 完美兼容 |

### 与 Cline 的类比

```
Cline:        LLM 主循环 → 选择工具 → 执行工具 → 更新上下文 → 继续循环
                                              ↓
本方案:    LoopWorkflow → 选择子工作流 → 执行子工作流 → 更新上下文 → 继续循环
```

---

## 2. 架构总览

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        循环工作流 (LoopWorkflow)                          │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    主循环 (Main Loop)                            │   │
│  │                                                                 │   │
│  │  ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐  │   │
│  │  │ ① 检查   │    │ ② 选择   │    │ ③ 执行   │    │ ④ 更新   │  │   │
│  │  │ 完成条件 │───→│ 子工作流 │───→│ 子工作流 │───→│ 上下文   │──┐  │   │
│  │  └──────────┘    └──────────┘    └──────────┘    └──────────┘  │  │   │
│  │       ▲                                                       │  │   │
│  │       └───────────────────────────────────────────────────────┘  │   │
│  │                        循环直到完成                                │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    选择策略 (SelectionStrategy)                   │   │
│  │                                                                 │   │
│  │  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐    │   │
│  │  │  RuleBased     │  │  LLMBased      │  │  Hybrid        │    │   │
│  │  │  规则匹配选择  │  │  LLM 推理选择  │  │  规则+LLM 兜底 │    │   │
│  │  │  确定性 100%   │  │  灵活但需监督  │  │  平衡可靠与灵活 │    │   │
│  │  └────────────────┘  └────────────────┘  └────────────────┘    │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└──────────────────────────┬──────────────────────────────────────────────┘
                           │
          ┌────────────────┼────────────────┬────────────────┐
          ▼                ▼                ▼                ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│  子工作流 A      │ │  子工作流 B      │ │  子工作流 C      │ │  子工作流 D      │
│  (数据收集)      │ │  (数据分析)      │ │  (结果生成)      │ │  (错误恢复)      │
│                 │ │                 │ │                 │ │                 │
│  ┌───┐ ┌───┐   │ │  ┌───┐ ┌───┐   │ │  ┌───┐ ┌───┐   │ │  ┌───┐ ┌───┐   │
│  │节点│→│节点│   │ │  │节点│→│节点│   │ │  │节点│→│节点│   │ │  │节点│→│节点│   │
│  └───┘ └───┘   │ │  └───┘ └───┘   │ │  └───┘ └───┘   │ │  └───┘ └───┘   │
│  ChainInfo      │ │  ChainInfo      │ │  ChainInfo      │ │  ChainInfo      │
└─────────────────┘ └─────────────────┘ └─────────────────┘ └─────────────────┘
```

---

## 3. 循环机制（Loop）

### 3.1 主循环流程

循环工作流的核心是一个 **4 步循环**，每一步都有明确的输入、处理和输出：

```
┌─────────────────────────────────────────────────────────────────────┐
│                    单轮迭代 (Single Iteration)                       │
│                                                                     │
│  输入: SmartContext (当前状态)                                       │
│                                                                     │
│  ┌──────────┐                                                      │
│  │ ① 检查   │  ── 是否满足完成条件？                                │
│  │ 完成条件 │  ├─ YES → 标记 Completed，退出循环                     │
│  └────┬─────┘  └─ NO  → 进入下一步                                  │
│       │                                                             │
│       ▼                                                             │
│  ┌──────────┐                                                      │
│  │ ② 选择   │  ── 根据策略选择下一个子工作流                         │
│  │ 子工作流 │  ├─ RuleBased: 匹配最高优先级规则                      │
│  └────┬─────┘  ├─ LLMBased: LLM 推理决策                            │
│       │        └─ Hybrid: 规则优先，无匹配则 LLM 兜底                │
│       ▼                                                             │
│  ┌──────────┐                                                      │
│  │ ③ 执行   │  ── 执行选中的子工作流（含重试逻辑）                   │
│  │ 子工作流 │  ├─ 成功 → 获取 Outputs                               │
│  └────┬─────┘  └─ 失败 → 根据 IsOptional 决定是否终止               │
│       │                                                             │
│       ▼                                                             │
│  ┌──────────┐                                                      │
│  │ ④ 更新   │  ── 将执行结果写回 SmartContext                       │
│  │ 上下文   │  ├─ 添加 CompletedSteps 记录                          │
│  └──────────┘  ├─ 合并 Outputs 到 _data                             │
│                └─ 添加 History 记录（滑动窗口管理）                   │
│                                                                     │
│  输出: 更新后的 SmartContext → 进入下一轮迭代                        │
└─────────────────────────────────────────────────────────────────────┘
```

**伪代码描述：**

```
function ExecuteLoop(workflow, context):
    result = new LoopWorkflowResult(Status=Running)
    
    for i = 0 to workflow.MaxIterations:
        // 步骤1: 检查完成条件
        if CheckCompletion(workflow, context):
            result.Status = Completed
            break
        
        // 步骤2: 选择子工作流
        selection = SelectSubWorkflow(workflow, context)
        
        // 如果选择 COMPLETE，结束循环
        if selection.IsComplete:
            result.Status = Completed
            RecordIteration(result, selection)
            break
        
        // 步骤3: 执行子工作流（含重试）
        subResult = ExecuteWithRetry(workflow, selection.Name, context)
        
        // 步骤4: 记录并更新上下文
        RecordIteration(result, selection, subResult)
        
        if subResult.Success:
            context.UpdateFromSubResult(subResult)
        else:
            HandleFailure(result, workflow, selection.Name)
            if result.Status == Failed:
                break
    
    if result.Status == Running:
        result.Status = MaxIterationsReached
    
    return result
```

### 3.2 循环工作流定义（LoopWorkflow）

```csharp
/// <summary>
/// 循环工作流 - 类似 Cline 的主循环，根据上下文动态选择执行子工作流
/// </summary>
public class LoopWorkflow
{
    /// <summary>工作流唯一标识</summary>
    public string WorkflowId { get; set; }
    
    /// <summary>任务目标（自然语言描述，用于 LLM 理解任务意图）</summary>
    public string Goal { get; set; }
    
    /// <summary>最大循环次数，防止无限循环</summary>
    public int MaxIterations { get; set; } = 50;
    
    /// <summary>完成条件描述（自然语言，用于 LLM 判断任务是否完成）</summary>
    public string CompletionCondition { get; set; }
    
    /// <summary>子工作流注册表 - 所有可选的子工作流</summary>
    public Dictionary<string, SubWorkflowDefinition> SubWorkflows { get; set; }
    
    /// <summary>选择策略 - 如何决定执行哪个子工作流</summary>
    public SelectionStrategy Strategy { get; set; }
    
    /// <summary>LLM 服务配置（LLMBased/Hybrid 模式下使用）</summary>
    public LLMServiceConfig LLMConfig { get; set; }
}
```

### 3.3 循环状态机

循环工作流在执行过程中经历以下状态转换：

```
                    ┌─────────────────────────────────────┐
                    │            NotStarted                │
                    └──────────────┬──────────────────────┘
                                   │ ExecuteAsync() 调用
                                   ▼
                    ┌─────────────────────────────────────┐
                    │             Running                  │
                    │  ┌───────────────────────────────┐  │
                    │  │  循环迭代中...                 │  │
                    │  │  ┌──→ ①检查 → ②选择 → ③执行  │  │
                    │  │  │       ④更新 → 继续         │  │
                    │  │  └─────────────────────────── │  │
                    │  └───────────────────────────────┘  │
                    └──────┬──────────┬──────────┬───────┘
                           │          │          │
                   完成条件  │  发生异常  │  达到最大  │
                   满足/    │  且无法   │  循环次数  │
                   LLM选择  │  恢复     │           │
                   COMPLETE │          │           │
                           ▼          ▼           ▼
              ┌────────────┐ ┌────────┐ ┌──────────────────┐
              │ Completed  │ │ Failed │ │MaxIterationsReached│
              └────────────┘ └────────┘ └──────────────────┘
                           │
                           ▼
              ┌─────────────────────┐
              │     Terminated      │  ← CancellationToken 取消
              └─────────────────────┘
```

```csharp
/// <summary>
/// 循环工作流执行状态
/// </summary>
public enum LoopStatus
{
    NotStarted,            // 尚未开始执行
    Running,               // 正在循环执行中
    Completed,             // 正常完成（满足完成条件或 LLM 选择 COMPLETE）
    Failed,                // 执行失败（非可选子工作流执行失败）
    Terminated,            // 被取消（CancellationToken）
    MaxIterationsReached   // 达到最大循环次数仍未完成
}
```

### 3.4 完成条件检查

完成条件检查是循环的**第一道关卡**，在每一轮迭代开始时执行。支持两种模式：

**模式一：LLM 判断（推荐）**

当设置了 `CompletionCondition`（自然语言描述）时，由 LLM 根据当前上下文判断任务是否完成：

```
Prompt 模板:
─────────────────────────────────────────────
## 任务目标
{workflow.Goal}

## 完成条件
{workflow.CompletionCondition}

## 当前状态
{context.ToLLMContext()}

请判断任务是否已经完成。如果已完成，请返回 YES，否则返回 NO。
─────────────────────────────────────────────
```

**模式二：LLM 选择 COMPLETE**

当 `SelectionStrategy.AllowLLMToComplete = true` 时，LLM 在选择步骤中可以直接返回 "COMPLETE" 来结束循环。

**两种模式的关系：**
- 模式一（完成条件检查）在**选择之前**执行，用于自动检测任务完成
- 模式二（LLM 选择 COMPLETE）在**选择步骤中**执行，由 LLM 主动判断
- 两者可以同时启用，互为补充

### 3.5 循环安全边界

为防止循环失控，设计了多层安全边界：

| 安全机制 | 说明 | 默认值 |
|---------|------|--------|
| **MaxIterations** | 最大循环次数，硬性上限 | 50 |
| **CancellationToken** | 支持外部取消操作 | - |
| **子工作流 MaxRetries** | 单个子工作流最大重试次数 | 0 |
| **子工作流 IsOptional** | 标记可选子工作流，失败不影响主流程 | false |
| **子工作流 Timeout** | 单个子工作流执行超时 | null（不限） |

**错误处理策略：**

```
子工作流执行失败
    │
    ├── IsOptional = true  → 记录错误，继续循环
    │
    └── IsOptional = false → 标记 Failed，终止循环
         │
         ├── MaxRetries > 0 → 先重试，重试耗尽仍失败则终止
         │
         └── MaxRetries = 0 → 直接终止
```

---

## 4. 选择机制（Selection）

### 4.1 选择策略总览

选择机制是循环工作流的**核心决策单元**，决定了"下一步该做什么"。支持三种策略：

```
                    ┌─────────────────────────────┐
                    │    选择策略 SelectionStrategy │
                    │                             │
                    │  ┌───────────────────────┐  │
                    │  │  决策入口              │  │
                    │  │  SelectSubWorkflow()  │  │
                    │  └───────┬───────────────┘  │
                    │          │                   │
                    │          ▼                   │
                    │  ┌───────────────────────┐  │
                    │  │  策略分发              │  │
                    │  │  switch(Type)         │  │
                    │  └──┬───────┬───────┬───┘  │
                    │     │       │       │       │
                    │     ▼       ▼       ▼       │
                    │  ┌────┐ ┌────┐ ┌──────┐    │
                    │  │规则│ │LLM │ │混合  │    │
                    │  └────┘ └────┘ └──────┘    │
                    └─────────────────────────────┘
```

```csharp
/// <summary>
/// 选择策略类型
/// </summary>
public enum SelectionStrategyType
{
    /// <summary>规则选择 - 基于预定义条件表达式</summary>
    RuleBased,
    
    /// <summary>LLM 选择 - 基于 LLM 推理决策</summary>
    LLMBased,
    
    /// <summary>混合选择 - 规则优先，规则不匹配时使用 LLM 兜底</summary>
    Hybrid
}

/// <summary>
/// 选择策略 - 决定循环工作流如何选择下一个要执行的子工作流
/// </summary>
public class SelectionStrategy
{
    /// <summary>策略类型</summary>
    public SelectionStrategyType Type { get; set; }
    
    /// <summary>规则列表（RuleBased/Hybrid 模式下使用）</summary>
    public List<SelectionRule> Rules { get; set; } = new();
    
    /// <summary>规则不匹配时是否回退到 LLM（Hybrid 模式下使用）</summary>
    public bool FallbackToLLM { get; set; } = true;
    
    /// <summary>是否允许 LLM 选择 "COMPLETE"（标记任务完成）</summary>
    public bool AllowLLMToComplete { get; set; } = true;
}
```

### 4.2 规则选择（RuleBased）

**适用场景：** 执行顺序确定、条件明确的流程。

**决策流程：**

```
输入: SmartContext
    │
    ▼
遍历所有规则，按优先级排序
    │
    ▼
对每条规则评估条件表达式
    │
    ├── 匹配 → 返回该规则指定的子工作流
    │
    └── 不匹配 → 继续下一条规则
    │
    ▼
所有规则都不匹配 → 返回 null（标记任务完成）
```

**规则定义：**

```csharp
/// <summary>
/// 选择规则 - 优先级数字越大越优先匹配
/// </summary>
public class SelectionRule
{
    /// <summary>规则名称</summary>
    public string Name { get; set; }
    
    /// <summary>条件表达式（如 "context.RawData == null"）</summary>
    public string Condition { get; set; }
    
    /// <summary>目标子工作流名称</summary>
    public string TargetSubWorkflow { get; set; }
    
    /// <summary>优先级（数字越大越优先）</summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>规则描述（用于日志和调试）</summary>
    public string Description { get; set; }
}
```

**条件表达式语法：**

当前支持的条件表达式格式：

| 表达式 | 含义 | 示例 |
|--------|------|------|
| `context.Key == null` | 检查 Key 不存在 | `context.RawData == null` |
| `context.Key != null` | 检查 Key 存在 | `context.CleanedData != null` |

**规则匹配算法：**

```
function SelectByRule(workflow, context):
    // 1. 筛选出所有条件满足的规则
    matchedRules = workflow.Rules.Where(rule => 
        EvaluateCondition(rule.Condition, context))
    
    // 2. 按优先级降序排序
    sortedRules = matchedRules.OrderByDescending(rule => rule.Priority)
    
    // 3. 取最高优先级的规则
    bestRule = sortedRules.FirstOrDefault()
    
    if bestRule == null:
        return null  // 无匹配规则
    
    return SubWorkflowSelection {
        Name = bestRule.TargetSubWorkflow,
        Reasoning = $"规则匹配: {bestRule.Description}",
        IsLLMDecision = false
    }
```

### 4.3 LLM 选择（LLMBased）

**适用场景：** 需要根据复杂上下文动态决策的流程。

**决策流程：**

```
输入: SmartContext
    │
    ▼
构建 LLM Prompt（包含任务目标、当前状态、可选子工作流）
    │
    ▼
调用 LLM 获取决策
    │
    ▼
解析 LLM 响应
    │
    ├── "COMPLETE" → 标记任务完成，结束循环
    │
    └── 子工作流名称 → 解析并返回对应的子工作流
```

**LLM Prompt 设计：**

```
## 任务目标
{workflow.Goal}

## 当前状态
{context.ToLLMContext()}

## 已完成步骤
- 步骤1: 收集数据 ✅
- 步骤2: 清洗数据 ✅

## 可选子工作流
- 转换数据: 数据格式转换、字段映射、聚合计算
- 分析数据: 统计分析、趋势分析、异常检测
- 生成报告: 生成可视化图表和报告文档
- 质量检查: 检查数据质量和报告完整性
- 错误恢复: 处理执行过程中的错误，尝试恢复

## 决策要求
请根据当前状态，选择下一步应该执行的子工作流。
如果任务已经完成，请返回 COMPLETE。
如果某个子工作流执行出错需要重试，请返回该子工作流的名称。

请直接返回子工作流名称或 COMPLETE，不要包含其他内容。
```

**LLM 响应解析：**

```
function ParseLLMResponse(response, validNames):
    // 1. 精确匹配
    for name in validNames:
        if response == name (忽略大小写):
            return name
    
    // 2. 模糊匹配（包含关系）
    for name in validNames:
        if response contains name (忽略大小写):
            return name
    
    // 3. 兜底：返回第一个子工作流
    return validNames.First()
```

### 4.4 混合选择（Hybrid）

**适用场景：** 有明确规则但需要 LLM 处理异常情况的流程。

**决策流程：**

```
输入: SmartContext
    │
    ▼
┌─────────────────────────────────────────────┐
│  第一步：规则匹配                             │
│  ┌───────────────────────────────────────┐  │
│  │ 遍历规则列表，按优先级匹配条件          │  │
│  └───────────────────────────────────────┘  │
│         │                                   │
│         ├── 匹配成功 → 返回规则选择的子工作流 │
│         │                                   │
│         └── 无匹配规则                       │
│              │                               │
│              ▼                               │
│  ┌───────────────────────────────────────┐  │
│  │  第二步：检查 FallbackToLLM           │  │
│  └───────────────────────────────────────┘  │
│         │                                   │
│         ├── false → 标记任务完成，结束循环   │
│         │                                   │
│         └── true                            │
│              │                               │
│              ▼                               │
│  ┌───────────────────────────────────────┐  │
│  │  第三步：LLM 兜底决策                  │  │
│  │  调用 LLM 选择下一个子工作流           │  │
│  └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
```

### 4.5 选择策略对比

| 维度 | RuleBased | LLMBased | Hybrid |
|------|-----------|----------|--------|
| **决策方式** | 预定义条件匹配 | LLM 实时推理 | 规则优先 + LLM 兜底 |
| **确定性** | 100% 确定 | 不确定（依赖 LLM） | 关键路径确定，异常灵活 |
| **灵活性** | 低 - 只能执行预设规则 | 高 - 可处理未预见情况 | 中 - 规则覆盖大部分场景 |
| **性能** | 高 - 毫秒级 | 低 - 秒级（LLM 调用延迟） | 中 - 大部分情况毫秒级 |
| **可调试性** | 高 - 规则明确 | 中 - 需查看 LLM 推理 | 高 - 规则部分明确 |
| **适用场景** | 标准审批、定时任务、简单 ETL | AI 客服、智能文档处理、代码分析 | 自动化测试、数据处理、运维自动化 |
| **LLM 依赖** | 无 | 强依赖 | 弱依赖（仅异常时使用） |

---

## 5. 子工作流定义（SubWorkflowDefinition）

子工作流是循环工作流的"手脚"，对应 Cline 中的工具调用。每个子工作流包装一个现有的 ChainInfo 管线。

```csharp
/// <summary>
/// 子工作流定义 - 对应 Cline 中的工具调用
/// </summary>
public class SubWorkflowDefinition
{
    /// <summary>子工作流名称</summary>
    public string Name { get; set; }
    
    /// <summary>自然语言描述 - 用于 LLM 理解该子工作流的用途</summary>
    public string Description { get; set; }
    
    /// <summary>工作流配置（ChainInfo JSON 格式）</summary>
    public string ChainJsonConfig { get; set; }
    
    /// <summary>需要的上下文键列表</summary>
    public List<string> RequiredContextKeys { get; set; } = new();
    
    /// <summary>输出的上下文键列表</summary>
    public List<string> OutputContextKeys { get; set; } = new();
    
    /// <summary>是否可选执行（失败不影响主流程）</summary>
    public bool IsOptional { get; set; } = false;
    
    /// <summary>最大重试次数</summary>
    public int MaxRetries { get; set; } = 0;
    
    /// <summary>执行超时时间</summary>
    public TimeSpan? Timeout { get; set; }
}
```

**子工作流与循环工作流的关系：**

```
循环工作流 (LoopWorkflow)
    │
    ├── 子工作流 A (SubWorkflowDefinition)
    │   ├── Name: "收集数据"
    │   ├── Description: "从API、数据库、文件等数据源收集原始数据"
    │   ├── ChainJsonConfig: [...]  ← 复用现有 ChainInfo JSON 格式
    │   ├── RequiredContextKeys: ["DataSource"]
    │   ├── OutputContextKeys: ["RawData"]
    │   ├── IsOptional: false
    │   └── MaxRetries: 2
    │
    ├── 子工作流 B (SubWorkflowDefinition)
    │   ├── Name: "清洗数据"
    │   ├── Description: "处理缺失值、异常值、重复数据"
    │   ├── ChainJsonConfig: [...]  ← 复用现有 ChainInfo JSON 格式
    │   ├── RequiredContextKeys: ["RawData"]
    │   ├── OutputContextKeys: ["CleanedData"]
    │   ├── IsOptional: false
    │   └── MaxRetries: 0
    │
    └── ...
```

---

## 6. 智能上下文（SmartContext）

智能上下文是循环工作流的"记忆体"，维护所有执行历史和数据，并为 LLM 提供结构化的上下文摘要。

```csharp
/// <summary>
/// 智能上下文 - 为 LLM 提供结构化的上下文信息
/// </summary>
public class SmartContext
{
    /// <summary>任务目标</summary>
    public string Goal { get; set; }
    
    /// <summary>上下文数据存储</summary>
    private readonly Dictionary<string, object> _data = new();
    
    /// <summary>已完成步骤列表</summary>
    public List<string> CompletedSteps { get; } = new();
    
    /// <summary>执行历史记录</summary>
    public List<ExecutionRecord> History { get; } = new();
    
    /// <summary>最大历史记录数（滑动窗口）</summary>
    public int MaxHistoryLength { get; set; } = 100;
    
    // 数据访问方法
    public T Get<T>(string key) { ... }
    public void Set(string key, object value) { ... }
    public bool ContainsKey(string key) { ... }
    
    /// <summary>从子工作流执行结果更新上下文</summary>
    public void UpdateFromSubResult(SubWorkflowResult result)
    {
        CompletedSteps.Add(result.Name);
        
        foreach (var kvp in result.Outputs)
            _data[kvp.Key] = kvp.Value;
        
        History.Add(new ExecutionRecord { ... });
        
        // 滑动窗口管理
        while (History.Count > MaxHistoryLength)
            History.RemoveAt(0);
    }
    
    /// <summary>构建 LLM 可理解的上下文摘要</summary>
    public string ToLLMContext()
    {
        // 输出格式：
        // ### 当前状态
        // - 已执行步骤数: 5
        // - 成功: 4
        // - 失败: 1
        //
        // ### 最近执行记录
        // - [12:30:01] ✅ 收集数据 (1500ms)
        // - [12:30:05] ✅ 清洗数据 (3200ms)
        //
        // ### 关键数据
        // - RawData: {...}
        // - CleanedData: {...}
    }
    
    /// <summary>转换为工作流参数字典</summary>
    public Dictionary<string, object> ToParameters()
    {
        return new Dictionary<string, object>(_data);
    }
}
```

**SmartContext 的数据流：**

```
                    ┌─────────────────────────┐
                    │      SmartContext        │
                    │                         │
                    │  ┌───────────────────┐  │
                    │  │  _data            │  │
                    │  │  (键值存储)        │  │
                    │  │  RawData → {...}  │  │
                    │  │  CleanedData→{...}│  │
                    │  └───────────────────┘  │
                    │                         │
                    │  ┌───────────────────┐  │
                    │  │  History          │  │
                    │  │  (执行历史记录)    │  │
                    │  │  [0] 收集数据 ✅  │  │
                    │  │  [1] 清洗数据 ✅  │  │
                    │  └───────────────────┘  │
                    │                         │
                    │  ┌───────────────────┐  │
                    │  │  CompletedSteps   │  │
                    │  │  (已完成步骤列表)  │  │
                    │  │  "收集数据"       │  │
                    │  │  "清洗数据"       │  │
                    │  └───────────────────┘  │
                    └─────────────────────────┘
                           ▲          │
                           │          ▼
                    ┌──────┴──────────┴──────┐
                    │  子工作流执行结果        │
                    │  SubWorkflowResult     │
                    │  - Outputs             │
                    │  - Success             │
                    │  - Duration            │
                    └────────────────────────┘
```

---

## 7. 与 WorkFlowMethod 现有架构的集成

### 7.1 集成方式

循环工作流设计完全复用 WorkFlowMethod 的现有架构：

| 现有组件 | 在循环工作流中的角色 |
|---------|-------------------|
| **ChainInfo** | 子工作流的定义格式（JSON 配置） |
| **CoreGraph** | 子工作流内部的有向图表示 |
| **GraphFoundationBridge** | 子工作流的链⇄图转换 |
| **PipelineExecutor** | 子工作流的执行管线 |
| **WorkflowProcessor** | 子工作流的核心执行引擎 |
| **FunctionExecutionFramework** | 子工作流中节点的执行框架 |

### 7.2 新增组件

| 组件 | 说明 | 依赖现有组件 |
|------|------|------------|
| **LoopWorkflow** | 循环工作流定义 | 无 |
| **LoopWorkflowExecutor** | 循环工作流执行器 | WorkflowProcessor |
| **SmartContext** | 智能上下文 | 无 |
| **SelectionStrategy** | 选择策略 | 无 |
| **ILLMService** | LLM 服务接口 | 无 |

---

## 8. 使用示例

### 8.1 规则选择：数据处理管线

展示 **RuleBased** 策略的典型用法——有明确阶段顺序的数据处理流程：

```csharp
// 定义数据处理管线的循环工作流
var dataPipeline = new LoopWorkflow
{
    WorkflowId = "data_pipeline_001",
    Goal = "从多个数据源收集数据，进行清洗、转换和分析，最终生成可视化报告",
    MaxIterations = 50,
    SubWorkflows = new Dictionary<string, SubWorkflowDefinition>
    {
        ["收集数据"] = new() { Description = "从API、数据库、文件等数据源收集原始数据" },
        ["清洗数据"] = new() { Description = "处理缺失值、异常值、重复数据" },
        ["转换数据"] = new() { Description = "数据格式转换、字段映射、聚合计算" },
        ["分析数据"] = new() { Description = "统计分析、趋势分析、异常检测" },
        ["生成报告"] = new() { Description = "生成可视化图表和报告文档" },
        ["质量检查"] = new() { Description = "检查数据质量和报告完整性" },
        ["错误恢复"] = new() { Description = "处理执行过程中的错误，尝试恢复", IsOptional = true, MaxRetries = 3 }
    },
    Strategy = new SelectionStrategy
    {
        Type = SelectionStrategyType.RuleBased,
        Rules = new List<SelectionRule>
        {
            new() { Name = "需要收集数据", Condition = "context.RawData == null", TargetSubWorkflow = "收集数据", Priority = 10, Description = "原始数据不存在，先收集数据" },
            new() { Name = "需要清洗数据", Condition = "context.CleanedData == null", TargetSubWorkflow = "清洗数据", Priority = 9, Description = "原始数据已存在但未清洗" },
            new() { Name = "需要转换数据", Condition = "context.TransformedData == null", TargetSubWorkflow = "转换数据", Priority = 8, Description = "数据已清洗但未转换" },
            new() { Name = "需要分析数据", Condition = "context.AnalysisResult == null", TargetSubWorkflow = "分析数据", Priority = 7, Description = "数据已转换但未分析" },
            new() { Name = "需要生成报告", Condition = "context.Report == null", TargetSubWorkflow = "生成报告", Priority = 6, Description = "数据已分析但未生成报告" },
            new() { Name = "需要质量检查", Condition = "context.QualityPassed == null", TargetSubWorkflow = "质量检查", Priority = 5, Description = "报告已生成但未做质量检查" },
            new() { Name = "需要错误恢复", Condition = "context.HasError == true", TargetSubWorkflow = "错误恢复", Priority = 100, Description = "发生错误，尝试恢复" }
        }
    }
};

// 执行循环工作流
var executor = new LoopWorkflowExecutor();
var context = new SmartContext { Goal = dataPipeline.Goal };
context.Set("DataSource", new[] { "API_Orders", "DB_Products", "File_Inventory" });

var result = await executor.ExecuteAsync(dataPipeline, context, cancellationToken);

Console.WriteLine($"状态: {result.Status}");
Console.WriteLine($"总迭代次数: {result.Iterations.Count}");
Console.WriteLine($"执行步骤: {string.Join(" → ", result.Iterations.Select(i => i.SubWorkflowName))}");
// 输出: 状态: Completed
//       总迭代次数: 6
//       执行步骤: 收集数据 → 清洗数据 → 转换数据 → 分析数据 → 生成报告 → 质量检查
```

### 8.2 LLM 选择：AI 客服工作流

展示 **LLMBased** 策略的典型用法——需要根据用户意图动态决策的智能客服系统：

```csharp
// 定义 AI 客服的循环工作流
var aiCustomerService = new LoopWorkflow
{
    WorkflowId = "ai_cs_001",
    Goal = "理解用户问题，检索相关知识库，生成个性化回复，解决用户问题",
    CompletionCondition = "用户问题已得到满意解决，或已确认无法解决并给出替代方案",
    MaxIterations = 20,
    SubWorkflows = new Dictionary<string, SubWorkflowDefinition>
    {
        ["意图识别"] = new() { Description = "分析用户输入，识别用户意图（咨询、投诉、退换货等）" },
        ["知识库检索"] = new() { Description = "根据意图和关键词检索知识库，获取相关答案" },
        ["订单查询"] = new() { Description = "查询用户订单状态、物流信息等" },
        ["生成回复"] = new() { Description = "根据检索结果和上下文生成个性化回复" },
        ["情感分析"] = new() { Description = "分析用户情绪，判断是否需要升级人工客服" },
        ["升级人工"] = new() { Description = "将对话转接给人工客服处理" }
    },
    Strategy = new SelectionStrategy
    {
        Type = SelectionStrategyType.LLMBased,
        AllowLLMToComplete = true
    },
    LLMConfig = new LLMServiceConfig
    {
        Provider = "OpenAI",
        Model = "gpt-4",
        Temperature = 0.3
    }
};

// 执行循环工作流
var executor = new LoopWorkflowExecutor();
var context = new SmartContext { Goal = aiCustomerService.Goal };
context.Set("UserInput", "我的订单怎么还没到？已经过了预计送达时间了");
context.Set("UserHistory", new[] { "之前咨询过发货时间" });

var result = await executor.ExecuteAsync(aiCustomerService, context, cancellationToken);

Console.WriteLine($"状态: {result.Status}");
Console.WriteLine($"总迭代次数: {result.Iterations.Count}");
Console.WriteLine($"执行步骤: {string.Join(" → ", result.Iterations.Select(i => i.SubWorkflowName))}");
// 可能输出: 状态: Completed
//          总迭代次数: 4
//          执行步骤: 意图识别 → 订单查询 → 知识库检索 → 生成回复
```

### 8.3 混合选择：自动化测试工作流

展示 **Hybrid** 策略的典型用法——有标准流程但需要 LLM 处理异常情况的自动化测试系统：

```csharp
// 定义自动化测试的循环工作流
var autoTestPipeline = new LoopWorkflow
{
    WorkflowId = "auto_test_001",
    Goal = "对目标系统进行全面的自动化测试，包括单元测试、集成测试、性能测试，并生成测试报告",
    MaxIterations = 30,
    SubWorkflows = new Dictionary<string, SubWorkflowDefinition>
    {
        ["单元测试"] = new() { Description = "执行单元测试用例，验证各模块功能" },
        ["集成测试"] = new() { Description = "执行集成测试用例，验证模块间交互" },
        ["性能测试"] = new() { Description = "执行性能测试，评估系统响应时间和吞吐量" },
        ["安全扫描"] = new() { Description = "执行安全漏洞扫描" },
        ["生成报告"] = new() { Description = "汇总所有测试结果，生成测试报告" },
        ["错误分析"] = new() { Description = "分析测试失败原因，判断是否需要重试或跳过", IsOptional = true },
        ["环境修复"] = new() { Description = "修复测试环境问题（如重启服务、清理数据）", IsOptional = true, MaxRetries = 2 }
    },
    Strategy = new SelectionStrategy
    {
        Type = SelectionStrategyType.Hybrid,
        Rules = new List<SelectionRule>
        {
            new() { Name = "需要单元测试", Condition = "context.UnitTestDone == null", TargetSubWorkflow = "单元测试", Priority = 10 },
            new() { Name = "需要集成测试", Condition = "context.IntegrationTestDone == null", TargetSubWorkflow = "集成测试", Priority = 9 },
            new() { Name = "需要性能测试", Condition = "context.PerfTestDone == null", TargetSubWorkflow = "性能测试", Priority = 8 },
            new() { Name = "需要安全扫描", Condition = "context.SecurityScanDone == null", TargetSubWorkflow = "安全扫描", Priority = 7 },
            new() { Name = "需要生成报告", Condition = "context.ReportDone == null", TargetSubWorkflow = "生成报告", Priority = 6 },
            new() { Name = "需要错误分析", Condition = "context.HasTestFailure == true", TargetSubWorkflow = "错误分析", Priority = 100 },
            new() { Name = "需要环境修复", Condition = "context.EnvError == true", TargetSubWorkflow = "环境修复", Priority = 99 }
        },
        FallbackToLLM = true,
        AllowLLMToComplete = true
    },
    LLMConfig = new LLMServiceConfig
    {
        Provider = "OpenAI",
        Model = "gpt-4",
        Temperature = 0.2
    }
};

// 执行循环工作流
var executor = new LoopWorkflowExecutor();
var context = new SmartContext { Goal = autoTestPipeline.Goal };
context.Set("TargetSystem", "OrderService");

var result = await executor.ExecuteAsync(autoTestPipeline, context, cancellationToken);

Console.WriteLine($"状态: {result.Status}");
Console.WriteLine($"总迭代次数: {result.Iterations.Count}");
Console.WriteLine($"执行步骤: {string.Join(" → ", result.Iterations.Select(i => i.SubWorkflowName))}");
// 可能输出: 状态: Completed
//          总迭代次数: 7
//          执行步骤: 单元测试 → 集成测试 → 错误分析 → 环境修复 → 集成测试 → 性能测试 → 生成报告
//          （集成测试失败后，LLM 选择了错误分析，然后环境修复，再重试集成测试）
```

---

## 9. 设计原则

### 9.1 单一职责

- **循环工作流**只负责"循环调度"——检查完成条件、选择子工作流、更新上下文
- **子工作流**只负责"执行具体任务"——包装现有的 ChainInfo 管线
- **选择策略**只负责"决策"——根据规则或 LLM 决定下一步

### 9.2 关注点分离

```
┌─────────────────────────────────────────────────────────────┐
│  关注点分离示意图                                            │
│                                                             │
│  LoopWorkflow       → 循环控制（何时开始、何时结束）          │
│  SelectionStrategy  → 决策逻辑（下一步做什么）               │
│  SubWorkflow        → 执行逻辑（具体怎么做）                 │
│  SmartContext       → 状态管理（当前在哪、有什么数据）        │
└─────────────────────────────────────────────────────────────┘
```

### 9.3 可组合性

- 子工作流可以独立定义、独立测试、独立复用
- 同一个子工作流可以被不同的循环工作流引用
- 选择策略可以独立替换，不影响子工作流的实现

### 9.4 渐进增强

- 从 **RuleBased** 开始（确定性高、易于调试）
- 需要灵活性时升级到 **Hybrid**（规则覆盖大部分场景）
- 需要完全动态决策时升级到 **LLMBased**（最大灵活性）

---

## 10. 选择策略指南

### 如何选择策略？

```
你的流程是否满足以下条件？
    │
    ├── 执行顺序完全确定，条件明确
    │   └── → 使用 RuleBased
    │
    ├── 有明确的阶段划分，但可能出现异常情况
    │   └── → 使用 Hybrid
    │
    ├── 需要根据复杂上下文动态决策
    │   └── → 使用 LLMBased
    │
    └── 不确定 → 从 RuleBased 开始，逐步升级
```

### 策略迁移路径

```
RuleBased ──→ Hybrid ──→ LLMBased
    │            │            │
    │ 规则明确    │ 规则+LLM   │ 完全动态
    │ 确定性高    │ 平衡方案   │ 最大灵活
    │ 无 LLM 成本 │ 低成本     │ 高 LLM 成本
```

### 性能考量

| 策略 | 每次选择延迟 | 适用迭代次数 | 推荐场景 |
|------|------------|------------|---------|
| **RuleBased** | < 1ms | 任意 | 标准流程、定时任务 |
| **Hybrid** | < 1ms（规则匹配）/ 1-3s（LLM 兜底） | < 100 | 大部分场景 |
| **LLMBased** | 1-3s | < 50 | 复杂决策场景 |

### 最佳实践

1. **优先使用 RuleBased**：除非流程确实需要动态决策，否则规则选择更可靠
2. **Hybrid 的 FallbackToLLM 谨慎开启**：仅在规则无法覆盖所有场景时启用
3. **LLM 选择时设置较低 Temperature**：推荐 0.2-0.3，保证决策稳定性
4. **合理设置 MaxIterations**：RuleBased 可设较大值（50-100），LLMBased 建议较小值（20-50）
5. **子工作流命名清晰**：名称和描述要能让 LLM 准确理解其用途
6. **SmartContext 的滑动窗口**：避免历史记录过多导致 LLM 上下文超长
7. **错误恢复子工作流**：建议为每个循环工作流配置一个可选的错误恢复子工作流
