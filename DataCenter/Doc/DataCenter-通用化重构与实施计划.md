# DataCenter.Core 通用化重构与实施计划

> **日期**：2026-09-12 | **对象**：`Frame\DataCenter\DataCenter.Core` | **定位**：治理型重构（非新功能）
> **一句话**：框架层**不得出现领域词汇**（loop / decision / eval / budget / round…）。把领域逻辑的**控制权反转**给扩展方——Core 只提供通用状态能力与**端口**，领域（循环裁决、图、工作流…）经扩展工程**注入**。
> **关联**：`Doc\DataCenter-架构设计.md`（立项与红线）、`Doc\DataCenter-架构设计.md §5.2`（迁移台账）、`Docs\analysis\2026\9\9.4\2026-09-04-StateHoldDriven-状态与决策统一通用框架.md`（R7/R8 来源）

---

## 1. 问题陈述：Core 中的领域泄漏（代码事实）

| # | 泄漏点 | 证据 | 性质 |
|---|---|---|---|
| L1 | **整包决策域** | `Decision\LoopStateStore.cs`（含 `CoreStateView{BudgetLevel,TokenValue,TokenMax,Halt,Round,LastEval}`、`LoopWorkingState : ILoopState`）、`Decision\DecisionCenter.cs`、`Decision\DecisionKeys`（`LoopKeys`：`core.budget.level`/`core.token.value`/`core.flag.halt`/`core.round`/`decision.latest`）、`Decision\DecisionRecord.cs`（`LoopDecision`/`EvalOutcome`/`ErrorClass`/`DecisionWords`） | **领域逻辑**（循环裁决 × 状态落地） |
| L2 | **依赖被领域拖入** | `DataCenter.Core.csproj` → `<ProjectReference Include="$(CsharpProjectRoot)\Share\Orchestration\TaskStatusPool\TaskStatusPool.csproj" />`；**该引用的唯一用途就是 L1** | 领域依赖污染 |
| L3 | **宿主/引擎设施直引** | `Core\StateCenter.cs` 直接使用 `IStorageCell`（`TConfig.ContextFrame.Abstractions` + `Modules.InternalStorage.Core.StorageCell`）与 `BasicMethod.Block` 的 `StateOrchestrator`；`Blocks\StateCenterBlock.cs` 使用 `FrameworkAbstractions.ComponentRegistry.AppServiceProvider`（服务定位器） | **平台/宿主耦合**（C# 引擎设施） |
| L4 | **注释层领域残留** | `Core\StateCenter.cs:125`："轮内执行期瞬态（**Agent Vars / 骨架临时空间**）"；`Layering\StateLayerScheme.cs:7`："泛化自 **Agent** 侧的三层键工厂（原 **AgentStateKeys**.Scheduler/Session/Round）" | 语义注释把领域写进通用件 |
| L5 | **命名遗留** | 命名空间 `Operation.StateBlock.*`（"Operation" 是 CsharpPrj 层级名、"StateBlock" 是旧模块名），与 `DataCenter.Core` 定位不符 | 语义漂移 |
| L6 | **数据面把引擎层名当枚举** | `schemas\csg-runtime-data.schema.json` 的 `id` 正则硬编码 `(config\|session\|transient)` | 中立性缺口（跨语言消费者被迫接受引擎分层名） |
| L7 | **死代码** | `Extensions\StateBlockExtensions.cs` 为空类（0 成员） | 冗余（`Strategy.RedundancyElimination`） |

**关键事实（降低迁移风险）**：L1 的领域件**消费方仅 2 类**——(1) 测试：`Environment\CentralTest\CSharpTest\Operation.Tests\StateHoldDriven.Tests\{LoopStateStoreTests,DecisionFrameworkTests}.cs`（`LoopStateStore`/`DecisionCenter`/`DecisionRecord`）；(2) **产品侧 1 处**：`Projects\Plug\Agent\src\Agent.Core\Loops\{AgentRunner.cs, AgentRunner.Persistence.cs, AgentRunner.Pipeline.cs}`（经 `Operation.StateBlock.Decision` 消费 `DecisionCenter`，把轮决策落 Center 供审计/回放）。
> **勘误（2026-09-12 实施时实测）**：本节初稿称"领域件没有任何产品消费方、全仓仅测试使用"**不成立**——`Agent.Core` 确为消费者。因此 G1 必须补一步"Agent 侧二进制接线"（`Projects\Plug\Agent\Directory.Build.props` 的 `DataCenterBinaryRefs` 清单），否则 `Agent.sln`/`PrjBridge` 断链。命名空间保留策略使该消费方**源码零改动**，仅需新增程序集引用。


**基线扫描证据**（对 `DataCenter.Core` 全量 `.cs` 扫 `Loop|Decision|EvalOutcome|FailureBudget|ProgressToken|ControlFlag|TaskStatusPool`）：

| 文件 | 命中数 |
|---|---|
| `Decision\LoopStateStore.cs` | 44 |
| `Decision\DecisionCenter.cs` | 13 |
| `Decision\DecisionRecord.cs` | 10 |
| `Decision\LoopKeys.cs` | 9 |
| **其余全部文件（StateCenter / StateStore / Layering / Blocks / Extensions）** | **0** |

→ 领域泄漏**恰好且仅仅**集中在 `Decision\*` 一个目录。**G1 阶段只需外移该目录，即可让 Core 的领域词汇归零**（注释项 L4 另以文案修正）。

---

## 2. 通用化不变式（重构的目标态）

| # | 不变式 | 判据 |
|---|---|---|
| G1 | **领域词汇零出现**：`DataCenter.Core`（含注释/schema）不得出现 `Loop`/`Decision`/`Eval`/`Budget`/`Token`/`Flag`/`Round`/`Agent`/`Engine` 等域名词 | `grep -iE "loop\|decision\|eval\|budget\|token\|flag\|round\|agent"` 仅命中"通用语义"白名单 |
| G2 | **依赖闭合**：Core 只依赖「协议（CSG）+ 契约 + 纯基础设施端口」，不依赖任何**语义域**工程（`TaskStatusPool`/`WorkFlowMethod`/`FunctionStorage`/产品） | csproj 引用清单评审 |
| G3 | **设施端口化（IoC）**：存储单元、分发器、服务定位、初始化编排一律经**DataCenter 自有端口**注入；宿主设施（`IStorageCell`/`Block`/`AppServiceProvider`）只出现在 **Adapters** | Core 内不出现 `TConfig.ContextFrame`/`BasicMethod.Block`/`FrameworkAbstractions` 类型 |
| G4 | **领域经扩展注入**：域逻辑以 `DataCenter.Extensions.*` 工程**消费** Core 端口实现（如循环决策落状态），Core 不反向知道域存在 | 扩展工程单向引用 Core |
| G5 | **数据面中立**：CSG `csg://runtime-data` 的分层维度为**注册制**而非封闭枚举 | schema 无引擎层名硬编码 |
| G6 | **契约与实现分离**：Core 暴露 `DataCenter.Abstractions`（纯端口，零实现、零第三方）供域/产品只依赖契约 | Abstractions 无 ProjectReference 至实现工程 |

> G3/G4 即本轮要的「**把 loop/decision 逻辑控制反转**」：不是删除能力，而是**把"谁拥有这段逻辑"从框架反转给域**。

---

## 3. 控制反转设计

### 3.1 反转的三个轴

```
轴 A（词汇反转）：领域词汇出 Core
   LoopStateStore / CoreStateView / LoopWorkingState / DecisionCenter / LoopKeys / DecisionRecord
        ── 从 DataCenter.Core ──▶  DataCenter.Extensions.LoopDecision（域扩展，消费 Core）

轴 B（设施反转）：引擎/宿主设施出 Core
   IStorageCell / StorageCell / StateOrchestrator(Block) / AppServiceProvider
        ── 从 Core 直引 ──▶  端口（Core 定义）+ Adapter（Adapters 实现）

轴 C（命名反转）：中立命名上位
   Operation.StateBlock.*  ──▶  DataCenter.Core.*（TypeForwardedTo 平滑迁移）
```

### 3.2 Core 的通用端口集（IoC 接缝）

| 端口（`DataCenter.Abstractions`） | 职责 | 默认实现（Core） | 领域/宿主替换方 |
|---|---|---|---|
| `IDataCell` | 分层数据单元原语：`Get/Set/SetQuiet/Remove/Contains/GetKeys/GetEntries/ClearLayer/DataChanged` | `InMemoryDataCell`（Core 内置，零依赖） | `StorageCellAdapter`（Adapters ← `Modules.InternalStorage`） |
| `IDataCellFactory` | 由父 cell 派生 scope 分区 cell（`CreateChild(name)`） | `InMemoryDataCellFactory` | 同上 |
| `IStateDispatch` | 广播/事件出队派发（写线程不执行订阅回调） | 内联同步（null 语义） | `PublishDispatcherAdapter`（Adapters ← `Share.StateManagement`/`Modules.Threading`） |
| `IStateProviderRegistry` | 生命周期 Provider 注册表（`IScopeLifecycle`/`ICompositionLifecycle`/`IContainerLifecycle`） | Core 内置（`StateLifecycleProviders` 上收） | 无（通用） |
| `IStateKeyScheme` | 键方案：域 + tier→命名空间映射（**不含任何具体域常量**） | `TierNamespaceScheme`（Config/Session/Transient 三段可配置名） | 域自备（如 `AgentStateKeys`） |
| `IStateValueCodec` | 值↔载荷编解码 | `JsonValueCodec` | 域/宿主定制 |
| `IStateProjector` | 状态 → CSG `CanonicalForm`（Configuration/StateMachine/Graph 三投影） | `TierStateProjector`（通用结构投影） | 域补充语义投影（loop 投影、图投影） |
| `IStateMigration` | `schemaVersion` 演进（diff/apply） | 无操作直通 | 插件代数/域 |
| `IGenerationParticipant` | 插件代际 `Export/Import/Migrate`（热更新带数据） | 无操作直通 | 插件 |
| `IStateInitializer` | 初始化编排（**取代 `StateCenterBlock` 的 Block 依赖**） | `NoOpStateInitializer` | 宿主（`BlockStateInitializer` ← `BasicMethod.Block`，在 Adapters） |

**设计要点**：Core 只**声明并消费**这些端口；`StateCenter` 的构造从
`StateCenter(IStorageCell cell, IPublishDispatcher? dispatcher)` 改为
`StateCenter(IDataCell root, IDataCellFactory factory, IStateDispatch? dispatch, IStateProviderRegistry? providers)`
——**引擎设施不再出现在 Core 的类型签名里**。

### 3.3 目标工程结构（Frame\DataCenter）

```
Frame\DataCenter\
├── DataCenter.Abstractions\            纯端口 + 值类型（零实现、零第三方依赖）
│   ├── Cells\        IDataCell / IDataCellFactory
│   ├── Keys\         StateKey / IStateKeyScheme / TierId
│   ├── Tx\           IStateTransaction / IStateSubscriptionHub / IStateDispatch
│   ├── Projection\   IStateProjector / CanonicalForm 桥契约
│   ├── Lifecycle\    IScopeLifecycle / ICompositionLifecycle / IContainerLifecycle
│   └── Generation\   IGenerationParticipant / IStateMigration
├── DataCenter.Core\                    ★ 通用实现（G1/G2/G3 达标）
│   ├── Cells\        InMemoryDataCell（默认 tier 后端）
│   ├── Keys\         TierNamespaceScheme / LayeredStateKey / LayeredStoreClient
│   ├── Core\         StateCenter / StateStore / StateOrchestrator / 订阅 / 事务
│   └── Projection\   TierStateProjector / JsonValueCodec
├── DataCenter.Adapters\                宿主设施适配（引擎设施只在此处）
│   ├── StorageCellAdapter / PublishDispatcherAdapter
│   ├── BlockStateInitializer（← BasicMethod.Block）/ AppServiceRegistration（← FrameworkAbstractions）
│   └── （CsharpPrj 经权威烘焙二进制引用，对齐 CallCenter.Adapters 约定）
├── DataCenter.Extensions.LoopDecision\ 域扩展示例：循环裁决 × 状态落地
│   └── 原 DataCenter.Core\Decision\*（消费 Core.Layering + Share.TaskStatusPool.Loops）
├── DataCenter.Mcp\                     （可选）数据面 MCP 暴露
├── DataCenter.Host\                    Exe：--dump-state / --swap-generation / --migrate
├── schemas\                            csg://runtime-data（tier 注册制，无引擎层名硬编码）
└── Doc\
```

### 3.4 领域扩展的形态（以 LoopDecision 为例）

```csharp
// DataCenter.Extensions.LoopDecision（域侧，单向依赖 Core + TaskStatusPool）
// ① 域自备键方案（域常量留在域，Core 无感）
static readonly IStateKeyScheme Scheme = TierNamespaceScheme.ForDomain("StateBlock.Decision");
// ② 域门面 = 通用分层客户端 + 域裁决语义（裁决权威仍在 Share.TaskStatusPool.Loops，R7）
public sealed class LoopStateStore(IDataCell cell, IStateDispatch? d, string scope) {
    // 内部 new LayeredStoreClient(center, Scheme)（Core 通用件）
    public CoreStateView View();          // 域只读投影
    public ILoopState LoadWorking();      // 域工作副本
    public void Save(ILoopState w);       // 域单一写口
}
public sealed class DecisionCenter(IStateCenter center) { void Record(...); DecisionRecord? Recent(...); }
```

---

## 4. 迁移映射表（现状 → 目标）

| 现状（`DataCenter.Core`） | 目标归属 | 动作 |
|---|---|---|
| `Decision\LoopStateStore.cs` | `DataCenter.Extensions.LoopDecision` | **移动**（域扩展；命名空间 `Operation.StateBlock.Decision` 保留，测试零改动） |
| `Decision\DecisionCenter.cs` | 同上 | 移动 |
| `Decision\LoopKeys.cs` | 同上 | 移动（域常量回归域） |
| `Decision\DecisionRecord.cs` | 同上 | 移动 |
| `Core\StateCenter.cs` | `DataCenter.Core\Core` | **改造**：`IStorageCell`→`IDataCell`；Transient 注释去领域化 |
| `Core\StateStore.cs` | `DataCenter.Core\Core` | 改造：`IStorageCell`→`IDataCell` |
| `Layering\LayeredStateKey.cs` | `DataCenter.Core\Keys` | 原样（已通用） |
| `Layering\LayeredStoreClient.cs` | `DataCenter.Core\Keys` | 原样（**核心 IoC 接缝**） |
| `Layering\StateLayerScheme.cs` | `DataCenter.Core\Keys` | 更名为 `TierNamespaceScheme`（实现 `IStateKeyScheme`），保留 `ForDomain` |
| `Blocks\StateCenterBlock.cs` | `DataCenter.Adapters` | **移动并更名**为 `BlockStateInitializer`（`Block`/`AppServiceProvider` 属宿主设施；Core 侧以 `IStateInitializer` 端口替代） |
| `Extensions\StateBlockExtensions.cs` | — | **删除**（空类） |
| `DataCenter.Core.csproj` 的 `$(Proj_TaskStatusPool)` | — | **删除引用**（仅 L1 需要） |
| `DataCenter.Core.csproj` 的 `$(Proj_TConfig)/Modules.InternalStorage/BasicMethod.Block` | `DataCenter.Adapters` | 下沉（Core 改引 `DataCenter.Abstractions`） |
| `schemas\csg-runtime-data.schema.json` | 同址 | 改造：`tier` 注册制（去 `config\|session\|transient` 硬编码） |

---

## 5. 分阶段实施计划

> 每阶段**独立编译 + 单测通过**再进下一阶段；每阶段可回退（命名空间保留 + 类型转发）。

### D0 · 立项与 StateCenter 家族上收 ✅ **已完成（2026-09-12）**

| 项 | 内容 | 验收（已实测） |
|---|---|---|
| D0.1 | 建 `Frame\DataCenter`（Directory.Build.props / GlobalUsings / 聚合 csproj / slnx / schema / Doc），对标 CallCenter | `DataCenter.slnx` 0 错 0 警 |
| D0.2 | `StateCenter`/通用 `StateStore`/`Layering`/`Decision`/`StateCenterBlock` 迁入 Core | 编译 0 错 |
| D0.3 | 产品端口剥离 → `StateHoldDriven\Core\OperationStateStore.cs` | **R-DC2** 保持 |
| D0.4 | Agent `DataCenterBinaryRefs` 清单接线 | `Agent.sln` / `GameEditor.sln` / `PrjBridge` / `MainProj.sln` 全绿 |
| D0.5 | 行为等价回归 | **StateHoldDriven.Tests 73/73**；**Agent.Axioms.Tests 84/84** |

### G1 · 领域出 Core（本重构第一批，风险最低、收益最直观） ✅ **已完成（2026-09-12）**

> 实施记录见 §9。G1.1 的"测试工程加引用"实际落点是**两处二进制接线**（测试 + Agent），详见 §9.1。


| 步骤 | 内容 | 判据 |
|---|---|---|
| G1.1 | 新建 `DataCenter.Extensions.LoopDecision`（引 `DataCenter.Core` + `Share.TaskStatusPool`）；移入 `Decision\*` 4 文件 | 编译 0 错；测试工程加引用后 73/73 仍绿 |
| G1.2 | `DataCenter.Core.csproj` 删 `$(Proj_TaskStatusPool)` | **G2 达标**：Core 依赖仅 `CSG.Unified` + 契约 + 设施 |
| G1.3 | 删除空类 `Extensions\StateBlockExtensions.cs` | 冗余归零 |
| G1.4 | 注释中立化（`StateCenter` Transient 注释去 "Agent Vars/骨架"） | G1 grep 通过 |
| G1.5 | 校验器：新增 `Check-DataCenterGenericity.ps1`（对 Core 扫描领域词表，命中即失败） | 门禁可执行、可回归 |

### G2 · 设施端口化（IoC 主干） ✅ **已完成（2026-09-12，重载并存过渡；A3 为"契约强制白名单"态）**

> 实施记录见 §10。关键纠偏：`IStateCenter.Cell`（返回 `IStorageCell`）被 **Agent/Engine 产品侧直接消费**（`ECSStorage(center.Cell)`、`StateContextAdapter.Cell`），故本轮**必须保留契约视图**，A3 以"自主依赖归零 + 契约强制点逐条白名单"达成。

| 步骤 | 内容 | 判据 |
|---|---|---|
| G2.1 | 新建 `DataCenter.Abstractions`（零依赖）：`IDataCell`/`IDataCellFactory`/`IStateDispatch`/`IStateKeyScheme`/`IStateProjector`/`IGenerationParticipant`/`IStateMigration`/生命周期三端口 | Abstractions 无 ProjectReference |
| G2.2 | Core 建 `InMemoryDataCell`（tier 字典 + 父链回退 + `DataChanged`）；`StateCenter`/`StateStore` 改吃 `IDataCell` | 单测：cell 语义等价（Get/Set/SetQuiet/枚举/父链回退） |
| G2.3 | 新建 `DataCenter.Adapters`：`StorageCellAdapter`(←`Modules.InternalStorage`)、`PublishDispatcherAdapter`、`BlockStateInitializer`(←`BasicMethod.Block`)、`AppServiceRegistration`(←`FrameworkAbstractions`) | Core 内不再出现引擎/宿主类型（G3 达标） |
| G2.4 | 消费方切换：`StatePipelineBuilder` 用 `BlockStateInitializer`；Agent/FrontEndDriven/Engine 传入 Adapter 构造的 `StateCenter` | 全仓编译 + 4 个测试工程全绿 |

### G3 · 命名中立化

| 步骤 | 内容 | 判据 |
|---|---|---|
| G3.1 | Core 命名空间改 `DataCenter.Core.*`（`Keys`/`Core`/`Cells`/`Projection`） | 编译 0 错 |
| G3.2 | 兼容：`Operation.StateBlock.*` 处加 `[assembly: TypeForwardedTo]`（旧 dll 指向新 dll） | 旧引用方零改动仍编译 |
| G3.3 | `grep` 旧命名空间仅剩转发声明 | 迁移完成 |

### G4 · 数据面中立化 ✅ **G4.1 已完成（2026-09-12）；G4.2 待办**

| 步骤 | 内容 | 判据 |
|---|---|---|
| G4.1 | schema `tier` 改注册制（`tier` 为字符串 + `tiers[]` 注册表；默认集 `config/session/transient` 仅作**示例**） | schema 无引擎层名作为**唯一**取值 |
| G4.2 | 增加 `IStateProjector` 的 CSG 契约校验（`CsgFacade.Validate`） | 投影产物过 CSG 校验 |

### G5 · `Share.StateManagement` 降纯契约（R1' 修订）

| 步骤 | 内容 | 判据 |
|---|---|---|
| G5.1 | 实现上收：`StateOrchestrator`/`StateManager`/`StateActor`/`ScopeLifecycleStateMachine`/`StateLifecycleProviders`/`StateSubscriptionHub`/`StateTransaction`/`InMemoryStateStore`/`ObjectKeyValueStore` 等 → Core | 7 个引用工程编译绿 |
| G5.2 | `Share.StateManagement` 只留 `Abstractions/` + `Models/`（契约） | 契约工程无实现类型 |
| G5.3 | 无关子域另择址：`Pooling\**`、`Stages\**`、`NodeExecutionCoordinator`/`ExecutionStateMachine`/`TaskExecutionState`/`TtlCache`/`GenericStore` → 执行/池域工程 | 数据中心不含无关域 |
| G5.4 | 旧类型 `dep` + 类型转发 + 限期退役 | `ANTI-DUPLICATED-IMPLEMENTATION` 归零 |

### G6 · 代数 / 迁移 / 热换（"带着运行数据热更新"）

| 步骤 | 内容 | 判据 |
|---|---|---|
| G6.1 | CallCenter 的 `HotReloadVersionStore`/`TrackHotReloader`/原子换快照 **上移** Core，CallCenter 反向引用 | `CallCenter.Tests` 全绿；两份实现归零 |
| G6.2 | `ISwapPoint`（任务池暂停 → 轮边界 `Commit` → 换代 → 恢复） | 单测：在途 run 不中断、结果不丢 |
| G6.3 | `IStateMigration` + `schemaVersion` + CanonicalForm diff/apply；失败保留旧代数 | 单测：迁移失败不破坏当前代数 |
| G6.4 | `IGenerationParticipant` 端到端：`Export/Import` 换代前后状态连续 | 单测：`budget/token/…` 跨代连续 |

### G7 · 消费方接入与插件可收集

> **单根持有的设计依据**：见 `DataCenter-可追踪数据流图框架-设计与实施.md §12`（根实例契约 `IDataCenterRoot`/`IRootProvider`/`ISwapPoint`、取得纪律、生命周期与换代、反模式清单）。G7.1–G7.3 即该章的落地步骤。

| 步骤 | 内容 | 判据 |
|---|---|---|
| G7.1 | Agent 删除插件内 `new StateCenter(...)`（`AgentOrchestrator.cs:93`、`WorkflowRunner.cs:89` 含第二根）与静态槽 | `grep` `new StateCenter(` 于 `Projects\Plug` 归零 |
| G7.2 | `ExtensionAssemblyHost` 加 `isCollectible` 代数 ALC 工厂 | `WeakReference` 断言旧代数已回收 |
| G7.3 | 宿主（PrjBridge/FileDriven）持有 DataCenter 单实例 + 代数交换入口 | 端到端：热换 + 数据连续 + ALC 回收证 |
| G7.4 | Engine / FrontEndDriven 接入（`EngineStateCenter`/`StateCenterFactory`） | 各自编译 + 冒烟 |

### G8 · 跨语言终端面

| 步骤 | 内容 | 判据 |
|---|---|---|
| G8.1 | TS Adapter（复用 `TsWorkspace\Protocol` 的 CSG 实现） | C#/TS 同一 `CanonicalForm` 往返等价 |
| G8.2 | PS Adapter / `DataCenter.Mcp`（数据面 MCP 暴露） | 冒烟脚本通过 |
| G8.3 | `DataCenter.Host` CLI（`--dump-state`/`--swap-generation`/`--migrate`） | 冒烟通过 |

---

## 6. 验收判据

| # | 判据 | 命令/方法 |
|---|---|---|
| A1 | **领域词汇零出现**（G1）：`DataCenter.Core` 内不含 `Loop`/`Decision`/`Eval`/`Budget`/`Token`/`Flag`/`Round`/`Agent`/`Engine`（白名单：`Dynamic`/`Tokenization` 之类误报除外） | `Check-DataCenterGenericity.ps1` |
| A2 | **依赖闭合**（G2）：`DataCenter.Core.csproj` 无 `TaskStatusPool`/`WorkFlowMethod`/`FunctionStorage`/产品引用 | csproj 评审（脚本校验引用白名单） |
| A3 | **设施端口化**（G3）：Core 内不出现 `IStorageCell`/`StorageCell`/`BasicMethod.Block`/`FrameworkAbstractions` 类型 | `grep` |
| A4 | **领域经扩展注入**（G4）：`DataCenter.Extensions.LoopDecision` 单向引用 Core；Core 无任何对扩展的反向引用 | 引用图校验 |
| A5 | **行为等价**：`StateHoldDriven.Tests` 73/73 保持全绿（迁移前后测试代码零改动） | `dotnet test` |
| A6 | **全仓编译**：`DataCenter.slnx`/`MainProj.sln`/`Agent.sln`/`GameEditor.sln`/`PrjBridge` 全绿 | `dotnet build` |
| A7 | **跨语言**：同一 `CanonicalForm` C#↔TS 往返等价（同一 `csgVersion`） | 往返单测 |
| A8 | **热更新**：换代前后状态连续；旧代数 `WeakReference.IsAlive == false` | G6/G7 单测 |

---

## 7. 风险与诚实边界

| 风险 | 说明 | 缓解 |
|---|---|---|
| **G2（端口化）是破坏性改动** | `StateCenter` 构造函数签名变化波及 Agent/FrontEndDriven/Engine/测试 | 分两步：先**重载并存**（旧 `IStorageCell` 构造内部适配 `InMemoryDataCell` 包装 → 保留 `StorageCellAdapter`），消费方逐一迁移后再删旧重载 |
| **G5 波及 7 个工程** | `Share.StateManagement` 降契约会动 DataStoreDriven/FrontEndDriven/StateHoldDriven/WorkFlowMethod/CommandFramework/AiScheduling/Share | 分批 + 类型转发 + `dep` 限期；每批编译 + 测试 |
| **跨树依赖边** | `Frame\CsharpPrj`（StateHoldDriven）→ `Frame\DataCenter` 是本轮**新增**的依赖方向（原 CsharpPrj 不自引 Frame 平台库） | 已在架构文档登记；若判定不可接受，替代方案 = 把 `Operation.StateBlock` 整体上移为 Frame 平台库，使方向变为 Frame→Frame |
| **`DataCenter.Extensions.*` 归属** | 域扩展放 Frame 平台层 vs 回归 `Share.TaskStatusPool` 邻域 | 见 §8 待决策；默认取「Frame 平台层扩展」（保持 Core 通用 + 方向一致） |
| **"载体即语义"风险** | 端口抽象若过度，反而增加一次跳转与调试成本 | 端口仅在**确有两个以上实现者**时引入（`IDataCell` 有 InMemory/StorageCell 两实现 ✓；不预置空端口） |
| **热更新非零成本** | 需交换点 + 排空；共享栈（`WorkFlowMethod`/`Share.*`/`Plate.*`）与 UI/native 不可热换 | 三域清单化；只承诺实现域换代 |

---

## 8. 待决策与建议默认

| # | 议题 | 建议默认 |
|---|---|---|
| P8.1 | 域扩展工程归属：`Frame\DataCenter\DataCenter.Extensions.LoopDecision` vs 回归 `Share.TaskStatusPool` 邻域 | **Frame 平台层扩展**（Core 通用性可被证明；依赖方向与 CallCenter→CSG 一致） |
| P8.2 | `StateCenter` 构造签名切换节奏：一次到位 vs 重载并存过渡 | **重载并存过渡**（先兼容后收紧，保证每步可编译可回退） |
| P8.3 | 命名空间切换时机（G3） | **放在 G5 之后**（等实现上收完成，一次改名收益最大、转发层最少） |
| P8.4 | 校验器（A1/A2）是否纳入 CI 门禁 | **纳入**（`PowerShellScript` 既有 DSL 校验同风格；防回退） |

---

## 9. 实施记录（B0 + G1 + G4.1，2026-09-12）

### 9.1 交付清单

| 步骤 | 落地 | 证据 |
|---|---|---|
| B0.1 | 新增门禁 `Frame\PowerShellScript\Operation\DSL\Check-DataCenterGenericity.ps1`（`-Check` 非零退出；`-Words`/`-Whitelist` 可配；扫描面 = Core 源码 + Core csproj + schemas） | 改造前 145 命中 → 改造后 **0** |
| B0.2 | 基线证据：`DataCenter.slnx` 0 错；`StateHoldDriven.Tests` 73/73 | 改造前实测 |
| G1.1 | 新建 `Frame\DataCenter\DataCenter.Extensions.LoopDecision\`（ModuleRule Layer 3；引 Core + TaskStatusPool），`Decision\*` 4 文件整体外移（命名空间 `Operation.StateBlock.Decision` 保留 → 消费方源码零改动）；注册进 `DataCenter.slnx` + 聚合 `DataCenter.csproj` | 编译 0 错；`DataCenter.Core.dll` 30720→19968 字节且**不再含** `LoopStateStore` 符号（findstr 证）；扩展 dll 含之 |
| G1.1a | **消费方二进制接线（两处，勘误补步）**：① `Environment\CentralTest\CSharpTest\Operation.Tests\StateHoldDriven.Tests\StateHoldDriven.Tests.csproj` 增 `DataCenter.Core`/`DataCenter.Extensions.LoopDecision` 两条 `<Reference HintPath>`（CentralTest 通配清单只扫 CsharpPrj 烘焙目录，Frame 平台库产物需显式接线）；② `Projects\Plug\Agent\Directory.Build.props` 的 `DataCenterBinaryRefs` 清单增扩展 dll | 测试输出目录含两 dll；`Agent.sln`/`PrjBridge` 恢复绿 |
| G1.2 | `DataCenter.Core.csproj` 删 `TaskStatusPool` 引用 | Core 编译 0 错 |
| G1.2a | **G1.2 的连锁发现**：删引用后暴露 `StateCenterBlock` 用的 `FrameworkAbstractions.ComponentRegistry` 原为 `TaskStatusPool → EventBindingOrchestrator → FrameworkAbstractions` 的**隐式传递依赖**；G1 将其**显式化**（`$(CsharpProjectRoot)\Share\Foundations\FrameworkAbstractions\FrameworkAbstractions.csproj`），G2 时随 `StateCenterBlock` 下沉 Adapters。注：DataCenter 构建未 import `modules.generated.props`，`$(Proj_*)` 变量不可用，须用显式相对路径 | 编译 0 错 |
| G1.3 | 删除空类 `DataCenter.Core\Extensions\StateBlockExtensions.cs`（含空目录） | Core 文件数 11→6 |
| G1.4 | 注释/元数据中立化 4 处：`Core\StateCenter.cs`（去 "Agent Vars/骨架"）、`Layering\StateLayerScheme.cs`（去 "Agent 侧/AgentStateKeys"）、`DataCenter.Core.csproj` 与聚合 `DataCenter.csproj` 的 `ModuleRule_Rules`（R-DC2 去 Agent/Engine 举例；R7 改为 G4 单向注入表述） | 门禁 0 命中 |
| G1.5 | 门禁可执行并可回归（同上 B0.1） | `-Check` exit=0 |
| G4.1 | `schemas\csg-runtime-data.schema.json`：节点 `id.pattern` 去 `(config\|session\|transient)` 硬编码 → `^csg://[^/]+/[^/]+/[^/]+/.+$`；新增 `metadata.tiers` **分层标识注册表**（`config/session/transient` 降为 `default` 示例集）；节点属性描述 `layer=` → `tier=`（注册制）；`domain`/`scope` 描述去领域举例（原 "Agent / StateBlock.Decision / Engine"、"roundId"） | 门禁对 schemas 0 命中 |

### 9.2 验收结果（实测）

| 判据 | 结果 |
|---|---|
| A1 领域词汇零出现 | ✅ `Check-DataCenterGenericity.ps1 -Check` → 命中 0，exit 0 |
| A2 依赖闭合（TaskStatusPool 已移除） | ✅ Core 现依赖：CSG.Unified / Share.StateManagement / TConfig / InternalStorage / Block / FrameworkAbstractions（末项为 G2 待下沉项） |
| A3 设施端口化 | ⏳ 未达标（G2 范围）：Core 内仍有 `IStorageCell`/`BasicMethod.Block`/`FrameworkAbstractions` |
| A4 领域经扩展注入 | ✅ 单向：扩展 → Core；Core 无反向引用 |
| A5 行为等价 | ✅ `StateHoldDriven.Tests` **73/73**（测试代码零改动）；`Agent.Axioms.Tests` **84/84**；`Agent.Core.Tests` **396/396** |
| A6 全仓编译 | ✅ `DataCenter.slnx` / `MainProj.sln` / `Agent.sln` / `GameEditor.sln` / `PrjBridge` / `FileDriven.sln` 全绿 0 错 0 警 |
| G5 schema 中立 | ✅ 无引擎层名硬编码（`default` 仅示例集） |

### 9.3 遗留与下一步

| 项 | 说明 |
|---|---|
| G4.2 | `IStateProjector` + `TierStateProjector`；**注意 Core 当前零 CSG 代码**，属"新增实现"而非"加校验" |
| G1 的依赖闭合仅为"半程" | `FrameworkAbstractions` 显式引用待 G2 随 `StateCenterBlock` 下沉 `DataCenter.Adapters` |
| VSCodeCounter 等统计产物 | 含历史 `Operation.StateBlock.*` 快照，非代码引用，不在门禁扫描面内 |

---

## 10. 实施记录（G2 设施端口化，2026-09-12）

### 10.1 交付清单

| 步骤 | 落地 | 证据 |
|---|---|---|
| G2.1 | 新建 `Frame\DataCenter\DataCenter.Abstractions\`（**零 ProjectReference**）：`Cells\{DataTier, DataChangeKind, DataCellChangedEventArgs, IDataCell, IDataCellFactory}`、`Dispatch\IStateDispatch`、`Lifecycle\{IStateInitializer, InitializationOutcome}` | `DataCenter.Abstractions.csproj` 无任何引用；`Frame\DataCenter\Directory.Build.props` 对其豁免 `Usings.External`（否则 GlobalUsings 击穿零依赖） |
| G2.2 | Core 新增 `Cells\InMemoryDataCell`（三 tier 字典 + 父链读回退 + Set/SetQuiet/ClearTier 通知语义）+ `Cells\InMemoryDataCellFactory`；`StateCenter`/`StateStore`/`LayeredStoreClient` 内部真源改吃 `IDataCell` | 新增 `DataCenter.Tests` **15/15**（含"双后端同构"一致性断言） |
| G2.2a | **实现上收第一步**：Core 自建 `Storage\TierStringStore` + `Storage\TierObjectStore` 取代 Share 的 `StorageCellStateStore`/`ObjectKeyValueStore`（Core 不再消费 Share 的 StateStorage 实现） | 编译 0 错；73/73 |
| G2.2b | `Compat\{TierMap, StorageCellAdapter, DataCellStorageView}`：契约枚举 ↔ 端口 tier 唯一映射点；IStorageCell→IDataCell 适配（兼容构造）；IDataCell→IStorageCell 视图（`Cell` 契约 + 事务 + 宿主 ECS 存储） | `DataCenter.Tests` 覆盖事务分区写入、`Cell` 直返原实例语义 |
| G2.3 | 新建 `Frame\DataCenter\DataCenter.Adapters\`：`BlockStateInitializer`（原 Core `Blocks\StateCenterBlock` 迁出，实现端口 `IStateInitializer`）、`AppServiceRegistration`（服务定位器） | Core 已删 `Blocks\` 目录；Adapters 编译 0 错 |
| G2.4 | 消费方切换：`StatePipelineBuilder` 改用 `DataCenter.Adapters.BlockStateInitializer`（StateHoldDriven 增引用）；`StateCenter` 双构造并存（端口构造为主 + IStorageCell 兼容构造）；测试/Agent/Engine/FrontEndDriven **源码零改动** | `StateHoldDriven.Tests` 73/73；`Agent.Core.Tests` 396/396；`Agent.Axioms.Tests` 84/84 |
| G2.5 | 门禁扩展：`Check-DataCenterGenericity.ps1` 增 `[G2]` 设施面检查（`IStorageCell`/`StorageCell`/`BasicMethod.Block`/`FrameworkAbstractions` + csproj 引用闭合），注释提及不计入、契约强制点逐条白名单 | `-Check` → **G1 命中 0 / G2 违规 0**（白名单 16、注释提及 13） |
| G2.6 | 消费方二进制接线：Agent `DataCenterBinaryRefs` 增 `DataCenter.Abstractions`（端口类型进 `StateCenter` 公开签名）；测试工程同步接线 | `Agent.sln`/`PrjBridge` 恢复绿 |

### 10.2 关键纠偏（文档初稿未覆盖）

| # | 事实 | 处置 |
|---|---|---|
| 1 | **`IStateCenter.Cell` 是产品级消费面**：`EngineHinge`（`ECSStorage(center.Cell)`）、`SceneManagerInitStage`、`StateContextAdapter.Cell` 均要求 `IStorageCell` | 契约视图保留（`DataCellStorageView`）；A3 改为"自主依赖归零 + 契约强制白名单"，彻底消除需 G5 契约收口 |
| 2 | **`IStateOrchestrator` 暴露 `ExecutionPipeline`（BasicMethod.Block 类型）**，实现 `IStateCenter` 必须解析 | Block 引用按"契约强制"保留并在 csproj 注明；Core 不再使用 Block 初始化块设施 |
| 3 | **DataCenter 构建不 import `modules.generated.props`** → `$(Proj_*)` 变量为空（静默失效） | 全部改用显式相对路径（`$(CsharpProjectRoot)\...`） |
| 4 | **GlobalUsings 会击穿 Abstractions 零依赖**（平台级 `Usings.External.cs` 引 CSG/TConfig） | 在 `Directory.Build.props` 对该工程条件排除 |
| 5 | 平台级 `GlobalUsings` 使零依赖工程无法通过"引共享 usings"实现 | 同上；未来新增纯端口工程需同样豁免 |
| 6 | 端口类型进入公开签名会强制**消费方编译期解析**新程序集（CS0012） | 二进制接线清单显式登记（Agent + 测试） |

### 10.3 验收结果（实测）

| 判据 | 结果 |
|---|---|
| A1 领域词汇零出现 | ✅ 命中 0 |
| A2 依赖闭合 | ✅ Core 引用：`CSG.Unified` / `Share.StateManagement` / `TConfig` / `BasicMethod.Block`（契约强制）/ `DataCenter.Abstractions`；**`Modules.InternalStorage`/`FrameworkAbstractions` 已移除** |
| A3 设施端口化 | ✅ 自主依赖归零（0 违规）；契约强制点 16 处逐条白名单（`IStateCenter.Cell`→IStorageCell、`IStateOrchestrator`→ExecutionPipeline、P8.2 兼容构造） |
| A4 领域经扩展注入 | ✅ 保持单向 |
| A5 行为等价 | ✅ `StateHoldDriven.Tests` **73/73（测试代码零改动）**、`DataCenter.Tests` **15/15**、`Agent.Core.Tests` **396/396**、`Agent.Axioms.Tests` **84/84** |
| A6 全仓编译 | ✅ `DataCenter.slnx` / `MainProj.sln` / `Agent.sln` / `GameEditor.sln` / `PrjBridge` / `FileDriven.sln` 全绿 0 错 |

### 10.4 遗留与下一步

| 项 | 说明 |
|---|---|
| 兼容构造收口 | `StateCenter(IStorageCell)` / `StateStore(IStorageCell)` 与 `Compat\` 适配器保留至 G5 契约收口后删除（届时 A3 可用严格规则，白名单缩减为 0） |
| Share 实现仍在 Share | `StateOrchestrator`/`StateSubscriptionHub`/`StateTransaction` 仍为 Share 实现（G5.1 上收对象；G2 只做设施端口化） |
| `IStateProjector`/`IGenerationParticipant`/`IStateMigration` 端口 | 未建（"不预置空端口"）：分别随 G4.2（CSG 投影）与 G6（代数/迁移）落地时建立 |
| 精确白名单维护 | 新增契约强制点须在 `Check-DataCenterGenericity.ps1` 的 `$facilityAllowList` 登记理由（禁止整目录泛白） |

---

## 附：本轮已交付物（可追溯）

| 交付 | 位置 |
|---|---|
| DataCenter 骨架 | `Frame\DataCenter\{Directory.Build.props, DataCenter.csproj, DataCenter.slnx, GlobalUsings\, schemas\}` |
| 通用状态核心 | `Frame\DataCenter\DataCenter.Core\{Core\StateCenter.cs, Core\StateStore.cs, Layering\*, Decision\*, Blocks\StateCenterBlock.cs}` |
| 域件（待 G1 外移） | `DataCenter.Core\Decision\*` |
| 产品端口派生 | `Frame\CsharpPrj\Operation\StateHoldDriven\Core\OperationStateStore.cs` |
| 消费方接线 | `Projects\Plug\Agent\Directory.Build.props`（`DataCenterBinaryRefs`） |
| 立项架构文档 | `Frame\DataCenter\Doc\DataCenter-架构设计.md` |
| 本通用化文档 | `Frame\DataCenter\Doc\DataCenter-通用化重构与实施计划.md` |



