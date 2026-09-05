# Agent 调用逻辑收敛至 CallCenter 通用框架 —— 架构收敛设计

> 原名《LLM 经 MCP 唯一交互——架构收敛设计》；2026-09 定稿改题：核心是**调用逻辑下沉 CallCenter 通用框架**（多寻址统一，分发以 FuncName/MCP 简便为准），MCP 非目标。

> 目标（2026-09 定稿）：把 Agent 侧的调用/调度核心逻辑**收敛进 `CallCenter` 通用框架**，使调用逻辑只此一份、与 Agent/后端解耦；**MCP 非目标**，仅作一种对外出口，**分发可按 FuncName 简便为之**。本文以整理优化设计 + 现状落点为主；标题"LLM 经 MCP 唯一交互"为早期候选形态，被 §〇·方向定稿取代。

---

## §〇·方向定稿（2026-09）：真正的收敛目标 ≠ "走 MCP"

> **更正的收敛目标**：把核心调用/调度逻辑（按名/按 FuncName 分发、执行 seam、契约、门控/可观测）收敛进 **`CallCenter`（通用框架）**，使 Agent 只保留领域装配与声明，**调用逻辑下沉为 CallCenter 的通用机制**。**"是否走 MCP"无关紧要**——MCP 只是其中一种可能的对外出口；**若按 FuncName 调用更方便就直接用 FuncName**（CallCenter 的 `CSharpTrack`/函数库源本就是"注册名 → 委托"的 FuncName 分发）。本文标题中的 "LLM 经 MCP 唯一交互" 仅为**早期候选形态之一**，不作为目标；凡与该定稿冲突处，以本定稿为准。

**判据（用来衡量是否"收敛到位"）**
1. 换一种 Agent（或换一套 LLM 后端）时，调用逻辑零改动——它已在 CallCenter。
2. 分发/执行/契约/门控只有一份实现，Agent 只声明"注册哪些函数/配哪个后端"。
3. MCP（工具名）与 FuncName 只是同一执行 seam 的**两种寻址入口**，底层同一张"注册名→委托"表；两者不各造一套机制。

**因此后续（P1–P3/N 决策）应读作**：把 Agent 的调用逻辑**迁入 CallCenter**；分发优先选更简便的一种（倾向 FuncName，因其与"函数库注册/骨架按名"天然一致），MCP 出口保留为给外部/模型的视图即可。

---

## §〇 TL;DR

- **现状**：LLM 在折叠算子 `ChatStepAsync`、`AnswerFoldExecutor`、Cline 子进程等多处被直接持有调用；工作流函数按 FuncName 被骨架 `WorkflowRunner` 直调。MCP 仅有外部工具腿（`McpFoldToolProvider`）与宿主侧能力工具（`McpServerTools`：answer/cline/local_llm/read_nodes）的雏形。
- **目标**：MCP 成为**唯一**与 LLM 交互的边界。所有能力 = 一张 MCP 工具表，LLM = 其中一枚 `llm_chat` 工具；框架不再持有 `ILlmClient`，工作流不再按 FuncName 直调函数。
- **已拍板决策**：
  1. MCP 边界 = **本进程内部 seam**（非外部独立 server），开销低、渐进收敛。
  2. LLM 门控/网关（`GlobalLlmGate`/`LlmMode.LocalOnly`/`LlmRoutingClient`）**下沉到 `llm_chat` 工具实现**，框架侧不感知。
  3. 本次先**整理为文档**，按 P1→P2→P3 分阶段实施，每步有验证闸。
  4. **2026-09 定稿**：目标 = 把 Agent 调用逻辑**收敛进 CallCenter 通用框架**（同一张"注册名→委托"表支撑 按名/FuncName/MCP 工具名 多寻址，分发以简便为准，见 §〇·方向定稿）；MCP 非目标，仅为一种出口。CallCenter 现成骨架 = `ToolRouter`(CSharpTrack/FuncName) + `CallCenterFoldToolProvider`(折叠 seam) + `ToolFunctionSchemaMapper`(两套工具面)。

---

## §一 背景与现状

### 1.1 当前 LLM 调用点（直接持有）
| 位置 | 调用方式 | 归属 |
|---|---|---|
| `SkConversationFoldOperator.ChatStepAsync` | `_llm.ChatAsync` / `_llm.ChatWithToolsAsync`（进程内） | 会话折叠算子 |
| `AnswerFoldExecutor`（AgentFoldExecutors.cs） | 持 `ILlmClient` 直接调用 | answer 执行端 |
| `ClineAgentExecutor` / `ClineFoldExecutor` | 经 Cline 子进程（daemon）间接触网 | cline 执行端（外部） |
| `LocalLlmHttp` | 本地联调 HTTP 后端 | 本地测试 |

### 1.2 当前工作流直调函数（骨架 WorkflowRunner，按 FuncName）
`agent-loop.json`：`guard_input → provide_prompt → cline_execute → rule_match`（+ 引擎附加 `guard_output`）。
经 `FunctionContext → NodeExecutor → WorkflowContext` 由函数注册表（`FunctionRegistrar`/反射目录）按名直调。

### 1.3 已有 MCP 基础（非从零）
| 基础设施 | 说明 |
|---|---|
| `McpServerTools.cs` | 宿主侧 MCP 工具：`answer`/`cline`/`local_llm`/`read_nodes`（能力→工具已走通） |
| `McpFoldToolProvider.cs` | 外部 MCP client 桥：`GetToolsAsync`/`CallAsync`（折叠算子 `_toolProvider`） |
| `LlmRoutingClient.cs` | 统一 LLM 网关：已收敛所有 `ILlmClient`（local↔配置后端，`ResolveCurrent` 单一路由点） |
| 外部桥 | `TypeScriptPrj/TsWorkspace/MCPBridge`：文件系统/终端等外部工具 |
| **`CallCenter.Core` ToolRouter**（2026-09 增） | **本进程内部统一执行 seam**：多轨（C# 函数库 / MCPBridge TS/PS/ExternalMCP）聚合为一张 `CallAsync(name, argumentsJson)` 工具表 |
| `CallCenterFoldToolProvider`（2026-09 增） | 把 `ToolRouter` 适配为折叠算子 `IMcpFoldToolProvider`（内/外统一 `_toolProvider.CallAsync` 分流的单一落点） |
| `ToolFunctionSchemaMapper`（2026-09 增） | 工具面→模型 Function schema 唯一派生，`NotExposedToModel` 过滤即"引擎确定性核心不暴露给模型"的防递归载体 |
| `RegistryFoldToolProvider` / `RegistryToolService` | 注册函数面 `registry_search/call`，进程内直调任意注册工作流函数（控制面/`guard_*` 默认不暴露给模型） |
| `CompositeFoldToolProvider` | 组合 注册表 + 外部 MCP + 额外确定性工具 为折叠算子单一工具提供 |

---

## §二 目标形态

```
┌─────────────────────────────────────────────────────────────┐
│  框架（解析链 + 只调 MCP 工具；不持 ILlmClient，不按 FuncName 直调）│
│   FoldOperator / WorkflowRunner / Executor                      │
└───────────────┬─────────────────────────────────────────────┘
                │ CallAsync("工具名", args)         唯一交互面
                ▼
┌─────────────────────────────────────────────────────────────┐
│            MCP 统一工具表（本进程内部 seam）                     │
│   llm_chat │ guard_input │ provide_prompt │ rule_match       │
│   answer │ cline │ read_nodes │ 外部 MCPBridge 工具 …          │
└───────┬──────────────┬──────────────────────────┬───────────┘
        │              │                          │
   LlmRoutingClient    GuardFunction/Core         McpClientManager
   +GlobalLlmGate                                      │
   +LlmMode(门控下沉)                           外部 MCP server
```

- 框架到 LLM 的唯一路径：`CallAsync("llm_chat", {question/system/history/tools})` → `llm_chat` 工具内部命中统一网关。
- 工作流函数 `guard_input/provide_prompt/rule_match` 由"按 FuncName 直调函数"改为"按 MCP 工具名调用"。

---

## §三 已拍板决策

| 决策点 | 结论 |
|---|---|
| MCP 边界形态 | **本进程内部 seam**（工具宿主在进程内，同 `McpServerTools` 一类）；不引入外部独立 MCP server |
| LLM 门控/网关落点 | **下沉到 `llm_chat` 工具实现**：`GlobalLlmGate`/`LlmMode.LocalOnly`/`LlmRoutingClient` 收敛于工具内部 |
| 本次范围 | **先整理为文档**；实施按 P1/P2/P3，每步验证闸 |

---

## §四 收敛映射表

| 当前直调 | 封装为 MCP 工具 | 现状 |
|---|---|---|
| guard_input / guard_output | `guard_input` / `guard_output` | 需新建（框架内部 exec 工具，复用 `GuardFunction`；标 `NotExposedToModel`） |
| provide_prompt | `provide_prompt` | 需新建（框架内部 exec 工具，挂 Core；不暴露给模型） |
| rule_match（零 LLM） | `rule_match` | 需新建（框架内部 exec 工具，挂 Core；不暴露给模型） |
| cline_execute | `cline` | ✅ 已有（`McpServerTools.Cline` / 外部 MCP） |
| answer | `answer` | ✅ 已有（`McpServerTools.Answer`） |
| 折叠算子 LLM 聊天（`ChatStepAsync`/`ChatWithToolsAsync`） | `llm_chat`（唯一 LLM 出口） | ⏳ 地基在：网关类齐全 + `local_llm` 工具已存在（纯聊天）；缺"打包网关/门控 + tools 的 llm_chat" 与热路径去 `_llm` |
| 框架→能力统一 seam | `_toolProvider.CallAsync(name, args)` | ✅ 已存在（`CompositeFoldToolProvider` / `CallCenterFoldToolProvider` / 折叠算子 `_toolProvider`） |
| 折叠算子 LLM 聊天（`ChatStepAsync`/`ChatWithToolsAsync`） | **`llm_chat`**（新，核心） | 新建，包 `LlmRoutingClient`+`GlobalLlmGate`+`LlmMode` |
| 执行端内部 LLM（Answer/Cline） | 经各自工具 / `llm_chat` | 收敛 |
| 外部文件系统/终端/节点 | MCPBridge 工具 + `read_nodes` | ✅ 已有 |

---

## §五 架构设计（内部 seam 形态）

### 5.1 `llm_chat` 工具（唯一 LLM 出口）
```
[McpServerTool] llm_chat(
    ILlmClient llm,            // LlmRoutingClient（组合根注入，含 GlobalLlmGate/LlmMode）
    string question,
    string? system = null,
    string? conversationContext = null,   // 标准上下文
    string? toolsJson = null)            // LLM Function Calling 的 tool schema
```
- **门控下沉**：`GlobalLlmGate.RunScopedAsync` 从框架侧调用点移到本工具实现内；`LlmMode.LocalOnly` 由 `LlmRoutingClient.ResolveCurrent` 在工具内统一裁决。
- **可观测**：`llm_chat` 作为唯一 LLM 边界，告警/审计/后端生效记录在此收敛一处。
- **防递归**：`llm_chat` 只对外提供"纯 LLM 能力"，不作为给模型自己调用的 function schema 暴露，避免"模型→调 llm_chat→再调"无限环。

### 5.2 框架侧改造
- `SkConversationFoldOperator`：删 `_llm` 直调；`ChatStepAsync`/工具调用改为 `_toolProvider.CallAsync("llm_chat", args)`。
- `AnswerFoldExecutor`：内部 LLM 改经 `llm_chat`（或经 `answer` 工具），不再持 `ILlmClient`。
- `GlobalLlmGate` 框架侧引用移除，改由 `llm_chat` 工具承载。

### 5.3 工作流函数化（P2）
- 新增宿主工具 `guard_input`/`guard_output`/`provide_prompt`/`rule_match`（复用现有 `GuardFunction`/Core 实现，工具方法零业务逻辑，同 `McpServerTools` 模式）。
- 骨架 `WorkflowRunner` 直调由"函数注册表按 FuncName"改为"MCP 工具名解析"；与折叠算子共用同一工具表。

### 5.4 内/外二分保持
- **内部能力** = 宿主侧工具（`llm_chat`/`guard_*`/`provide_prompt`/`rule_match`/`answer`/`cline` 进程内直调）。
- **外部能力** = `McpClientManager` → `MCPBridge`（文件系统/终端）。
- 统一通过 `_toolProvider.CallAsync(name, args)` 分流，不新增平行机制。

---

## §六 分阶段实施（含验证闸）

> **2026-09 就绪度**：本节的难点基建（统一 seam / 两套工具面 / 注册函数面 / 网关类）已就绪（见 §1.3），
> P1–P3 收敛为"内容改造 + 决策拍板"。**边界**：本设计只改"框架→能力 / 框架→LLM"两条路径的**交互边界**；
> `guard_*`/`provide_prompt`/`rule_match` 属引擎确定性核心，只能作**框架内部 exec 工具**（标 `NotExposedToModel`），
> **不得暴露为给模型的 function schema**（对齐 `SEP.DeterministicCoreDisjoint` / sk-function-calling-boundary 检查 5）。
> "工作流直调改工具名"应理解为"经统一 seam 的框架内部 exec 工具"，非"变成模型可自调用的 MCP 工具"。

### P1 · LLM 单一化（核心）
- 新增 `llm_chat` 宿主工具（包 `LlmRoutingClient`+`GlobalLlmGate`+`LlmMode`）。**现状**：网关类均在源码（`Agent.Runtime`/`Agent.Guardrails`）；`McpServerTools.LocalLlm` 已存在但为**纯聊天**（无 tools），需扩展为带 `toolsJson` 合一形态（拍板 N1）。
- `SkConversationFoldOperator.ChatStepAsync`/`AnswerFoldExecutor` 改经 `llm_chat`，删除 `_llm` 直接引用。**现状**：`SkConversationFoldOperator` 仍直持 `ILlmClient _llm`（L29）且 `ChatStepAsync` 直接 `_llm.ChatWithToolsAsync/ChatAsync`（L209–210）；`AnswerFoldExecutor` 持 `ILlmClient` —— 本轮删除并改走 seam。
- `GlobalLlmGate/LlmMode` 门控下沉到工具。

**验证闸**：Agent.Core.Tests 全绿；`LocalOnly` 本地端到端（EndToEndConversationLocalTests）通过；LLM 告警/审计在 `llm_chat` 单点可见。

### P2 · 工作流函数化
- 新增宿主工具 `guard_input`/`guard_output`/`provide_prompt`/`rule_match`（**框架内部 exec 工具**，复用 `GuardFunction`/Core，方法零业务逻辑，同 `McpServerTools` 模式；标 `NotExposedToModel` 使其不进模型 function schema）。归属拍板 N3（建议独立 `WorkflowFunctionTools`）。
- 骨架 `WorkflowRunner` 直调由"函数注册表按 FuncName"改为"经统一 seam 的工具名解析"；与折叠算子共用同一工具表（拍板 N4）。

**验证闸**：agent-loop 全链路回归（guard→provide→cline/answer→rule_match）；折叠与骨架行为一致。

### P3 · 统一执行 seam
- 折叠算子与骨架共用同一 MCP 工具表（当前 `WorkflowRunner` 折叠回退开关待移除）；确立单一执行入口，收敛重复直调。

**验证闸**：双路径行为一致 + 全量回归 + 文档同步。

---

## §六·补 N 决策收敛建议（2026-09）
| N | 建议 | 依据 |
|---|---|---|
| N1 | `llm_chat` 合一：`tools` 经参数传入，工具调用结果仍由框架经 `CallAsync` 处理 | 避免工具面内再开第二层工具面，少一条递归面 |
| N2 | `cline` 保留对外直连（编码代理本就外部触网），仅封装"框架→Cline"入口为工具 | 与现状 `McpServerTools.Cline`/`ClineFoldExecutor` 一致 |
| N3 | `guard_*`/`provide_prompt`/`rule_match` 独立 `WorkflowFunctionTools`（不入 `McpServerTools`，避免容器膨胀） | 与文档倾向一致；且利于统一打 `NotExposedToModel` |
| N4 | 折叠为唯一执行内核，骨架仅作 schema 预热（对齐既有"折叠为唯一内核"决策） | 消除 `WorkflowRunner` 回退双路径 |

---

## §七 代码/文件清单

**新增**
- `Agent.McpRegistryHost`：`llm_chat` 工具（新容器或并入 `McpServerTools`）
- 宿主工具：`guard_input`/`guard_output`/`provide_prompt`/`rule_match`（可并入 `McpServerTools` 或独立 `WorkflowFunctionTools`）

**修改**
- `Agent.Core\Signal\SkConversationFoldOperator.cs`：`_llm` 直调 → `CallAsync("llm_chat")`
- `Agent.Modules\Signal\AgentFoldExecutors.cs`：`AnswerFoldExecutor` 持 `ILlmClient` → 经 `llm_chat`
- 骨架 `WorkflowRunner`/`RoundStepInvoker`（若 P2/P3）：FuncName 直调 → MCP 工具名解析
- 配置 `agent-loop.json`：`FuncName` 声明同步为工具名语义

**复用不动**
- `McpServerTools.cs`（answer/cline/read_nodes）、`McpFoldToolProvider.cs`、`LlmRoutingClient.cs`、`GuardFunction.cs`、外部 `MCPBridge`
- 2026-09 已就绪的统一 seam：`CallCenter.Core.ToolRouter`、`CallCenterFoldToolProvider`、`ToolFunctionSchemaMapper`（NotExposedToModel）、`CompositeFoldToolProvider`/`RegistryFoldToolProvider`（registry 注册函数面）——P1–P3 在其上收敛，不新增平行机制

---

## §八 收益 / 风险

**收益**
- 单一 LLM 边界：门控/可观测/路由/后端切换收敛一处。
- 内外部能力统一为 MCP 工具，泛化"内/外二分"。
- 框架与模型解耦：换后端只需换 `llm_chat` 工具实现。
- 工作流直调 vs 折叠绑定双路径收敛为单一工具表。

**风险/代价**
- 折叠热路径每步经 MCP 工具往返 → **内部 seam 可控**（同进程、零 IPC），需测微基准确认无感知损耗。
- 需区分两套工具面：**给模型的 function schema**（Function Calling）vs **框架内部 exec 工具**（`llm_chat`/`guard_*`）。**载体已就绪**：`ToolFunctionSchemaMapper.NotExposedToModel` 过滤 + registry 控制面不暴露（§1.3）；剩余是给 `guard_*`/`provide_prompt`/`rule_match` 打 `NotExposedToModel`，确保不进模型 function schema，防递归/环路。
- 骨架路径收敛（P2/P3）会动 `FuncName` 直调语义，需回归锁定 agent-loop 行为。

---

## §九 未决问题（N）

> 收敛建议见 §六·补（N1 合一 / N2 保留对外 / N3 独立 WorkflowFunctionTools / N4 折叠唯一内核）。下列为原始表述，供追溯。

1. **N1**：`llm_chat` 的 Function Calling（`ChatWithToolsAsync`）如何表达——`llm_chat` 工具内是否再暴露一个 `llm_chat_with_tools`，还是经 `toolsJson` 参数合一？建议合一（工具调用结果仍由框架经 `CallAsync` 处理）。
2. **N2**：Cline 子进程本就外部触网，是否保留外部直连、仅把"框架→Cline"次数入口封装为 `cline` 工具（不改其内部 LLM 触网）？倾向保留对外。
3. **N3**：`guard_*`/`provide_prompt`/`rule_match` 放入 `McpServerTools` 容器还是独立 `WorkflowFunctionTools`（避免容器膨胀）？倾向后者。
4. **N4**：P3 是否真需"骨架 WorkflowRunner"与"折叠"二选一，还是以折叠为唯一执行入口、骨架仅作 schema 预热？（与既有"折叠为唯一内核"决策保持一致）

---

## §十·实施裁决（代码现状核对 · 一次性收口落点）

> 本节把 §六 P1–P3 与 §六·补 N 决策逐条对应**当前代码现状**给出最终裁决，作为一次性实施收口的权威落点。
> 原则：不制造平行重复实现（`SingleImplementation`/TH-12、`RedundancyElimination`/TH-13）；不复活已退役路径；
> LLM 面留在 Agent（对齐跨语言文档 D7，不引入 CallCenter→Agent 反向依赖）。凡与现状基建冲突的照抄项，一律以"由现有机制覆盖 / 不应照抄"裁决收口。

### 10.1 逐条裁决

| 文档条目 | 代码现状（源码佐证） | 最终裁决 |
|---|---|---|
| P2·新增宿主工具 guard_input/provide_prompt/rule_match（独立 WorkflowFunctionTools，标 NotExposedToModel） | 三者已是 `Modules.Registry` 注册工作流函数（`WorkflowFunctionNames` + 各自 `CreateDescriptors`/`Register`）；`RegistryToolService.InternalFunctionPrefixes`（`RegistryToolService.cs:23-27`）**已含 `guard_`/`rule_match`/`provide_prompt`/`cline_execute`** → 已"不进模型 function schema、但可经 `registry_call_tool` 内部 exec 直调" | **不照抄**。Agent 侧 `NotExposedToModel` 等价物 = registry 黑名单，无需新工具机制；再包一层 `[McpServerTool]` = ANTI-DUPLICATED-IMPLEMENTATION |
| P2·骨架 WorkflowRunner 直调 FuncName → 工具名解析 | 骨架 `WorkflowRunner` 已被 R2 决策退役（`ConversationHosting.cs:102-104` 退役回退；`SignalWorkflowRunner.FromFold` 折叠唯一内核） | **不照抄**。改动正在移除的路径=复活双路径，与 N4/折叠唯一内核冲突 |
| P1·llm_chat 宿主工具（包 LlmRoutingClient+GlobalLlmGate+LlmMode；LocalLlm 演进不新增） | ✅ 已落地：`Agent.McpRegistryHost/LlmChat.cs`（`LlmChat.ExecuteAsync` 纯封装）+ `McpServerTools.LocalLlm` 委托它 + 配套测试工程 `Agent.McpRegistryHost.Tests`（6/6 绿） | **完成**。门控/路由由注入的 `LlmGateway.Current`（LlmRoutingClient.GuardAsync）承载，封装不感知（§5.1 门控下沉语义） |
| P1·折叠算子 ChatStepAsync/AnswerFoldExecutor 去 `_llm` 经 llm_chat | ✅ 已落地（端口收敛）：折叠算子 `SkConversationFoldOperator` 与 `AnswerFoldExecutor`/`StatelessAnswerExecutor` 不再直持 `ILlmClient`，LLM 调用收敛到 `Agent.Core.Signal.ILlmChatPort`（默认 `LlmClientChatPort` 包装 `LlmGateway.Current`，返回 `LlmResult` 含 ToolCalls 保留 Function Calling 往返） | **完成**（端口收敛，非 string seam——CallAsync 返回 string 无法承载 ToolCalls，故用可返回 LlmResult 的端口） |
| N1·llm_chat tools 合一（toolsJson） | LlmChat.ExecuteAsync 已按合一实现（带 toolsJson→ChatWithToolsAsync） | **采纳（已落地）** |
| N3·guard_* 独立容器 vs 并入 McpServerTools | registry 黑名单已覆盖"不进模型 schema"，无需容器 | **由 registry 黑名单承载**，不再建独立容器 |
| N4·折叠唯一内核、骨架 schema 预热 | `SignalWorkflowRunner.FromFold` + R2 退役即此决策实现 | **已落地**，P3 无需额外移除回退开关（已退役） |

### 10.2 一次性收口范围（本次变更清单）

**新增**
- `Projects/Agent/src/Agent.McpRegistryHost/LlmChat.cs` —— llm_chat 纯封装核心（question/system/conversationContext/toolsJson → 结果文本/错误 JSON；toolsJson 合一 N1）。
- `Projects/Agent/src/Agent.Core/Signal/ILlmChatPort.cs` —— 折叠/answer 的 LLM 调用收敛端口（返回 `LlmResult` 含 ToolCalls，保留 Function Calling 往返）+ `LlmClientChatPort`（ILlmClient 适配实现）。
- `CentralTest/AgentTest/Agent.McpRegistryHost.Tests/`（csproj + `LlmChatTests.cs`，6 用例：纯聊天 / 带 toolsJson / conversationContext 前置 / 失败错误 JSON / 非法/空 toolsJson 回退）。
- `CentralTest/AgentTest/Agent.Core.Tests/LlmChatPortTests.cs`（2 用例：纯聊天转发 / 带 tools 转发且保留 ToolCalls）。

**修改**
- `McpServerTools.cs` —— `LocalLlm` 演进：委托 `LlmChat.ExecuteAsync`，扩展 `conversationContext`/`toolsJson` 参数（保留原 3 参兼容语义）。
- `SkConversationFoldOperator.cs` —— P1·去 `_llm`：字段 `ILlmClient _llm` → `ILlmChatPort _chat`（默认 `LlmClientChatPort(LlmGateway.Current)`），`ChatStepAsync` 经 `_chat.ChatAsync`；构造兼容注入 ILlmClient。
- `AgentFoldExecutors.cs`（AnswerFoldExecutor）与 `StatelessAnswerExecutor.cs` —— P1·去 `_llm`：同收敛到 `ILlmChatPort`，构造兼容注入 ILlmClient。

**验证**
- Agent.McpRegistryHost / Agent.Modules / Agent.Core 编译通过；`Agent.McpRegistryHost.Tests` 6/6、`LlmChatPortTests` 2/2 通过；既有折叠相关回归（SkConversationFoldOperator/SignalWorkflowRunner/FoldConversationRunner/CallAsyncRegression）18/18 通过。

### 10.3 留待独立分支（未并入本次收口）

- 无。本次已把 P1 去 `_llm` 以**端口收敛**（`ILlmChatPort`）纳入收口并验证；不做把折叠硬迁到 string `CallAsync("llm_chat")` 的形态——因 CallAsync 返回 string 无法承载 `LlmResult.ToolCalls`，会丢失 Function Calling 往返语义，属错误方向（已在此裁决）。若未来确需折叠经统一工具表寻址 LLM，应把该端口接入 composite/工具表而非改回 string seam。