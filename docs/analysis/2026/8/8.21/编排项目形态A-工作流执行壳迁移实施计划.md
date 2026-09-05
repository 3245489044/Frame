# 编排项目形态 A · 工作流执行壳迁移实施计划（下一阶段）

> 日期：2026-08-21 · 状态：**待实施**（前置「会话编排壳迁移」已完成并验证）
> 目标：把 `WorkflowRunner`/`RoundStateMapper` 等**工作流执行壳**从 `Agent.Axioms` 迁入独立编排项目 `Agent.Orchestration`，
> 达成形态 A 的「会话编排 + 工作流执行」全量归属编排壳、能力实现（执行器/传输/网关）经端口注入。
> 前置方案：`docs/analysis/2026/8/8.21/统一LLM网关收敛重构-实施状态与链路分析.md`、`统一LLM网关收敛重构方案.md`。

---

## 〇、背景与目标

- **形态 A（纯编排壳）**：`Agent.Orchestration` 只承载「会话编排 + 工作流执行 + 后端选择 + 取消/追踪端口」，执行器（Cline/Answer）、传输（daemon/LocalLlmHttp）、网关作为能力提供方经 `Agent.Contract` 端口注入，**不引用 `Agent.Modules`**，可注入假后端独立测试。
- **已完成**：编排项目骨架 + 端口下沉（`IBackendRouter`/`IExecutorRegistry`/`ITrace`/`BackendRoute`）；会话编排壳迁移（`ConversationPipeline`/`ConversationSessionService`/`ConversationReply`/`ConversationSignal` → `Agent.Orchestration/SessionWorkflow`）；R1 契约（`SessionWorkflowContext`/`StepContract`）。
- **本计划目标**：迁移工作流执行壳，使编排项目成为完整的会话+执行编排宿主。

---

## 一、当前已落地成果（安全基线）

| 项 | 内容 | 验证 |
|----|------|------|
| 新项目 | `Agent.Orchestration`（`Agent.sln` 已注册） | 编译通过 |
| 端口下沉 | `Agent.Contract/Observability/`：`BackendRoute`/`IBackendRouter`/`IExecutorRegistry`/`ITrace` | 编译通过 |
| 会话编排壳 | `Agent.Orchestration/SessionWorkflow/`：`ConversationPipeline`/`ConversationSessionService`/`ConversationReply`/`ConversationSignal` | 测试 130/131（仅既有基线失败） |
| R1 契约 | `SessionWorkflowContext` / `StepContract`/`StepField` | 编译通过 |
| 路径 Y 奠基 | `Agent.Contract/State/`：`IConversationEntry` / `IConversationStore` | 编译通过 |
| 引用更新 | `Agent.Launcher`（`ConversationHosting`/`AgentUiHost`）、`Agent.Core.Tests` | 编译通过 |
| 其他 | `RoundLoopDriver.CreateLeaseContext` 改 `public` | 编译通过 |

依赖边界：编排项目引用内核层（`Agent.Core`/`Agent.Axioms`/`Agent.Runtime`/`Agent.Operation`），**不引用 `Agent.Modules`**。

---

## 二、问题：项目级循环依赖（硬约束）

把 `WorkflowRunner`/`LoopDecisionMapper` 迁入编排后，若不做前置重构，会形成**项目引用循环**（编译器硬拒绝，非可修编译错误）：

| 迁走类型 | 引发循环的引用 | 后果 |
|----------|---------------|------|
| `WorkflowRunner`/`LoopDecisionMapper` | `AgentRunner.cs:80,84`（`Agent.Core`）`new WorkflowRunner(...)` / `new LoopDecisionMapper()` | `Agent.Core` ↔ `Agent.Orchestration` 循环 |
| `DataContractRegistrar` | `AxiomBindingModule.cs:22`（`Agent.Axioms`）`DataContractRegistrar.EnsureRegistered()` | `Agent.Axioms` ↔ 编排 循环 |
| `LeaseDeriver` | `AgentRunner.cs:213` `LeaseDeriver.Derive(...)` | `Agent.Core` ↔ 编排 循环 |

**当前 `Agent.Orchestration` 已引用 `Agent.Core`**（会话壳 `ConversationPipeline` 用 `AgentTaskPoolAdapter`/`RoundLoopDriver`），这是循环的另一端。

> 关键事实：`Agent.Axioms` 内部对执行机制类型的引用**几乎全为 XML 注释**（`<see>/<c>`），非代码依赖；`DataContractRegistrar`/`LeaseDeriver`/`RoundKeys` 需**保留在 `Agent.Axioms`**（被 `AxiomBindingModule`/`AgentRunner` 代码引用），从循环中剥离。

---

## 三、迁移边界（类型归属）

### 迁入 `Agent.Orchestration/StepExecution`（执行机制簇）
- `WorkflowRunner.cs`（namespace `Agent.Axioms` → `Agent.Orchestration.StepExecution`）
- `RoundStateMapper.cs` / `RoundStepInvoker.cs` / `RoundSpec.cs` / `RoundSpecLoader.cs`
- `RoundPipelineContext.cs` / `LoopDecisionMapper.cs` / `DecisionWordMapper.cs`

### 保留在 `Agent.Axioms/Loops`（避免 Agent.Axioms ↔ 编排循环）
- `RoundKeys.cs`（被 Agent.Axioms 各目录代码引用）
- `LeaseDeriver.cs`（被 `AgentRunner.cs:213` 引用）
- `DataContractRegistrar.cs`（被 `AxiomBindingModule.cs:22` 引用）
- `AgentLoopConfig.cs`（循环配置，非执行机制）

### 不迁移
- `Agent.Modules`（能力实现）——编排项目永不引用
- 执行器的传输实现（`ClineClient`/`LocalLlmHttp`）

---

## 四、路径 Y 分阶段实施计划

### Phase A：会话壳端口化（打破 `Agent.Core` ↔ 编排 循环）

目标：让编排项目**不再引用 `Agent.Core`**，使 `Agent.Core`→编排 变为单向（`AgentRunner` 保持 `new WorkflowRunner`）。

| 步骤 | 改动 | 文件 |
|------|------|------|
| A1 | `ConversationPipeline` 构造参数 `AgentTaskPoolAdapter pool` → `IConversationStore store`；内部 `_pool.WriteStatusAsync` → `_store.WriteStatusAsync`；`_persistence.*` → `_store.*`；`new AgentTaskItem` → `_store.CreateNew`；`RoundLoopDriver.CreateLeaseContext` → 编排内 `LeaseContextFactory` | `Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs` |
| A2 | 编排内新增 `LeaseContextFactory`（用 `IConversationEntry` 投影 `AxiomContext`，替代 `RoundLoopDriver.CreateLeaseContext`） | `Agent.Orchestration/StepExecution/LeaseContextFactory.cs` |
| A3 | `AgentTaskPoolAdapter` 实现 `IConversationStore`（`WriteStatusAsync` 已有；持久化经 `Persistence` 适配；`CreateNew` → `new AgentTaskItem`） | `Agent.Core/TaskPool/AgentTaskPool.cs` |
| A4 | `ConversationHosting` 装配：传 `IConversationStore`（`pool` 即实现） | `Agent.Launcher/Composition/ConversationHosting.cs` |
| A5 | 测试 `ConversationPipelineTests.Build` 适配 `IConversationStore` | `CentralTest/AgentTest/Agent.Core.Tests/ConversationPipelineTests.cs` |
| A6 | 编排 csproj **移除 `Agent.Core` 引用** | `Agent.Orchestration/Agent.Orchestration.csproj` |

**验收**：`Agent.sln` 全绿；编排项目不再引用 `Agent.Core`。

### Phase B：移动执行簇 + 命名空间

| 步骤 | 改动 |
|------|------|
| B1 | 命令行移动 8 个执行机制文件 → `Agent.Orchestration/StepExecution/`，替换命名空间 `Agent.Axioms` → `Agent.Orchestration.StepExecution` |
| B2 | 补 `using Agent.Axioms.*`（`RoundKeys`/`DataContainerBinder`/`StateContextAdapter`/`ExecResultDocument`/`PromptCatalog`/`Diagnostics` 等保留在 Agent.Axioms） |
| B3 | `Agent.Core` 引用编排（`AgentRunner` new `WorkflowRunner`/`LoopDecisionMapper` 现在是单向） |
| B4 | `Agent.Launcher`/`Agent.Modules`/`Agent.Axioms` 内对迁走类型的**代码**引用改命名空间/`using` |

**验收**：`Agent.sln` 编译通过（无项目循环）。


### Phase C：测试迁移

| 步骤 | 内容 |
|------|------|
| C1 | 迁移/改 `Agent.Axioms.Tests/RoundStateMapperTests`、`RoundSpecLoaderTests`、`StateContextAdapterTests` |
| C2 | 迁移/改 `Agent.ClineUsage.Tests/RoundStateMapperSessionTests`、`Boundary.Tests/LoopDecisionMapperTests`、`DslMethod.Tests/DslAxiomBindingTests` |
| C3 | 对应测试 csproj 增加 `Agent.Orchestration` 引用；命名空间更新 |

**验收**：相关测试全绿。

### Phase D：可观测 + 后端单裁决接线（可选后续）

- `LlmRoutingClient` 实现 `IBackendRouter`（三要素由路由代理派生）；执行器运行时三要素统一由 `IBackendRouter.Current` 派生，消除 provider 矛盾。
- 取消/追踪：UI 传真实 ct；`ITrace` 贯穿 CallLog/审计/主回显。

---

## 五、风险与回滚

| 风险 | 缓解 |
|------|------|
| 循环依赖未完全打破导致无法编译 | 严格按 Phase A→B 顺序；Phase A 完成即验证「编排不再引用 Agent.Core」后再动 Phase B |
| `WorkflowRunner` 依赖 `Agent.Axioms` 大量协作类型（`StateCenter`/`DataContainerBinder` 等） | Phase B2 显式补 `using`；编排已引用 `Agent.Axioms`，单向无环 |
| 测试搬迁回归面大 | 每 Phase 独立编译 + 跑既有测试；先迁移测试再改生产引用 |
| `AgentTaskItem` 具体类依赖（`UserInput`/`Round`/`Status`） | 由 `IConversationEntry`（继承 `IAgentLoopState`+`ITaskPoolItem`+`IStateFacet`）覆盖，`AgentTaskItem` 天然实现，无需改实体类 |

**回滚**：每次 Phase 完成即 `git commit`；回退 = `git checkout` 到上一 Phase 的提交点。

---

## 六、关键端口/契约设计（已建）

```csharp
// Agent.Contract/Observability
public sealed record BackendRoute(string Provider, string? Model=null, string? BaseUrl=null, string? ClientType=null);
public interface IBackendRouter { BackendRoute Current {get;} bool IsLocal {get;} ILlmClient Chat {get;} }
public interface IExecutorRegistry { IAgentExecutor? Resolve(string? handle, string fallbackName); }
public interface ITrace { string BeginCall(string target, string? sessionId); void Audit(string sessionId, string text); }

// Agent.Contract/State
public interface IConversationEntry : IAgentLoopState, ITaskPoolItem, IStateFacet { }
public interface IConversationStore { GetAsync / CreateNew / EnqueueAsync / UpdatePayloadAsync / ListTaskKeysAsync / WriteStatusAsync }
```

---

## 七、下一步（首个执行动作）

1. **Phase A1**：改造 `ConversationPipeline` 构造/内部用 `IConversationStore`+`IConversationEntry`
2. **Phase A2**：新增 `LeaseContextFactory`（编排内）
3. **Phase A3**：`AgentTaskPoolAdapter` 实现 `IConversationStore`
4. **Phase A4-A6**：装配/测试适配 + 编排 csproj 移除 `Agent.Core`，编译验证

