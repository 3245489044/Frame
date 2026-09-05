# 修复进度

## P0: 严重 Bug（已完成）
- [x] 修复 StateStoreExtensions.TryGet 逻辑错误
- [x] 修复 VirtualStoreExtensions.TryGetValue 逻辑错误
- [x] 修复 StateStoreExtensions.GetOrSet 类型转换丢失

## P1: 重复逻辑合并（进行中）
- [x] 删除 ObjectStoreAdapter.cs（整个文件）
- [x] 从 IOrchestrationAbstractions.cs 删除 IObjectStore 接口
- [x] 优化 InMemoryStateStore.GetAll 消除两次字典查找
- [x] 优化 VirtualStore.GetAllValues 消除两次字典查找
- [ ] 合并 IVirtualStore 与 IStateStore 接口（添加过时标记引导迁移）
- [ ] 合并 VirtualStoreExtensions 与 StateStoreExtensions 重复方法

## P2: 逻辑缺陷修复
- [x] 修复 ExecutionStateMachine.TryTransitionTo 时间戳更新
- [x] 修复 ExecutionStateStore.GetOrCreate 竞态条件
- [x] 优化 ObservableStateStore.RemoveByPrefix 批量事件
- [x] 提取 NodeExecutionCoordinator 重复代码
- [x] 删除 InMemoryStateStore.GetAllKeysList 死代码

## P3: 冗余清理
- [x] 删除 IStateQuery（无实现）
- [x] 删除 IRetryPolicy/RetryPolicy（归属不当）
- [x] 清理 ExecutionStateStore IDisposable 过度设计
- [x] 清理 NodeExecutionCoordinator IDisposable 过度设计
- [x] 删除 StateManager 未使用的 StateChanged 事件

## P4: 从 OrchestrationStages.cs 提取通用逻辑到 Share.StateManagement（已完成）
- [x] 创建 IExecutionParameterStore 接口（提取自 OrchestrationExecutionStage 的参数管理逻辑）
- [x] 创建 ExecutionParameterStore 实现（ConcurrentDictionary 参数存储 + 线程安全）
- [x] 创建 IErrorHandler 接口（提取自 OrchestrationErrorHandlingStage 的错误处理模式）
- [x] 创建 IMetricsCollector 接口（提取自 OrchestrationMetricsStage 的指标收集模式）
- [x] 创建 IWorkflowAdapter 接口（提取自 OrchestrationAdapterStage 的适配器模式）
- [x] 创建 IConfigProvider 接口（提取自 OrchestrationConfigStage 的配置提供模式）
- [x] 创建 ErrorContext 模型（提取自 OrchestrationStages 的数据模型）
- [x] 创建 RetryPolicy 模型（提取自 OrchestrationStages 的数据模型）
- [x] 创建 NodeExecutionRecord 模型（提取自 OrchestrationStages 的数据模型）
- [x] 增强 ExecutionExtensions.cs（批量操作 + 参数管理 + 节点协调扩展方法）

## P5: 通用任务池（状态机 canonical，SingleImplementation / TH-12）

> 目标：在 Share 层构建"写任务池即激活 AI 循环、AI 循环可控暂停/继续"的通用状态机组件，
> Agent / Operation 只引用不重复实现。依赖方向：Share → Operation(DataStoreDriven) → Agent.Core → Agent.Launcher。

### 已落地（`Share\TaskStatusPool\TaskPool\`，编译 0 警告 0 错误）
- [x] `ITaskPoolItem`：任务载荷契约（TaskKey 幂等键 / Priority / MaxRetries）
- [x] `TaskPoolEntry<TItem>`：持久化条目（载荷 + 状态 + 重试计数）
- [x] `ITaskPoolPersistence<TItem>`：持久化端口（不耦合 SQLite/LiteDB/Local）
- [x] `InMemoryTaskPoolPersistence<TItem>`：内存占位实现（测试/无后端引导）
- [x] `TaskPoolOptions`：并发/批次/队列暂停轮询配置（L3）
- [x] `TaskPoolStateChangedEventArgs`：状态变更事件（写即触发观测）
- [x] `TaskExecutionStatus` + `TaskStateMachine` + `TaskStateStore` + `TaskPauseHandle` + `TaskPauseManager`：任务生命周期状态枚举 + 自包含状态机原语（通用基础框架内自包含，不依赖 Modules.StateMachine / Share.StateManagement）
- [x] `ITaskPool<TItem>` + `TaskPool<TItem,TResult>`：
  组合 TaskStateMachine/TaskStateStore + TaskPauseHandle(TCS) + TaskPauseManager + SemaphoreSlim 并发闸门；
  写入即激活（Enqueue → NotStarted + 唤醒消费循环）；暂停为循环内非终止（Running ⇄ Paused）；
  双源收敛（Loop 主动 / External 强制）；队列级全局暂停；重启回填 ProcessPendingAsync（Running→NotStarted 重放，Paused 保持挂起）
- [x] **通用化迁移（2026-08-13）**：任务池由 `Share.StateManagement.TaskPool` 迁入 `Share.TaskStatusPool.TaskPool`（与 Agent 无关的通用基础框架）。
     状态原语自包含（TaskExecutionStatus/TaskStateMachine/TaskStateStore/TaskPauseHandle/TaskPauseManager），枚举 `ExecutionStatus` → `TaskExecutionStatus`；
     消费者（DataStoreDriven/Agent.Core/Agent.Launcher/测试）改指新命名空间；旧目录已删除。

### 测试（`CentralTest\...\Share.Tsets\StateManagement.Tests`，7/7 通过）
- [x] Enqueue→Running→Completed 激活闭环
- [x] Priority 高优先先出队
- [x] 排队暂停（NotStarted→Paused→NotStarted 重新可出队）
- [x] 在途暂停（Running→Paused→Resume→Completed，合作式 WaitIfPausedAsync 挂起）
- [x] 失败重试（重试后 Completed）
- [x] 取消（Cancelled，ct 传导）
- [x] ProcessPending 重启回填（死 Running → NotStarted 重放）

### 已落地（Agent 应用层，2026-08-12，编译 0 警告 0 错误）
- [x] `SqliteTaskPoolStore<TItem>`（`ur_tasks` 表，实现 `Share.TaskStatusPool.TaskPool.ITaskPoolPersistence`，随 `SqliteSession`/`IDbCommandExecutor` 复用；
      `DataStoreDriven` 新增对 `Share.TaskStatusPool` 引用；`TaskPoolStoreFactory` 按后端解析 Sqlite / 内存占位）
- [x] `AgentTaskPoolAdapter`（`Agent.Core/TaskPool`：Share/TaskStatusPool 层通用 TaskPool ⇄ `AgentOrchestrator.RunAsync` 接线；复用 IZoneStore/ReactiveZoneBridge 写回 ur_outputs；
      任务键贯通轮间暂停）
- [x] `AgentRunner` 异步轮间 `RoundPauseCheck`（仿 SetCancellationCheck：`SetRoundPauseCheck` + 每轮起始 `WaitIfPausedAsync` 合作式挂起 Running ⇄ Paused；
      任务键经 `AgentRunCancellation.SetTaskKey/GetTaskKey` Functor 透传；缺省 null = no-op 行为保持）
- [x] 装配：`DataStoreHosting.RegisterTaskPool`（组合根可开关可回退；对外门面 = `AgentTaskPoolAdapter`）
- [x] 测试：`SqliteTaskPoolStoreTests` + `TaskPoolWithSqliteStoreTests`（自动执行 / 全局暂停 / 重启回填；StateManagement.Tests 40/40 通过）
- [x] 自检脚本：`PowerShellScript/TaskPool/Test-AgentTaskPool.ps1`
- [ ] clinerules 对齐（SchedulerAction.PauseRun / ur_tasks 参数 / SingleImplementation 收敛）——待 references.md 重编译

### 待办（Agent 应用层，见实施计划 P2–P6）
- [x] SqliteTaskPoolStore（`ur_tasks` 表，实现 ITaskPoolPersistence，挂 PersistenceProviderFactory）
- [x] AgentTaskPoolAdapter（复用 ReactiveZoneBridge，handler = AgentOrchestrator.RunAsync）
- [x] AgentRunner 注入异步轮间 RoundPauseCheck（仿 SetCancellationCheck）
- [x] AgentUi_* 门面 + AgentRuntimeWorkflow + 自检脚本
- [ ] clinerules 对齐（SchedulerAction.PauseRun / ur_tasks 参数 / SingleImplementation 收敛）

- [x] 更新 OrchestrationStateManager.cs（添加 ExecutionParameterStore 支持）
- [x] 更新 OrchestrationStages.cs（DefaultConfigProvider 实现 Share.StateManagement.IConfigProvider）
- [x] 构建验证通过（StateManagement + WorkFlowMethod 均 0 Error）

## P6: 统一上下文状态原语 + 纯裁决器 canonical（SingleImplementation / TH-12）

> 目标：把 WorkFlowMethod.LoopFramework.Axioms 中"统一上下文（FailureBudget/ProgressToken/ControlFlag/ILoopState）+ 纯裁决器
> （ProgressUpdater/BudgetEscalator/TerminationDecider/SyncProtocol）+ 循环决策域（LoopDecision/EvalOutcome/ErrorClass）"
> 从 Operation 层上移到 Share 层唯一权威，消除重复实现（对齐 clinerules axioms §2/§3/§4/§5）。

### 已落地（`Share\StateManagement\Loops\`，编译验证通过）
- [x] `FailureBudget` / `ProgressToken` / `ControlFlag`（统一上下文三结构）
- [x] `ILoopState`（循环状态契约：算法层与状态宿主分离）
- [x] `EvalOutcome` / `LoopDecision` / `ErrorClass`（评估/决策/错误分类域）
- [x] `ProgressUpdater`（EvaluateDrivenUpdate）、`BudgetEscalator`（FailureBudgetEscalation）、
      `TerminationDecider`（TerminationPriority）、`SyncProtocol`（SyncProtocolTriple Commit→Compensate→Observe）
- [x] WorkFlowMethod 删除原实现，经 `global using` 类型别名（WorkFlowMethod + Agent + CentralTest 消费项目）保持既有引用兼容
- [x] 构建验证：StateManagement / WorkFlowMethod / Agent.sln / Operation.csproj 均 0 Error

### 保留在 Operation（耦合错误动作域，不提取）
- [ ] `Scope` / `Action` / `ErrorClassifier` / `RoundLifecycle` / `AxiomContext` / `WorkflowLoopState` /
      `AxiomLoopEngine` / `Strategy`（后续可评估 ErrorClassifier/RoundLifecycle 是否再上移）

### P0 已完成（2026-08-12）
- [x] `NodeProcess.INodeExecutionCoordinator` 与 Share 合并：`WorkFlowMethod.Skeletons.NodeProcess.Abstractions.INodeExecutionCoordinator`
      为死代码（全库无实现/无引用），已删除；实际使用侧（NodeExecutionStage / NodeProcessPipelineBuilder / StageBridge）
      均为 `Share.StateManagement.INodeExecutionCoordinator`，Share 接口即唯一权威（TH-12/TH-13）。
- [x] 执行状态枚举收敛：删除死代码子系统 `WorkflowExecution\Runtime\RuntimeContext.cs` + `RuntimeContextManager.cs`
      （含 `RuntimeExecutionStatus`，全库零引用，被更新 `Context\Runtime\WorkflowRuntimeContext` 取代）。
      四套枚举收敛为一套 `Share.ExecutionStatus` + 两套**保留**的节点/循环级枚举：
      - `Share.ExecutionStatus`（执行生命周期，唯一权威）
      - `LoopState`（循环级，含 Deciding/MaxIterationsReached，语义不同，保留）
      - `NodeStatus`（节点级，含 Skipped/Pending，语义不同，保留）
- [x] 构建验证：StateManagement / WorkFlowMethod / Operation.csproj / Agent.sln 均 0 Error

### P2 已完成（2026-08-12）
- [x] `ExecutionSession` 节点状态泛化为 `Share.TaskExecutionState<TKey,TStatus>`（子任务级状态追踪，TH-12 单一实现）：
      新增 `Share\StateManagement\Core\TaskExecutionState.cs`（线程安全状态转移 + 起止时间 + 错误消息 + 执行计数）；
      `NodeExecutionState` 改为继承 `TaskExecutionState<string, NodeStatus>`，注入节点枚举并保留既有 API（Status/NodeKey/ErrorMessage…）。
- [x] `SnapshotDrivenLoopEngine` 与 `Share.TaskPool` 收敛：把轮次式快照循环引擎（`SnapshotDrivenLoopEngine`/`SnapshotLoopRound`/`SnapshotLoopResult`）
      上移为 Share 层循环机制 canonical，与条目式 `TaskPool` 同层（round 式 vs item 式，语义不同不合并为一类型）。
      生产消费方（AgentRunner）+ 测试（SnapshotLoopEngineTests/HybridSchedulerCheck）经 `using Share.StateManagement;` 重指。
- [x] **通用化迁移（2026-08-13）**：`SnapshotDrivenLoopEngine` 由 `Share.StateManagement.Loops` 迁入 `Share.TaskStatusPool.Loops`
      （与 Agent 无关的通用基础框架），与条目式 `TaskPool` 同库同层。因 `TaskStatusPool → EventBindingOrchestrator → CommandFramework → StateManagement`
      存在既有引用链，`LoopDecision`（StateManagement.Loops 统一上下文原语）无法迁入 TaskStatusPool（否则 StateManagement→TaskStatusPool 成环），
      故引擎改为**决策类型无关**：`budgetTerminate` 由返回 `LoopDecision` 改为返回 `bool`，`SnapshotLoopResult.FinalDecision` 改为 `TerminatedByBudget`；
      具体决策值（TS/TO/TM）由消费方谓词经闭包记录（AgentRunner/HybridSchedulerCheck 已适配），行为等价。
      消费方（AgentRunner/Agent.Core.Tests/HybridSchedulerCheck）经 `using Share.TaskStatusPool.Loops;` 重指。
- [x] **统一上下文原语整体下沉（2026-08-13）**：Share.StateManagement.Loops 全块（LoopStatePrimitives/LoopDeciders/SyncProtocol）迁入 Share.TaskStatusPool.Loops，
      StateManagement 不再含 Loops 命名空间。无环依据：StateManagement 在 Loops 之外不使用其中任何类型（全库 grep）→ 整块迁出后无需引用 TaskStatusPool，
      不构成 StateManagement→TaskStatusPool 环；TaskStatusPool → EventBindingOrchestrator → CommandFramework → StateManagement 单向链不变。
      决策点：引擎回归 LoopDecision（决策域与引擎同库）——budgetTerminate 恢复返回 LoopDecision，SnapshotLoopResult 恢复 FinalDecision，
      AgentRunner/HybridSchedulerCheck 撤销闭包适配直接消费引擎承载决策值。TaskStatusPool 成为完整通用循环框架库（统一上下文原语+决策域+裁决器+引擎+条目式 TaskPool）。
- [x] 消费面重指：全局别名 GlobalUsings.cs（Agent/src + WorkFlowMethod + CentralTest 5 份）Share.StateManagement.Loops.* → Share.TaskStatusPool.Loops.*；
      直接 using OptimizationRegressionTests.cs 改指；补 TaskStatusPool 引用（WorkFlowMethod/Agent.Axioms/Agent.Calculate/Agent.Contract/Agent.Persistence/Agent.State/
      Agent.ClineUsage.Tests/Agent.Persistence.Tests；Agent.Core/Agent.Launcher 已有；Boundary.Tests/Agent.Core.Tests/HybridSchedulerCheck 经 Agent.Core 传递）。
- [x] 构建验证：StateManagement / WorkFlowMethod / Operation.csproj / Agent.sln 均 0 Error；
      测试通过：HybridSchedulerCheck ALL PASS（含 FinalDecision=TO/TM 回归）、Agent.Core.Tests 99/99、StateManagement.Tests 40/40、
      Agent.Persistence.Tests 35/35、Agent.ClineUsage.Tests 66/66、Boundary.Tests 43/43（Boundary.Tests 补 using Agent.Axioms.Boundaries;——既有命名空间缺失，与 Loops 迁移无关）
