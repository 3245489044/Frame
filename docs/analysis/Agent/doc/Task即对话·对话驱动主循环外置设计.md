# Task 即对话：对话驱动主循环外置设计

> **版本**：0.4（完善——主循环升级为**通用信号驱动的工作流编排器**：循环触发只由「外部输入 ∪ 内部子智能体传递的信息」决定，**不硬编码层数/子智能体**；`collect→analyze→act` 三层仅作为**一个固定的工作流模板**（声明式 `agent-loop.json` 风格，可任意层数/任意子智能体），主循环本身不受任何限制）
> **日期**：2026-08-20
> **状态**：已按 P0–P3 落地实现（`Share.Conversation` 纯逻辑 + `Agent.Core.Conversation` 编排器 + `answer` 工作流函数 + 默认模板 `Config/workflows/collect-analyze-act.json` + `AgentUi_Conversation*` 协议面 + AgentRuntimeUI.json「⑦ 主对话框」视图）
> **范围**：在既有「任务池（TaskPool）+ 任务快照（`AgentTaskItem`）+ 快照驱动主循环（`SnapshotDrivenLoopEngine`）+ 工作流骨架（`RoundSpec`/`WorkflowRunner`/`RoundSpecLoader`）」之上，把**一个 task 定义为一个 Cline 式持续对话会话**——用户在「主对话框」发消息即自动更新 task（追加对话历史、触发主循环沿**会话工作流**驱动各子智能体、回写任务快照），**不再手动维护 task**。
> **依据（代码核实）**：
> - 任务快照：`Projects/Agent/src/Agent.Core/TaskPool/AgentTaskItem.cs`（task = 快照，`Data` 空间随 `PayloadJson` 全量序列化）、`AgentTaskItemJsonConverter.cs`（手写字段映射，`Data` 完整读写）
> - 主循环：`CsharpPrj/Share/TaskStatusPool/Loops/SnapshotDrivenLoopEngine.cs`（轮次式快照循环，唯一 while）→ `Agent.Core/Loops/RoundLoopDriver.cs`（单根五步）
> - 任务池：`Agent.Core/TaskPool/AgentTaskPool.cs`（`AgentTaskPoolAdapter`，`EnqueueAndWaitAsync` 直跑统一走任务池、`Resolve` 按执行器路由）
> - 回答循环：`Agent.Modules/Answer/StatelessAnswerLoop.cs` / `StatelessAnswerExecutor.cs` / `StatelessAnswerTaskExecutor.cs`（executorId=`stateless-answer`，多轮精化 + 上轮答案回写；`StatelessAnswerRequest` 明确"无 SessionId、无持久化"；尚未注册为工作流函数，需补一个 `answer` 包装函数供会话工作流步骤消费）
> - 工作流骨架（主循环不限层数的关键复用）：`Agent.Contract/IWorkflowRunner.cs`（骨架端口 `RunRoundAsync`）、`Agent.Axioms/Loops/WorkflowRunner.cs`（按声明式 `RoundSpec` 逐步骤执行 + `RoundStepInvoker` 按 FuncName 调注册函数 + KeyMapping 端口传递）、`RoundSpecLoader.cs`（从 `Config/workflows/agent-loop.json` 沿 `start→on_success` 提取主路径线性链为 `RoundSpec`）、`ClineExecutorFunction`（注册 `cline_execute`）——即"一个工作流 = 一个 `agent-loop.json` 定义（任意步骤数、任意子智能体函数）"，主循环只沿链驱动，不感知具体步骤
> - UI 声明：`FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json`（左侧模板树 + 单一请求体 `jsonRequestTextBox` + `▶ 运行`；右侧分页展示——**无对话气泡视图**）
> - 数据存储：`ur_tasks`（Id/ThreadId/SessionId/SnapshotJson），`SnapshotJson` 全量序列化 `AgentTaskItem`，TaskKey 幂等 upsert（写即激活消费循环）

---

## 一、背景与目标

### 1.1 现状痛点（为什么"对话"很别扭）

现有交互是**「请求体 + ▶ 运行」**，一次运行 = 一个独立任务（enqueue → 消费 → 一轮 AI 循环 → 结果写回 → 任务终态）。要持续对话就得**手动**反复：

1. 每次发消息手动写一长串 `requestJson`（`taskKey/input/mode/executorId/relatedNodes/...`）；
2. 自己维护"上一轮说了什么"——任务快照只有单条 `UserInput` + `LastResultText`，**没有跨消息的对话历史**；
3. 跑完一轮要手动再入队，对话上下文靠人肉拼进 `input`。

根因是：**task 被当作"一次性任务"而非"持续会话"**。已有最接近的 `stateless-answer` 执行器是**无状态回答**（`StatelessAnswerRequest` 注释原文"无 SessionId、无持久化"），只在单次 enqueue 内做多轮精化，**不跨消息累积对话**。

### 1.2 目标

> **一个 task = 一个像 Cline 一样的持续对话过程**：用户/外部智能体在「主对话框」发一条消息，系统自动完成「追加对话历史 → 更新 task 快照 → 触发主循环分析对话 → 回写任务 → 刷新 UI」，**全程无需手动维护 task**。

| 你要做的事 | 现在的写法 | 目标写法 |
|---|---|---|
| 开始对话 | 手写 `task enqueue`（taskKey/input/mode/executorId/relatedNodes 一长串） | 主对话框输入第一句，自动创建会话 task |
| 继续对话 | 手动拼历史 + 再 enqueue | 主对话框输入下一句，自动更新同 task |
| 看对话结果 | `query outputs(threadId)` / 分页查 | 主对话框气泡实时回显 + 任务快照自动推进 |
| 维护任务状态 | 手动管理 round/status/history | 主循环自动回写 task 快照 |

---

## 二、核心概念（三条）

### 2.1 Task = 对话会话

`AgentTaskItem` 即**任务 = 快照**（已有：单条 `PayloadJson` = 唯一权威状态基座，TaskKey 幂等 upsert）。本设计把快照语义升级为**对话会话**：

- `taskKey` = 会话标识（`ThreadId` 对齐 = `taskKey`，归属命名空间，见《任务中心多层作用域封装框架设计》命名规则）；
- 快照内 `Data` 空间新增 `__conversation` 键，承载**结构化对话历史**（消息列表），随 `PayloadJson` 全量序列化/还原（重启不丢，AX-CORE.InformationMonotonicity 只增不丢）；
- 任务生命周期 `Status` = 会话当前运行态（Running=AI 正在回答 / Paused=暂停 / Completed=本次回答完成）。

### 2.2 对话 = 更新 task

每次用户/外部智能体发一条消息，就是一次**「对话输入事件」**，自动落到 task 快照上：

```
发消息 ──→ ① 追加 user 消息到 __conversation + UserInput=当前消息（外部输入信号）
         ② ConversationPipeline 沿会话工作流推进（复用 EnqueueAndWait 同 taskKey，写即激活）
         ③ 各子智能体步骤由上层产出逐层触发（collect→analyze→act…，层数/子智能体由工作流决定）→ 产出结果
         ④ 回写 task：追加各层 sub-agent + assistant 消息 + 更新 Round/Status → 持久化
```

用户**只表达对话意图**，taskKey/round/status/history 全由系统自动维护。

### 2.3 主循环外置为主对话框（通用信号驱动，不限制层数）

「主循环外置」不只是"入口与观测外置"，更关键的是**把主循环的循环触发外置为"只由外部输入 + 内部子智能体信号"决定**——主循环本体是一个**通用工作流编排器**，它不感知一个会话要跑几个子智能体、跑哪几层，只负责"信号进来 → 沿会话工作流推进一步 → 回写 → 等下一信号"。具体层数/子智能体由**声明式工作流**决定，主循环本身不受任何限制。

- **主循环本体（通用，零硬编码）**：复用既有 `SnapshotDrivenLoopEngine`（快照驱动唯一 while）+ `RoundLoopDriver`（单根五步）承载"一个工作流步骤的运行"，新增 `ConversationPipeline`（Agent.Core 组装）作**通用编排器**——不感知 `collect/analyze/act`，只沿会话工作流链驱动；
- **会话工作流 = 一个 `agent-loop.json` 风格定义**（复用 `RoundSpec`/`RoundSpecLoader`/`WorkflowRunner`）：步骤 = 子智能体函数（`cline_execute`/`answer`/…），步骤间用 `KeyMapping` 端口把上层 `ResultKeys` 传给下层 `ParameterKeys`。`collect→analyze→act` 三层**只是其中一个工作流模板**（见 §四 示例；其 MCP 工具面封装见《三层工作流MCP封装设计》），换一个工作流文件即可跑任意层数/任意子智能体组合，主循环无需改动；
- **触发契约（循环只由两类信号驱动，见 §4.3）**：
  - **外部输入**：用户在「主对话框」发消息 / 外部智能体发消息 → 启动或介入本次工作流运行；
  - **内部子智能体传递的信息**：某子智能体（工作流步骤）完成并产出 → 触发下一个子智能体；
  - **禁止内部预算自触发层推进**（如靠 `ProgressToken`/`rule_match` 自行决定"要不要跑下一层"）——层推进只由信号内容决定；
- **终止**：① 工作流链式终点（最后步骤产出，探索域，责任外置）；② 外部新输入介入（重定向）；③ 超时/取消（watchdog）。预算终止（`TerminationDecider`）**仅作内循环防护**，不参与外循环层推进（不与 `AX.TerminationBipartition` 冲突：预算终止仍是内循环可判定裁决，只是不作外循环层推进依据）；
- UI 新增「主对话框」视图（消息气泡 + 输入框 + 发送按钮 + 实时运行态，按工作流步骤分层展示），`AgentUi_Conversation*` 注册函数接线（见 §五）。

> **一次用户消息 = 启动一次会话工作流运行**：对话本身仍跨消息多轮（每条消息触发一次工作流），但**单条消息内可含多个子智能体步骤**（如 collect→analyze→act 由各自产出逐层触发）。精化由用户后续消息自然提供（对齐 Cline：你一句它一句）。

---

## 三、数据模型（对话历史落 Data 空间，随快照持久化）

### 3.1 为什么落 `AgentTaskItem.Data`

已核实：`AgentTaskItemJsonConverter` 手写字段映射，其中 **`Data`（DataSpace）被完整序列化（Write 遍历所有键）与反序列化（Read 经 `LoopStateSnapshot.Unwrap` 还原）**。因此对话历史写进 `Data` 空间的 `__conversation` 键，即**自动随 `ur_tasks.PayloadJson` 持久化/还原**，无需改 `AgentTaskItem` 核心字段、无需改 converter——这是最小侵入且最贴合"任务即快照"语义（`IStateFacet.WriteArchive/ReadArchive` 正是读写 `Data` 的归档区通道）。

> **落点**：`IStateFacet.WriteArchive("__conversation", conversationJson)` / `ReadArchive("__conversation")`。归档只增不丢（AX-CORE.InformationMonotonicity）；LLM 上下文只取**窗口**（见 §3.3），全量留在快照归档。

### 3.2 `ConversationLog`（Share 纯逻辑模型，可单测）

```csharp
// CsharpPrj/Share/Conversation/ —— 纯逻辑，零 Agent 依赖
public enum ConversationRole { System, User, Assistant, Tool, Error }

public sealed record ConversationMessage(
    ConversationRole Role,
    string? Stage,                      // 会话工作流步骤标识（声明式：如 "cline_execute"/"answer"…，不限定层数/子智能体）；null=会话级/user/assistant
    string? SubAgent,                   // 产生方执行器函数名（"cline_execute"/"answer"/…）；null=user/assistant
    string Text,
    int Round,                          // 关联轮次（0=会话级/system）
    DateTimeOffset AtUtc);

public sealed class ConversationLog
{
    public IReadOnlyList<ConversationMessage> Messages { get; } // 只增
    public int NextRound { get; }                                // 会话累计轮次（Round 单调）
    public void AppendUser(string text);                         // → User, Round=NextRound+1
    public void AppendAssistant(string text);                    // → Assistant, Round 与 user 对齐
    public void AppendSubAgent(string? stage, string? subAgent, string text); // 工作流步骤产出（中层气泡，Stage/SubAgent 标识，UI 可折叠）
    public void AppendSystem(string text);
    public string ToPromptText(int window, bool includeSystem = true); // 渲染最近 window 条为 LLM 上下文
    public string Serialize();                                   // → JSON（存 Data.__conversation）
    public static ConversationLog Deserialize(string? json);     // 缺省 → 空会话；旧数据（无 Stage/SubAgent）向后兼容读取
}
```

> 会话级 `Round` 单调递增，即**对话第几轮**；`AgentTaskItem.Round`（循环轮次）由内循环推进，两者不混用（对话轮次 = 用户消息计数，循环轮次 = 单步子智能体内部步骤）。**`Stage` 是会话工作流的步骤标识，不是固定三层**——`collect→analyze→act` 只是其中一个工作流模板，主循环不感知 `Stage` 集合，仅透传工作流步骤名。

### 3.3 上下文窗口与全量归档

| 用途 | 来源 | 说明 |
|---|---|---|
| LLM 上下文 | `ToPromptText(window)` 最近 **N 条**（默认对齐 `RECENT_CHAIN_COUNT=3` 语义，`ConversationWindow` 配置） | 控制上下文长度，防爆（AX-CORE QuantificationConstraint） |
| 快照归档 | `Data.__conversation` 全量 | 只增不丢；压缩历史仍可经 query 读回（AX-CORE InformationMonotonicity + AX-META ContextPollutionIsolation：截断不污染上下文、旧条目归档不丢） |

---

## 四、主循环外置架构（组件分层 + 交互时序）

### 4.1 分层落点（对齐既有：通用逻辑下沉 Share、Agent 只组装、UI 只接线）

| 层 | 组件 | 职责 | 谁用 |
|---|---|---|---|
| **Share 纯逻辑** | `ConversationLog`（`CsharpPrj/Share/Conversation/`） | 对话历史模型：追加/窗口渲染/序列化（含 `Stage/SubAgent` 泛化） | 被 Agent.Core 引用；被 CentralTest 直测 |
| **Agent.Core 组装** | `ConversationSessionService` + `ConversationPipeline`（**通用编排器**） | 会话定位/创建/追加 + 信号驱动沿**会话工作流**推进/回写快照（纯逻辑 + 注入任务池端口；**不感知具体层数/子智能体**） | 被 Launcher 引用；被测试直测 |
| **工作流（声明式模板）** | `Config/workflows/*.json`（`RoundSpecLoader` 加载 → `RoundSpec`；`collect→analyze→act` 仅是其一） | 定义会话的步骤链（步骤 = 子智能体函数 + KeyMapping 端口）；**换文件即换主循环行为，主循环零改动** | 被 `ConversationPipeline` 消费 |
| **主循环引擎** | `SnapshotDrivenLoopEngine` + `RoundLoopDriver` | 承载"一个工作流步骤"的内循环（唯一 while；预算终止仅作内循环防护） | 被 `ConversationPipeline` 调用 |
| **Agent.Modules 子智能体函数** | `cline_execute`（ClineAgentExecutor）+ `answer`（新增包装，复用 `StatelessAnswerExecutor`） | 工作流步骤执行体：收集（Cline）/ 分析（Answer）/ 执行（Cline）各自内部可多轮 | 被 `RoundStepInvoker` 按 FuncName 命中 |
| **Agent.Launcher 协议面** | `AgentUi_Conversation*` 注册函数 | 把消息翻译为会话命令（外部输入信号），回传诊断/最新对话 | 被 UI/脚本/外部智能体调用 |
| **FileDriven UI** | AgentRuntimeUI.json「主对话框」视图 | 会话选择 + 消息气泡（按工作流步骤/子智能体分层折叠）+ 输入框 + 发送 + 实时运行态 | 最终调用方：人 |

> **复用而不复制（SingleImplementation/TH-12）**：主循环引擎复用 `SnapshotDrivenLoopEngine` + `RoundLoopDriver`；工作流复用 `RoundSpec`/`RoundSpecLoader`/`WorkflowRunner`/`RoundStepInvoker`；子智能体复用 `ClineAgentExecutor`（`cline_execute`）与 `StatelessAnswerExecutor`（`answer` 包装）；存储复用 `AgentTaskPoolAdapter.EnqueueAndWaitAsync` + `Data` 归档；不 new 机械码、不复制落库。**主循环本身不引入任何 `collect/analyze/act` 硬编码**——它们只存在于工作流配置中。

### 4.2 组件接口（Agent.Core 新增）

```csharp
// Projects/Agent/src/Agent.Core/Conversation/ —— 通用编排器（不感知具体工作流）

// 会话信号：外循环推进的唯一触发源（外部输入 ∪ 内部子智能体传递的信息）
public enum ConversationSignalKind { ExternalInput, SubAgentCompleted }
public sealed record ConversationSignal(
    ConversationSignalKind Kind,
    string? ExternalText,           // Kind=ExternalInput：用户在对话框发消息 / 外部智能体发消息
    SubAgentResult? SubResult);     // Kind=SubAgentCompleted：工作流某子智能体步骤的产出

public sealed record SubAgentResult(string StepName, string OutputText, bool ChainEnded);

// 会话工作流 = 一个声明式 RoundSpec（复用 RoundSpecLoader/WorkflowRunner/RoundStepInvoker）；
// collect→analyze→act 只是其中一个模板，主循环零硬编码层数/子智能体。
public sealed class ConversationPipeline
{
    // 注入：任务池适配器 + 会话工作流（RoundSpec）+ 子智能体函数注册表（RoundStepInvoker）
    public ConversationPipeline(AgentTaskPoolAdapter pool, RoundSpec workflow);

    /// <summary>推进信号：ExternalInput 启动/介入；SubAgentCompleted 沿工作流链触发下一步（内触发）。</summary>
    Task<ConversationReply> RunSignalAsync(
        string sessionName, ConversationSignal signal, CancellationToken ct);
}

public sealed class ConversationSessionService
{
    public ConversationSessionService(AgentTaskPoolAdapter pool,
        Func<string, string>? sessionKeyMapper = null,
        Func<string, RoundSpec>? workflowResolver = null);   // 会话名 → 工作流；缺省默认模板

    /// <summary>发送一条用户消息（外部输入信号）→ 定位/创建会话 → 追加历史 → ConversationPipeline.RunSignalAsync → 回写。</summary>
    Task<ConversationReply> SendAsync(string sessionName, string text, CancellationToken ct);

    /// <summary>列出会话历史（全量，供 UI 回显，含各工作流步骤分层）。</summary>
    Task<ConversationLog> GetHistoryAsync(string sessionName, CancellationToken ct);

    /// <summary>新建/清空会话（taskKey=会话名；重建空 __conversation）。</summary>
    Task<string> NewAsync(string sessionName, CancellationToken ct);
}

public sealed record ConversationReply(
    string TaskKey, int Round, string? AssistantText, IReadOnlyList<ConversationStageInfo> Stages,
    TaskExecutionStatus Status, IReadOnlyList<string>? Diagnostics);

// 供 UI 分层回显：本消息触发的各工作流步骤产出（StepName + SubAgent + 文本）
public sealed record ConversationStageInfo(string StepName, string SubAgent, string Text);
```

### 4.3 交互时序（信号驱动：一次发消息 = 启动一次会话工作流）

```
[主对话框] 发送按钮
   │ text="请分析 source.data 并产出结构化结论"
   ▼
AgentUi_ConversationSend(sessionName="proj-x", text)     ← 外部输入信号（触发源①）
   ▼
ConversationSessionService.SendAsync
   ① 定位会话：pool.GetStatusAsync(taskKey="proj-x") → 有则复用；无则 EnqueueAsync 创建（taskKey=proj-x）
   ② 追加：ReadArchive("__conversation") → ConversationLog.AppendUser(text) → WriteArchive + UserInput=text
   ③ ConversationPipeline.RunSignalAsync(ExternalInput)
        └─ 沿会话工作流（RoundSpec）链驱动，每步 = 一个子智能体函数，由上层产出触发下一步（触发源②）：
           Step1 cline_execute(collect) → 读 source.data + 当前意图 → 收集 → 产出 collected_info
           Step2 answer(analyze)        → 输入 collected_info → 分析 → 产出 conclusion（SubAgentCompleted 触发）
           Step3 cline_execute(act)     → 输入 conclusion → 执行写库 → 产出 execution_result（链终点）
        （每步内部可含多轮内循环：SnapshotDrivenLoopEngine + RoundLoopDriver；预算仅作内循环防护）
   ④ 回写：AppendSubAgent(各步 Stage/SubAgent/Text) + AppendAssistant(execution_result) + Round/Status 更新 → 持久化
   ⑤ 返回 ConversationReply（Stages 分层 + AssistantText）
   ▼
AgentUi_ConversationSend 聚合 → UI 追加各层气泡（可折叠）+ assistant 终答 + 刷新实时运行态
```

> **触发源仅两类**：`ExternalInput`（用户消息/外部智能体）启动或介入；`SubAgentCompleted`（工作流步骤产出）沿链触发下一步。主循环**不靠内部预算自触发层推进**——`collect/analyze/act` 只是**默认工作流模板**（`Config/workflows/*.json`），换工作流文件即换主循环行为（任意层数/任意子智能体），主循环零硬编码。
>
> **无手动 task 维护**：taskKey = 会话名自动派生；round/status/history 由 `SendAsync` 自动推进；并发/重放安全（同一会话串行 `EnqueueAndWaitAsync`，不同会话可并发）。

---

### 4.4 主对话框视图（AgentRuntimeUI.json 构建）

在现有 `AgentRuntimeUI.json` 右侧 `displayTabControl` 新增一个 `tabpage`「⑥ 主对话框」（对齐现有声明式控件 + `bindings` 事件绑定风格），或在左侧 `inputGroup` 之上叠加会话栏：

| 控件 | 类型 | 作用 | 事件（click → 注册函数） |
|---|---|---|---|
| 会话下拉 | `combobox`（DropDownList） | 选择/新建会话（taskKey） | `AgentUi_ConversationList` / `AgentUi_ConversationNew` |
| 消息气泡列表 | `listbox`（OwnerDraw）或 `markdownviewer` 逐条渲染 | 展示 user / 各层 sub-agent / assistant 消息；按 `Stage/SubAgent` 分色、中层可折叠 | `AgentUi_ConversationHistory`（载入回显） |
| 输入框 | `textbox`（multiline） | 用户输入下一条消息 | — |
| 发送 | `button`「发送」 | 追加 user + 触发会话工作流 | `AgentUi_ConversationSend` |
| 运行态 | `label`/状态区 | 当前工作流步骤 + Running/Paused/Completed | `AgentUi_ConversationState`（轮询） |

> 气泡数据来自 `ConversationLog`（`Stage/SubAgent` 分层）：`User`=用户侧、`Assistant`=链终点终答、`Stage` 非空=工作流中层产出（默认折叠，可展开看收集/分析/执行原始内容）。视图**只按数据渲染，不硬编码 `collect/analyze/act` 三层**——新增工作流步骤自动在气泡中多出一层。

---

## 五、端到端示例（示意：一条消息内跑三层工作流模板）

**会话工作流（默认模板 `collect-analyze-act.json`，仅是其中一个模板）**：
```
Step1 cline_execute(collect) → Step2 answer(analyze) → Step3 cline_execute(act)
```

> 该三层模板的 **MCP 工具面封装**（供外部智能体如 Cline 主控按 `caa_*` 工具调用，`BusinessService` 桥到 Agent action 面）见《三层工作流MCP封装设计》。对话 UI 内嵌调用与 MCP 调用**复用同一模板**（单一来源，不双真源）。

**主对话框**（用户逐句输入，系统沿工作流自动分层推进）：

```
[主对话框 — 会话 proj-x]                     taskKey=proj-x  Running→Completed
─────────────────────────────────────────────
👤 user     : 请分析 source.data 并产出结构化结论写入 target.data。
   🤖 cline(collect) [▸] 已读取 source.data，收集到 12 条原始记录……
   🔍 answer(analyze) [▸] 结论：{title, body, 建议}，剔除 body 为空项……
   🛠️ cline(act)      [▸] 已写入 ur_nodes target.data，完成……
✅ assistant: 任务已完成，结论已落库。
👤 user     : 补充：忽略 body 为空的部分。
   🤖 cline(collect) [▸] 重读 source.data，保留 8 条非空……
   🔍 answer(analyze) [▸] 更新结论：仅保留非空要点……
   🛠️ cline(act)      [▸] 已覆盖写回 target.data……
✅ assistant: 已更新。
─────────────────────────────────────────────
[输入框] 补充：忽略 body 为空的部分。          [发送]
```

> 每条消息 = 一次 `ConversationSessionService.SendAsync`：追加 user（外部输入信号）→ `ConversationPipeline` 沿会话工作流分层推进（每步由上层产出触发）→ 各层产出回写**可折叠气泡** + assistant 终答。用户**不需要**手动指定 `taskKey/input/mode/executorId/relatedNodes`。**换掉 `collect-analyze-act.json` 为任意工作流定义（更多/更少步骤、换子智能体）即换主循环行为**——主循环与对话逻辑零改动。

---

## 六、分阶段落地（通用逻辑优先 Share，逐层可独立验收）

| Phase | 内容 | 验收 |
|---|---|---|
| **P0（Share）** | `CsharpPrj/Share/Conversation/` 新建 `ConversationLog`（消息模型含 `Stage/SubAgent` 泛化 + 追加 + 窗口渲染 + 序列化，旧数据向后兼容）；`Share.csproj` 聚合引用 | CentralTest 单测：追加 round 单调、窗口截断、Serialize/Deserialize 往返（含旧数据）；`Share` 编译 0 错误 |
| **P1（Agent.Core 组装）** | `ConversationSessionService` + `ConversationPipeline`（通用编排器：信号驱动沿 `RoundSpec` 链推进）+ `AgentTaskItem.Data` 归档读写 `__conversation`；`Agent.Core.Tests` 直测（注入内存任务池桩 + 最小两步工作流） | 发一条消息 → task 快照含 user+各层 sub-agent+assistant、Round 推进、Status 正确；同会话第二条追加不覆盖历史；**换两步/四步工作流，主循环行为随之变化且零改动** |
| **P2（工作流函数 + 协议面）** | 子智能体工作流函数：复用 `cline_execute`（Cline）+ 新增 `answer` 包装（复用 `StatelessAnswerExecutor` 纯聊天）；默认模板 `collect-analyze-act.json`；注册 `AgentUi_ConversationSend/List/New/History/State`；`Agent.sln` 编译 0 错误 | `RoundStepInvoker` 按 FuncName 命中各步；一条 `AgentUi_ConversationSend` 触发整条工作流链；未装配函数返回诊断、不消耗额度 |
| **P3（UI + 收尾）** | AgentRuntimeUI.json 加「主对话框」视图（会话选择 + 消息气泡按工作流步骤/子智能体分层折叠 + 输入 + 发送 + 实时运行态）；文档定稿 | 发消息自动建会话/沿工作流分层推进/分层回显；重启恢复对话历史与层结构（Data 归档） |

---

## 七、权衡

| 决策点 | 方案 | 取舍 |
|---|---|---|
| 对话历史落点 | `AgentTaskItem.Data.__conversation`（随 `PayloadJson` 全量序列化） | 不改 converter/核心字段，自动持久化重启不丢；代价是弱类型 JSON（经 `ConversationLog` 工具读写收敛，不改散落） |
| 主循环外置 | `ConversationPipeline`（**通用编排器**，零硬编码层数）+ 复用 `SnapshotDrivenLoopEngine`/`RoundLoopDriver` 承载单步内循环 | 不重写引擎；主循环不感知具体工作流，任意层数/子智能体由工作流配置决定；代价是多一层组装 |
| 会话工作流 | 复用 `RoundSpec`/`RoundSpecLoader`/`WorkflowRunner`（`Config/workflows/*.json` 声明式步骤链） | **换文件即换主循环行为**，主循环零改动；`collect→analyze→act` 仅是默认模板；代价是需要一个 `answer` 包装函数（复用 `StatelessAnswerExecutor`，零复制逻辑） |
| 触发契约 | 循环触发仅 `ExternalInput ∪ SubAgentCompleted`；预算终止仅内循环 | 由信号内容决定层推进，不靠内部计数自触发（对齐 ANTI-NO-TERMINATION）；终止三途：链式终点/外部介入/超时取消 |
| 子智能体步骤 | 工作流步骤 = 子智能体函数（`cline_execute`/`answer`），每步内部可多轮 | 收集/分析/执行各司其职且可任意组合；代价是单步需显式声明 Prompt/KeyMapping |
| 会话定位 | taskKey = 会话名（幂等 upsert，写即激活） | 无需单独会话表；代价是会话即任务（删除任务即结束会话，符合直觉） |
| 上下文窗口 | 窗口进 LLM 上下文 + 全量进快照归档 | 控制上下文长度；旧消息仍可查询（InformationMonotonicity） |
| 与 stateless-answer | `conversation` 是有状态（跨消息、沿工作流多步），`stateless-answer` 保留无状态单次 | 两个执行器并存（单一实现各司其职），不冲突 |

---

## 八、一句话总结

> **Task 即对话 = 把 `AgentTaskItem` 升级为持续对话会话（对话历史落 `Data` 归档随快照持久化），`ConversationSessionService`/`ConversationPipeline` 把"一条用户消息"（外部输入信号）自动翻译为"一次会话工作流运行"：沿声明式工作流（复用 `RoundSpec`/`WorkflowRunner`，`collect→analyze→act` 仅是默认模板）逐个子智能体推进（每步由上层产出触发），产出回写 task（追加各层 + assistant、推进 round/status）——主循环外置为主对话框，循环触发只由「外部输入 ∪ 内部子智能体信号」决定、不限制层数，发消息即自动更新 task，无需手动维护。**
