# UI 刷新通用框架（Strategy.Refresh）—— 刷新闭环、在途租约与无进展熔断 · 框架级重构施工单

> **版本**：1.0 · **日期**：2026-10-19 · **状态**：待评审 / 待实施
> **定位**：本单是《UI刷新通用框架-收拢分散刷新逻辑-重构施工单》《…-剩余未收拢逻辑-后续施工单》《…-代码整理优化-框架级施工单》的**接续**。
> 前三单解决「机制单源 / 入口收拢 / 受控态外置」；本单解决**框架缺少闭环反馈**导致的
> 「1 秒刷新风暴 + 空产物 no-op + 在途误判 + 无进展不熔断」这一类系统性问题。
> **触发事件**：2026-10-19 GUI 脚本 `agent_history_cline_read_file_local_test` 运行中出现
> 每 1 秒 `cmd=refresh` 推送、每约 5 秒分发规划、`conversation/send` 长时间不返回，
> 最终宿主在约 31.6 秒关窗，脚本 `completed=false`。
> **范围**：
> - `Frame/CsharpPrj/Strategy/Refresh/**`（L3 刷新框架，主战场）
> - `Frame/CsharpPrj/Share/Orchestration/TaskStatusPool/**`（任务状态事件 / 镜像 / 激活语义）
> - `Projects/Plug/Agent/src/Agent.Ui/**`（领域适配：在途租约源、页面回投、信号隔离）
> - `Projects/FileDriven/**`（脚本生命周期总预算 / 命令心跳）
> **纪律**：`diagnose-then-edit-then-verify` 三相位不可逆；每批独立可回滚；旧面先转调、后删除。
> **标注**：〔事实〕= 代码/日志可核 ·〔裁定〕= 本单确认的目标语义 ·〔缺口〕= 待修阻断项 ·〔目标〕= 本单交付物。

---

## §0 结论先行

| # | 结论 | 态 |
|---|------|----|
| 1 | 现象不是单点死锁，而是**三条链路叠加**：1 秒刷新心跳、5 秒多级信号 flush、同步 `conversation/send` 阻塞 + 宿主关窗。 | 〔事实〕 |
| 2 | 1 秒循环的**点火条件**不是 LLM 调用，而是脚本以 `active=false` 预挂的休眠子任务被 `IsInFlight` 当成「在途」。日志中首次心跳（19:59:25.741）早于 `send`（19:59:32.391）。 | 〔事实〕 |
| 3 | 5 秒分发节奏与 `SignalTriggerPolicy.FlushWindowMs = 5000` 完全吻合；信号暂存条目本身也是 `Active=false + NotStarted`，继续喂养 1 秒心跳。 | 〔事实〕 |
| 4 | 脚本卡在同步 `conversation/send`：`defer=false`，命令预算 300 秒；宿主在约 31.6 秒关窗，框架如实归类为 `HostWindowClosed / ExternalTermination`，**不是命令超时**。 | 〔事实〕 |
| 5 | 框架现有兜底只覆盖「推送未送达」和「命令超时」，**不覆盖「推送已送达但内容永远无变化」**；`RefreshArbiter` / `IRefreshSink` 在页面回投路径上未生效。 | 〔缺口〕 |
| 6 | 仅把 `NotStarted` 从 `IsInFlight` 删除只能治本次；`Paused` 卡死、`Running` 无进度、未来新增休眠簿记仍会复现。必须把刷新升级为**带租约、带产物、带进度、带结果的闭环周期**。 | 〔裁定〕 |
| 7 | 目标架构 = 闭环控制环 + 在途租约 + 触发分车道 + 单门执行 + 任务/信号语义分离 + 脚本总预算。 | 〔目标〕 |

---

## §1 事件复盘（2026-10-19）

> 时间取自运行日志；行号仅供定位，符号名为准。

### 1.1 关键时间线

| 时间 | 事件 | 证据 |
|---|---|---|
| 19:59:24.304 | 脚本加载完成，`totalCommands=79` | `FileDriven.Execution` 日志 |
| 19:59:25.359 / 27.202 | 首次出现 `[分发] 目标集规划归因` | `MemberSet_TargetPlan` |
| 19:59:25.741 | **首次 1 秒心跳写回**，早于 `conversation/send` | `写回 conversationHistoryViewer 合并（在飞中，latest-wins）` |
| 19:59:29.239 | `tp_hist_read_child`：`status=NotStarted, round=0, agentStatusCount=0` | `AgentHistoryRuntimePort_actor_unbound` |
| 19:59:31.857 | cline 池预热完成 | `StateBlock` 预热汇总 |
| 19:59:32.391 | `RunJson` 进入 `action=conversation, op=send` | `BusForward` legacy 回落 |
| 19:59:33.178 起 | 每 1 秒 `cmd=refresh` + `目标 conversationHistoryViewer → 跳过（空产物）` | `ConversationView_RefreshTrace` / `EventBridge_writeback` |
| 19:59:37.101 / 42.138 / 47.042 / 52.140 | 每约 5 秒一次分发规划 | 与 `FlushWindowMs=5000` 吻合 |
| 19:59:53.250 | 宿主关窗，已执行 40/79，`completed=false` | `ScriptExecutionPort_GuiModeComplete` |
| 19:59:53.255 | 工作流结束，`Success=false, Time=31617ms` | `FrameworkExecutor_Complete` |

### 1.2 三条闭环

```text
闭环 A（1s 心跳）
  休眠任务(NotStarted, Active=false)
    → IsInFlight()=true
    → RefreshScheduler 每拍推送
    → 页面回投 refresh
    → 渲染产物 Unchanged/Skip
    → 无 DOM 写、无状态变化
    → 下一拍继续

闭环 B（5s 信号 flush）
  多级信号入暂存(NotStarted, Active=false)
    → SignalTriggerPolicy 5s 时间窗到期
    → state_response / 目标集规划 / 分发
    → 若再次产生 pending 或子任务未推进
    → 下一个 5s 继续

闭环 C（脚本阻塞）
  conversation/send(defer=false, timeout=300s)
    → HistoryRuntime.StartAsync → ConversationSessionService.SendAsync
    → 等待子任务完整轮次
    → 宿主约 31.6s 关窗
    → HostWindowClosed / ExternalTermination
```

> 三个闭环互相喂养：A 让日志与页面持续忙碌，B 让分发持续发生，C 让脚本无法在宿主预算内完成。
> 其中 **A 是主循环**，B/C 是放大器与终止条件。

---

## §2 根因模型（框架级）

### 2.1 六个结构性缺口

| # | 缺口 | 框架级表现 | 直接后果 |
|---|---|---|---|
| 1 | **在途模型是 bool** | `hasInFlight: Func<bool>`，无身份/年龄/租约/进度 | 休眠任务、停滞任务、正常任务无法区分 |
| 2 | **产物模型无语义** | `IRefreshRenderer.RenderAsync` 返回 `object` | 框架看不到 `Changed/Unchanged/Skipped/Failed` |
| 3 | **交付模型无结果** | `IRefreshSink.DeliverAsync` 返回 `bool` | 「送达成功」被当作「有进展」 |
| 4 | **状态源无进度** | `IRefreshStore` 只有 version/fingerprint | 无法累计 `ConsecutiveNoChange` / `Stalled` |
| 5 | **入口双门** | 宿主 Push 走 Scheduler；页面 Pull 只走 `RenderAsync` | Pull 绕过 `CommitAsync/RefreshArbiter/IRefreshSink` |
| 6 | **状态空间混用** | 信号暂存写成 `Active=false + NotStarted` 的任务行 | 信号簿记进入任务状态镜像，污染「在途」判定 |

### 2.2 代码证据

| 事实 | 落点 | 说明 |
|---|---|---|
| 在途判定把 `NotStarted` 算在内 | `AgentUiRefreshPublisher.cs` · `IsInFlight()` | `NotStarted / Running / Paused` 一刀切；没有 `Active` / `Kind` / `Lease` |
| 心跳无上限 | `RefreshScheduler.cs` · `TickAsync()` | `if (!dirty && !_hasInFlight()) return;` 之后直接推送；无连续次数/退避/熔断 |
| 渲染产物是不透明对象 | `Strategy/Refresh/Ports/IRefreshRenderer.cs` | `Task<object> RenderAsync(...)`，无 Outcome/Fingerprint/ProgressToken |
| 写落地只返回 bool | `Strategy/Refresh/Ports/IRefreshSink.cs` | `Task<bool> DeliverAsync(...)`，无 `ContentChanged` |
| Store 无进度计数 | `Strategy/Refresh/Ports/IRefreshStore.cs` | 只有 `NextVersion / Decide / Commit` |
| 页面回投只走 Render | `AgentUiLibraryConversation.cs` · `HandleRefreshAsync` | `RefreshService.RenderAsync`；未走 `CommitAsync`，框架仲裁不在路径上 |
| 空产物被合法跳过 | `ConversationViewService.cs` · `BuildRegionOutputCoreAsync` | 稳定输入未变 ⇒ `RenderOutput.Skip`；`HtmlEventBridge` 打 Info「空产物 = 无写回内容」 |
| 信号暂存用任务行 | `SignalAccumulator.cs` · `EnqueueAsync` | `Active=false` + `Status=NotStarted` + `ExecutorId=signal_flush` |
| 状态事件无 Kind/Active | `TaskPoolStateChangedEventArgs.cs` | 只有 `TaskKey / FromState / ToState / Reason` |
| 镜像只有状态字符串 | `TaskPoolStateMirror.cs` · `OnStateChanged` | `Set(TaskKey, ToState.ToString())`，丢失 Active/Kind/Lease |
| 管线无总超时 | `FileDrivenPipelineRunner.cs` · `ExecuteAsync` | 管线级超时已移除，传 `CancellationToken.None`，只靠逐命令 timeout |
| 宿主关窗即终止 | `ScriptExecutionPort.cs` · `CompleteFromFormClosed` | `HostWindowClosed / ExternalTermination`；框架没有自己的整脚本预算 |

### 2.3 为什么「只改 IsInFlight」不够

只排除 `NotStarted` 可以消掉本次 1 秒风暴，但仍有：

1. `Running` 但 LLM/daemon 永久不返回：仍每 1 秒推送，直到命令自身超时；
2. `Paused` 永久挂起：仍每 1 秒推送；
3. 未来新增任何「休眠但不是 NotStarted」的簿记类型：问题原样复现；
4. 页面 Pull 仍绕过仲裁，空产物仍不进入框架进度统计；
5. 脚本仍没有总预算，宿主关窗仍是唯一终止手段；
6. 信号暂存仍污染任务状态空间。

因此本单的目标不是「删一个枚举分支」，而是**更换刷新框架的运行时模型**。

### 2.4 与既有缺口编号的关系

本单吸收并前置以下既有缺口：

| 既有 | 内容 | 本单关系 |
|---|---|---|
| G-1 | `RefreshArbiter` / `IRefreshSink` 已建未接 | 由 §6 单门化 + §4 `DeliveryResult` 一并激活 |
| G-2 | 会话页 refresh 回投未转调 | 由 §6 `ExecuteAsync` 收口 |
| G-6 | `MarkDirty` 零调用方 | 由 §5 三车道定义真实消费方（Change/Manual） |
| G-16 | 验证欠账（INV-4/5/7） | 由 §9 R0 基线 + §10 INV-R 扩展 |

新增缺口编号（延续既有编号）：

| 新# | 缺口 |
|---|---|
| G-17 | `IRefreshRenderer` 无产物语义（`object`） |
| G-18 | `IRefreshSink` / `IRefreshStore` 无「未变化/无进展」反馈 |
| G-19 | `hasInFlight` 为 bool，无租约/年龄/进度 |
| G-20 | 页面 Pull 与宿主 Push 双入口，Pull 绕过仲裁 |
| G-21 | 信号簿记与可执行任务共用状态空间 |
| G-22 | 脚本生命周期无框架级总预算 |
| G-23 | 在途停滞无 `StallPolicy`（超时/无进展后的处置） |

---

## §3 目标架构：刷新即闭环控制环

### 3.1 原则

| # | 原则 | 说明 |
|---|---|---|
| P1 | **闭环反馈** | 一次刷新必须产出 `Outcome + Fingerprint + ProgressToken`；框架据反馈决定下一次何时刷 |
| P2 | **在途 = 租约** | 「在途」由 `InFlightLease` 表达（身份、开始时间、心跳、截止时间、进度令牌），不是任务行状态 |
| P3 | **单门执行** | 宿主 Push / 页面 Pull / 手动刷新 / 兜底轮询统一经 `ExecuteAsync`；`RenderAsync` 不再作为外部入口 |
| P4 | **触发分车道** | Change / Heartbeat / Fallback 三条车道，各自有优先级、预算、退避与熔断 |
| P5 | **语义分层** | 可执行任务、信号簿记、休眠成员在状态事件中带 `Kind + Active`，禁止混用 |
| P6 | **生命周期归框架** | 脚本总预算、命令心跳、取消传播由框架持有；宿主关窗只是外部终止之一，不是唯一兜底 |
| P7 | **兼容迁移** | 旧契约保留适配器；每批可回滚；框架内零领域词、零 Plate 类型 |

### 3.2 总体结构

```text
ChangeSource ─────┐
HeartbeatSource ──┤
FallbackSource ───┤   RefreshScheduler V2
Manual/PagePull ──┘   per-slot state machine
                              │ RefreshCycle
                              ▼
                    IRefreshRenderer V2
                    → RefreshArtifact
                      { Payload, Outcome, Fingerprint,
                        ProgressToken, SuggestedDelay }
                              │
                              ▼
                    IRefreshStore V2
                    Decide / Commit / RecordOutcome
                              │
                              ▼
                    IRefreshSink V2
                    → DeliveryResult
                      { Delivered, ContentChanged, Error }
```

### 3.3 每 slot 状态机

```text
Idle
  │ Dirty(Change/Manual)
  ▼
Dirty
  │ due
  ▼
Rendering ── Unchanged/Skipped ──▶ NoProgress (backoff)
  │ Changed
  ▼
Delivering ── NotDelivered ──▶ Retry (backoff)
  │ Delivered(ContentChanged)
  ▼
Idle

NoProgress ≥ N 或 Lease 超 Deadline
  → Stalled（一次诊断 + StallPolicy：cancel/fail/notify）
Change 触发
  → 重置 backoff，立即回 Dirty
```

---

## §4 契约改造（非破坏优先）

> 目标：先新增语义，旧接口保留适配；避免一次性改穿 `Strategy.Refresh` 的公开面。
> 所有新类型落在 `Strategy/Refresh/Model` 或 `Ports`，**不得**引用 `Plate.*` / `UiBackend.*` / Agent 领域类型。

### 4.1 `RefreshOutcome` / `RefreshArtifact`

```csharp
namespace Strategy.Refresh.Model;

public enum RefreshOutcome
{
    Changed,     // 内容变化，应交付
    Unchanged,   // 内容未变，合法 no-op
    Skipped,     // 条件不满足，不交付
    Failed,      // 渲染失败
    Blocked      // 依赖未就绪
}

public sealed record RefreshArtifact(
    object? Payload,
    RefreshOutcome Outcome,
    string Fingerprint,
    string? ProgressToken = null,
    TimeSpan? SuggestedDelay = null);
```

约束：

1. `Fingerprint` 必填；空串视为 `Failed`（防止域忘记给指纹导致去重失效）。
2. `Payload` 仅在 `Outcome=Changed` 时参与交付；其余情况允许为 `null`。
3. `ProgressToken` 是「有进展」的结构事实；建议由域给出单调值（round / seq / elapsed / rowVersion）。
4. `SuggestedDelay` 只作为建议，最终由调度器策略裁决，域不得直接控制节拍。

### 4.2 `DeliveryResult`

```csharp
namespace Strategy.Refresh.Model;

public sealed record DeliveryResult(
    bool Delivered,
    bool ContentChanged,
    string? Fingerprint,
    Exception? Error = null);
```

约束：

1. `Delivered=false` ⇒ 调度器按 at-least-once 置回重试。
2. `Delivered=true && ContentChanged=false` ⇒ 记 `NoChange`，**不得**推进「有进展」水位。
3. `Delivered=true && ContentChanged=true` ⇒ 推进 baseline + 进度水位。
4. 旧 `IRefreshSink` 由适配器包装为 `DeliveryResult`：`true ⇒ Delivered=true, ContentChanged=true`，保持旧行为。

### 4.3 `InFlightLease` / `IInFlightSource`

```csharp
namespace Strategy.Refresh.Model;

public sealed record InFlightLease(
    string Key,
    string Kind,                 // Executable / Signal / Bookkeeping（域侧词表经适配器注入）
    string LeaseId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastHeartbeatAt,
    DateTimeOffset? Deadline,
    string? ProgressToken);

namespace Strategy.Refresh.Ports;

public interface IInFlightSource
{
    ValueTask<IReadOnlyList<InFlightLease>> SnapshotAsync(CancellationToken ct = default);
}
```

约束：

1. 框架只消费 `Kind == Executable`（大小写由适配器归一）的租约；`Signal/Bookkeeping` 永不参与心跳。
2. `Deadline` 为空 ⇒ 使用框架默认租约上限（例如 30 分钟），并在诊断中标记 `deadline-defaulted`。
3. `LastHeartbeatAt` 由域在执行推进时刷新；超过 `StallWindow` 未刷新 ⇒ `Stalled`。
4. 无租约 ⇒ 心跳车道关闭；只保留 Change 与 30s Fallback。
5. 框架内**不得**出现 Agent 领域词；`Kind` 是字符串，具体词表由域适配器注入。

### 4.4 `RefreshTrigger` / `RefreshCycle`

```csharp
namespace Strategy.Refresh.Model;

public enum RefreshTrigger
{
    Change,       // 事件驱动
    Heartbeat,    // 在途租约心跳
    Fallback,     // 30s 兜底
    Manual,       // 用户显式刷新
    PagePull      // 页面回投 refresh
}

public sealed record RefreshCycle(
    string SlotKey,
    long Version,
    RefreshTrigger Trigger,
    IReadOnlyList<string>? Scopes,
    string? TraceId,
    DateTimeOffset StartedAt);
```

### 4.5 `SlotRefreshState`（每 slot 反馈状态）

```csharp
namespace Strategy.Refresh.Model;

public sealed record SlotRefreshState(
    long LastDeliveredVersion,
    string LastFingerprint,
    DateTimeOffset LastChangedAt,
    DateTimeOffset LastAttemptAt,
    int ConsecutiveNoChange,
    int ConsecutiveDeliveryFailures,
    RefreshOutcome LastOutcome,
    TimeSpan NextDelay,
    string? LastProgressToken,
    DateTimeOffset? NoProgressSince);
```

`IRefreshStore` 增加（默认接口方法或 V2 接口，避免破坏既有实现）：

```csharp
SlotRefreshState GetState(string slotKey);
void RecordOutcome(string slotKey, RefreshOutcome outcome, string fingerprint,
                   string? progressToken, DateTimeOffset at);
```

### 4.6 渲染器兼容

新增：

```csharp
public interface IRefreshArtifactRenderer : IRefreshRenderer
{
    Task<RefreshArtifact> RenderArtifactAsync(
        RefreshRequest request, CancellationToken ct = default);
}
```

旧渲染器经适配器接入：

```csharp
public sealed class LegacyRendererAdapter : IRefreshArtifactRenderer
{
    // 旧 RenderAsync 的返回值：
    //   null / 空串 / 无内容 ⇒ Unchanged（Fingerprint 由框架计算）
    //   非空               ⇒ Changed
    // 异常                ⇒ Failed（由调用方捕获）
}
```

迁移期 `RefreshRegistry` 优先取 `IRefreshArtifactRenderer`，否则用适配器包装旧渲染器；禁止两条渲染实现并行维护。

---

## §5 调度器 V2：三车道 + 每 slot 状态机

### 5.1 触发车道

| 车道 | 触发 | 频率 | 优先级 | 计入进度 | 失败/无变化处置 |
|---|---|---|---|---|---|
| `Change` | `IRefreshSource.Changed` / `MarkDirty` / `Manual` | 立即，合并 1 拍 | High | 是 | 置回重试；连续失败退避 |
| `Heartbeat` | `IInFlightSource` 有 Executable 租约 | 基础 1s，自适应退避到 30s | Normal | 仅 `ProgressToken` 变化时 | 无变化累计；N 次后 Stalled |
| `Fallback` | 无 Change/Heartbeat 时的兜底 | 30s | Low | 否 | 不改变进度水位 |

规则：

1. High 不得被 Low 抢占；同拍内 High 先出。
2. Low 只在无 High/Heartbeat 时出。
3. 任何 Change/Manual 触发都重置该 slot 的 `NextDelay` 与 `ConsecutiveNoChange`。
4. `MarkDirty` 必须成为真实入口：任务池事件、手动刷新、页面回投都可调用；不再允许零调用方。

### 5.2 心跳策略（伪代码）

```text
leases = InFlightSource.Snapshot()
        .Where(l => l.Kind == Executable && l.Deadline > now)

if (leases.Count == 0)
{
    heartbeatEnabled = false;
    nextDue = now + FallbackPollMs;
}
else
{
    heartbeatEnabled = true;
    var noProgress = State.ConsecutiveNoChange;
    var interval = BaseTickMs * (1 << Min(noProgress, 5)); // 1s,2s,4s,8s,16s,32s
    interval = Min(interval, MaxHeartbeatMs);             // 默认 30s
    nextDue = now + interval;

    if (noProgress >= StallAfter)
        Emit("RefreshStalled", slot, leases, State.NoProgressSince);

    if (AnyLeasePastDeadline(leases) || noProgress >= StallAfter)
        StallPolicy.OnStalled(slot, leases, State);
}
```

### 5.3 `StallPolicy`

```csharp
public interface IStallPolicy
{
    Task OnStalledAsync(
        string slotKey,
        IReadOnlyList<InFlightLease> leases,
        SlotRefreshState state,
        CancellationToken ct = default);
}
```

默认实现只做三件事（不改变业务）：

1. 打一次 Warn：`RefreshStalled(slot, leaseCount, lastProgressAt, noProgress)`；
2. 停止该 slot 的心跳车道，直到下一次 Change 或租约结束；
3. 可选：调用域注入的 `IStalledLeaseHandler`（取消轮次 / 标记失败 / 通知 UI）。

### 5.4 单飞与 latest-wins

保留现有 `RefreshArbiter` 语义，但纳入 V2：

- 每 slot 同时最多一个 `Rendering/Delivering` Cycle；
- 在飞期间到达的更新按版本合并，旧的不得覆盖新的；
- 只有 `Delivered && ContentChanged` 才推进 baseline；
- `Unchanged` 记录 `LastOutcome` 与 `ConsecutiveNoChange`，不推进 baseline。

### 5.5 诊断快照

每 slot 暴露：

```csharp
public sealed record RefreshSlotDiagnostics(
    string SlotKey,
    RefreshTrigger LastTrigger,
    RefreshOutcome LastOutcome,
    long LastVersion,
    string? LastProgressToken,
    int ConsecutiveNoChange,
    int ConsecutiveDeliveryFailures,
    TimeSpan CurrentDelay,
    int ActiveLeaseCount,
    DateTimeOffset? LastChangedAt,
    DateTimeOffset? NoProgressSince,
    long Pushes,
    long UnchangedDeliveries,
    long SuppressedHeartbeats);
```

日志只在状态迁移时打，不在每拍打；避免诊断本身变成刷屏源。

---

## §6 单门化：Pull / Push / Manual 同门

### 6.1 唯一执行入口

```csharp
public interface IRefreshService
{
    Task<RefreshCycleResult> ExecuteAsync(
        RefreshRequest request,
        RefreshTrigger trigger,
        CancellationToken ct = default);

    void MarkDirty(string? scopeKey,
                   RefreshPriority priority = RefreshPriority.High);
}
```

内部顺序固定：

```text
ExecuteAsync
  → RenderArtifactAsync
  → Decide(version, fingerprint)
  → 若 Changed：DeliverAsync
  → 若 Delivered && ContentChanged：Commit
  → 否则：RecordOutcome(Unchanged/Skipped/Failed)
  → 更新 SlotRefreshState
```

### 6.2 页面回投改走 `ExecuteAsync`

现状 `AgentUiLibraryConversation.HandleRefreshAsync`：

```csharp
var viaFramework = await AgentUiRefreshPublisher.TryRenderViaFrameworkAsync(
    new RefreshRequest(...));
return viaFramework ?? await ConversationRefreshRenderer.RenderCoreAsync(...);
```

目标：

```csharp
return await AgentUiRefreshPublisher.ExecuteAsync(
    new RefreshRequest(
        ConversationRefreshRenderer.ConversationSlotKey,
        Scopes: msg.Scopes,
        ContextJson: payload.RawJson,
        TraceId: msg.TraceId,
        SelectionKeys: msg.SelectionKeys,
        OpenKeys: msg.OpenKeys),
    RefreshTrigger.PagePull);
```

验收：

- 旧路径保留一个发布周期，对拍 diff=0；
- `ConversationView_RefreshTrace` 字段不变；
- `agent_html_view_switch_test` / `agent_db_batch_ops_test` 回归绿；
- 空产物从「Info 跳过」变成框架可观测的 `Unchanged`，不再触发心跳。

### 6.3 宿主 Push 也走同一入口

`PushOnceAsync` 只负责把 `cmd=update` 发到页面；页面回投 `cmd=refresh` 后仍经 `ExecuteAsync(PagePull)`。
即：

```text
Host Push：Scheduler → PushOnce → 页面
页面 Pull：页面 → ExecuteAsync(PagePull) → Render/Commit/Sink
```

**Push 不再直接渲染**；Pull 不再绕过仲裁。两条链路共享同一 `SlotRefreshState` 与 `Fingerprint`。

### 6.4 手动刷新

页面「⟳」仍可先 `MarkDirty(scope, High)` 再立即 `ExecuteAsync(Manual)`；二者都走同一入口，保证：

- 手动刷新不受 1s 节流；
- 手动刷新会重置 backoff；
- 手动刷新结果进入同一进度统计。

### 6.5 `RenderAsync` / `CommitAsync` 降级

- `RenderAsync` 标记为 `internal` 或 `[Obsolete]`，仅测试/内部使用；
- `CommitAsync` 由 `ExecuteAsync` 内部调用，不再对外暴露为第二入口；
- 旧调用方通过适配器迁移，禁止长期双门并存。

---

## §7 在途租约与任务/信号语义分离

### 7.1 状态事件扩展

`Share/Orchestration/TaskStatusPool/TaskPool/TaskPoolStateChangedEventArgs.cs`：

```csharp
public enum TaskPoolItemKind
{
    Executable,
    Signal,
    Bookkeeping
}

public sealed class TaskPoolStateChangedEventArgs : EventArgs
{
    public string TaskKey { get; }
    public TaskExecutionStatus FromState { get; }
    public TaskExecutionStatus ToState { get; }
    public string? Reason { get; }
    public DateTime Timestamp { get; }

    // 新增（源可空，旧实现默认 Executable/true，保持兼容）
    public TaskPoolItemKind Kind { get; init; } = TaskPoolItemKind.Executable;
    public bool Active { get; init; } = true;
    public string? LeaseId { get; init; }
}
```

### 7.2 镜像条目升级

`TaskPoolStateMirror` 从 `TaskKey → string` 升级为：

```csharp
public readonly record struct TaskPoolEntryState(
    TaskExecutionStatus Status,
    bool Active,
    TaskPoolItemKind Kind,
    string? LeaseId);

public IReadOnlyList<KeyValuePair<string, TaskPoolEntryState>> Entries();
```

兼容策略：

- 保留旧 `TryGet(string)` 返回 `Status.ToString()`；
- 新增 `TryGetEntry(string)`；
- 所有 in-flight 判定只读 `Kind == Executable && Active == true`。

### 7.3 领域适配：租约源只认执行租约

`Agent.Ui` 新增 `AgentInFlightLeaseSource : IInFlightSource`：

- 数据源 = 治理运行期在途租约（`GovernanceRuntime.Current.Governor.Registry.InFlight`）；
- 映射 `TaskKey → Key`、`LeaseId → LeaseId`、`StartedAt/LastHeartbeatAt/Deadline`；
- `Kind` 固定 `Executable`；
- **不再**扫描 `TaskPoolStateMirror` 的 `NotStarted` 行；
- 未装配治理运行期 ⇒ 返回空租约（心跳关闭），并记录一次 `LeaseSourceUnattached` 诊断。

### 7.4 信号暂存隔离

过渡（推荐先做）：

- `SignalAccumulator.EnqueueAsync` 写入时设置 `Kind = Signal`；
- `TaskPoolStateChangedEventArgs` 携带 `Kind`；
- 镜像/租约源过滤 `Signal` 与 `Bookkeeping`；
- `NotStarted + Active=false` 不再进入任何心跳判定。

终态（R7）：

```csharp
public interface ISignalBacklogStore
{
    Task<string> EnqueueAsync(SignalEnvelope envelope, CancellationToken ct);
    Task<IReadOnlyList<PendingSignal>> ListPendingAsync(
        string scope, string coalesceKey, CancellationToken ct);
    Task<bool> DeleteAsync(string key, CancellationToken ct);
}
```

- 新表 `ur_signals`（或独立 SQLite 表）承载暂存；
- 双读/双写一个版本周期，确认 pending 不丢后切主；
- `ur_tasks` 不再出现 `ExecutorId=signal_flush` 的簿记行。

### 7.5 验收

- `NotStarted + Active=false` 的休眠任务/信号条目不产生任何 1s 心跳；
- `Running` 任务产生 1s 心跳，且 `ProgressToken` 变化时重置退避；
- 删除任务后镜像同步移除（现有 `RemoveMirror` 路径保留）；
- 信号 flush 行为与当前等价（数量、顺序、5s 窗口不变）。

---

## §8 脚本生命周期：总预算、命令心跳、取消传播

### 8.1 现状缺口

| 事实 | 落点 |
|---|---|
| 管线级超时已移除，传 `CancellationToken.None` | `FileDrivenPipelineRunner.ExecuteAsync` |
| 只有逐命令 `timeout`；命令 40 为 300 秒 | 脚本第 51 行 `timeout:300000` |
| GUI 宿主关窗是唯一提前终止 | `ScriptExecutionPort.CompleteFromFormClosed` |
| 关窗结果归 `HostWindowClosed / ExternalTermination` | 同上 |

### 8.2 目标契约

```csharp
namespace FileDriven.Execution.Lifecycle;

public sealed record ScriptExecutionBudget(
    TimeSpan Total,
    TimeSpan? PerCommand = null,
    TimeSpan? Idle = null);

public sealed class ScriptRunContext
{
    public CancellationToken Token { get; }
    public DateTimeOffset Deadline { get; }
    public void ReportProgress(string commandId);
}
```

### 8.3 执行改造

1. `ScriptExecutionPort` 创建 `CancellationTokenSource(budget.Total)`；
2. 将 token 传入 `FileDrivenPipelineRunner.ExecuteAsync`，再由管线传入命令执行器；
3. `Task.WhenAny(pipelineTask, Task.Delay(budget.Total, token))`：
   - 管线先完成 ⇒ 正常结果；
   - 预算先到 ⇒ `cts.Cancel()`，终止子进程 / 取消轮次，返回 `FailureKind.CommandTimeout`；
4. 命令协调器在每条命令开始/结束时 `ReportProgress`；超过 `Idle` 无进度 ⇒ `IdleHung`；
5. 宿主关窗仍保留为 `ExternalTermination`，但不再是唯一提前终止。

### 8.4 验收

- 命令 40 超过总预算 ⇒ `[command_timeout]`，`completed=true`（有明确终态），而不是 `completed=false`；
- 正常脚本不被总预算误杀（总预算 > 脚本实测上限）；
- 宿主关窗仍能取消后台命令，不产生孤儿进程；
- `ScriptExecutionPort_GuiModeComplete` 的 `elapsedMs` 与 `TerminationKind` 可复算。

---

## §9 分批实施 R0–R9

> 每批独立提交、独立回滚；R3/R4 风险最高，必须先影子对拍。
> 既有 G-1/G-2/G-6/G-16 作为本单的前置或同批项，不重复登记。

### R0 基线存证

| 项 | 内容 |
|---|---|
| 动作 | 跑现有刷新/写回/脚本回归；记录每 slot 推送次数、DOM 写入数、脚本耗时、空产物次数 |
| 命令 | §11 的 ①–⑦ |
| 验收 | 基线报告齐备（至少包含 `agent_html_view_switch_test`、`agent_db_batch_ops_test`、`agent_history_cline_read_file_local_test`） |
| 回滚 | 不适用 |

### R1 契约增强（非破坏）

| 项 | 内容 |
|---|---|
| 文件 | `Strategy/Refresh/Model/RefreshArtifact.cs`、`DeliveryResult.cs`、`InFlightLease.cs`、`RefreshCycle.cs`、`SlotRefreshState.cs`；`Ports/IRefreshArtifactRenderer.cs`、`Ports/IInFlightSource.cs` |
| 动作 | 新增类型与适配器；旧 `IRefreshRenderer` / `IRefreshSink` 保持可用 |
| 验收 | 编译 0 错；旧路径行为不变；框架边界守卫通过 |
| 回滚 | 新类型仅新增，删除即回退 |

### R2 Store/State V2

| 项 | 内容 |
|---|---|
| 文件 | `Core/InMemoryRefreshStore.cs`、`Ports/IRefreshStore.cs`（默认接口或 V2） |
| 动作 | 增加 `SlotRefreshState`、`RecordOutcome`、`ConsecutiveNoChange`、`NextDelay` |
| 验收 | 单测覆盖 `Changed/Unchanged/Skipped/Failed`、连续计数、Commit 条件 |
| 回滚 | 保留旧 `Decide/Commit` 实现 |

### R3 Scheduler V2（影子模式）

| 项 | 内容 |
|---|---|
| 文件 | `Core/RefreshScheduler.cs`、`Core/RefreshService.cs` |
| 动作 | 三车道 + 每 slot 状态机 + 心跳退避/StallPolicy；feature flag 双跑 |
| 验收 | 影子模式决策与旧调度器一致（除已定义的无进展退避）；无重复推送；单测覆盖 1s→30s 退避 |
| 回滚 | 关闭 feature flag，回旧调度器 |

### R4 单门化

| 项 | 内容 |
|---|---|
| 文件 | `Core/RefreshService.cs`、`Agent.Ui/Core/Functions/Conversation/AgentUiLibraryConversation.cs`、`Agent.Ui/Ui/Hosting/AgentUiRefreshPublisher.cs` |
| 动作 | `HandleRefreshAsync` 改走 `ExecuteAsync(PagePull)`；`RenderAsync` 降级；`MarkDirty` 接真实入口 |
| 验收 | 产物 diff=0；`ConversationView_RefreshTrace` 不变；脚本回归绿；空产物可观测为 `Unchanged` |
| 回滚 | 恢复旧直连调用行；`ExecuteAsync` 保留但不用 |

### R5 在途租约源

| 项 | 内容 |
|---|---|
| 文件 | `Agent.Ui/Core/Host/AgentInFlightLeaseSource.cs`（新增）、`Agent.Ui/Ui/Hosting/AgentUiRefreshPublisher.cs`、`Core/RefreshScheduler.cs` |
| 动作 | 用 `IInFlightSource` 替换 `Func<bool> IsInFlight`；接入治理租约；无租约关闭心跳 |
| 验收 | 休眠任务 0 心跳；`Running` 任务 1s；`ProgressToken` 变化重置退避；无租约时仅 30s Fallback |
| 回滚 | 保留 `TaskPoolInFlightAdapter`（旧 bool 适配）作为 feature flag 回退 |

### R6 任务/信号语义分离（过渡）

| 项 | 内容 |
|---|---|
| 文件 | `Share/Orchestration/TaskStatusPool/TaskPool/TaskPoolStateChangedEventArgs.cs`、`TaskPoolStateMirror.cs`、`Agent.Core/Engine/Signal/SignalAccumulator.cs` |
| 动作 | 增加 `TaskPoolItemKind/Active/LeaseId`；镜像条目升级；信号写入标记 `Kind=Signal` |
| 验收 | `Signal/Bookkeeping` 不进入心跳；旧 `TryGet` 兼容；删除任务镜像清理 |
| 回滚 | 新字段默认 `Executable/true`；旧消费方行为不变 |

### R7 信号暂存独立存储（终态）

| 项 | 内容 |
|---|---|
| 文件 | 新增 `ISignalBacklogStore` + SQLite 实现；`SignalAccumulator` / `SignalIntake` 改依赖 |
| 动作 | 新表承载 pending；双读/双写一个版本；确认后切主 |
| 验收 | pending 不丢；flush 数量/顺序/5s 窗口与当前等价；`ur_tasks` 无信号簿记行 |
| 回滚 | 双写保留，切回 `TaskPoolPersistence` |

### R8 脚本总预算

| 项 | 内容 |
|---|---|
| 文件 | `FileDriven.Execution/Ports/ScriptExecutionPort.cs`、`FileDriven.Execution/Pipeline/FileDrivenPipelineRunner.cs`、`FileDriven.Execution/Lifecycle/ScriptExecutionBudget.cs`（新增） |
| 动作 | 总预算 + 命令心跳 + 取消传播 + 类型化超时 |
| 验收 | 超预算返回 `CommandTimeout` 且 `completed=true`；正常脚本不误杀；关窗可取消 |
| 回滚 | 预算参数可空；为空时回当前行为 |

### R9 清理与文档

| 项 | 内容 |
|---|---|
| 动作 | 删除旧 `Func<bool> IsInFlight` 路径；更新 README/施工单状态；登记新门禁；补 INV-R 测试 |
| 验收 | 无旧入口调用；`Check-RefreshFrameworkBoundary` 绿；`RefreshFramework.Tests` 全绿；文档索引更新 |
| 回滚 | 旧入口已在 R4/R5 保留转调，删除前需一个发布周期 |

---

## §10 验收判据（INV-R）

| 编号 | 判据 | 落点 |
|---|---|---|
| **INV-R1** | 无有效 `InFlightLease` 时，除 30s Fallback 外零推送；不出现 1s 心跳 | `RefreshSchedulerTests` |
| **INV-R2** | 有 Executable 租约时 1s 心跳；连续 N 次无进展后 1s→2s→4s→8s→16s→30s 退避 | `RefreshSchedulerTests` |
| **INV-R3** | `Unchanged/Skipped` 不触 DOM，且被记为 `NoChange`，不是错误 | `RefreshServiceTests` + 域测试 |
| **INV-R4** | 页面 Pull 与宿主 Push 对同一状态产出相同 `Fingerprint/Outcome` | 影子对拍脚本 |
| **INV-R5** | `Kind=Signal/Bookkeeping` 或 `Active=false` 永不进入心跳 | `InFlightSourceTests` |
| **INV-R6** | 每 slot 单飞；在飞 latest-wins；旧版本不推进 baseline | `RefreshArbiterTests` |
| **INV-R7** | 租约超 `Deadline` 或连续 N 次无进展 ⇒ 一次 `RefreshStalled` + `StallPolicy` | `RefreshStallTests` |
| **INV-R8** | 脚本总预算到点 ⇒ 取消管线、终止子进程、返回 `CommandTimeout` | FileDriven 测试 |
| **INV-R9** | 框架内零领域词、零 Plate 类型；层边界与边界守卫绿 | `Check-RefreshFrameworkBoundary.ps1` |
| **INV-R10** | 旧脚本回归：`agent_history_cline_read_file_local_test` 不再出现 1s 风暴，且能到 `CLINE_READ_FILE_OK`（或按新预算明确失败） | 集成脚本 |

---

## §11 风险与取舍

| 风险 | 影响 | 缓解 |
|---|---|---|
| 公开契约变更 | 域侧编译失败 | 新增接口 + 适配器；旧接口保留一个发布周期 |
| 心跳退避导致进度显示变慢 | 用户感知 | 上限 30s；`ProgressToken` 变化立即重置；UI 可显示「低频刷新」状态 |
| 租约源与治理运行期耦合 | 框架污染 | `IInFlightSource` 通用端口；Agent 侧适配器注入；框架零领域词 |
| 信号存储迁移丢 pending | 业务信号丢失 | 双读/双写；对账 pending 数量；切主前跑 flush 等价测试 |
| 影子模式增加开销 | 性能 | 采样/仅测试环境双跑；生产只跑 V2 |
| 单门化增加 DB 命令路径开销 | DB 页变慢 | 保留 `RefreshPolicy.CommandDriven`；渲染仍单源，调度可选 |
| 总预算误杀长脚本 | 测试失败 | 预算可配置；默认值大于现有最长脚本；命令级 timeout 仍优先 |
| 旧路径长期双门 | 回归风险 | R9 强制清理；新增门禁检查 `RenderAsync` 外部调用数 |

---

## §12 核查命令（可复算）

```powershell
# ① 框架边界（正/负对照）
pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Agent\Check-RefreshFrameworkBoundary.ps1 -Check -NegativeControl

# ② 门禁聚合器
pwsh D:\Project\Main\Frame\Invoke\Invoke-Frame.ps1 -Action invoke-gatecheck -Only RefreshFrameworkBoundary

# ③ 刷新框架测试
dotnet test D:\Project\Main\Environment\CentralTest\CSharpTest\Share.Tsets\RefreshFramework.Tests\RefreshFramework.Tests.csproj

# ④ 层边界
pwsh D:\Project\Main\Frame\PowerShellScript\Share\Solution\Test-ProjectReferences.ps1 -Check

# ⑤ 运行时槽检查
pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Agent\Check-AgentRuntimeSlots.ps1 -Check

# ⑥ 旧入口 / 旧在途判定计数（目标：RenderAsync 外部调用=0；IsInFlight bool=0）
cd D:\Project\Main\Frame
git --no-pager grep -n -E 'TryRenderViaFrameworkAsync|Func<bool>.*hasInFlight|IsInFlight\(' -- ':!*BuildOutput*' ':!*.md'
cd D:\Project\Main\Projects
git --no-pager grep -n -E 'TryRenderViaFrameworkAsync|IsInFlight\(' -- ':!*BuildOutput*' ':!*.md'

# ⑦ 信号簿记计数（目标：SignalAccumulator 不再直接写 TaskExecutionStatus.NotStarted）
git --no-pager grep -n -E 'SignalAccumulator|signal_flush|TaskExecutionStatus\.NotStarted' -- ':!*BuildOutput*' ':!*.md'

# ⑧ 集成回归（R0/R10）
#    运行 agent_html_view_switch_test / agent_db_batch_ops_test / agent_history_cline_read_file_local_test
#    记录：每秒 refresh 次数、空产物次数、脚本耗时、TerminationKind
```

---

## §13 变更记录

| 日期 | 内容 |
|---|---|
| 2026-10-19 | 初版：基于 `agent_history_cline_read_file_local_test` 的 1 秒刷新风暴复盘，登记 G-17..G-23；提出闭环控制环、在途租约、三车道调度、单门化、任务/信号分离与脚本总预算；给出 R0–R9 分批、INV-R1..R10、回滚与核查命令。 |

---

## §14 与既有文档的关系

| 文档 | 关系 |
|---|---|
| `UI刷新通用框架-收拢分散刷新逻辑-重构施工单.md` | 前置：机制单源、契约常量、注册表/编排面 |
| `UI刷新通用框架-剩余未收拢逻辑-后续施工单.md` | 前置：G-1/G-2/G-6/G-16；本单将其吸收为 R4/R5 的前置 |
| `UI刷新通用框架-代码整理优化-框架级施工单.md` | 前置：受控态外置、单门化雏形、写落地端口 |
| `README.md` | 本单完成后需更新 §8「迁移现状」与 §5「红线」 |

> 本单不改变领域渲染文本、不改变 `AgentUi_*` 函数返回文案、不改变页面视觉与交互；
> 只改变「何时刷 / 是否刷 / 在途判定 / 无进展处置 / 脚本终止」的框架运行时模型。

