# 信号驱动 + Semantic Kernel 会话主循环整合方案（当前状态与后续步骤）

> **版本**：1.2　**日期**：2026-08-29（v1.2 记录 Step 3 已落地：信号驱动内核切换 + executor 进程内直调 + 删旁路；保留 WorkflowRunner 回退开关）
> **范围**：历史主循环（会话历史主循环）改造 —— 信号驱动步进 + SK 执行 + MCP 工具，整合为单一路径
> **性质**：状态快照 + 实施计划（含已实施 ✅ / 待实施 ⏳）
> **关联**：`docs/analysis/2026/8/8.28/AgentRuntimeUI_历史主循环调用链.md`（历史主循环调用链）、`.clinerules/axioms.md`（SEP.DeterministicCoreDisjoint / SkFunctionBinder 边界）

---

## 一、目标与架构原则

把历史主循环（`ConversationPipeline` 会话推进）改造为**信号驱动步进 + SK 执行 + MCP 工具**的统一架构，**单一路径**，不另开辟旁路。

| 原则 | 说明 |
|------|------|
| 推进唯一机制 | 信号驱动步进：`ExternalInput`（外部直接输入）∪ `StepCompleted`（子智能体/子步骤产出），链终点 `run_to_end` 收敛 |
| 步进循环 + 信号解析在框架 | `CsharpPrj/Operation/LoopFoldMethod`（Agent 无关、LLM-free，复用 `FoldEngine`） |
| SK 逻辑在 Agent 侧 | `Projects/Agent`：执行算子 + 技能注入 + MCP 工具 |
| 调用逻辑写在 skill | 每步调哪个执行端（answer/cline/sk）由该步 `SKILL.md` 的 `executor` 声明 |
| 主循环执行端无关 | answer/cline 只是能力，由 skill 选用；**主循环内部进程内直调**，外部统一工具面经 C# MCP server（`Agent.McpServer`） |

---

## 二、当前已实施状态（已构建通过、测试通过）

### 2.1 框架层（`LoopFoldMethod/Signal/`，新增）
| 文件 | 职责 |
|------|------|
| `FoldSignal.cs` | `FoldSignalKind{ExternalInput,StepCompleted,Terminal}` + `FoldSignal<TVertex>` + `SignalFoldStep<TState,TVertex>` |
| `ISignalResolver.cs` | 信号解析端口 + `SignalDirective`（null=链终点终止） |
| `ChainSignalResolver.cs` | 表驱动链式解析器（已完成键→下一步键，on_success 语义） |
| `ISignalDrivenOperator.cs` | 信号驱动算子端口（消费信号→展开一步） |
| `SignalDrivenFoldEngine.cs` | 信号驱动步进引擎（复用 `FoldEngine`；消费信号→解析→展开→产下一信号→链终点/预算/取消终止） |

### 2.2 Agent 侧（`Projects/Agent`）
| 文件 | 职责 |
|------|------|
| `Agent.Core/Signal/ConversationFoldState.cs` / `ConversationFoldVertex.cs` | 折叠状态/每步结论（TState/TVertex） |
| `Agent.Core/Signal/IMcpFoldToolProvider.cs` | MCP 工具端口：`GetToolsAsync`（Function Calling 工具集）+ `CallAsync`（命名工具直接经 MCP 调用） |
| `Agent.Core/Signal/SkConversationFoldOperator.cs` | 算子：选 skill → skill 声明 executor（非 sk）→ **优先进程内直调**（Step 3）；未命中才经 MCP 调用；否则 SK 聊天 + MCP 工具（Function Calling） |
| `Agent.Core/Signal/ConversationFoldRunner.cs` | 组装 `SignalDrivenFoldEngine`+`ChainSignalResolver`+预算，一次 ExternalInput 驱动整链 |
| `Agent.Core/Signal/IFoldInProcessExecutor.cs` | 进程内执行端端口（answer/cline）；Skill executor 分支进程内直调（Step 3 新增） |
| `Agent.Modules/Signal/AgentFoldExecutors.cs` | `AnswerFoldExecutor`（复用 `StatelessAnswerExecutor`）/ `ClineFoldExecutor`（复用 `ClineAgentExecutor`），进程内直调实现（Step 3 新增） |
| `Agent.Modules/Signal/McpFoldToolProvider.cs` | 桥接 `McpClientManager`（外部 MCP bridge）为 `LlmTool`/`CallAsync` |
| `Agent.Modules/MCP/McpClientManager.cs` | MCP 客户端门面，内部用官方 `ModelContextProtocol` SDK 的 `McpClient`/`StdioClientTransport`（取代自研 stdio JSON-RPC 读循环，SingleImplementation/TH-12）；外部 MCP 工具唯一连接真源 |
| `Agent.Axioms/Skills/SkillDefinition.cs` | 新增 `Executor` 字段（skill 前端声明 `executor: answer\|cline\|sk`） |
| `Agent.Axioms/Skills/SkillCatalog.cs` | 解析 `executor:` 元数据 |
| `Agent.Orchestration/SignalDriven/ConversationSignalFoldAdapter.cs` | 既有 `ConversationSignal` → 框架 `FoldSignal` 桥接 |
| `Agent.Orchestration/SessionWorkflow/FoldConversationRunner.cs` | 折叠 runner（`_runnerFactory` 端口实现）：spec→chain + 折叠→`WorkflowRoundResult` 映射（Step 3 新增） |
| `Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs` | 保持 `RunSignalAsync` 编排层（历史/状态/节点/回显）；删除旁路 `RunSignalByFoldAsync`（Step 3） |

### 2.3 测试与构建状态
- `LoopFoldMethod.Tests`：47/47 通过（新增 `SignalDrivenFoldEngineTests` 6 项）
- `Agent.Core.Tests/SkConversationFoldOperatorTests`：4/4 通过（纯聊天 / 带 MCP 工具 / 工具空集回退 / **skill 声明 executor → MCP 调用**）
- `Agent.sln`、`Operation.csproj`：均 0 警告 0 错误

### 2.4 本次落地进展（2026-08-29）
| 里程碑 | 内容 | 状态 |
|--------|------|------|
| Step 1 | `conversation-answer` / `conversation-cline` 两个 SKILL.md + `SkillCatalog` 解析 `executor` 测试 | ✅ 已实施（`SkillCatalogTests` 7/7） |
| Step 2 | 新增 `Agent.McpServer`（官方 `ModelContextProtocol` 2.2.0）把 answer/cline/local_llm 包装为 stdio MCP server；stdio 冒烟 `initialize`+`tools/list` 通过 | ✅ 已实施 |
| Step 3 | `RunSignalAsync` 信号驱动内核切换 + executor 进程内直调 + 删旁路/旧路径 | ✅ 已实施（`FoldConversationRunner` + `IFoldInProcessExecutor` 进程内直调；删除 `RunSignalByFoldAsync`；`WorkflowRunner` 降级回退开关） |

---

## 三、步骤分解（已实施 ✅ / 待实施 ⏳）

### Step 1 · 会话步 SKILL.md（调用逻辑写入 skill）—— ✅ 已实施
新建 `Projects/Agent/skills/conversation-answer/SKILL.md` 与 `conversation-cline/SKILL.md`（已建，front-matter 用干净 `executor: answer|cline`，**无行内注释**，避免 `SkillCatalog.ParseFrontMatter` 把注释吃进 `Executor` 值导致 `CallAsync` 工具名匹配失败）：

```markdown
---
name: conversation-answer
description: 会话 answer 步 —— 调用 answer 执行端（SK + 本地库工具）产出分析
kind: operation
tags: [conversation, answer]
trigger: conversation-answer
executor: answer
---
# 调用逻辑
1. 经 `mcp_call_tool` 调 `answer` 工具，入参 {question, system, stepKey, stateKey}；
2. 返回文本作为本步产出/终答。
```

`conversation-cline/SKILL.md` 同理，`executor: cline`。

### Step 2 · answer / cline / local_llm 包装为 C# MCP server —— ✅ 已实施（架构调整）
> **架构调整（2026-08-29）**：原方案为「注册进 Node MCPBridge（TypeScript）→ StorageBridge 装配 LLM」的跨进程 C#→TS→C# 绕行。落地时确认 `answer/cline` 均为 C# 侧能力、`McpClientManager` 本身就是 C#，走 Node 桥纯属绕行。改为**用官方 `ModelContextProtocol` SDK（2.2.0）新增 `Agent.McpServer`** 把 C# 能力直接包装为 stdio MCP server，**C#→TS 边界只保留真正的外部 MCP 逻辑**：
- `Agent.McpServer`（Exe，net10.0-windows）：`Program.cs` 装配 `AgentConfig → LlmHosting.Configure`（`LlmGateway`）+ Cline，`AddMcpServer().WithStdioServerTransport().WithTools<AgentTools>()`；
- `Tools/AgentTools.cs`（`[McpServerToolType]`/`[McpServerTool]`）暴露 `answer`（`StatelessAnswerExecutor`）/ `cline`（`ClineAgentExecutor`）/ `local_llm`（统一 LLM 网关含 LocalLlm），执行端经 DI 注入（进程内直调，零 Node 绕行）；
- 入参 `question/system/stepKey/stateKey`；stdio 冒烟 `initialize` + `tools/list` 通过（三工具注册、执行器被识别为 DI 依赖而非输入）；
- **MCP stdio stdout 卫生**：`LogRepository.Enabled=false` 禁用 Agent 业务日志，stdout 仅承载 JSON-RPC 帧（避免日志污染协议）；
- 边界：确定性核心（Budget/Token/Flag/Round）工具不进 LLM 自治集；`versions.json` 已注册 `ModelContextProtocol 2.2.0`。

### Step 3 · 整合单一路径 + 删除旧路径 —— ✅ 已实施（2026-08-29）
把执行内核整合进 `RunSignalAsync`（单一入口），删除旧 `WorkflowRunner` 会话路径与独立 `RunSignalByFoldAsync`：
- `RunSignalAsync` 保持编排层不变（历史/状态/节点/回显），仅把「`_runnerFactory(spec)` 整链跑」换成「`SignalDrivenFoldEngine` 信号驱动逐步推进」；
- 删除独立 `RunSignalByFoldAsync`（"另开辟"的旁路）；
- 会话工作流线性链 → `ChainSignalResolver` 的 chain 字典（`start→collect→analyze→act`）；
- 每步执行由 `SkConversationFoldOperator` 承担；executor 分支按架构调整改为**进程内直调**：`answer` 复用算子进程内 `_llm`、`cline` 经注入的进程内 `ClineAgentExecutor`，未命中才走 MCP（外部统一工具面由 `Agent.McpServer` 提供）；

**落地实现**（`FoldConversationRunner.cs`，Agent.Orchestration.SessionWorkflow）：保持 `_runnerFactory(spec)` 端口不变，以信号驱动折叠 runner 作为其新实现 —— `RunSignalAsync` 主体零改动，§四 管线能力对等复用（SingleImplementation / TH-12）：
- `FoldConversationRunner.BuildChain(spec)`：`spec.Steps`（EffectiveNodeId）→ chain 转移表 `start→step0→…`；
- `FoldConversationRunner.Build(spec, op)`：返回适配 `_runnerFactory` 的委托，内部组装 `SignalDrivenFoldEngine`+`ChainSignalResolver`+`BudgetCutoffPolicy`+预算，一次 ExternalInput 沿链推进；折叠产出映射回 `WorkflowRoundResult`（终答=末位状态产出、分层气泡=按 `spec.Steps.ResultKeys[0]` 填充 `Outputs` 走 `ExtractStages` 回退路径、非 Success → ErrorClass 归因）；
- executor 进程内直调：新增 `Agent.Core/Signal/IFoldInProcessExecutor`（端口）+ `Agent.Modules/Signal/AgentFoldExecutors.cs`（`AnswerFoldExecutor` 复用 `StatelessAnswerExecutor`、`ClineFoldExecutor` 复用 `ClineAgentExecutor`）；`SkConversationFoldOperator` 新增 `inProcessExecutors` 构造参数，executor 分支优先进程内直调 → 未命中回退 MCP → 两者皆缺 SK 聊天兜底；
- 装配：`ConversationHosting.TryBuildFoldOperator()`（LLM 网关 + 进程内执行端 + `McpFoldToolProvider`）装配成功 → runnerFactory 用折叠内核；未装配 → 回退 `WorkflowRunner`（回退开关，验证后移除）。

> **关键**：旧路径的管线级能力必须保留（见 §四），整合时不得丢失。

---

## 四、Step 3 必须保留的管线能力（旧路径有、新路径目前未实现）

`RunSignalAsync` 的编排层是会话语义承载，整合时**必须在新的执行内核下原样保留**：

| 能力 | 旧路径 | 新路径现状 | 归属 |
|------|--------|-----------|------|
| 历史加载 `LoadLogAsync`（ur_histories 真源） | ✅ | ❌ | 编排层 |
| 历史追加（ExternalInput→AppendUser / SubResult→AppendSubAgent） | ✅ | ❌ | 编排层 |
| 历史持久化 `PersistHistoryAsync`（ur_histories + task→node cross_db_link + sequence 边） | ✅ | ❌ | 编排层 |
| 节点产出持久化（onAfterStep → ur_nodes NodeType=output） | ✅ | ❌ | 编排层 |
| 会话状态回写 `FinalizeConversationStateAsync`（Round/Status/UpdatePayload/WriteStatus，D1 完成信封） | ✅ | ❌ | 编排层 |
| 节点→规范对话映射 `MapToConversation`（仅 assistant 写历史、节点不入 ur_histories） | ✅ | ❌ | 编排层 |
| Reply 完整历史快照 + 自动 wire（UI 渲染 + Cline/Claude 投喂上下文） | ✅ | ❌ | 编排层 |
| 后端审计（M5 `__backend_audit`） | ✅ | ❌ | 编排层 |
| 空终答/错误按 ErrorClass/ErrorStep 归因（E2） | ✅ | ❌ | 编排层 |

> 这些是**编排器职责**，与"信号驱动步进"正交，属于必须保留的会话语义。Step 3 整合时，折叠产出的 `vertices`/终答映射回 `stages`/`result` 形状，复用既有下游历史/状态/回显逻辑，零重复。

---

## 五、边界与红线（保持不破）

| 约束 | 落点 |
|------|------|
| `SEP.DeterministicCoreDisjoint` | 步进循环（`SignalDrivenFoldEngine`）与确定性核心（Budget/Token/Flag/Round/任务池裁决）不被 LLM 自治触碰；MCP 工具中触确定性核心的**不进** LLM 工具集 |
| `EngineSkeletonFunctionCatalog` / `SkFunctionBinder` | 引擎执行链函数（guard_input/provide_prompt/cline_execute/rule_match/guard_output）不注册为 LLM Function Choice 工具；折叠操作走确定性调用 |
| `SingleImplementation` / TH-12 | MCP 工具以 `McpClientManager` 为唯一连接真源；skill 提示词复用 `SkillSelector`/`SkillPromptAssembler`；不复制历史/状态/节点逻辑 |
| C#→TS 边界 | C# 能力（answer/cline/LocalLlm）由 `Agent.McpServer` 在 C# 侧暴露；Node MCPBridge 只承载真正的外部 MCP 工具（sequential-thinking/context7 等），杜绝 C#→TS→C# 绕行 |
| 推进唯一性 | 无论 ExternalInput 还是 StepCompleted，都经信号驱动步进，无第二条推进链 |

---

## 六、风险与回归

- **Step 3 为高改动面**：切换 `RunSignalAsync` 执行内核触及运行时，须先保证 §四 管线能力在折叠内核下对等，否则历史渲染/落库/状态回写回归。
- **Step 2 引入新 NuGet（`ModelContextProtocol` 2.2.0）**：官方 SDK，需网络恢复；`Agent.McpServer` 为独立 Exe，真实工具调用（`local_llm`/`cline`）依赖 `LlmGateway`/Cline 凭据装配与宿主环境（当前 agent-config `Provider=deepseek` 触网，未实际调用验证）。
- **MCP stdio stdout 卫生**：已通过 `LogRepository.Enabled=false` 解决；若后续需要 Agent 诊断日志，须改写到 stderr（MCP stdio 原则），勿写 stdout。
- **回归兜底**：skill 未命中 → 空技能块回退现状；MCP 未装配/失败 → 空工具集/诊断文本；折叠异常 → 可回退（建议 Step 3 落地时保留旧执行内核为回退开关，验证后移除）。

---

## 七、验收清单

- [x] Step 1：`conversation-answer` / `conversation-cline` 两个 SKILL.md 已建，front-matter `executor` + 正文调用逻辑
- [x] Step 2：`Agent.McpServer`（C# 官方 MCP SDK）暴露 `answer`/`cline`/`local_llm`，`initialize`+`tools/list` 冒烟通过
- [ ] Step 2b：真实 `mcp_call_tool` 调 `answer`/`cline`/`local_llm` 返回结构化产出（需本地/真实 LLM 凭据环境验证）
- [x] Step 3：`RunSignalAsync` 走信号驱动步进；历史/状态/节点/回显/审计能力对等（无回归）
- [x] Step 3：删除独立 `RunSignalByFoldAsync`（旁路）；`WorkflowRunner` 降级为回退开关（验证后移除旧会话路径）
- [x] 测试：`LoopFoldMethod.Tests` 47/47 全绿；`Agent.Core.Tests` 257/257 全绿（新增 `FoldConversationRunnerTests` 4 项 + `SkConversationFoldOperatorTests` 扩展进程内直调 3 项）；`Agent.sln` 0 警告 0 错误
- [x] 边界复核：确定性核心未被 LLM 自治触碰；引擎执行链函数不进 Function Choice 工具集
