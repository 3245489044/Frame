# agent.db 写锁长持 — 当前状态与下一步实施报告

> 日期：2026-09-03
> 关联：`docs/analysis/2026/9/9.3/2026-09-03-db-read-hang-arch-refactor.md`（重构基线，本报告为最新状态与后续计划）

---

## 一、问题演化（认识随验证推进）

| 阶段 | 观察到的现象 | 结论 |
|------|--------------|------|
| 1 初现 | `AgentUi_DbReadTableMarkdown`（读 ur_tasks）卡死 120s 被强杀；Agent `ResolveInputs`(ur_nodes) 同步饿死 | 误判"读路径卡死" |
| 2 读修复后 | 读全秒回（#46 正常）；**所有写**（Agent 每轮快照回写 / UI `RunJson`/`TaskPoolCancel`）报 `SQLite 命令重试耗尽`，各烧 ~30s | 真因 = **agent.db 写锁被长持**，此前读写共用同一把进程锁掩盖了写锁问题 |
| 3 精确分析 | **成功 vs 失败的写都围绕同一对象**：成功=取消 `tp_llm_mcp_demo`/其它任务；失败=操作 `tp_mcpinvoke_ctrl`（正在 Running 的任务本身）或其资源、以及 Agent 每轮把快照写回**该任务自己的 ur_tasks 行**。运行结束(Timeout)后锁释放、同一批删除立即成功 | **假设收敛：Running 任务的 ur_tasks 行被其自身消费路径持锁**，运行期内对自己行及相关资源的写全部被挡 |

关键佐证（日志）：
- 21:21:36 取消 `tp_llm_mcp_demo` 成功（不同任务、写，OK）；
- 21:21:37 起所有对 `tp_mcpinvoke_ctrl` 的写全 busy 失败；
- 21:23:28 Agent Timeout 结束后，21:23:38-44 删除 `tp_mcpinvoke_ctrl` 资源立即成功；
- Agent 主循环 3 轮 ×（每轮边界快照回写烧 ~30s）= ~90s 整体被拉长。

---

## 二、当前代码状态（已落地 + 编译验证 ✅）

| 文件 | 改动 | 编译 |
|------|------|------|
| `CsharpPrj/Plate/DataManage/Storage/Sqlite/SqliteRelate.cs` | ① 读(`ExecuteQueryCore`)脱离进程写门，新增 `SetReadConnect`（仅 busy，不再每次抢 WAL 写权）；② 写门 `object Gate` → 按库 `SemaphoreSlim` 有界；③ **取消应用层写重试**——写锁非瞬态，busy_timeout 5s 兜底仍 busy 即快速失败抛带 DB/SQL 诊断 | `DataManage.csproj` 0 错 |
| `Projects/Agent/src/Agent.Core/Nodes/NodePersistOutbox.cs` | Stage3 每信号有界批量(32)+余队重置位；Stage4 `FlushOneAsync` 单条落库高水位自报告 `OutboxWriter_flush_slow` | `Agent.Core.csproj` 0 错 |
| `Projects/Agent/src/Agent.Core/Diagnostics/agent_log_messages.json` | 补 `OutboxWriter_item_timeout/item_failed/flush_slow` 三条 msgId 定义 | JSON 合法 |
| `CsharpPrj/Operation/BackEndDriven/CommandExecution/CommandTelemetry.cs` | **移除全部写 dump 到盘的机制**（`TryAutoDump`/`FILEDRIVEN_HANG_DUMP`/dotnet-dump spawn/手动建议）；卡死证据仅内存+控制台 | `BackEndDriven.csproj` 0 错 |
| `PowerShellScript/Operation/DSL/Check-SingleWriter.ps1` | 判据由"全库 WriteCoordinator.Run*"改为"架构不变式"（A 无全局门 / B 读解耦 / C 事务经 SqliteWriteChannel），实跑 A/B/C OK | 脚本 OK |
| `.clinerules/axioms.md` | `SEP.DeterministicCoreDisjoint`/`Strategy.SingleImplementation` 两处 `enf` 加作用域限定 | — |

已删文件：`%TEMP%\hang_*.dmp`（含手动抓的 494MB `hang_41864_live.dmp`）——**全库 `.cs` 已无任何 dump/大文件写盘引用**（`NO_DUMP_WRITE_REFS` 实测）。

> 已确立约定：本问题的 debug 不落任何磁盘文件（不抓 dump、不写日志大文件）；锁归属定位一律走**进程内自报告**。

---

## 三、根因当前结论

**已确证**：读饿死已修复；写锁长持是真正问题；取消重试后写快速失败（≤5s），不再把运行拖到 30s/条。
**待验证假设（下一步主攻）**：Running 任务 `tp_mcpinvoke_ctrl` 的 ur_tasks 行在消费/执行期间被**其自身路径持有一个未释放的写事务/写连接**，直到运行结束才释放。

---

## 四、下一步实施步骤（纯代码、零落盘）

### Step 1 —— 代码定位 Running 期间持行锁的路径（只读调查）
- 阅读 `Share.TaskStatusPool.TaskPool` 消费循环 + `Agent.Core/TaskPool/AgentTaskPool.cs`（`WriteStatusAsync`/状态标记）与 `LlmTaskExecutor`；
- 检查：状态改为 Running 到任务完成的**区间内**，是否存在：
  - 对 ur_tasks 开了一个 `BeginTransaction`（`SqliteTransaction`/`SqliteWriteChannel` 租约）却**在进入 Agent 主循环前未提交/未释放**；
  - 或 `WriteThroughTaskPoolStore`/`SqliteTaskPoolStore` 的 `UpdatePayloadAsync`/`EnqueueAsync` 路径在运行期间仍持有写通道/写事务；
  - 或 ur_tasks 写入既有 `SqliteWriteChannel`（事务）又有 `DataUniversal` 独立连接，二者在运行期互相持锁。
- 产出：明确"哪一处把该任务行的写锁持到运行结束"。

### Step 2 —— 修复（按定位结果落一处或组合）
- 若消费路径在 Running 期间持该行写事务 → **改为：状态先 commit/释放锁，再进入 Agent 主循环；运行期间不再持有该任务行的写锁**；每轮快照回写为独立 autocommit/短事务。
- 若某 `SqliteTransaction` 异常未释放 → 补 `await using`/finally 提交回滚归还。
- 若 DataUniversal(独立连接) 与 SqliteWriteChannel(事务) 对同库互锁 → 收敛到**同一库级写通道**（对齐 2026-09-01 文档 §四 方案 A/C），读经 WAL 多读快照，写经单写通道排队，杜绝运行期长持。

### Step 3 —— 回归验证
- 复跑 `PowerShellScript/FileDriven/FileDriven/运行测试GUI模式.ps1 agent_mcpinvoke_create_task_test.json`
- 预期：任务 `tp_mcpinvoke_ctrl` 能进入并完成；写不再 busy 失败；单条写 ≤ 正常耗时（无 5s/30s 空转）；日志显著变短（不再整段卡死证据）。
- 兜底断言：写失败出现时，走既有 `OutboxWriter_flush_slow`/Agent 读源打点即可定位 owner，**不依赖任何 dump**。

### Step 4 —— 治理收尾
- 据 Step 2 实际落地更新 `.clinerules` `enf` 与 `Check-SingleWriter.ps1`（保持"读解耦 + 写走库级单写通道"判据一致）。
- 归档本报告阶段结论到 `2026-09-03-db-read-hang-arch-refactor.md`（同步去掉已废弃的 dump 取证表述）。

---

## 五、另立工作项（环境噪声，非本次写锁范围）
- `CoreWebView2 can only be accessed from the UI thread`（回流自检 2/3 异常）——UI 线程封送纪律（对齐既有 P2/UiDispatchGuard 项）。
- `注册工作流到 Hub 失败（IEventExecutionService 为空）`——装配缺口。
- `MainForm_AgentReadiness`/`RuleSet_Load_loaded` 缺 msgId 定义——日志定义补齐（`execution_log_messages.json` / `agent_log_messages.json`）。

---

## 六、关键索引
- 重构基线：`docs/analysis/2026/9/9.3/2026-09-03-db-read-hang-arch-refactor.md`
- 写门锁域分析（历史）：`docs/analysis/2026/9/9.1/2026-09-01-write-serialization-arch-defects.md`
- 取证设计（dump 法已弃，仅存历史参考）：`docs/analysis/Agent/doc/线程自卫式卡死取证-*.md`
- 改动人：`SqliteRelate.cs`、`NodePersistOutbox.cs`、`CommandTelemetry.cs`、`Check-SingleWriter.ps1`、`.clinerules/axioms.md`

---

## 七、Step1 静态定位结论 + 取证机制落地（本会话新增）

> 状态：已完成静态定位；新增库级写面持有者自报告机制（进程内、零落盘）；编译 `DataManage.csproj` 0 错 0 警。
> 尚未复跑（需真实 Agent 运行环境）——复跑失败场景时应按本节约定的异常观测判定 owner。

### 7.1 静态定位结论（写路径图 + 判定）

排查 `ur_tasks`/`ur_nodes` 全写链后确认：**不是"某个未释放的行级事务"，而是两套库级写域对同一 agent.db 无协调**。

| 写面 | 内容 | 连接/锁模型 |
|------|------|-------------|
| 面① DataUniversal | ur_tasks（Agent 每轮回写/心跳/UI cancel/delete） | `SqliteSession`→`SqliteRelate.DataUniversal.ExecuteSQL`：自持按库门 + 每次新建连接 + autocommit |
| 面② SqliteWriteChannel | ur_nodes/KeyMapping/引用边（OutboxWriter 后台） | `WriteCoordinator`(SerialWriteGate) + `SqliteTransaction`→`SqliteWriteChannel.Acquire`：常驻单写连接 + 事务 |

已排除：`NodeExecutionService.PersistNodeOutputsAndKeyMappingsAsync`（`await using(tx)` 异常安全，不泄漏）、TaskPool 状态写与心跳（autocommit 不持事务）。
根因 = 两套写面对同库并发仅靠 SQLite busy_timeout(5s) 兜底，运行期任一写面长持即挡另一面对该任务行的写；"围绕 Running 任务自身"因该行是运行期写最频繁对象。

### 7.2 新增：库级写权持有者登记（跨两套写面可见 owner）

- 新文件：`CsharpPrj/Plate/DataManage/StorageFramework/Sqlite/SqliteWriteOwnerRegistry.cs`（internal，进程内内存 registry，按归一化库文件路径分组）。
- 集成点：写方在「持库级写权期间」`Enter` 登记（owner + 线程 + 已持时长），归还 `Exit` 注销；任一侧等写权超时或撞 busy 失败时，异常消息追加 `当前库写权持有者=[...]`：
  - `SqliteWriteChannel.cs`：`Acquire` 首次借出 `Enter("SqliteWriteChannel")`、`Release` 末次归还 `Exit`；`Acquire` 门等待超时异常附 `Describe`。
  - `SqliteRelate.cs` `DataUniversal.ExecuteSQL`：门内写执行期 `Enter("DataUniversal")`、`finally` `Exit`；写门超时 / busy 竞争异常附 `Describe`（busy 排除自身 token）。
- 语义不变式：登记/注销全程 `try/catch` 静默，绝不改变正常读写；纯内存、零落盘。

### 7.3 复跑判定 owner（Step3 预期观测）

复跑 `运行测试GUI模式.ps1 agent_mcpinvoke_create_task_test.json` 失败场景，在控制台/日志的写失败异常消息中读取 `当前库写权持有者=[...]`：

- `持有者=[...SqliteWriteChannel... thread=OUTBOX_TID heldMs=长]` → 面② OutboxWriter 后台事务长持（对回 Outbox 线程号）→ Step2 落"事务改独立短写 / 有界批量已降"或收敛同写通道；
- `持有者=[...DataUniversal... heldMs=长]` → 面①某长写（对回线程即 AgentRunner/心跳/UI）→ 定位该写为何长持；
- 两侧均在短暂窗口交替（heldMs 短）→ 属高并发瞬时竞争，观察是否仍有 5s 级空转，必要时提高通道复用。

据此选择 Step2 修复路线（收敛同库级写通道 vs 定点归还），无需任何 dump。

### 7.4 S1 registry 精化 + 借出点栈（已落地，编译/测试通过）

运行日志显示持权者为 **Agent 主执行线程 thread=19 自身**（`SqliteWriteChannel | thread=19 | heldMs 5219→30000` 贯穿 run），且 SqliteWriteChannel 与 DataUniversal 同刻同线程登记 —— 此前的「整队重建」registry 在并发下可能丢项/虚报，须先排除干扰再判 owner。据此完成 S1 精化：

- **修复竞态**：registry 由「读快照 + 整队重建」改为「每库 token→Holder 精确集合」（`ConcurrentDictionary` `TryRemove` 按 token 精确注销），高并发进出不再丢项/虚报长持。
- **新增借出点栈**：`Enter` 采集 `CaptureBorrowStack`（顶部 10 帧），写失败异常携带「借出点」调用链 —— owner 精确定位到**调用行**，不再止于 owner/线程号。
- **验证**：新增 `CentralTest/AgentTest/Agent.Persistence.Tests/SqliteWriteOwnerRegistryTests.cs`（5 用例：往返清零 / 排除自身 / 多持有者精确移除 / 8 线程并发进出无残留 / 库路径归一命中）；`DataManage.csproj` 经 `AssemblyInfo.Friends.cs` `InternalsVisibleTo("Agent.Persistence.Tests")` 暴露 internal。`dotnet test --filter SqliteWriteOwnerRegistryTests|SqliteWriteChannelTests` = **11/11 通过**。
- **复跑指引**：重跑 `agent_mcpinvoke_create_task_test.json` 失败场景，读异常 `当前库写权持有者=[... 借出点: at ...]` → 直接锁死 Agent run 内首次长持借出的调用行，据此再定 S2（存储收敛单写通道）/ S3（主循环回写投后台）。

### 7.5 复跑借出点证据 —— owner 已定死到 NodeExecutionService 节点持久化链（S2 精确落点）

按 §7.4 复跑后，写失败异常携带了完整「借出点」，把两个长持者都钉在同一处：

**两个持权者（同线程 thread7、同刻、同库 agent.db、heldMs 5250→30000 贯穿 run）**
- `SqliteWriteChannel`（单写连接）借出点：
  `SqliteTransaction..ctor ← SqlitePersistenceStore 事务工厂 ← PersistenceStoreFacade.BeginTransactionAsync ← AgentOrchestrator:123 ← NodeExecutionService.PersistNodeOutputsAndKeyMappingsAsync:271` → **事务内写 ur_nodes 输出 + KeyMapping**。
- `DataUniversal` 借出点：
  `SqliteNodeStore.PutAsync ← WriteGuardPort.PutAsync` → **节点库写透 ur_nodes（经 DataUniversal 新连接）**。

**结论**
病灶 = **`NodeExecutionService.PersistAsync` 一次节点持久化把 agent.db 写拆成两条独立写面**：①`SqliteTransaction`（事务，ur_nodes 输出/KeyMapping）；②节点库写透 `SqliteNodeStore`（DataUniversal 新连接，同一 ur_nodes）——同库两写面并发自竞争/被长持卡死 ~30s，饿死所有其它写者（主循环 ur_tasks 回写、UI `TaskPoolCancel`/`RunJson` 删除/资源清理全部 5s 级空转），run 结束（Timeout）后持久化被回收、写立即恢复（22:00:00 删除成功）。
注意：该写由单写后台（Outbox worker，thread7）执行，其 per-item 5s `CancelAfter` 对**同步 SQLite 阻塞无效**，故一条卡死可贯穿整个 run —— 印证并发设计 I2「单库单写通道」缺失是根因。

**S2 精确落点（最小修）**
让 `NodeExecutionService` 一次持久化的**全部 agent.db 写收敛到同一个 `SqliteTransaction` 连接**上串行完成：
- 节点输出 + KeyMapping（已在事务内）+ **节点库写透 ur_nodes（`SqliteNodeStore.PutAsync`，当前因事务已提交、ambient 失效而落到 DataUniversal 新连接）** → 把写透并入同一事务（SqliteNodeStore/KvJsonStoreBase 已是 `AmbientTransactionAwareCommandExecutor`，只要仍在 ambient 事务作用域内即自动路由到事务连接）；
- LiteDB 引用/权威边写（不同库文件，不与 agent.db 冲突）留在事务外、commit 之后执行。
由此单次持久化内 agent.db 只有一把写权，从机制上消灭本证据暴露的「同持久化两写面自锁」。

（S1 精化 registry + 借出点栈对本步判定的价值已验证：owner 从"thread 号"精确定位到 `NodeExecutionService.cs:271` + `WriteGuardPort.PutAsync` 两条调用行。）

### 7.6 S2 阶段落地：agent.db 单写通道收敛（架构 I2，非补丁）

依据 §7.5 owner（一次节点持久化拆成 `SqliteTransaction` + `DataUniversal` 节点写透两条同库写面），做**agent.db 专用单写通道收敛**——不全局改 `SqliteSession`（其常驻副作用会锁临时库/破坏测试生命周期），而为 agent.db 新增专用执行器并在**装配层**替换：

- **新增原语**：`CsharpPrj/Plate/DataManage/StorageFramework/Sqlite/SqliteChannelSession.cs`（`IDbCommandExecutor`）：写（`ExecuteNonQuery`/`ExecuteInsertReturningId`）经 `SqliteWriteChannel` 借该库唯一常驻单写连接 autocommit；读（`ExecuteQuery`/`ExecuteScalar`）经 WAL 只读连接。与 `SqliteTransaction`（事务，同走通道）共用同一 agent.db 写权。
- **`SqliteWriteChannel` 按归一化库路径分组**（复用 `SqliteWriteOwnerRegistry.DbPathOf`）：同一库文件无论连接串写法如何都只一个常驻写连接/一把写权。
- **装配点换用**（agent.db 写面 → 单通道）：
  - `TaskPoolHosting.BuildTaskPoolStoreAsync`：ur_tasks（AgentRunner 每轮回写/心跳/UI 取消/删除）executor → `SqliteChannelSession`；
  - `DataStoreProviderFactory.BuildSqliteDataStore`：数据驱动基础节点库 ur_nodes（`SqliteNodeStore`，含 WriteGuardPort 节点写透）executor → `SqliteChannelSession`；
  - `AgentFramework`（ConversationHosting ur_histories 会话历史）→ `SqliteChannelSession`。
- **验证**：`DataManage`/`DataStoreDriven`/`Agent.Launcher` 编译 0 错；`SqliteChannelSession` 写读往返测试新增并通过；`Agent.Persistence.Tests`（SqliteChannelSession + SqliteWriteOwnerRegistry + SqliteWriteChannel）12/12；`StateManagement` `SqliteTaskPoolStoreTests` 6/6（临时库仍 SqliteSession，不受常驻锁影响）。

**预期效果**：agent.db 热路径写（ur_tasks 回写、ur_nodes 节点写透、持久化事务、UI 任务操作）统一经 SqliteWriteChannel 单写通道串行（短事务/autocommit），不再有 `DataUniversal` 新连接与 `SqliteTransaction` 同库互撞 → 运行期 30s 写面长持根因被消除。

**遗留 / 待你复跑确认**：
1. 复跑 `agent_mcpinvoke_create_task_test.json`，确认写不再连续 5s 空转、任务能完成、`SqliteWriteChannel`/`DataUniversal` 不再成对长持。
2. 若仍见某 `DataUniversal` agent.db 写者长持，用 §7.4 registry 借出点栈定位该写者装配点并补 `SqliteChannelSession`（候选：`SqlitePersistenceStore` 内 `SqliteSchema`/`SqliteSnapshotStore` 仍自用 `SqliteSession`，仅初始化/非热路径，必要时收敛）。
3. S3（Agent 主循环回写投单写后台、Outbox per-item 取消能真正中断同步写）为后续阶段，非本证据直接必需。
