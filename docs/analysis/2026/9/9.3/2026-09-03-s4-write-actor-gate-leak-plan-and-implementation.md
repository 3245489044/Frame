# S4 实施规划：SqliteWriteChannel 单写 actor 事务门遗留治理（防永久等待 + 异常路径 TxOpen 兜底释放）

> 日期：2026-09-03（晚）
> 基线：`docs/analysis/2026/9/9.3/2026-09-03-s3-single-writer-convergence-and-log-compaction.md`（S3 接线）
> 性质：S4 阶段实施规划。基于 S3 接线后 GUI 复跑证据，规划「运行期新卡死（轮后 next / UI 单条写永久等待）」的治理方案。
> 前置：S3 已把 agent.db 写收敛到单写 actor（`SingleWriterActor`，Modules.Threading）并消除旧「5s 借出超时死信」。

---

## 一、复跑证据结论（S3 接线之后）

### 1.1 已根治（对比 S3 前）
| 项 | S3 前 | 本次（S3 接线后） |
|---|---|---|
| agent.db 写失败形态 | `TimeoutException 借出等待超时 5s` 循环死信、Outbox queue 积压 | **无任何借出超时死信** |
| `OutboxWriter_flush_slow` | 每次 5s 高水位 | **不出现** |
| Outbox queue | 3→9 积压 | 不积压 |
| ur_tasks / 节点写 | 5s 空转失败 | 正常（id=103 Running、落库） |
| 读操作（DbRead/GetStatus） | 受写锁拖累 | 正常返回 |

→ SQLite 单写并发问题在运行态已根除（S3 目标达成）。

### 1.2 新卡死（本次引入、需治理）
| 时间点 | 现象 |
|---|---|
| 23:33:42 | round1 骨架完成 `decision=stop(Success)`；`AgentRunner 轮后 next 开始 round=1` |
| 23:33:59 起 | **命令 #54 `AgentUi_RunJson`（任务删除/回写）停滞**：15s「疑似慢」→30s「运行期卡死」 |
| 23:35:12 | **`轮后 next 卡超`**：round1 90s 后 `AgentRunner 轮后 next 卡超 → 记 TO` |
| 证据 | `threadpool workerBusy=8/32767`、主循环 TO、多个 UI 单条写一起卡 |

**关键判别**：卡住的不是 SQLite 读（读仍正常），而是**走单条写（`RunWrite`）的路径**（轮后 ur_tasks 回写、UI 删除/心跳）。形态与 S3 前完全不同（不再是 5s 超时循环，而是**无限等待**）。

---

## 二、根因假设（待复跑/插桩确认）

S3 把单条写改为「`WaitTxIdle` 等在途事务结束」（无界 `Monitor.Wait`），把事务打开标记 `TxOpen=true`（`SqliteTransaction` 构造即 `MarkTxOpen`）。**若某个 `SqliteTransaction` 因异常/取消中途未走到 Commit/Rollback/Dispose → `MarkTxIdle` 永不触发 → `TxOpen` 永久为 true → 此后所有单条写（ur_tasks 回写、UI 删除/心跳）在 `WaitTxIdle` 上无限等待**。

候选根因（按可能性）：
- **候选 A（主）**：某个持久化事务在「构造已 `MarkTxOpen`」但「Commit/Rollback/Dispose 未执行」的异常/取消路径上遗留 `TxOpen=true`。`SqliteTransaction` 构造即置位（`NodeExecutionService` 经 `_beginTransaction` 工厂同步建）；若其后的 `await using(tx)` 因取消/异常未正常收尾（或 Outbox 每项 5s 取消正中中途），门不释放。
- **候选 B**：Outbox 后台持久化事务与主线程轮后 next 的单条写在同一单写 actor 上形成占用未及时释放（语义同 A，聚焦「谁开的门没关」）。
- **候选 C（已排除）**：旧 5s 门超时——S3 已删除门与超时，非本次形态。

**判定点（代码事实）**
- `SqliteWriteChannel.WaitTxIdle`：`while(entry.TxOpen) Monitor.Wait(entry.Sync)` —— **无界**，任一遗留即永久挂。
- `MarkTxOpen`/`MarkTxIdle` 仅由 `SqliteTransaction` 构造/`Finish`（Commit/Rollback/DisposeAsync）翻转，无任何兜底/owner 自证。
- `NodeExecutionService.PersistNodeOutputsAndKeyMappingsAsync`（L269-286）：`await using(tx){ ...; await tx.CommitAsync(); }`——正常与取消都依赖 using 的 Dispose 释放；但若 `_beginTransaction` 后进入 using 前的任意 await 抛取消/异常、或事务块内某步抛而 Dispose 未被调用，门即遗留（待插桩确认是否真发生）。

---

## 三、目标

1. **任何路径（正常 / 异常 / 取消）都不遗留 `TxOpen`** → 后续单条写绝无永久挂。
2. 即便极端仍卡，也**有界 + owner/借出点自证**转上层归类（TO / execution_failure），不静默挂进程。
3. 不改变 S3 的写模型（保留单写 actor、杜绝 5s 死信）。

---

## 四、方案设计

### A. 兜底释放（治本，首选）
- **事务门与事务生命周期强耦合于单一最终持有者**，避免依赖「调用方恰好在异常/取消时仍 Dispose」。
- `NodeExecutionService` 的事务块改为**显式 try/finally**：`finally` 内 `RollbackAsync`（幂等；未提交即回滚）保证任何抛错/取消路径都触发 `Finish → MarkTxIdle`；正常走 `CommitAsync`。
- `SqliteTransaction`：`Finish`（Commit/Rollback/Dispose）幂等、且统一在 worker 上执行并 `MarkTxIdle`；确认 `IsFinished` 防重复释放（已具备）。

### B. WaitTxIdle 有界 + owner 自证（兜底，防永久挂）
- 无界 `Monitor.Wait` → **有界等待**（如 10s，> 单个合理事务时长）：超时即报告「在途事务 owner + 借出点 + 本写者」并快速失败（上层归类），把「永久挂」降为「可自证的一次性卡顿失败」。
- `Entry` 增**打开事务的登记**：`MarkTxOpen` 时记录 owner（业务调用点）/线程/打开时刻；`MarkTxIdle` 清除。超时报告复用并扩展自证（与既有进程内自报告约定一致，零落盘）。
- 复跑取证：卡住瞬间打印的 owner/借出点，把候选 A/B 落到具体方法（哪条持久化路径开的门没关）。

### C. Outbox 每项超时语义复核
- `OutboxWriter.HandleAsync` 用 `cts.CancelAfter(perItemTimeout)` 取消**等待**；确认其不中断「在途事务收尾」（Commit/Rollback 仍会在 worker 完成并释放门），避免「等待已取消、门却已开」。

---

## 五、实施步骤（文件 / 改动点）

| # | 文件 | 改动 | 目的 |
|---|------|------|------|
| 1 | `NodeExecutionService.cs` `PersistNodeOutputsAndKeyMappingsAsync`（L269-286） | 事务块改 try/catch/finally：`finally` 内 `RollbackAsync()`（幂等兜底释放门），正常 `CommitAsync` | 治本：异常/取消路径必 `MarkTxIdle` |
| 2 | `SqliteWriteChannel.cs` | `WaitTxIdle` 改有界（≈10s）并在超时抛带「在途事务 owner/借出点 + 本写者」的异常；`Entry`/`MarkTxOpen` 增 owner 登记、`MarkTxIdle` 清除 | 防永久挂 + 自证定位 |
| 3 | `SqliteTransaction.cs` | `Finish` 幂等（已具备）复核：Commit/Rollback/Dispose 统一 worker 执行 + `MarkTxIdle`；确认取消不跳过收尾 | 释放路径完备 |
| 4 | （可选）`NodePersistOutbox.cs` | `HandleAsync` 复核：取消只作用于等待，不中断在途事务收尾 | 消除候选 B |
| 5 | `Agent.Persistence.Tests` | 新增故障注入：事务开后不 Commit 即模拟取消/异常，断言后续 `RunWrite` 不被永久阻塞（有界/已清） | 单测固话 |

---

## 六、验证

1. 编译 `DataManage`/`DataStoreDriven`/`Agent.Core`/`Agent.Launcher` 0 错；`Agent.Persistence.Tests` 回归全绿（S3 后 86 项）。
2. 新增故障注入单测通过（事务遗留不再永久阻塞单条写）。
3. GUI 复跑 `agent_mcpinvoke_create_task_test.json`，断言：
   - 无 `轮后 next 卡超`、命令无 30s 停滞；
   - 无 `借出等待超时 5s` 死信 / `flush_slow` / queue 积压；
   - 若仍有卡顿，必打印「在途事务 owner + 借出点」供定位。

---

## 七、边界 / 不做

- 不回退 S3 单写 actor 模型（其已根治 5s 死信）。
- 不引入 dump / 落盘取证（保持进程内自报告约定）。
- B 的有界等待仅作「防永久挂 + 自证」，不作正常并发配额（避免重蹈旧 5s 门超时）。
- 若复跑证实并非事务遗留（候选 A/B 排除），则据 owner 证据转其它占用路径治理。
