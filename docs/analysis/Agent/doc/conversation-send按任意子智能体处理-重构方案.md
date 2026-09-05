# conversation/send 按任意子智能体处理 — 重构方案

> 目标版本：把 `conversation/send` 从「固定三步链（collect→analyze→act）」彻底改为「**单步调用**：每次把当前会话（`conversation_context`）投喂给**单个**指定的子智能体处理」。

## 1. 背景与不合理

### 1.1 现状

`conversation/send` 当前不可指定目标，消息必走装配时注入的一条固定工作流链 `collect-analyze-act`：

```
collect (cline_execute) → analyze (answer) → act (cline_execute)
```

调用链（全部 C# 固定）：

```
AgentUi_ConversationSend(sessionName, text, ...)
  → ConversationSendStandardAsync
    → ConversationSessionService.SendAsync
      → ConversationPipeline.RunSignalAsync   // 沿注入的单一 _workflow(RoundSpec) 跑完整条链
```

### 1.2 不合理点

1. **粒度与意图错位**：我只是想「让 Answer/Cline 处理这一条消息」，系统却强制跑三层、串行打两次 Cline + 一次 Answer，产生大量无关气泡 / nodePorts / callLog 冗余上下文。
2. **无法直选**：没有「本轮把会话交给谁」的概念；`contextScope/contextKey/contextRole` 也只是给 `cline_execute` 挑上下文粒度，不选处理者。
3. **工作流 = 会话行为的唯一来源**：换子智能体要改装配常量指向的文件，主循环不可按需切换。

## 2. 目标

- `send` 携带 `agent`（目标子智能体，**必选**），取值 `cline` | `answer`（可扩展更多）。
- 把「当前会话历史（`conversation_context`）+ 本轮消息（`question`）」**只投喂给选中的那一个**。
- 完全放弃三步链作为 send 的默认行为；`send.json` 模板参数默认 `agent=answer`。
- 复用既有函数与工作流引擎；不新建模块（硬约束：增强现有循环）。
- 零依赖回归：只 `send` 语义变化；`history/state/list/addNode` 与旧工作流文件不受影响。

## 3. 设计决策（已确认）

| 决策 | 取值 | 影响 |
|------|------|------|
| send 是否保留三步链 | **完全放弃**（改单步） | 删除装配默认工作流的三步语义；send 不再默认链式跑 Cline→Answer→Cline |
| `agent` 是否必选 | **必选**，模板默认 `answer` | 缺省值仅存在于 `/Templates/AgentRun/conversation/send.json`，运行时未解析到合法 agent 则返回明确诊断 |
| 落地方式 | **先出本设计文档**（审阅后实现） | 本文件为唯一权威，实现按文件执行 |

待确认：单步 spec 的来源（见 §7）。

## 4. 目标链路

单步 `send`（以 `agent=answer` 为例）：

```
AgentUi_ConversationSend(sessionName, text, agent="answer", ...)
  → ConversationSendStandardAsync
    → ConversationSessionService.SendAsync(session, text, agent)
      → ConversationPipeline.RunSignalAsync(session, signal, agent)
        1. 定位/创建会话、追加 user 消息、持久化
        2. SeedWorkflow(...)                       // 不变：已种 question/conversation_context/system/MaxRounds
        3. spec = _specResolver("answer")          // → 单步 RoundSpec(answer)
        4. runner = _runnerFactory(spec)           // → WorkflowRunner(单步 spec)（缓存）
        5. result = await runner(lease, ct)        // 只跑 answer 一步
        6. 分层回写 + 终答 + Round/Status + 持久化  // 复用现有逻辑，零改动
```

## 5. 分层改动

### 5.1 请求层

`/FileDriven/DrivenConfig/Templates/AgentRun/conversation/send.json` 增加 `agent`（必选，默认 `answer`），并同步 `_doc`、`conversation/README.md`、`AgentRuntimeUI.json` 的 toolTip/请求体示例。

`AgentUiLibraryConversation`：

- 注册签名 `AgentUi_ConversationSend` 增加 `agent` 参数（`string`，空 → 诊断「send 必须指定 agent（cline|answer）」）。
- `ConversationSendAsync/StandardAsync` 透传 `agent` → `svc.SendAsync(..., agent)`。

> 保留 `contextScope/contextKey/contextRole` 作为**可选增强**：它们仍只影响 `cline_execute` 的上下文粒度；`agent=answer` 时其内部 `conversation_context` 恒定全量，不受影响。

### 5.2 spec 解析层（新增轻量注册表）

新增单一来源把 `agent` 名 → 单步 `RoundSpec`，对齐既有 `RoundSpecLoader`「声明式 JSON 加载 + 内置兜底」惯例：

| agent | RoundSpec(Name) | Steps |
|-------|-----------------|-------|
| `cline` | `session-cline` | `RoundStep("cline_execute", [system, l1, l2, session_id, round, conversation_context], [exec_result])` |
| `answer` | `session-answer` | `RoundStep("answer", [question, system, conversation_context, max_rounds], [analyze_result])` |

两者 `Source=BuiltinDefault`、`SuccessCriterion=RunToEnd`（会话语义：跑完即成功，不依赖 decision 词）。

- 公开解析接口：`RoundSpec? ResolveSessionSpec(string agent)`（未识别的 agent 返回 null → 上层给诊断）。
- 未来新增子智能体：加一行注册 + 一条配置文件即可，管线零改动。

### 5.3 管线层（核心改动）

`ConversationPipeline` 现持有**单一** `_workflow` 且 `_runWorkflow` 是一次 `BuildDefaultRunner(workflow)`（`WorkflowRunner` 的 `_spec` 只读绑定一条链）。重构为该两个可注入工厂：

```csharp
Func<string, RoundSpec?> _specResolver;                     // agent/工作流名 → 单步 spec（null 表示不识别的 agent）
Func<RoundSpec, Func<AxiomContext, CancellationToken, Task<WorkflowRoundResult>>> _runnerFactory;
```

- `RunSignalAsync(..., string? agent = null)`：沿目标 `spec`（`_specResolver(agent)`）取 runner 跑本轮；`spec` 为 null → 返回可观测诊断，不消耗额度。
- `WorkflowRoundResult` / `ExtractStages` / 终答 / 持久化等回写逻辑**整体复用**，仅"跑哪条链"由 `agent` 决定。
- 取消 pipeline 对三步链的任何隐含假设（无 `_workflow` 固定字段）。

### 5.4 装配层（ConversationHosting）

- 组建 `agent→spec` resolver（加载声明式文件 + 内置兜底，见 §7）。
- 组建配好基础设施的 runner 工厂（`StateCenter` / `keyMappings` / `nodeStore` / `grounding`），**按 spec 进程内缓存** `WorkflowRunner` 实例（同一 spec 复用一个实例；不同 spec 各自实例，`_spec` 只读）。
- 不再用 `DefaultWorkflowFile = "collect-analyze-act.json"` 作为会话装配的单一骨架来源。

### 5.5 种子复用（零改动验证）

`SeedWorkflow` 已统一种子（[ConversationPipeline.cs#L420-L450](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs#L420-L450)），单步两 agent 所需参数均可用：

| 键 | 值 | cline 步消费 | answer 步消费 |
|----|----|:---:|:---:|
| `question` | 本轮消息文本 | ✓ | ✓（主源） |
| `user_input` / `l1` | 同上（兼容别名） | ✓ | — |
| `conversation_context` | 先前会话 wire（全量） | ✓ | ✓ |
| `system` | 会话系统提示 | ✓ | ✓ |
| `session_id` / `round` / `max_rounds` | 编排上下文 | ✓（session/round） | ✓（max_rounds） |

**结论：`SeedWorkflow` 不改。**

## 6. 删除 / 兼容策略

- `conversation/send` 不再默认走 `collect-analyze-act`。该三步链的**装配默认地位取消**（`DefaultWorkflowFile` 不再作为 send 骨架来源）。
- 保留 `Config/workflows/collect-analyze-act.json` 文件本身：仍可能被历史快照、`collect-analyze-act-mcp`（MCP 工具面，见《三层工作流 MCP 封装设计》）等既有入口引用；删除会有回归。仅断开「send 默认引用」。
- 只读 ops `history/state/list` 与 `addNode` 不涉及执行器选择，**行为与文件均不动**。
- `agent` 必填是对 send 消费方的行为变更：脚本/外部调用需显式给 agent；缺省仅存在于 UI 模板（默认 `answer`）。

## 7. 待确认决策：单步 spec 来源

> 用户此前未从二选一（声明式 JSON / 仅内置）中做最终选择，故在此开放。推荐 **声明式 JSON + 内置兜底**。

- **选项 A（推荐）**：新增 `Config/workflows/session-cline.json`、`session-answer.json`（换文件即换行为，契合既有惯例）；加载失败/缺失回退内置单步 spec。
- **选项 B**：仅 C# 内置硬编码两步 spec，不加配置文件，改动面最小。

其余两项已定：**默认行为 = 放弃三步链改单步**；**agent 必选、模板默认 answer**。

## 8. 变更清单

| 文件 | 改动 |
|------|------|
| `Templates/AgentRun/conversation/send.json` | +`agent`（默认 `answer`）+`_doc` 更新 |
| `Templates/AgentRun/conversation/README.md` | 字段表 + 示例更新 |
| `JsonConfig/AgentRuntimeUI.json` | toolTip / 请求体示例更新 |
| `Agent.Launcher/Modules/AgentUiLibraryConversation.cs` | `AgentUi_ConversationSend` 增 agent 参数并透传 |
| `Agent.Orchestration/SessionWorkflow/ConversationSessionService.cs` | `SendAsync` 增 agent 透传 → pipeline |
| `Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs` | `_workflow`→`_specResolver`+`_runnerFactory`；`RunSignalAsync` 增 agent |
| `Agent.Core/Nodes/RoundSpecLoader.cs`（或新解析器） | 新增 `ResolveSessionSpec(agent)`；内置两步 spec |
| `Agent.Launcher/Composition/ConversationHosting.cs` | 组装 resolver + runnerFactory；取消三步链默认骨架 |
| （选项 A）`Config/workflows/session-cline.json`、`session-answer.json` | 新增单步工作时流程定义 |

## 9. 验证

1. 编译 `Agent.sln` 0 错误。
2. `agent=answer`：发送后历史仅有 1 条工具气泡（answer 步），终答=Answer 文本，`conversation_context` 已被投喂；CallLog 递增可审计。
3. `agent=cline`：历史 1 条工具气泡（cline_execute 步），resume 上下文生效。
4. 非法 agent：返回明确诊断，不消耗额度，历史不追加 LLM 气泡。
5. `history/state/list/addNode` 行为不变（用现有 UI 回归）。
6. 三层 MCP 入口 `caa_run` 仍可用（collect-analyze-act 文件保留）。

## 10. 风险

- **runner 多实例**：`WorkflowRunner._spec` 只读，需按 spec 各自实例 + 缓存；是本次唯一架构重心（其余纯参数穿透）。
- **send 语义变更**：任何直接调 `AgentUi_ConversationSend` 且未传 agent 的既有脚本会得到诊断，需适配传 `agent`。