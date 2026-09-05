# 写门统一化重构 — 架构级设计缺陷分析与正确方向

> **日期**：2026-09-01
> **依据**：两条复跑运行日志（23:19 统一写门后级联超时 / 23:27 复现）+ 存储层/Agent 代码结构
> **结论先行**：`2026-09-01-framework-refactor-arch.md` 的 **R1「单一写门（WriteCoordinator 进程级门）」目标假设在本架构下不成立**，
> 实施后实证出现**锁序反转死锁 + 级联 5s 超时**。P1（Outbox 解耦）为正确根治；R1 需改为**连接/事务级串行**而非进程级门。

---

## 一、实证事实（两条运行日志）

### 1.1 P1 Outbox 生效（正确方向）
两次日志一致：`guard_input` 完成后 `provide_prompt` **立即开始**（`…39.810→39.813` / `…50.110→50.115`），
步骤后跨介质持久化已走 Outbox 后台，**D2 卡死（provide_prompt 永不开始）消失**。✅

### 1.2 统一写门后级联超时（回归）
统一为单一 `WriteCoordinator` 门后，反复出现：
```
运行时快照持久化失败: 统一写门等待超时（5000ms）
[WorkflowRunner] 步骤 provide_prompt 失败: TimeoutException: 统一写门等待超时（5000ms）
AgentUi_RunJson / AgentUi_TaskPoolCancel / AgentUi_DbReadTableMarkdown → TIMEOUT（5000ms）
第 1/2/3 轮执行失败: 统一写门等待超时（errorClass=Timeout）
```
即**单一门被某长持者占用 → 一切 SQLite/图写等待者 5s 级联超时**。

---

## 二、架构级缺陷分析

### A. 单一写门的串行化粒度与 SQLite 多连接写模型不匹配
- 代码库对**同一个 agent.db** 存在**多个独立 SQLite 连接**：`SqliteTransaction`（事务连接）、`DataUniversal`（每次调用新建连接）、`SqliteNodeStore`（经 `SqliteSession`→`DataUniversal` 的独立连接）。
- 一个**进程级门**能串行"谁进入临界区"，但 **SQLite 写锁是按连接/事务持有的**，两者不共界。
- 门（进程协调）与库锁（连接协调）是**两套独立串行化域**，无法由进程级门统一表达。

### B. 锁序反转死锁（本次级联的直接根因）
- **Outbox 后台线程**：先拿统一门（进入事务）→ 再等 SQLite 库写锁。
- **主循环 / UI 命令线程**：先拿 SQLite 库写锁（经 DataUniversal 独立连接）→ 再等统一门。
- 两个线程各自"持一门等另一把" → **双向等待死锁**（lock-order inversion）→ 门被长持 → 其余等待者 5s 级联超时。
- 独立性：门与库锁**不嵌套有序**时，进程级门放大而非消除竞争。

### C. Outbox 引入新的并发写者，存储层未在连接/事务边界内协调
- Outbox 后台 writer（独立线程）+ 主循环轮边界持久化（`PersistRuntimeAsync`）+ UI 命令（`AgentUi_*`）是**同一库上的并发写者**。
- Outbox 是正确的解耦（P1），但它新增了一个**并行写线程**；用一个进程级门强行串行它们，恰好触发了 B 的锁序反转。

### D. 事务内嵌套写同一库（独立连接）必然 self-contention
- `PersistAsync` 用事务写 NodeOutput+KeyMapping；**节点库/引用库走独立连接**（见 E）。
- 若在事务持锁期间（同线程、门被重入）再经独立连接写同一库 → 该连接对**本事务自己的写锁** busy-wait 5s → 门被长持 → 级联。
- **重入门只解决"门的嵌套获取"，不解决"独立连接对同库锁的自竞争"。** 这正是本架构下重入无法根治的原因。

### E. `_beginTransaction` 只覆盖部分写面，单条逻辑写被拆到多连接/多锁
- 一次节点持久化横跨：事务连接（NodeOutput+KeyMapping）+ DataUniversal 连接（节点库）+ LiteDB（图关系）。
- 三个独立锁域、无法由一把进程级门做"单一逻辑写单元"的原子串行。
- 文档 R1 的"所有 store 实例由此构造（无旁路写）"未覆盖到"同一库多连接"这一底层事实。

### F. 架构文档 R1 的前提假设错误
- R1 假设"D1 根因 = 多写门互不协调 → 收敛为单一写门即根治"。
- **实证反例**：多写门不协调（各自有界）不产生锁序反转；而单一进程级门在多连接 SQLite 下**制造锁序反转**。
- 即：`多门 × 有界` 是安全的（无级联），`一门 × 多连接` 是危险的（锁序反转）。R1 的"收敛方向"写反了。

---

## 三、根因一句话

> **串行化单元错位**：SQLite 的天然串行化单元是「连接/事务」（WAL 单写者 + 每连接写锁）；架构文档却把串行化单元设计为「进程级互斥门」。两者叠加时，谁先拿门、谁先拿库锁的顺序不固定 → 锁序反转死锁；而**正确的单库串行化应落在连接/事务层，门只做"等待有界"的护栏，不与库锁争抢**。

---

## 四、正确架构方向（替代 R1 的进程级门）

| 方案 | 说明 | 消除缺陷 |
|------|------|----------|
| **A. 单连接单写者** | 对每个 SQLite 库收敛到**单一连接/单一写通道**（如池化单写连接），WAL 单写者天然串行；门仅限 `WaitAsync` 有界 | B（无两把锁争抢）、D、E |
| **B. 逻辑写=单事务=单连接** | 一次节点持久化（NodeOutput+KeyMapping+节点库+引用）收敛为**同一事务/同一连接**，一次获取一个写锁 | D、E（消灭多连接自竞争） |
| **C. Outbox 与主循环同写通道** | Outbox writer 与主循环写同一 agent.db 时，共用**同一连接写通道**（非进程级门），按事务排队 | C（并发写者协调于连接层） |
| **护栏** | 连接层之上保留**有界等待 + 取消令牌 + 单步/单轮超时**（P3），超时即取消而非放弃 | B 的兜底 |

> 共通点：**串行化单元从「进程门」下沉到「连接/事务」，门退化为有界护栏**——这才能与 SQLite 自身的锁模型一致，杜绝锁序反转。

---

## 五、与既有治理（clinerules）的偏差修正建议

| 既有规则 | 文档 R1 落地 | 修正方向 |
|----------|--------------|----------|
| `SEP.DeterministicCoreDisjoint` | 单一 `WriteCoordinator` 进程门 | 改为「**单库单写连接/单事务写面**」；门仅作有界护栏 |
| `Strategy.SingleImplementation` | 写路径单一注册 `AssertSingleWriter` | 保持"单一实现"精神，但判定单元 = **写通道（连接/事务）**，非进程门 |

> 注：`.clinerules/axioms.md` 已补的 `enf` 标注与 `Check-SingleWriter.ps1` 当前仍引用已回退的 `WriteCoordinator`，**处于不一致状态**，需在确定正确方向后同步修正（本次未改，遵"先分析"指示）。

---

## 六、当前工作区状态（已回退，待定方向）
| 项 | 状态 |
|----|------|
| P1 Outbox 解耦 | ✅ 保留（正确根治 D2） |
| P3 单轮 linked-CTS 取消 | ✅ 保留 |
| P5 StallDetector / P2 UiDispatchGuard 机制 | ✅ 保留 |
| P0 单一写门统一化 | ⛔ **已回退**（`SqliteWriteGate` 恢复、`WriteCoordinator` 删除）——避免锁序反转级联 |
| `.clinerules` enf / `Check-SingleWriter.ps1` | ⚠️ 引用已回退的 `WriteCoordinator`，待按正确方向（§四）重写 |

---

## 七、待决事项与后续路线
1. **选择 §四 的正确串行化方案**：A（单连接单写者）/ B（逻辑写=单事务=单连接）/ C（Outbox 与主循环同写通道）。
2. **P2（UI 线程）**：日志仍见 `CoreWebView2 can only be accessed from the UI thread`，按项目既有门槛留待**运行时取证**后迁移。
3. **测试环境问题**（非本次改动）：`cline_execute 函数未找到`（函数注册/测试配置）；`WorkflowRunner_historyPersist_failed`（`agent_log_messages.json` 缺 msgId 定义）。
4. 修正 `.clinerules` enf 标注与 `Check-SingleWriter.ps1`，使其与最终串行化设计一致。

---

*本文档基于两条运行日志（23:19 / 23:27）实证与存储层/Agent 源码结构整理，用于在落地前对齐串行化架构的正确方向。*
