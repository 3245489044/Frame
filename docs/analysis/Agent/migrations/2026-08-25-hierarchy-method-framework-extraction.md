# Agent.Core → Operation.HierarchyMethod 通用框架下沉：迁移计划与阻塞分析

> 日期：2026-08-25
> 目标：把 `Agent.Core` 中「通用实现被 Agent 包着 / 已重复双写」的逻辑下沉到 `Operation.HierarchyMethod`（通用、LLM-free 基础框架），
> 保持单向依赖无环（`HierarchyMethod` 零 `Agent.*` 依赖，clinerules SingleImplementation / TH-12）。
> 状态：P0 已完成并验证；P2 的 `RuleEngine`/`RuleSet` 已完成并验证；剩余 P1/P2 被「基础支撑设施」前置阻塞，本文件给出阻塞映射与解锁方案。

---

## 1. 目标与现状

### 1.1 两个项目的边界

| 项目 | 角色 | 命名空间 | 依赖约束 |
|------|------|----------|----------|
| `Operation.HierarchyMethod` | 通用框架落点 | `Nodes` / `History` / `Tasks` / `Abstractions` / `Rules` | 零 Agent 依赖、LLM-free；LLM 知识仅经注入端口由 Agent 提供 |
| `Agent.Core` | Agent 组合根 / 运行入口 | `Agent.Core` | 消费框架，不反向定义框架符号 |

### 1.2 已确立的「下沉」迁移模式（本项目既定惯例）

```
① 类型迁入 Operation.HierarchyMethod（或 Share.* 共享层）；
② 旧命名空间保留空 shim（使既有 using 可解析）；
③ 共享 GlobalUsings.cs（经各项目 csproj <Compile Link>）加 `global using 旧名 = 新命名空间.类型;` 兼容别名；
   测试项目（CentralTest\AgentTest\*.Tests）各自镜像 GlobalUsings.cs，同段补别名；
④ 依赖方向保持无环：迁入文件不得引用 Agent.*（必须参数化/端口化剥离）；
⑤ 若新增 clinerules [CR] 符号 → references.md 重新 -UpdateIndex 编译注册。
```

---

## 2. 已完成迁移（可追溯）

### 2.1 P0：`NodeOutputStore` + `JsonElementRead` ✅

| 原位置（Agent.Core） | 新位置（Operation.HierarchyMethod） | 变更 |
|------|------|------|
| `Nodes\NodeOutputStore.cs`（`Agent.Core.Nodes`） | `Nodes\NodeOutputStore.cs`（`Operation.HierarchyMethod.Nodes`） | `INodeOutputStore` 端口的 ur_nodes 背板默认实现；去除 `using Agent.Contract.Nodes;`（框架零 Agent 依赖）；逻辑逐字迁移（含分页护栏） |
| `JsonElementRead.cs`（`Agent.Core`，`internal`） | `Abstractions\JsonElementRead.cs`（`Operation.HierarchyMethod.Abstractions`，`public`） | 通用 `JsonElement`/`Utf8JsonWriter` 安全读写工具；internal → public 使跨程序集可用 |

- 删除旧文件：`Agent.Core\Nodes\NodeOutputStore.cs`、`Agent.Core\JsonElementRead.cs`。
- 别名（主 `GlobalUsings.cs` + 镜像测试 `Agent.Persistence.Tests` / `Agent.Core.Tests` / `Agent.Axioms.Tests`）：
  - `global using NodeOutputStore = Operation.HierarchyMethod.Nodes.NodeOutputStore;`
  - `global using JsonElementRead = Operation.HierarchyMethod.Abstractions.JsonElementRead;`
- 验证：`HierarchyMethod` / `Agent.Core` / `Agent.Launcher` / 相关测试项目 0 错误；`NodeOutputStoreTests` 5/5 通过。

### 2.2 P2：`RuleEngine` / `RuleSet` 纯评估核心 ✅

| 原位置（Agent.Core） | 新位置 | 变更 |
|------|------|------|
| `Loops\RuleEngine.cs`（`Agent.Core`） | `Rules\RuleEngine.cs`（`Operation.HierarchyMethod.Rules`） | 纯评估核心：`GuardConfig` / `LoopRule` / `RuleSet`（纯 POCO 模型）/ `RuleResult` / `RuleEngine`（`Evaluate` 首匹配 + `IsWithinGuard` + 单条判定）；委托通用内核 `Modules.Parsing.JsonRuleEngine`；未知 op 经 `onUnknownOperator` 回调暴露（缺省 no-op） |

- 配置加载与日志**保留 Agent 应用层**：
  - 新增 `Agent.Core\Loops\RuleSetLoader.cs`：`RuleSet.Load/Default` + 内嵌默认/回答型规则集（经 `ConfigFileLoader`/`ConfigJson`）。
  - 新增 `Agent.Core\Loops\RuleEngineDiagnostics.cs`：未知 op 的 `AgentLog` 回调。
  - 删除 `Agent.Core\Loops\RuleEngine.cs`。
- 更新 3 处消费方：`RuleMatchFunction` / `PromptFrameworkFunction` / `Agent.Modules\Answer\StatelessAnswerLoop`（`RuleSet.Load`→`RuleSetLoader.Load`，评估透传诊断回调）。
- `HierarchyMethod.csproj` 新增 `Modules\Parsing\Parsing.csproj` 引用。
- 别名（主 `GlobalUsings.cs`）：`GuardConfig` / `LoopRule` / `RuleSet` / `RuleResult` / `RuleEngine` → `Operation.HierarchyMethod.Rules.*`。
- 验证：`HierarchyMethod` + 全部 10 个 Agent src 项目（Contract/Axioms/Calculate/Operation/Reference/Runtime/Orchestration/Guardrails/StorageBridge/Core/Modules/Launcher）0 错误 0 警告；`JsonRuleEngineTests` 5/5 通过。

---

## 3. 剩余 P1/P2 阻塞映射（真实依赖分析）

> 结论：其余项**不是单文件迁移**，而是被「基础支撑设施迁移」前置阻塞。以下每一项的依赖都是 Agent 侧/领域侧基础设施，强制一次性搬会破坏大范围构建。

### 3.1 P1 节点引擎

| 目标项 | 阻塞依赖 | 说明 |
|--------|----------|------|
| `NodeExecutionService` | `AgentNode`（框架 `NodeDefinition` 别名）、`RoundKeys`、`ReferenceGrounding`、`TaskNodeRelationService`、`WorkFlowMethod.Usage.CallFunctionAsync`、`KeyMapping` | 节点构建/解析/执行/持久化核心可泛化，但被节点模型 + 函数注册 + 引用守卫端口化阻塞 |
| `RoundStepInvoker` | `RoundPipelineContext`、`StateContextAdapter`、`DataContainerBinder`、`KeyMappingConfig`、`RoundKeys` | 骨架步骤调用 + 状态外置适配阻塞 |
| `WorkflowRunner` | `Modules.Pipeline.StaticAsyncPipeline`、`DecisionWordClassifier`、`ExecResultDocument`、`RoundKeys`、`AgentNode`、默认骨架 `agent-loop.json` | 声明式管线装配本身可通用，但被决策词映射 + 执行结果文档 + 状态空间阻塞 |
| `RoundSpecLoader` | `ConfigFileLoader`、`ConfigJson.WorkflowOptions`、`AgentLog`、`KeyMappingConfig` | 声明式骨架加载器可通用，但依赖基础配置设施（§4 解锁） |

### 3.2 P2 其余项

| 目标项 | 阻塞依赖 | 说明 |
|--------|----------|------|
| `RoundLoopDriver` | `Agent.Operation.LoopDriver`（通用循环驱动本就在 Agent.Operation，未在框架）+ `WorkFlowMethod` 的 `AxiomContext`/`TerminationDecider`/`RoundLifecycle` + `AgentTaskItem` | 已是 `AgentTaskItem` 薄外观；真正通用循环驱动不在框架，需先决定承载层 |
| 边界评估纯函数（`LogicalBoundaryEvaluator`/`ReactiveBoundaryTrigger`/`BoundaryTriggerArbiter`） | `Agent.Calculate.BoundaryPressureCalculator` + `Agent.Contract.Boundaries` + `AgentLog` | 纯数值评估内核可通用，但压力计算内核在 Agent.Calculate |
| `DataStream` 三件套（`ReactiveZoneBridge`/`ReactiveTrigger`/`EventSink`） | `Agent.Core.OutputZoneWriter`/`RunEventFactory`、`Agent.Axioms.AgentLog`、`Agent.State.ReactiveConsumptionState`、`RunEvent` | 响应式数据区集成可通用，但被结果/事件物化 + 运行态持有阻塞 |
| `NodeStore` 门面 | `NodeExecutionService` + `NodeInputResolution` + Agent 侧取终答 `WorkflowResultDeriver`（依赖 `ExecResultDocument`） | 门面本身通用，但依赖 P1 节点引擎 |

---

## 4. 基础支撑设施下沉（最大杠杆解锁前置）

> 这是解锁 §3 多个项（`RoundSpecLoader` / `BoundaryConfigLoader` / `RuleSet` 配置段等）的公共前置。当前这些**通用配置/路径/日志基础设施**都落在 Agent 侧。

### 4.1 目标迁移清单

| 现位置（Agent 侧） | 建议落点 | 说明 |
|------|------|------|
| `Agent.Axioms\Infrastructure\ConfigFileLoader.cs` | `HierarchyMethod.Abstractions`（或 Share 层） | 统一「解析路径→读取→校验→StrictConfig fail-fast→兜底」加载模板；剥离 `AgentLog`（参数化 logger 回调）与 `AgentCoreRuntime.StrictConfig`（框架内置配置开关） |
| `Agent.Axioms\Infrastructure\ConfigJson.cs` | 同上 | 配置/规则 JSON 反序列化共用选项（含 `KeyMappingConfigJsonConverter`） |
| `Agent.Axioms\Infrastructure\ConfigSourceResolver.cs` | 同上 | 配置源路径解析 |
| `Agent.Axioms\Infrastructure\RepositoryPathLocator.cs` | 同上 | 仓库根向上回溯定位 |
| `Agent.Axioms\Infrastructure\RepositoryPaths.cs` | 同上 | 仓库路径常量 |
| `Agent.Axioms\Infrastructure\KeyMappingConfigJsonConverter.cs` | 同上 | `KeyMappingConfig` JSON 双形态转换器 |
| `Agent.Runtime\State\Infrastructure\AgentCoreRuntime.cs` | 拆分 | 仅 `StrictConfig` 标志随配置设施迁出；其余运行时状态留在原处 |
| `Agent.Axioms\Diagnostics\AgentLog.cs` | 抽象化 | 框架侧用 logger 端口/回调，Agent 侧经 `AgentLog` 实现；框架零日志依赖 |

### 4.2 依赖剥离要点

- `ConfigFileLoader` 现依赖 `AgentLog` + `AgentCoreRuntime.StrictConfig` + `ConfigSourceResolver`/`RepositoryPathLocator`/`RepositoryPaths`（均随迁）+ `KeyMappingConfigJsonConverter`（经 `ConfigJson`）。
- 迁入框架时必须：
  1. 把 `AgentLog.Log(...)` 改为「注入/回调式 logger」（缺省 no-op，Agent 装配时绑定 `AgentLog`）。
  2. `StrictConfig` 收敛为框架可写配置开关（Agent 组合根注入），不引 `Agent.Runtime`。
  3. 保持 `ConfigFileLoaderLog`（四消息 ID）契约不变，避免改 `agent_log_messages.json`。
- 命名空间迁至 `Operation.HierarchyMethod.Abstractions`，经 `global using` 别名保持 `Agent.Axioms.ConfigFileLoader` 等旧名可解析。

### 4.3 消费方影响面（迁出后需同步别名/引用）

- `BoundaryConfigLoader` / `RuleSetLoader` / `RoundSpecLoader`（配置加载）
- `ConfigStateDirector` / 其他经 `ConfigJson` 反序列化的路径
- 凡是 `using Agent.Axioms.Infrastructure` + 引用上述类型的 Agent 文件，迁移后经别名保持编译。

---

## 5. 分阶段建议顺序（按解锁杠杆排序）

| 阶段 | 内容 | 解锁 |
|------|------|------|
| **S0（已做）** | P0：`NodeOutputStore` + `JsonElementRead` | 消除重复、补框架默认实现 |
| **S1（已做）** | P2：`RuleEngine`/`RuleSet` 纯核心 | `rule_match`/`rule_needs_replan` 通用化 |
| **S2（下一步）** | 基础配置设施下沉（§4） | 解锁 `RoundSpecLoader`/`BoundaryConfigLoader` 等配置加载类 |
| **S3** | `RoundSpecLoader` 通用化（加载器入框架、默认骨架留 Agent） | 声明式骨架加载通用 |
| **S4** | 边界评估纯函数内核（`BoundaryPressureCalculator` 入框架 + 端口化 LLM 自评） | 上下文边界/注意力压力通用 |
| **S5** | 节点引擎泛化（`NodeExecutionService`/`RoundStepInvoker`/`WorkflowRunner`，以 `RoundSpec`+`NodeDefinition` 为唯一契约、端口化 KeyMapping/函数注册/引用守卫） | P1 核心 |
| **S6** | `NodeStore` 门面 + `DataStream` 三件套 + `RoundLoopDriver`（视循环驱动承载层决策） | 框架自足 |

> 每阶段以「构建 0 错误 + 对应回归测试通过」为完成判据，逐阶段独立可验证，避免一次性大迁移。

---

## 6. 风险与约束

- **依赖方向**：`HierarchyMethod` 禁止反向引用 `Agent.*`；迁入文件涉及 `AgentNode`/`RoundKeys`/`DecisionWordClassifier`/`AxiomLoopResult`/`SchedulerAction` 的必须参数化（`TResult`）或端口化（handler/interface）。
- **重复双写**：`NodeOutputStore` 曾 Agent 与框架并存（已消除）；`WorkflowResultDeriver` 仍有「应用侧取终答」与「框架纯链尾索引」两份——只把通用索引归框架，取文本留 Agent。
- **双向 `ITaskExecutor`**：框架 `Tasks.Ports.ITaskExecutor`（`ExecutionNode` 级，通用）与 Agent `TaskPool\ITaskExecutor.cs`（`AgentTaskItem` 级，池级）是不同契约，后者保留 Agent，勿合并。
- **GlobalUsings 一致性**：主 `GlobalUsings.cs` 与镜像测试 GlobalUsings 须同步补别名；误删段（如 History）会静默破坏消费方，改后必须核验完整。
- **references.md**：若框架新文件定义 clinerules `[CR]` 符号，须 `-UpdateIndex` 重编译注册，避免悬空引用。

---

## 7. 变更记录

| 日期 | 变更 |
|------|------|
| 2026-08-25 | P0（`NodeOutputStore`+`JsonElementRead`）完成并验证 |
| 2026-08-25 | P2（`RuleEngine`/`RuleSet` 纯核心）完成并验证 |
| 2026-08-25 | 整理阻塞映射与基础配置设施解锁方案（本文件） |

