# Strategy.Refresh —— UI 刷新通用框架（闭环 + 在途租约）

> **定位**：把「何时刷 / 是否刷 / 怎么落地 / 是否有进展」收拢为**单一实现**的通用刷新框架。
> **单向依赖**：`Strategy`(L3) 不引用任何领域工程；消费方经烘焙二进制引用取类型面。
> **零领域**：框架内零 `agent` / `conversation` / `db` / `task` / `session` / `history` 等领域词；领域语义只经端口注入。

---

## 1. 目录结构

| 层 | 位置 | 关键类型 |
|---|---|---|
| L0 契约 | `Model/` | `RefreshRequest` / `RefreshArtifact` / `RefreshOutcome` / `DeliveryResult` / `InFlightLease` / `SlotRefreshState` / `RefreshTrigger` / `RefreshCycleResult` / `RefreshTicket` / `RefreshDelivery` / `RefreshContract` |
| 端口 | `Ports/` | `IRefreshSource` / `IRefreshRenderer` / `IRefreshSink` / `IRefreshStore` / `IInFlightSource` / `IStallPolicy` / `IFingerprint` / `IRefreshClock` |
| L1 调度 | `Core/RefreshScheduler.cs` | Change / Heartbeat / Fallback 三车道、单拍互斥、退避、停滞熔断、失败置回 |
| L2 仲裁 | `Core/RefreshArbiter.cs` | 每 slot 单飞、latest-wins、Stale 即丢、仅变化才 Commit |
| 状态源 | `Core/InMemoryRefreshStore.cs` | 版本水位 + 指纹 + 连续无进展 + 退避 + 最近结论 |
| 编排面 | `Core/RefreshService.cs` | `ExecuteAsync` 唯一入口、`Registry`、`MarkDirty`、`Attach/Detach` |
| 注册表 | `Core/RefreshRegistry.cs` | 源 / 渲染器登记与查找 |
| JSON | `Json/RefreshJson.cs` | 运行态 / 槽状态 / 契约常量投影 |

---

## 2. 唯一刷新契约

```csharp
public interface IRefreshRenderer
{
    string SlotKey { get; }
    Task<RefreshArtifact> RenderAsync(RefreshRequest request, CancellationToken ct = default);
}

public sealed record RefreshArtifact(
    object? Payload,
    RefreshOutcome Outcome,      // Changed / Unchanged / Skipped / Failed / Blocked
    string Fingerprint,          // 空串 ⇒ Failed
    string? ProgressToken = null,
    TimeSpan? SuggestedDelay = null);

public sealed record DeliveryResult(
    bool Delivered,
    bool ContentChanged,
    string? Fingerprint,
    Exception? Error = null);

public sealed record InFlightLease(
    string Key, string Kind, string LeaseId,
    DateTimeOffset StartedAt, DateTimeOffset LastHeartbeatAt,
    DateTimeOffset? Deadline, string? ProgressToken,
    bool Active = true);
```

- 框架只消费 `Kind=Executable ∧ Active ∧ Deadline 未过` 的租约。
- `Signal` / `Bookkeeping` / `Active=false` 永不唤醒心跳。
- 只有 `Delivered && ContentChanged` 才推进 baseline 与进度水位。

---

## 3. 刷新闭环

```text
Change 车道：源事件 / MarkDirty → 合帧 → 下一拍推送（高优先）
Heartbeat 车道：可执行租约 → 按 NextDelay 到期推送
Fallback 车道：无 Change / Heartbeat 时 30s 低优先兜底

页面 Pull / 宿主 Push / 手动刷新
  → RefreshService.ExecuteAsync
  → Renderer.RenderAsync（RefreshArtifact）
  → IRefreshStore.Decide（Stale / Skip / Apply）
  → RefreshArbiter（单飞 + latest-wins）
  → IRefreshSink.DeliverAsync（DeliveryResult）
  → Commit / RecordOutcome（NoChange / Failed / Progress）
```

- 无进展退避：`1s → 2s → 4s → 8s → 16s → 30s`。
- 进度令牌变化 / Change / Manual ⇒ 立即重置退避。
- 超 Deadline、心跳窗口过期或连续无进展达阈值 ⇒ 每槽一次 `RefreshStalled` + `IStallPolicy`。
- 空产物 = `Unchanged`；空指纹 = `Failed`；二者都不是错误日志刷屏。

---

## 4. 接入方式

| 场景 | 做法 |
|---|---|
| 新增刷新面 | 实现 `IRefreshRenderer` → `RefreshService.RegisterRenderer` |
| 新增变化源 | 实现 `IRefreshSource` + `RefreshDescriptor` → `RegisterSource` |
| 在途驱动心跳 | 实现 `IInFlightSource`，返回可执行租约 |
| 写落地 | 实现 `IRefreshSink`，返回 `DeliveryResult` |
| 停滞处置 | 实现 `IStallPolicy`（取消轮次 / 通知页面等） |
| 页面回投 | `RefreshService.ExecuteAsync(request, RefreshTrigger.PagePull)` |

---

## 5. 契约常量（唯一声明处）

| 常量 | 值 | 用途 |
|---|---|---|
| `RefreshContract.Command` | `refresh` | 宿主推送 / 页面回投 |
| `RefreshContract.TickMs` | `1000` | 调度节拍 |
| `RefreshContract.ThrottleMs` | `1000` | 页面回投节流 |
| `RefreshContract.FallbackPollMs` | `30000` | 兜底轮询 |
| `RefreshContract.MaxHeartbeatMs` | `30000` | 心跳退避上限 |
| `RefreshContract.StallAfterNoChange` | `6` | 无进展熔断阈值 |
| `RefreshContract.StallWindowMs` | `30000` | 租约心跳窗口 |

---

## 6. 红线

1. 框架内不得出现领域字符串（`agent` / `conversation` / `dbRefresh` / `task-pool` / `session` / `history`）。
2. 节拍 / 合并 / 去重 / 退避 / 熔断 / 仲裁只有一份实现。
3. 契约常量只有一处声明（`RefreshContract`）。
4. 框架内零 HTML / 零 Plate 类型。
5. 禁止 bool 在途判定；禁止不透明 `object` 渲染产物。
6. 空闲零推送；未送达必置回重试；未变化不推进 baseline。

守卫：`Frame\PowerShellScript\Operation\DSL\Agent\Check-RefreshFrameworkBoundary.ps1`（正 / 负对照）。

---

## 7. 当前接入现状（2026-10-19）

| 项 | 状态 |
|---|---|
| 框架闭环（产物 / 租约 / 三车道 / 退避 / 熔断 / DeliveryResult） | ✅ 已实施；旧 bool / object / 旁路入口已删除 |
| 会话页渲染器 | ✅ 产出 `RefreshArtifact`（空产物 Unchanged；非空 Changed + 稳定指纹 + 进度令牌） |
| DB 页渲染器 | ✅ 产出 `RefreshArtifact`（整库 HTML 指纹） |
| 宿主发布器 | ✅ `ExecuteAsync` 唯一门；`AgentInFlightLeaseSource` 接治理租约 |
| 写落地 | ✅ `AgentUiRefreshSink` 返回 `DeliveryResult`；Skipped/Nothing = 已送达未变化 |
| 任务池语义 | ✅ `TaskPoolItemKind` + 事件 `Kind/Active/LeaseId` + 镜像条目快照；信号簿记不驱动刷新 |
| 脚本总预算 | ✅ `ScriptExecutionBudget`（默认 600s，环境变量可调）；到点取消管线并返回 `CommandTimeout` |
| 框架测试 | ✅ `RefreshFramework.Tests` 29/29 |

---

## 8. 验证命令

```powershell
dotnet build D:\Project\Main\Frame\CsharpPrj\Strategy\Refresh\Refresh.csproj
dotnet test  D:\Project\Main\Environment\CentralTest\CSharpTest\Share.Tsets\RefreshFramework.Tests\RefreshFramework.Tests.csproj
pwsh D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Agent\Check-RefreshFrameworkBoundary.ps1 -Check -NegativeControl
pwsh D:\Project\Main\Frame\Invoke\Invoke-Frame.ps1 -Action invoke-gatecheck -Only RefreshFrameworkBoundary
pwsh D:\Project\Main\Frame\PowerShellScript\Share\Solution\Test-ProjectReferences.ps1 -Check
```
