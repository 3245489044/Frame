# 中央思考调度模块设计：提示词分级 + 规则匹配循环

> 日期：2026-08-07
> 状态：设计阶段（当前阶段精简版，待审阅）
> 参考：
> - [从"省着用"到"循环用"：Agent Token 成本优化的逻辑重构与工程全景](../docs/KnowLedge/从“省着用”到“循环用”：Agent%20Token成本优化的逻辑重构与工程全景.md)
> - [契约式脚本生成：Agent 工具复用的结构化设计理念](../docs/KnowLedge/契约式脚本生成：Agent工具复用的结构化设计理念.md)

## 摘要

在现有声明式 ReAct Agent 基础上，**不新建模块**，把"中央思考调度"能力注入现有 `Agent.Planner`，把"CLine 执行端"能力注入现有 `Agent.ClineUsage`，并增强现有 `agent-loop.json` 主循环。

**当前阶段只做两个核心功能**（模块本质是"调度"）：

1. **提示词分级框架** — 规则与生成相结合，把提示词分为**系统提示词**与**多级提示词**（任务级/步骤级），为 CLine 提供执行指令。
2. **规则匹配循环** — 根据上一步结果，用规则判定下一步是否调用，而非 LLM 判定或无限循环。

CLine 作为执行端被调度（复用既有 `ClineClient`）。现有 `sk_plan` + `dynamic_workflow_executor`（MCP）路径保留为兜底分支。计划缓存库、契约四维显式化、两层检索等文章2 的复用体系推迟到 **v2 未来阶段**。

**核心收益**：循环控制零 LLM 成本（规则匹配替代 `condition_check`）；系统提示词固化命中前缀缓存；多级提示词只在必要时调用 SK 生成。

---

## 一、架构

### 1.1 总体架构（增强现有循环）

不新建项目。两个核心调度函数落在 `Agent.Planner`，CLine 执行落在 `Agent.ClineUsage`，增强现有 `agent-loop.json`：

```
                          ┌─────────────────────────────────────────────┐
                          │          WorkFlowMethod 引擎（既有）          │
                          │     读取增强后的 agent-loop.json 声明式编排     │
                          └───────────────────────┬─────────────────────┘
                                                  │
        ┌─────────────────────────────────────────┴────────────────────────────────┐
        │                       agent-loop.json（增强后，当前阶段）                  │
        │                                                                            │
  start → guard_input → provide_prompt ──首步/继续──┐                               │
        │                  │                          │                               │
        │                  ▼                          │                               │
        │            cline_execute  ←─────────────────┘                               │
        │                  │                                                          │
        │                  ▼                                                          │
        │              rule_match  ──continue──→ provide_prompt（下一步，带 hint）   │
        │                  │                                                          │
        │                  ├─ stop / error ──→ guard_output → end                     │
        │                  │                                                          │
        │  ── 兜底（CLine 不可用）──→ sk_plan(既有) → check_complete → execute_dynamic │
        └────────────────────────────────────────────────────────────────────────────┘

        两个核心调度函数（Agent.Planner）：
        ┌──────────────────────────────────────────┐
        │ ① provide_prompt  提示词分级框架            │
        │     L0 系统提示词（固定，规则，命中前缀缓存）│
        │     L1 任务级提示词（规则模板 + SK 生成）   │
        │     L2 步骤级提示词（规则模板为主，SK 兜底）│
        │ ② rule_match      规则匹配循环              │
        │     规则集评估上一步结果 → continue/stop/error│
        │     零 LLM，确定性，内置 max_steps 守卫       │
        └──────────────────────────────────────────┘

        执行端（Agent.ClineUsage，复用）：
        ┌──────────────────────────────────────────┐
        │ cline_execute  ClineClient.AskAsync       │
        │ result_validate（v2，当前阶段并入 rule_match）│
        └──────────────────────────────────────────┘
```

### 1.2 模块职责分布（核心变更）

| 模块 | 既有职责 | 当前阶段新增 | 改动 |
|------|---------|------------|------|
| **Agent.Planner** | `sk_plan`/`sk_chat`（SK 规划） | `provide_prompt`（分级框架）+ `rule_match`（规则循环）+ 提示词模板/规则集 | 新增文件，无新依赖 |
| **Agent.ClineUsage** | `ClineClient`（纯客户端库） | `cline_execute` 注册为工作流函数 | 新增 Agent.Core 引用 + 1 个文件 |
| **Agent.Core** | `AgentModuleRegistrar` | 新增 `TryRegisterClineUsageModule` 反射注册 | 仅改注册器 |
| **Config** | `agent-loop.json` 等 | `agent-loop.json` 增节点；`Config/prompts/`（系统提示词）；`Config/rules/`（规则集） | 增配置 |

### 1.3 与现有 ReAct 的关系（零破坏）

- **不修改** `SKPlannerFunction` 既有 `sk_plan`/`sk_chat`，保留为兜底分支。
- **不修改** `DynamicWorkflowMerger`/`dynamic_workflow_executor`（MCP 路径），CLine 不可用时降级。
- **增强** `agent-loop.json`：在 `guard_input` 后插入 `provide_prompt → cline_execute → rule_match` 主路径；`sk_plan` 路径降为兜底。
- `AgentModuleRegistrar` 沿用既有反射 `TryRegister*` 模式，新增 `TryRegisterClineUsageModule`，不可用仅告警不崩溃。

### 1.4 设计原则映射（来自两篇文章）

| 文章原则 | 当前阶段落地点 |
|---------|--------------|
| 轮次＞输出＞输入 | `rule_match` 替代 LLM `condition_check`，循环控制零 LLM 成本 |
| System Prompt 固化与缓存 | L0 系统提示词固定，命中 API 自动前缀缓存（0.1× 输入成本） |
| 数据与逻辑分离 | L0（逻辑固化）+ L1/L2（数据语义生成）分层 |
| 最大重试阈值 | `rule_match` 内置 `max_steps` 守卫，避免无限循环 |
| 规划-执行分离 | `provide_prompt`（规划/调度）+ `cline_execute`（执行）解耦 |
| 指针化（v2） | 当前阶段提示词内联传递；v2 升级为契约文件+指针 |
| 生成前检索正反馈（v2） | 当前阶段不做；v2 引入计划缓存库 |

---

## 二、核心功能一：提示词分级框架（`provide_prompt`）

### 2.1 分级结构

提示词分**三级**，规则与生成结合：

| 级别 | 名称 | 来源 | 频率 | 成本 |
|------|------|------|------|------|
| **L0** | 系统提示词 | 规则（固定文件） | 进程级常量 | 0（命中前缀缓存） |
| **L1** | 任务级提示词 | 规则模板 + SK 生成 | 每任务 1 次 | 1 次 SK 调用/任务 |
| **L2** | 步骤级提示词 | 规则模板为主，SK 兜底 | 每步骤 1 次 | 0（模板）或 1（兜底） |

**最终 prompt = L0 + L1 + L2**，拼接后传给 `cline_execute`。

### 2.2 L0 系统提示词（规则，固定）

定义 Agent 角色、全局约束、输出格式、安全规则。存为 `Config/prompts/system.prompt.md`，进程启动加载一次。

要点（呼应文章1"控制输出"）：
- 末尾死命令："禁止开场白/结束语/解释过程，只输出最终结构化结果"。
- 输出格式约束（JSON/CSV，截断规则）。
- 环境与权限声明。

### 2.3 L1 任务级提示词（规则模板 + SK 生成）

一次任务生成一次，结合 `user_input` 与 L0 约束。

- **规则模板部分**：任务分类槽位、目标槽位、约束槽位（规则填充，不调 LLM）。
- **SK 生成部分**：任务意图解析、目标细化（调 SK 1 次/任务）。

输入：`user_input`
输出：`task_prompt`（结构化任务指引）

### 2.4 L2 步骤级提示词（规则模板为主，SK 兜底）

每步生成，基于上一步 `exec_result` + `rule_match` 的 `next_prompt_hint`。

- **首步**：L2 = L1（无上一步结果）。
- **继续步**：规则模板根据 `next_prompt_hint` 选模板填槽（默认零 LLM）；仅当规则判定"需要语义重规划"时调 SK 兜底生成。

输入：`exec_result`（上一步）, `next_prompt_hint`（来自 rule_match）
输出：`step_prompt`

### 2.5 规则与生成的结合机制

```
provide_prompt(user_input, exec_result?, next_prompt_hint?):
    L0 = load("Config/prompts/system.prompt.md")        # 规则，固定
    if 首步 (exec_result == null):
        L1 = SK.generate_task_prompt(user_input, L0)     # 规则模板 + SK 生成
        L2 = L1
    else:
        L2 = rule_template.fill(exec_result, next_prompt_hint)  # 规则模板为主
        if rule_needs_replan(exec_result):               # 规则判定
            L2 = SK.generate_step_prompt(exec_result, hint, L0)  # SK 兜底
    return L0 + L1 + L2
```

规则集 `Config/prompts/templates.json` 定义：模板文本、槽位、填充规则、`rule_needs_replan` 判定条件。

---

## 三、核心功能二：规则匹配循环（`rule_match`）

### 3.1 设计目标

替代既有 LLM 驱动的 `condition_check`：根据上一步 `exec_result`，用**确定性规则**判定下一步动作，零 LLM 成本，杜绝无限循环。

### 3.2 规则集结构（`Config/rules/loop-rules.json`）

有序规则列表，首匹配胜出：

```json
{
  "rules": [
    {
      "name": "exit_error",
      "condition": { "field": "exec_result.ok", "op": "==", "value": false },
      "action": "error",
      "reason": "CLine 执行失败"
    },
    {
      "name": "done_marker",
      "condition": { "field": "exec_result.text", "op": "contains_any", "value": ["完成", "DONE", "任务结束", "已处理"] },
      "action": "stop",
      "reason": "检测到完成标记"
    },
    {
      "name": "need_more",
      "condition": { "field": "exec_result.text", "op": "contains_any", "value": ["需要", "下一步", "继续", "待处理"] },
      "action": "continue",
      "next_prompt_hint": "根据上一步结果继续执行"
    },
    {
      "name": "max_steps",
      "condition": { "field": "step_count", "op": ">=", "value": 10 },
      "action": "stop",
      "reason": "达到最大步数"
    },
    {
      "name": "default",
      "condition": "true",
      "action": "stop",
      "reason": "默认终止"
    }
  ],
  "guards": { "max_steps": 10, "timeout_sec": 300 }
}
```

### 3.3 规则评估引擎

`rule_match` 函数职责：

1. 读取 `exec_result` + `step_count`。
2. 按 `rules` 顺序评估 `condition`（支持 `==`/`!=`/`contains_any`/`>=`/`true`）。
3. 首匹配胜出，返回 `action` + `next_prompt_hint` + `reason`。
4. 内置守卫：`max_steps`、`timeout_sec`、用户取消检查（复用 `AgentLoopFunction.CheckCancellation`）。

输出：

```json
{ "action": "continue|stop|error", "next_prompt_hint": "...", "reason": "...", "step_count": 3 }
```

### 3.4 循环控制语义

| `action` | 工作流路由 | 含义 |
|----------|-----------|------|
| `continue` | → `provide_prompt`（带 hint） | 规则判定需继续，生成 L2 步骤提示词再执行 |
| `stop` | → `guard_output` → end | 任务完成，正常结束 |
| `error` | → `sk_plan`（兜底） | 执行失败，降级到既有 MCP 路径或错误处理 |

**关键**：`continue` 由规则判定而非 LLM，杜绝"LLM 总是判定未完成"导致的无限循环（文章1"最大重试阈值"）。

---

## 四、组件

### 4.1 文件结构（分布到现有模块）

```
Agent/src/Agent.Planner/                  # 增强 — 调度核心（两个核心功能）
├── SKPlannerFunction.cs                  # 既有，不改（sk_plan/sk_chat 兜底）
├── SemanticKernelFactory.cs              # 既有，复用
├── PromptFrameworkFunction.cs            # 新增：[FunctionLibrary] 注册 provide_prompt
├── PromptCatalog.cs                      # 新增：L0 加载 + L1/L2 模板管理
├── RuleMatchFunction.cs                  # 新增：[FunctionLibrary] 注册 rule_match
└── RuleEngine.cs                         # 新增：规则集评估引擎

Agent/src/Agent.ClineUsage/               # 增强 — 执行端
├── ClineClient.cs                        # 既有，复用 AskAsync
├── ClineModels.cs                        # 既有，复用 ClineRunResult
└── ClineExecutorFunction.cs              # 新增：[FunctionLibrary] 注册 cline_execute

Agent/src/Agent.Core/
└── AgentModuleRegistrar.cs               # 改：新增 TryRegisterClineUsageModule

Agent/Config/
├── prompts/
│   ├── system.prompt.md                  # L0 系统提示词（规则，固定）
│   └── templates.json                   # L1/L2 模板与填充规则
└── rules/
    └── loop-rules.json                   # 规则匹配循环规则集
```

### 4.2 注册的函数（沿用 `[FunctionLibrary]` 模式）

| 函数名 | 所属模块 | 职责 | 输入 | 输出 |
|--------|---------|------|------|------|
| `provide_prompt` | Agent.Planner | **核心①** 提示词分级框架 | `user_input`, `exec_result?`, `next_prompt_hint?` | `prompt` |
| `rule_match` | Agent.Planner | **核心②** 规则匹配循环 | `exec_result`, `step_count` | `action`, `next_prompt_hint`, `reason` |
| `cline_execute` | Agent.ClineUsage | CLine 执行（被调度） | `prompt` | `exec_result` |

### 4.3 组件依赖关系

```
Agent.ClineUsage（增强后）
  └── Agent.Core（新增引用，带来 WorkFlowMethod + AgentLogger）

Agent.Planner（增强后，无新依赖）
  ├── Agent.Core（既有）
  └── Microsoft.SemanticKernel（既有）
```

`Agent.ClineUsage` → `Agent.Core` 不构成循环。`Agent.Planner` 不依赖 `Agent.ClineUsage`（`provide_prompt` 产 prompt，`cline_execute` 消费 prompt，通过工作流参数传递，不跨模块直接调用）。

---

## 五、数据流

### 5.1 端到端数据流（首步 → 继续 → 完成）

```
user_input="把 src 模块所有中文注释翻译成英文"
    │
    ▼
[provide_prompt]  (Agent.Planner)  首步
    L0 = system.prompt.md（固定，命中前缀缓存）
    L1 = SK 生成任务级提示词（1 次 LLM）
    L2 = L1（首步无上一步）
    → prompt = L0 + L1 + L2
    │  (1 次 LLM 调用)
    ▼
[cline_execute]  (Agent.ClineUsage)
    ClineClient.AskAsync(prompt, auto_approve=true)
    → exec_result = { ok:true, text:"已翻译 12 个文件，需要继续处理测试目录..." }
    │  (1 次 LLM 调用 — CLine 内部)
    ▼
[rule_match]  (Agent.Planner)  零 LLM
    评估：text contains "需要继续" → 命中 need_more
    → action=continue, next_prompt_hint="继续处理测试目录"
    │  (0 次 LLM 调用)
    ▼
[provide_prompt]  继续步
    L0 = system.prompt.md（缓存）
    L1 = 复用首步任务级提示词
    L2 = 规则模板填槽（hint → "继续处理测试目录"）  # 默认零 LLM
    → prompt = L0 + L1 + L2
    ▼
[cline_execute] → exec_result = { ok:true, text:"测试目录已处理，完成" }
    ▼
[rule_match]  零 LLM
    text contains "完成" → 命中 done_marker
    → action=stop
    ▼
guard_output → end
```

**总 LLM 调用：3 次**（1 首步规划 + 2 CLine 执行），`rule_match` 全程零 LLM。

### 5.2 工作流定义 `agent-loop.json`（增强后核心节点）

在现有 `agent-loop.json` 的 `guard_input` 与 `sk_plan` 之间插入 CLine 主路径，`sk_plan` 降为兜底：

```json
{
  "WorkflowId": "agent-loop",
  "Version": "3.1.0",
  "Nodes": [
    { "NodeId": "start", "FuncName": "agent_loop_entry",
      "ParameterKeys": ["user_input"], "ResultKeys": ["safe_input"],
      "NextNodeConfig": { "Configs": { "on_success": "guard_input" } } },
    { "NodeId": "guard_input", "FuncName": "guard_input",
      "NextNodeConfig": { "Configs": { "on_success": "provide_prompt", "on_blocked": "end" } } },

    { "NodeId": "provide_prompt", "FuncName": "provide_prompt",
      "ParameterKeys": ["user_input", "exec_result", "next_prompt_hint"],
      "ResultKeys": ["prompt"],
      "NextNodeConfig": { "Configs": { "on_success": "cline_execute", "on_error": "sk_plan" } } },
    { "NodeId": "cline_execute", "FuncName": "cline_execute",
      "ParameterKeys": ["prompt"], "ResultKeys": ["exec_result"],
      "NextNodeConfig": { "Configs": { "on_success": "rule_match", "on_error": "sk_plan" } } },
    { "NodeId": "rule_match", "FuncName": "rule_match",
      "ParameterKeys": ["exec_result", "step_count"], "ResultKeys": ["action", "next_prompt_hint"],
      "NextNodeConfig": { "Configs": {
          "continue": "provide_prompt",
          "stop": "guard_output",
          "error": "sk_plan"
      } } },

    { "NodeId": "sk_plan", "FuncName": "sk_plan",
      "ParameterKeys": ["input", "history"], "ResultKeys": ["plan_result"],
      "NextNodeConfig": { "Configs": { "on_success": "check_complete" } } },
    { "NodeId": "check_complete", "FuncName": "condition_check",
      "ParameterKeys": ["plan_result"], "ResultKeys": ["decision"],
      "NextNodeConfig": { "Configs": { "complete": "guard_output", "continue": "execute_dynamic" } } },
    { "NodeId": "execute_dynamic", "FuncName": "dynamic_workflow_executor",
      "ParameterKeys": ["plan_result", "history", "user_input"],
      "ResultKeys": ["exec_result", "updated_history"],
      "NextNodeConfig": { "Configs": { "on_success": "guard_input", "on_error": "error_handler" } } },

    { "NodeId": "guard_output", "FuncName": "guard_output",
      "NextNodeConfig": { "Configs": { "on_success": "end" } } }
  ]
}
```

复用既有 `agent_loop_entry`/`guard_input`/`guard_output`/`handle_error`/`sk_plan`/`condition_check`/`dynamic_workflow_executor`，仅新增 3 个函数节点。

---

## 六、错误处理

### 6.1 错误分类与策略

| 错误类型 | 检测点 | 处理策略 |
|---------|--------|---------|
| **L1 SK 生成失败** | `provide_prompt` | 重试 1 次（temperature=0）；仍失败转兜底 `sk_plan` |
| **CLine 超时** | `cline_execute` | `ClineClient` 已有总超时；`rule_match` 判 `ok=false` → `error` → 兜底 `sk_plan` |
| **CLine 执行失败**（exit≠0） | `cline_execute` | `rule_match` 命中 `exit_error` → `error` → 兜底 `sk_plan` |
| **规则集未配置/损坏** | `rule_match` | 用内置默认规则（`default → stop`），告警不崩溃 |
| **达到 max_steps** | `rule_match` | `max_steps` 规则 → `stop`，避免无限循环 |
| **用户取消** | `rule_match` | 复用 `CheckCancellation`，抛取消异常 |
| **CLine 模块不可用** | 注册期 | `TryRegisterClineUsageModule` 告警；`cline_execute` 节点 `on_error → sk_plan` |

### 6.2 成本预算（呼应文章1"减少轮次"第一杠杆）

```
单步成本：  provide_prompt(0 或 1) + cline_execute(1) + rule_match(0) = 1–2 次 LLM/步
首步：     L1 生成(1) + CLine(1) = 2 次
继续步：   L2 模板(0) + CLine(1) = 1 次  （模板命中时）
循环控制： rule_match 全程 0 次 LLM（对比既有 condition_check 每轮 1 次）
```

`max_steps=10` 时最坏 11 次 LLM（1 首步 + 10 CLine），优于 ReAct 25 次迭代上限。

---

## 七、测试

### 7.1 测试分层（沿用 xUnit + 内联数据）

```
tests/Agent.Tests/
├── Planner/
│   ├── PromptCatalogTests.cs          # L0 加载、L1/L2 模板填槽
│   ├── PromptFrameworkFunctionTests.cs # provide_prompt 集成（mock SK）
│   ├── RuleEngineTests.cs             # 规则评估：首匹配/守卫/默认
│   └── RuleMatchFunctionTests.cs      # rule_match 集成
├── ClineUsage/
│   └── ClineExecutorFunctionTests.cs  # cline_execute 集成（mock ClineClient）
└── AgentLoopWorkflowTests.cs          # 增强后 agent-loop.json 端到端（冒烟，零 AI 成本）
```

### 7.2 测试用例矩阵

| 组件 | 用例 | 验证点 | 是否消耗 AI |
|------|------|--------|------------|
| **PromptCatalog** | L0 加载固定、命中缓存 | 进程级常量、内容一致 | 否 |
| **PromptCatalog** | L1/L2 模板填槽 | `{{var}}` 替换正确、规则选模板正确 | 否 |
| **RuleEngine** | 首匹配胜出 | `exit_error` 优先于 `done_marker` | 否 |
| **RuleEngine** | `max_steps` 守卫 | step_count=10 → stop | 否 |
| **RuleEngine** | 规则集损坏用默认 | 不抛异常、`default → stop` | 否 |
| **RuleMatchFunction** | continue/stop/error 三态 | `next_prompt_hint` 正确传递 | 否 |
| **PromptFramework** | 首步 L1 调 SK 1 次 | mock SK 仅被调 1 次 | mock SK，否 |
| **PromptFramework** | 继续步 L2 模板零 LLM | mock SK 不被调 | mock SK，否 |
| **ClineExecutor** | cline_execute 读 prompt 执行 | mock `ClineClient.AskAsync` 正确参数 | mock CLine，否 |
| **端到端冒烟** | 首步→继续→完成 | 节点流转正确、`rule_match` 控制循环 | mock，否 |
| **兜底分支** | CLine 不可用→sk_plan | 路由到既有 MCP 路径、不崩溃 | mock，否 |

### 7.3 验收标准

- **循环控制零 LLM**：`rule_match` 全程不调 LLM。
- **L0 缓存命中**：系统提示词进程级固定。
- **模板优先**：继续步默认零 LLM，仅规则判定需重规划时调 SK。
- **无无限循环**：`max_steps` 守卫 + 规则 `default → stop` 双保险。
- **零破坏**：移除新增函数后，兜底分支（`sk_plan`→`dynamic_workflow_executor`）可独立运行。

---

## 八、与现有系统集成

### 8.1 注册接入点（扩展 AgentModuleRegistrar）

在 [AgentModuleRegistrar.cs](../src/Agent.Core/AgentModuleRegistrar.cs) 新增反射注册，沿用既有 `TryRegisterPlannerModule` 模式：

```csharp
private static void TryRegisterClineUsageModule(AgentOrchestrator orchestrator)
{
    try
    {
        var execType = System.Type.GetType("Agent.ClineUsage.ClineExecutorFunction, Agent.ClineUsage");
        if (execType != null)
        {
            execType.GetMethod("Register")?.Invoke(null, null);
            AgentLogger.Info(AgentLogCategoryEx.Agent_Core, "已注册 CLine 执行模块");
        }
    }
    catch
    {
        AgentLogger.Warn(AgentLogCategoryEx.Agent_Core, "CLine 执行模块不可用（未构建 ClineApi 或未加 Agent.Core 引用）");
    }
}
```

在 `SetupAgentAsync` 中于 `TryRegisterPlannerModule` 之后调用。`Agent.Planner` 的 `PromptFrameworkFunction`/`RuleMatchFunction` 由既有 `TryRegisterPlannerModule` 的 `[FunctionLibrary]` 自动发现机制一并加载。

### 8.2 复用既有组件的边界

| 既有组件 | 复用方式 | 改动 |
|---------|---------|------|
| `SemanticKernelFactory` | `provide_prompt` L1/L2 兜底生成调用 | 无改动 |
| `ClineClient` | `cline_execute` 内部调用 `AskAsync` | 无改动（仅包装） |
| `AgentLogger`/`AgentLogCategoryEx` | 新增 `Agent_SemanticCore` 分类 | 增加枚举值 |
| `AgentLoopFunction.CheckCancellation` | `rule_match` 复用取消检查 | 无改动 |
| `sk_plan`/`condition_check`/`dynamic_workflow_executor` | 兜底分支引用 | 无改动 |
| `agent_loop_entry`/`guard_input`/`guard_output`/`handle_error` | 工作流直接引用 | 无改动 |

### 8.3 csproj 改动

**`Agent.ClineUsage.csproj`** 新增引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\Agent.Core\Agent.Core.csproj" />
</ItemGroup>
```

`Agent.Planner.csproj` **无改动**（已有 Agent.Core + SK）。`Agent.sln` 无新项目。不引入新依赖。

### 8.4 分阶段交付路径

1. **P0 骨架**：`PromptCatalog`/`RuleEngine` + `Config/prompts`/`Config/rules` + `agent-loop.json` 增节点 + 注册器冒烟（mock 全函数，验证兜底不破坏）。
2. **P1 单通路**：`provide_prompt`（L0+L1，SK 生成）+ `cline_execute`（真实 CLine）+ `rule_match`（默认规则集），首步→执行→stop 走通。
3. **P2 循环**：`rule_match` continue 路径 + L2 模板填槽，多步循环走通。
4. **P3 健壮性**：规则集损坏兜底 + CLine 不可用降级 `sk_plan` + `max_steps` 守卫。
5. **P4 配置化**：规则集/模板外部化到 `Config/`，文档。

---

## 九、v2 未来阶段（当前不做）

以下文章2 能力推迟到 v2，当前阶段不实现：

- **契约式四维显式化**：`PlanContract`（identity/params/output_schema/env_tags）。
- **指针化**：提示词内联 → 契约文件 + `plan_id` 指针传递。
- **计划缓存库**：`plan_cache_lookup`（两层检索）+ `plan_promote`（≥3 次/30 天入库）。
- **生成前检索正反馈**：命中缓存时跳过 `provide_prompt` 的 SK 生成。
- **结构化结果校验**：`result_validate` 按 `output_schema` 校验（当前阶段并入 `rule_match` 的字段条件）。

当前阶段的 `rule_match` 字段条件评估（`exec_result.ok`/`text contains`）是 v2 结构化校验的轻量前身，可平滑升级。

---

## 十、设计自审

- **占位符**：无 TBD/TODO。
- **一致性**：架构图/工作流 JSON/函数表三处节点名一致（`provide_prompt`/`cline_execute`/`rule_match`）；模块归属与文件结构一致。
- **零破坏**：兜底分支保留 `sk_plan`→`dynamic_workflow_executor`；移除新增函数后既有路径可独立运行；`Agent.ClineUsage`→`Agent.Core` 不循环。
- **范围**：当前阶段聚焦两个核心功能（提示词分级 + 规则匹配循环），缓存/契约/检索明确推迟 v2。
- **歧义**：`continue` 由规则判定非 LLM；L2 默认模板零 LLM，仅规则判定需重规划时调 SK；`max_steps=10` 内置守卫。

---

## 十一、待决问题（实现阶段确认）

1. L0 系统提示词的具体内容与死命令措辞（当前占位，需按实际任务领域填充）。
2. L1 任务级 SK 生成的模板槽位与 `rule_needs_replan` 判定条件细节。
3. 规则集 `loop-rules.json` 的完成/继续标记词表是否需要按任务领域定制（当前通用词表）。
4. `max_steps` 默认 10 是否合适，是否外部化到配置（当前硬编码于规则集）。
5. 是否需要 `step_count` 跨工作流节点传递的机制（当前由 `rule_match` 自增并回传）。

---

## 十二、关键决策速查

| 决策 | 选择 | 理由 |
|------|------|------|
| 模块本质 | 调度（不新建模块，注入现有模块） | 零破坏，复用 SK/ClineClient |
| 当前阶段功能 | ①提示词分级 ②规则匹配循环 | 用户明确精简范围 |
| 提示词实现 | 规则与生成结合，分 L0/L1/L2 | 系统提示词固化缓存 + 必要时 SK 生成 |
| 循环控制 | 规则匹配（非 LLM） | 零 LLM 成本，杜绝无限循环 |
| 执行端 | CLine（复用 ClineClient） | 用户指定 CLine 为执行模块 |
| 兜底分支 | sk_plan → dynamic_workflow_executor | CLine 不可用时降级 |
| 缓存/契约/检索 | 推迟 v2 | 当前阶段聚焦调度核心 |
