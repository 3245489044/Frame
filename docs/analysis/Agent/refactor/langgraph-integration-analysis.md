# LangGraph 集成方案：基于现有 workflow 统一调度解决短板

> **分析对象**：本项目 Agent 框架（HFSM + 公理引擎驱动，C# .NET）  
> **集成目标**：引入 LangGraph（Python，StateGraph 有向图编排）统一 workflow 调度  
> **分析日期**：2026-08-09  
> **分析方法**：基于源码只读分析 + LangGraph v1.0+ 官方能力 + 市面生产实践综合设计  
> **约束**：仅分析，不修改任何代码和逻辑

---

## 目录

1. [集成动机与目标](#一集成动机与目标)
2. [当前 workflow 与 LangGraph 的概念映射](#二当前-workflow-与-langgraph-的概念映射)
3. [三种集成路径对比与推荐](#三三种集成路径对比与推荐)
4. [推荐方案详细设计：包装器模式](#四推荐方案详细设计包装器模式)
5. [短板逐项解决方案](#五短板逐项解决方案)
6. [StateGraph 设计与伪代码](#六stategraph-设计与伪代码)
7. [多 Agent 协作扩展](#七多-agent-协作扩展)
8. [跨语言桥接方案](#八跨语言桥接方案)
9. [实施路线图](#九实施路线图)
10. [风险与缓解](#十风险与缓解)
11. [保留与增强矩阵](#十一保留与增强矩阵)

---

## 一、集成动机与目标

### 1.1 已识别的短板

基于前期框架对比分析，本项目存在 6 项结构性短板：

| # | 短板 | 影响 | LangGraph 可解决 |
|---|---|---|---|
| 1 | 无长期记忆/持久化 | 不支持跨会话上下文 | ✅ Checkpointer (SQLite/Postgres/Redis) |
| 2 | LLM 供应商锁定 DeepSeek | 切换成本高 | ✅ 模型无关 + 按节点路由不同模型 |
| 3 | 无生态/社区 | 无社区支持、无插件 | ✅ 126K+ Stars, LangSmith, 45+ 集成 |
| 4 | 执行后端绑定 Cline | 部署场景受限 | ✅ 可插拔工具 + MCP 适配器 |
| 5 | 无人机协作原语 | 无 interrupt/approve | ✅ 原生 interrupt() + Command(resume) |
| 6 | 无测试/调试工具 | 无可视化追踪 | ✅ LangSmith 全链路追踪 + 时间旅行调试 |

### 1.2 集成原则

- **保留核心优势**：零 LLM 规则引擎、双通道边界评估、有界升级、管道内联守卫不替换
- **LangGraph 补位**：用 LangGraph 的图编排、持久化、人机协作、可观测性能力补齐短板
- **渐进式引入**：不一次性重写，先包装后下沉
- **跨语言协作**：C# 保留为执行引擎，Python LangGraph 作为编排层

---

## 二、当前 workflow 与 LangGraph 的概念映射

本项目现有的 workflow 结构与 LangGraph 的图执行模型高度同构，这是集成可行性的核心前提。

### 2.1 概念对应关系

| 本项目概念 | LangGraph 概念 | 映射说明 |
|---|---|---|
| `AxiomLoopEngine.while(true)` | `StateGraph` + 循环边 | while 循环 → 有向图的环 |
| `RoundLifecycle.RunAsync` (五步) | Super-step (节点执行) | 每轮 = 一个 super-step |
| `StaticAsyncPipeline` 5 步 | 5 个图节点 + 顺序边 | guard_input → provide_prompt → cline_execute → rule_match → guard_output |
| `RuleEngine.Route()` | `add_conditional_edges` | Done/Error/Retry → 不同边目标 |
| `RoundState` + `RoundStateMapper` | `AgentState` (TypedDict) | Seed = 状态初始化, Harvest = 状态更新 |
| `AgentRunCancellation` (AsyncLocal) | `thread_id` + `recursion_limit` | 取消和预算通过 config 传递 |
| `DefaultBoundaryFramework` | 自定义节点 `boundary_eval` | 双通道评估封装为后置节点 |
| `BoundaryReconstructor.Apply` | 节点内状态更新 | 重构 hint 写入 state |
| `ctx.Data` 字典 | State 字段 | Cwd/ReconstructHint 等映射为 state 字段 |
| `LeaseDeriver.Derive` | 节点入口状态读取 | 租约从 state 派生 |
| `Token.Max` (有界升级) | `recursion_limit` + 状态字段 | 预算封顶映射为图递归限制 |

### 2.2 关键映射：Route 决策 → 条件边

本项目最核心的确定性路由逻辑可直接映射为 LangGraph 的条件边：

```
当前 Route 逻辑:                          LangGraph 条件边:
─────────────────                         ──────────────────
RuleEngine.Evaluate                        def route_after_match(state):
  → Done    → Terminate                      if state["decision"] == "done":
  → Error   → SwitchAgent (budget>=2)            return END
              Retry (budget<2)                 if state["decision"] == "error":
  → Retry   → Continue                           if state["budget"] >= 2:
                                                   return "switch_agent"
                                                 return "retry_round"
                                               if state["decision"] == "retry":
                                                 return "guard_input"  # 循环回边
```

> **关键优势**：零 LLM 的 Route 逻辑完全保留。LangGraph 的条件边本身就是确定性函数，不调用 LLM。本项目的确定性优势不丧失。

### 2.3 关键映射：循环结构

```
当前: AxiomLoopEngine while(true) { round → evaluate → break? }
目标: StateGraph { START → guard_input → ... → rule_match → route_decision
                   route_decision --Continue--> guard_input  (循环边)
                   route_decision --Done--> END
                   route_decision --Error(budget>=2)--> switch_agent
                   route_decision --Error(budget<2)--> retry_round → guard_input
                 }
```

---

## 三、三种集成路径对比与推荐

### 路径 A：LangGraph 包装器模式（推荐）

**思路**：LangGraph StateGraph 作为外层编排器，C# Agent 的核心组件（RuleEngine、BoundaryFramework、GuardFunction）通过 gRPC/REST 暴露为服务，作为 LangGraph 节点调用。

```
LangGraph StateGraph (Python, 编排层)
  ├── Node: guard_input       → 调用 C# GuardFunction 服务
  ├── Node: provide_prompt    → LangGraph LLM 调用 (模型无关)
  ├── Node: cline_execute     → 调用 C# ClineAgentExecutor 服务
  ├── Node: rule_match        → 调用 C# RuleEngine 服务
  ├── Node: guard_output      → 调用 C# GuardFunction 服务
  ├── Node: boundary_eval     → 调用 C# BoundaryFramework 服务
  ├── Node: route_decision    → Python 纯函数 (条件边)
  ├── Node: interrupt_review  → LangGraph interrupt()
  ├── Checkpointer            → PostgresSaver
  ├── LangSmith tracing       → 全链路追踪
  └── conditional_edges       → Route 映射
```

| 维度 | 评价 |
|---|---|
| 侵入性 | 低 — C# 核心逻辑不变，仅添加 gRPC 接口 |
| 能力覆盖 | 高 — 全部 6 项短板可解决 |
| 性能 | 中 — 跨语言 gRPC 调用增加 ~0.5ms/节点 |
| 复杂度 | 中 — 需搭建 Python+C# 双进程 |
| 可维护性 | 高 — 各层职责清晰 |
| 推荐度 | ★★★★★ |

### 路径 B：LangGraph 子图嵌入模式

**思路**：保留 C# AxiomLoopEngine 完整运行，LangGraph 仅作为外层调度器，通过单个节点调用 C# 编排器执行一轮。

```
LangGraph StateGraph (Python, 外层)
  ├── Node: execute_round     → 调用 C# AgentOrchestrator.RunAsyncCore (执行一轮)
  ├── Node: checkpoint_round  → PostgresSaver 持久化
  ├── Node: human_review      → interrupt() 审批
  └── conditional_edges       → 基于返回的 decision 路由
```

| 维度 | 评价 |
|---|---|
| 侵入性 | 最低 — C# 完全不变 |
| 能力覆盖 | 中 — 持久化和人机协作可解决，但节点级追踪和模型路由受限 |
| 性能 | 高 — 每轮仅一次跨语言调用 |
| 复杂度 | 低 — 最少改动 |
| 可维护性 | 高 — 双层完全解耦 |
| 推荐度 | ★★★★ |

### 路径 C：全量 Python 重写

**思路**：用 Python + LangGraph 重写整个 Agent 框架，C# 仅保留 Cline 守护进程池。

| 维度 | 评价 |
|---|---|
| 侵入性 | 最高 — 全部重写 |
| 能力覆盖 | 最高 — 完全统一 |
| 性能 | 低 — 重写初期有性能风险 |
| 复杂度 | 最高 — 大量重写工作 |
| 可维护性 | 中 — 前期技术债 |
| 推荐度 | ★★ |

### 推荐：路径 A（包装器模式）

**理由**：
1. 平衡了能力覆盖和侵入性 — 6 项短板全部可解决，同时 C# 核心逻辑零修改
2. gRPC 跨语言开销可接受 — 同机房 ~0.5ms，相对 LLM 调用的秒级延迟可忽略
3. 职责清晰 — Python 做编排（图、持久化、人机、观测），C# 做执行（规则、边界、守卫、Cline）
4. 渐进式落地 — 可先路径 B 验证，再逐步拆细为路径 A

---

## 四、推荐方案详细设计：包装器模式

### 4.1 整体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                    LangGraph 编排层 (Python)                      │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  StateGraph                                               │  │
│  │  START → guard_input → provide_prompt → cline_execute     │  │
│  │    → rule_match → guard_output → boundary_eval             │  │
│  │    → route_decision ──┬── Continue ──→ guard_input (环)   │  │
│  │                        ├── Done ──────→ END                │  │
│  │                        ├── Error(≥2) ─→ switch_agent       │  │
│  │                        └── Error(<2) ─→ retry_round        │  │
│  │                                                           │  │
│  │  可选: interrupt_before=["cline_execute"] (人机审批)       │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │ Checkpointer │  │ LangSmith    │  │ Model Router          │  │
│  │ (Postgres)    │  │ (tracing)    │  │ (per-node LLM)        │  │
│  └──────────────┘  └──────────────┘  └──────────────────────┘  │
└──────────────────────────┬──────────────────────────────────────┘
                           │ gRPC / REST
┌──────────────────────────┴──────────────────────────────────────┐
│                    C# 执行引擎 (.NET)                             │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐  │
│  │ GuardFunc  │  │ RuleEngine│  │ Boundary   │  │ ClinePool │  │
│  │ (gRPC svc) │  │ (gRPC svc)│  │ (gRPC svc)│  │ (gRPC svc)│  │
│  └────────────┘  └────────────┘  └────────────┘  └───────────┘  │
│  核心逻辑: 零LLM规则匹配 / 三维压力计算 / 双通道仲裁 / 守护进程池 │
└─────────────────────────────────────────────────────────────────┘
```

### 4.2 节点职责划分

| LangGraph 节点 | 实现位置 | 职责 | 是否调用 LLM |
|---|---|---|---|
| `guard_input` | C# gRPC | 输入安全模式匹配，过滤危险输入 | 否 |
| `provide_prompt` | Python LangChain | L0/L1/L2 提示词生成（模型无关） | 是 |
| `cline_execute` | C# gRPC | Cline 守护进程池执行子代理任务 | 是（通过 Cline） |
| `rule_match` | C# gRPC | 零 LLM 规则匹配 + 结构化信号优先 | 否 |
| `guard_output` | C# gRPC | 输出安全模式匹配（条件执行） | 否 |
| `boundary_eval` | C# gRPC | 双通道边界评估 + 仲裁 + 重构 | 否（反应式通道） |
| `route_decision` | Python 纯函数 | Route 条件边路由 | 否 |
| `interrupt_review` | Python LangGraph | 人机协作中断点 | 否 |
| `switch_agent` | Python LangGraph | Supervisor 多 Agent 路由 | 可选 |

### 4.3 状态定义

```python
from typing import TypedDict, Annotated, Optional
from langgraph.graph import add_messages

class AgentState(TypedDict):
    # 不可变输入
    user_input: str
    goal: Optional[str]
    constraints: Optional[str]
    
    # 流程控制
    round_count: int
    budget: int              # 对应 Token.Max
    budget_max: int          # 有界升级封顶 (默认6)
    decision: str            # done/error/retry/continue
    error_class: Optional[str]
    
    # 提示词分层
    prompt_l0: str           # Protocol 层
    prompt_l1: Optional[str] # Task Prompt
    prompt_l2: Optional[str] # Step Prompt
    
    # 执行结果
    cline_result: Optional[dict]
    cline_text: Optional[str]
    cline_error: Optional[str]
    cline_stage: Optional[str]
    
    # 边界评估
    boundary_assessment: Optional[dict]
    reconstruct_hint: Optional[str]   # compact/resubmit/rescope/escalate
    context_pressure: Optional[float]
    info_gain_pressure: Optional[float]
    complexity_pressure: Optional[float]
    
    # 空间边界
    effective_root: Optional[str]
    
    # 租约
    lease: Optional[dict]
    
    # 守卫
    filtered_input: Optional[str]
    
    # 人工审批
    human_approved: Optional[bool]
```

---

## 五、短板逐项解决方案

### 5.1 短板 1：无长期记忆/持久化 → Checkpointer

**问题**：当前 `RoundState` 仅存内存，进程死亡后状态丢失，不支持跨会话恢复。

**LangGraph 方案**：

```python
from langgraph.checkpoint.postgres import PostgresSaver

# 编译图时注入 checkpointer
checkpointer = PostgresSaver.from_conn_string(DATABASE_URL)
app = graph.compile(
    checkpointer=checkpointer,
    interrupt_before=["cline_execute"]  # 可选中断点
)

# 每次调用指定 thread_id
config = {"configurable": {"thread_id": f"agent-task-{task_id}"}}
result = await app.ainvoke(initial_state, config=config)

# 进程崩溃后恢复
state = await app.aget_state(config)
# state.next 指示下一个待执行节点
# 从最后 checkpoint 继续
```

**收益**：
- 每个 super-step（节点执行后）自动持久化
- 进程崩溃后从最后 checkpoint 恢复，不丢轮次状态
- **时间旅行调试**：回溯到任意历史状态，注入修正后重新执行
- 支持跨天长任务（interrupt 后等待人工审批，数小时/数天后恢复）

**Checkpointer 后端选择**：

| 后端 | 场景 | 本项目建议 |
|---|---|---|
| MemorySaver | 本地开发/测试 | ✅ 开发阶段 |
| SqliteSaver | 单进程部署 | ✅ 小规模生产 |
| PostgresSaver | 生产部署（ACID事务） | ✅ 推荐生产 |
| RedisSaver | 低延迟实时场景 | 可选（高频轮次场景） |

### 5.2 短板 2：LLM 供应商锁定 → 模型无关路由

**问题**：当前通过 `SemanticKernelFactory.CreateDeepSeekKernel()` 硬编码 DeepSeek。

**LangGraph 方案**：

```python
from langchain_openai import ChatOpenAI
from langchain_anthropic import ChatAnthropic
from langchain_google_genai import ChatGoogleGenerativeAI

# 按节点配置不同模型
models = {
    "task_prompt": ChatOpenAI(model="gpt-4o", temperature=0.7),
    "step_prompt": ChatAnthropic(model="claude-sonnet-4-20250514"),
    "fallback": ChatGoogleGenerativeAI(model="gemini-2.0-flash"),
}

def provide_prompt_node(state: AgentState) -> dict:
    # 首步生成 L1 task prompt
    if state["round_count"] == 0:
        model = models["task_prompt"]
        prompt = build_task_generation_prompt(state)
        l1 = model.invoke(prompt)
        return {"prompt_l1": l1.content}
    
    # 继续步生成 L2 step prompt
    needs_replan = call_csharp_rule_engine(state)  # 复用 C# RuleEngine
    model = models["step_prompt"] if needs_replan else models["fallback"]
    ...
```

**收益**：
- 提示词生成层完全模型无关
- 不同步骤可用不同模型（task prompt 用强模型，step prompt 用快模型）
- 可按成本/延迟/质量动态选择模型
- DeepSeek 仍可用（作为 ChatOpenAI 的兼容 API），但不再绑定

### 5.3 短板 3：无生态/社区 → LangGraph 生态

**问题**：内部框架，无社区、无插件、无可视化。

**LangGraph 方案**：

| 能力 | 工具 | 说明 |
|---|---|---|
| 全链路追踪 | LangSmith | 每个节点的输入/输出、延迟、token 使用量自动追踪 |
| 可视化调试 | LangGraph Studio | 图结构可视化、实时执行流、状态检查 |
| 时间旅行 | Checkpointer + LangSmith | 回溯历史状态，注入修正，重新执行 |
| 评估框架 | LangSmith Datasets | 构建测试数据集，自动评估 agent 质量 |
| 45+ 集成 | LangChain 生态 | 向量库（Pinecone/Chroma）、消息（Slack/Discord）、CRM 等 |
| 社区支持 | 126K+ Stars | 活跃社区、丰富文档、持续迭代 |

**配置方式**：
```python
import os
os.environ["LANGSMITH_TRACING"] = "true"
os.environ["LANGSMITH_API_KEY"] = "your-key"

# 所有图执行自动被追踪
# 在 LangSmith UI 查看完整调用链
```

### 5.4 短板 4：执行后端绑定 Cline → 可插拔工具

**问题**：执行仅通过 Cline 守护进程池，不如 LangGraph 的可插拔工具系统灵活。

**LangGraph 方案**：

Cline 不被替换，而是成为 LangGraph 工具生态中的一个后端选项：

```python
from langchain_core.tools import tool

@tool
def cline_execute(prompt: str, options: dict) -> dict:
    """通过 Cline 守护进程池执行子代理任务"""
    # 调用 C# gRPC 服务
    return cline_grpc_client.execute(prompt, options)

@tool
def mcp_tool_call(tool_name: str, arguments: dict) -> dict:
    """通过 MCP 协议调用外部工具"""
    return mcp_client.call_tool(tool_name, arguments)

@tool
def direct_api_call(url: str, method: str, body: dict) -> dict:
    """直接 HTTP API 调用"""
    ...

# 节点内可选择性使用不同工具
def cline_execute_node(state: AgentState) -> dict:
    result = cline_execute.invoke({
        "prompt": state["prompt_l2"],
        "options": {"timeout": state["budget"] * 60}
    })
    return {"cline_result": result, "cline_text": result["text"]}
```

**收益**：
- Cline 保留为默认执行后端（守护进程池优势不变）
- 可按需添加其他执行后端（直接 API、MCP、Docker 沙箱等）
- 工具切换通过 LangGraph 配置完成，不需要修改 C# 代码

### 5.5 短板 5：无人机协作原语 → interrupt()

**问题**：无 interrupt/approve 机制，高后果操作无法暂停等待人工确认。

**LangGraph 方案**：

```python
from langgraph.types import interrupt, Command

def cline_execute_node(state: AgentState) -> dict:
    # 高后果操作前暂停，等待人工审批
    if state.get("requires_approval"):
        decision = interrupt({
            "round": state["round_count"],
            "prompt": state["prompt_l2"],
            "question": "Approve cline execution?"
        })
        if not decision["approved"]:
            return {"decision": "done", "error_class": "user_cancelled"}
    
    # 审批通过后执行
    result = cline_grpc_client.execute(...)
    return {"cline_result": result}

# 恢复执行（人工审批后）
app.invoke(
    Command(resume={"approved": True}),
    config={"configurable": {"thread_id": task_id}}
)
```

**三种人机协作模式**：

| 模式 | 场景 | 实现 |
|---|---|---|
| Approve/Reject | 高后果操作前暂停审批 | `interrupt_before=["cline_execute"]` |
| Review & Edit | 人工审查并修改 agent 输出 | interrupt 后人工修改 state，resume |
| Collect Input | agent 需要额外信息 | interrupt 携带结构化提问，人工填写 |

**收益**：
- 合规场景（金融/医疗）的操作前审批
- 跨天审批工作流（interrupt 后状态持久化，数天后恢复）
- 不需要额外编码，interrupt 是 LangGraph 原生原语

### 5.6 短板 6：无测试/调试工具 → LangSmith

**问题**：仅有 `AgentLogger` 文本日志，无可视化追踪、无回放能力。

**LangGraph 方案**：

```python
# 自动追踪所有节点执行
# LangSmith UI 中可见：
#   - 每个节点的输入/输出完整 dump
#   - 每个节点的延迟和 token 使用量
#   - 状态在节点间的流转（state diff）
#   - 条件边的路由决策及原因
#   - 错误发生时的完整上下文

# 时间旅行调试
state = await app.aget_state(config, as_dict=True)
# 回溯到第 3 轮的状态
fork_config = {"configurable": {"thread_id": "debug-fork"}}
await app.aupdate_state(fork_config, {"prompt_l2": "modified prompt"})
# 从修改后的状态重新执行
result = await app.ainvoke(None, config=fork_config)

# 评估测试
from langsmith import Client
client = Client()
# 创建测试数据集
# 自动运行评估
# 对比不同模型/配置的结果质量
```

**收益**：
- 可视化全链路追踪（替代文本日志）
- 时间旅行调试（从任意历史状态分叉重执行）
- 自动化评估测试（构建测试数据集，CI/CD 集成）
- 生产监控（延迟、token 成本、错误率告警）

---

## 六、StateGraph 设计与伪代码

### 6.1 完整图定义

```python
from langgraph.graph import StateGraph, START, END
from langgraph.checkpoint.postgres import PostgresSaver
from langgraph.types import interrupt, Command
from typing import TypedDict, Optional

# === 状态定义 ===
class AgentState(TypedDict):
    user_input: str
    goal: Optional[str]
    constraints: Optional[str]
    round_count: int
    budget: int
    budget_max: int
    decision: str
    error_class: Optional[str]
    prompt_l0: str
    prompt_l1: Optional[str]
    prompt_l2: Optional[str]
    cline_result: Optional[dict]
    cline_text: Optional[str]
    cline_error: Optional[str]
    boundary_assessment: Optional[dict]
    reconstruct_hint: Optional[str]
    effective_root: Optional[str]
    lease: Optional[dict]
    filtered_input: Optional[str]
    human_approved: Optional[bool]

# === C# 服务桥接 ===
import grpc
from agent_pb2 import (
    GuardRequest, GuardResponse,
    RuleMatchRequest, RuleMatchResponse,
    BoundaryEvalRequest, BoundaryEvalResponse,
    ClineExecuteRequest, ClineExecuteResponse
)
import agent_pb2_grpc

channel = grpc.aio.insecure_channel("localhost:50051")
guard_stub = agent_pb2_grpc.GuardServiceStub(channel)
rule_stub = agent_pb2_grpc.RuleEngineServiceStub(channel)
boundary_stub = agent_pb2_grpc.BoundaryServiceStub(channel)
cline_stub = agent_pb2_grpc.ClineExecutorServiceStub(channel)

# === 节点定义 ===

async def guard_input_node(state: AgentState) -> dict:
    """调用 C# GuardFunction.CheckInputSafetyAsync"""
    resp: GuardResponse = await guard_stub.CheckInputSafety(
        GuardRequest(input=state["user_input"])
    )
    return {"filtered_input": resp.filtered_input if resp.filtered else state["user_input"]}

async def provide_prompt_node(state: AgentState) -> dict:
    """LangGraph 内生成 L1/L2 提示词（模型无关）"""
    user_input = state.get("filtered_input", state["user_input"])
    
    if state["round_count"] == 0:
        # 首步：生成 L1 task prompt
        l1 = await generate_task_prompt(user_input, state)
        return {"prompt_l1": l1, "prompt_l2": build_l2_continue(state)}
    else:
        # 继续步：复用 C# RuleEngine 判定 needsReplan
        needs_replan = await call_csharp_needs_replan(state)
        if needs_replan:
            l2 = await generate_step_prompt(state)
            return {"prompt_l2": l2}
        else:
            return {"prompt_l2": build_l2_continue(state)}

async def cline_execute_node(state: AgentState) -> dict:
    """调用 C# ClineAgentExecutor，可选人工审批"""
    # 高后果操作前暂停
    if state.get("requires_approval") and state.get("human_approved") is None:
        decision = interrupt({
            "round": state["round_count"],
            "prompt": state["prompt_l2"],
            "question": "Approve cline execution?"
        })
        if not decision["approved"]:
            return {"decision": "done", "error_class": "user_cancelled"}
    
    # 调用 C# Cline 守护进程池
    resp: ClineExecuteResponse = await cline_stub.Execute(
        ClineExecuteRequest(
            prompt=assemble_prompt(state),
            budget=state["budget"],
            effective_root=state.get("effective_root", ""),
        )
    )
    return {
        "cline_text": resp.text,
        "cline_error": resp.error,
        "cline_stage": resp.stage,
        "cline_result": {"ok": resp.ok, "text": resp.text},
    }

async def rule_match_node(state: AgentState) -> dict:
    """调用 C# RuleEngine（零 LLM 决策）"""
    resp: RuleMatchResponse = await rule_stub.Evaluate(
        RuleMatchRequest(
            cline_text=state.get("cline_text", ""),
            cline_result=str(state.get("cline_result", {})),
            error=state.get("cline_error", ""),
        )
    )
    return {"decision": resp.decision, "error_class": resp.error_class}

async def guard_output_node(state: AgentState) -> dict:
    """调用 C# GuardFunction.CheckOutputSafetyAsync（条件执行）"""
    if state["decision"] != "done":
        return {}  # 仅成功时执行
    resp: GuardResponse = await guard_stub.CheckOutputSafety(
        GuardRequest(input=state.get("cline_text", ""))
    )
    return {}  # 输出安全检查结果

async def boundary_eval_node(state: AgentState) -> dict:
    """调用 C# DefaultBoundaryFramework.PostRoundAsync"""
    resp: BoundaryEvalResponse = await boundary_stub.Evaluate(
        BoundaryEvalRequest(
            cline_text=state.get("cline_text", ""),
            cline_error=state.get("cline_error", ""),
            round_count=state["round_count"],
            budget=state["budget"],
            effective_root=state.get("effective_root", ""),
        )
    )
    updates = {"boundary_assessment": resp.assessment}
    if resp.reconstruct_hint:
        updates["reconstruct_hint"] = resp.reconstruct_hint
        if resp.reconstruct_hint == "escalate":
            updates["budget"] = min(state["budget_max"], state["budget"] + 1)
    return updates

# === 条件边 ===
def route_after_match(state: AgentState) -> str:
    """Route 决策路由（纯函数，零 LLM）"""
    decision = state["decision"]
    budget = state["budget"]
    
    if decision == "done":
        return END
    elif decision == "error":
        if budget >= 2:
            return "switch_agent"
        else:
            return "retry_round"
    elif decision == "retry":
        return "guard_input"  # 循环回边
    else:
        return "guard_input"  # continue

async def switch_agent_node(state: AgentState) -> dict:
    """Supervisor 模式：切换到不同子代理"""
    # 可选：路由到不同专家子图
    return {"decision": "continue", "budget": state["budget"] - 1}

async def retry_round_node(state: AgentState) -> dict:
    """重试当前轮次"""
    return {"decision": "continue"}

# === 图组装 ===
workflow = StateGraph(AgentState)

workflow.add_node("guard_input", guard_input_node)
workflow.add_node("provide_prompt", provide_prompt_node)
workflow.add_node("cline_execute", cline_execute_node)
workflow.add_node("rule_match", rule_match_node)
workflow.add_node("guard_output", guard_output_node)
workflow.add_node("boundary_eval", boundary_eval_node)
workflow.add_node("switch_agent", switch_agent_node)
workflow.add_node("retry_round", retry_round_node)

workflow.add_edge(START, "guard_input")
workflow.add_edge("guard_input", "provide_prompt")
workflow.add_edge("provide_prompt", "cline_execute")
workflow.add_edge("cline_execute", "rule_match")
workflow.add_edge("rule_match", "guard_output")
workflow.add_edge("guard_output", "boundary_eval")
workflow.add_edge("boundary_eval", "route_after_match")  # route_after_match 是条件边

workflow.add_conditional_edges(
    "boundary_eval",
    route_after_match,
    {
        "guard_input": "guard_input",   # Continue / Retry → 循环
        END: END,                       # Done → 终止
        "switch_agent": "switch_agent", # Error(budget>=2) → 切换
        "retry_round": "retry_round",   # Error(budget<2) → 重试
    }
)

workflow.add_edge("switch_agent", "guard_input")   # 切换后重新开始
workflow.add_edge("retry_round", "guard_input")     # 重试后重新开始

# === 编译 ===
checkpointer = PostgresSaver.from_conn_string(DATABASE_URL)
app = workflow.compile(
    checkpointer=checkpointer,
    interrupt_before=["cline_execute"],  # 可选：cline 执行前人工审批
    recursion_limit=50,                  # 最大轮次（替代有界升级硬上限）
)

# === 执行 ===
config = {"configurable": {"thread_id": f"task-{task_id}"}}
result = await app.ainvoke(
    {
        "user_input": user_input,
        "goal": goal,
        "constraints": constraints,
        "round_count": 0,
        "budget": 3,
        "budget_max": 6,
        "decision": "continue",
        "prompt_l0": PROMPT_CATALOG_PROTOCOL,
    },
    config=config,
)
```

### 6.2 与当前代码的对应关系

| LangGraph 节点 | 当前 C# 对应 | 交互方式 |
|---|---|---|
| `guard_input_node` | `GuardFunction.ExecuteInputGuardAsync` | gRPC 调用 C# 服务 |
| `provide_prompt_node` | `PromptFrameworkFunction.ExecuteProvidePromptAsync` | Python 内实现（模型无关） |
| `cline_execute_node` | `ClineExecutorFunction.ExecuteClineAsync` | gRPC 调用 C# Cline 服务 |
| `rule_match_node` | `RuleMatchFunction.ExecuteRuleMatchAsync` | gRPC 调用 C# RuleEngine 服务 |
| `guard_output_node` | `GuardFunction.ExecuteOutputGuardAsync` | gRPC 调用 C# 服务 |
| `boundary_eval_node` | `DefaultBoundaryFramework.PostRoundAsync` | gRPC 调用 C# 服务 |
| `route_after_match` | `AgentRunner.Route()` | Python 纯函数 |
| `switch_agent_node` | `Route(Error, budget>=2)` | Python + LangGraph Supervisor |
| Checkpointer | `Agent.State`（当前为空） | LangGraph PostgresSaver |

---

## 七、多 Agent 协作扩展

当前项目的 `SwitchAgent` 路由（Error + budget>=2）在 LangGraph 中可扩展为真正的多 Agent Supervisor 模式。

### 7.1 Supervisor 模式

```python
def supervisor_node(state: AgentState) -> dict:
    """中心协调者：根据任务类型路由到不同专家子图"""
    task_type = classify_task(state["user_input"])
    
    agents = {
        "coding": "coder_agent",
        "research": "researcher_agent",
        "review": "reviewer_agent",
    }
    
    next_agent = agents.get(task_type, "coder_agent")
    return {"next": next_agent}

# 构建多 Agent 图
multi_agent = StateGraph(AgentState)
multi_agent.add_node("supervisor", supervisor_node)
multi_agent.add_node("coder_agent", coder_subgraph)      # 子图
multi_agent.add_node("researcher_agent", research_subgraph)
multi_agent.add_node("reviewer_agent", review_subgraph)

multi_agent.add_conditional_edges(
    "supervisor",
    lambda s: s["next"],
    {
        "coder_agent": "coder_agent",
        "researcher_agent": "researcher_agent",
        "reviewer_agent": "reviewer_agent",
        "FINISH": END,
    }
)

# 每个子 Agent 完成后回到 supervisor
for agent in ["coder_agent", "researcher_agent", "reviewer_agent"]:
    multi_agent.add_edge(agent, "supervisor")
```

### 7.2 与当前 SwitchAgent 的关系

| 当前 | LangGraph 扩展 |
|---|---|
| `Route(Error, budget>=2) → SwitchAgent` | Supervisor 根据错误类型选择不同专家子图 |
| 固定切换到同一 Cline Worker | 可切换到不同配置的子图（不同模型/工具/提示词） |
| 无并行能力 | LangGraph 支持 Fan-out/Fan-in 并行子图 |

---

## 八、跨语言桥接方案

### 8.1 gRPC 服务定义

C# 侧需要暴露以下 gRPC 服务：

```protobuf
// agent.proto
syntax = "proto3";

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
```

### 8.2 C# 侧实现（暴露现有逻辑为 gRPC）

C# 侧不需要修改核心逻辑，只需添加 gRPC 服务层包装现有方法：

```csharp
// 现有 GuardFunction 逻辑不变，仅添加 gRPC 包装
public class GuardGrpcService : GuardService.GuardServiceBase
{
    public override Task<GuardResponse> CheckInputSafety(
        GuardRequest request, ServerCallContext context)
    {
        // 调用现有 GuardFunction.ExecuteInputGuardAsync
        var result = GuardFunction.CheckInputSafetyAsync(request.Input);
        return Task.FromResult(new GuardResponse
        {
            Filtered = result.Filtered,
            FilteredInput = result.FilteredInput
        });
    }
}
```

### 8.3 性能考量

| 调用方式 | 延迟 | 适用场景 |
|---|---|---|
| 进程内调用（当前） | ~0ms | 单体部署 |
| gRPC 本地 | ~0.1-0.5ms | 推荐：同机部署 |
| gRPC 远程 | ~1-5ms | 分布式部署 |
| REST/HTTP | ~0.5-2ms | 备选方案 |

> **关键洞察**：gRPC 跨语言开销（~0.5ms/节点）相对 LLM 调用延迟（秒级）完全可忽略。5 个节点 × 0.5ms = 2.5ms 额外开销，而单次 LLM 调用通常 1-10 秒。

### 8.4 替代方案：进程内 Python 重写

如果不想引入跨语言调用，可将 C# 核心组件用 Python 重写：

| 组件 | 重写复杂度 | 说明 |
|---|---|---|
| GuardFunction | 低 | 模式匹配，纯字符串处理 |
| RuleEngine | 低 | 规则匹配，无外部依赖 |
| BoundaryPressureCalculator | 中 | 三维计算 + EWMA 平滑 |
| BoundaryTriggerArbiter | 低 | 合并逻辑 |
| ClineDaemonPool | 高 | IPC + 进程管理，复杂度高 |

> **建议**：GuardFunction、RuleEngine、BoundaryFramework 用 Python 重写（低复杂度），Cline 守护进程池保留 C# 并通过 gRPC 调用（高复杂度，保留 C# 优势）。

---

## 九、实施路线图

### 阶段 0：验证（1-2 周）

- [ ] 搭建 Python + LangGraph 开发环境
- [ ] 定义 `AgentState` TypedDict
- [ ] 用 MemorySaver 构建最小 StateGraph（2 个节点：provide_prompt + rule_match）
- [ ] 验证循环边和条件边工作正常
- [ ] 接入 LangSmith 追踪，验证可观测性

### 阶段 1：路径 B 验证（2-3 周）

- [ ] C# 侧暴露 `AgentOrchestrator.RunAsyncCore` 为 gRPC 服务（单轮执行）
- [ ] Python 侧构建外层 StateGraph：execute_round → checkpoint → route
- [ ] 接入 PostgresSaver checkpointer
- [ ] 验证崩溃恢复和跨会话状态恢复
- [ ] 添加 interrupt_before 验证人机协作

### 阶段 2：路径 A 细化（3-4 周）

- [ ] 将 C# 的 5 个管道步骤分别暴露为 gRPC 服务
- [ ] Python 侧构建完整 StateGraph（5 节点 + 条件边）
- [ ] 迁移 provide_prompt 到 Python（模型无关）
- [ ] 对接 LangSmith 完整追踪
- [ ] 性能测试和优化

### 阶段 3：多 Agent 扩展（2-3 周）

- [ ] 实现 Supervisor 节点
- [ ] 定义多个专家子图（coder/researcher/reviewer）
- [ ] 对接 SwitchAgent 路由
- [ ] 测试 Fan-out/Fan-in 并行执行

### 阶段 4：生产化（2-3 周）

- [ ] 部署 PostgresSaver 生产实例
- [ ] 配置 LangSmith 生产监控
- [ ] 编写运维文档
- [ ] 压力测试和故障恢复演练

---

## 十、风险与缓解

| 风险 | 概率 | 影响 | 缓解措施 |
|---|---|---|---|
| 跨语言调用增加延迟 | 低 | 低 | gRPC 本地 ~0.5ms，相对 LLM 秒级延迟可忽略 |
| Python+C# 双进程运维复杂 | 中 | 中 | Docker Compose 统一编排，健康检查 + 自动重启 |
| LangGraph 版本迭代 breaking changes | 中 | 中 | 锁定 langgraph>=1.0,<2.0（1.0 LTS 承诺 API 稳定） |
| 重写 provide_prompt 引入 bug | 中 | 中 | 先用 C# gRPC 验证，Python 侧逐步替换 + 对比测试 |
| Checkpointer 数据增长 | 低 | 低 | Postgres 定期清理 + 设置 checkpoint 保留策略 |
| 双语言团队技能要求 | 中 | 中 | C# 核心逻辑不变，Python 侧门槛低，LangGraph 文档丰富 |
| LangSmith 追踪成本 | 低 | 低 | 开发期免费 5K traces/月，生产期按需付费 |

---

## 十一、保留与增强矩阵

### 保留不变（C# 核心优势）

| 能力 | 当前实现 | 集成后 | 说明 |
|---|---|---|---|
| 零 LLM 规则引擎 | `RuleEngine.Evaluate` | C# gRPC 服务 | 决策逻辑不变，Python 通过条件边调用 |
| 双通道边界评估 | `DefaultBoundaryFramework` | C# gRPC 服务 | 三维压力 + 迟滞 + 仲裁逻辑不变 |
| 有界升级 | `Math.Min(6, max+1)` | `recursion_limit` + state 字段 | 硬封顶逻辑保留 |
| 管道内联守卫 | `GuardFunction` | C# gRPC 服务 | 输入/输出安全检查不变 |
| Cline 守护进程池 | `ClineDaemonPool` | C# gRPC 服务 | JSON-Line IPC + 多 Worker 并行不变 |
| HFSM 租约链 | `LeaseDeriver` | State 派生 | 租约逻辑保留，从 state 派生 |
| 提示词分层 L0/L1/L2 | `PromptCatalog` | Python 重新实现 | 协议层固定，L1/L2 生成迁移到 Python |

### 新增增强（LangGraph 补位）

| 能力 | LangGraph 实现 | 解决的短板 |
|---|---|---|
| 状态持久化 | PostgresSaver Checkpointer | 短板 1：无长期记忆 |
| 崩溃恢复 | Checkpoint + thread_id 恢复 | 短板 1：无持久化 |
| 时间旅行调试 | Checkpointer + LangSmith | 短板 6：无调试工具 |
| 人机协作 | interrupt() + Command(resume) | 短板 5：无人机协作 |
| 跨天审批 | interrupt 后状态持久化 | 短板 5：无审批工作流 |
| 模型无关 | LangChain ChatModel 抽象 | 短板 2：供应商锁定 |
| 按节点模型路由 | 不同节点配置不同 LLM | 短板 2：灵活性 |
| 可插拔工具 | @tool 装饰器 + MCP 适配 | 短板 4：执行后端绑定 |
| 多 Agent 协作 | Supervisor + 子图 | 短板 4：单一 Agent |
| 全链路追踪 | LangSmith 自动追踪 | 短板 6：无观测性 |
| 评估测试 | LangSmith Datasets | 短板 6：无测试工具 |
| 生态集成 | 45+ LangChain 集成 | 短板 3：无生态 |

### 从图执行视角的统一调度

当前项目的"调度"分散在多个组件中：
- `AxiomLoopEngine` 控制 while 循环
- `RoundLifecycle` 控制五步执行
- `StaticAsyncPipeline` 控制管道顺序
- `Route()` 控制路由
- `DefaultBoundaryFramework` 控制边界

引入 LangGraph 后，**调度统一到 StateGraph 一个地方**：
- 图结构定义所有节点和边（替代分散的循环/管道/路由）
- 条件边统一处理路由决策
- Checkpointer 统一处理状态持久化
- interrupt 统一处理人机协作
- LangSmith 统一处理可观测性

> **核心价值**：不是替换 C# 的确定性引擎，而是用 LangGraph 的图编排能力**统一调度**，同时保留 C# 的执行优势。调度（what/when/why）由 LangGraph 管，执行（how）由 C# 管。

---

> **说明**：本分析基于本项目源码的只读分析（约 100 个 .cs 文件）、LangGraph v1.0+ 官方文档和 2026 年 8 月公开技术资料综合设计。分析未修改任何代码和逻辑，为纯架构设计方案。
