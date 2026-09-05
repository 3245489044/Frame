# 中心化管理执行框架 + 贯穿式可取消执行边界 — 实施报告

> **日期**：2026-09-03
> **承接设计**：`docs/analysis/2026/9/9.3/2026-09-03-center-execution-boundary-framework-design.md`
> **落点**：`CsharpPrj/Operation/HierarchyMethod/Center`（新增横切包）+ 消费方（`Agent.Core` / `Produce.Core` / `Share.TaskStatusPool`）接线
> **原则**：未新建任何项目；新文件随既有 `HierarchyMethod.csproj`（SDK 隐式包含）经 `Operation` 聚合进 `MainProj.sln`。
> **验证**：`HierarchyMethod.csproj` / `Agent.Core.csproj` / `Produce.Core.csproj` 各 `dotnet build -c Debug` → 0 警告 0 错误。

---

## 一、范围与关键判断（相对设计文档的现状差异）

实施前对 HEAD 代码逐项核对，**部分设计目标已超前落地**，避免重复劳动：

| 设计文档目标 | HEAD 现状 | 处理 |
|---|---|---|
| 引擎单一 while（Fold/Signal/Decision 复用 SnapshotDrivenLoopEngine） | ✅ 已达成 | 跳过 |
| Agent Outbox 持久化解耦（R2）、单步读源超时、`INodeFunctionInvoker` 抽象、R4 同库事务、D4 强类型归因 | ✅ 已达成 | 保留 |
| 编排层框架级可取消/有界边界（NodeGraphOrchestrator 裸 await、Center 包缺失） | ❌ 缺口 | 本轮实施 |
| Center/ 横切包（ExecutionScope/写门/读源/装配契约） | ❌ 全无 | 本轮新建 |
| 统一 ErrorClassifier 认识超时语义（超时误归 ExecutionFailure） | ❌ 盲区 | 本轮接通 |
| R2 双 FunctionInvoker（Agent / Produce）平行 | ❌ 平行 | 本轮收敛 |
| Produce 写门 / 装配契约 fail-fast | ❌ 未做 | 本轮收敛 + 可选开关 |

---

## 二、实施清单

### Phase 1 — Center/ 横切包基础件（`Operation.HierarchyMethod.Center`，7 个新文件）

| 文件 | 职责 |
|------|------|
| `ExecutionBoundaryExceptions.cs` | 强类型自证异常族：`ExecutionBoundaryException`(Stage/Step/Budget) + `Step/Round/StoreTimeoutException`；实现 Share 标记 `IBudgetExhausted` |
| `ExecutionScope.cs` | 可取消执行边界载体：CTS 根 + `Derive` 层级子边界 + 有界竞速；**预算仅本级、不跨级继承**（防把轮预算隐式误套 LLM/暂停步） |
| `NodeReadView.cs` | R4 task 作用域单一读源视图 + `BuildSourceMap`（qualified/裸端口源映射） |
| `SingleWriteGuard.cs` | 运行时 `AssertSingleWriter`（平行写 fail-fast；`Check-SingleWriter.ps1` 之外补运行时断言） |
| `WriteCoordinator.cs` | 单一确定性写门：SemaphoreSlim 串行 + ExecutionScope 有界等待，锁卡死 → `StoreTimeoutException` |
| `WorkflowMethodFunctionInvoker.cs` | R2 唯一 canonical 函数调用适配器（`INodeFunctionInvoker`，`executionIdPrefix` 参数化兼容 Agent/Produce） |
| `PortContractGuard.cs` | R10 装配期端口契约校验（缺端口 fail-fast） |

### Phase 2 — 编排层纳入 Center 边界（`NodeGraphOrchestrator.cs`）
- 每步 `ExecuteStepAsync` 经 `scope.RunStepAsync`、持久化 `OnAfterStepAsync` 经 `scope.RunStoreAsync` 包裹 → 取消级联 + 强类型异常归一。
- **设计纠正**：确认 Agent 已有双层有界（`RoundLoopDriver` 轮级 + `RoundStepInvoker` 步级读源）+ `StallDetector`，故**编排层不持有步骤/轮预算**（避免与轮驱动重复、避免误杀 LLM 步）。边界只提供取消级联与异常归一。
- 可选 `requireAllPorts` 装配校验；Agent 组合根已全装配，故在生产开启 `requireAllPorts: true`。

### Phase 3 — 统一错误分类接通（`ErrorClassifier.cs` + `StepTimeoutException.cs`）
- `Share/TaskStatusPool/Loops/ErrorClassifier.cs` 新增协议标记 `IBudgetExhausted` → 分类归 `ErrorClass.Timeout`。
- 修正：此前 Agent 单步超时被误归 `ExecutionFailure`，现正确归 `Timeout`（对齐其 TO 转归语义）。Center 边界异常族经基类实现标记一并接通。
- 说明：`ErrorClassifier` 本就在 Share 确定性核心（最低层），**未做下移**（下移会反转依赖方向）。

### Phase 4 — R4 读源收敛 + R3 写门生产接线（`NodeExecutionService.cs`）
- **读源**：`EnumerateInputOutputsAsync` 改经 `NodeReadView.ListTaskOutputsAsync`（task 作用域单一读源）；用「显式解析器」重放在 `graphKeysDone / urNodesDone` 间的**两段诊断打点**——读源收敛不丢卡点归属日志。
- **写门**：`PersistAsync`（业务节点持久化唯一汇合点，同步 + Outbox 后台共用同一服务单例）经 `WriteCoordinator` 单门串行；注册运行时 `AssertSingleWriter("ur_nodes.persist", "NodeExecutionService.PersistAsync")`（固定写者名 → 多实例幂等、异写者平行才 fail-fast）。

### Phase 5 — R2 双 invoker 收敛 + R10 装配契约
- **Agent**：默认改用 Center canonical；删除重复类 `Agent.Core/.../WorkFlowMethodFunctionInvoker.cs`。
- **Produce**：`Produce.Core/WorkflowMethodFunctionInvoker` 改为转发 Center canonical（保留 `IProduceFunctionInvoker` 端口形态）。经 `Operation.csproj` 聚合引用可达，无新增/改动工程引用。
- **PortContractGuard**：`NodeGraphOrchestrator` 新增 `requireAllPorts`（默认 false 零回归）；Agent 组合根 `WorkflowRunner` 开启（已全装配，安全）。

---

## 三、依赖与治理对齐

- 单向依赖保持 `Domain(Agent/Produce) → HierarchyMethod/Center → LoopFoldMethod → Share.TaskStatusPool → 存储`；Center 不引用 Agent/Produce。
- `SEP.DeterministicCoreDisjoint` → `WriteCoordinator` 单门 + 运行时 `SingleWriteGuard.AssertSingleWriter`（编译期 `Check-SingleWriter.ps1` 之外双保险）。
- `AX.TerminationPriority` 预算耗尽语义 → `ExecutionBoundaryException` 族（`IBudgetExhausted` → `Timeout`，非整轮取消误判）。
- `SingleImplementation`（R2）→ Agent 重复类删除、Produce 逻辑单源化。
- `LogicCorrectness`/`RedundancyElimination` → 读源跨库拼读收敛、装配缺端口显式化。

---

## 四、验证口径

- 编译：`HierarchyMethod` / `Agent.Core` / `Produce.Core` 各 0 警告 0 错误。
- 行为零回归：编排层无预算（直跑仅响应取消）、`requireAllPorts` 默认关（仅在 Agent 生产组合根开启且已全装配）、写门默认 unbounded、AssertSingleWriter 固定写者名幂等。
- 读源诊断保留：`enumerateStart / graphKeysDone / urNodesDone` 三点日志经解析器重放，卡死归属仍可精确定位。

---

## 五、遗留与后续（需在产品级集成/回归环境验证）

| 项 | 说明 | 前置 |
|---|---|---|
| `AssertSingleWriter` 全覆盖（含终态信封 NodeOutputStore 直写） | 现注册于唯一持久化路径；信封等其它 ur_nodes 业务节点写点尚未并入同一注册名 | 写门盘点审计 + Agent 集成回归 |
| Produce 整链（R9 消费方骨架上移） | 本报告只收敛 invoker 逻辑（R2）；消费方整体上移属更大重构 | Produce 工程回归 |
| 引擎每 `op.Expand` 有界（LoopFoldMethod） | 设计 Phase1 残余；需在 `FoldEngine`/`DecisionFoldEngine` 内部逐 await 纳入 scope | 折叠引擎单测 |
| `DumpStallEvent` 栈转储 | 现为 `ExecutionScope.onStall` 预留钩子，未接具体线程栈通道 | 复用既有栈事件通道 |

*本报告为实施记录；行为接线均按「可回退、0 回归」门槛推进，产物可随 `git` 提交。*
