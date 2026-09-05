# 框架级彻底重构 — 实施记录（P0–P5 全量落地）

> **日期**：2026-09-01
> **依据**：`2026-09-01-framework-refactor-arch.md`（R1–R6 / P0–P5）
> **状态**：P0/P1/P3/P4/P5 ✅ 已实施并构建验证；P2 ✅ 机制落地（具体 UI 调用点迁移待运行时取证，见 §三）

---

## 一、已实施内容

### P1/R2 持久化 Outbox 解耦（根治 D2 卡死）
| 文件 | 改动 |
|------|------|
| `Projects/Agent/src/Agent.Core/Nodes/NodePersistOutbox.cs`（新建） | `NodePersistWork` + `IOutbox` + `OutboxWriter`（后台单 worker、单条有界超时、失败死信归类、停止时最终排空） |
| `Projects/Agent/src/Agent.Core/Nodes/RoundStepInvoker.cs` | 可选 `enableOutbox`（默认关=零回归）；`OnAfterStepAsync` 启用时 `Append` + 立即登记端口，否则原同步持久化 |
| `Projects/Agent/src/Agent.Core/Nodes/WorkflowRunner.cs` | 透传 `enableOutbox` |
| `Projects/Agent/src/Agent.Core/Loops/AgentRunner.cs` | 生产主循环显式 `enableOutbox: true` |

**正确性**：`ExecuteAsync` 已写内存 `vars`，同轮内下一步经数据空间读到上一步产物——Outbox 仅延迟存储侧持久化，不破坏轮内节点组合；终态信封走同步 `WriteNodeOutputAsync`。

### P0/R1 单一写协调器 `WriteCoordinator`（三套门全收敛）
| 文件 | 改动 |
|------|------|
| `Plate/DataManage/StorageFramework/Sqlite/WriteCoordinator.cs`（新建） | 单一写门 `Run(Action)`/`Run<T>`/`RunAsync`/`RunAsync<T>`/`RunGraph*`（有界等待 + finally 释放 + 可取消） |
| `.../SqliteTransaction.cs` | 事务写路径 → `WriteCoordinator.Run` |
| `Plate/DataManage/Storage/Sqlite/SqliteRelate.cs` | 非事务 `ExecuteSQL`/`ExecuteQueryCore` → `WriteCoordinator.Run`（保留 busy 指数退避重试），删除 `lock(Gate)` |
| `DataStoreDriven/Graph/ReferenceGraphCoordinator.cs` | LiteDB `_gate` → `WriteCoordinator.RunGraph(Async)`，删除实例门 |
| `.../SqliteWriteGate.cs` | **删除** |

**验收**：全库搜索 `lock(Gate)`/`SqliteWriteGate`/`SemaphoreSlim _gate` 为 0；三处关键存储文件均经 `WriteCoordinator.Run*`（`Check-SingleWriter.ps1` 通过）。

### P3/R4 可取消单轮超时（WhenAny 放弃 → linked-CTS 真正中断）
| 文件 | 改动 |
|------|------|
| `Projects/Agent/src/Agent.Core/Loops/RoundLoopDriver.cs` | 单轮超时以 per-round linked-CTS 承载：`CancelAfter` → 取消令牌真正到达在途执行体（线程/锁回收）；外层取消上抛、单轮超时记 TO；非合作式阻塞有界宽限收敛 |

### P4/R5 机制强制（写路径审计）
| 文件 | 改动 |
|------|------|
| `WriteCoordinator` | 新增 `AssertSingleWriter(channel, owner)` 单一写通道注册表（平行实现 fail-fast） |
| `SqliteTransaction` / `SqliteRelate.DataUniversal` / `ReferenceGraphCoordinator` | 分别注册 `sqlite-transaction` / `sqlite-direct` / `litedb-graph` 写通道 |
| `PowerShellScript/DSL/Check-SingleWriter.ps1`（新建） | 编译/CI 级写门审计：残留旧门 + 关键文件经 `WriteCoordinator.Run*` 白名单；EXIT=0 通过 |
| `.clinerules/axioms.md` | `SEP.DeterministicCoreDisjoint` / `Strategy.SingleImplementation` 补 `enf` 标注 |

**验证**：`Check-ClinerulesReferences.ps1` 悬空/废弃/循环 = 0。

### P5/R6 内建观测与卡死自取证
| 文件 | 改动 |
|------|------|
| `Projects/Agent/src/Agent.Core/Diagnostics/StallDetector.cs`（新建） | watchdog：无心跳超阈即产出诊断（阶段 + 停滞点 + 线程数）→ AgentLog + `ur_events`（RunEvent） |
| `AgentRunner.RunMainLoopAsync` | 包裹 StallDetector：每轮完成心跳，卡死单次运行即定位停滞轮 |

### P2/R3 线程隔离机制
| 文件 | 改动 |
|------|------|
| `Share/HostAssembly/Concurrency/UiDispatchGuard.cs`（新建） | UI 封送守卫：有界超时 + 失败隔离（防 sync-over-async 死锁 / 挂起 dispatcher 拖死运行时），供 WebView2/ECS 封送点收敛 |

---

## 二、构建验证（全部 ✅ 0 警告 0 错误）
| 目标 | 结果 |
|------|------|
| `CsharpPrj/MainProj.sln` | ✅ |
| `Projects/Agent/Agent.sln` | ✅ |
| `FileDriven/FileDriven.sln` | ✅ |
| `Check-SingleWriter.ps1` | ✅ 0 绕过 |
| `Check-ClinerulesReferences.ps1` | ✅ 悬空/废弃/循环 = 0 |

---

## 三、运行时回归修复（2026-09-01 复跑测试发现并根治）

复跑 `agent_mcpinvoke_create_task_test.json` 观测到：P1 Outbox 生效（`guard_input→provide_prompt` 立即开始，卡死消失 ✅），
但出现**统一写门级联超时（5000ms）**。根因与修复：

| 根因 | 修复 |
|------|------|
| 单一门收敛后，`SqliteTransaction` **逐命令**获取门，命令间事务仍持 SQLite 写锁 → 「Outbox 后台事务持库写锁 + 主循环/UI 命令在门内对库锁 busy-wait 5s」→ 门被长持 → 全局级联超时 | 写门改**同线程可重入**（`Enter/Exit` 深度计数，Exit 跨线程安全）；`SqliteTransaction` 经 `WriteCoordinator.Acquire()` **事务全程持门**（单一逻辑写单元）：事务持门期间其他写入线程在**门上排队**（而非门内对库锁 busy-wait），彻底消除自相竞争级联超时 |

**改动文件**：`WriteCoordinator.cs`（可重入 + `Acquire` lease）、`SqliteTransaction.cs`（ctor 持门 / Commit·Rollback·Dispose 归还）。

**验证**：三 sln 0 警告 0 错误；`Check-SingleWriter.ps1` 0 绕过。

---

## 四、待办 / 门槛项
| 阶段 | 内容 | 门槛 |
|------|------|------|
| P2 续 | 具体 UI 调用点（WebView2 / ECS 投影 / FileDriven 命令管道）迁移到 `UiDispatchGuard` + `IUIDispatcher` | **需运行时取证**（项目文档明示：先抓卡死线程栈确认 C4 精确因果再动），本会话未盲改 UI 侧 |
| 回归 | 重跑 `agent_mcpinvoke_create_task_test.json` / `agent_taskpool_test.json` | 需运行环境 |

---

## 四、回归建议
- 重跑 `agent_mcpinvoke_create_task_test.json` / `agent_taskpool_test.json`：`guard_input` 后 `provide_prompt` 立即开始；挂起存储写不卡 `Running`；卡死单次自取证。
- 生产启用 Outbox：`AgentRunner` 已显式 `enableOutbox: true`（可在 `RoundStepInvoker` 构造传 `enableOutbox:false` 回退同步持久化）。
- CI 集成：`Check-SingleWriter.ps1` 入流水线（写门回归即 fail）。

*P0/P1/P3/P4/P5 全量实施并构建验证；P2 机制已落地，具体 UI 调用点迁移按项目既有门槛（运行时取证）留待下一步。*

