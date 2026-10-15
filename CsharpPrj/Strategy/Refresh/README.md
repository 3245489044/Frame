# Strategy.Refresh —— UI 刷新通用框架

> **定位**：把「何时刷 / 是否刷 / 怎么落地」收拢为**单一实现**的通用刷新框架，与 `Strategy.Catalog` 同层同构、同纪律、同接入范式。
> **单向依赖**：`Strategy`(L3) 不引用任何领域工程；消费方经**烘焙二进制引用**取类型面（产品侧不得 `ProjectReference` Frame）。
> **零领域**：框架内零 `agent`/`conversation`/`db`/`task` 等领域字符串、零 HTML/Plate 类型；领域词只经端口由上层注入。

---

## 1. 目录结构（三层分解）

| 编号 | 职责 | 位置 | 关键类型 |
|---|---|---|---|
| R1 | 数据模型（契约零领域） | `Model/` | `RefreshScope`（范围键）/ `RefreshTicket`（版本凭证）/ `RefreshDelivery`（交付信封）/ `RefreshPriority` / `AcceptDecision` / **`RefreshContract`**（契约常量） |
| R2 | 领域描述（登记用） | `Model/` | `RefreshDescriptor`（SourceId/Name/TickMs/SubscribeKey/Priority） |
| R3 | 扩展端口（唯一接入点） | `Ports/` | `IRefreshSource`（变化源）/ **`IRefreshRenderer`**（域渲染，`Task<object> RenderAsync(RefreshRequest, ct)`）/ `IRefreshSink`（写落地）/ `IRefreshStore`（状态真源）/ `IFingerprint` / `IRefreshClock`（定时器工厂） |
| R4 | 调度引擎（单一实现） | `Core/` | `RefreshScheduler`（双车道 dirty 合并 + 节拍 + 单拍互斥 + 失败置回 + 空闲零推送 + 兜底轮询收编） |
| R5 | 写仲裁（单一实现） | `Core/` | `RefreshArbiter`（latest-wins 单飞 drain）/ `InMemoryRefreshStore`（Baseline + LastFingerprint） |
| R6 | 注册与编排面 | `Core/` | `RefreshRegistry`（登记源/渲染器/描述符）/ **`RefreshService`**（唯一刷新编排面） |
| R7 | JSON 门面 | `Json/` | `RefreshJson`（`RefreshQuery` → 稳定 JSON；错误收敛为 `status:"error"` 信封 + 注册源清单） |
| R8 | 诊断与追踪 | `Diagnostics/` | `RefreshTraceId`（`{source}-{seq:D8}`）/ `RefreshException` 族 |

---

## 2. 唯一刷新契约（域只需实现渲染端口）

```csharp
namespace Strategy.Refresh.Ports;

public sealed record RefreshRequest(
    string SlotKey,
    IReadOnlyList<string>? Scopes,   // null/空 = 显式全量
    string? ContextJson,             // 页面回投上下文（域自解析；框架不解释）
    string? TraceId);

public interface IRefreshRenderer
{
    string SlotKey { get; }
    Task<object> RenderAsync(RefreshRequest request, CancellationToken ct = default);
}
```

领域侧**只注册一个 `IRefreshRenderer`**（会话页 / DB 页各一），不再自建定时器、节流窗口、去重表。

---

## 3. 接入三档

| 档 | 场景 | 做法 |
|---|---|---|
| **一档（零改动）** | 已有 `RefreshService`，只想加一个刷新面 | 实现 `IRefreshRenderer` → `RefreshService.RegisterRenderer(...)` |
| **二档（源接入）** | 有新的「变化会引发刷新」来源 | 实现 `IRefreshSource` + `RefreshDescriptor` → `RefreshService.RegisterSource(...)`；节拍/去重/重试全归框架 |
| **三档（写仲裁）** | 需要「版本 + 指纹」写落地裁决 | 注入 `IRefreshSink`；`RefreshService.CommitAsync(request, version, fingerprint)` → `Apply/Skip/Stale` |

---

## 4. 契约常量（唯一声明处）

| 常量 | 值 | 消费方 |
|---|---|---|
| `RefreshContract.Command` | `"refresh"` | 宿主推送 / 页面回投 / 桥协议 |
| `RefreshContract.TickMs` | `1000` | 宿主调度器节拍（`AgentUiRefreshPublisher.TickMs`） |
| `RefreshContract.ThrottleMs` | `1000` | 页面回投节流窗口 |
| `RefreshContract.FallbackPollMs` | `30000` | 兜底轮询周期 |

> `Plate` 位于 L1，**不得**上行引用本工程（传递闭包会毒化全部 `Share` 消费方 → `LB0001`），
> 故 `UiBackend.Html.HtmlRefreshProtocol` 保留字面量，其值由 `RefreshFramework.Tests/ContractParityTests` **漂移对拍**守护（改权威值即测试红）。

---

## 5. 红线（写入 `Refresh.csproj` 的 `ModuleRule_Rule`）

1. 框架内**不得**出现 `agent`/`conversation`/`db`/`task`/`session`/`history` 等具体领域字符串。
2. 节拍 / 合并 / 去重 / 重试 / 优先级 / 仲裁**只有一份实现**；禁各域再造 `XxxTimer`/`XxxThrottle`/`XxxDirty` 变体。
3. 契约常量只有一处声明（`RefreshContract`）。
4. 框架内**零 HTML / 零 Plate 类型**（不得 `using Plate.*` / `UiBackend.*`）。
5. 空闲**零推送**；未送达**必置回重试**（at-least-once，不静默失刷）。

守卫：`Frame\PowerShellScript\Operation\DSL\Check-RefreshFrameworkBoundary.ps1`（正/负对照）。

---

## 6. 复用设施

| 需求 | 设施 |
|---|---|
| 脏范围合并 / 节拍 | `Core/RefreshScheduler` |
| 版本 + 指纹仲裁 | `Core/RefreshArbiter` + `Ports/IRefreshStore` |
| 指纹实现 | `Core/Fnv1aFingerprint`（FNV-1a 64，跨进程稳定；**勿**用 `string.GetHashCode`） |
| 追踪号 | `Diagnostics/RefreshTraceId` |
| 状态投影 | `Json/RefreshJson.Query/Contract` |

---

## 7. 验收（INV-1..INV-8 摘要）

| # | 判据 | 落点 |
|---|---|---|
| INV-1 | 旧名/旧文本不变（`refresh`/`1000`/`30000` 字面量） | `ContractParityTests` + 页面属性串 |
| INV-2 | 刷新行为等价（MarkDirty×N → 单拍 1 推；未送达置回；空闲零推送） | `RefreshSchedulerTests` / `RefreshServiceTests` |
| INV-3 | 写仲裁等价（单调版本 / Stale 不触 DOM / 指纹相同 Skip） | `RefreshArbiterTests` |
| INV-5 | 状态保真（展开态/滚动/焦点不重置） | 键控对齐脚本回归 |
| INV-6 | 框架零领域依赖 | `Check-RefreshFrameworkBoundary.ps1` |
| INV-7 | 零进程静态可变字段 | `Check-AgentRuntimeSlots.ps1 -Check` |
| INV-8 | 构建 0 错 0 警 | `Refresh.csproj` / `Strategy.csproj` / `Agent.sln` |

---

## 8. 迁移现状（2026-10-15）

| 项 | 状态 |
|---|---|
| 通用原语上迁（`RefreshScheduler`/`RefreshArbiter`/`InMemoryRefreshStore`/`IFingerprint`/`RefreshTraceId`…） | ✅ 已迁入本工程；**`Share.RefreshFramework` 工程已回收**（源码/工程/三处登记全删） |
| 契约常量 `RefreshContract` | ✅ 新增；宿主侧已改引（`AgentUiRefreshPublisher.TickMs`） |
| `RefreshRegistry` / `RefreshService` / `RefreshJson` | ✅ 新增 |
| `IRefreshRenderer` 端口 + 会话页/DB 页渲染器 | ✅ `ConversationRefreshRenderer` / `DbRefreshRenderer`（`Agent.Ui`），宿主 Attach 时登记 |
| 宿主发布器转调 | ✅ `AgentUiRefreshPublisher` → `RefreshService`（旧入口签名不变；暴露 `Store`/`Fingerprint`/`Renderer(slot)`） |
| 写仲裁接线 | ✅ `FrontEndDriven.RefreshWriteArbiter` / `HtmlBridgeAssembly` 改引 `Strategy.Refresh` |
| DB 页旁路收编（W6） | ✅ `AgentUiLibraryBatch` 经 `DbRefreshRenderer`（框架端口）渲染；无第二条渲染实现 |
| 领域去重上收（W7） | ✅ 删 `_lastRegionHtml` / `_lastVolatileFingerprint` / `_lastRegionFingerprint`；改经 `IRefreshStore.Decide/Commit`（框架单一状态源） |
| plate 常量单源 | ⚠️ 走「字面量 + 漂移对拍」（L1 不得上行引用 L3，传递闭包会触发 LB0001） |
| 红线守卫入口 | ✅ `Check-RefreshFrameworkBoundary.ps1`，已注册进门禁聚合器（`GateManifests.psd1` → `invoke-gatecheck -Only RefreshFrameworkBoundary`） |

> 遗留：`RefreshFramework.Tests` 工程目录名沿用旧名（其被验证对象已为 `Strategy.Refresh`），改名须同步 CentralTest 索引，暂缓。

**⚠️ 尚未收拢的接入面**：本框架机制层已单源，但**入口层尚未全部经由框架单一门进出**。
已收口：会话页 refresh 渲染单源、指纹单例（`Fnv1aFingerprint.Default`）、未接入类型均已标注`【预留】`。
**剩余**：`RefreshArbiter`/`IRefreshSink` 已建未接（写落地仲裁未生效）、DB 未进调度（改判不做）、
兜底轮询两处并存、`MarkDirty` 零调用方、契约常量双端字面量、验证欠账（INV-4/5/7）。
**清单与实施批次见** `UI刷新通用框架-剩余未收拢逻辑-后续施工单.md`（含每条证据、§0.1 执行状态与可复算核查命令）。
>
> **框架级代码整理优化（2026-10-15，第 1 批已实施）**：新增通用**选择态**契约 `Abstractions/UiSelectionState`（L1）、桥回投 `uiState.select` 解析、JS 声明式选择采集（`[data-select-slot]`）、`RefreshRequest.SelectionKeys/OpenKeys`；域侧以受控字段 `_currentHistory` 承载选中历史并删除 `ResolveHistoryForTask` 的 `default` 伪造回退。
> 施工单见 `UI刷新通用框架-代码整理优化-框架级施工单.md`（通用逻辑提取 E1–E6 / 重复合并 M1–M7 / 错误修复 X1–X5 / 分批步骤 B1–B7 + 核查命令）；守卫 `Check-UiSelectionExternalization.ps1`。
>
> **后续批次已实施（2026-10-15）**：**B2** 声明式选择打标（`DefaultTemplates` 的 `foldItem`/`listItem` 增 `data-select-slot`/`data-select-key`/`data-selected`，`ConversationPageRenderer` 行模型增 `selected` ⇒ `capture()` 可回投 `uiState.select`）；**B4** 会话页 refresh 经框架**单一门**（`RefreshService.RenderAsync`，`ContextJson = HtmlBridgePayload.RawJson`，未装配/异常回退域内实现）；**B5** 写落地端口 `AgentUiRefreshSink`（**Domain L3**）接入 `RefreshService`，使 `RefreshArbiter` 不再是死代码（交付到写协调器；未装配写协调器 ⇒ false 不推进 baseline，零回归）；**B6** 兜底轮询单路（页面侧 `fallbackTimer` 退役、`HtmlRefreshProtocol` 不再下发 `data-poll-interval`，兜底唯一生产者 = 宿主 `EnableIdlePolling`）；**B7** 选择态外置守卫登记进 `GateManifests.psd1`（并补脚本成功路径 `exit 0`）。复核：M2/M3 已由 S3 收敛（`BuildRegionOutputCoreAsync` 单入口 + `RenderRegionsAsync` 复用 `RenderViewAsync`）、M5 指纹单例（`Fnv1aFingerprint.Default`）、P7 常量对拍（`ContractParityTests`）。
> 验证：`HtmlViewer`/`Refresh` 0 警 0 错、`Agent.Ui` 0 错（既有可空性告警）；`Check-UiSelectionExternalization` 正/负对照 + 聚合器 `-Only UiSelectionExternalization,RefreshFrameworkBoundary` 双绿；`RefreshFramework.Tests` 26/26。
