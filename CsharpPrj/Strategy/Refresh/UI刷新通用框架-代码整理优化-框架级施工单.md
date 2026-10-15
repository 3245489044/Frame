# UI 刷新通用框架（Strategy.Refresh）—— 代码整理优化 · 框架级施工单

> **版本**：1.0 · **日期**：2026-10-15 · **状态**：**第 1 批已实施并验证；后续批待实施**
> **定位**：本单是《UI刷新通用框架-收拢分散刷新逻辑-重构施工单》《…-剩余未收拢逻辑-后续施工单》的**接续**，
> 主题从「机制单源」转向**框架层面的代码整理优化**：提取通用逻辑 / 合并重复逻辑 / 修复错误逻辑。
> **触发**：会话页「选中历史后刷新把历史刷掉」；根因不在渲染器，而在**框架级「受控态」契约不完整**
> （只有 open/overlay/focus，缺「选择态」），迫使领域退回「扫 DOM 选择器反查领域字段 + 重推导 + 伪造 default」。
> **范围**：`Frame/CsharpPrj/Plate/HtmlViewer/**`（L1 契约与通道）· `Frame/CsharpPrj/Strategy/Refresh/**`（L3 框架）·
> `Projects/Plug/Agent/src/Agent.Ui/**`（L3 领域）。**不改**领域渲染算法与 DOM 落地原语。
> **纪律**：诊断 → 编辑 → 验证 三相位不可逆；每批独立可回滚。
> **标注**：〔事实〕=代码/实测可核 ·〔裁定〕=经确认的语义 ·〔缺口〕=待修阻断项 ·〔已落地〕=本批完成。

---

## §0 结论先行

| # | 结论 | 态 |
|---|------|----|
| 1 | 「选择态」过去**不是框架级一等状态**：框架侧只有 `open / scroll / focus`（`HtmlBridgePayload`+JS `capture()`）与 `overlay`（`UiOverlayState`）；领域「当前选中历史/任务」只能退化为 `session/history` 两个领域字段。 | 〔事实〕 |
| 2 | 该字段的回投靠 `data-poll-ctx=".item.active"` **扫 DOM、抄领域属性**；历史容器无 `.active` 载体 ⇒ `history` 永远为空。 | 〔事实〕 |
| 3 | 服务端刷新在 `history` 缺失时用 `ResolveHistoryForTask` **重推导**，其未命中回退 `SessionKeys.Default`（把「无归属」伪装成名为 `default` 的历史）⇒ 选中历史被刷掉。 | 〔事实〕 |
| 4 | **框架层正确解法**：把「选择态」补进框架级受控态契约（选择态槽 + 结构化回投），域只声明槽与消费；**同批删除**「DOM 反查」与「伪造 default」两条错误逻辑。 | 〔裁定〕 |
| 5 | 本批已落地：选择态契约（L1）+ 桥解析 + JS 声明式采集 + 框架请求承载 + 领域受控字段 + 6 处 handler 置位 + 错误回退删除 + 门禁。**构建 0 错、门禁正负对照通过**。 | 〔已落地〕 |
| 6 | 第 2 批已落地：模板接线 `data-select-slot`（B2，键已去领域化为 `foldItem`/`listItem`）、入口经框架单一门（B4）、写落地端口 `AgentUiRefreshSink` 接入（B5）、兜底轮询单路（B6）、门禁登记（B7）；M2/M3 经复核已由 S3 收敛。 | 〔已落地〕 |

---

## §1 现状与根因（框架视角）

### 1.1 框架级受控态矩阵（整理前）

| 态维度 | 契约 | 采集端 | 消费端 | 缺口 |
|---|---|---|---|---|
| 展开 `open` | `UiStateBinder` + `data-open-state` | JS `capture().open` | `ProjectRegionTrees` 绑定 `<details>` | ✅ 完整 |
| 滚动/焦点 | `UiInteractionSnapshot` | `capture().scroll/focus` | `applyState` 回放 | ✅ 完整 |
| 浮层 `overlay` | `UiOverlayState` | 仅回投「关闭」信号 | 域受控字段 `_overlayHtml` | ⚠️ 域持态，框架只传关闭 |
| **选择 `selection`** | **无** | **无（靠 `data-poll-ctx` 扫 DOM）** | **无（靠 `session/history` 领域字段）** | ❌ **缺失（根因）** |

### 1.2 三层根因

| 层 | 根因 | 受害面 |
|---|---|---|
| 框架态外置不完整 | 无 `Selection` 契约 ⇒ 选择态无法进框架 | 选择态只能走领域字段 |
| 通道用 DOM 反推模型 | `data-poll-ctx` 选择器 + `ctx-attrs` 抄领域属性（对齐 `axioms:SEP.JudgmentCarrierStructural` 反面） | 类名漂移 / 容器形态不同 / 领域字段硬编码 |
| 域状态双关 + 错误回退 | `_currentSession` 兼作会话/历史 id；`ResolveHistoryForTask` 回退 `default` | 选中历史被替换为 `default` |

### 1.3 因果链（可复现）

```
点选历史容器(.hname, data-cmd=selectHistoryContainer)
 → BuildViewOutputAsync(historyId, …, historyId)  ⇒ 右侧显示该历史（正确）
 → 下一拍刷新（宿主推送 / 30s 兜底 / 手动 ⟳）
 → 回投: {cmd:refresh, scope, traceId, uiState{open,scroll,focus}}   // history 丢失
 → RenderCoreAsync: refreshName ← CurrentSession（幸存）; historyOverride ← null
 → RenderViewAsync: currentHistory = ResolveHistoryForTask(histories, "<historyId>")  // 历史 id ≠ task key
 → 未命中 ⇒ SessionKeys.Default ("default")
 → 侧树 open 迁移 + 右区改渲染 default ⇒ "选中历史被刷掉"
```

---

## §2 整理原则（框架层面，含分层硬约束）

| # | 原则 | 说明 |
|---|------|------|
| P1 | **态外置是一等框架不变式** | 「展开/滚动/焦点/浮层/选择」一律为**模型状态**（`f(data,state)`），非 DOM 副产物；出现新态维度即补契约，不得借领域字段旁路。 |
| P2 | **通道只搬运、不解释领域** | 框架只承载「槽→值」；槽名/键语义由域注入。禁框架内出现领域词（对齐 `Refresh.csproj` `ModuleRule_Rule`）。 |
| P3 | **禁 DOM 反查模型** | 回投一律结构化（声明式槽），禁 `data-poll-ctx` + `ctx-attrs` 扫 DOM 抄领域属性（判据载体必须是结构事实，非呈现）。 |
| P4 | **空即空，禁伪造** | 未命中/未声明 ⇒ `null`，不补 `default`/占位容器（对齐《会话页读写单一真源》C1/C4）。 |
| P5 | **分层单向（硬约束）** | `Plate`(L1) **不得**上行引用 `Strategy.Refresh`(L3)——传递闭包会触发 `LB0001`。故**态契约落 L1**（`Plate.Abstractions`），L3 框架仅以 **BCL 类型/不透明串**承载态，域(L3)两侧可见做桥接。 |
| P6 | **入口单源** | 一次刷新的「何时刷/是否刷/怎么落地」有且仅有一个实现（`RefreshService`+`RefreshScheduler`）；域只实现 `IRefreshRenderer`。 |
| P7 | **改一处两端同步** | 协议常量单源（`RefreshContract`）；L1 保留字面量时以**漂移对拍**守护（`ContractParityTests`）。 |

> **P5 的落地含义**：`UiSelectionState` 属于 L1 契约（`Plate/Abstractions`）；`RefreshRequest` 只以 `IReadOnlyList<string>` 这类 BCL 类型携带 `SelectionKeys/OpenKeys`，**不引用** `UiSelectionState` 类型本身，避免 L3→L1 类型耦合反向污染。

---

## §3 通用逻辑提取（框架级契约）

| # | 契约 | 落点 | 替代的散落逻辑 | 状态 |
|---|------|------|----------------|------|
| E1 | **选择态** `UiSelectionState(Slots: slot→key?)` | `Plate/HtmlViewer/Abstractions/UiSelectionState.cs` | `.item.active` + `data-poll-ctx-attrs` 抄 `session/history` | 〔已落地〕 |
| E2 | **统一态包**（`open`/`scroll`/`focus`/`overlay`/`select` 同源回投） | `HtmlBridgePayload` | 三处平行态 | ⚠️ 部分（select 并入，未并 record） |
| E3 | **声明式选择采集** `[data-select-slot]`+`[data-selected]` → `uiState.select` | `HtmlAssetRegistry.RefreshBridgeScript`(capture) | `ctxOf()` 选择器反查 | 〔已落地〕（模板接线待办） |
| E4 | **回投协议扩展** `uiState.select:["slot=key"]` | `HtmlBridgePayload` | 领域字段从 DOM 硬抄 | 〔已落地〕 |
| E5 | **受控态槽**（域侧 `Set/Clear/Current` 单字段） | `ConversationViewService._currentHistory` | 无 `_currentHistory`、靠重推导 | 〔已落地〕 |
| E6 | **框架请求承载态** `RefreshRequest.SelectionKeys/OpenKeys` | `Strategy/Refresh/Ports/IRefreshRenderer.cs` | `ContextJson` 黑盒 + 域各自 parse | 〔已落地〕 |

**线格式约定**：选择态用 `字符串数组 "slot=key"`（与 `uiState.open` 同形），复用既有 `JsonTolerantReader.StrList` 通道，**不新增读取器**（最小改动、零新原语）。

---

## §4 重复逻辑合并（框架层面单源）

| # | 重复项 | 现象 | 合并目标 | 状态 |
|---|--------|------|----------|------|
| M1 | 刷新渲染两条并列入口 | `HandleRefreshAsync` 直连渲染器 vs 框架 `RefreshService.RenderAsync` | 统一经 `RefreshService.RenderAsync(RefreshRequest)`（旧路径薄转发） | ⏳ 待办（G-2） |
| M2 | 双产物入口 `BuildRefreshOutputAsync` ≟ `BuildViewOutputAsync` | 仅 `fingerprintShortCircuit`/overlay/scope 之差 | 合一 `BuildRegionOutputCoreAsync(mode, state, …)` | ⏳ 待办 |
| M3 | 双渲染入口 `RenderViewAsync` ≟ `RenderRegionsAsync` | 整页 ≟ 区域，仅 `renderPage` 之差 | 合一（装配一次 + 开关） | ⏳ 待办 |
| M4 | 节流/兜底双端各一份 | 页面 JS 1s+30s ↔ 宿主 `EnableIdlePolling`/`PollingRefreshSource` | 兜底单路；常量引 `RefreshContract` | ⏳ 待办（G-5/G-14） |
| M5 | 指纹实例多份 | 域/装配各自 `new` | 全库 `new Fnv1aFingerprint` ≤ 1 | ⚖️ 已收敛为 `Default` 单例（对账） |
| M6 | 写仲裁三层 | 域去重 + `RefreshWriteArbiter` + plate `_lastWrite`；`RefreshArbiter`+`IRefreshSink` 未接 | 接 `IRefreshSink` → `RefreshArbiter` latest-wins 生效 | ⏳ 待办（G-1） |
| M7 | 页面侧第二份实现 | `RefreshBridgeScript` 自带节流/兜底 | **保留**（通道薄脚本）；退役其中「ctx 反查」部分 | 〔已落地〕（退役 ctx） |

---

## §5 错误逻辑修复

| # | 错误 | 修法 | 状态 |
|---|------|------|------|
| X1 | `ResolveHistoryForTask` 未命中回退 `default`（伪造历史） | 未命中 → `null`（空即空） | 〔已落地〕 |
| X2 | 选择态缺失静默重推导（把「未知选择」当「默认选择」） | 选择态**优先**：`payload.History`/`uiState.select` → 受控 `_currentHistory` → 归属解析 | 〔已落地〕 |
| X3 | `_currentSession` 双语义（会话/历史 id 混用） | 拆出独立 `_currentHistory`；`_currentSession` 保持会话语义 | 〔已落地〕 |
| X4 | 选择态按 class 猜（`.item.active` 漂移/恒空） | 退役选择器反查，改声明式槽 | 〔已落地〕 |
| X5 | overlay 与选择态处理路径不一致 | 两者统一为「受控态槽」（刷新同源消费） | 〔已落地〕 |


---

## §6 整理优化步骤（分批，框架层面）

> 依赖序：契约 → 通道 → 域受控态 → 入口合并 → 写仲裁 → 去重/常量 → 门禁。
> 每批独立可回滚；先跑基线再动改；批次边界 `diagnose-then-edit-then-verify` 不可逆。

| 批 | 目标 | 落点（框架/域） | 判据 | 回滚 | 状态 |
|----|------|----------------|------|------|------|
| **B1 契约层** | 选择态契约 + 桥解析 + 框架请求承载 | `Plate/Abstractions/UiSelectionState.cs`(新)·`HtmlBridgePayload.cs`·`Strategy/Refresh/Ports/IRefreshRenderer.cs` | 编译 0 警；契约形状/解析单测 | 新类型不接线即零变更 | 〔已落地〕 |
| **B2 通道层** | JS 声明式选择采集；退役 ctx 反查；**模板打标**（模板键已去领域化：`foldItem`/`listItem`） | `HtmlAssetRegistry.cs`(capture)·`HtmlAssetRegistry.DefaultTemplates.cs`(foldItem/listItem)·`ConversationPageRenderer.cs`(行模型 `selected`) | 渲染 HTML 含 `data-select-slot`/`data-selected`；`uiState.select` 含 `history=<id>`；`data-poll-ctx` 零命中 | 去属性即回退 | 〔已落地〕 |
| **B3 域态外置** | 受控 `_currentHistory` + 选择态优先 + 删 default 回退 | `ConversationViewService.cs`·`ConversationTreeModel.cs`·`ConversationRefreshRenderer.cs`·`ConversationBridgeMessage.cs`·`AgentUiLibraryConversation.cs` | 选中历史后连点刷新/跨 30s：`region-aside`+`region-main` **不切 default** | 恢复默认回退行 | 〔已落地〕 |
| **B4 入口合并** | `HandleRefreshAsync` 转调 `RefreshService.RenderAsync`（`ContextJson=HtmlBridgePayload.RawJson`，未装配/异常回退域实现）；M2/M3 复核（S3 已收敛） | `AgentUiLibraryConversation.cs`·`AgentUiRefreshPublisher.cs`·`HtmlBridgePayload.cs`(RawJson) | 渲染唯一入口经框架门；产物等价；跨平台 diff=0 | 恢复直连 | 〔已落地〕 |
| **B5 写仲裁接线** | 接 `IRefreshSink`（`AgentUiRefreshSink`）→ `RefreshArbiter` 不再是死代码 | `AgentUiRefreshPublisher.cs` + `AgentUiRefreshSink.cs`(新) | 写目标数 ≤ 基线；ping-pong 不回归 | sink 传 `null` | 〔已落地〕（交付接管对拍待续） |
| **B6 去重/常量** | 兜底轮询单路（页面 `fallbackTimer` 退役）；指纹单例/常量对拍**已达成** | `HtmlAssetRegistry.cs`·`HtmlRefreshProtocol.cs` | 30s 心跳 2→1；`new Fnv1aFingerprint` ≤ 1 | 恢复页面兜底分支 | 〔已落地〕 |
| **B7 门禁** | 选择态外置守卫 + 聚合器登记 | `Check-UiSelectionExternalization.ps1`·`GateManifests.psd1` | 正/负对照红绿；聚合器 PASS | 删条目 | 〔已落地〕 |

### §6.1 B3 展开为可执行步骤（本次已按此执行）

1. **加受控字段**：`ConversationViewService` 增 `private string? _currentHistory;` + `SetCurrentHistory(string?)` + `CurrentHistory`（对齐既有 `_overlayHtml`/`SetOverlay` 模式）。
2. **改渲染取值**：`RenderViewAsync` 的 `currentHistory` 计算改为
   `historyOverride ?? _currentHistory ?? ResolveHistoryForTask(...) ?? SessionKeys.Default`，并把结果回写 `_currentHistory`（使选中黏住）。
3. **删错误回退**：`ConversationTreeModel.ResolveHistoryForTask` 返回类型改 `string?`，未命中 `return null`（不再 `SessionKeys.Default`）。
4. **刷新拍置位**：`ConversationRefreshRenderer.RenderCoreAsync` 读取 `uiState.select.history` / `payload.History`，命中即 `SetCurrentHistory`；缺省沿用受控态。
5. **显式动作置位**：`AgentUiLibraryConversation` 6 处 handler（`selectHistoryContainer`/`selectTask`/`selectType`/`composerSend`/`showAiPayload`/`selectHistory`）在携带 `history` 时 `SetCurrentHistory`。
6. **退役反查**：`ConversationPageRenderer` 的 `PollAttributes("*", null, null)`（原第三/四参 = `.item.active`/`session,history`）。
7. **通道采集**：`capture()` 增 `select[]`（扫描 `[data-select-slot]` 中带 `[data-selected]` 者）。

### §6.2 框架与域的职责边界（每批都须遵守）

| 层 | 负责 | 禁止 |
|----|------|------|
| `Strategy.Refresh`(L3) | 节拍/合并/单飞/去重/仲裁 + 承载态（BCL 类型） | 领域词；引用 Plate 类型 |
| `Plate.HtmlViewer`(L1) | 态契约形状 + 回投协议解析 + 采集/绑定原语 | 上行引用 L3；解释领域语义 |
| `Agent.Ui`(L3 域) | 声明槽名 + 消费态 + 渲染 `data-selected`；只实现 `IRefreshRenderer` | 自建节拍/去重；扫 DOM 反查领域字段 |


---

## §7 本批已落地（B1 / B3 / B7 + 通道采集）

### 7.1 文件清单

| # | 文件 | 改动 |
|---|------|------|
| 1 | `Plate/HtmlViewer/Abstractions/UiSelectionState.cs` **（新）** | 选择态契约（槽→键；`FromKeys` 解析 `"slot=key"`） |
| 2 | `Plate/HtmlViewer/Html/Bridge/HtmlBridgePayload.cs` | `SelectionKeys` / `Selection`（读 `uiState.select`） |
| 3 | `Plate/HtmlViewer/Html/Assets/HtmlAssetRegistry.cs` | `capture()` 增声明式选择采集 |
| 4 | `Strategy/Refresh/Ports/IRefreshRenderer.cs` | `RefreshRequest` 增 `SelectionKeys`/`OpenKeys` |
| 5 | `Agent.Ui/Ui/Views/ConversationBridgeMessage.cs` | 桥映射增 `SelectionKeys` |
| 6 | `Agent.Ui/Ui/Views/ConversationTreeModel.cs` | `ResolveHistoryForTask` → `string?`，未命中 `null` |
| 7 | `Agent.Ui/Ui/Views/ConversationViewService.cs` | `_currentHistory` + `SetCurrentHistory/CurrentHistory`；渲染取选择态优先 |
| 8 | `Agent.Ui/Ui/Views/ConversationRefreshRenderer.cs` | 刷新拍选择态优先置位 |
| 9 | `Agent.Ui/Ui/Views/ConversationPageRenderer.cs` | `PollAttributes("*", null, null)`（退役 ctx 反查） |
| 10 | `Agent.Ui/Core/Functions/Conversation/AgentUiLibraryConversation.cs` | 6 处 handler 选中即置受控历史 |
| 11 | `Frame/PowerShellScript/Operation/DSL/Agent/Check-UiSelectionExternalization.ps1` **（新）** | 门禁 C1–C5 + 负对照 |

### 7.2 验证证据

| 项 | 命令/方式 | 结果 |
|----|-----------|------|
| 契约层编译 | `dotnet build Frame/CsharpPrj/Plate/HtmlViewer/HtmlViewer.csproj -c Debug` | **0 警 0 错** |
| 框架层编译 | `dotnet build Frame/CsharpPrj/Strategy/Refresh/Refresh.csproj -c Debug` | **0 警 0 错** |
| 领域层编译 | `dotnet build Projects/Plug/Agent/src/Agent.Ui/Agent.Ui.csproj -c Debug` | **0 错**（7 既有 nullability 警告） |
| 门禁（正） | `pwsh Frame/PowerShellScript/Operation/DSL/Agent/Check-UiSelectionExternalization.ps1 -Check` | C1–C5 **全 PASS** |
| 门禁（负） | 同上 `-NegativeControl` | **PASS**（判据可区分正/负样本） |

---

## §8 待办与依赖

| 待办 | 依赖 | 说明 |
|------|------|------|
| ~~B2 模板接线~~ | — | ✅ **已落地**：模板键已去领域化为 `foldItem`/`listItem`，落点 = `HtmlAssetRegistry.DefaultTemplates.cs`（BuildOutput 资产包为遗留产物，非源）；加 `data-select-slot="history"/"task"` + `data-select-key` + `data-selected`，行模型补 `selected` |
| ~~B4 入口合并~~ | B3（已） | ✅ **已落地**：`HandleRefreshAsync` 经 `RefreshService.RenderAsync`（`ContextJson=RawJson`，未装配回退域实现）；M2/M3 由 S3 复核收敛 |
| ~~B5 写仲裁接线~~ | 基线存证 | ✅ **已落地（保守）**：`AgentUiRefreshSink` 注入 `RefreshService`，`RefreshArbiter` 不再死代码；**写放大对拍 / CommitAsync 接管**仍待续 |
| ~~B6 去重/常量~~ | — | ✅ **已落地**：页面侧兜底 `fallbackTimer` 退役 + 不再下发 `data-poll-interval`（宿主 `EnableIdlePolling` 单源）；指纹单例/常量对拍本就已达成 |
| ~~B7 登记~~ | — | ✅ **已落地**：`GateManifests.psd1` 增 `UiSelectionExternalization`；并补脚本成功路径 `exit 0`（否则聚合器判 2） |
| 文档对齐 | — | ✅ **已落地**：`README.md` §8、本单 §0/§6/§8/§11；`…剩余未收拢逻辑-后续施工单.md` §0.1 G-1/G-5 状态 |


---

## §9 验收判据（可机械复算）

| 场景 | 期望 |
|------|------|
| 选中历史容器后连点刷新 ≥5 次 / 跨 30s | 右区仍显示该历史；`region-aside`+`region-main` **不切到 `default`** |
| 选中历史后 `ConversationView_RefreshTrace` | `sessionName` 与选中历史一致；无「选择态丢失」导致的整区改写 |
| 展开侧栏 `historyItem` / 对话内 `tool-layer` 后刷新 | 展开态按 key 保留（`uiState.open`） |
| 显式 `selectTask` 后刷新 | 主视图仍为该 task 所属历史（不漂移到 `default`） |
| 全仓 | `data-poll-ctx` 反查 = **0**；`ResolveHistoryForTask` 内 `return SessionKeys.Default` = **0** |
| 门禁 | `Check-UiSelectionExternalization.ps1 -Check` 绿；`-NegativeControl` 绿 |
| 编译 | `HtmlViewer`/`Refresh` 0 警 0 错；`Agent.Ui` 0 错 |

---

## §10 核查命令（可复算）

```powershell
# ① 选择态外置守卫（正/负对照）
pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Agent\Check-UiSelectionExternalization.ps1 -Check
pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Agent\Check-UiSelectionExternalization.ps1 -NegativeControl

# ② 契约/框架/领域 三层编译
dotnet build D:\Project\Main\Frame\CsharpPrj\Plate\HtmlViewer\HtmlViewer.csproj -c Debug
dotnet build D:\Project\Main\Frame\CsharpPrj\Strategy\Refresh\Refresh.csproj -c Debug
dotnet build D:\Project\Main\Projects\Plug\Agent\src\Agent.Ui\Agent.Ui.csproj -c Debug

# ③ 证据复算：DOM 反查与伪造回退应为 0
cd D:\Project\Main; git --no-pager grep -n 'data-poll-ctx' -- 'Projects/Plug/Agent/src' ':!*BuildOutput*'
cd D:\Project\Main; git --no-pager grep -n 'PollAttributes(' -- 'Projects/Plug/Agent/src' ':!*BuildOutput*'

# ④ 刷新框架红线（框架内零领域）
pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Agent\Check-RefreshFrameworkBoundary.ps1 -Check -NegativeControl

# ⑤ 契约漂移对拍
dotnet test D:\Project\Main\Environment\CentralTest\CSharpTest\Share.Tsets\RefreshFramework.Tests\RefreshFramework.Tests.csproj

# ⑥ P2 唯一验证入口（build+test+gate 一次）
pwsh D:\Project\Main\Frame\Invoke\Invoke-Frame.ps1 -Action invoke-verify -Changed <files>
```

---

## §11 变更记录

| 日期 | 内容 |
|------|------|
| 2026-10-15 | 初版：框架级代码整理优化施工单。结论：根因 = 框架受控态缺「选择态」契约；给出通用逻辑提取（E1–E6）、重复合并（M1–M7）、错误修复（X1–X5）、分批步骤（B1–B7）与判据/核查命令。**第 1 批（B1/B3/B7 + 通道采集）已实施**：11 文件改动 + 1 新门禁；三层编译 0 错、门禁正负对照通过。待办：模板接线、入口合并、写仲裁接线、去重/常量收敛、门禁登记、文档对齐。 |
| 2026-10-15 | **第 2 批（B2/B4/B5/B6/B7 + 文档）已实施**：B2 模板打标（`DefaultTemplates.cs` 的 `foldItem`/`listItem` 增 `data-select-slot`/`data-select-key`/`data-selected`，`ConversationPageRenderer` 行模型增 `selected`）；B4 `HandleRefreshAsync` 经框架单一门（新增 `HtmlBridgePayload.RawJson` + `AgentUiRefreshPublisher.TryRenderViaFrameworkAsync`）；B5 新增 `AgentUiRefreshSink` 并注入 `RefreshService`；B6 页面侧兜底退役 + `HtmlRefreshProtocol` 不再下发 `data-poll-interval`；B7 门禁登记 `GateManifests.psd1` + 脚本补 `exit 0`。验证：`HtmlViewer`/`Refresh` 0 警 0 错、`Agent.Ui` 0 错；`Check-UiSelectionExternalization` 正/负对照 + 聚合器双绿；`RefreshFramework.Tests` 26/26。复核：M2/M3 已由 S3 收敛，M5/P7 已达成。 |

