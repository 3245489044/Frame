# LLM 调用落库与跨任务转接分析

> **定位**：本文基于《Agent架构集中介绍.md》与当前代码，回答两个问题：**LLM 调用是怎么落到数据库里的**（端到端 + 落库机制 + 多轮参数传递 + 节点复用），以及**能否实现「框架用 Cline 取数落节点 → Answer 循环分析 → 再交给 Cline 执行」的纯转接流水线**。
> **状态**：与 2026-08-19 代码同步。落库路径为既有能力描述；跨任务转接为可行性评估（含一处待补的"消费点"）。

---

## 一、LLM 调用怎么落到数据库

### 1.1 核心结论

**LLM 的「调用本体」本身不落库**——它只在内存里流转并返回 `ClineResult`。落库的是循环骨架收敛后的**三类产物**，经 3 个持久化端口分布到两种数据库：

| 落库物 | 端口 | 物理位置 | 权威性 |
|--------|------|----------|--------|
| 每轮快照（任务=状态基座） | `ITaskPoolPersistence` | SQLite `ur_tasks.PayloadJson` | 唯一 truth |
| 每轮结果（decision/rounds/duration） | `IZoneStore` | SQLite `ur_nodes` NodeType=Output | 输出区 |
| 运行事件（决策词/token 等） | `IObservabilitySink` | SQLite `ur_nodes` NodeType=Event | 事件区 |
| 确定性产物（文件/分析结论） | `IResourceReferenceStore` | LiteDB `ur_endpoints`/`ur_references` | 可重建投影 |

### 1.2 端到端路径

**① LLM 调用本体（内存）**
`cline_execute` 执行链（`ClineExecutorFunction.ExecuteClineAsync`）：
1. `CheckLlmGateAsync`（任务池暂停门，仅阻塞不落库）
2. `ResolveExecutor` → `ClineAgentExecutor.ExecuteAsync(request)`
3. 底部经 `GlobalLlmGateFilter.CheckGate` 裁决并发/用量后，由 SK `kernel.InvokeAsync` 经 `LlmClientFactory`（单一注册表）发请求。

返回的 `ClineResult` 只作为内存结果回到骨架，直接落库不在此处。

**② 结果回流骨架 → 收敛为 AxiomLoopResult**
`ClineResult` → `rule_match` → `guard_output` → `RoundLifecycle` 收束 → `AxiomLoopResult`（`FinalDecision`/`Rounds`/`TotalDuration`）。之后在**每轮 next 边界 + 终态**由框架统一落库——「写回数据库 = 框架决定，非 AI 决定」。

**③ 三条落库路径**

| 路径 | 代码链 | 落点 |
|------|--------|------|
| A 状态基座 | `AgentRunner.PersistRuntimeAsync` → `hooks.TaskPoolPersistence` → `SqliteTaskPoolStore.UpdatePayloadAsync`（序列化 `AgentTaskItem` 含 LastResultText/token/budget/策略为 `SnapshotJson`） | `ur_tasks` |
| B 每轮结果 | `AgentTaskPool`/`ReactiveTrigger` → `OutputZoneWriter`（载荷仅 decision/rounds/durationMs，不含原始文本）→ `NodeZoneStore.AppendOutputAsync` → `NodeRecord{NodeType=Output}` | `ur_nodes` |
| C 运行事件 | `IObservabilitySink.Emit(RunEvent)` → `EventSink`（有界通道+后台批写）→ `NodeZoneStore.AppendEventAsync` | `ur_nodes` NodeType=Event |
| D 确定性产物 | `Invoke-AgentStep store.resource` → `PsRuntimeBinding` → StoreBridge → `ReferenceGraphCoordinator` | LiteDB 端点/边 |

> 观察：LLM 原始生成（prompt 文本/返回文本）不直接落表；真正落库的是①快照（含最终文本）②每轮指标 ③运行事件。三库各司其职、规避双持久根。

---

## 二、落库机制细节

### 2.1 SQL 写机制（纯「键值 + 一个 JSON」）

`ur_nodes` 三列表，元数据全在 Json 内：
- DDL：`[Id] INTEGER PK AUTOINCREMENT, [NodeKey] TEXT UNIQUE, [Json] TEXT`；`NodeType`/`ThreadId` 靠 JSON 表达式索引 `json_extract([Json],'$.NodeType')` + `[Id]` 过滤，非物理列。
- `PutAsync`：`NodeRecord` → 序列化 `NodeDoc`（NodeType/ThreadId/Key/Status/PayloadJson/RelationsJson/CreatedAt/UpdatedAt）→ `INSERT INTO ur_nodes (NodeKey, Json) ON CONFLICT(NodeKey) DO UPDATE SET Json=json_set(...保留CreatedAt)`。即 NodeKey 幂等 upsert。

`ur_tasks` 同理：`[Id],[ThreadId],[SessionId],[SnapshotJson]`；`UpdatePayloadAsync` 用 `UPDATE ... SnapshotJson=json_patch(...)` 保留 Status/CreatedAt。

> 一句话：落库 = 对象序列化成 Json → INSERT/UPDATE 幂等 upsert，NodeKey（任务用 ThreadId+SessionId）作唯一键。

### 2.2 SQL 节点类型

`NodeTypes` 判别集：`node / input / output / event / checkpoint / resource / config / keymapping / relation`。

| 落库物 | 位置 | NodeType |
|--------|------|----------|
| 任务快照 | `ur_tasks.PayloadJson` | （不在 ur_nodes，独立表） |
| 每轮结果 | `ur_nodes` | `output` |
| 步骤产物（ExecResult/Output/SafeOutput 经 `WriteNodeOutputAsync`） | `ur_nodes` | `output`（节点即存储单元） |
| 运行事件 | `ur_nodes` | `event` |
| 配置 / 关系声明 | `ur_nodes` | `config` / `relation` |

> LLM 路径实际只会产出 `output` + `event` 两类节点。

---

## 三、多轮对话参数传递

以 **`sessionId` 为 scope**，跨轮参数经 `AxiomContext.Data`（每轮根快照）携带：

1. `LeaseDeriver.Derive` 从 `Data[SessionId/Goal/Constraints/PlanSummary/LastResult/NextHint/Cwd/StepCount]` 派发租约。
2. `cline_execute` 读 `context.Parameters[RoundKeys.SessionId]` → `AgentExecutorRequest.SessionId` → `ClineAgentExecutor` 写 `ClineRunOptions.SessionId` → `ClineClient.ResumeAsync(sessionId,…)` 发 `"resume"` 命令**恢复 Cline/LLM 侧会话历史**（多轮对话真正承接处）。
3. 每轮 harvest 后回灌真实 sessionId：`state.Data[RoundKeys.SessionId]=sid`，供下一轮 resume 复用。
4. 持久化闭环：这些 `Data` 字段随快照每轮 `next` 边界落 `ur_tasks.PayloadJson`，下轮 `LoadState` 恢复。

> 多轮参数 = 显式 `Data` 携带 + 落库；LLM 侧历史 = `sessionId` 恢复。

---

## 四、循环调度中如何复用已落库节点

三种复用机制并存：

| 机制 | 说明 |
|------|------|
| 任务/快照复用 | `LoadState` 从 `ur_tasks.SnapshotJson` 恢复 `AgentTaskItem`（含全部跨轮 Data），每轮边界 `PersistRuntimeAsync` 写回——多轮/重启时唯一状态根的恢复入口 |
| 会话历史复用 | `sessionId` → `"resume"` 恢复 Cline/LLM daemon 侧对话历史（内存态，非 SQL 查询） |
| 节点/输出复用 | `WorkflowRunner.GetNodeOutputsAsync(sessionId)` → `ReadNodeOutputsAsync` → `StorageBridgeCommands` 按 `threadId/roundId` 读 `ur_inputs/ur_outputs/ur_events`、按 `nodeKey` 读 `ur_nodes`；`NodeZoneStore.Query` 用 NodeType + ThreadId + afterId 游标分页增量读取 |

> LLM 门（`CheckLlmGateAsync`）只做暂停阻塞，不读库。

---

## 五、能否实现「Cline 取数 → Answer 分析 → Cline 执行」纯转接流水线

**目标**：框架先用 Cline 获取数据并存储为节点 → 节点数据输入 Answer 循环分析处理 → 再输入 Cline 执行；全程由 LLM 任务管理编排，**转接层不读任何节点上下文、只做数据/引用转接**。

### 5.1 环节能力清单

| 环节 | 现状 | 承载 |
|------|------|------|
| Cline 取数 → 落节点 | ✅ 已支持 | `store.resource`/`read.file -Persist` → `WriteNodeOutputAsync`/`OutputZoneWriter` 落 `ur_nodes`(output) |
| 节点数据 → Answer 循环 | ✅ 循环本体已支持 | `StatelessAnswerLoop`（Question→UserInput，MaxRounds 预算多轮精化，上轮 `prev_answer` 回写），`AnswerTaskSpec` 参数随任务池 `PayloadJson`，`enqueue` 带 `ExecutorId=stateless-answer` |
| Answer 结果 → Cline 再执行 | ✅ 执行器可插拔 | `ResolveExecutor` 按 `RoundKeys.Executor`/`ExplicitExecutor` 切换，Cline 只是其中一种 |
| 把节点引用挂到 task | ✅ 已支持 | `enqueue relatedNodes` → `ReconcileTaskAsync` 建 `task→node` 引用边 |
| 自动把引用节点内容喂给该任务 LLM | ❌ 缺消费点 | 见 5.2 |

### 5.2 关键结论：挂引用现成可用，只缺「消费点」

框架**已经支持把节点引用挂到 task**：

- `RunRequest.RelatedNodes`（注释「task op=enqueue 专用，声明关联 nodeKey 列表」）。
- 入队后写透守卫：`maintenance.ReconcileTaskAsync(...)` 注册 task 端点、node 端点、`AddReferenceAsync("node_to_node")` 写 `task→node` 边——**建引用边、不读 payload，正是"只转接不读上下文"**。

查证结果：`relatedNodes`/`ReconcileTaskAsync` 目前**只负责建引用边（记录 + 图可达 + `GetRelatedNodesAsync` 可检索）**，没有发现 StatelessAnswerLoop / runLoop 在启动时**自动把 relatedNodes 指向的节点 payload load 成自己的 `UserInput`/`Question`**。

因此真正要补的是一处**消费点**：

```
Cline#1 写节点A ──enqueue relatedNodes=[A]──▶ Answer任务（引用=仅转 nodeKey）
                                                │  ←缺：装配时把节点A payload load 成 Question
                                             Answer 循环
                                                │
Cline#2 ◀── (Answer 把结果落节点B，再 enqueue relatedNodes=[B])
```

- **转接层**：不读任何节点内容，只随 enqueue 传 `nodeKey` 引用 —— 已满足"只转接"。
- **消费层**：唯一读 payload 的点是「把引用节点装配成下游 LLM 输入」，即从 Cline 获得的数据流入 Answer 的地方。

### 5.3 实施建议

- 最小增补 = 在 Answer/runLoop 输入装配点加「按 `relatedNodes` 把节点内容读进 `UserInput`/`Question`」的消费桥，约等于把 `GetNodeOutputsAsync(sessionId)` 的一个节点 payload 填入本轮输入。
- 转接方向可选：引用转接（推荐，最贴合"只转接不读上下文"）/ 写即触发串联（ReactiveTrigger 扩展，触发层需读一次节点转写）/ 外部管道编排（PS/C# 布局层按 action 顺序显式调用，改动最小但编排外置）。

---

## 六、总结

- **落库** = `NodeRecord`/`AgentTaskItem` 序列化成 Json 按 NodeKey 幂等 upsert；LLM 相关只落 `ur_nodes` 的 `output`/`event` 两类节点 + `ur_tasks` 快照。
- **多轮** = `sessionId` 走 `Data`→`resume` 恢复 + 显式 `Data` 携带跨轮字段；落 `ur_tasks.PayloadJson`，下轮 `LoadState` 恢复。
- **复用** = `LoadState` 恢复快照、`resume` 恢复会话、`GetNodeOutputsAsync` 游标读已落节点。
- **跨任务流水线**：Cline 取数落节点 / Answer 循环 / Cline 再执行 / 节点引用挂 task 均现成可用；唯一缺口是「按 relatedNodes 把引用节点内容装配成下游 LLM 输入」的消费点，补齐后 Cline→Answer→Cline 的纯转接流水线即可闭环。