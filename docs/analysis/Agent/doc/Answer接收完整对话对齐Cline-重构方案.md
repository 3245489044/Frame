# Answer 接收完整对话对齐 Cline —— 重构方案

> 状态：待实现（设计定稿）
> 适用范围：会话工作流 analyze 步（`answer` 函数）的输入与执行方式
> 目标版本：对齐 `cline_execute` 的「标准对话 → 喂入 → 解析结果」三段式，同时保留 Answer 的确定性轮次语义（`max_rounds=1` 即单轮）。

---

## 1. 背景与痛点

### 1.1 现状：两条腿不对称

| 维度 | Cline（`cline_execute`） | Answer（`answer`） |
|------|--------------------------|--------------------|
| 上下文 | 完整对话：`conversation_context`（priorWire 全量）+ `l2` 窗口 + ur_nodes 回灌 resume | 只有 `question`+`system`（[collect-analyze-act.json analyze 步 ParameterKeys](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/Config/workflows/collect-analyze-act.json#L23-L31)） |
| 执行载体 | 状态化 Cline daemon 编码代理（多轮、工具、resume） | 无状态直连 `LlmGateway` 一次聊天 |
| 会话状态 | 有 `SessionId`：回灌/归档 ur_nodes | 无 SessionId、无归档、无回灌 |
| 多轮 | Cline 自身是长循环代理 | 单发；多轮自精化在独立任务池路径 `StatelessAnswerAgent`/`StatelessAnswerLoop` |
| 结果解析 | 终答 → 工作流节点 + 挂 task | 同左（已满足） |

**核心缺陷**：会话 analyze 步的 Answer 是"失忆"的——连着发几条消息，answer 每次只看到刚发的那一条，看不到前面几轮；这与 Cline 能延续整段对话形成不对称。

### 1.2 目标

1. Answer 与 Cline 一样接收**完整对话**，且以**标准对话格式**（`ConversationMessage` → `[{role, content}]` wire）喂入。
2. 执行走**确定性轮次**：`max_rounds` 控制，`=1` 即单轮（与用户语义一致），`>1` 即多轮自精化。
3. 结果照旧解析成节点（这部分现状已满足，仅确认不回归）。

---

## 2. 核心设计：三段对称 Cline

Cline 已走通「标准对话 → 喂入 → 解析成节点」，Answer 复刻同一骨架：

```
Cline:  ConversationPipeline.SeedWorkflow 种 conversation_context(=priorWire 标准对话)
        → cline_execute 步 ParameterKeys 读它
        → ClineDaemonEndpoint.ComposePrompt 前置标准上下文块 → daemon → 结果成节点

Answer: (同源) conversation_context 已是标准对话
        → answer 步 ParameterKeys 读它 + max_rounds
        → StatelessAnswerLoop 确定性轮次(MaxRounds)
        → StatelessAnswerExecutor.ComposeUserPrompt 前置标准上下文块 → LlmGateway → 结果解析成节点
```

**完全复用既有机制，不新建模块**（符合硬约束"增强现有循环而非新建模块"）：

- 上下文种子：`ConversationPipeline.SeedWorkflow` 已种 `conversation_context`（[ConversationPipeline.cs#L438-L439](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs#L438-L439)）。
- 标准对话渲染：`IConversationAgentWire`（`[{role, content}]`，见 [ClineConversationWire](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Session/Wires/ConversationAgentWires.cs#L83-L103)）。
- 结果解析：`NodeExecutionContract` + `NodeTaskLinker` 挂 task（现状不改）。

---

## 3. 具体改动点

### 3.1 输入侧：Answer 消费标准对话

**文件**：`src/Agent.Modules/Answer/StatelessAnswerExecutor.cs`

- 新增可空字段 `ConversationContext`（标准对话 JSON `[{role, content}]`）。
- 在 [ComposeUserPrompt](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Answer/StatelessAnswerExecutor.cs#L120-L130) 开头前置该块——复用 Cline 的「以 `[` 开头判定为结构化 JSON」逻辑，对齐 [ClineDaemonEndpoint.ComposePrompt](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/ClineDaemonEndpoint.cs#L143-L157) 的「标准上下文（结构化 JSON）」标签。
- [ExecuteRequestAsync](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Answer/StatelessAnswerExecutor.cs#L187-L198)（单发路径）同样前置。
- **不改** `ChatAsync` / `LocalDbToolFacade`（仍是字符串 prompt 聊天；仅把标准对话并入 user prompt）。

### 3.2 工作流模板接入

**文件**：`Config/workflows/collect-analyze-act.json`

- analyze 步 `ParameterKeys`：`["question","system"]` → `["question","system","conversation_context","max_rounds"]`（只传该步所需，位置即语义）。

### 3.3 确定性轮次：analyze 步路由到 StatelessAnswerLoop

**文件**：`src/Agent.Modules/Answer/AnswerWorkflowFunction.cs`

- [ExecuteAnswerAsync](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Answer/AnswerWorkflowFunction.cs#L41-L58) 由"单发 `ExecuteAsync`"改为调用 [StatelessAnswerLoop.RunLoopAsync](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Answer/StatelessAnswerLoop.cs#L49-L78)：
  - 从 `context.Parameters` 读 `max_rounds`（**缺省 1** → 单轮，向后兼容零回归）。
  - `StatelessAnswerLoop` 用 `Token.Max` 承载 `MaxRounds`，确定性（非 LLM 自主）。
  - `=1` → 仅跑 1 轮即返回（等价单轮）；`>1` → 每轮带 `prev_answer` 自精化，`rule_match`/`AcceptancePolicy` 判定 `answerAccepted` 终止。
  - 幂等：直接原地跑（不经任务池），与当前会话同步驱动语义一致。
  - 结果经 `StatelessAnswerLoop.ToStatelessResult` 映射（rounds/finalText/stopReason 已解析），终答文本回流 `WorkflowRoundResult.Text` → 照旧成节点。

### 3.4 结果侧（不改）

回流 `result.Nodes` → `ExtractStages` → 气泡 + `NodeTaskLinker.LinkTaskNodesAsync` 挂 task。analyze 步产出合法节点已被 [ResolveProducedNodeKeys](file:///D:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs#L377-L390) 覆盖，无需改。

---

## 4. 保持不变的约束（硬约束清单）

- 函数均经 `context.Parameters`（FunctionContext）传参。
- 结果解析仍走 `NodeExecutionContract` + `NodeTaskLinker`。
- 不新建执行器/模块，只增强 `StatelessAnswerExecutor` / `StatelessAnswerLoop`。
- LLM 仍经 `LlmGateway`（GlobalLlmGate）唯一咽喉，`LlmMode.LocalOnly` 生效。
- `RoundPromptPlan`（逐轮提示词计划）保持正交，不受影响。
- 默认 `max_rounds=1` → 现有行为不变（零回归）。

---

## 5. 验证标准（分步验收）

| 编号 | 验收项 | 判据 |
|------|--------|------|
| V1 | 单轮 | 会话 send，analyze 步 `max_rounds=1` → 恰好 1 轮返回；user prompt 含完整对话前置块；`ur_nodes` 挂出 analyze 节点 |
| V2 | 多轮精化 | `max_rounds=3` → 问→评→改循环；`prev_answer` 跨轮携带；达标/预算终止语义正确，`answerAccepted` 归因不丢 |
| V3 | 对话记忆 | 连发多条后 send，answer 能看到前几轮（对比旧版失忆） |
| V4 | 标准格式 | 注入 answer 的对话块为合法 `[{role, content}]`（OpenAI 兼容形态）；空对话不注入 |
| V5 | 回归 | Cline 腿、`Agent.Core.Tests` / `Agent.Session.Tests`（StandardContext 系列）全绿；`max_rounds` 缺省时旧断言不变 |

---

## 6. 边界与待确认

- `max_rounds` 的权威来源：会话模板参数（本次采用，缺省 1）vs 逐轮提示词计划 `RoundPromptPlan`（可选后续合并）。
- 是否叠加 `StandardContextBuilder` 三粒度（node/task/history）选择器：本次先吃 `conversation_context`（全量标准对话）；粒度选择作为可选增强，后续接已种的 `context_scope` / `context_key` 种子。
- 完整对话的承载：复用现有 `conversation_context`（priorWire），其已是标准 `[{role, content}]` 形态，无需新增渲染器。

---

## 7. 改动文件索引

| 文件 | 改动类型 |
|------|----------|
| `src/Agent.Modules/Answer/StatelessAnswerExecutor.cs` | 增强：加 `ConversationContext`，ComposeUserPrompt/ExecuteRequestAsync 前置标准对话块 |
| `src/Agent.Modules/Answer/AnswerWorkflowFunction.cs` | 增强：读 `conversation_context`+`max_rounds`，analyze 步路由到 StatelessAnswerLoop |
| `Config/workflows/collect-analyze-act.json` | 模板：analyze 步 ParameterKeys 增加 `conversation_context`、`max_rounds` |
| `src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs` | 确认（种子已含 conversation_context，`max_rounds` 若需要新种子键则补） |