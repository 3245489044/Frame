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

**关键事实（降低迁移风险）**：L1 的领域件**没有任何产品消费方**——全仓仅 `Environment\CentralTest\CSharpTest\Operation.Tests\StateHoldDriven.Tests\{LoopStateStoreTests,DecisionFrameworkTests}.cs` 使用（`LoopStateStore`/`DecisionCenter`/`DecisionRecord`）。即：**这 4 个文件是"框架内自持、无人消费"的领域件**，外移不触达任何产品代码。

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

### G1 · 领域出 Core（本重构第一批，风险最低、收益最直观）

| 步骤 | 内容 | 判据 |
|---|---|---|
| G1.1 | 新建 `DataCenter.Extensions.LoopDecision`（引 `DataCenter.Core` + `Share.TaskStatusPool`）；移入 `Decision\*` 4 文件 | 编译 0 错；测试工程加引用后 73/73 仍绿 |
| G1.2 | `DataCenter.Core.csproj` 删 `$(Proj_TaskStatusPool)` | **G2 达标**：Core 依赖仅 `CSG.Unified` + 契约 + 设施 |
| G1.3 | 删除空类 `Extensions\StateBlockExtensions.cs` | 冗余归零 |
| G1.4 | 注释中立化（`StateCenter` Transient 注释去 "Agent Vars/骨架"） | G1 grep 通过 |
| G1.5 | 校验器：新增 `Check-DataCenterGenericity.ps1`（对 Core 扫描领域词表，命中即失败） | 门禁可执行、可回归 |

### G2 · 设施端口化（IoC 主干）

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

### G4 · 数据面中立化

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



