# Workflow 与 LangGraph 深度对比：执行语义级差异与具体集成方案

> **分析日期**：2026-08-09（第二轮深度分析）
> **分析方法**：逐文件精读源码 + LangGraph Pregel 内部机制研究
> **前置文档**：`function-call-path-analysis.md`、`langgraph-integration-analysis.md`
> **本文定位**：前一轮报告停留在"概念映射"层面；本文从**执行语义、状态模型、控制流结构**三个维度做代码级精确对比，识别 7 个不可忽略的摩擦点，并给出逐项具体解决方案。

---

## 目录

1. [核心差异总结](#一核心差异总结)
2. [7 个摩擦点逐一分析](#二7-个摩擦点逐一分析)
3. [修订后的 StateGraph 精确设计](#三修订后的-stategraph-精确设计)
4. [C# 侧最小改造清单](#四c-侧最小改造清单)
5. [跨语言数据契约设计](#五跨语言数据契约设计)
6. [迁移优先级矩阵](#六迁移优先级矩阵)

---

## 一、核心差异总结

前一轮报告说"高度同构"——这个判断在**拓扑结构**层面成立（while 循环→环、管道→节点链、Route→条件边），但在**执行语义**层面存在根本差异：

| 维度 | 当前 workflow | LangGraph Pregel | 差异性质 |
|---|---|---|---|
| **状态模型** | 可变就地状态（`AxiomContext.Budget.Escalate()` 原地改写） | 不可变 partial update + reducer 合并 | **范式级**，非映射可解 |
| **循环结构** | 显式 `while(true)` + `break`，用户代码控制 | Pregel 引擎隐式超步循环，图拓扑即循环 | 结构级，需要适配 |
| **跨轮传递** | `RoundStateMapper.Seed/Harvest` 手动双向拷贝 | 节点直接返回 partial state，自动合并 | 模式级，Seed/Harvest 可消除 |
| **路由层级** | 两级：管道内 RuleEngine + 循环级 Route | 单级：`add_conditional_edges` 一个函数 | 架构级，需扁平化 |
| **边界框架状态** | `DefaultBoundaryFramework` 实例字段跨轮持久 | 节点纯函数无状态，跨轮数据必须入 state | 实现级，需状态外提 |
| **函数调用** | 反射注册表 `CallFunctionAsync(funcName, ctx)` | 直接 Python 函数引用 | 机制级，gRPC 桥接解决 |
| **条件步骤** | `StaticAsyncPipeline` 的 `CanExecute` 谓词 | 条件边跳过节点 | 模式级，条件边替代 |
| **预算/熔断** | `FailureBudget`/`ProgressToken`/`ControlFlag` 可变对象 | state 字段 + 自定义 reducer | 需要将对象方法转为纯函数 |

**核心洞察**：前一轮报告说"零修改集成"——这在路径 B（子图嵌入，整轮一次 gRPC 调用）成立。但路径 A（包装器模式，5 个节点分别 gRPC 调用）**必须解决上述 7 个摩擦点**，因为每个节点暴露为独立 gRPC 服务后，不再共享同一个 `AxiomContext` 可变对象实例。

---

## 二、7 个摩擦点逐一分析

### 摩擦点 1：可变就地状态 vs 不可变 reducer 合并

**当前代码**（`AxiomContext.cs`）：

```csharp
public sealed class FailureBudget {
    public int Level { get; private set; }
    public void Escalate() => Level = Math.Min(Level + 1, MaxLevel);  // 原地改写
    public void Deescalate() => Level = Math.Max(Level - 1, MinLevel);
    public void Trip() => Level = MaxLevel;
}
```

`BudgetEscalator.Apply(ctx, errCls, eval)` 直接调用 `ctx.Budget.Escalate()` 改写 `AxiomContext` 的内部状态。`ProgressToken.Advance()` 和 `ControlFlag.SetHalt()` 同理。

**LangGraph 语义**：节点返回 `{"budget_level": 2}`，Pregel 引擎用 LastValue reducer（直接替换）合并到 state。没有 `Escalate()` 方法——节点函数自己计算新值：

```python
def escalate_node(state: AgentState) -> dict:
    return {"budget_level": min(state["budget_level"] + 1, 3)}
```

**解决方案**：把 `FailureBudget`/`ProgressToken`/`ControlFlag` 的方法逻辑**翻译为纯函数**，放入条件边或独立"熔断评估"节点：

```python
def budget_escalate_node(state: AgentState) -> dict:
    """对应 BudgetEscalator.Apply — 纯函数计算新 budget/halt"""
    eval_outcome = state["eval_outcome"]
    error_class = state["error_class"]
    budget = state["budget_level"]
    
    # BudgetEscalator 逻辑翻译
    if error_class is not None:
        budget = min(budget + 1, 3)  # Escalate
    elif eval_outcome == "pass":
        budget = max(budget - 1, 0)  # Deescalate
    
    # ControlFlag 逻辑翻译
    halt = budget >= 3  # IsTripped → SetHalt
    
    # ProgressToken 逻辑翻译
    progress = state["progress"]
    if eval_outcome in ("fail", "parse_error"):
        progress = min(progress + 1, state["progress_max"])
    elif eval_outcome == "pass":
        progress = max(progress - 1, 0)
    
    return {"budget_level": budget, "halt": halt, "progress": progress}
```

**保留点**：C# 侧的 `AxiomContext`/`FailureBudget`/`ProgressToken` 不需要修改——它们仍在 C# 进程内被路径 B 的单轮调用使用。只有当迁移到路径 A（多节点独立 gRPC）时，上述纯函数翻译才需要。

### 摩擦点 2：Seed/Harvest 双向拷贝 vs 单一状态

**当前代码**（`RoundStateMapper.cs`）：

```csharp
// Seed: AxiomContext.Data → RoundPipelineContext.Vars
public static void Seed(RoundPipelineContext ctx, AxiomContext state, ...) {
    foreach (var kv in lease is not null ? SeedFromLease(lease) : Seed(state))
        ctx.Vars[kv.Key] = kv.Value;
}

// Harvest: RoundPipelineContext.Vars → AxiomContext.Data
public static void Harvest(AxiomContext state, Dictionary<string, object?> vars) {
    Set(RoundKeys.ExecResult);      // 回写 exec_result
    Set(RoundKeys.NextPromptHint);  // 回写 next_hint
    Set(RoundKeys.StepCount);
    // ... 10 个键
}
```

这个双向拷贝存在的原因是：`RoundPipelineContext.Vars` 是管道步骤间共享的可变字典，`AxiomContext.Data` 是跨轮持久化的可变字典。它们是**两个独立的可变容器**，需要手动同步。

**LangGraph 语义**：只有一个 state 对象。节点 A 写 `state["exec_result"]`，节点 B 直接从 `state["exec_result"]` 读。Pregel 引擎在每个 super-step 结束时自动通过 reducer 合并并持久化。**Seed/Harvest 完全不需要**。

**解决方案**：在 LangGraph 中消除 Seed/Harvest——直接将 `RoundKeys` 中所有键映射为 `AgentState` 的字段：

```python
class AgentState(TypedDict):
    # RoundKeys.Input / UserInput / FilteredInput
    user_input: str
    filtered_input: Optional[str]
    # RoundKeys.Goal / Constraints / PlanSummary
    goal: str
    constraints: str
    plan_summary: str
    # RoundKeys.ExecResult / NextPromptHint
    exec_result: Optional[dict]  # ClineResult 序列化
    next_prompt_hint: Optional[str]
    # RoundKeys.Prompt / System / L1 / L2 / Replanned
    prompt: Optional[str]
    system: Optional[str]
    l1: Optional[str]
    l2: Optional[str]
    replanned: Optional[bool]
    # RoundKeys.Decision / StepCount / Reason / Rule
    decision: Optional[str]
    step_count: int
    reason: Optional[str]
    rule: Optional[str]
    # RoundKeys.ReturnSpec (HFSM 结构化信号)
    return_spec: Optional[dict]  # ChildAgentReturn 序列化
    # RoundKeys.BoundaryAction
    boundary_action: Optional[str]
    # RoundKeys.SessionId (Cline 会话连续性)
    session_id: Optional[str]
    # RoundKeys.Output / SafeOutput
    output: Optional[str]
    safe_output: Optional[str]
    # 边界状态 (原 AxiomContext.Data 中的跨轮键)
    cwd: Optional[str]
    data_dir: Optional[str]
    boundary_state: Optional[dict]
    reconstruct_hint: Optional[str]
    # 预算/熔断 (原 AxiomContext.Budget/Token/Flag)
    budget_level: int
    progress: int
    progress_max: int
    halt: bool
    # 跨轮边界框架实例状态 (原 DefaultBoundaryFramework 实例字段)
    no_progress_steps: int
    length_window: list  # EWMA 窗口
    last_result_length: int
```

### 摩擦点 3：两级路由 vs 单级条件边

**当前代码**有**两层**路由决策，嵌套执行：

**第一层（管道内）**：`RuleMatchFunction.ExecuteRuleMatchAsync` 调用 `RuleEngine.Evaluate` 产出 `decision` 字符串（`continue`/`stop`/`error`）。此决策写入 `vars["decision"]`，是管道步骤的产出。

**第二层（循环级）**：`RoundLifecycle.RunAsync` 的第④步 `Decide`：
```csharp
if (TerminationDecider.ShouldTerminate(ctx, out var term))
    decision = term;  // halt/token耗尽 → TripTerminated
else if (result.Ok && eval == EvalOutcome.Pass)
    decision = LoopDecision.Success;
else
    decision = LoopDecision.Continue;
```
然后 `AxiomLoopEngine` 检查 `if (round.Decision != LoopDecision.Continue) break;`

**第三层（HFSM 路由）**：`AgentRunner.Route(signal, budgetLevel)` 将 HFSM 信号映射为 `SchedulerAction`：
```csharp
AgentSignal.Done => SchedulerAction.Terminate,
AgentSignal.Error when budgetLevel >= 2 => SchedulerAction.SwitchAgent,
AgentSignal.Error => SchedulerAction.Retry,
AgentSignal.Retry => SchedulerAction.Continue,
```

**LangGraph 语义**：只有**一层**条件边。`add_conditional_edges("node", route_fn, {map})` 中 `route_fn` 是一个纯函数，接收 state 返回下一跳节点名。

**解决方案**：将三层路由**扁平化为一个 `route_after_boundary` 条件边函数**，把所有决策逻辑集中到一处：

```python
def route_after_boundary(state: AgentState) -> str:
    """
    扁平化三层路由：
    1. TerminationDecider (halt/token耗尽)
    2. RoundLifecycle.Decide (Success/Continue/TripTerminated)
    3. AgentRunner.Route (HFSM Signal → SchedulerAction)
    """
    # 第一层：终止裁决
    if state["halt"] or state["progress"] >= state["progress_max"]:
        return END
    
    # 第二层+第三层：决策映射 + HFSM 路由
    decision = state.get("decision", "continue")
    budget = state["budget_level"]
    
    # 对应 DecisionWordMapper → AgentSignal → Route 链
    if decision in ("stop", "complete"):  # Done → Terminate
        return END
    elif decision == "error":
        if budget >= 2:  # Error + budget≥2 → SwitchAgent
            return "switch_agent"
        else:             # Error + budget<2 → Retry
            return "retry_round"
    elif decision == "continue":
        return "guard_input"  # Continue/Retry → 循环回边
    else:
        # 未知决策词 → Partial → Continue（与 LoopDecisionMapper 一致）
        return "guard_input"
```

**关键保留点**：C# 的 `RuleEngine.Evaluate` 逻辑完全保留——它负责产出 `decision` 字符串，这个字符串通过 gRPC 返回给 Python。Python 侧的 `route_after_boundary` 只是**消费**这个字符串做路由，不替代 `RuleEngine` 的规则匹配逻辑。零 LLM 决策不丢失。

### 摩擦点 4：边界框架实例状态外提

**当前代码**（`DefaultBoundaryFramework.cs`）：

```csharp
public sealed class DefaultBoundaryFramework : IBoundaryFramework {
    private int _lastResultLength;          // 实例字段，跨轮持久
    private int _noProgressSteps;           // 实例字段，跨轮累计
    private readonly Queue<int> _lengthWindow = new();  // EWMA 窗口
```

这三个字段在 `PostRoundAsync` 中被读写，跨轮持久。它们不是 `AxiomContext.Data` 的一部分，而是 `DefaultBoundaryFramework` 实例的状态。这在 C# 单进程内工作，因为 `DefaultBoundaryFramework` 是单例。

**LangGraph 语义**：节点是**无状态纯函数**。每次调用时，节点函数从 state 读取输入，返回 partial update。不存在"实例字段"的概念。

**解决方案**：将三个实例字段**外提为 AgentState 字段**（已在摩擦点 2 的 state 定义中包含），在 `boundary_eval` 节点中读取和更新：

```python
async def boundary_eval_node(state: AgentState) -> dict:
    """
    对应 DefaultBoundaryFramework.PostRoundAsync
    将 C# 实例字段逻辑翻译为纯函数
    """
    # === 跨轮状态从 state 读取 ===
    no_progress_steps = state.get("no_progress_steps", 0)
    length_window = state.get("length_window", [])
    last_result_length = state.get("last_result_length", 0)
    
    # === UpdateCrossRound 逻辑翻译 ===
    result_text_length = len(state.get("cline_text", ""))
    eval_outcome = state.get("eval_outcome", "none")
    progress = (result_text_length > last_result_length and eval_outcome == "pass")
    no_progress_steps = 0 if progress else no_progress_steps + 1
    
    length_window.append(result_text_length)
    window_size = 5  # 对应 config.Logical.InfoGainWindow + 1
    if len(length_window) > window_size:
        length_window = length_window[-window_size:]
    
    smoothed_rate = ewma(length_window, alpha=0.3)  # InfoGainSmoothing.Ewma 翻译
    
    # === 调用 C# gRPC 做双通道评估+仲裁+重构 ===
    resp = await boundary_stub.Evaluate(BoundaryEvalRequest(
        cline_text=state.get("cline_text", ""),
        cline_error=state.get("cline_error", ""),
        round_count=state.get("step_count", 1),
        budget=state["budget_level"],
        effective_root=state.get("cwd", ""),
        no_progress_steps=no_progress_steps,
        smoothed_rate=smoothed_rate,
        last_result_length=last_result_length,
    ))
    
    updates = {
        "no_progress_steps": no_progress_steps,
        "length_window": length_window,
        "last_result_length": result_text_length,
        "boundary_assessment": resp.assessment,
    }
    
    if resp.reconstruct_hint:
        updates["reconstruct_hint"] = resp.reconstruct_hint
        if resp.reconstruct_hint == "escalate":
            # 有界升级：Math.Max(ctx.Token.Max, Math.Min(6, ctx.Token.Max + 1))
            current_max = state["progress_max"]
            updates["progress_max"] = max(current_max, min(6, current_max + 1))
    
    return updates
```

**关键设计决策**：跨轮状态（EWMA 窗口等）放在 Python state 中维护，而不是 C# gRPC 服务端。这是因为：
- 路径 A 中，C# gRPC 服务端可能是无状态的（每次调用独立）
- EWMA 窗口逻辑简单，Python 侧维护成本低
- C# 侧的 `ReactiveBoundaryTrigger.Evaluate` 和 `ProactiveBoundaryTrigger.Evaluate` 的**计算逻辑**通过 gRPC 调用，不依赖实例状态

### 摩擦点 5：LeaseDeriver HFSM 租约链

**当前代码**（`LeaseDeriver.cs`）：

```csharp
public static DerivedAgentLease Derive(AxiomContext root) {
    // 从根快照派生只读有界租约
    // 只读取授权键：UserInput/Goal/Constraints/PlanSummary/LastResult/NextHint/SessionId/Cwd/DataDir/StepCount/TaskId
    // 不携带 history/boundary_state/budget —— 这些留在调度侧
}
```

这是 HFSM 安全模型的核心——子执行智能体只看到授权切片，不能访问根快照全量。

**LangGraph 语义**：所有 state 字段对每个节点可见。没有原生的"租约"概念。

**解决方案**：有两种选择：

**选择 A（推荐）**：在 `cline_execute` 节点内部显式构造租约切片——只从 state 中提取授权字段传给 C# gRPC：

```python
async def cline_execute_node(state: AgentState) -> dict:
    """对应 ClineExecutorFunction — 内部构造 HFSM 租约"""
    # 显式租约切片：只传授权字段给 C#，不传 budget/halt/boundary_state
    lease = {
        "user_input": state.get("filtered_input", state["user_input"]),
        "goal": state["goal"],
        "constraints": state["constraints"],
        "plan_summary": state["plan_summary"],
        "last_result": state.get("exec_result"),
        "next_hint": state.get("reconstruct_hint") or state.get("next_prompt_hint"),
        "session_id": state.get("session_id"),
        "cwd": state.get("cwd", ""),
        "data_dir": state.get("data_dir", ""),
        "step_count": state["step_count"],
        "task_id": state.get("task_id", ""),
    }
    resp = await cline_stub.Execute(ClineExecuteRequest(lease=lease, ...))
    return {"cline_text": resp.text, ...}
```

**选择 B**：用 LangGraph 的 `input_schema`/`output_schema` 机制限制节点可见字段。但这只控制输入/输出过滤，不限制节点内部访问 state。不如选择 A 安全。

**保留点**：`LeaseDeriver.Derive` 的 C# 逻辑在路径 B 中完全保留（单轮 gRPC 调用时 C# 内部仍用 LeaseDeriver）。路径 A 中翻译为 Python 侧的显式字段选择。

### 摩擦点 6：guard_output 条件执行

**当前代码**（`WorkflowRunner.BuildPipeline`）：

```csharp
steps.Add(new AsyncStep<RoundPipelineContext>(
    Name: "guard_output",
    Execute: async ctx => { await RoundStepInvoker.ExecuteStepAsync(GuardOutputStep, ...); },
    IsFatal: false,
    CanExecute: ctx => DecisionWordMapper.IsSuccess(  // 条件执行谓词
        ctx.Vars.TryGetValue(RoundKeys.Decision, out var d) ? d?.ToString() : null)));
```

`guard_output` 步骤有 `CanExecute` 谓词——只在 `decision` 为 `stop`/`complete` 时执行。

**LangGraph 语义**：没有 `CanExecute` 概念。条件执行通过**条件边跳过节点**实现。

**解决方案**：用条件边替代 `CanExecute`：

```python
# guard_output 不作为固定边连接的节点
# 而是用条件边从 rule_match 跳转到 guard_output（或跳过）

def route_after_match(state: AgentState) -> str:
    decision = state.get("decision", "")
    if decision in ("stop", "complete"):
        return "guard_output"  # 成功 → 执行输出守卫
    return "boundary_eval"     # 非成功 → 跳过输出守卫，直接边界评估

# 图定义
workflow.add_conditional_edges("rule_match", route_after_match, {
    "guard_output": "guard_output",
    "boundary_eval": "boundary_eval",
})
workflow.add_edge("guard_output", "boundary_eval")
```

### 摩擦点 7：ReturnSpec 结构化信号解析

**当前代码**（`WorkflowRunner.BuildPipeline`）：

```csharp
// cline_execute 步骤后插入内联解析逻辑
if (s.FuncName == "cline_execute" &&
    ctx.Vars.TryGetValue(RoundKeys.ExecResult, out var er) && er is ClineResult cline ...)
{
    var spec = AgentReturnSpecParser.TryParse(cline.Text);
    if (spec != null)
        ctx.Vars[RoundKeys.ReturnSpec] = spec;  // 写入 vars 供后续步骤消费
}
```

这是管道步骤之间的**内联后处理**——在 `cline_execute` 执行完成后、`rule_match` 执行前，插入一段解析逻辑把 Cline 返回文本中的结构化信封提取出来。

**LangGraph 语义**：有两种处理方式：

**选择 A（推荐）**：在 `cline_execute` 节点内部完成解析——节点函数返回时同时返回 `return_spec`：

```python
async def cline_execute_node(state: AgentState) -> dict:
    resp = await cline_stub.Execute(...)
    # 内联解析 ReturnSpec（对应 AgentReturnSpecParser.TryParse）
    return_spec = parse_return_spec(resp.text) if resp.text else None
    return {
        "cline_text": resp.text,
        "cline_error": resp.error,
        "exec_result": {"ok": resp.ok, "text": resp.text, ...},
        "return_spec": return_spec,  # 供 rule_match 消费
    }
```

**选择 B**：增加一个独立的 `parse_return_spec` 节点在 `cline_execute` 和 `rule_match` 之间。更清晰但多一个节点/一次 state 合并。

**推荐选择 A**：减少节点数量，降低 gRPC 调用次数。解析逻辑简单（正则/JSON 解析），放在 `cline_execute` 节点内部不违反单一职责。

---

## 三、修订后的 StateGraph 精确设计

基于 7 个摩擦点的解决方案，以下是**修正前一轮报告中 3 处不准确之处**后的精确设计：

### 3.1 修正点

| # | 前一轮报告的说法 | 精确分析后的修正 |
|---|---|---|
| 1 | "route_after_match 条件边对应 RuleEngine.Route()" | `RuleEngine` 产出 `decision` 字符串，条件边函数**消费**该字符串做路由。Route() 的 HFSM 逻辑翻译为条件边的分支判断。两者是**消费关系**不是**等价关系** |
| 2 | "budget_max 映射为 recursion_limit" | 不准确。`recursion_limit` 是 Pregel 超步上限，对应的是 `MaxSteps` 守卫（`GuardConfig.MaxSteps=10`），不是 `Token.Max`（MAX_PROGRESS=3）。`Token.Max` 映射为 `progress_max` state 字段 |
| 3 | "provide_prompt 节点完全用 Python LangChain 实现" | 忽略了 `RuleEngine` 在 `provide_prompt` 中的 `needsReplan` 判定复用。`provide_prompt` 节点需要调用 C# gRPC 的 `RuleEngineService.NeedsReplan` 来决定是否重新规划，不能完全在 Python 内实现 |

### 3.2 修正后的图拓扑

```
START
  │
  ▼
guard_input          ← C# gRPC: GuardFunction.CheckInputSafetyAsync
  │  写: filtered_input
  ▼
provide_prompt       ← Python LLM + C# gRPC RuleEngine.NeedsReplan
  │  写: prompt, system, l1, l2, replanned
  │  读: filtered_input(user_input回灌), goal, constraints, plan_summary, exec_result, next_prompt_hint, task_id
  ▼
cline_execute         ← C# gRPC: ClineAgentExecutor (+ 内联 ReturnSpec 解析)
  │  写: cline_text, cline_error, exec_result, return_spec, session_id
  │  读: system, l1, l2, session_id, cwd, data_dir
  │  可选: interrupt_before (人机审批)
  ▼
rule_match           ← C# gRPC: RuleEngine.Evaluate (+ ReturnSpec 优先)
  │  写: decision, next_prompt_hint, reason, rule, step_count
  ▼
route_after_match ──── 条件边（摩擦点 6: guard_output 条件执行）
  │
  ├── decision=stop/complete → guard_output → boundary_eval
  └── 其他 → boundary_eval（跳过 guard_output）

boundary_eval        ← C# gRPC: DefaultBoundaryFramework.PostRoundAsync
  │  写: boundary_assessment, reconstruct_hint, no_progress_steps, length_window, last_result_length
  │  (摩擦点 4: 实例状态外提为 state 字段)

budget_escalate      ← Python 纯函数（摩擦点 1: 可变对象翻译为纯函数）
  │  写: budget_level, progress, halt, progress_max(escalate时)
  │  (对应 RoundLifecycle 的 Collect→Evaluate→Decide 步骤)

route_after_boundary ──── 条件边（摩擦点 3: 三级路由扁平化）
  │
  ├── halt 或 progress>=progress_max → END          (TerminationDecider)
  ├── decision=stop/complete → END                   (Done → Terminate)
  ├── decision=error + budget>=2 → switch_agent     (HFSM SwitchAgent)
  ├── decision=error + budget<2 → retry_round        (HFSM Retry)
  └── decision=continue/未知 → guard_input           (循环回边)

switch_agent → guard_input  (切换后重新开始)
retry_round → guard_input    (重试后重新开始)
```

### 3.3 完整修正版伪代码

```python
from typing import TypedDict, Annotated, Optional
import operator
from langgraph.graph import StateGraph, START, END
from langgraph.checkpoint.postgres import PostgresSaver
from langgraph.types import interrupt, Command

# === 状态定义（摩擦点 2: RoundKeys 全量映射 + 实例状态外提）===
class AgentState(TypedDict):
    # 入参 / 守卫回灌
    user_input: str
    filtered_input: Optional[str]
    # 目标 / 约束 / 规划
    goal: str
    constraints: str
    plan_summary: str
    # 执行与提示
    exec_result: Optional[dict]
    next_prompt_hint: Optional[str]
    prompt: Optional[str]
    system: Optional[str]
    l1: Optional[str]
    l2: Optional[str]
    replanned: Optional[bool]
    # 子智能体返回规约
    return_spec: Optional[dict]
    # 循环控制
    decision: Optional[str]
    step_count: int
    reason: Optional[str]
    rule: Optional[str]
    # 边界
    boundary_action: Optional[str]
    boundary_assessment: Optional[dict]
    reconstruct_hint: Optional[str]
    cwd: Optional[str]
    data_dir: Optional[str]
    boundary_state: Optional[dict]
    # 会话连续性
    session_id: Optional[str]
    # 输出
    output: Optional[str]
    safe_output: Optional[str]
    # 预算/熔断（摩擦点 1: 可变对象 → state 字段）
    budget_level: int          # 原 FailureBudget.Level (0-3)
    progress: int              # 原 ProgressToken.Value (0-Max)
    progress_max: int          # 原 ProgressToken.Max (默认3, escalate封顶6)
    halt: bool                 # 原 ControlFlag.Halt
    # 跨轮边界框架状态（摩擦点 4: 实例字段 → state 字段）
    no_progress_steps: int
    length_window: list        # EWMA 窗口
    last_result_length: int
    # 评估
    eval_outcome: Optional[str]  # pass/partial/fail/parse_error
    error_class: Optional[str]
    # 人工审批
    human_approved: Optional[bool]
    # 任务
    task_id: Optional[str]

# === 节点定义 ===

async def guard_input_node(state: AgentState) -> dict:
    """对应 GuardFunction.ExecuteInputGuardAsync"""
    resp = await guard_stub.CheckInputSafety(GuardRequest(input=state["user_input"]))
    filtered = resp.filtered_input if resp.filtered else state["user_input"]
    return {"filtered_input": filtered, "user_input": filtered}

async def provide_prompt_node(state: AgentState) -> dict:
    """对应 PromptFrameworkFunction.ExecuteProvidePromptAsync
    修正点 3: 需调用 C# RuleEngine.NeedsReplan 判定是否重新规划"""
    if state["step_count"] == 0:
        # 首步：生成 L1 task prompt（模型无关）
        l1 = await generate_task_prompt(state)
        l2 = build_l2_continue(state)
        return {"l1": l1, "l2": l2, "prompt": assemble(state, l1, l2)}
    else:
        # 继续步：调用 C# RuleEngine 判定 needsReplan
        needs_replan = await rule_stub.NeedsReplan(NeedsReplanRequest(
            exec_result=state.get("exec_result"),
            step_count=state["step_count"],
        ))
        if needs_replan.value:
            l2 = await generate_step_prompt(state)
            return {"l2": l2, "replanned": True, "prompt": assemble(state, state["l1"], l2)}
        else:
            l2 = build_l2_continue(state)
            return {"l2": l2, "replanned": False, "prompt": assemble(state, state["l1"], l2)}

async def cline_execute_node(state: AgentState) -> dict:
    """对应 ClineExecutorFunction.ExecuteClineAsync
    摩擦点 5: 显式构造 HFSM 租约切片
    摩擦点 7: 内联 ReturnSpec 解析"""
    # 人机审批（可选）
    if state.get("requires_approval") and state.get("human_approved") is None:
        decision = interrupt({"round": state["step_count"], "prompt": state["l2"]})
        if not decision["approved"]:
            return {"decision": "stop", "reason": "user_cancelled"}
    
    # HFSM 租约切片：只传授权字段
    resp = await cline_stub.Execute(ClineExecuteRequest(
        system=state.get("system", ""),
        l1=state.get("l1", ""),
        l2=state.get("l2", ""),
        session_id=state.get("session_id", ""),
        cwd=state.get("cwd", ""),
        data_dir=state.get("data_dir", ""),
        budget=state["budget_level"],
    ))
    
    # 内联 ReturnSpec 解析（摩擦点 7）
    return_spec = parse_return_spec(resp.text) if resp.text else None
    
    return {
        "cline_text": resp.text,
        "cline_error": resp.error,
        "exec_result": {"ok": resp.ok, "text": resp.text, "stage": resp.stage},
        "return_spec": return_spec,
        "session_id": resp.session_id if resp.session_id else state.get("session_id"),
    }

async def rule_match_node(state: AgentState) -> dict:
    """对应 RuleMatchFunction.ExecuteRuleMatchAsync
    C# RuleEngine 零 LLM 决策逻辑完全保留"""
    resp = await rule_stub.Evaluate(RuleMatchRequest(
        exec_result=state.get("exec_result"),
        step_count=state["step_count"],
        return_spec=state.get("return_spec"),
    ))
    return {
        "decision": resp.decision,
        "next_prompt_hint": resp.next_prompt_hint,
        "reason": resp.reason,
        "rule": resp.rule,
        "step_count": state["step_count"] + 1,
    }

async def guard_output_node(state: AgentState) -> dict:
    """对应 GuardFunction.ExecuteOutputGuardAsync"""
    resp = await guard_stub.CheckOutputSafety(GuardRequest(
        input=state.get("cline_text", "")
    ))
    return {"safe_output": resp.filtered_input}

async def boundary_eval_node(state: AgentState) -> dict:
    """对应 DefaultBoundaryFramework.PostRoundAsync
    摩擦点 4: 实例状态从 state 读取"""
    no_progress = state.get("no_progress_steps", 0)
    length_window = state.get("length_window", [])
    last_len = state.get("last_result_length", 0)
    
    # UpdateCrossRound 翻译
    text_len = len(state.get("cline_text", ""))
    eval_out = state.get("eval_outcome", "none")
    progress_made = text_len > last_len and eval_out == "pass"
    no_progress = 0 if progress_made else no_progress + 1
    length_window = (length_window + [text_len])[-6:]
    smoothed_rate = ewma(length_window, alpha=0.3)
    
    # C# gRPC 调用（双通道评估+仲裁+重构的计算逻辑）
    resp = await boundary_stub.Evaluate(BoundaryEvalRequest(
        cline_text=state.get("cline_text", ""),
        cline_error=state.get("cline_error", ""),
        round_count=state["step_count"],
        budget=state["budget_level"],
        effective_root=state.get("cwd", ""),
        no_progress_steps=no_progress,
        smoothed_rate=smoothed_rate,
        last_result_length=last_len,
    ))
    
    updates = {
        "no_progress_steps": no_progress,
        "length_window": length_window,
        "last_result_length": text_len,
        "boundary_assessment": resp.assessment,
    }
    if resp.reconstruct_hint:
        updates["reconstruct_hint"] = resp.reconstruct_hint
        if resp.reconstruct_hint == "escalate":
            cur_max = state["progress_max"]
            updates["progress_max"] = max(cur_max, min(6, cur_max + 1))
    return updates

def budget_escalate_node(state: AgentState) -> dict:
    """摩擦点 1: FailureBudget/ProgressToken/ControlFlag 可变对象 → 纯函数
    对应 RoundLifecycle 的 Collect→Evaluate→Decide 步骤"""
    eval_out = state.get("eval_outcome", "none")
    err_cls = state.get("error_class")
    budget = state.get("budget_level", 0)
    progress = state.get("progress", 0)
    
    # BudgetEscalator 逻辑翻译
    if err_cls is not None:
        budget = min(budget + 1, 3)
    elif eval_out == "pass":
        budget = max(budget - 1, 0)
    
    # ProgressUpdater 逻辑翻译
    if eval_out in ("fail", "parse_error"):
        progress = min(progress + 1, state["progress_max"])
    elif eval_out == "pass":
        progress = max(progress - 1, 0)
    
    # ControlFlag 逻辑翻译
    halt = budget >= 3 or state.get("halt", False)
    
    return {"budget_level": budget, "progress": progress, "halt": halt}

# === 条件边 ===

def route_after_match(state: AgentState) -> str:
    """摩擦点 6: guard_output 条件执行替代 CanExecute"""
    decision = state.get("decision", "")
    if decision in ("stop", "complete"):
        return "guard_output"
    return "budget_escalate"

def route_after_boundary(state: AgentState) -> str:
    """摩擦点 3: 三级路由扁平化为单级条件边"""
    # 第一层: TerminationDecider
    if state.get("halt") or state["progress"] >= state["progress_max"]:
        return END
    # 第二层+第三层: Decision + HFSM Route
    decision = state.get("decision", "continue")
    budget = state["budget_level"]
    if decision in ("stop", "complete"):
        return END
    elif decision == "error":
        return "switch_agent" if budget >= 2 else "retry_round"
    else:
        return "guard_input"

# === 图组装 ===
wf = StateGraph(AgentState)
wf.add_node("guard_input", guard_input_node)
wf.add_node("provide_prompt", provide_prompt_node)
wf.add_node("cline_execute", cline_execute_node)
wf.add_node("rule_match", rule_match_node)
wf.add_node("guard_output", guard_output_node)
wf.add_node("boundary_eval", boundary_eval_node)
wf.add_node("budget_escalate", budget_escalate_node)
wf.add_node("switch_agent", switch_agent_node)
wf.add_node("retry_round", retry_round_node)

wf.add_edge(START, "guard_input")
wf.add_edge("guard_input", "provide_prompt")
wf.add_edge("provide_prompt", "cline_execute")
wf.add_edge("cline_execute", "rule_match")
wf.add_edge("guard_output", "boundary_eval")
wf.add_edge("boundary_eval", "budget_escalate")

# 摩擦点 6: rule_match 后条件跳转
wf.add_conditional_edges("rule_match", route_after_match, {
    "guard_output": "guard_output",
    "budget_escalate": "budget_escalate",
})

# 摩擦点 3: budget_escalate 后三级路由扁平化
wf.add_conditional_edges("budget_escalate", route_after_boundary, {
    END: END,
    "guard_input": "guard_input",
    "switch_agent": "switch_agent",
    "retry_round": "retry_round",
})

wf.add_edge("switch_agent", "guard_input")
wf.add_edge("retry_round", "guard_input")

app = wf.compile(
    checkpointer=PostgresSaver.from_conn_string(DATABASE_URL),
    interrupt_before=["cline_execute"],  # 可选人机审批
    recursion_limit=25,  # 对应 GuardConfig.MaxSteps(10) × 2.5 安全余量
)
```

---

## 四、C# 侧最小改造清单

路径 A 需要 C# 侧暴露 gRPC 服务。改造原则：**核心逻辑零修改，仅添加 gRPC 包装层**。

### 4.1 需要暴露的 gRPC 服务

| gRPC 服务 | 包装的 C# 现有方法 | 修改量 | 依赖文件 |
|---|---|---|---|
| `GuardService.CheckInputSafety` | `GuardFunction.CheckInputSafetyAsync` | 新增 gRPC 包装类 | `Agent.Guardrails/GuardFunction.cs` |
| `GuardService.CheckOutputSafety` | `GuardFunction.CheckOutputSafetyAsync` | 同上 | 同上 |
| `RuleEngineService.Evaluate` | `RuleEngine.Evaluate(RuleSet.Default, data, stepCount)` | 新增 gRPC 包装类 | `Agent.Core/RuleEngine.cs` |
| `RuleEngineService.NeedsReplan` | `RuleEngine.Evaluate` (复用同一引擎) | 同上 | 同上 |
| `BoundaryService.Evaluate` | `DefaultBoundaryFramework.PostRoundAsync` | 新增 gRPC 包装类 | `Agent.Core/Boundaries/DefaultBoundaryFramework.cs` |
| `ClineExecutorService.Execute` | `ClineAgentExecutor.ExecuteAsync` | 新增 gRPC 包装类 | `Agent.ClineUsage/ClineAgentExecutor.cs` |

### 4.2 不需要修改的文件（核心逻辑）

以下文件**完全不动**：

| 文件 | 原因 |
|---|---|
| `AxiomLoopEngine.cs` | 路径 B 中仍被使用（单轮 gRPC 调用入口） |
| `RoundLifecycle.cs` | 路径 B 中仍被使用 |
| `WorkflowRunner.cs` | 路径 B 中仍被使用 |
| `RoundStepInvoker.cs` | 路径 B 中仍被使用 |
| `RuleEngine.cs` | gRPC 包装层调用，核心逻辑不动 |
| `DefaultBoundaryFramework.cs` | gRPC 包装层调用，核心逻辑不动 |
| `LeaseDeriver.cs` | gRPC 包装层调用，核心逻辑不动 |
| `BoundaryTriggerArbiter.cs` | 被 DefaultBoundaryFramework 内部调用 |
| `BoundaryReconstructor.cs` | 被 DefaultBoundaryFramework 内部调用 |
| `DecisionWordMapper.cs` | 被 RuleMatchFunction 和 LoopDecisionMapper 调用 |
| `AgentRunner.cs` | 路径 B 中仍被使用 |
| `AgentOrchestrator.cs` | 路径 B 中仍被使用 |

### 4.3 新增的文件

| 新增文件 | 职责 |
|---|---|
| `Agent.GrpcServices/agent.proto` | gRPC 服务定义 |
| `Agent.GrpcServices/GuardGrpcService.cs` | 包装 GuardFunction |
| `Agent.GrpcServices/RuleEngineGrpcService.cs` | 包装 RuleEngine |
| `Agent.GrpcServices/BoundaryGrpcService.cs` | 包装 DefaultBoundaryFramework |
| `Agent.GrpcServices/ClineExecutorGrpcService.cs` | 包装 ClineAgentExecutor |
| `Agent.GrpcServices/GrpcHost.cs` | gRPC 服务器启动/停止 |

### 4.4 gRPC 服务定义

```protobuf
syntax = "proto3";
package agent;

service GuardService {
  rpc CheckInputSafety(GuardRequest) returns (GuardResponse);
  rpc CheckOutputSafety(GuardRequest) returns (GuardResponse);
}

service RuleEngineService {
  rpc Evaluate(RuleMatchRequest) returns (RuleMatchResponse);
  rpc NeedsReplan(NeedsReplanRequest) returns (NeedsReplanResponse);
}

service BoundaryService {
  rpc Evaluate(BoundaryEvalRequest) returns (BoundaryEvalResponse);
}

service ClineExecutorService {
  rpc Execute(ClineExecuteRequest) returns (ClineExecuteResponse);
}

message GuardRequest { string input = 1; }
message GuardResponse { bool filtered = 1; string filtered_input = 2; }

message RuleMatchRequest {
  optional string exec_result_json = 1;
  int32 step_count = 2;
  optional string return_spec_json = 3;
}
message RuleMatchResponse {
  string decision = 1;
  optional string next_prompt_hint = 2;
  optional string reason = 3;
  optional string rule = 4;
}

message NeedsReplanRequest {
  optional string exec_result_json = 1;
  int32 step_count = 2;
}
message NeedsReplanResponse { bool value = 1; }

message BoundaryEvalRequest {
  string cline_text = 1;
  string cline_error = 2;
  int32 round_count = 3;
  int32 budget = 4;
  string effective_root = 5;
  int32 no_progress_steps = 6;
  double smoothed_rate = 7;
  int32 last_result_length = 8;
}
message BoundaryEvalResponse {
  string assessment_json = 1;
  optional string reconstruct_hint = 2;
}

message ClineExecuteRequest {
  string system = 1;
  string l1 = 2;
  string l2 = 3;
  string session_id = 4;
  string cwd = 5;
  string data_dir = 6;
  int32 budget = 7;
}
message ClineExecuteResponse {
  bool ok = 1;
  string text = 2;
  string error = 3;
  string stage = 4;
  optional string session_id = 5;
}
```

---

## 五、跨语言数据契约设计

### 5.1 序列化策略

C# 的强类型对象（`ClineResult`、`ChildAgentReturn`、`BoundaryAssessment`）需要跨 gRPC 传输。策略：

| C# 类型 | gRPC 传输方式 | Python 反序列化 |
|---|---|---|
| `ClineResult` | 序列化为 JSON 字符串放入 `exec_result_json` | `json.loads()` → dict |
| `ChildAgentReturn` (ReturnSpec) | 序列化为 JSON 字符串放入 `return_spec_json` | `json.loads()` → dict |
| `BoundaryAssessment` | 序列化为 JSON 字符串放入 `assessment_json` | `json.loads()` → dict |
| 基本类型 (int/string/bool) | 直接用 proto 基本类型 | 直接使用 |

### 5.2 ReturnSpec 解析（摩擦点 7 的 Python 侧翻译）

```python
import json
import re

def parse_return_spec(text: str | None) -> dict | None:
    """
    对应 AgentReturnSpecParser.TryParse
    从 Cline 返回文本中提取结构化信封
    """
    if not text:
        return None
    # 查找 <agent_return> JSON 信封
    match = re.search(r'<agent_return>(.*?)</agent_return>', text, re.DOTALL)
    if not match:
        return None
    try:
        data = json.loads(match.group(1).strip())
        # 验证必需字段
        if "signal" not in data:
            return None
        return data
    except json.JSONDecodeError:
        return None
```

### 5.3 EWMA 计算（摩擦点 4 的 Python 侧翻译）

```python
def ewma(values: list[float], alpha: float) -> float:
    """对应 InfoGainSmoothing.Ewma"""
    if not values:
        return 0.0
    result = float(values[0])
    for v in values[1:]:
        result = alpha * float(v) + (1 - alpha) * result
    return result
```

---

## 六、迁移优先级矩阵

按"摩擦点解决难度"和"短板解决价值"排序：

| 优先级 | 摩擦点 | 解决方案 | 难度 | 解决的短板 |
|---|---|---|---|---|
| P0 | 摩擦点 3: 三级路由扁平化 | `route_after_boundary` 条件边函数 | 低 | 无（必须解决，否则图无法运转） |
| P0 | 摩擦点 1: 可变对象→纯函数 | `budget_escalate` 节点翻译 | 中 | 无（必须解决，否则 budget 不工作） |
| P0 | 摩擦点 6: guard_output 条件执行 | 条件边替代 CanExecute | 低 | 无（必须解决，否则守卫不执行） |
| P1 | 摩擦点 4: 实例状态外提 | EWMA/no_progress 入 state | 中 | 持久化（短板 1） |
| P1 | 摩擦点 2: Seed/Harvest 消除 | state 字段直接映射 | 低 | 持久化（短板 1） |
| P1 | 摩擦点 7: ReturnSpec 解析 | 节点内联解析 | 低 | 无（功能正确性） |
| P2 | 摩擦点 5: HFSM 租约切片 | 显式字段选择 | 中 | 安全性（非短板，但需保留） |

### 6.1 建议实施顺序

```
阶段 0（验证）：先实现 P0 三个摩擦点 → 最小可运行图
  └─ guard_input → provide_prompt → cline_execute → rule_match → route_after_match
     只用 MemorySaver，验证循环+条件边工作

阶段 1（持久化）：加 P1 两个摩擦点 → 持久化可用
  └─ 接入 PostgresSaver，验证崩溃恢复
  └─ 接入 LangSmith 追踪

阶段 2（完整）：加 P1 摩擦点 7 + P2 摩擦点 5 → 全功能
  └─ ReturnSpec 解析 + HFSM 租约 + 人机审批 interrupt

阶段 3（生产化）：边界框架 gRPC + Cline gRPC → 全量部署
  └─ 所有 C# 核心组件暴露为 gRPC 服务
```

---

## 七、与前一轮报告的差异对照

| 维度 | 前一轮报告 | 本轮修正 |
|---|---|---|
| 概念映射 | "高度同构" | 拓扑层面同构，**执行语义层面有 7 个摩擦点** |
| 路径 A 可行性 | "C# 核心逻辑零修改" | 需新增 6 个 gRPC 包装类 + 1 个 proto 文件，核心逻辑确实零修改 |
| 路由映射 | "Route → 条件边" | 三级路由需**扁平化**为一个条件边函数，非简单映射 |
| budget 映射 | "Token.Max → recursion_limit" | 不准确。`MaxSteps`(10) → `recursion_limit`(25)，`Token.Max`(3) → `progress_max` state 字段 |
| provide_prompt | "完全 Python 实现" | 需调用 C# `RuleEngineService.NeedsReplan`，**不能完全 Python 实现** |
| 状态管理 | "RoundState → AgentState" | 需消除 Seed/Harvest + 实例状态外提，**非简单映射** |
| 边界框架 | "gRPC 包装 PostRoundAsync" | 需将实例字段（EWMA 窗口等）外提为 state 字段，**不能直接包装** |
| guard_output | "固定边连接" | 需用条件边替代 CanExecute，**非固定边** |

---

> **结论**：前一轮报告的宏观方向正确（路径 A 包装器模式、LangGraph 补位短板），但低估了执行语义层面的摩擦。本文识别的 7 个摩擦点都有具体解决方案，且 C# 核心逻辑确实可以零修改（仅新增 gRPC 包装层）。迁移的真正工作量在 Python 侧的状态模型翻译和路由扁平化，而非 C# 侧改造。
