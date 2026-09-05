# Agent.Core.Tests 测试整理与重构文档

> 目的：消除测试目录中的重复冗余，合并同领域测试，抽取共享测试支撑，降低维护成本并统一断言口径。
> 范围：`CentralTest/AgentTest/Agent.Core.Tests` 全部测试文件（约 50 个 .cs）。

---

## 1. 现状概览

按领域分组统计如下（基于逐文件阅读确认，非臆测）：

| 领域 | 文件 | 现状 |
|---|---|---|
| DynamicWorkflow / AgentLoopFunction | DynamicWorkflowCoreTests.cs、DynamicWorkflowMerger.ShorthandCompatTests.cs、EndToEndIntegrationTests.cs | **重复最严重**，三文件交叉覆盖 |
| 对话映射 / 投影 | ConversationNodeSyncTests.cs、ConversationLlmProjectorTests.cs、ConversationAgentWireTests.cs | 同域小测试分散 |
| 会话渲染 / UI 装配 | ConversationPageRendererTests.cs、HtmlConversationRendererTests.cs、AgentUiHostConversationRendererTests.cs、HtmlUiAssemblyTests.cs、ConversationTemplateTestSupport.cs | 相关但各自独立 |
| 历史循环 / 主循环 | HistoryMainLoopTests.cs + HistoryMainLoopFakes.cs、HistoryLoopDeciderTests.cs + HistoryLoopDeciderFakes.cs | fakes 强耦合分离文件 |
| 编排器 | TaskOrchestratorTests.cs + TaskOrchestratorFakes.cs | fakes 强耦合分离文件 |
| 序列化 / 持久化 | AgentTaskItemSerializationTests.cs、AgentTaskItemJsonConverterTests.cs、SnapshotSerializerTests.cs | 两文件重叠 |
| 路由 / 调度 | Routing/TaskRouterTests.cs、Routing/AgentRuleBackendTests.cs、RuleTaskExecutorTests.cs、HybridSchedulerTests.cs、ConfigSchedulingTests.cs、HistoryDecisionMapperTests.cs、JsonRuleEngineTests.cs | CanExecute 断言双处 |
| 任务池并发 | TaskPoolParallelTests.cs | 独立，无需动 |
| 标准上下文 | StandardContextBuilderTests.cs、StandardContextContractTests.cs | 与管道共享桩重复 |
| 折叠 runner | FoldConversationRunnerTests.cs、SkConversationFoldOperatorTests.cs | 独立 |
| 组件级单测 | TConfigReturnTypeTests.cs、CallAsyncRegressionTests.cs、InputSafetyGuardTests.cs、SkillInjectorTests.cs 等 | 独立，无需动 |

---

## 2. 问题清单

### 2.1 P0｜DynamicWorkflowCoreTests.cs 巨型重复聚合（单文件 6 个 TestClass）

`DynamicWorkflowCoreTests.cs` 一次性塞入 6 个 TestClass：

- `DynamicWorkflowMergerTests`
- `AgentLoopFunctionTests`
- `WorkFlowMethodIntegrationTests`
- `DynamicWorkflowMergeScenariosTests`
- `AgentLoopBoundaryTests`
- `WorkflowLoopIntegrationTests`

内部自重复严重：

| 重复对 | 说明 |
|---|---|
| `WorkFlowMethodIntegrationTests.RegisterAndCallFunction_注册并调用_应成功` ↔ `DynamicWorkflowMergerTests.MergeAndExecuteAsync_无效JSON_应返回error状态` | **同一逻辑**：都用无效 JSON 调 `MergeAndExecuteAsync` 并断言 `status=error` |
| `WorkFlowMethodIntegrationTests.BuildWorkflow_节点列表` ↔ `DynamicWorkflowMergeScenariosTests.MergeFragment_单节点/多节点链式/含KeyMapping/含NextNodeConfig` ↔ `WorkflowLoopIntegrationTests.BuildWorkflowJson_循环/条件/并行节点` | 高度重叠：都在做 `BuildWorkflowJson`/`ParseFragment` 后的“JSON 包含某字段”断言 |
| `AgentLoopFunctionTests.ShouldCompressContext` ↔ `AgentLoopBoundaryTests.ShouldCompressContext/CompressHistory` | 同一文件内两个类重复测压缩函数 |

### 2.2 P0｜`AgentLoopFunction` 压缩/判定在三个文件重复覆盖且口径不一

- `DynamicWorkflowCoreTests.cs`：`AgentLoopFunctionTests` + `AgentLoopBoundaryTests`
- `EndToEndIntegrationTests.cs`：`AgentLoopFunction_循环控制_边界条件验证`

三处都在测 `ShouldCompressContext` / `CompressHistory` / `IsTaskComplete` / `IsJsonWorkflowFragment`。
**阈值口径不统一**：`AgentLoopBoundaryTests` 精确定位“10 条不触发 / 11 条触发”，`AgentLoopFunctionTests` 写“超 10 条触发”（12 条），`EndToEnd` 用 5/15 验证。`IsTaskComplete` 对空串是否判定完成的断言三处也不一致。
→ 重构时必须统一为单一权威口径（建议以 `AgentLoopBoundaryTests` 的 10/11 边界为准），消除三处漂移。

### 2.3 P1｜`EndToEndIntegrationTests.cs` 与 DynamicWorkflow 领域重复

`EndToEndIntegrationTests.cs` 的 `DynamicWorkflowExecution_多节点链式执行`、`WorkflowBuilder_多种节点类型`、`CompleteWorkflowExecution`（含五阶段）与 `DynamicWorkflowCoreTests` 的 `BuildWorkflowJson` / `MergeAndExecuteAsync` 覆盖完全重复，应整体并入 DynamicWorkflow 合并后的套件。

### 2.4 P1｜`RuleTaskExecutor.CanExecute` 断言双处

- `Routing/AgentRuleBackendTests.cs` → `RuleTaskExecutor_CanExecute_NonHybrid`
- `RuleTaskExecutorTests.cs` → `RuleTaskExecutor_MatchesOnlyConfigMode`

两者都按 `SchedulingMode` 对 `RuleTaskExecutor.CanExecute` 做 Config/Hybrid/Deterministic 判定。保留 `RuleTaskExecutorTests` 处，删除 `AgentRuleBackendTests` 处（该处职责是路由接线，不重复执行器判定）。

### 2.5 P1｜共享测试桩在两文件逐字段重复定义

`FakeNodeOutputStore`（`INodeOutputStore` 桩）与 `FakeHierarchy`（`IHierarchyReferenceManager` 桩）在两个文件内实现几乎逐行相同：

- `ConversationPipelineTests.cs`（`FakeNodeOutputStore` / `FakeHierarchy` / `InMemoryHistoryStore`）
- `StandardContextBuilderTests.cs`（`FakeNodeOutputStore` / `FakeHierarchy` / `FakeHistoryStore`）

后果：接口签名一旦变化，两处要同步改两遍，极易漂移。应抽取为共享 TestSupport 类（与既有 `HistoryMainLoopFakes` / `TaskOrchestratorFakes` 约定一致）。

### 2.6 P2｜强耦合 fakes 分离文件

`HistoryMainLoopFakes` / `HistoryLoopDeciderFakes` / `TaskOrchestratorFakes` 均只被对应测试类独占，分开文件增加导航成本，建议合并进所属测试文件。

### 2.7 P2｜同领域小测试分散

`ConversationNodeSyncTests` / `ConversationLlmProjectorTests` / `ConversationAgentWireTests` 均围绕 `ConversationLog` 做纯映射，各自仅 1-3 个方法，合并为一个文件收益明显。

---

## 3. 目标目录结构（重构后）

```
Agent.Core.Tests/
├── 组件级单测/           # 保留现状，无需动
│   ├── TConfigReturnTypeTests.cs / CallAsyncRegressionTests.cs / ...
├── DynamicWorkflow/      # 从巨型文件拆分 + 合并重复
│   ├── DynamicWorkflowMergerTests.cs        # 内置 ParseFragment/BuildWorkflowJson 有效/无效用例
│   ├── DynamicWorkflowExecutionTests.cs     # 吸收 EndToEndIntegration 的 MergeAndExecute 执行链路
│   └── AgentLoopFunctionTests.cs            # 统一压缩/判定，以 10/11 边界为准
├── Conversation/
│   ├── ConversationMappingTests.cs          # 合并三个投影小测试
│   ├── ConversationPipelineTests.cs
│   ├── ConversationRenderingTests.cs        # 合并 PageRenderer/HtmlRenderer/UiHost 挂载
│   └── ...
├── Persistence/
│   └── AgentTaskItemPersistenceTests.cs     # 合并两序列化文件
├── Routing/
│   └── RoutingTests.cs                      # 合并 TaskRouter + AgentRuleBackend
├── TestSupport/
│   ├── FakeNodeOutputStore.cs               # 从两处抽取
│   ├── FakeHierarchy.cs
│   └── InMemoryHistoryStore.cs
```

> 说明：`ISupport` merge 的可选项，最终目录是否分层可由你拍板；核心是**消除重复 + 统一口径 + 抽取共享桩**，不强制重组目录。

---

## 4. 实施步骤（按优先级）

| 步骤 | 内容 | 风险 | 验证 |
|---|---|---|---|
| 1 | 拆分 `DynamicWorkflowCoreTests.cs` 6 类，合并 `WorkFlowMethodIntegrationTests`/`MergeScenarios`/`WorkflowLoopIntegration` 的 BuildWorkflowJson 重复；删除重复的 `invalid→error` 用例 | 中（断言多、易误删） | 单测跑通 |
| 2 | 统一 `AgentLoopFunction`：合并三处压缩/判定到 `AgentLoopFunctionTests.cs`，固定 10/11 边界口径，删除 `EndToEnd` 重复方法 | 中（口径若与实现不符会暴露矛盾，需以实现为准） | 单测跑通 |
| 3 | 抽取 `FakeNodeOutputStore`/`FakeHierarchy`/`InMemoryHistoryStore` 到 `TestSupport/` | 低 | 编译 + 引用方测试通过 |
| 4 | 合并强耦合 fakes（MainLoop/Decider/Orchestrator） | 低 | 编译 + 单测 |
| 5 | 合并同领域小测试（映射 / 序列化 / 路由） | 低 | 编译 + 单测 |
| 6 | 删除 `AgentRuleBackendTests` 处重复的 CanExecute 断言 | 低 | 单测 |

---

## 5. 验证与护栏

- 本项目为 MSTest，重构后需保证全套测试通过，尤其关注此前基线：
  - `Persistence` 类（SnapshotSerializerTests 等）不得变更既有用例的行为。
  - 项目约定「核心合约修复即回归护栏」，`CoreConsistencyGuardTests`、`CallAsyncRegressionTests` 等只删不动的重复，不改语义。
- 建议分批重构、每批独立跑一遍负责领域测试，避免一次性大改导致回归难以定位。
- 若用 Rider/VSTest 执行：`Agent.Core.Tests` 项目单独测试即可，无需依赖 GUI 运行态。

---

## 6. 风险与注意

- **Threshold 口径勿臆断**：步骤 2 的统一须以 `AgentLoopFunction` 真实实现（`ShouldCompressContext` 的阈值常量）为准，不可强行套用某个测试的注释口径。
- **Shared stub 抽取**：抽取 `FakeNodeOutputStore`/`FakeHierarchy` 后，确保 `ConversationPipelineTests` 与 `StandardContextBuilderTests` 仍各自注入同一语义（历史/层级行为一致）。
- **不要"顺手改语义"**：合并时仅做归类与去重，绝不调整既有断言的业务预期；除明确指出的口径矛盾外，其余用例原样保留。