# UI 刷新通用框架（Strategy.Refresh）—— 以 `Strategy.Catalog` 为范式收拢分散刷新逻辑的重构施工单

> 定位：现有 UI 刷新逻辑**分散在 5 组 20+ 处**（宿主节拍 / 页面脚本 / 领域渲染 / 写仲裁 / 旁路入口），
> 本单把它**收拢到一个通用框架**上——**通用能力 ⊃ 领域适配 ⊃ 组合根** 三层分解，
> 与 `Frame/CsharpPrj/Strategy/Catalog`（通用分级查询框架）**同层同构、同纪律、同接入范式**。
>
> 本单是可执行施工单：每项含 **现状 → 目标 → 改动文件 → 代码骨架 → 验收判据 → 回滚**。
> 配套参考：《分级查询通用框架-重构施工单.md》（范式样板）、`Plug/Agent/doc/Agent模块门面框架化-施工单.md` §7.5（刷新发布提取）、
> `Plug/Agent/doc/AgentUi函数库通用化与L3门面外迁-施工单.md` C14/DC-4（刷新节流等价判据）。
>
> 三条硬约束（贯穿全单）：
> 1. **只换刷新出口，不换领域语义**——`AgentUi_*` 函数名/返回文本、会话渲染内容、DB 页交互**一字不变**。
> 2. **旧面保留转调**——过渡期旧入口（`AgentUiRefreshPublisher` 等）**不删除**，内部转调新框架；对拍红可一键回滚。
> 3. **先对拍后切换**——每项先落对拍断言（旧行为 ≡ 新行为），全绿才允许把旧实现体改为转调。

---

## §0 前置接线（最先做，且必须一次做对）

框架落点在 **Strategy 层（Layer 3）**，与 `Strategy.Catalog` 并列；产品侧（Agent/Produce）经**烘焙轨**取得类型面，
**不得** `ProjectReference` 到 Frame（`Projects\ReferenceBoundary.targets` 强判 error）。

| 步骤 | 文件 | 改动 | 说明 |
|---|---|---|---|
| S0-1 | `Frame\CsharpPrj\modules.json` | `roots` 已含 `Strategy`（Layer 3，无需改）；`projects` 追加 `"Refresh": "Strategy\\Refresh\\Refresh.csproj"`；`Strategy.csproj` 聚合引用 `$(Proj_Refresh)` | 模块路径单一真源 |
| S0-2 | 运行 `Frame\PowerShellScript\Share\Solution\Generate-ModulePaths.ps1` | 再生 `generated\Directory.Build.props.modules.generated.props` → 出现 `<Proj_Refresh>` | 改 `modules.json` 后**必跑** |
| S0-3 | `Projects\BuildFramework\CsharpBakeRefs.props` | ① 具名清单追加 `<CsharpBakedAssembly Include="Strategy.Refresh" />`（与 `Strategy.Catalog`(L115) 并列，带注释） | **唯一人工维护点**；Agent/Produce/FileDriven 三产品共用 |
| S0-4 | 运行 `Frame\CsharpPrj\Generate-CsharpBakeManifest.ps1` | 重生成 `Frame\CsharpPrj\BuildOutput\Debug\csharp-bake.manifest.json` | 新程序集进 manifest，缺件才被 `EnsureCsharpBaked` 补烘 |
| S0-5 | 运行 `Projects\BuildFramework\Check-BakeFramework.ps1` | #3 校验具名清单 ↔ manifest 一致 | 不跑则烘焙轨可能静默少件 |
| S0-6 | 编译验证 | `dotnet build Strategy\Refresh\Refresh.csproj` → `Strategy\Strategy.csproj` → Agent.sln | 任一产品内 `using Strategy.Refresh.Ports;` 可编译 = 接线成功 |

**为什么必须 S0-3**：`Plug\Agent` 与 `Plug\Produce` 走**具名清单**（未置 `CsharpBakeRefsGlob`），
未加具名条目则 `Strategy.Refresh` 类型面**不可见**；`FileDriven` 走目录通配（技术上自动可见，仍建议加，防通配被关时静默丢件）。

> 与既有 `Share.RefreshFramework` 的关系见 §2.4：**本单采用「上迁单一化」**——通用原语自 `Share\Frameworks\RefreshFramework`
> 上迁进 `Strategy\Refresh`，`Share.RefreshFramework` 退化为**类型转发 + 过渡壳**（消费方逐个切到 `Strategy.Refresh`）。

---

## §1 重构总原则

1. **数据留在原域，刷新上收框架**：各域「什么会变 / 变了长什么样」仍是各自真源（任务池事件、会话渲染产物、DB 页状态），
   只把它们包成一个端口（`IRefreshSource` / `IRefreshRenderer`）**注册进框架**；节拍 / 合并 / 去重 / 重试 / 优先级 / 仲裁全部单源。
2. **一处一端口，能合则合**：会话页、DB 页、任务总览**共用同一个** `RefreshScheduler` + `RefreshRegistry`；
   不再各自维护定时器 / 节流窗口 / 在途判定（对齐 Catalog「分组/检索/统计只有一份」）。
3. **契约是常量单源**：`refresh` 命令词、节拍 `1000ms`、兜底轮询 `30000ms` 由框架 `RefreshContract` **唯一声明**，
   宿主 C# 与页面 JS（经 `{pollAttrs|raw}` 注入）**两端同源**，消除双写漂移（INV-5）。
4. **写落地单一仲裁**：`Stale/Skip` 不触 DOM；版本水位 + 指纹去重只有一份（框架 `IRefreshStore`），
   领域侧**不再自持第二层去重缓存**（现 `_lastRegionHtml` / `_lastVolatileFingerprint` 见 §3-C）。
5. **旧面转调，不做双份真源**：旧入口内部改为调新框架并**适配回旧形态**；禁「新面一份、旧面另一份」并行维护。
6. **机制与语义分离（Catalog 红线同款）**：框架内零领域字符串（`agent`/`conversation`/`db`/`task` 等）与零 HTML/Plate 类型；
   领域词只经端口由上层注入。

---

## §2 通用框架设计（`Strategy.Refresh`）

### 2.1 三层分解

| 层 | 内容 | 落点 |
|---|---|---|
| **L0 通用框架**（机制） | 节拍调度、dirty 双车道合并、单飞互斥、失败置回重试、空闲零推送、版本+指纹仲裁、写落地端口、追踪号、契约常量、注册表、统一出口 | `Frame\CsharpPrj\Strategy\Refresh\`（本工程） |
| **L1 领域适配**（Agent） | 会话页渲染端口、DB 页渲染端口、任务池变化源、在途判定、通道解析、`AgentUi_*` 旧面转调 | `Plug\Agent\src\Agent.Ui\`（`Ui\Hosting` / `Ui\Views`） |
| **L2 组合根**（装配） | 注册源/渲染器、装配/解绑钩子、写仲裁注入、页面协议属性注入 | `Agent.Ui\Core\Host\AgentUiHost.cs`；`FrontEndDriven\HtmlHosting\` |

### 2.2 目录结构与核心类型（对齐 Catalog 的 `Model/Ports/Core/Json/Diagnostics`）

| 编号 | 职责 | 位置 | 关键类型 |
|---|---|---|---|
| R1 | 数据模型（契约零领域） | `Model/` | `RefreshScope` / `RefreshTicket` / `RefreshDelivery` / `RefreshPriority` / `AcceptDecision` / `RefreshContract`（词 + 节拍 + 轮询） |
| R2 | 领域描述（登记用） | `Model/` | `RefreshDescriptor`（`SourceId` / `Name` / `TickMs?` / `SubscribeKey` / `Priority`） / `RefreshCell`（`SlotKey` + 渲染产物） |
| R3 | 扩展端口（唯一接入点） | `Ports/` | `IRefreshSource`（变化源） / **`IRefreshRenderer`**（域渲染：`Task<object> RenderAsync(RefreshRequest, ct)`） / `IRefreshSink`（写落地） / `IRefreshStore`（状态真源） / `IFingerprint` / `IRefreshClock`（定时器工厂注入） |
| R4 | 调度引擎（单一实现） | `Core/` | `RefreshScheduler`（双车道 dirty 合并 + 节拍 + 单拍互斥 + 失败置回 + 空闲零推送 + 兜底轮询收编） |
| R5 | 写仲裁（单一实现） | `Core/` | `RefreshArbiter`（latest-wins 单飞 drain） / `InMemoryRefreshStore`（Baseline + LastFingerprint） |
| R6 | 注册与编排面 | `Core/` | `RefreshRegistry`（登记源/渲染器/描述符） / `RefreshService`（**唯一刷新编排面**：源事件 → 合并 → 调度 → 渲染端口 → sink → 仲裁） |
| R7 | JSON 门面 | `Json/` | `RefreshJson`（`RefreshQuery` 统一入参 → 稳定 JSON；错误收敛为 `status:"error"` 信封 + 注册源清单） |
| R8 | 诊断与追踪 | `Diagnostics/` | `RefreshTraceId`（`{source}-{seq:D8}`，一次刷新一条轨迹） / `RefreshException` 族 |

### 2.3 唯一刷新契约（域只需实现渲染端口）

```csharp
namespace Strategy.Refresh.Ports;

/// <summary>刷新请求（框架 → 域）：一次刷新的完整上下文（范围/受控态/追踪号）。</summary>
public sealed record RefreshRequest(
    string SlotKey,
    IReadOnlyList<string>? Scopes,          // null/空 = 显式全量
    string? ContextJson,                    // 页面回投上下文（域自解析；框架不解释）
    string? TraceId);

/// <summary>领域渲染端口（L1 唯一接入点）：从"做什么"到"长什么样"的生产者。
/// 框架只负责"何时刷 / 是否刷 / 怎么落地"，不解释渲染产物类型。</summary>
public interface IRefreshRenderer
{
    string SlotKey { get; }
    Task<object> RenderAsync(RefreshRequest request, CancellationToken ct = default);
}
```

- 领域侧**只注册一个 `IRefreshRenderer`**（会话页 / DB 页各一），不再自建定时器、节流窗口、去重表。
- 页面协议（`{cmd:'refresh'}` + `data-refresh-throttle` + `data-poll-interval`）由 `RefreshContract` 常量经
  `HtmlRefreshProtocol` 注入，**两端同源**。

### 2.4 与既有 `Share.RefreshFramework` 的关系（上迁映射）

`Share\Frameworks\RefreshFramework` 已具雏形（被 Agent.Ui 与 FrontEndDriven 消费，见 `CsharpBakeRefs.props:95`）。
**本单不重造引擎，采用「上迁单一化」**：原语整体上迁进 `Strategy\Refresh`，`Share.RefreshFramework` 退化为过渡壳。

| 既有（`Share.RefreshFramework`） | 上迁落点（`Strategy.Refresh`） | 处理 |
|---|---|---|
| `RefreshScheduler` / `IRefreshSource` / `PollingRefreshSource` / `RefreshPriority` | `Core/` · `Ports/` · `Model/` | **平移**（语义不变，命名空间改用 `Strategy.Refresh.*`） |
| `RefreshArbiter` / `InMemoryRefreshStore` / `IRefreshStore` / `IRefreshSink` / `RefreshDelivery` / `RefreshTicket` / `RefreshScope` / `AcceptDecision` | `Core/` · `Ports/` · `Model/` | **平移**（`RefreshWriteArbiter` 由 FrontEndDriven 改引 `Strategy.Refresh`） |
| `IFingerprint` / `Fnv1aFingerprint` / `RefreshTraceId` | `Ports/` · `Core/` · `Diagnostics/` | **平移** |
| （缺）统一契约常量 / 注册表 / 编排面 / 渲染端口 / JSON 门面 | `Model/RefreshContract` · `Core/RefreshRegistry+RefreshService` · `Ports/IRefreshRenderer` · `Json/RefreshJson` | **新增**（本单主体） |
| `Share.RefreshFramework` 程序集 | 保留为**类型转发壳**（`TypeForwardedTo`）+ 过渡期 `[Obsolete]` 提示；消费方逐个切到 `Strategy.Refresh` 后删除 | **收敛** |

> 层合规：`Strategy`(L3) → `Share`(L2) 向下引用合法；上迁后 `Strategy.Refresh` **零 ProjectReference 领域工程**（仅 BCL + 可选 `BasicMethod.Initialization` 的 `PortGuard`）。
> `FrontEndDriven`(Operation L4) 引用 `Strategy.Refresh` 与引用 `Strategy.Catalog` 同范式（已就绪先例）。

### 2.5 红线（写入 `Refresh.csproj` 的 `ModuleRule_Rule`）

1. 框架内**不得**出现 `agent`/`conversation`/`db`/`task`/`session`/`history` 等具体领域字符串；领域词只经端口注入。
2. 节拍 / 合并 / 去重 / 重试 / 优先级 / 仲裁**只有一份实现**；禁各域再造 `XxxTimer` / `XxxThrottle` / `XxxDirty` 变体。
3. 契约常量（`refresh` / `1000` / `30000`）**只有一处声明**（`RefreshContract`），宿主与页面两端同源。
4. 框架内**零 HTML / 零 Plate 类型**（不得 `using Plate.*` / `using UiBackend.*`）；通道解析由域侧委托注入。
5. 空闲**零推送**；未送达**必置回重试**（`at-least-once`，不静默失刷）；索引/明细不得双份真源。

---

## §3 分散点清点与收拢映射（现状 → 目标）

> 清点基线（绝对路径见 §8 索引）：宿主侧 A1–A8、页面侧 B1–B3、领域产物 C1–C6、写仲裁 D1–D5、旁路 E1–E3。
> 下表逐点给出「改什么 / 接到哪个端口 / 判据」。

### 3.1 宿主侧：变化源 + 节拍 + 推送（反向刷新）

| 现状 | 文件 | 收拢动作 | 判据 |
|---|---|---|---|
| A1 `AgentUiRefreshPublisher`（领域薄适配 + `TickMs=1000` + 静态槽 `AgentUiRefreshPublisherState`） | `Agent.Ui\Ui\Hosting\AgentUiRefreshPublisher.cs` | 保留为**兼容壳**转调 `Strategy.Refresh.Core.RefreshService`；`TickMs` 改由 `RefreshContract.TickMs` 取；运行态槽改承载 `RefreshService` | INV-5（`TickMs=1000` 字面量不变）；DC-4（合并/重试/空闲零推送等价） |
| A2 `TaskPoolRefreshSource`（`IRefreshSource`，`Name="task-pool"`） | 同文件 | 改实现 `Strategy.Refresh.Ports.IRefreshSource`；注册改经 `RefreshRegistry` | 源登记幂等；换池重订等价 |
| A3 `AgentUiHost.TaskPoolLifecycle`（`Attach`/`Detach` 接线） | `Agent.Ui\Core\Host\AgentUiHost.cs:106-123` | 改调 `RefreshService.Attach/Detach`（内部 `RegisterSource` + `Start/Stop`）；镜像 `_taskPoolMirror` 是否并入见 E2 | 装配→在役；解绑→停表（空闲零推送） |
| A4 `RefreshScheduler`（框架引擎） | `Share\Frameworks\RefreshFramework\RefreshScheduler.cs` | **上迁** `Strategy.Refresh\Core\RefreshScheduler.cs`（语义/算法零改） | 既有 `RefreshFramework.Tests` 全绿（改引用） |
| A5 `PollingRefreshSource`（低优先级兜底源） | 同框架 | 上迁 `Ports/` | 低优先不抢占高优先 |
| A6 `HtmlUpdatePusher`（`{cmd:'update',scope,v,traceId}`） | `Plate\HtmlViewer\Html\Sync\HtmlUpdatePusher.cs` | **留在 Plate**（属通道层）；其版本/追踪注入改由 `Strategy.Refresh` 提供（`IRefreshStore.NextVersion` + `RefreshTraceId`） | 载荷形态逐字不变 |
| A7 `HtmlRefreshProtocol`（`Command/ThrottleMs/IntervalMs`） | `Plate\HtmlViewer\Html\Sync\HtmlRefreshProtocol.cs` | 常量**改引** `Strategy.Refresh.Model.RefreshContract`（`Refresh` / `1000` / `30000`），消除双写 | 双端常量一致（编译期单源） |
| A8 `AgentHtmlHosting`（控件/页面名/桥函数库接线） | `Agent.Ui\Ui\Hosting\AgentHtmlHosting.cs` | 通道解析委托（`ResolveChannel`）改由组合根注入 `RefreshService` | 未挂载 → 静默零推送 |

### 3.2 页面侧：节流 + 兜底轮询 + 手动刷新（正向回投）

| 现状 | 文件 | 收拢动作 | 判据 |
|---|---|---|---|
| B1 `RefreshBridgeScript`（1s trailing 节流 + 30s 兜底 + 手动刷新 + scopeMatch + ctx 保态） | `Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs:116-190` | **保留**（页面侧薄脚本，属通道层）；`ThrottleMs`/`IntervalMs`/`Command` 改由 `RefreshContract` 注入（A7）；`pushSeen` 逻辑与宿主 `PollingRefreshSource` 收敛为"仅一处兜底生效" | 30s 兜底不双跑；1s 下限不变 |
| B2 模板 `{pollAttrs\|raw}` 占位 + 内联 `data-poll-refresh` | `FileDriven\BuildOutput\Debug\Configs\AgentRuntimeUI.json`（源配置） | 值仍由 `HtmlRefreshProtocol.PollAttributes` 构造（改常量来源） | 渲染出的属性串逐字不变 |
| B3 回投载荷解析（cmd/scope/uiState/traceId/overlay） | `Plate\...\Bridge\HtmlBridgePayload.cs`；`Agent.Ui\Ui\Views\ConversationBridgeMessage.cs` | 保留（属协议解析）；`IRefreshRenderer.RenderAsync` 收 `RefreshRequest` | 字段解析等价 |

### 3.3 领域渲染产物（会话页）

| 现状 | 文件 | 收拢动作 | 判据 |
|---|---|---|---|
| C1 `HandleRefreshAsync`（`refresh` 命令入口） | `Agent.Ui\Core\Functions\Conversation\AgentUiLibraryConversation.cs:585-599` | 改为**仅**从 `RefreshService` 取产物（不再自行拼 `BuildRefreshOutputAsync` 参数）；旧路径保留转调 | 产物 diff=0 |
| C2 `BuildRegionOutputCoreAsync` / `BuildRefreshOutputAsync`（区域单入口） | `Agent.Ui\Ui\Views\ConversationViewService.cs:606-679` | **包成** `IRefreshRenderer`（`SlotKey="conversation"`）；渲染算法不动 | 渲染内容逐字不变 |
| C3 领域侧去重缓存：`_lastRegionHtml`(:76) / `_lastVolatileFingerprint` | 同文件 | **删除**（去重上收 `IRefreshStore` + `RefreshArbiter`）；区域/易变叶子的"是否变"改由指纹单源判定 | 写放大不回涨；展开态/滚动/焦点不重置 |
| C4 `ProjectRegionTrees`(:691) / `BuildVolatileLeavesPlan`(:709) | 同文件 | 保留（领域投影），产物经 `IRefreshSink` 落地 | 叶子定点写不变 |
| C5 `Fnv1aFingerprint`（指纹单源） | `ConversationViewService.cs:551-552` | 改引 `Strategy.Refresh.Core.Fnv1aFingerprint` | 同一口径 |
| C6 诊断 `AgentUi_Refresh_push` / `ConversationView_RefreshTrace` / `_RefreshScope` | `Agent.Core\Diagnostics\agent_log_messages.json` | 保留文案（逐字）；轨迹号由框架 `RefreshTraceId` 贯穿 | 文案字面量不变 |

### 3.4 框架写仲裁 + 写落地

| 现状 | 文件 | 收拢动作 | 判据 |
|---|---|---|---|
| D1 `RefreshWriteArbiter`（`IRefreshStore`→`IWriteArbiter`） | `FrontEndDriven\HtmlHosting\RefreshWriteArbiter.cs` | 改引 `Strategy.Refresh.Ports.IRefreshStore` | `Stale/Skip/Apply` 映射不变 |
| D2 `InMemoryRefreshStore` / `IRefreshStore`（版本水位 + 指纹） | `Share\Frameworks\RefreshFramework\` | **上迁** `Strategy.Refresh\Core` | 判定规则三条不变 |
| D3 `RefreshArbiter` / `RefreshDelivery` / `IRefreshSink` / `RefreshTicket` / `RefreshScope` / `RefreshPriority` | 同框架 | **上迁** | 单飞 latest-wins 语义不变 |
| D4 `HtmlBridgeAssembly.Attach`（组装 writeArbiter + 指纹 + 追踪） | `FrontEndDriven\HtmlHosting\HtmlBridgeAssembly.cs:57-63` | 改为从 `Strategy.Refresh` 取 `RefreshWriteArbiter`/`Fnv1aFingerprint`/`RefreshTraceId` | 装配就绪报告等价 |
| D5 `UiReconcileScript`（键控对齐写原语） | `Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs:192+` | **不动**（写原语属 Plate；框架只产产物/裁决） | 保 `open`/滚动/焦点 |

### 3.5 旁路与其它入口（必须收编，否则"收拢"不彻底）

| 现状 | 文件 | 收拢动作 | 判据 |
|---|---|---|---|
| E1 **DB 页"刷新"旁路** `dbRefresh`（直连 `RenderDbTableHtmlAsync`，独立于调度链） | `Agent.Ui\Ui\Views\ConversationViewService.Db.cs:273`；`Agent.Ui\Core\Functions\Batch\AgentUiLibraryBatch.cs:133/204` | 包成第二个 `IRefreshRenderer`（`SlotKey="db"`）；`dbRefresh` 命令改经 `RefreshService`（可声明为**手动高优先**触发） | DB 页交互与渲染逐字不变；不再有第二条刷新实现 |
| E2 `_taskPoolMirror`（`TaskPoolStateMirror`，与发布器并列的第二个订阅者） | `Agent.Ui\Core\Host\AgentUiHost.cs:64,113` | 保留读侧镜像；但**变更通知**统一走 `IRefreshSource`（`InFlight` 判定改由 `RefreshService` 经镜像查询）；避免"第二个隐性刷新源" | 在途心跳周期不变 |
| E3 手动刷新按钮 `[data-poll-refresh]`（不受节流） | `Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs:182-185` | 保留；语义 = 一次**高优先手动刷新**（经 `RefreshContract`/`RefreshPriority.High` 表达） | 点击即刷，不受 1s 节流 |

### 3.6 收拢终态：分散点 → 单一实现

| 分散维度 | 现状（多份） | 终态（一份） |
|---|---|---|
| 节拍/合并/重试 | `AgentUiRefreshPublisher` + `RefreshScheduler` | `Strategy.Refresh.Core.RefreshService`（内含 `RefreshScheduler`） |
| 1s 节流下限 | 宿主 `TickMs=1000` + 页面 `data-refresh-throttle=1000` | `RefreshContract.TickMs`（两端同源） |
| 30s 兜底轮询 | 宿主 `EnableIdlePolling(30000)` + 页面 `fallbackTimer=30000` | `RefreshContract.FallbackPollMs` + 平台 `PollingRefreshSource`（仅一处生效） |
| 指纹/去重 | 领域 `_lastRegionHtml`/`_lastVolatileFingerprint` + 框架 `IRefreshStore` | `IRefreshStore`（唯一状态源）+ 框架 `IFingerprint` |
| 版本/写仲裁 | 框架 `RefreshArbiter`（未全接入） | `RefreshArbiter` + `RefreshWriteArbiter`（全接入） |
| 刷新入口 | `refresh` + `dbRefresh` + 手动按钮 | `RefreshRegistry` 登记的 `IRefreshRenderer`（会话/DB）+ `RefreshService` 唯一编排面 |
| 追踪号 | `RefreshTraceId._seq` + 页面本地号 | `RefreshTraceId`（框架唯一） |

---

## §4 工作项（按建议施工序，各项独立可回滚）

### W1 — 框架工程落地（S0 + 原语上迁）

**目标**：`Strategy\Refresh\` 建工程，`Share.RefreshFramework` 通用原语上迁，`Share` 退化为类型转发壳。

| 改动 | 文件 | 要点 |
|---|---|---|
| 新增 | `Strategy\Refresh\Refresh.csproj` | `ModuleRule`（Title/Layer=3/Description/Keywords/Rules/Prompts）+ `RootNamespace=Strategy.Refresh` + `AssemblyName=Strategy.Refresh` + `<Using Include="BasicMethod.Initialization.Guards" />` |
| 新增 | `Strategy\Refresh\Model\` · `Ports\` · `Core\` · `Json\` · `Diagnostics\` | 见 §2.2 类型表（`RefreshContract` / `IRefreshRenderer` / `RefreshRegistry` / `RefreshService` / `RefreshJson` 为**新增**；`RefreshScheduler` 等为**平移**） |
| 改 | `Share\Frameworks\RefreshFramework\*.cs` | 保留 `TypeForwardedTo` 壳 + `[Obsolete("改用 Strategy.Refresh")]` |
| 改 | `Frame\CsharpPrj\modules.json` · `generated\*.props` · `Strategy.csproj` | S0-1/S0-2 |
| 改 | `Projects\BuildFramework\CsharpBakeRefs.props` | S0-3（`Strategy.Refresh`） |

**判据**：`dotnet build Strategy\Refresh\Refresh.csproj` 0 错 0 警；`RefreshFramework.Tests` 改引用后全绿；GN-1（框架内 `using Agent.`/领域词 = 0）。

### W2 — 契约常量单源（消双端双写）

**目标**：`refresh` / `1000` / `30000` 仅在 `RefreshContract` 声明，宿主与页面两端同源。

| 改动 | 文件 | 要点 |
|---|---|---|
| 新增 | `Strategy\Refresh\Model\RefreshContract.cs` | `public const string Command="refresh"; public const int TickMs=1000; public const int FallbackPollMs=30000; public const int ThrottleMs=1000;` |
| 改 | `Plate\HtmlViewer\Html\Sync\HtmlRefreshProtocol.cs` | `Command/ThrottleMs/IntervalMs` 改引 `RefreshContract`（Plate 需引用 `Strategy.Refresh`，与 `Strategy.Catalog` 同范式） |
| 改 | `Agent.Ui\Ui\Hosting\AgentUiRefreshPublisher.cs` | `TickMs` 改引 `RefreshContract.TickMs` |

**判据**：INV-5（`TickMs=1000`、`RefreshRequestJson={"cmd":"refresh"}` 字面量不变）；页面渲染出的 `data-refresh-throttle/data-poll-interval` 值逐字不变。

### W3 — 注册与编排面（`RefreshRegistry` + `RefreshService`）

| 改动 | 文件 | 要点 |
|---|---|---|
| 新增 | `Strategy\Refresh\Core\RefreshRegistry.cs` | 登记 `IRefreshSource` / `IRefreshRenderer` / `RefreshDescriptor`；重复/未注册**调用边界抛**（`RefreshException` 族） |
| 新增 | `Strategy\Refresh\Core\RefreshService.cs` | **唯一编排面**：`Attach/Detach`、`RegisterSource/Renderer`、`Start/Stop`、`MarkDirty(scope)`、`PushOnceAsync`；内含 `RefreshScheduler`；`IRefreshSink` 落地 + `RefreshArbiter` 仲裁 |
| 新增 | `Strategy\Refresh\Json\RefreshJson.cs` | `RefreshQuery`（`sources`/`status`/`stats`）→ 稳定 JSON；错误 `status:"error"` 信封 |

**判据**：DC-1（注册幂等）/DC-2（槽等价）；`RefreshJson` 输出对拍逐字。

### W4 — 会话页渲染端口接入（`Agent.Ui`）

| 改动 | 文件 | 要点 |
|---|---|---|
| 新增 | `Agent.Ui\Ui\Views\ConversationRefreshRenderer.cs` | `IRefreshRenderer { SlotKey="conversation"; }` 包 `BuildRefreshOutputAsync`（渲染算法不动） |
| 改 | `Agent.Ui\Core\Functions\Conversation\AgentUiLibraryConversation.cs:585-599` | `HandleRefreshAsync` 改经 `RefreshService`（`ContextJson` = 桥载荷原串） |

**判据**：产物 diff=0；`ConversationView_RefreshTrace` 字段不变；UI 行为等价（`agent_html_view_switch_test` 等脚本回归绿）。

### W5 — 宿主发布器转调（旧面兼容壳）

| 改动 | 文件 | 要点 |
|---|---|---|
| 改 | `Agent.Ui\Ui\Hosting\AgentUiRefreshPublisher.cs` | 全部方法改转调 `RefreshService`；`Attach/Detach`/`RegisterSource`/`MarkDirty` 签名不变；槽承载改 `RefreshService` |
| 改 | `Agent.Ui\Core\Host\AgentUiHost.cs:106-123` | 装配钩子改调 `RefreshService.Attach/Detach` |

**判据**：DC-3/DC-4；`Check-AgentRuntimeSlots.ps1 -Check` 违规不新增（槽键 `AgentUiRefreshPublisher` 保留字面量）。

### W6 — DB 页旁路收编

| 改动 | 文件 | 要点 |
|---|---|---|
| 新增 | `Agent.Ui\Ui\Views\DbRefreshRenderer.cs` | `IRefreshRenderer { SlotKey="db"; }` 包 `RenderDbTableHtmlAsync` |
| 改 | `Agent.Ui\Core\Functions\Batch\AgentUiLibraryBatch.cs:133/204` | `dbRefresh` 改经 `RefreshService`（高优先手动刷新） |

**判据**：`agent_db_batch_ops_test` 全绿；DB 页 HTML 逐字不变。

### W7 — 领域去重上收（删第二层缓存）

| 改动 | 文件 | 要点 |
|---|---|---|
| 改 | `Agent.Ui\Ui\Views\ConversationViewService.cs` | 删 `_lastRegionHtml`(:76) / `_lastVolatileFingerprint`；"区域是否变"改由 `IRefreshStore.Decide`（指纹）单源 |
| 改 | 同文件 `Fnv1aFingerprint`(:551-552) | 改引 `Strategy.Refresh.Core.Fnv1aFingerprint` |

**判据**：写放大不回涨（每拍写目标数 ≤ 现状）；展开态/滚动/焦点不重置（键控对齐回归）。

### W8 — 写仲裁接线 + 追踪单源（`FrontEndDriven`）

| 改动 | 文件 | 要点 |
|---|---|---|
| 改 | `FrontEndDriven\HtmlHosting\RefreshWriteArbiter.cs` | 改引 `Strategy.Refresh.Ports.IRefreshStore` |
| 改 | `FrontEndDriven\HtmlHosting\HtmlBridgeAssembly.cs:57-63` | 组装改取 `Strategy.Refresh` 的 store/指纹/追踪 |

**判据**：装配就绪报告等价；region/tree ping-pong 治本不回归。

### W9 — 门禁与文档

| 改动 | 文件 | 要点 |
|---|---|---|
| 新增 | `Strategy\Refresh\README.md` | 对齐 `Catalog\README.md`（目录表 / 唯一契约 / 接入三档 / 复用设施 / 红线 / 验收 / 迁移现状） |
| 新增 | 框架红线守卫（ps1） | 框架内 `Select-String 'using Agent\.|using Plate\.|conversation|dbRefresh'` 期望 0（负对照必报） |
| 改 | `Plug\Agent\doc\Agent模块门面框架化-施工单.md` §7.5 | 标注「已由 `Strategy.Refresh` 承接」 |

**判据**：GN-1 正/负对照命中；构建 0 错 0 警。

---

## §5 对拍判据（全绿才允许切换；防假绿）

| # | 判据 | 期望 | 落点 |
|---|---|---|---|
| INV-1 | **旧名/旧文本不变** | `AgentUi_GetFunctions` 输出快照 diff=0；`{cmd:'refresh'}`/`TickMs=1000`/`data-refresh-throttle=1000`/`data-poll-interval=30000` 字面量不变 | §3.1-A1/A7、§3.2-B2 |
| INV-2 | **刷新行为等价** | `MarkDirty`×N → 单拍仅 1 次推送；`pushOnce=false` → 置回 dirty 下一拍重试；空闲 → 0 次推送 | `RefreshFramework.Tests`（DC-4） |
| INV-3 | **写仲裁等价** | 每 slot 单调版本；`version<baseline` ⇒ `Stale` 不触 DOM；指纹相同 ⇒ `Skip` | `InMemoryRefreshStore` 用例 |
| INV-4 | **渲染产物等价** | 会话页区域 HTML / DB 页 HTML / 易变叶子计划逐字不变 | 脚本回归 `agent_html_view_switch_test` / `agent_db_batch_ops_test` |
| INV-5 | **状态保真** | 展开态（`<details>.open`）/滚动/焦点不重置；写目标数 ≤ 现状 | 键控对齐回归 |
| INV-6 | **框架零领域依赖** | 框架内 `using Agent.`/`using Plate.`/领域词命中 = 0（负对照注入必报） | W9 守卫脚本 |
| INV-7 | **零进程静态可变字段** | `Check-AgentRuntimeSlots.ps1 -Check` 违规数不新增 | 现有门禁 |
| INV-8 | **构建** | `Strategy.Refresh` / `Strategy` / `Agent.sln` 各 0 错 0 警 | §7 |

**防假绿**：每项须同时给**正对照**（真实路径命中 > 0）与**负对照**（合成违规必被守卫命中）。

---

## §6 风险与回滚

| 风险 | 触发 | 缓解 | 回滚 |
|---|---|---|---|
| 上迁丢语义 | `RefreshScheduler`/`RefreshArbiter` 平移时误改 | 平移=**零算法改动**，仅改命名空间；对拍 INV-2/3 | 壳转回 `Share.RefreshFramework` |
| 双端常量漂移 | W2 只改一端 | 常量**编译期单源**（`RefreshContract`），改一处两端同步 | 恢复 `HtmlRefreshProtocol` 字面量 |
| 去重上收导致写放大 | W7 删领域缓存 | 先并行（领域缓存 + store 同时生效）灰度，对拍写目标数 | 恢复 `_lastRegionHtml` |
| 层/边界违规 | 产品误用 `ProjectReference` | `ReferenceBoundary.targets` 已守（error）；产品走烘焙 `HintPath` | 改烘焙引用 |
| BuildGuard 并发拒绝 | 施工期与其他构建并行 | 串行构建 | 串行 |
| 兜底轮询双跑 | 宿主/页面各 30s | W2 后仅 `RefreshContract.FallbackPollMs` 一处生效 | 关宿主 `EnableIdlePolling` |

**回滚总则**：每项旧实现**不删除**（只改为转调）；对拍红时把旧实现体换回、注销新 `IRefreshRenderer`/`IRefreshSource` 即完成回滚
（新框架不影响任何既有路径）。

---

## §7 命令与检查清单

```powershell
# ① Frame 侧：框架 + 聚合链
cd D:\Project\Main\Frame\CsharpPrj
dotnet build Strategy\Refresh\Refresh.csproj     # 0 警告 0 错误
dotnet build Strategy\Strategy.csproj            # 聚合链（BuildGuard 空闲时跑）

# ② 烘焙（S0-4/S0-5）
pwsh .\Generate-CsharpBakeManifest.ps1           # 生成 csharp-bake.manifest.json
pwsh ..\..\Projects\BuildFramework\Check-BakeFramework.ps1

# ③ 模块路径（改 modules.json 后必跑）
pwsh ..\PowerShellScript\Share\Solution\Generate-ModulePaths.ps1

# ④ 产品侧编译（S0-6）
dotnet build D:\Project\Main\Projects\Plug\Agent\Agent.sln
```

施工前检查清单（勾选后再动手）：
- [ ] `Strategy.Refresh.dll` 已在 `Frame\CsharpPrj\BuildOutput\Debug\` 且进 manifest
- [ ] `BuildFramework\CsharpBakeRefs.props` 具名清单含 `Strategy.Refresh`
- [ ] `modules.json` 含 `Refresh` + `generated` 已再生
- [ ] `Agent.Ui` 编译期可见 `Strategy.Refresh.*`
- [ ] 对拍脚本骨架已建（先跑旧实现记基线）
- [ ] 回滚路径已确认（旧实现体仍在，仅未启用转调）

---

## §8 路径索引与变更记录

### 8.1 清点基线（绝对路径）

| 组 | 关键文件 |
|---|---|
| A1/A2 | `D:\Project\Main\Projects\Plug\Agent\src\Agent.Ui\Ui\Hosting\AgentUiRefreshPublisher.cs` |
| A3 | `D:\Project\Main\Projects\Plug\Agent\src\Agent.Ui\Core\Host\AgentUiHost.cs` |
| A4/A5/D2/D3 | `D:\Project\Main\Frame\CsharpPrj\Share\Frameworks\RefreshFramework\*.cs` |
| A6/A7/B1/D5 | `D:\Project\Main\Frame\CsharpPrj\Plate\HtmlViewer\Html\Sync\*.cs`、`...\Html\Assets\HtmlAssetRegistry.cs` |
| B3 | `D:\Project\Main\Frame\CsharpPrj\Plate\HtmlViewer\Html\Bridge\HtmlBridgePayload.cs` |
| C1 | `D:\Project\Main\Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Conversation\AgentUiLibraryConversation.cs` |
| C2–C5 | `D:\Project\Main\Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationViewService.cs` |
| D1/D4 | `D:\Project\Main\Frame\CsharpPrj\Operation\FrontEndDriven\HtmlHosting\RefreshWriteArbiter.cs`、`HtmlBridgeAssembly.cs` |
| E1 | `D:\Project\Main\Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationViewService.Db.cs`、`Core\Functions\Batch\AgentUiLibraryBatch.cs` |
| 范式样板 | `D:\Project\Main\Frame\CsharpPrj\Strategy\Catalog\README.md`、`分级查询通用框架-重构施工单.md` |

### 8.2 变更记录

| 日期 | 内容 |
|---|---|
| 2026-10-15 | 初版：以 `Strategy.Catalog` 为范式，定 `Strategy.Refresh` 通用框架（Model/Ports/Core/Json/Diagnostics）；S0 前置接线（modules.json + 烘焙清单 + manifest）；A1–E3 全量分散点清点与收拢映射；W1–W9 工作项 + INV-1..8 对拍 + 回滚；明确「上迁单一化」处理 `Share.RefreshFramework`。 |






