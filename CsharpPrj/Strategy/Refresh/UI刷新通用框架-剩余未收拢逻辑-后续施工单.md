# UI 刷新通用框架（Strategy.Refresh）—— 剩余未收拢逻辑清单 · 后续施工单

> **定位**：本单是《UI刷新通用框架-收拢分散刷新逻辑-重构施工单.md》的**接续单**。
> 前单已完成「机制单源 + `Share.RefreshFramework` 工程回收」（见 `README.md` §8）；
> 本单登记**尚未收拢到「单一门」的接入面**——即「机制已单源，但入口没走全框架」的部分。
>
> **核查方法**：全量 `git grep`（Frame / Projects 各库）+ 文件定位，每条结论附**证据**。
> 行号仅供定位（会随编辑漂移），**以符号名为准**。
>
> **硬约束（沿用前单）**：① 只换刷新出口，不换领域语义；② 旧面转调不删；③ 先对拍后切换；④ 框架内零领域 / 零 Plate。

---

## §0 收拢度总览

| 面 | 维度 | 唯一实现 | 收拢度 |
|---|---|---|---|
| 机制 | 节拍 / dirty 双车道合并 / 单拍互斥 / 失败置回 / 空闲零推送 | `Core/RefreshScheduler` | ✅ 单源 |
| 机制 | 编排（源→合并→节拍→渲染→落地） | `Core/RefreshService` | ✅ 单源 |
| 机制 | 源 / 渲染器登记 | `Core/RefreshRegistry` | ✅ 单源 |
| 端口 | 变化源 | `Ports/IRefreshSource` | ✅ 单源 |
| 端口 | 域渲染 | `Ports/IRefreshRenderer` | ✅ 单源（`conversation` / `db` 已登记） |
| 机制 | 版本水位 + 指纹去重判定 | `Ports/IRefreshStore` + `Core/InMemoryRefreshStore` | ✅ 单源 |
| 机制 | 写落地仲裁（latest-wins 单飞） | `Core/RefreshArbiter` | ✅ **已接（保守）**：域侧 `AgentUiRefreshSink` 注入 `RefreshService`（接管对拍待续） |
| 机制 | 指纹算法 / 追踪号 | `Core/Fnv1aFingerprint` / `Diagnostics/RefreshTraceId` | ✅ 算法单源（实例三份，G-7） |
| 契约 | 常量声明（`refresh` / `1000` / `30000`） | `Model/RefreshContract` | ⚠️ 声明单源，页面侧字面量并存（G-14） |
| 入口 | 会话页 refresh 回投 | — | ❌ **两条并列调用**（G-2） |
| 入口 | DB 页刷新 | — | ⚖️ 渲染单源；调度纳入**改判不引入**（G-3） |
| 入口 | 手动刷新按钮 | — | ⚖️ 语义已满足（页面侧 `last=0`）；优先级通道**改判不必要**（G-4） |
| 入口 | 兜底轮询 | — | ✅ **单路**（G-5）：页面侧 `fallbackTimer` 退役，兜底唯一生产者 = 宿主 `EnableIdlePolling` |
| 入口 | `MarkDirty` | — | ❌ **零调用方**（G-6） |
| 框架内 | 未接入类型（`RefreshJson` / `PollingRefreshSource` / `IRefreshClock` / `RefreshTicket` / `RefreshDelivery`） | — | ✅ 已标注`【预留】`+ 预定消费方；`RefreshCell` 已删除（G-8..G-12） |

> **一句话**：机制层收拢完毕；**入口层现有 2 个门在通着**（宿主推送、DB 命令直连渲染）——
> 会话页回投门已于第 1 批收口（G-2 → `ConversationRefreshRenderer` 唯一实现）。

### §0.1 本单执行状态（2026-10-15 第 1 批）

| 缺口 | 状态 | 说明 |
|---|---|---|
| G-2 会话页 refresh 未转调 | ✅ **已收口** | `HandleRefreshAsync` 改薄转发至 `ConversationRefreshRenderer.RenderCoreAsync`（渲染唯一实现）；直连调用已消除 |
| G-7 指纹实例三份 | ✅ **已收口** | 新增 `Fnv1aFingerprint.Default` 单例；`RefreshService`/`HtmlBridgeAssembly`/`AgentUiRefreshPublisher`/`ConversationViewService`/测试全部改引；全库 `new Fnv1aFingerprint()` **= 1 处**（框架内） |
| G-11 `RefreshScope`/`RefreshCell` 待裁 | ✅ **已裁定** | `RefreshCell` 零消费方 → **已删除**；`RefreshScope` 由 L0 契约测试消费 → **保留** |
| G-8 / G-9 / G-10 / G-12 未接入类型 | ✅ **已标注预留** | 四类均加 `【预留】` XML 注释并写明**预定消费方**（不再视为"无说明的孤儿"） |
| G-3 DB 未进调度 | ⚖️ **改判：不做** | 见下方「G-3 改判说明」 |
| G-4 手动刷新未表达优先级 | ⚖️ **改判：不必要** | E3 的实质判据「点击即刷、不受 1s 节流」已由页面侧 `last=0;fire(b)` 满足；引入 `priority` 通道只会增加一次协议往返而无行为差异 |
| G-1 | ✅ **已接（保守）** | 新增域侧 `AgentUiRefreshSink` 注入 `RefreshService` ⇒ `RefreshArbiter` 不再是死代码；**CommitAsync 接管写落地 + 写放大对拍仍待续** |
| G-5 | ✅ **已单路** | 页面侧 `fallbackTimer` 退役 + `HtmlRefreshProtocol` 不再下发 `data-poll-interval`；兜底唯一生产者 = 宿主 `EnableIdlePolling` |
| G-6 | ⏳ 未动 | `MarkDirty` 零调用方；待 G-3 落位（G-3 已改判不做 ⇒ 自然维持预留） |

#### G-3 改判说明（为什么**不**加 `MarkDirty("db")`）

实测链路：`AgentUiRefreshPublisher.MarkDirty("db")` → 调度器下一拍推送 `{cmd:'update',scope:['db']}` →
页面 `RefreshBridgeScript` 的 `scopeMatch` 按 `data-poll-subscribe` 匹配，而**会话页订阅键是 `"*"`**
（`ConversationPageRenderer.PollAttributes("*", …)`）⇒ 恒真 ⇒ 立刻回投 `{cmd:'refresh',scope:['db']}` →
宿主重渲染**会话页**（而非 DB 页）。

即：加 `MarkDirty("db")` 会产生**一次多余的会话页刷新往返**，而 DB 页本身是「点击 → 命令 → 立即渲染返回」
的命令式语义（用户显式动作 = 立即生效，本就无需节拍合并）。
**结论**：DB 页「渲染已单源」（`DbRefreshRenderer`）；「调度纳入」在当前无事件驱动需求下**不引入**。

---



## §1 已收拢（不重复登记，仅索引）

| 维度 | 唯一实现 | 关键消费方 |
|---|---|---|
| 节拍 / 合并 / 单拍互斥 / 失败置回 / 空闲零推送 | `Core/RefreshScheduler.cs` | `AgentUiRefreshPublisher` → `RefreshService`（`AgentUiHost.cs`@114/@121 Attach/Detach） |
| 编排唯一入口 | `Core/RefreshService.cs` | `ServiceOf()` 为唯一构造点 |
| 源 / 渲染器登记 | `Core/RefreshRegistry.cs` | `AgentUiRefreshPublisher.cs`@79/@80 登记 `conversation` + `db` |
| 变化源 | `Ports/IRefreshSource.cs` | `TaskPoolRefreshSource`（任务池 `TaskStateChanged`） |
| 域渲染 | `Ports/IRefreshRenderer.cs` | `ConversationRefreshRenderer` / `DbRefreshRenderer` |
| 版本 + 指纹去重 | `Ports/IRefreshStore.cs` + `Core/InMemoryRefreshStore.cs` | ① 领域三处缓存已删（`ConversationViewService.cs`@85 `Store.Decide/Commit`）；② 写落地经 `RefreshWriteArbiter`（`HtmlBridgeAssembly.cs`@61） |
| 指纹算法 | `Core/Fnv1aFingerprint.cs` | 3 处 `new`（算法一致，实例三份 → G-7） |
| 追踪号 | `Diagnostics/RefreshTraceId.cs` | `AgentUiRefreshPublisher.cs`@158、`HtmlBridgeAssembly.cs`@64 |
| 常量声明 | `Model/RefreshContract.cs` | 宿主侧 `TickMs` 已改引（`AgentUiRefreshPublisher.cs`@29） |

> 领域侧去重上收的落点（W7 已完成）：槽键 `conversation/region/{name}`、`conversation/stable-input`、
> `conversation/volatile-leaves`；判定器 `ConversationViewService.Unchanged`(@83)，四个使用点 @605 / @648 / @661 / @704。

---

## §2 P0 · 接线缺口（"已建未接 / 未转调"，非新功能）

### G-1 「`RefreshArbiter` + `IRefreshSink`」已建未接 —— 写落地仲裁未生效

| 项 | 内容 |
|---|---|
| **现状** | `IRefreshSink` 全仓**零实现**；`RefreshService` 的 `sink` 参数恒 `null` ⇒ `_arbiter` 恒 `null` ⇒ `CommitAsync` 必抛 `RefreshNotRegisteredException`。**`RefreshArbiter` 是死代码。** |
| **证据** | `Core/RefreshService.cs`：`IRefreshSink? sink = null`(@42)、`_arbiter = sink is null ? null : new RefreshArbiter(_store, sink)`(@49)、`CommitAsync`(@146)、`throw … 未注入 IRefreshSink`(@151)；`Ports/IRefreshSink.cs`(@12) 无实现类；全仓 `git grep IRefreshSink` 仅命中框架自身 |
| **实际生效的是什么** | 写仲裁实际走 `FrontEndDriven.HtmlHosting.RefreshWriteArbiter`（`HtmlBridgeAssembly.cs`@61 `new RefreshWriteArbiter(new InMemoryRefreshStore())`）→ plate `IWriteArbiter`。**只有** `IRefreshStore` 三层判定（Stale / Skip / Apply），**不含** latest-wins 单飞合并 |
| **目标** | 把 DOM / 通道写落地包成 `IRefreshSink` 注入 `RefreshService`，让 `RefreshArbiter` 的「每 slot 单飞 + 在飞期合并为最新一次」真正生效（`CommitAsync` 接管写仲裁，与 `RefreshWriteArbiter` 分工或二选一） |
| **改动文件** | `Projects\Plug\Agent\src\Agent.Ui\Ui\Hosting\AgentUiRefreshPublisher.cs`（注入 sink）；新增域侧 sink 实现（包裹写回通道） |
| **验收判据** | `RefreshArbiterTests` 扩到真路径；region/tree ping-pong 不回归；**写目标数不回涨**（先记基线） |
| **风险/回滚** | **中高**（触及写路径，须单批 + 写放大对拍） / sink 传 `null` 即回当前行为 |

### G-2 会话页 `refresh` 回投「未转调」—— 两条并列调用

| 项 | 内容 |
|---|---|
| **现状** | `AgentUiLibraryConversation.HandleRefreshAsync` **仍直连** `ConversationViewService.Shared.BuildRefreshOutputAsync(...)`；与 `ConversationRefreshRenderer` 是**两条并列调用**（底层同一方法，但"唯一入口"未成立） |
| **证据** | `Core\Functions\Conversation\AgentUiLibraryConversation.cs`@597（直连调用）；`Ui\Views\ConversationRefreshRenderer.cs` 为已登记的框架渲染器 |
| **目标** | `HandleRefreshAsync` 改经 `RefreshService.RenderAsync(new RefreshRequest("conversation", …))`（W4 补完）；`ContextJson` = 桥载荷原串 |
| **改动文件** | `AgentUiLibraryConversation.cs`（旧路径保留转调） |
| **验收判据** | 产物 diff = 0；`ConversationView_RefreshTrace` 字段不变；`agent_html_view_switch_test` 等脚本回归绿 |
| **风险/回滚** | 低 / 恢复直连调用行 |

### G-3 DB 页刷新「未进调度」—— 渲染单源但节拍在外

| 项 | 内容 |
|---|---|
| **现状** | `AgentUiLibraryBatch` 现已经 `DbRefreshRenderer`（框架端口）渲染（**渲染已单源** ✅），但**不经** `RefreshService.MarkDirty` ⇒ DB 页「何时刷」仍在框架节拍之外（仍是命令式同步返回） |
| **证据** | `Core\Functions\Batch\AgentUiLibraryBatch.cs`@40 `FallbackDbRenderer`、@146 `AgentUiRefreshPublisher.Renderer(DbRefreshRenderer.DbSlotKey) ?? FallbackDbRenderer`、@148 `new RefreshRequest(...)`；该文件全文无 `MarkDirty` |
| **目标** | `dbRefresh` 动作同时 `MarkDirty("db", RefreshPriority.High)`（W6 补完），把 DB 刷新纳入统一节拍/合并 |
| **改动文件** | `AgentUiLibraryBatch.cs`；（如需）`AgentUiRefreshPublisher.cs` 暴露 `MarkDirty(scope, priority)` |
| **验收判据** | `agent_db_batch_ops_test` 绿；DB 页 HTML 逐字不变；不出现「同一拍两次渲染」 |
| **风险/回滚** | 低（须确认无双渲染） / 去掉 `MarkDirty` 调用 |

---


## §3 P1 · 语义表达不完整

### G-4 手动刷新按钮未表达 `RefreshPriority.High`（施工单 E3 未做）

| 项 | 内容 |
|---|---|
| **现状** | 页面 `[data-poll-refresh]` 的语义是「一次高优先手动刷新」，实现上只是 JS `last=0;fire(b)`（**绕过节流**），**未经** `RefreshPriority.High` / `RefreshContract` 表达 |
| **证据** | `Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs`：`RefreshBridgeScript`(@128-190)，手动刷新分支 `b.addEventListener('click',…)`(@182-185) |
| **目标** | 点击回投携带高优先标记（如 `o.priority='high'`），宿主侧映射为 `MarkDirty(scope, RefreshPriority.High)` |
| **改动文件** | `HtmlAssetRegistry.cs`（JS 加一个字段）；`AgentUiRefreshPublisher.cs`（解析并分派 High 车道） |
| **验收判据** | 点击即刷、不受 1s 节流（行为不变）；High 车道不被 Low 抢占（`RefreshSchedulerTests.S7`） |
| **风险/回滚** | 低 / 忽略该字段即回旧行为 |

### G-5 兜底轮询「两处并存」（施工单 §3.6「仅一处生效」未达成）

| 项 | 内容 |
|---|---|
| **现状** | 宿主 `EnableIdlePolling(30000)` **常驻**（无停表条件）+ 页面 `fallbackTimer=30000`（页面在收到首个宿主推送后 `pushSeen=true` 停自主轮询） |
| **证据** | `AgentUiRefreshPublisher.cs`@83 `service.EnableIdlePolling(HtmlRefreshProtocol.IntervalMs)`；`HtmlAssetRegistry.cs`@170-171 `pushSeen`/`fallbackTimer`、@186-189 兜底分支 |
| **目标** | 收敛为单一兜底生产者：宿主任选其一（保留 `EnableIdlePolling` **或** 改用 `Ports/PollingRefreshSource`），页面侧保留「收到推送即永久停」 |
| **改动文件** | `AgentUiRefreshPublisher.cs` 或组合根 |
| **验收判据** | 30s 心跳从 2 路降到 1 路；推送通道不可用时仍 at-least-once（不失刷） |
| **风险/回滚** | 中（须验证通道故障场景） / 恢复双路 |

### G-6 宿主入口大量空置（`MarkDirty` 零调用方）

| 项 | 内容 |
|---|---|
| **现状** | `AgentUiRefreshPublisher` 对外入口中，只有 `Attach` / `Detach` / `Renderer(slot)` 有外部调用方；`RegisterSource` / `UnregisterSource` / `RegisterRenderer` / **`MarkDirty`** / `Store` / `Fingerprint` 除内部外**无调用方**。刷新实际只由 `TaskPoolRefreshSource` 事件 + `EnableIdlePolling` 驱动 |
| **证据** | 全仓 `git grep AgentUiRefreshPublisher` 仅命中 `AgentUiHost.cs`@114/@121（Attach/Detach）、`AgentUiLibraryBatch.cs`@146（Renderer）；`MarkDirty` 无外部调用 |
| **目标** | 明确各入口的**预定消费方**（G-2/G-3/G-4 落位后 `MarkDirty`/`RegisterSource` 即有真实调用方）；无预定消费方的入口按 `Strategy.RedundancyElimination` 复核删除或显式标注「预留」 |
| **验收判据** | 每个入口有调用方或显式「预留」标注 |
| **风险/回滚** | 低 / 不适用 |

### G-7 `Store` / `Fingerprint` 暴露面与领域侧用法不一致

| 项 | 内容 |
|---|---|
| **现状** | `AgentUiRefreshPublisher.Store`(@47) 已被 `ConversationViewService` 使用（@85）；但 `AgentUiRefreshPublisher.Fingerprint`(@50) **无人使用** —— `ConversationViewService` 自持 `private static readonly IFingerprint Fingerprint = new Fnv1aFingerprint()`(@560)，`HtmlBridgeAssembly` 又 `new Fnv1aFingerprint()`(@64) |
| **目标** | 统一为框架单例导出（见 G-12），消除「暴露了但没人用」的入口 |
| **验收判据** | `git grep Fnv1aFingerprint` 的 `new` 处 ≤ 1（框架内） |
| **风险/回滚** | 低 / 无状态类，回退无副作用 |

---


## §4 P2 · 框架内未接入类型（预留 or 待裁）

> 判定口径：**已建但零消费方** ≠ 缺陷；但须显式裁定「预留（写明预定消费方）」或「删除（`Strategy.RedundancyElimination`）」。
> 证据：`git grep`（Frame / Projects，排除 `BuildOutput` 与框架自身目录）零外部命中。
> G-13（`RefreshDescriptor`）为**已接入项**，列出仅为对账（表内已注明）。

| # | 类型 | 位置 | 外部消费方 | 裁定建议 |
|---|---|---|---|---|
| G-8 | `RefreshJson` | `Json/RefreshJson.cs` | **0** | 预留：作为工具面/诊断查询入口（`RefreshQuery` → 稳定 JSON）。若长期无消费方则删除 |
| G-9 | `PollingRefreshSource` | `Ports/PollingRefreshSource.cs` | **0**（仅 `Baselines\BatchScope.psd1` 注释提及） | 保留：G-5 收敛兜底轮询时即为预定消费方 |
| G-10 | `IRefreshClock` / `SystemRefreshClock` | `Ports/IRefreshClock.cs` | **0**（`RefreshService.clock` 默认 `null` ⇒ 走真实 `Timer`） | 保留：测试替身注入面（`RefreshServiceTests.NoopClock` 已在用——**测试侧唯一消费方**） |
| G-11 | `RefreshScope` / `RefreshCell` | `Model/` | **0** | **待裁**：`RefreshScope` 与「scope 字符串集合」语义重叠；`RefreshCell` 与 `RefreshDelivery` 重叠。建议删除或补使用 |
| G-12 | `RefreshTicket` / `RefreshDelivery` | `Model/` | 仅被 `Core/RefreshArbiter` 引用（**死路径**，见 G-1） | 随 G-1 一并激活；G-1 若裁定不接，则随 `RefreshArbiter` 一并复核 |
| G-13 | `RefreshDescriptor` | `Model/` | ✅ `AgentUiRefreshPublisher.cs`@74/@107 | 已接入，无需处置 |

---

## §5 半收拢 / 已知偏离（有仓库硬约束理由，**非缺陷**，登记备查）

### G-14 契约常量双端字面量（施工单 A7 降级为 B2′）

| 项 | 内容 |
|---|---|
| **现状** | 权威声明单源在 `Model/RefreshContract.cs`；但 `Plate\HtmlViewer\Html\Sync\HtmlRefreshProtocol.cs` **仍持字面量**（`"refresh"` @18 / `30000` @21 / `1000` @24），靠 `RefreshFramework.Tests\ContractParityTests` 对拍守漂移 |
| **原因（硬约束）** | `Frame\CsharpPrj\LayerBoundary.targets` 按**传递闭包**判定「Share 不得引用 Strategy」：`Plate\Plate.csproj` 引用 `Plate.HtmlViewer`，一旦 HtmlViewer 引用 `Strategy.Refresh`，**5 个 Share 工程立刻 LB0001 硬红**（PlaceholderFramework / TaskStatusPool / CommandFramework / UniversalMiddleware / EventBindingOrchestrator，实测） |
| **处置** | 保持「字面量 + 漂移对拍」。**若将来要真正编译期单源**：把 `RefreshContract` 下沉到 L0/L1（`Protocol` 或 `BasicMethod`）由 `Strategy.Refresh.Model` 再导出 |
| **验收判据** | `ContractParityTests.Contract_Parity_HostAndPage` 常绿（改任一端即红） |

### G-15 页面侧第二份实现（施工单 §3.2-B1 **明确认可保留**）

| 项 | 内容 |
|---|---|
| **现状** | `HtmlAssetRegistry.RefreshBridgeScript`(@128-190) 自带 1s trailing 节流（`throttleOf`/`refreshNow`/`pendTimer`）+ 30s 兜底（`fallbackTimer`/`pushSeen`）+ 手动刷新 + `scopeMatch` 订阅匹配 |
| **判定** | 属**通道层薄脚本**，施工单 §3.2-B1 判定「**保留**」；同理 A6 `HtmlUpdatePusher`、D5 `UiReconcileScript` 均按原设计留在 Plate |
| **真正的问题** | 只是「1s/30s 常量」与「兜底轮询双份」（→ G-5、G-14），不是"第二份实现"本身 |

### G-16 验证欠账（前单 INV 未跑全）

| 项 | 内容 |
|---|---|
| **现状** | 已跑：`RefreshFramework.Tests` 26/26（含迁移后原语 + 编排面 + 契约对拍）、`MainProj.sln`/`Agent.sln` 编译、层守卫、门禁 `RefreshFrameworkBoundary` |
| **未跑** | **INV-4**（`agent_html_view_switch_test` / `agent_db_batch_ops_test` 脚本回归）、**INV-5**（键控对齐保态：`<details>.open`/滚动/焦点不重置）、**INV-7**（`Check-AgentRuntimeSlots.ps1 -Check`） |
| **处置** | G-1/G-2/G-3 落地前**必须先跑一遍基线**（旧行为存证），否则改动无对拍依据 |
| **命令** | 见 §7 |

---


## §6 分批实施建议

> 相位纪律沿用 `BATCH_BOUNDARY_RULE = diagnose-then-edit-then-verify`；每项独立可回滚。
> **前置**：先跑 §7 的基线（INV-4/INV-5/INV-7 存证），再动 G-2/G-3/G-1。

| 批 | 内容 | 依赖 | 判据 | 风险 |
|---|---|---|---|---|
| **R0** | **基线存证**：跑 `agent_html_view_switch_test` / `agent_db_batch_ops_test` / `Check-AgentRuntimeSlots.ps1 -Check`，记录当前「每拍写目标数」「展开态/滚动/焦点」基线 | — | 三份基线齐备 | — |
| **R1** | **G-2** 会话页 refresh 回投转调 `RefreshService.RenderAsync` | R0 | 产物 diff=0；脚本回归绿 | 低 |
| **R2** | **G-3** DB 刷新 `MarkDirty("db", High)` 纳入调度 | R0 | DB HTML 逐字不变；无双渲染 | 低 |
| **R3** | **G-4** 手动刷新经 `RefreshPriority.High` 表达 | R1/R2 | 点击即刷、不受节流；S7 不抢占 | 低 |
| **R4** | **G-1** 接 `IRefreshSink` + `RefreshArbiter`（写落地走 latest-wins 单飞） | R0/R1/R2 | `RefreshArbiterTests` 扩真路径；**写目标数 ≤ 基线**；ping-pong 不回归 | **中高** |
| **R5** | **G-5** 兜底轮询收敛为单路 | R1 | 30s 心跳 2→1；通道故障仍 at-least-once | 中 |
| **R6** | **G-6/G-7/G-8..G-13** 入口与未用类型裁定（补消费方 / 标预留 / 删除） | R1..R5 | 每个入口有调用方或「预留」标注；`new Fnv1aFingerprint()` ≤ 1 | 低 |
| **R7** | **G-14 备选**：若决定编译期单源，把 `RefreshContract` 下沉到 L0/L1 | — | `LayerBoundary` 无 LB0001；`HtmlRefreshProtocol` 引常量 | 中（动分层） |

**回滚总则**：每项旧路径保留转调；对拍红 → 恢复旧调用行 / 把 `sink` 传 `null` / 恢复双路兜底。

---

## §7 核查命令（可复算）

```powershell
# ① 框架内零领域 / 零上层依赖（正负对照）
pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-RefreshFrameworkBoundary.ps1 -Check -NegativeControl

# ② 门禁聚合器单跑（注册于 GateManifests.psd1）
pwsh D:\Project\Main\Frame\Invoke\Invoke-Frame.ps1 -Action invoke-gatecheck -Only RefreshFrameworkBoundary

# ③ 行为对拍（调度 / 仲裁 / 编排 / 契约漂移）
dotnet test D:\Project\Main\Environment\CentralTest\CSharpTest\Share.Tsets\RefreshFramework.Tests\RefreshFramework.Tests.csproj

# ④ 层边界与引用体系（硬违规须 0）
pwsh D:\Project\Main\Frame\PowerShellScript\Share\Solution\Test-ProjectReferences.ps1 -Check

# ⑤ 缺口证据复算（外部消费方计数；期望：IRefreshSink=0 / RefreshArbiter=0 / RefreshJson=0 / PollingRefreshSource=0 / RefreshScope=0 / RefreshCell=0）
cd D:\Project\Main\Frame;  git --no-pager grep -n -E 'IRefreshSink|RefreshArbiter|RefreshJson|PollingRefreshSource|RefreshScope|RefreshCell' -- ':!*BuildOutput*' ':!*.md' ':!CsharpPrj/Strategy/Refresh/*'
cd D:\Project\Main\Projects; git --no-pager grep -n -E 'IRefreshSink|RefreshArbiter|RefreshJson|PollingRefreshSource|RefreshScope|RefreshCell' -- ':!*BuildOutput*' ':!*.md'

# ⑥ 指纹实例计数（期望 ≤ 1 处 new，位于框架内）
cd D:\Project\Main\Frame; git --no-pager grep -n 'new Fnv1aFingerprint' -- ':!*BuildOutput*'
cd D:\Project\Main\Projects; git --no-pager grep -n 'new Fnv1aFingerprint' -- ':!*BuildOutput*'

# ⑦ INV-4/INV-5/INV-7 基线（R0 前置；唯一入口 = Invoke-Frame.ps1 分发器，-Matrix 为必填）
pwsh D:\Project\Main\Frame\Invoke\Invoke-Frame.ps1 -Action invoke-regression -Matrix default -Project 'Agent\.Ui\.Tests\.csproj$'
pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-AgentRuntimeSlots.ps1 -Check

# ⑧ P2 唯一验证入口（build+test+gate 一次）
pwsh D:\Project\Main\Frame\Invoke\Invoke-Frame.ps1 -Action invoke-verify -Changed <files>
```

---

## §8 变更记录

| 日期 | 内容 |
|---|---|
| 2026-10-15 | 初版：接续《…-重构施工单.md》，登记 16 项剩余未收拢逻辑（G-1..G-16）；分 P0 接线缺口 / P1 语义表达 / P2 未接入类型 / 半收拢·已知偏离 四类；附 R0–R7 分批实施建议与可复算核查命令。全部结论基于 `git grep`（Frame / Projects）实证。 |
| 2026-10-15 | **第 1 批收口（快速批）**：G-2 会话页 refresh 转调（`ConversationRefreshRenderer.RenderCoreAsync`，消除直连）；G-7 指纹单例（`Fnv1aFingerprint.Default`，全库 `new` = 1）；G-11 裁定（`RefreshCell` 删 / `RefreshScope` 留——由 L0 契约测试消费）；G-8/G-9/G-10/G-12 标注`【预留】`并写明预定消费方；G-3/G-4 **改判为不做**（附实测理由）。验收：`MainProj.sln` 0 错 0 警；`Agent.sln` 0 错（9 既有警）；`RefreshFramework.Tests` 26/26；红线守卫 23 文件正/负对照通过。 |

