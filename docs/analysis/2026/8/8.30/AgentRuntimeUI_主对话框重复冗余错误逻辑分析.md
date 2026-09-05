# AgentRuntimeUI — 主对话框函数调用链的重复 / 冗余 / 错误逻辑分析

> **版本**：1.0　**日期**：2026-08-30
> **范围**：`FileDriven/DrivenConfig/JsonConfig/AgentRuntimeUI.json`（④ 主对话框）下的函数调用链（`AgentUi_RunJson` 分发 + 会话/历史/任务池/数据处理器）
> **性质**：静态逻辑排查（基于源码取证，非实施变更）
> **关联**：`docs/analysis/2026/8/8.28/AgentRuntimeUI_历史主循环调用链.md`（调用链梳理）

***

## 一、调用链速览（本次分析范围）

```
AgentRuntimeUI.json initCommands / click=data-cmd
  ├─ Ui_ListCatalogTree / Ui_LoadCatalog / Ui_LoadCatalogDoc        （UiCatalogLibrary，模板面，孤立）
  ├─ AgentUi_RunJson（RunJsonAsync 按 action 分发）                  ← 单一请求体入口
  │    ├─ conversation.*  → AgentUiLibraryConversation
  │    ├─ task.*          → AgentUiLibraryTaskPool
  │    ├─ node/query/resource/reference/graph/config/history/flag → AgentUiLibraryData / Db / History / Flag
  ├─ AgentUi_ConversationListHtml                                  （initCommand，会话页渲染）
  └─ AgentUi_ConversationMessageDispatch                           （htmlviewer events.message 桥）
       └─ ConversationViewService.RenderViewAsync                   （4 处调用点收敛的渲染编排）
```

## 二、重复逻辑（同构体 / 多真源）

### D1 任务运行状态存在两套真相源

`AgentUiLibraryTaskPool.TaskStateMirror`（[TaskPool.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryTaskPool.cs#L37-L48)，订阅 `TaskStateChanged` 的进程内镜像）与 `ConversationViewService.BuildRunStateAsync`（[ConversationViewService.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs#L226-L252)，经 `IHistoryRuntimeService.GetOverviewAsync` 的只读投影）**各维护一套任务→状态映射**。

* 镜像口径：ur\_tasks + 订阅事件。

* 投影口径：history 编排门面的聚合视图。
  两套状态在 `TaskPoolStatus`/任务体徽章（历史树）与运行态查询中并行存在，状态时效与来源不同，易读出不一致。

### D2 wf 前缀剥离逻辑重复

`BuildTaskNodeKeysAsync`（[ConversationViewService.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs#L183-L190)）与 `RenderTaskNodesPanelAsync`（[ConversationViewService.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs#L268-L281)）都各自实现 `k.IndexOf('.')` + `Substring` 剥离 `wf.` 前缀的同一逻辑。应抽唯一助函数 `StripWfPrefix(key)`。

### D3 「未装配→诊断」骨架重复

`WithAgentString/WithAgentStringAsync/WithDbAsync/WithDbStringAsync/WithPoolStringAsync` 已部分收敛于 [AgentUiLibrarySupport.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibrarySupport.cs#L84-L125)。但 Conversation / History 库仍有 10+ 处手写 `if (svc is null) return XXNotAttached;`（如 [Conversation.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryConversation.cs#L54-L56)、[History.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryHistory.cs#L31-L33)）。骨架设施已存在但未在会话/历史域全量采用。

### D4 `conversation.addNode` 组合逻辑有半重复等价体

`ConversationAddNodeAsync`（[Conversation.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryConversation.cs#L277-L306)）一次性做「归拢历史 + 写输出节点 + AttachNodeToTask」；而 `HistoryAttachNodeAsync/HistoryAttachNodesAsync`（[History.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryHistory.cs#L51-L75)）只做 Attach 一环。两个入口对"把节点挂进任务+历史"的健壮性检查（空校验、wf 兜底、去重）各写一遍，组合语义半重复。

## 三、冗余逻辑（可简化 / 双入口 / 字段膨胀）

### R1 `DescribeMode` 三态枚举只作二分

`AgentUiLibraryRuntime.DescribeMode`（[Runtime.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryRuntime.cs#L129-L132)）：
`mode == Config ? "特定方法" : "LLM 导演"`。
`Determanistic`（零 LLM 纯规则）会落入 else 被描述为「LLM 导演」——**文案错误且误导**（详见 E3）。

### R2 `task op=status` 枚举解析两遍

[Runtime.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryRuntime.cs#L314-L316) 对同一 `req.Status` 调 `EnumConvert.TryParse<TaskExecutionStatus>` 两次（一次判空、一次取值）。应单次解析后用局部变量。

### R3 本地联调开关双入口

`RunJson` 的 `config/localtest` 用 `req.Enabled == true`（[Runtime.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryRuntime.cs#L449-L451)），同时注册独立函数 `AgentUi_LocalTestSet`（`enabled` 字符串经 `ParseBool`）。同一布尔开关两套入口两种取值判定，冗余。

### R4 HTML 装配双通道

组合根 `HtmlUiAssembly.Attach` 与补救式自装配 `AgentUiLibraryHtml.HtmlAttachAsync`（[AgentUiLibraryHtml.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryHtml.cs#L91-L170)）是两条装配路径。注释自述后者是"生产无调用点缺口"的补救——两套装配语义长期并行易漂移。

### R5 `RenderViewAsync` 各调用点重复 try/catch

`ConversationSendStandardAsync` / `ConversationListHtmlAsync` / `ConversationHistoryAsync` / `ConversationMessageDispatchAsync` 四处都包 `try/catch + ErrorClassifier + $"...失败[{cls}]"`（[Conversation.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryConversation.cs#L87-L92) 等）。渲染编排已收敛到 [ConversationViewService.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs#L41-L72)，但错误包装仍分散重复。

### R6 单一大 DTO 承载全部 action

`RunRequest`（[Runtime.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryRuntime.cs#L135-L222)）把 resource/reference/query/graph/config/flag 所有字段塞进一个 DTO，多数字段仅被单个 action 消费 → 单 action 入参污染、字段无类型约束（如 `Priority/MaxRetries` 用 `object` 再 `ToStr/JsonValueConverter` 转换，类型擦除冗余）。

## 四、错误逻辑（潜在 bug）

### E1【关键】enqueue 默认调度模式不一致，且与注释矛盾

链路：`RunJson` `task/enqueue` → `ResolveModeOrError(req.Mode)`（[Runtime.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryRuntime.cs#L278-L279)）。`req.Mode` 缺失时 `ResolveModeOrError(null)` 返回 **`Hybrid`**（[Support.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibrarySupport.cs#L28-L31)：空值统一视为 `Hybrid`），即**经 UI 请求体入队默认触发 LLM 导演**。
而 `TaskPoolEnqueueAsync` 的签名默认参数为 `schedulingMode = "Deterministic"`（[TaskPool.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryTaskPool.cs#L233)），直调默认零 LLM。
**同一 enqueue 语义，两条路径默认差异**。而代码注释明确声称"回退默认 = Deterministic，task=纯簿记不默认 LLM"——实现与意图不符。模板载入 task 模板若未显式填 mode，就会静默走 LLM。

### E2 `query nodesByTask` 必填键与实际过滤键错位

[QueryDataTextAsync.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryData.cs#L318-L321)：

```
case "nodesbytask":
    if (string.IsNullOrWhiteSpace(key)) return "（...需指定 key=taskKey）";  // 强校验 key
    return await ListNodesByTaskJsonAsync(threadId);                          // 却用 threadId 过滤
```

校验要求 `key`（taskKey）非空，但**过滤完全用** **`threadId`**，`key` 从未参与查询。若 `threadId` 未提供（null），`ListNodesByTaskAsync` 列出**全部节点**，与"按任务查节点"意图不符，且 `key` 校验形同虚设。见 [ListNodesByTaskJsonAsync](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryData.cs#L336-L352)。

### E3 `DescribeMode` 对 Deterministic 误报为「LLM 导演」

见 R1。`SchedulingMode` 实为三态（`Hybrid/Config/Deterministic`，见 [SchedulingMode.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Share/HierarchySupport/SchedulingMode.cs#L11-L17)），但 `DescribeMode` 只二分。`Determanistic` 被当作 `Hybrid` 描述——调度文案会给使用者错误信息（误以为零 LLM 模式在调 LLM）。

### E4 `selectHistory` 的 round 后端未被消费

`ConversationMessageDispatchAsync` 解析出 `round` 但渲染只传 `historyOverride`（[Conversation.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryConversation.cs#L343-L353)）。`selectHistory` 的定位完全依赖 htmlviewer 客户端 `scrollIntoView`（模板 `data-cmd=selectHistory` + `data-round`）。若某环境客户端注入缺失，点轮次会**静默无效且无后端诊断**（桥诊断 sink 是否覆盖到该用例存疑）。
另：`ParseMessagePayload` 优先取 `history` 字段，模板 `taskItem` 仅在 `{?history}` 存在才写 `data-history`；未写时 `history=null` → 每次 `selectTask` 都走 `ResolveHistoryForTask` 重推导（正确性依赖引用图，且为空态时回退 default）。

### E5 wire 函数返回风格不一致 / 疑无消费方

`ConversationSendWireAsync` / `ConversationWireAsync`（[Conversation.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryConversation.cs#L178-L214)）在错误分支返回 `JsonSerializer.Serialize(new { error = ... })` 的 JSON 字符串，被 UI/模板当作正常返回直接显示 `{error:...}` 原样；与其余函数返回「诊断文案」风格不一致。已注册（`AgentUi_ConversationSendWire`/`AgentUi_ConversationWire`），但未在 `AgentRuntimeUI.json` 中被任何控件/模板消费——候选死代码/仅外部智能体专用，需确认使用方。

### E6 `GetStatus` 会话数与任务池镜像口径分离

`AgentUiLibraryRuntime.GetStatus` 用 `db.GetActiveSessionIds()` 计会话（[Runtime.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryRuntime.cs#L46-L50)，DataStore 会话层），任务状态却读 `TaskStateMirror`（订阅 ur\_tasks）。"会话数"与"任务状态"若不一致便测出两套底层（会话层 vs 任务池），排查时易误判。

## 五、已做好的收敛（避免误判为问题）

* `ConversationViewService.RenderViewAsync` 已把 4 处「ListHistories→GetHistory→ResolveHistoryForTask→RenderFlow」编排收敛到唯一实现（[ConversationViewService.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/ConversationViewService.cs#L19-L24)）——这是正确收敛。

* `resource/reference` 的 add/remove 已用 `MutateResourceAsync/MutateReferenceAsync` 合并（[Data.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibraryData.cs#L202-L239)）。

* `ResolveModeOrError` 已在 runloop/enqueue 收敛 mode 校验（[Support.cs](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Launcher/Modules/AgentUiLibrarySupport.cs#L37-L47)）。

## 六、建议优先级

1. **高**：E1（enqueue 默认模式不一致）——统一为 Deterministic 或让注释与实现一致，避免默认误触发 LLM。
2. **高**：E2（nodesByTask 键/过滤错位）——用 `key` 推导过滤键或直接用 `key` 过滤。
3. **中**：E3/R1（`DescribeMode` 二分误报）、R2（status 枚举解析两遍）。
4. **中**：D1（两套任务状态真源）——明确单一口径。
5. **低**：D2/D3/D4/R3/R4/R5 —— 逐步抽取统一助函数与装配入口。
6. **待确认**：E4（selectHistory 客户端依赖与 history 空态回退）、E5（wire 函数消费方）。

