# 下一步实施：agent.db 单写通道收敛后的自锁治理 + 日志精简

> 日期：2026-09-03（晚）
> 基线：`docs/analysis/2026/9/9.3/2026-09-03-db-write-lock-status-and-next.md`（§7.1-7.6）
> 性质：S3 阶段实施规划。分两部分：(A) 日志精简（低风险、立即）; (B) S3 持久化单事务化（架构，需复跑验证）。

---

## 一、复跑证据结论（S1/S2 之后，本规划的依据）

复跑 `agent_mcpinvoke_create_task_test.json`（GUI 模式）后，日志形态相较 §7.5 发生**关键变化**：

| 项 | §7.5（收敛前） | 本次（收敛后） |
|---|---|---|
| 失败异常写面 | `SqliteWriteChannel` + `DataUniversal` 两个持有者成对长持 | **仅剩 `SqliteWriteChannel` 一个持有者**（DataUniversal 写面已消除 → 装配收敛起效） |
| holder.heldMs | 单调增至 ~30000 | 稳定在 ~5000-5031（每次快照都是 ~5s 边界） |
| 后果 | 所有写 5s 空转直至 run 结束 | OutboxWriter 每条节点落库 `TimeoutException 借出等待超时 5s` 死信（queue 积到 7-9）；主线程 ur_tasks 快照回写同样等门 5s 失败 |

**归因（S3）**：在 agent.db 只剩一把 `SqliteWriteChannel` 写权后，真正的缺陷是——`NodeExecutionService.PersistNodeOutputsAndKeyMappingsAsync:271` 开出的 `SqliteTransaction`（持有 agent.db 单写连接/独占门）在其存续期间，流程内/并发的其它 agent.db 写（ur_nodes 节点写透、ur_outputs、ur_tasks 回写）**无法共享这条连接**，需再 `Acquire` 同一通道 → 与持门者自锁；`SqliteWriteChannel` 的 `AsyncLocal` ambient 重入只对同一异步流有效，跨线程（Outbox 后台 vs Agent 主线程 vs 节点写透）即失效 → 每 ~5s 门超时 → 释放 → 下一条顶上，循环死信、饿死所有写者。

---

## 二、日志冗长问题（现状与目标）

### 2.1 现状（用户痛点）
每次写失败异常都携带 `当前库写权持有者=[ ... ]`，内含：owner、线程、heldMs + **`借出点` 全栈 10 帧**。在 5s 循环故障下，Outbox 每 node 一条、ur_tasks 每轮一条，每条 ~15 行栈 → **日志刷屏、关键信号淹没**（本次单次复跑此类超长块出现 8+ 次）。

### 2.2 目标
- 常规失败（短持竞争）只输出**单行摘要**：`当前库写权持有者=owner|tid|heldMs|borrow=Method(File:line)`。
- 仅在「确为异常长持」时（holder.heldMs ≥ 阈值，如 2×busy=10s）才展开完整借出点栈。
- 同一 owner 同 heldMs 的重复报告去噪（相邻同摘要只打一次，或用 owner 收敛计数）。

### 2.3 落点（均不改写语义，纯输出收敛）
| 文件 | 改动 |
|---|---|
| `SqliteWriteOwnerRegistry.cs` | 持有者项已存完整栈；新增 `DescribeCompact`（单行摘要，取栈第一业务帧方法名:行）与 `DescribeFull`；`Describe` 内部按 `heldMs>=FullStackThresholdMs` 择一 |
| `SqliteWriteChannel.cs` `Acquire` 超时异常 | 附加改为 `Describe`（自动按阈值 compact/full） |
| `SqliteRelate.cs` `DataUniversal.ExecuteSQL` busy/门超时异常 | 同上 |
| （可选）`NodePersistOutbox.cs` 死信日志 | `OutboxWriter_item_timeout`/`item_failed` 精简 message 长度（不再整段转发异常栈） |

新增常量：`SqliteWriteOwnerRegistry.FullStackThresholdMs = 10_000`（>busy_timeout 一倍方认为真长持、才给全栈）。

---

## 三、下一步实施步骤

### A. 日志精简（低风险，先做）
1. registry 增加 `DescribeCompact`/`DescribeFull` 与阈值择一；`Describe` 保留签名做内部路由（兼容现有调用，零侵入）。
2. SqliteWriteChannel / SqliteRelate 两处异常消息改用（行为不变，消息变短）。
3. 验证：复跑一次，确认常规写失败仅一行摘要、真长持(>10s)才展开栈、日志量骤降。
   预期从「每次 8+ 块 ×15 行」降到「每块 1-2 行；真长持才带栈」。

### B. S3 持久化单事务单线程化（架构，需复跑验证）
1. **读定位**：`NodeExecutionService` 一次持久化（PersistAsync → WriteGuardPort/NodeOutputStore/SqliteNodeStore）里，哪些 agent.db 写在 `SqliteTransaction`(通道连接) 存续期间**未走该事务连接、而二次 `Acquire` 通道**（`AmbientTransactionAware` 是否覆盖、跨 `Task.Run`/线程是否丢 ambient）。
2. **目标结构**：一次持久化的**全部 agent.db 写收敛到同一个 `SqliteTransaction` 连接**上串行完成（节点输出 + KeyMapping + 节点库写透的 SQLite 部分）；LiteDB 引用/权威边写（不同库文件）留在事务之外、commit 后执行 → 杜绝「持门期间二次 Acquire」自锁。
3. 若跨线程共享连接不可避免（写透在不同线程），改为**逐条短写排队**（每次 Acquire 立即还），不做跨线程持连接事务。
4. 验证：复跑 `agent_mcpinvoke_create_task_test.json`，断言：无 5s 借出超时死信、Outbox queue 不积压、ur_tasks 每轮回写成功、`OutboxWriter_flush_slow` 不出现。

### C. 收尾治理
- 依据 B 实际落地更新 `.clinerules` `enf` / `Check-SingleWriter.ps1`（保持「读走 WAL、agent.db 写走单库写通道、无跨线程长事务」判据一致）。
- 本报告阶段结论回写基线报告（§7.x），并更新 §2 日志 msgId 缺口（`MainForm_AgentReadiness`/`RuleSet_Load_loaded`）与五节环境噪声项。

---

## 四、验收矩阵

| # | 验收 | A | B |
|---|---|---|---|
| 1 | 常规写失败仅单行摘要（≤2 行/次） | ✅ | — |
| 2 | 真长持(>10s)才展开借出点全栈 | ✅ | — |
| 3 | 复跑无 5s 借出超时死信、queue 不积压 | — | ✅ |
| 4 | ur_tasks 每轮回写 / UI 操作不再 5s 空转 | — | ✅ |
| 5 | `OutboxWriter_flush_slow` 不出现 | — | ✅ |
| 6 | 编译 `DataManage`/`DataStoreDriven`/`Agent.Launcher` 0 错；`Agent.Persistence.Tests` 回归通过 | ✅ | ✅ |

---

## 五、不做 / 边界
- 不做：全局 `SqliteSession`→`SqliteWriteChannel` 强制收敛（常驻连接会锁临时库/破坏测试，已回退验证）。
- 不做：引入 dump / 落盘取证（保持进程内自报告约定）。
- S3 若证实无法在可接受影响面内单事务化 → 退化为「逐条短写排队 + Outbox 同步写可中断」方案，另行设计。
