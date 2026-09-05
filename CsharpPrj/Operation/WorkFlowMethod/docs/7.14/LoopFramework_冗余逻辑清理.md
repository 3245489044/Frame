# LoopFramework 冗余逻辑清理文档

> 日期：2026-08-09 | 范围：`CsharpPrj/Operation/WorkFlowMethod/LoopFramework`、`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs`、`CentralTest/.../LoopFrameworkUsageTests.cs`
> 关联：`Agent/docs/refactor/2026-08-09-strategy-state-machine.md`（复活 `IStateMachine` 端口）、`LoopFramework_综合设计文档.md`（v1.2）
> 方案来源：LoopFramework 使用现状核查（生产在用 vs 生产死代码二分）

---

## 1. 清理目标

| # | 目标 | 对应问题 |
|---|------|---------|
| 1 | 明确 LoopFramework **生产使用**与**生产死代码**边界 | 通用状态机引擎（`Core`/`StateMachine`/`Extensions`/`Observability`）生产无调用方 |
| 2 | 清理无调用方死代码与死工厂 | `LoopEngine`/`RuleBasedStateMachine`/`LoopEngineBuilder` 等仅被测试 + 死工厂引用 |
| 3 | 保留活路径类型（`Axioms`/`ILoopObserver`/`LoopState`/`ActionResult`/`StateTransition`） | `AxiomLoopEngine` 主循环依赖 |
| 4 | 保留 `IStateMachine` 端口待**复活**（AI 策略状态机） | 设计文档 v1.2 预留 `LLMStateMachine`；Agent 侧将实现 |

---

## 2. 基线

- 构建命令：`dotnet build CsharpPrj\Operation\WorkFlowMethod\WorkFlowMethod.csproj -c Debug`（注：`Agent\Agent.sln` 不含 WorkFlowMethod，仅作 Agent 侧基线）
- 测试命令：`dotnet test CentralTest\CSharpTest\Operation.Tests\WorkFlowMethod.Tests\WorkFlowMethod.Test.csproj -c Debug`
- 基线状态：**0 个警告 / 0 个错误**（2026-08-09 记录）
- 目标框架：`net10.0-windows`

---

## 3. 现状核查：生产使用 vs 生产死代码

### 3.1 生产使用（活路径，必须保留）

| 子目录 | 类型 | 消费方 |
|--------|------|--------|
| `Axioms/` | `AxiomContext`/`AxiomLoopEngine`/`RoundLifecycle`/`ErrorClassifier`/`ProgressUpdater`/`BudgetEscalator`/`TerminationDecider`/`SyncProtocol`/`Strategy`/`LoopDecision`/`AxiomLoopResult`/`AxiomRound`/`AxiomLoopEngineBuilder` | Agent 全模块（24 处） |
| `Abstractions/` | `ILoopObserver` | Agent + `AxiomLoopEngine` |
| `Core/` | `LoopState`、`ActionResult` | `AxiomLoopEngine`（经 `ILoopObserver` 调用） |
| `StateMachine/` | `StateTransition<TState>` | `ILoopObserver.OnDecisionMadeAsync<TState>` 签名 |

> 注：`AxiomLoopEngine` 用 `Core.LoopState`/`Core.ActionResult`；`ILoopObserver` 用 `Core.ActionResult` + `StateMachine.StateTransition`。三者**不可删**。

### 3.2 生产死代码（无调用方，仅测试 + 死工厂引用）

| 子目录 | 类型 | 引用方 | 建议处置 |
|--------|------|--------|---------|
| `Core/` | `LoopEngine<TContext,TState>` | 死工厂 + 测试 | 删除 |
| `Core/` | `DefaultLoopController` | 死工厂 + 测试 | 删除 |
| `Core/` | `LoopContext` | `ILoopController`/`LoopEngine`/`ILoopMiddleware`（皆死） | 删除 |
| `Core/` | `LoopResult` + `IterationRecord` | `LoopEngine`（死）+ 测试 | 删除 |
| `Core/` | `LoopStatus` | `ILoopEngine`/`LoopEngine`/`LoopContext`（皆死） | 删除 |
| `Core/` | `AnonymousImplementations`（AnonymousActionExecutor/AnonymousContextManager/StateMachineObjectAdapter） | 死工厂 `CreateLoopEngine` | 删除 |
| `StateMachine/` | `RuleBasedStateMachine` | 死工厂 + 测试 | 删除（或并入文档一 Hybrid 设计）→ **已恢复 2026-08-09**（Hybrid 确定性守卫候选） |
| `StateMachine/` | `StateManager<TState>`、`StateRecord<TState>`、`StateRule` | 死工厂 + 测试 | `StateManager`/`StateRecord` 删除；`StateRule` 随 `RuleBasedStateMachine` **已恢复 2026-08-09**（直接依赖） |
| `Extensions/` | `LoopEngineBuilder` | 死工厂 `CreateLoopEngineBuilder` | 删除 |
| `Observability/` | `LoopTracker`、`LoopEvent` | 仅测试 | 删除 |
| `Abstractions/` | `IActionExecutor`、`IContextManager`、`ILoopController`、`ILoopMiddleware` | 死工厂 + 测试 | 删除 |
| `Abstractions/` | `ILoopEngine` | 死路径（`LoopEngine`） | 删除（如无保留必要） |
| `Abstractions/` | **`IStateMachine<TState>`** | 死工厂 + 测试 | **保留**（待复活） |
| `Logic/` | `Assess`/`Aggregate`/`Resolve`/`TruthValue`（含 ThreeValuedLogic/策略枚举） | 无任何 C# 消费方（仅 .clinerules 提及） | 删除（本次清理补充项）→ **已恢复 2026-08-09**（clinerules 逻辑算子的规范物化实现） |
| `WorkflowMethodUseage.cs` | `CreateLoopEngine`/`CreateRuleBasedStateMachine`/`CreateDefaultLoopController`/`CreateLoopEngineBuilder`/`CreateLoopTracker`（556/559/566/570/573 行，共 5 个） | **无调用方** | 删除 |

### 3.3 死工厂核实

`WorkflowMethodUseage` 第十一部分 LoopFramework 的 5 个工厂方法（`CreateLoopEngine` / `CreateRuleBasedStateMachine` / `CreateDefaultLoopController` / `CreateLoopEngineBuilder` / `CreateLoopTracker`）全仓库**无调用方**（仅定义处命中），为死工厂。其中 `CreateDefaultLoopController` / `CreateLoopTracker` 为文档初版遗漏项，随本清理一并删除。

---

## 4. 清理方案（分阶段）

| 阶段 | 内容 | 状态 |
|------|------|------|
| 0 | 基线锁定 + 本文档 | ✅ 本文档 |
| 1 | 删除死工厂（`WorkflowMethodUseage` 第十一部分 5 方法 + 相关 `using`） | ✅ 2026-08-09 |
| 2 | 删除死类型：`Core/LoopEngine`、`DefaultLoopController`、`LoopContext`、`LoopResult`(+`IterationRecord`)、`LoopStatus`、`AnonymousImplementations`；`ActionResult` 独立为 `Core/ActionResult.cs` 保留 | ✅ 2026-08-09 |
| 3 | 删除死类型：`StateMachine/StateManager`、`StateRecord`、`Extensions/LoopEngineBuilder`、`Observability/*`（`RuleBasedStateMachine`+`StateRule`、`Logic/*` 先删后恢复见阶段 7） | ✅ 2026-08-09 |
| 4 | 删除死接口：`Abstractions/IActionExecutor`、`IContextManager`、`ILoopController`、`ILoopMiddleware`、`ILoopEngine`（保留 `ILoopObserver`/`IStateMachine`） | ✅ 2026-08-09 |
| 5 | 同步更新 `LoopFrameworkUsageTests`（删除测试已删类型的用例） | ✅ 2026-08-09 |
| 6 | 全仓引用核查 + 构建/测试验证 + 本文档回填 | ✅ 2026-08-09 |
| 7 | 评估恢复：`Logic/*`（4）+ `RuleBasedStateMachine` + `StateRule`（clinerules 算子物化 + Hybrid 守卫候选） | ✅ 2026-08-09 |
---

## 5. 与策略状态机文档的联动（复活 `IStateMachine`）

- 本清理**保留 `Abstractions/IStateMachine<TState>`**（活抽象、死实现），不删除。
- `Agent/docs/refactor/2026-08-09-strategy-state-machine.md` 将在该端口上实现 `AiDrivenStrategyStateMachine`（决策核心 `IStateMachine<StrategyState>` + LLM 导演），使端口由"死"转"活"。
- 清理时**不得删除**：`StateMachine/StateTransition<TState>`（被活路径 `ILoopObserver` 引用）、`Core/LoopState`、`Core/ActionResult`（被 `AxiomLoopEngine` 引用）。

### 依赖保留矩阵（清理后存活集）

| 保留类型 | 被谁引用 |
|---------|---------|
| `IStateMachine<TState>` | （待）`AiDrivenStrategyStateMachine` |
| `StateTransition<TState>` | `ILoopObserver.OnDecisionMadeAsync` |
| `Core.LoopState` / `Core.ActionResult` | `AxiomLoopEngine` |
| `Abstractions.ILoopObserver` | Agent + `AxiomLoopEngine` |
| `Axioms/*` | Agent 主循环 |
| `Logic/*`（Assess/Aggregate/Resolve/TruthValue） | clinerules 逻辑算子物化（恢复项，供后续 Axioms/状态机复用） |
| `RuleBasedStateMachine` / `StateRule` | Hybrid 确定性守卫候选（恢复项，实现 `IStateMachine<LoopState>`） |

---

## 6. 测试影响（`LoopFrameworkUsageTests.cs`）

现有 `LoopFrameworkUsageTests` 直接测试：`LoopState`/`LoopStatus` 值、`LoopResult.RecordIteration`、`ActionResult`、`StateTransition` 默认/完成、`RuleBasedStateMachine` 创建/重置、`LoopContext`/`DefaultLoopController`、`LoopTracker`。

| 测试用例 | 依赖类型 | 清理处置 |
|---------|---------|---------|
| `LoopState_And_LoopStatus_Values_ShouldBeDefined` | `LoopStatus`（删） | 改为 `LoopState_Values_ShouldBeDefined`，删 `LoopStatus` 断言；保留 `LoopState` |
| `LoopResult_ShouldRecordIterations` | `LoopResult`（删） | 删除用例 |
| `ActionResult_DefaultAndCustom_ShouldWork` | `ActionResult`（保留） | 保留 |
| `StateTransition_DefaultAndCompleted_ShouldWork` | `StateTransition`（保留） | 保留 |
| `RuleBasedStateMachine_ShouldCreateAndReset` | `RuleBasedStateMachine`（删） | 删除用例（若未复活） |
| `LoopContext_And_DefaultLoopController_ShouldWork` | `LoopContext`/`DefaultLoopController`（删） | 删除用例（文档初版遗漏项） |
| `LoopTracker_ShouldTrackSteps` / `LoopTracker_Clear_ShouldRemoveAll` | `LoopTracker`（删） | 删除两用例 |

> 若阶段 4 前已将 `IStateMachine` 复活为策略状态机，可新增 `AiDrivenStrategyStateMachine` 的替代单测。

---

## 7. 风险与验证

| 风险 | 缓解 |
|------|------|
| 误删活路径类型（`LoopState`/`ActionResult`/`StateTransition`） | 以 §3.1 / §5 依赖保留矩阵为准，逐类型 grep 全仓引用后再删 |
| 删除后测试编译失败 | §6 同步更新 `LoopFrameworkUsageTests` |
| 外部（非 LoopFramework）隐性引用 | 清理前全仓 `grep 'WorkFlowMethod.LoopFramework.(Core|StateMachine|Extensions|Observability)'` 确认仅死工厂 + 测试命中 |
| `IStateMachine` 端口误删导致策略状态机无法落地 | 该端口**明确保留**并交文档一复活 |

验证方式：每阶段 `dotnet build CsharpPrj\Operation\WorkFlowMethod\WorkFlowMethod.csproj -c Debug` 0 警告 0 错误；清理后全仓 grep 无指向已删类型的新引用。

---

## 8. 待确认（已处置）

- `Abstractions/ILoopEngine` 是否一并删除（目前仅死路径引用）。 → ✅ 已删除（仅死路径引用，无保留必要）。
- `RuleBasedStateMachine` 是删除，还是保留并入文档一 `HybridStrategyStateMachine` 的确定性守卫。 → 初版删除；经评估作为 Hybrid 确定性守卫候选，**已恢复 2026-08-09**（连同直接依赖 `StateRule`）。
- 清理范围是否含 `LoopFramework_综合设计文档.md` 中已规划但未实现的 `Execution/`、`Context/` 目录（当前不存在，仅文档提及）。 → 不涉及（目录本就不存在）。
- 死工厂删除是否影响 `WorkflowMethodUseage` 其他公开 API（需确认无外部契约依赖）。 → ✅ 已确认 5 个工厂方法无调用方，无外部契约依赖。
- **`Logic/` 目录（4 文件）** 是否一并删除（本次核查新增项，文档初版未列）。 → 初版删除；经评估为 clinerules 逻辑算子的规范物化实现，**已恢复 2026-08-09**（`Assess`/`Aggregate`/`Resolve`/`TruthValue`）。

