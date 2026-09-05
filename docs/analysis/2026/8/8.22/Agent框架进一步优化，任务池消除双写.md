# Agent 框架进一步优化：任务池消除双写（阶段 1-3 交付报告与双写路径分析）

> **校验记录（2026-08-22 依据当前代码重新核对）**
>
> 本文档以 `Projects/Agent` 当前工作树代码为准重新核对了全部代码行号与论断，并与同目录>   
> 《工作流调用链重构与UniversalReference联动方案.md》（下称"方案文档"）交叉验证。核对结论：
>
> - **整体成立**：阶段 1/2/3 的改动描述、3 组双写路径、"为什么是问题"的风险表，与代码一致。
> - **修正 4 处**（正文已就地改正，此处列出以便快速核对）：
>   1. `guard_output` 的 `ResultKeys` 实为 `["safe_output"]`（`["output"]` 是其 `ParameterKeys`），>        
>      `WorkflowRunner.cs:74`；相应地，实际被"写两遍"的端口是 `guard_output.safe_output` 与>        
>      `cline_execute.exec_result`（`guard_output.output` 只在终态信封写一次，不构成双写）。
>   2. 终态信封写入位置：`RunRoundAsync:216-229`（`WriteNodeOutputAsync` 调用在 `:218-223`），非原写的 186-201。
>   3. 任务池载荷列名：ur_tasks 表的载荷列为 `SnapshotJson`（`SqliteTaskPoolStore.cs:53`）；>        
>      "单条 PayloadJson 即为唯一权威状态基座"是 `AgentTaskItem` 类注释的用语（`AgentTaskItem.cs:21-22`），二者并存，正文已区分。
>   4. `ConversationSendAsync` 的 `CancellationToken.None` 在 `:38`（`:37` 为 try 起始行）。
> - **保留为历史快照**：三个 commit 哈希、测试通过数（137/137、DslMethod 4/5）为实施当时报告数据，本次未复跑验证。



---

## 一、交付报告：彻底工作流重构（节点化主线，3 个阶段全部落地 + 验证通过）

以"优先彻底的工作流重构"为主线，已完成 **D1/D2/D4 的核心根治**，全部构建 0 警告 0 错误、测试通过。

| Commit      | 阶段             | 解决的问题                 | 改动（与代码核对）                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | 验证                                         |
| ----------- | -------------- | --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------ |
| `d63edac29` | 1 错误归一化基座      | **D4** 错误从字符串降级改为强类型  | `WorkflowRoundResult` 增加 `ErrorClass`/`ErrorStep`（`IWorkflowRunner.cs:33-43`）；`RoundPipelineContext` 增加错误信封字段 `Error/ErrorClass/ErrorStep`（`RoundPipelineContext.cs:42-49`）；`WorkflowRunner` 用 `ErrorClassifier` 归类替换裸 `Message`（`WorkflowRunner.cs:143-150,181-190`）；`ConversationPipeline`/`AgentUiLibraryConversation` 消费强类型归因（`ConversationPipeline.cs:141-148`、`AgentUiLibraryConversation.cs:47-49`）                                                                                                                                         | Agent 构建✅；ConversationPipelineTests 11/11✅ |
| `ecd0b77b9` | 节点化 Step1 横切挂钩 | **D2/D4** 持久化从步骤调用链解耦 | 启用 `StaticAsyncPipeline` 的 `onAfterStep`（`WorkflowRunner.cs:138-142`，横切持久化；失败由 `PersistStepAsync` 内部归类+归因日志、不翻转 ok，`RoundStepInvoker.cs:114-127`）/ `onStepError`（`WorkflowRunner.cs:143-150`，错误即时归类，语义由 `PipelineExecutor.cs:87-114` 保证）；`NodeExecutionService.ExecuteAndPersistAsync` 拆为 `ExecuteAsync`（纯函数，`NodeExecutionService.cs:145-169`）+ `PersistAsync`（落库，`NodeExecutionService.cs:177-190`）；`RoundStepInvoker` 新增 `PersistStepAsync`（`RoundStepInvoker.cs:108-128`）；`RoundPipelineContext` 暂存产物+溯源供横切读取（`RoundPipelineContext.cs:51-71`） | 新增 `RoundStepInvokerTests` 2/2✅            |
| `e06daa4a4` | 节点化 Step2 完成信封 | **D1** 会话状态不再悬置       | `ConversationPipeline.RunSignalAsync` 加完成信封（`ConversationPipeline.cs:100-136`、`FinalizeConversationStateAsync:230-245`）：无论成功/取消/异常都回写 `Round/Status/UpdatePayload/WriteStatus`（取消→Cancelled、异常→Faulted、成功→Completed，`ConversationPipeline.cs:237-241`），用兜底令牌避免被取消阻死（`:233`）                                                                                                                                                                                                                                                                        | 新增 2 条 D1 测试（取消/异常回写状态）✅                   |

**验证结果汇总**（实施当时数据，历史快照）：

- `Agent.sln` 构建：✅ 0 警告 0 错误
- `Agent.Core.Tests`：✅ **137/137 通过**（含阶段 1/2/3 新增的 5 条）
- `DslMethod.Tests`：4/5 通过；1 个失败 `Regenerate_与提交产物逐字节一致` 为**预存失败**（`AxiomCodeGenerator.cs:69` 扫描 prompts/axioms 时 `First()` 无匹配，与本次编排/错误改动无关；同文件其余 4 个扫描 `WorkflowRunner` 绑定的测试全通过，证明 `WorkflowRunner` 绑定未受破坏）

---

## 二、关键结论与后续路线

**发现两处"方案文档建议的 P0 项已在代码中落地"**，无需重复实施：

- **P0-3 预算对齐**已存在：`ClineAgentExecutor.cs:80-87` 已实现 `callTimeout = min(AgentLoopGuard.GetBudget(), ClineCallCap)`（5 分钟硬顶，`TimingDefaults.ClineCallCap` 单一来源）。
- **P0-2 取消透传**当前 `AgentUiLibraryConversation.ConversationSendAsync:38` 仍用 `CancellationToken.None`；但 GUI 命令注册签名（`Func<string,string,Task<string>>`，`AgentUiLibraryConversation.cs:93-94`）无 ct 参数，**透传需改命令框架**（中等风险，非纯小改）。

**剩余路线（建议下一步，按价值/风险排序）**：

1. **阶段 4 节点化 Step3**（`DynamicWorkflowMerger`（`Agent.Core/DynamicWorkflow/DynamicWorkflowMerger.cs`）恢复 `on_success/on_error` 图语义 + `session_complete` 显式完成信号）——方案文档标注为"架构优化，服务调用流程可查询"，是彻底重构的收尾，改面较大（涉 `RoundSpecLoader`/执行语义）。
2. **P0-2 取消透传**——需为 GUI 命令框架加 ct 通道，消除 `CancellationToken.None`。
3. **P0-1 daemon 回传断链**——方案文档标注的"根治点"，但为 `ClineDaemonIpc/ClineDaemonHost/DaemonPool`（`Agent.Modules/Cline/Pool`）+ TypeScript 传输层（`ClineApi`，`TypeScriptPrj/TsWorkspace/ClineApi`），属跨语言、更大改动，是端到端验收 `步骤完成` 打印的硬前提。
4. **D3 单一事实源收敛**——以 ur_nodes 持久态为唯一权威写（当前 `vars`/任务池仍双写），涉存储层与大量 vars 消费方，回归风险最高，建议单独评审后再动。

需要我继续推进哪一项？若继续，我建议从**阶段 4（on_error 图语义，纯 C#）**&#x6216; **P0-2（取消透传，但需先评估命令框架 ct 通道）**&#x5207;入。

读完整条状态链后，我可以给你一个基于代码行号的准确解释。

---

## 三、"任务池双写"是什么

**一句话**：同一轮会话/任务运行产生的状态，被**并行写进多个互不派生、无单一权威关系的持久化位置 + 内存**，且"先内存后落库"、事务只包局部——这就是设计文档 D3 违背 `StateRootSingleSource` 的实质。它不是"一处写了两次同样的字面值"，而是**同一逻辑状态存在两套平行的存储层次**。

具体有 **3 组写路径**：

### 1. 步骤产出：内存 vars ↔ ur_nodes（SQL）——"先内存后落库"

`NodeExecutionService` 里同一份步骤结果被写两处、有先后：

- `ExecuteAsync:166` → `vars.Set(key, v)`：先写**内存**（`StateBackedDataSpace` → `StateCenter.Transient` 层，`StateBackedDataSpace.cs:28-32`，轮级生命周期）
- `PersistAsync:182-186` → `_store.WriteOutputAsync(...)`：后写 **ur_nodes**（`NodeOutputStore` → 基础节点库 SQLite ur_nodes 表，`NodeType=output`，键 `nodes.{wf}.{nodeKey}`，`NodeOutputStore.cs:138-146`、`PersistenceHelper.cs:122-141`）

`vars` 是内存瞬态（StateCenter.Transient），ur_nodes 是落盘。进程在这两者之间崩溃 → 两处漂移。这正是方案文档说的 `:166 先于 :169/175`（方案文档第 46-48/59 行，指原 `ExecuteAndPersistAsync` 内 `vars.Set(:166)` → `WriteOutputAsync(:169-172)` → `PersistNodeMetadataAsync(:175)` 的先后序）。

### 2. 终态信封：ur_nodes 被"二次写"

`WorkflowRunner.RunRoundAsync:216-229` 在轮结束时把 `SafeOutput/Output/ExecResult` 又经 `WriteNodeOutputAsync` 写一遍 ur_nodes（`WorkflowRunner.cs:218-223`，代码注释明确"此处补终态信封"，`:214-215`）。而步骤体内（阶段 2 之后的 `PersistStepAsync`）已经写过这些结果键——因为主循环 `cline_execute` 的 `ResultKeys=["exec_result"]`（`Config/workflows/agent-loop.json:40`）、`guard_output` 的 `ResultKeys=["safe_output"]`（`ParameterKeys` 才是 `["output"]`，`WorkflowRunner.cs:74`）都含这些键。于是**同一节点端口被写两遍**（一次步骤横切、一次轮终信封）：

| 节点端口                        | 步骤横切（PersistStepAsync）      | 轮终信封（RunRoundAsync） | 是否双写       |
| --------------------------- | --------------------------- | ------------------- | ---------- |
| `cline_execute.exec_result` | ✅（ResultKeys 含 exec_result） | ✅ `:222-223`        | **是**      |
| `guard_output.safe_output`  | ✅（ResultKeys 含 safe_output） | ✅ `:218-219`        | **是**      |
| `guard_output.output`       | ❌（非 ResultKeys）             | ✅ `:220-221`        | 否（仅终信封写一次） |

> 注：`guard_output` 步骤体执行时先把 `output` 键设为终答文本（`WorkflowRunner.cs:126`），但该键不在其 ResultKeys 内，故横切持久化不会写 `output` 端口——只有 `safe_output` 端口被双写。

### 3. 会话/任务状态：任务池快照 ↔ ur_nodes ↔ state.Data（两套平行的持久化）

- **任务池快照 `AgentTaskItem`（ur_tasks 表，载荷列 `SnapshotJson`）**：`ConversationPipeline.RunSignalAsync` 经 `_store.UpdatePayloadAsync/WriteStatusAsync`（`IConversationStore` → `AgentTaskPoolAdapter`，`AgentTaskPool.cs:32,319-320,328-329` → `_persistence.UpdatePayloadAsync`）写入 `Round/Status/__conversation/__backend_audit`。`AgentTaskItem` 类注释自述"单条 PayloadJson 即为唯一权威状态基座"（`AgentTaskItem.cs:21-22`；`SqliteTaskPoolStore` 注释"SnapshotJson 即唯一权威状态字符串"，`SqliteTaskPoolStore.cs:20-22`）。
- **ur_nodes（节点库）+ vars**：步骤产出以"节点为基底"落另一套表。
- **state.Data（AxiomContext.Data）**：`RoundStateMapper.Harvest:108-133` 又把 vars 收割进跨轮状态（`RoundStateMapper.cs:120-130` 写 ExecResult/NextPromptHint/StepCount/Goal/TaskId/SessionId/History/UserInput/Cwd/DataDir/Prompt）。

会话任务的**对话归档**（任务池 `__conversation`/`__backend_audit`，`ConversationPipeline.cs:31-34`）与**步骤产出**（ur_nodes 按节点）是两套各自独立、互不派生的持久化。`ConversationPipeline` 从 `result.Outputs`（vars 快照）提取 stages 回写 `__conversation`（`ConversationPipeline.cs:117,264-283`），而产出又同时落在 ur_nodes——同一个值在这两套体系各有一份。

---

## 四、为什么是问题（风险）

| 维度        | 现状                                                                                                                                                                                                                 |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **单一事实源** | 步骤结果同时存 vars、ur_nodes、state.Data；会话状态存任务池快照——任一处都不是唯一权威，读方可从任意一处读到过期/不一致值                                                                                                                                          |
| **事务边界**  | SQL 事务仅包 KeyMapping（`PersistKeyMappingsAsync`，`NodeExecutionService.cs:207-225`），ur_nodes 写、图边写、任务池写各自独立，跨介质只能靠 `ReferenceGrounding` 对账兜底最终一致（`ReferenceGrounding.ReconcileAsync`，`ReferenceGrounding.cs:252-280`） |
| **一致性窗口** | "先内存后落库"：崩溃/取消时内存有而磁盘无（或反之）                                                                                                                                                                                        |

---

## 五、这与我阶段 2/3 的关系

阶段 2 做的是把"函数执行"与"落库"在**代码结构上解耦**（`ExecuteAsync` 纯函数 / `PersistAsync` 落库、失败不翻转 ok），阶段 3 是完成信封——它们缓解了 D2/D1，但**没有消除双写本身**（双写仍在：vars 写 + ur_nodes 写 + 任务池写）。"D3 单一事实源收敛：当前 vars/任务池仍双写"说的就是上述 3 组写路径未收敛。

**彻底消除 D3** 是更高风险的存储层重构（要选定单一权威写、其余改派生读，并动到大量 vars/任务池消费方），列为单独评审后再动的最后一项。收敛思路参考方案文档第 142 行："以 ur_nodes 持久态为唯一权威写，内存 vars / 任务池存储只做派生读，消除双写歧义"。要我现在评估具体怎么收敛（例如以 ur_nodes 为唯一权威、vars 只做执行期派生读、会话快照从 ur_nodes 推导）吗？还是先继续其他阶段？
