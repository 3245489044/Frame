# agent.db 读路径无界饿死卡死 — 架构重构方案与实施基线

> **日期**：2026-09-03
> **触发**：`agent_mcpinvoke_create_task_test.json` 运行至命令 #46 `AgentUi_DbReadTableMarkdown`（读 `ur_tasks`）卡死 120s 被 Launcher 强杀。
> **上游参考**：`docs/analysis/2026/9/9.1/2026-09-01-write-serialization-arch-defects.md`（写门/锁域结论）、`docs/analysis/Agent/doc/线程自卫式卡死取证-*.md`（S3 取证）。

---

## 1. 根因一句话

同一 `agent.db` 上并存**三套互不协调的 SQLite 串行化路径 + 多条独立连接**；而**读路径（UI `DbReadTableMarkdown`、Agent `ResolveInputs` 的 `ur_nodes`）绑死在旧通道**——`SqliteRelate.DataUniversal` 的**进程级 `lock(Gate)` + 每命令新建连接 + 无超时**。后台任一写者长持写面时，该库读在进程门上**无限排队**，两条独立读同时饿死 → 命令 120s 无返回 → 文件驱动强杀。

## 2. 现状通道全表（Stage 0 核对产出）

| 通道 | 位置 | 串行化模型 | 读 | 写 | 使用方 |
|------|------|-----------|----|----|--------|
| ① 旧进程门 | `Plate/DataManage/Storage/Sqlite/SqliteRelate.cs` → `DataUniversal` | 全局 `lock(Gate)` + 每命令 `SetConnect`(busy5000+WAL) | **经此（无超时，元凶）** | 非通道残余直写（有 retry） | `SqliteSession`（`SqliteTableReader` UI 读、无环境事务的 `SqliteNodeStore` 读/写、若干直写） |
| ② R3 写门 | `HierarchyMethod/Center/WriteCoordinator.cs` → `Modules.Threading.SerialWriteGate` | SemaphoreSlim + `BoundedExecution` 有界 | — | 业务节点持久化（同步+Outbox 共享） | `NodeExecutionService.PersistAsync` |
| ③ 单写通道 | `Plate/DataManage/StorageFramework/Sqlite/SqliteWriteChannel.cs` + `SqliteTransaction.cs` | **每库单写连接 + 事务独占（正确模型）** | 环境事务内路由 | 事务写（`_beginTransaction` 注入） | `NodeExecutionService.PersistNodeOutputsAndKeyMappingsAsync` |
| LiteDB 图库 | `Operation/DataStoreDriven/Graph/ReferenceGraphCoordinator.cs` | 实例 `_gate` | 经协调器 | 协调器 | 引用边（graph-log.db） |

### 2.1 关键定论（本次日志可解释的）

- Agent `guard_output` 读源（`ur_nodes`）与命令 #46（`ur_tasks`）**两条独立读**都走通道① `DataUniversal` 进程门 → 同一把 `lock(Gate)` → 同时 120s 饿死（非同一把 SQLite 库锁的并发读互锁，而是进程门被单持 + 读在门上排队）。
- 业务节点**写**走 ②③（WriteCoordinator + SqliteWriteChannel/事务），**不经** `DataUniversal` 门。因此持门元凶 = **仍走 `DataUniversal` 的写/长持者**（与 Outbox 通道写者不同域），而非通道写者本身。持门 owner 需 S3/dump 最终坐实，但**架构修复不依赖 owner**。
- 三层不共享门/锁 → 跨域竞争时复现 2026-09-01 文档描述的"门 vs 库锁两套串行化域"残留。文档称"WriteCoordinator 已删除"与实际工作区不符（现态为中间态，正是本次要收敛对象）。

## 3. 目标架构

```
写面：统一到 单库单写连接通道（SqliteWriteChannel），写者按事务排队；任何写门只作有界护栏
读面：WAL 多读快照独立读连接，绝不进进程写门；读全部有界（busy_timeout + 超时返回诊断）
铁律：
  ① 写 = 单库单写连接/单事务（唯一写所有权 → SqliteWriteChannel）
  ② 读 = 独立读连接，不进任何进程互斥门
  ③ 所有等待有界 + 可取消 + 自带取证（owner 自报告），卡死归既有 TO
```

## 4. 阶段（每步独立编译，回归信号 = 复跑 `agent_mcpinvoke_create_task_test.json`）

| 阶段 | 内容 | 本次实施 |
|------|------|---------|
| Stage 0 | 只读核对通道全表（本文件 §2） | ✅ 完成 |
| Stage 1 | 读路径止血：读退出进程写门 + 读连接不再每次抢 WAL 写锁 | ✅ 完成（§5，DataManage 编译通过） |
| Stage 2 | 写门收敛：`DataUniversal` 全局无界 `lock(Gate)` → 按库有界单写门（SemaphoreSlim + 有界等待）；读独立 | ✅ 完成（§5.2，DataManage 编译通过） |
| Stage 3 | Outbox 长事务治理：每信号有界批量排空 + 余队重置位 | ✅ 完成（NodePersistOutbox.cs，Agent.Core 编译通过） |
| Stage 4 | 取证接线 | ⚠️ 部分：Agent 侧 owner 高水位自报告（`OutboxWriter_flush_slow`）+ msgId 定义已加；**完整 `HangEvidenceAssembler` 装配 AgentRunner 未做**（依赖 `Threading.dll` 烘焙 HangForensics 的跨树接线，见 §5.4 待办） |
| Stage 5 | 一致性清理：`Check-SingleWriter.ps1` 判据改为"库级写通道 + 读解耦"；clinerules `enf` 对齐 | ✅ 完成（审计脚本跑通 A/B/C OK） |

## 5. Stage 2 落地说明（DataUniversal 写门收敛）
`CsharpPrj/Plate/DataManage/Storage/Sqlite/SqliteRelate.cs`：
- 删除全局 `object Gate` 无界 Monitor 锁与 `lock(Gate)`；改 `ConcurrentDictionary<string, SemaphoreSlim>` **按库（连接串）单写门** + `WriteGateWaitMs=5000` **有界等待**（不同 DB 文件不再互串、写不再无界阻塞）。
- **取消应用层写重试**（2026-09-03 修正）：`ExecuteSQL` 单次执行于每库信号量内；写锁 `database is locked` 非瞬态，重试会把单次写拖到 ~30s 且掩盖真实写锁长持者。SQLite 自身 `busy_timeout(5s)` 已吸收短暂瞬态；busy_timeout 到期仍 busy 或写门 5s 未获 → **快速失败**抛带 DB/SQL 的诊断异常，交上层归类（execution_failure/引擎 TO），不再存储层空转。`try/finally` 保证门释放。
- 编译：`DataManage.csproj` 0 错误。
> 效果：被堵写由"~30s 重试耗尽"收敛为"~5s 快速失败"，不再把每轮/每条命令拖 30s；长持锁问题仍待卡死瞬间 dump 定位（见 §5.4）。

## 5.3 Stage 3 落地说明（Outbox 有界批量）
`Projects/Agent/src/Agent.Core/Nodes/NodePersistOutbox.cs`：
- `DrainLoop` 每次信号最多排空 `MaxBurstPerSignal=32` 条即让出写通道，余队经 `Signal()` 重新置位继续——避免大积压一口气独占单写通道饿死读。
- 编译：`Agent.Core.csproj` 0 错误。

## 5.4 Stage 4 落地与待办
已加（Agent.Core，编译通过）：`FlushOneAsync` 单条落库超高水位（≥3000ms）经 `AgentLog.Log("OutboxWriter_flush_slow", ...)` 自报告 owner（`outbox-writer:<pid>`）；并在 `agent_log_messages.json` 补 `OutboxWriter_item_timeout/item_failed/flush_slow` 三条定义。
**待办（跨树接线，需在你的机器上完成）**：按 `docs/analysis/Agent/doc/线程自卫式卡死取证-S3实施文档.md` §4，把 `Modules.Threading.HangForensics`/`IHangEvidenceAssembler` 装配进 `AgentRunner` per-run，并注册 `Outbox EvidenceSource`——前提是先重烘焙 `Threading.dll`（含 HangForensics）供 Agent 二进制引用；本环境无法安全完成烘焙+Agent 全编译，故未引入这些跨树类型引用。

## 5.5 Stage 5 落地说明
- `PowerShellScript/Operation/DSL/Check-SingleWriter.ps1`：由「全库 WriteCoordinator.Run* 唯一写门」改为「架构不变式审计」——A 无全局 `object Gate`/`lock(Gate)`；B 读经 `SetReadConnect` 解耦；C 事务写经 `SqliteWriteChannel`；D 旧残留仅告警。实跑 A/B/C OK。
- `.clinerules/axioms.md`：`SEP.DeterministicCoreDisjoint` 与 `Strategy.SingleImplementation` 的 `enf` 加作用域限定（Agent 确定性写门保留；SQLite 存储写判据指向新 Check-SingleWriter）。

## 5. Stage 1 落地说明（本次改动）

改动文件：`CsharpPrj/Plate/DataManage/Storage/Sqlite/SqliteRelate.cs`（`DataUniversal`）。

- **读（`ExecuteQueryCore`）不再获取进程写 `lock(Gate)`**：SQLite WAL 下读者拿独立快照，可与单写者并发，不需要与写者争进程门。消除"读在进程门上无界饿死"这一本次卡死的直接承担机制。
- 读改用**只读连接 `SetReadConnect`**：仅设 `busy_timeout`（有界），**不再每条读重发 `PRAGMA journal_mode=WAL`**——该 pragma 本身需库级写权，每次读抢它会与在途写者互等（此前读慢的又一根因）。
- 写路径（`ExecuteSQL`/`ExecuteNonQuery`/`ExecuteScalar`/写相关）保留既有串行门 + busy 有界重试，语义不变（Stage 2 再收敛到 `SqliteWriteChannel`）。

### 5.1 验收
- ✅ `DataManage.csproj` 编译通过（0 警告 0 错误，2026-09-03）。改动仅落于共享 CsharpPrj 二进制 `DataManage.dll`，Agent/UI 读路径（`SqliteSession`/`SqliteTableReader`/`DataUniversal` 均在该程序集）新启动即载入修复，无需改 Agent 源码。
- 复跑目标脚本：即便再遇锁竞争，UI/Agent 读不再 120s 静默饿死，而按 busy 有界返回（读失败/诊断），进程不被 Launcher 强杀。
- 读-改-写一致性：WAL + 每命令新连接快照，读到的均为已提交态，与旧"串行读"语义等价的可见性（只增并发生）。
- 备注：`ExecuteScalar`（COUNT 类只读、内部经写路径 `ExecuteSQL`）仍会与写门竞争，属 Stage 2 收编到读通道的范围，非本次止血承担路径。

## 6. 风险与不做
- **风险**：迁移写路径到 `SqliteWriteChannel` 时若借出连接跨异步流/线程误用会引入新问题 → 严格遵循 AsyncLocal 重入契约；以既有 StorageFramework 单测守护。
- **不做**：自动修复卡死/自动 switch；进程内抓他线程托管栈；把 UI/WebView2 线程模型重构卷进本方案。
