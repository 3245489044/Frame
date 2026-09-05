# AgentRuntimeUI — 历史主循环调用链

> **版本**：1.0　**日期**：2026-08-28
> **范围**：`FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json`（④ 主对话框 / 会话历史）的历史主循环调用流程
> **性质**：调用链梳理文档（基于源码 + 既有分析文档取证，非实施变更）
> **关联**：`docs/analysis/2026/8/8.24/主对话框HTML渲染为空_调用链根因分析.md`（渲染链路排障）、`docs/analysis/2026/8/8.25/2026-08-25-Agent主循环提取为LLM无关通用框架设计.md`（底层主循环通用化）、`Projects/Agent/doc/Task即对话·对话驱动主循环外置设计.md`（会话主循环设计）

---

## 一、术语澄清：「历史主循环」指什么

本项目「主循环」一词存在两个抽象层，需先分清，避免混淆：

| 主循环 | 抽象层 | 用途 | 关键载体 |
|--------|--------|------|----------|
| **会话历史主循环**（本文主题） | Agent 会话层 | 「一条用户消息 = 一次会话工作流运行」，沿声明式 `RoundSpec` 链推进并回写历史 | `ConversationPipeline` + `ConversationSessionService` + `ConversationViewService` |
| **通用快照循环引擎** | Share 通用机制 | 轮次式快照循环（读快照 → execute → next → 更新快照），任务池五步骨架的引擎 | `SnapshotDrivenLoopEngine`（`CsharpPrj/Share/TaskStatusPool/Loops/`） |

> 会话历史（UI 主对话框回显）走**会话历史主循环**；`SnapshotDrivenLoopEngine` 是通用任务池循环机制，与本次 UI 历史回显链不在同一抽象层，仅作对照。

---

## 二、调用链全景（一图流）

```
【阶段0 启动注入】initCommands
  exec Ui_ListCatalogTree output=templateListView
  exec AgentUi_ConversationListHtml output=conversationHistoryViewer
        └─ ConversationViewService.RenderViewAsync（渲染"历史→任务"树 + 默认历史）

【阶段1 用户发送】模板树选 conversation/send → 载入 → 填 input + taskKey → 「▶ 运行」
  ▶ 运行 click → AgentUi_RunJson（RunJsonAsync 按 action 分发）
        └─ action=conversation op=send → AgentUi_ConversationSend

【阶段2 会话工作流主循环】AgentUiLibraryConversation.ConversationSendStandardAsync
  ├─ ResolveSessionName(sessionName)                空/空白 → "default"
  ├─ svc.SendAsync(name, text, ct, agent, ctxScope, ctxKey)
  │    └─ ConversationSessionService.SendAsync      同会话串行锁 BoundedSessionGate
  │         └─ ConversationPipeline.RunSignalAsync  一条消息 = 一次会话工作流运行
  │              ├─ 定位/创建会话（taskKey=会话名，纯落库，不激活池）
  │              ├─ 沿注入 RoundSpec 链推进（默认 collect→analyze→act / 或指定 agent）
  │              │    └─ 每步 WorkflowRunner → RoundStepInvoker 按 FuncName 调注册函数
  │              │         （cline_execute→ClineAgentExecutor / answer→StatelessAnswerExecutor）
  │              │    └─ SubAgentCompleted 触发下一步（KeyMapping 触发语义）
  │              │       链终点产出 assistant 终答
  │              └─ 每轮归档：历史落 SQLite ur_histories + 建 task→node cross_db_link 边
  ├─ EnsureHistoryTaskAsync(name)                   归拢 task 到 History 上层容器
  └─ ConversationViewService.RenderViewAsync(name)  发送后回显完整历史
```
```
【阶段3 历史渲染回显】ConversationViewService.RenderViewAsync
  ├─ ListHistoriesAsync                             列全部历史（ur_histories / 引用图）
  ├─ ResolveHistoryForTask(histories, sessionName)  当前任务归属哪个 History
  ├─ 视图分发：Nodes / Task / History
  │    ├─ Nodes → RenderNodesView()（只读 ur_nodes 直读）
  │    ├─ 默认 → ResolveTaskStandardMessagesAsync
  │    │        └─ IStandardContextProvider.BuildTaskAsync
  │    │           = 任务累积对话（ur_histories）+ 节点产出（ur_nodes Tool 气泡）
  │    │           未装配 → 回退 svc.GetHistoryAsync(...).Messages
  │    └─ Task → + RenderTaskNodesPanelAsync（右侧节点分类面板）
  └─ RenderFlow(view, histories, currentHistory, sessionName, msgs, nodesPanel)
       ├─ treeHtml    = RenderHistoryTree(...)             左侧「历史→任务」两级树
       ├─ historyHtml = HtmlConversationRenderer.Render    右侧分层气泡
       └─ return HtmlTemplateEngine.Render(EffectiveTemplates, "flowShell", { tree, history })
       渲染结果 HTML → conversationHistoryViewer / resultTextBox

【阶段4 消息桥交互】左侧点选 task（data-cmd=selectTask）→ 浏览器 postMessage
  └─ AgentUi_ConversationMessageDispatch（AgentRuntimeUI.json events.message）
       └─ ConversationMessageDispatchAsync → 按 payload（selectType/selectTask/selectHistory）
            重调 RenderViewAsync 重渲染；selectHistory 客户端定位、中层可折叠
```

---

## 三、分阶段详解

### 3.1 阶段 0 · 启动注入（initCommands）

`AgentRuntimeUI.json` 第 3-6 行两条 init 命令：

| 命令 | 目标 | 作用 |
|------|------|------|
| `exec Ui_ListCatalogTree output=templateListView` | 左侧模板树 | 扫描 `Templates/AgentRun` 填充左侧模板树 |
| `exec AgentUi_ConversationListHtml output=conversationHistoryViewer` | 主对话框 | 渲染交互式会话页（历史→任务树 + 默认历史） |

由 `ScopeLifecycleAdapter.ExecuteInitCommandsAsync` → `ExecuteInitExecAsync` 解析符号 / `output=` / 命名参数 → `InvokeFunctionAsync`。

### 3.2 阶段 1 · 用户发送消息（统一入口）

- 模板树选 `conversation/send` → 「载入模板」→ 填 `input` 消息 + 可选 `taskKey`（会话名）→ 点「▶ 运行」。
- 「▶ 运行」`click → AgentUi_RunJson`（`RunJsonAsync`）按 `action` 分发（`runLoop/task/conversation/resource/reference/query`）。
- `action=conversation op=send` → `AgentUi_ConversationSend`。

### 3.3 阶段 2 · 会话工作流主循环（核心推进）

`AgentUi_ConversationSend` → `AgentUiLibraryConversation.ConversationSendStandardAsync`
（`Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryConversation.cs:42`）：

```
ResolveSessionName(sessionName)              空/空白 → "default"（:52）
svc.SendAsync(name, text, ct, agent, contextScope, contextKey)  主循环入口（:57）
  └─ ConversationSessionService.SendAsync    同会话串行锁 BoundedSessionGate（:45）
       └─ ConversationPipeline.RunSignalAsync
            ├─ 定位/创建会话（taskKey = 会话名，纯落库，不激活池）
            ├─ 沿注入 RoundSpec 链推进（默认 collect→analyze→act；可换任意 agent）
            │    └─ 每步 WorkflowRunner → RoundStepInvoker 按 FuncName 调注册函数
            │         （cline_execute→ClineAgentExecutor / answer→StatelessAnswerExecutor）
            │    └─ SubAgentCompleted 触发下一步（KeyMapping 触发语义）
            │        链终点产出 assistant 终答
            └─ 每轮归档：历史落 SQLite ur_histories + 建 task→node cross_db_link 边
EnsureHistoryTaskAsync(name, ct)             归拢 task 到 History 上层容器（:62）
ConversationViewService.RenderViewAsync(name, ct)  发送后回显完整历史（:63）
```

关键语义：
- **一条消息 = 一次会话工作流运行**（`ConversationSessionService` 类注释），主循环不感知 collect/analyze/act——换 `RoundSpec` 即换主循环行为，`ConversationPipeline` 零改动。
- **并发安全**：同一会话按会话名 `SemaphoreSlim`（有界化 `BoundedSessionGate`，容量上限 + LRU 淘汰未持有锁）串行化，不同会话可并发。


### 3.4 阶段 3 · 历史渲染回显

`ConversationViewService.RenderViewAsync`（`Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs:40`）：

```
RenderViewAsync
  ├─ ListHistoriesAsync                                 列全部历史
  ├─ ResolveHistoryForTask(histories, sessionName)      当前任务归属哪个 History
  ├─ 视图分发：Nodes / Task / History
  │    ├─ Nodes → RenderNodesView()（只读 ur_nodes 直读，非对话）
  │    ├─ 默认 → ResolveTaskStandardMessagesAsync
  │    │        └─ IStandardContextProvider.BuildTaskAsync
  │    │           = 任务累积对话（ur_histories）+ 节点产出（ur_nodes Tool 气泡）
  │    │           未装配 → 回退 svc.GetHistoryAsync(...).Messages（零回归）
  │    └─ Task → + RenderTaskNodesPanelAsync（右侧节点分类面板）
  └─ RenderFlow(view, histories, currentHistory, sessionName, msgs, nodesPanel)
       ├─ treeHtml    = RenderHistoryTree(...)          左侧「历史→任务」两级树
       ├─ historyHtml = HtmlConversationRenderer.Render  右侧分层气泡
       └─ return HtmlTemplateEngine.Render(EffectiveTemplates, "flowShell", { tree, history })
```

渲染结果 HTML → 输出到 `conversationHistoryViewer` / `resultTextBox`。
> 注意：`ConversationViewService` 是**单一编排点**，收敛四处「ListHistories → GetHistory → ResolveHistoryForTask → RenderFlow」编排（`ConversationSendAsync`/`ConversationListHtmlAsync`/`ConversationHistoryAsync`/`ConversationMessageDispatchAsync`）。

### 3.5 阶段 4 · 消息桥交互（点选刷新）

- 左侧点选 task（`data-cmd=selectTask`）→ 浏览器 `postMessage` → 路由到 `AgentUi_ConversationMessageDispatch`（`AgentRuntimeUI.json` 第 620 行 `events.message` 绑定）。
- `ConversationMessageDispatchAsync` 按 payload（`selectType` / `selectTask` / `selectHistory`）重调 `RenderViewAsync` 重渲染。
- `selectHistory` 客户端滚动定位；中层可折叠、角色分色（模板 `round` / `message` / `tool-layer`）。

---

## 四、关键文件对照表

| 环节 | 文件 |
|------|------|
| UI 声明 / 模板 / initCommands / 消息桥绑定 | `FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json` |
| 协议面入口（Send/List/New/History/State/ListHtml/MessageDispatch） | `Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryConversation.cs` |
| 历史渲染编排（四处收敛为一处） | `Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs` |
| 会话工作流主循环（一条消息=一次运行，沿 RoundSpec 链推进） | `Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs` |
| 会话服务（定位/创建/追加历史/串行锁） | `Projects/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationSessionService.cs` |
| 会话模板（send/history/state/list） | `FileDriven/DrivenConfig/Templates/AgentRun/conversation/*.json` |
| 模板引擎（缺失模板 → 空串 fallback） | `CsharpPrj/Plate/HtmlViewer/Html/HtmlTemplateEngine.cs` |
| 通用快照循环引擎（任务池五步骨架，对照） | `CsharpPrj/Share/TaskStatusPool/Loops/SnapshotDrivenLoopEngine.cs` |


---

## 五、对照：通用快照循环引擎（`SnapshotDrivenLoopEngine`）

与本文主题并行的另一条更底层链路（`action=task op=enqueue → TaskPool → TaskRouter → SnapshotDrivenLoopEngine`）：

```
RunAsync(initialState, execute, next, ct, externalTerminate, budgetTerminate)
  while(true):
    ├─ 探索终止（外置，不可判定）：ct 取消 / externalTerminate watchdog → break + TerminatedByExternal
    ├─ 预算终止（引擎内，可判定）：budgetTerminate 返回 TS/TO/TM → break + TerminatedByBudget
    │      （权威：AX.TerminationBipartition.Budgetary，阈值到达即触发，不外置）
    ├─ execute(state)                    单轮执行体（如五步骨架）
    ├─ next(state, roundResult)          混合机制核心：读快照+产物 → LLM 提议 + 计算校验 → 变更整体状态
    └─ 更新快照，继续
```

> 该引擎是 **LLM 无关的通用轮次式快照循环**（自包含，决策域 `LoopDecision` 同库下沉）；`ConversationPipeline` 是会话历史专用的声明式工作流编排器，两者职责不同、抽象层不同。

---

## 六、要点小结

1. **单一编排点**：会话历史渲染统一收敛到 `ConversationViewService.RenderViewAsync`，四处 UI 函数共用同一条「列历史 → 取历史 → 归拢 → 渲染」链。
2. **声明式主循环**：`ConversationPipeline` 沿注入 `RoundSpec` 链推进，换 spec 即换主循环行为，不感知 collect/analyze/act。
3. **任务即对话**：会话名 = taskKey，History = Task 的上层容器（多 task 多 node 归拢），发送后经 `EnsureHistoryTaskAsync` 归拢。
4. **真源**：历史以 SQLite `ur_histories` 为真源（弃用旧 LiteDB 会话图 / `__conversation` blob）；节点产出由 `ur_nodes`（cross_db_link）合成，UI 与 AI 同源（`BuildTaskAsync`，SingleImplementation/TH-12）。
5. **交互回显**：消息桥 `postMessage` → `AgentUi_ConversationMessageDispatch` → 重渲染，支持 selectType/selectTask/selectHistory 三种定位。

