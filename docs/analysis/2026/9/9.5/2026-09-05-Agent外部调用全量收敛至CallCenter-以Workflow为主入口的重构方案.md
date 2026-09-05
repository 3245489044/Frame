# Agent 外部调用全量收敛至 CallCenter —— 以 Workflow/FuncName 为主入口的重构方案

> **日期**：2026-09-05
> **职责层级**：L3（流程/规范流程）| 依据：`SingleImplementation/TH-12`、`RedundancyElimination/TH-13`、`ANTI-DUPLICATED-IMPLEMENTATION`、`ANTI-PARALLEL-RACE`
> **源文档**：
> - `[源1] docs/analysis/2026/9/9.4/2026-09-04-Agent调用逻辑收敛至CallCenter通用框架-收敛设计.md`（方向定稿：调用逻辑收敛 CallCenter，FuncName/MCP 同为「注册名→委托」执行 seam 的两种寻址，MCP 非目标）
> - `[源2] docs/analysis/2026/9/9.5/2026-09-05-CallCenter收敛-后续实施步骤规划.md`（实施缺口盘点）
> - `[源3] docs/analysis/Agent/doc/LocalLlm函数调用模拟方案.md` 与 `agent_mcpinvoke` 集成脚本（Path A/B 验收）
> **本文定位**：在源1/源2 之上的**重构实施总纲**，把收敛落点进一步钉死为——
> **① 以 Workflow/FuncName 工作流为唯一初始调用入口；② Agent 侧全部"外部调用逻辑"实体迁入 `CallCenter`；③ 由工作流对后续每一步可选分发到 CallCenter 或 Agent 内部处理；④ 以"运行时机械护栏"根治（而非仅文档纪律约束）当前深链手工透传、静默失败、多执行端漂移的混乱。**
> **触发事实（第一手证据）**：2026-09-05 `agent_mcpinvoke_create_task_test` Path A 静默失败——LocalLlm 服务端已命中并产出 `create_node_reference` 的 tool_call 字符串，但经本地 wire 腿 `OpenAiCompatibleLlmClient.CallWithToolsAsync` 映射回 `LlmResult` 时**只取 Content、丢弃 tool_calls**，`LlmResult.ToolCalls` 恒为空，链条在 `LlmToolExecutor` 之前断裂，`tp_llm_mcp_demo` 从未建。该缺陷被自家 9.4 分析"缺陷4 工具可用性跨 3 层手工透传、任一层丢名即静默回退"精确预言却未根治。本文以根治此类问题为出发点。

---

## §0 重构意图与验收判据

**意图（一句话）**：一切对 Agent 能力的调用，**初始一律以 FuncName 工作流调用进入**；工作流在其内部把每一步**可选地分发给 CallCenter（外部调用逻辑的承载）或 Agent 内部（领域处理）**；Agent 不再存在散落、直连、平行的"外部调用腿"，不再手工跨层透传工具可用性与往返结果。

**判据（对齐源1 §〇·方向定稿 + 本次 bug 教训，机器可检查）**
1. **换一种 Agent / 换一套 LLM 后端，调用逻辑零改动**——它已在 CallCenter（源1判据1）。
2. **分发/执行/契约/门控只有一份实现**，Agent 只声明"注册哪些 FuncName / 配哪个后端"（源1判据2）。
3. **FuncName 与 MCP/工具名是同一张「注册名→委托」表的两种寻址**，不各造一套机制（源1判据3）。
4. **全链工具往返（wire `tool_calls` → `LlmResult.ToolCalls` → `LlmToolExecutor` 执行）在任一装配层缺失即 fail-fast**，绝不静默回退文本（本次 bug 的止血硬闸）。
5. **可观测默认开**：决策/工具往返/命中/排除均有可审计记录，且不以"注册表放行才输出"为默认（消除"探针被静默抑制"这一误导源）。

---

## §一 现状问题盘点（为什么乱）

### 1.1 现状：Agent 侧"外部调用"散落多入口

| 外部调用形态 | 载体（现状路径） | 当前接入点 | 问题 |
|---|---|---|---|
| 会话折叠 LLM 调用 | `SkConversationFoldOperator.ChatStepAsync`（`Agent.Core/Signal`）→ `ILlmChatPort` | 折叠算子 | 依赖端口把 tools 全链透传，任一层丢即静默 |
| answer 执行端 | `AnswerFoldExecutor`(AgentFoldExecutors) / `StatelessAnswerExecutor`(`Agent.Modules/Answer`) | answer | **同名"answer"至少两处能力不一致**（有/无 extraTools），TH-12 运行时背离 |
| cline 执行端 | `ClineFoldExecutor` / `ClineAgentExecutor`（daemon 子进程） | cline | Agent 直持外部执行端，属"外部调用逻辑在 Agent 内" |
| LLM 传输 | `OpenAiCompatibleLlmClient`（唯一传输，本地 8787 与真实共用） | 网关路由 | **tool_calls→LlmResult 映射丢失（本次 bug）** |
| 本地/真实后端 | `LlmRoutingClient` + `LocalLlmHttpHost` + `LocalLlmClient`（规则引擎） | Agent.Runtime/Modules | 深链 7 层手工透传 |
| MCP 客户端腿 | `McpClientManager`/`McpToolFunction`/`McpFoldToolProvider`（外部 MCPBridge） | 折叠算子 `_toolProvider` | 外部工具与内部工具"几套 MCP"语义混杂 |
| 节点写图能力 | `create_node_reference`（LlmTool）/ `TaskGraphFoldExecutor` / CallCenter 折叠面 / FileDriven `addResource` 模板 | 多处并存 | 同一能力多套入口 |
| 工作流骨架 | `WorkflowRunner` 按 FuncName 直调注册函数（`agent-loop`：guard→prompt→cline_execute→rule_match→guard_output） | Agent.Core | 已经"按 FuncName"，但未与 CallCenter 单表统一，仍直调内部函数 |
| 宿主侧能力工具 | `McpServerTools`(answer/cline/local_llm/read_nodes) + `llm_chat`(LlmChat) | Agent.McpRegistryHost | 能力→工具面已走通，可作迁入 CallCenter 的现成骨架 |

### 1.2 结构性问题归纳（"乱"的五种形态）

1. **多入口并存**：同一能力（外部 LLM / answer / cline / 节点写图）在 折叠算子、骨架、宿主工具、外部 MCP、模板兜底 多处各有接入，行为随"走哪个组合根"漂移。
2. **深调用链 + 手工逐层透传**：`conversation/send → 折叠算子 → ILlmChatPort → LlmRoutingClient → OpenAiCompatibleLlmClient → HTTP 8787 → LocalLlmHttpHost → 规则引擎` 约 7 层，tools/往返结果全靠手工透传。
3. **静默失败**：任一层丢工具名/ToolCalls 只静默回退文本，无 fail-fast、无错误（本次 bug 是典型）。
4. **语义漂移**："MCP"同时指 进程内技能(LlmTool，零 transport)、外部 MCPBridge 腿、未来 MCP server；"真实模拟/进程内直调/同一路径"在不同文档定义不一致。
5. **可观测被开关抑制**：诊断探针以"临时"名义埋在热路径，靠 `agent_log_messages.json` 注册放行；未注册即静默 → 该出现的信号不出现，反成误导。

## §二 目标架构：以 Workflow/FuncName 为主入口，CallCenter 承载外部调用

### 2.1 分层与归属（目标形态）

```
┌────────────────────────────────────────────────────────────────────┐
│  入口层 · 一切初始调用 = FuncName 工作流调用（唯一入口）              │
│    external/UI/脚本/MCP/LLM-对话 → RunJson(task)/conversation      │
│    → 统一生成/复用一条 FuncName 工作流（骨架 agent-loop / 业务 wf）  │
│    → 骨架按 FuncName 步进（guard_input→…→guard_output）            │
└───────────────┬────────────────────────────────────────────────────┘
                │  工作流内对每一步 dispatch（声明式，可选）
                ▼
┌────────────────────────────────────────────────────────────────────┐
│  分发层 · FuncName 寻址单一表 = CallCenter ToolRouter               │
│    dispatch(FuncName) → CallCenter.CallAsync(FuncName, args)       │
│    按 FuncName 归属决定出口：                                      │
│      external → CallCenter（LLM 出口 / cline / 外部 MCP / 宿主能力） │
│      internal → Agent 内部处理（节点/图/存储/领域，经端口 seam）      │
└───────────────┬────────────────────────────────────────────────────┘
                ▼
┌────────────────────────────────────────────────────────────────────┐
│  CallCenter（通用框架，承载全部"外部调用逻辑"）                      │
│    Core: ToolRouter=注册名→委托单表 · CSharpTrack(函数库/FuncName)  │
│          · RouterHotReloadService(热换) · ToolFunctionSchemaMapper │
│    Adapters: ExecutionFrameworkInvokerFactory / CsSourceCompiler   │
│    Mcp: MCPBridge 终端轨 · 分类/映射/TS 编译                        │
│    Host: 宿主/CLI 组合根（Watch/list-tools/…）                      │
└────────────────────────────────────────────────────────────────────┘
```

### 2.2 核心机制（三条，作为"消灭混乱"的架构支点）

**M1 · FuncName 唯一寻址 = CallCenter 单表「注册名 → 委托」**
- CallCenter `ToolRouter` 的 `CSharpTrack` 本质就是"注册名→委托"，天然承载 FuncName。
- 所有 Agent 能力（llm_chat / cline / answer / 节点写图 / read_nodes / 注册函数）都登记为**同一张表的一条 FuncName 记录**；折叠算子、骨架、外部 MCP 都经 `CallAsync(FuncName, argsJson)` 这一个落点。**消除"直持 ILlmClient / 直调内部函数 / 多套工具面"三个平行寻址**。
- 保留 `NotExposedToModel` 黑名单语义：`guard_*`/控制面/确定性核心可登记、可不暴露给模型（防递归），但**执行仍走同表**。

**M2 · Workflow 主入口 + 声明式分发（internal | CallCenter）**
- 初始一律以 FuncName 工作流调用进入（`RunJson(task op=…)/conversation` → 骨架按 FuncName 步进）。
- 骨架/业务工作流对**每一步**声明分发：`dispatch = external → CallCenter`（LLM、cline、外部 MCP、宿主能力）`| internal → Agent 端口`（节点/图/存储/领域）。分发表是**声明式数据**（对齐 `RoundSpec.Steps` 的 FuncName/NodeId），非散落 if/switch。
- 这样"哪步走 CallCenter、哪步走内部"是一眼可查的配置，不再是装配偶然。

**M3 · 一条真实的执行 seam，运行时机械护栏（不再靠文档纪律）**
- 全链工具往返完整性做成**契约单测 + 装配期校验 + fail-fast**：wire `tool_calls → LlmResult.ToolCalls → LlmToolExecutor` 任一环缺，报错而非静默回退（根治本次 bug）。
- 可观测默认开：决策命中/排除/工具往返一律落审计（`DecisionRecord` / `ur_events`），不由"注册表放行"决定是否可见。

### 2.3 目标调用链示例（对照 §一 现状）

```
外部对话请求
 → conversation/send(FuncName=answer_workflow)        // 以工作流(FuncName)为主入口
 → Workflow 骨架：guard_input → provide_prompt → answer_step(dispatch:external) → rule_match → guard_output
      answer_step: CallCenter.CallAsync("answer", {…})   // CallCenter 承载 answer 外部调用逻辑
        → CallCenter 内统一 seam → LLM 出口(llm_chat) → wire tools → ToolCalls 保真回传
        → (若模型返回工具调用 create_node_reference)
          → CallCenter.CallAsync("create_node_reference", {…})   // 内部写图，或经 internal seam 回落 Agent 确定性写图
      guard_output: internal（Agent 内部校验，登记为 FuncName 但 dispatch=internal）
 → 终答回话
```

> 说明：同一能力仅"寻址"从多入口收敛为一个 FuncName；"写图走确定性模块、LLM 只做意图/参数"的既有职责边界**不变**（LLM 不猜节点/边，StructuralGrounding 保持）。

---

## §三 CallCenter 承载面与 Agent 保留面（迁移清单）

### 3.1 CallCenter 目标承载（"外部调用逻辑"全量归此）

| 承载项 | 迁入形态（建议落点） | 迁自 |
|---|---|---|
| **LLM 统一出口 llm_chat** | `CallCenter/…/LlmChat`（含 Gate/路由，包裹 LlmRoutingClient+GlobalLlmGate+LlmMode）+ `LlmChatPort` 适配为 FuncName 登记 | `Agent.McpRegistryHost/LlmChat.cs`、`Agent.Runtime` 网关 |
| **answer / cline 执行端** | 统一单一 `answer`、`cline` 两 FuncName 记录 + 共享执行缝；删 `AnswerWorkflowFunction`/`AgentFoldExecutors` 等平行实现 | `Agent.Modules/Answer`、`Signal`、`Cline` |
| **LLM 传输（唯一）** | `OpenAiCompatibleLlmClient` 下沉并**补 ToolCalls 保真映射**（§四 P0） | `Agent.Contract/Llm` |
| **本地后端/规则引擎** | `LocalLlmClient`(规则) + `LocalLlmHttpHost` 入 CallCenter 承载 | `Agent.Modules/LocalLlmHttp` |
| **外部 MCP 客户端腿** | `MCPBridge` 终端轨（分类/映射/TS 编译）已在 `CallCenter.Mcp`；`McpClientManager` 等移入/以桥方式收口 | `Agent.Modules/MCP` |
| **能力→工具面 / 注册函数面** | `CallCenterFoldToolProvider` + `RegistryFoldToolProvider` + `CompositeFoldToolProvider` 统一为 ToolRouter 单表消费者 | 折叠算子 `_toolProvider` 侧 |
| **热换 / 编译** | `RouterHotReloadService` + `ToolRouterFactory` + `CsSourceCompiler` 已在 Core/Adapters | —（已有） |
| **框架→能力统一 seam** | `_toolProvider.CallAsync(name,args)` 即 `CallCenter.CallAsync(FuncName,args)` | 折叠算子 |

### 3.2 Agent 保留面（只剩"领域装配 + 声明"）

| Agent 保留 | 说明 |
|---|---|
| 组合根装配（`Agent.Launcher` `ConversationHosting` / `AgentFramework`） | 只负责：把 Agent 领域能力**登记**为 FuncName 到 CallCenter 单表、注入装配、管生命周期（停 CallCenter 热换）；**不再直持 ILlmClient / 直调外部执行器** |
| FuncName / Workflow 声明（`agent-loop` 骨架、`RoundSpec.Steps`、DrivenConfig 脚本/模板） | 声明式数据：哪条工作流、每步 FuncName、`dispatch=external|internal` |
| Agent 领域处理（节点/图/存储/会话历史/消息桥） | 经 `internal` 分发端口 seam 暴露，不在折叠算子/骨架里散落 if/switch |
| 确定性核心（`guard_*`/控制面/预算/终止） | 仍是引擎确定性核心（`SEP.DeterministicCoreDisjoint`），登记但 `NotExposedToModel` |

### 3.3 命名与依赖规划

- **依赖方向**：`Agent.Launcher/…/领域 → CallCenter(Adapters→Core/Mcp) →（不反向引用 Agent 命名）`。CallCenter 不依赖 `Agent.*` 具体类型（可参考 `Agent.Modules/Skill` 零外部依赖的做法），只在组合根做适配。
- **目标**：`Projects/Agent/…` 里不再出现 `ClineAgentExecutor`、`McpClientManager`、`LocalLlmHttp`、`StatelessAnswerExecutor` 等"外部执行实体"的直接持有与平行实现；它们以 **FuncName 记录 + CallCenter 承载**存在。
- **归并候选命名**（避免又造一层）：优先复用已有 CallCenter 结构 `Core/Adapters/Mcp/Host`，不新增平行命名空间。

---

## §四 根治"混乱"的运行时机械护栏（P0 前先立，非文档纪律）

混乱的病根是"纪律靠文档、不靠机械校验"。以下护栏**先于一切迁移落地**，作为把门层，防止边迁边乱。

### 4.1 【P0 硬闸】工具往返全链保真契约（直接根治本次 bug）

- **契约测试**：本地 wire（`LocalLlmHttpHost` 回写 tool_calls）+ 真实 wire（`OpenAiCompatibleHost`）往返，断言 `LlmResult.ToolCalls` 非空且内容 = 服务端产出的 `create_node_reference + CreateTaskGraphRequest`。当前实现因 `OpenAiCompatibleLlmClient.CallWithToolsAsync` 只 `new LlmResult(result.Content)` 会立即红——这就是要补的映射。
- **修复点**：`OpenAiCompatibleLlmClient` 把 `result`（wire `OpenAiChatResult`）的 `tool_calls` 转成 `LlmToolCall[]` 一并放进 `LlmResult`；`LlmResult` 需能承载 ToolCalls（沿用现有字段）。
- **装配期校验**：凡声称"支持工具（Function Calling）"的调用点（折叠算子/answer/llm_chat），若后端声明支持工具而返回既无 ToolCalls 又无错误，记**可观测告警**（不静默当纯文本吞）。缺工具名/缺往返 = fail-fast 或显式告警，二者必居其一，杜绝静默回退。

### 4.2 FuncName/工具可用性单一真源（消除"跨 3 层手工透传"）

- 工具面/注册名只在 **CallCenter ToolRouter 单表**声明一次；折叠算子、骨架、`llm_chat`、外部 MCP 都从**同一注册中心**取 `FuncName→委托 + 是否暴露给模型 + dispatch 归属`，不再各自 `extraToolsProvider`/手工塞名。
- 装配后**启动期断言**：骨架引用的每个 FuncName、分发表每个 dispatch、模型可见工具集，都必须能在单表解析；解析失败 = 装配失败（fail-fast），不是运行时静默缺名。
- 该表即源1 §〇 判据3 的"一张注册名→委托表"，同时支撑 按名/FuncName/MCP 工具名 三种寻址。

### 4.3 多执行端/多入口收敛为"一能力一 FuncName"

- 同名能力（answer/cline/节点写图/llm_chat）只允许**一条 FuncName 记录** + **一份实现**；凡出现第二份执行实现 = 编译期/评审否决（对齐 TH-12/SingleImplementation + 源1 §〇判据2）。
- 消除"同名 answer 两执行器能力不一致"的装配偶然：能力是否可工具调用由单表登记决定，不再随走哪个组合根漂移。

### 4.4 可观测默认开

- 决策命中/规则排除/工具往返/后端选择/门控结果一律落结构化审计（`DecisionRecord` / 统一事件），**默认即输出**；诊断探针不以"临时"名义埋热路径、不受"注册表放行才打印"抑制（未注册 msgId 视为错误而非静默）。
- 集成脚本断言从 `text-contains` 升级为"决策记录/ur_tasks 状态"的语义断言（对齐源2 §四 P4：动作已执行/已 Abort 的直接断言），消除 Path B 兜底掩盖 Path A 未激活的假绿/假阳。

---

## §五 分阶段实施（每步带验证闸，先立护栏再迁移）

### P0 · 固本止血：工具往返护栏（1 落点 + 契约测试）
- **改动落点**：`Projects/Agent/src/Agent.Contract/Llm/OpenAiCompatibleLlmClient.CallWithToolsAsync`——把 wire `OpenAiChatResult` 的 `tool_calls` 映射进 `LlmResult.ToolCalls`（不再 `new LlmResult(result.Content)`）；`LlmResult` 承载 ToolCalls。
- **契约测试**：`LlmChatPort`/wire 工具往返断言 ToolCalls 保真（见 §4.1）。
- **验证闸**：新增单测绿；既有 `LocalLlmToolCallTests`/`LlmChatTests`/`WireTools_*` 不回归；重跑 `agent_mcpinvoke_create_task_test` Path A → 出现 `[PathA] create_node_reference 被 tool_call 调起` 且 `ur_tasks` 出现 `tp_llm_mcp_demo`（本地/GUI 闸）。
- **意义**：这一步同时打通**真实 LLM** 的 Function Calling（同一条 `OpenAiCompatibleLlmClient` 传输），即"与真实 LLM 同一路径"的机制前提。

### P1 · 建立 CallCenter 单表统一执行 seam（不搬实体先并寻址）
- **落点**：把折叠算子/骨架的"外部调用出口"从 多路 `_llm`/`_toolProvider`/直调内部函数 收敛为 **`CallCenter.CallAsync(FuncName, args)` 单一 seam**；在 CallCenter 单表登记 `llm_chat`、`answer`、`cline`、`create_node_reference`、`read_nodes` 与宿主能力，作为首批 FuncName 记录。
- 折叠算子 `ChatStepAsync` 去 `ILlmChatPort` 直连、去工具手工拼接：从单表取"模型可见工具集 + dispatch 归属"，调用经 CallCenter 出口。
- **验证闸**：`Agent.Launcher` 编译 0 错；现有会话/骨架测试回归；Host CLI `--list-tools/--call` 可见并调用登记的 FuncName。

### P2 · 外部调用实体迁入 CallCenter（删平行实现）
- **落点（迁入 CallCenter.Adapters/…，Agent 只留组合根登记）**：
  - `llm_chat`（含 Gate/路由）→ CallCenter；
  - `answer`/`cline` 执行端收敛为**单一实现**入 CallCenter；删除平行份（如 `AnswerWorkflowFunction`/`AgentFoldExecutors` 的重复 answer、Agent 侧直持 `ClineAgentExecutor` 腿）；
  - LLM 传输、本地后端（`LocalLlmClient`/`LocalLlmHttpHost`）由 CallCenter 承载，Agent 经 FuncName/端口 seam 使用；
  - `McpClientManager`/外部 MCP 以 CallCenter.Mcp MCPBridge 轨收口。
- **验证闸**：编译 0 错；`TH-12` 评审——同名能力全局仅一份；Agent.Core/Modules 不再出现外部执行实体直接持有；回归 CallCenter 全量测试 + Host 冒烟。
- **风险闸**：迁 answer/cline 有运行时行为影响（超时/异常映射），**禁止在无验证的盲改**下硬编组合根——每迁一个执行端配契约测试 + GUI 运行闸（延续源2 §六障碍的纪律）。

### P3 · Workflow/FuncName 为主入口落地
- **落点**：把"初始调用"统一改为 FuncName 工作流调用——外部/UI/脚本/MCP/LLM-对话一律进入 `RunJson(task/conversation)` → 生成/复用一条 FuncName 工作流 → 骨架步进。
- 工作流内对每步声明 `dispatch=external(CallCenter)|internal(Agent 端口)`（声明式分发表，见 §2.2 M2），在折叠算子/骨架中**删除散落的 if/switch 寻址**。
- **验证闸**：外部请求（对话 create 任务、脚本断言、注册函数调用）都经同一 FuncName 工作流路径到达执行，观测记录显示每步 dispatch 归属；语义断言（非 text-contains）通过。

### P4 · 收敛验证与文档归档
- 代码单一真源清理：删死代码/未用引用（TH-13），反模式清零（`ANTI-DUPLICATED-IMPLEMENTATION`/`ANTI-CYCLIC-AGGREGATION`/`ANTI-BLOAT`）。
- 文档归并：以本文为总纲，把 9.4 收敛设计、9.5 实施规划、`LocalLlm函数调用模拟方案`、`MCP服务化接入方案`、`三层工作流MCP封装设计` 等状态刷新/归并，避免继续叠文档。
- **验收**：§0 五条判据逐条勾稽；仓库真源/命名单一（含发布历史线治理，见 §六）。

---

## §六 风险与回退

| 风险 | 应对 |
|---|---|
| 边迁边引入新入口 | 严格"先立护栏(P0)再迁移(P1→P3)"；单表解析失败/能力重复即 fail-fast，迁移中禁止新增直持/平行实现 |
| answer/cline 迁移改变运行时行为（超时/异常映射/后端切换） | 每个执行端迁移独立成 commit + 契约测试 + GUI 运行闸；无验证不硬编组合根（延续源2 §六障碍纪律） |
| 骨架/折叠寻址重构回归会话主循环 | 保留既有会话/骨架测试 + `agent-loop` 冒烟；分发表先增后删，双轨验证后再切 |
| FuncName 工作流为主入口改动面大 | P3 在 P2 实体归位后做；入口统一以 `RunJson(task/conversation)` 承接，旧入口做兼容转发后删 |
| 发布历史线混乱干扰 | 建议仓库治理独立于代码重构推进：统一一个真源 remote 与历史线（如以完整仓库为真源，废弃 excl. Projects/ 孤立根双源），避免"两个仓库/两套历史"持续放大混乱感知 |
| 过度收敛反而再叠抽象 | 不新增平行命名空间；只复用 CallCenter `Core/Adapters/Mcp/Host`；每步以"删了代码"为成功而非"加了封装" |

## §七 验收清单（逐条可勾稽）

- [ ] P0：新增/既有工具往返契约测试绿；`agent_mcpinvoke` Path A 真建 `tp_llm_mcp_demo`（不再依赖 Path B 兜底）；真实 wire 工具往返同实现可用
- [ ] §0判据1：换一套 LLM 后端（local↔真实）调用逻辑零改动——差异只在 CallCenter 单表/装配
- [ ] §0判据2：分发/执行/契约/门控仅一份实现；Agent 只有"注册哪些 FuncName/配哪个后端"的声明
- [ ] §0判据3：FuncName 与 MCP/工具名同走一张 CallCenter 注册名→委托表，不各造机制
- [ ] §0判据4：任一装配层缺工具/丢 ToolCalls 即 fail-fast 或显式告警，无静默回退文本
- [ ] §0判据5：决策/工具往返/门控审计默认可见，无"注册表放行才输出"的静默抑制
- [ ] TH-12：同名能力（answer/cline/节点写图/llm_chat）全局仅一份实现
- [ ] Agent 侧不再直接持有 `ClineAgentExecutor`/`McpClientManager`/`LocalLlmHttp`/重复 `answer` 等外部执行实体
- [ ] 骨架/会话、CallCenter 全量测试、Host CLI 冒烟全绿；反模式清单无新增

## §八 附：本次 ToolCalls 丢失与本文的对应（为什么是"护栏问题"而非"又多一个 bug"）

- 现象：LocalLlm 服务端已产出合法 `create_node_reference` tool_call，却因 `OpenAiCompatibleLlmClient` 映射丢 ToolCalls 而静默断链 → 是"工具往返无全链保真护栏 + 深链手工透传无装配期校验 + 可观测被注册表抑制"三者叠加的必然结果（§四 4.1/4.2/4.4 各对应其一）。
- 它是源1 §〇判据4、§一缺陷4/5 的**运行时实证**：文档已预言、代码未关账。
- 根治不靠"再修一个映射点"，靠：单一执行 seam（CallCenter 单表）+ 全链契约护栏 + 声明式 dispatch + 可观测默认开（§二 M3 / §四）。
- 完成 P0–P4 后，同类"某层悄悄丢东西、全链看似成功"的隐性故障将转为**装配失败 / 显式告警 / 审计可见**三者之一，可被机器即时拦截。
