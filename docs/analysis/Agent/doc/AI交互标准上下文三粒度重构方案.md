# AI 交互标准上下文（StandardContext）三粒度重构方案

> **版本**：1.0
> **日期**：2026-08-25
> **状态**：方案定稿（分析交付；实施按 P1–P6 分阶段）
> **模块范围**：`Projects/Agent/src/Agent.Modules/Cline|Answer`、`Projects/Agent/src/Agent.Core/Prompts`、`Projects/Agent/src/Agent.Orchestration/SessionWorkflow`、`CsharpPrj/Share/Conversation`、`CsharpPrj/Operation/HierarchyMethod`、`Projects/Agent/Config/workflows/*.json`、`FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json`
> **目标**：把 AgentRuntimeUI 控制的 **LLM 循环**从「L0/L1/L2 prompt 字符串驱动」改为「**标准上下文（StandardContext）驱动**」——支持把 **单 node / 单 task / 单 history** 三种粒度统一构建为标准上下文，渲染为**适配 cline 历史 JSON 格式**的会话历史，使**所有 AI 交互都基于标准上下文**进行。
> **前置**：三层（nodes/task/history）通用解耦已落地（见 `nodes-task-history三层通用解耦设计.md`）；task↔node 真多对多 + 历史节点集派生已落地（引用端口 `IHierarchyReferenceManager`）。

---

## 〇、一句话总览

> 引入**标准上下文 `StandardContext`**（纯数据、零 LLM）作为**所有 AI 交互的唯一输入**：按 **node / task / history** 三种粒度把「单节点输出 / 单任务对话历史 / 单历史容器（其全部任务并集）」统一投影为规范消息序列（复用 `ConversationMessage`），再经 `IConversationAgentWire`（`ClineConversationWire`）渲染为 **cline 历史 JSON `[{role, content}]`** 投喂 AI。LLM 循环的 `cline_execute` 由「拼 prompt 字符串」改为「按粒度构建标准上下文 → 渲染 cline 历史 JSON → 结构化投喂」。

---

## 一、目标与设计原则

### 1.1 目标

1. **三粒度统一**：支持只把**单 node** 构建为标准上下文；只把**单 task** 构建为标准上下文；只把**单 history** 构建为标准上下文。
2. **cline 历史可适配**：标准上下文渲染为 **cline 历史 JSON 格式**（`[{role, content}]`，`content` 前置 `[stage]` 保留分层）。
3. **AI 交互单一入口**：所有 LLM 交互（主循环 `agent-loop`、会话 `collect-analyze-act`、任务池执行器）都基于标准上下文，而非散落的 prompt 字符串。
4. **不破坏既有**：保留 L0/L1/L2 提示词路径作为回退；标准上下文为叠加/增强，非替换。

### 1.2 设计原则

| # | 原则 | 语义 |
|---|------|------|
| P1 | **纯数据、零 LLM** | `StandardContext` 与构建器/渲染器位于 `Share.Conversation`（或 HierarchyMethod.History），零 Agent 依赖、零 LLM 调用 |
| P2 | **复用既有规范结构** | 标准上下文的 `Messages` 复用 `ConversationMessage`（Role/Stage/SubAgent/Text/Round），不新造消息模型 |
| P3 | **三粒度同构** | node / task / history 构建器产出**同一** `StandardContext.Messages` 形态，差异只在数据来源范围 |
| P4 | **引用图 + ur_histories 双源** | 归属关系（history→task→node）经 `IHierarchyReferenceManager`；对话内容经 `IHistoryStore`（ur_histories）/ `NodeOutputStore`（ur_nodes） |
| P5 | **渲染可插拔** | 复用 `IConversationAgentWire`（cline/claude/openai），不硬编码 cline |
| P6 | **回退兼容** | `contextScope` 缺省走原 prompt 逻辑，标准上下文为叠加，避免回归 |


---

## 二、现状盘点（对应当前代码）

### 2.1 LLM 循环链路（agent-loop.json + cline_execute）

```
agent-loop.json 工作流（RoundSpec）：
  guard_input → provide_prompt → cline_execute → rule_match（零 LLM 规则循环，回环）
```

| 步骤 | 函数 | 现状 |
|------|------|------|
| `provide_prompt` | `PromptFrameworkFunction.AssembleLayersAsync` | 组装 **L0/L1/L2**（system/user1/user2） |
| `cline_execute` | `ClineExecutorFunction.ExecuteClineAsync` | 读 system/l1/l2 → `IPromptComposer.Compose` → `AgentExecutorRequest(System, UserContents[], ConversationContext)` |
| 执行器 | `ClineDaemonEndpoint.ComposePrompt` | 把三段**拼成单个 prompt 字符串** → `ClineClient.AskAsync(prompt)` → daemon `run({prompt})` → SDK `StartSessionInput.prompt` |

> **结论**：当前 AI 交互是 **prompt 字符串驱动**；历史只作为 `conversation_context` **文本块前置**（`ClineDaemonEndpoint.cs#L141-154`），非结构化 cline 历史 JSON 数组。

### 2.2 已有的"标准对话"基建（已存在但未接入主循环 cline_execute）

| 组件 | 位置 | 能力 |
|------|------|------|
| `ConversationMessage` / `ConversationLog` | `CsharpPrj/Share/Conversation` | 规范消息/历史模型（Role/Stage/SubAgent/Text/Round/窗口/序列化） |
| `IConversationNodeMapper` / `ConversationNodeMapper` | `Share/Conversation/Adapters` | 节点贡献 → 对话消息（双向） |
| `ConversationNodeContribution` | `Share/Conversation/Adapters` | 单节点贡献（Stage/SubAgent/Text/Round） |
| `IConversationAgentWire` / **`ClineConversationWire`** | `Share/Conversation/Adapters` | 对话 → cline 历史 JSON `[{role, content}]` |
| `ConversationPipeline` | `Agent.Orchestration/SessionWorkflow` | 把先前会话历史渲染为 cline wire → `conversation_context` 透传 |
| 三层引用端口 | `Operation.HierarchyMethod.History` + `DataStoreDriven.IHierarchyReferenceManager` | history→task→node 枚举（历史节点集派生） |

### 2.3 缺口（Gap）

| # | 缺口 | 说明 |
|---|------|------|
| ① | 无统一标准上下文抽象 | 不能按 node / task / history 三粒度统一构建 |
| ② | AI 交互基于 prompt 字符串 | `cline_execute` 只吃 L0/L1/L2 文本；历史是文本拼接，非结构化 cline 历史 JSON 数组 |
| ③ | 三粒度未打通 | 单节点输出、单任务历史、单历史容器无统一入口投影为同一标准上下文 |
| ④ | 三层引用未接入上下文构建 | `IHierarchyReferenceManager` 未用于"按历史/任务枚举节点来组装上下文" |


---

## 三、目标设计：StandardContext

### 3.1 核心模型（落 `Share.Conversation`，纯数据、零 LLM）

```csharp
// Share/Conversation/StandardContext.cs
public enum StandardScope { Node, Task, History }

public sealed record StandardContext(
    StandardScope Scope,                              // 粒度：Node / Task / History
    string ScopeId,                                   // nodeKey / taskKey / historyId
    string? WorkflowId,                               // node 作用域需要（空则用默认工作流）
    IReadOnlyList<ConversationMessage> Messages);     // 规范消息序列（复用 ConversationMessage）
```

### 3.2 三粒度构建器 `IStandardContextBuilder`

| 构建器 | 数据来源 | 产物 |
|--------|----------|------|
| `BuildNodeAsync(workflowId, nodeKey)` | `NodeOutputStore.TryGetNodeOutputAsync`（单节点输出端口） | 一条/多条 `ConversationMessage`（`Role=Tool/Assistant`，`Stage=nodeKey`） |
| `BuildTaskAsync(taskKey)` | `IHistoryStore`（ur_histories `ConversationLog`）+ `IHierarchyReferenceManager.GetTaskNodeKeysAsync` | 该任务累积对话 + 其节点产出 → 完整消息序列 |
| `BuildHistoryAsync(historyId)` | `IHierarchyReferenceManager.GetHistoryTaskKeysAsync` → 各任务上下文 | 该历史全部任务的对话**并集** → 完整消息序列 |

> 三者产出**同一** `StandardContext.Messages` 形态 → 统一为"标准上下文"。

### 3.3 渲染器 `IStandardContextRenderer`

- `RenderToClineAsync(context)` → **cline 历史 JSON `[{role, content}]`**（复用 `ClineConversationWire`）——即"适配 cline 历史 json 格式"。
- `RenderToWireAsync(context, wireName)` → 泛化（openai / claude / cline），复用 `ConversationAgentWireRegistry`。

### 3.4 AI 交互接入（关键改动）

`cline_execute` 从"拼 prompt 字符串"改为"**按粒度构建 StandardContext → 渲染为 cline 历史 JSON → 结构化投喂**"：

- `AgentExecutorRequest` 增加结构化上下文字段（如 `IReadOnlyList<ConversationMessage>? Context` 或 `StandardContext?`），**替代/增强**现有 `ConversationContext` 文本拼接（`IAgentExecutor.cs#L28-39`）。
- `ClineDaemonEndpoint` 把标准上下文渲染的 cline 历史 JSON 作为**会话历史**（`StartSessionInput` 消息 / 结构化前置），而非仅文本块（`ClineDaemonEndpoint.cs#L141-154`）。

### 3.5 循环配置（AgentRuntimeUI.json + 工作流）

- **UI**：`AgentRuntimeUI.json` 增加 **`contextScope` 选择器**（`node` / `task` / `history`）+ 可选 `contextKey`（nodeKey / taskKey / historyId）。
- **工作流**：`agent-loop.json` 的 `provide_prompt`/`cline_execute` 参数表增加 `context_scope` / `context_key`，`cline_execute` 据此构建标准上下文并投喂。

---

## 四、数据来源与依赖（已具备）

| 粒度 | 内容来源 | 归属来源 |
|------|----------|----------|
| Node | `NodeOutputStore`（ur_nodes NodeType=output） | `IHierarchyReferenceManager.GetTaskNodeKeysAsync` |
| Task | `IHistoryStore`（ur_histories `ConversationLog`） | `IHierarchyReferenceManager.GetHistoryTaskKeysAsync` |
| History | 各 task 对话并集 | 引用图 history→task 边（历史节点集派生） |

渲染到 cline：`ClineConversationWire` 已产出 `[{role, content}]`，为 cline 兼容基线。


---

## 五、分阶段任务分解（P1–P6）

| 阶段 | 内容 | 落点 | 交付物/验证 |
|------|------|------|-------------|
| **P1** | `StandardScope` + `StandardContext` 模型 | `Share/Conversation` | 纯数据记录；单测 |
| **P2** | `IStandardContextBuilder` 三粒度实现（node/task/history） | 复用 `NodeOutputStore` + `IHistoryStore` + `IHierarchyReferenceManager` | 三粒度各一构建用例 |
| **P3** | `IStandardContextRenderer`（复用 `ClineConversationWire`） | `Share/Conversation/Adapters` | 渲染为 `[{role, content}]`；断言 |
| **P4** | `cline_execute`/`AgentExecutorRequest`/`ClineDaemonEndpoint` 接入 | `Agent.Modules/Cline` | 标准上下文 → cline 历史 JSON 结构化投喂；保留 L0/L1/L2 回退 |
| **P5** | `AgentRuntimeUI.json` 加 `contextScope` 控件 + `agent-loop.json` 参数表扩展 | `FileDriven/DrivenConfig` + `Config/workflows` | UI 可选 node/task/history；工作流参数表透传 |
| **P6** | 回归 + 端到端 | 测试 | node/task/history 三粒度各出一轮；现有 Cline/Conversation 测试全绿 |

> P1–P3 为纯数据/纯投影，零侵入，可先行落地；P4 之后再接入循环。

---

## 六、风险 / 边界

1. **兼容回退**：标准上下文是叠加/增强，不破坏现有 L0/L1/L2 prompt 路径；`contextScope` 缺省仍走原 prompt 逻辑，避免回归。
2. **cline 历史格式**：`[{role, content}]`（`ClineConversationWire` 已是此形态）为 cline 兼容基线；SDK 以 `prompt + sessionId` 续会话，标准上下文以结构化消息/前置块注入，不改 daemon 协议。
3. **粒度语义**：History 上下文 = 其任务并集（历史节点集派生），与 `history_owns_node` 降级一致，不重复建边。
4. **数据完整性**：node 输出在 ur_nodes、task/history 对话在 ur_histories、归属在引用图——三源以 `IHierarchyReferenceManager` / `IHistoryStore` / `NodeOutputStore` 端口隔离，标准上下文构建器只依赖端口，不直接触碰存储实现。

---

## 七、结论

**能把 LLM 循环改为标准上下文驱动吗？** 能，且 80% 基建已就位：

- **规范消息结构**（`ConversationMessage`/`ConversationLog`）、**节点→对话映射**（`IConversationNodeMapper`）、**cline 历史 JSON 渲染**（`ClineConversationWire`）已存在。
- **三层引用枚举**（`IHierarchyReferenceManager`，history→task→node、历史节点集派生）已就位（上一轮）。
- 缺的是：**统一 `StandardContext` 抽象**（P1-P2）、**渲染器**（P3）、**循环接入**（P4-P5）。

P1–P3 为纯数据/纯投影，可先行落地、低风险；P4–P5 把 `cline_execute` 从"拼 prompt"升级为"标准上下文 → cline 历史 JSON 结构化投喂"，最终使**所有 AI 交互都基于标准上下文**。

---

## 附录 A：代码位置索引

| 组件 | 路径 |
|------|------|
| LLM 循环工作流 | `Projects/Agent/Config/workflows/agent-loop.json` |
| 提示词分层 | `Projects/Agent/src/Agent.Core/Prompts/PromptFrameworkFunction.cs`、`AgentContextProjector.cs` |
| cline_execute | `Projects/Agent/src/Agent.Modules/Cline/ClineExecutorFunction.cs` |
| daemon 调用 | `Projects/Agent/src/Agent.Modules/Cline/ClineDaemonEndpoint.cs`（`ComposePrompt` L141-154） |
| 执行器请求契约 | `Projects/Agent/src/Agent.Contract/IAgentExecutor.cs`（`AgentExecutorRequest` L28-39） |
| 规范对话模型 | `CsharpPrj/Share/Conversation/ConversationMessage.cs`、`ConversationLog.cs` |
| 节点→对话映射 | `CsharpPrj/Share/Conversation/Adapters/ConversationNodeMapper.cs` |
| cline 历史 JSON 渲染 | `CsharpPrj/Share/Conversation/Adapters/ConversationAgentWire.cs`（`ClineConversationWire`） |
| 三层引用端口 | `CsharpPrj/Operation/DataStoreDriven/Abstractions/IHierarchyReferenceManager.cs` |
| 会话编排 | `Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs` |
| UI 控制台 | `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` |

