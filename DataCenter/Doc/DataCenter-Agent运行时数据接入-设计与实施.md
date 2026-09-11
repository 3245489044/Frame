# DataCenter · Agent 侧运行时数据接入（设计与实施）

> **日期**：2026-09-12 | **定位**：Frame 平台层「接入方」设计（L4/L5，构建在 `Frame\DataCenter` 之上） | **性质**：接入方案 + 分步实施
> **一句话**：把 Agent 侧全部运行时数据收敛为**四条通道**（状态 KV / 对象槽 / 投影 / 域真源对接）；整体数据流的**根实例由 `Frame\DataCenter` 持有**（新增 `DataCenter.Hosting` 单根宿主），Agent 只经端口消费、不再自持运行数据。
> **关联**：`DataCenter-架构设计.md`（立项与红线）、`DataCenter-通用化重构与实施计划.md`（G1–G8）、`DataCenter-可追踪数据流图框架-设计与实施.md`（DF1–DF8 / D0–D5）、`DataCenter-Agent运行时数据接入-实施进展与后续.md`（**执行台账**：已完成阶段的提交与实测判据）、`DataCenter-Agent运行时数据接入-下一步实施计划.md`（**下一步权威**：S1–S5 步骤/改动点/判据/回退/提交粒度）

---

## 0. 结论先行

| # | 结论 | 依据（实测） |
|---|---|---|
| C1 | Agent 侧运行时数据可分 5 类，**真正要"接入"的只有 2 类**（进程内静态对象槽、实例态对象）；另 2 类已外置（只需投影与追踪），1 类明确不接入 | §1 全景表 |
| C2 | 「根实例由 `Frame\DataCenter` 持有」**成立且已有机制保障**：`ExtensionLoadContext.Load` 先复用默认 ALC 已加载同名程序集，契约程序集只要由宿主默认 ALC 加载，跨 ALC 类型身份唯一，根可安全跨插件边界 | `Frame\CsharpPrj\Modules\Registry\ExtensionAssemblyHost.cs:226-249` |
| C3 | 需新增 **`Frame\DataCenter\DataCenter.Hosting`**：进程内单根宿主（唯一实例 + 装配工厂 + 生命周期/换代 + `RuntimeSlot` 分区）；业务侧只经 `IRootProvider`/域 Gateway，**不给业务暴露 `static Current`** | 对齐设计文档 §12.3/§12.4、DF8 |
| C4 | 接入 = **4 条通道**（状态 KV / 对象槽 / 投影 / 域真源对接），**一律不搬真源**（SQL/LiteDB 保持既有权威） | §3、§8 复用矩阵 |
| C5 | 落地顺序：**A0 Hosting → A1 槽清单 + 去静态化 → A2 对象槽挂根 → A3 投影追踪 → A4 双写收敛 → A5 换代闭环**；A4 风险最高放最后 | §4 |

---

## 1. Agent 侧运行时数据全景（实测清单）

> 口径：**数据 = 需要被持有、查询、追踪、跨代际延续的东西**；纯执行栈上下文与进程外资源不计入（见 §1.5）。

### 1.1 A 类：已外置到 StateCenter / 存储（DataCenter 已有真源）

| 数据 | 路径 | 现状 |
|---|---|---|
| 执行瞬态 Vars（轮内） | `Agent.Axioms\StateAdapters\StateBackedDataSpace.cs` → `StateContextAdapter.cs` | ns=`Agent.Round`、tier=Transient、scope=`round-{n}` |
| 会话跨轮状态（原 AxiomContext.Data） | 同上（Session 层） | ns=`Agent.Session`、scope=sessionId |
| 调度持久状态（原 SchedulerState） | 同上（Config 层） | ns=`Agent.Scheduler`、scope=threadHash、键 `*.scheduler` |
| 统一任务快照（权威根） | `Agent.Core\TaskPool\AgentTaskItem.cs` | `ur_tasks.PayloadJson`（Record / ExecutionNode / LoopState） |
| 节点产出 | `ur_nodes`（`NodeOutputStore`） | NodeType=output |
| 引用边 | `graph-log.db`（`ReferenceGraphCoordinator` 为唯一写边 owner） | LiteDB |

→ **不需要搬数据**，D2/D4 的投影 + 追踪即可覆盖。

### 1.2 B 类：进程内静态槽（本次接入主角）

| # | 槽 | 文件:行 | 内容 | 语义 | 处置 |
|---|---|---|---|---|---|
| S1 | `AgentUiHost` | `Agent.Launcher\Modules\AgentUiHost.cs:27-35` | `_current`(`AgentFramework?`)、`_database`(`DatabaseModule?`)、`_mainDialogViewType` | **静态槽持有整个运行时框架** | 迁根分区 `agent`；UI 函数经 `IRootProvider` 读 |
| S2 | `LlmRuntime` | `Agent.Runtime\State\Infrastructure\LlmRuntime.cs:23-36` | `_routing` / `_localOnly` / `_ensureHttp` / `_backends` / `_scopeRoute`(AsyncLocal) | LLM 网关单根（含进程资源） | 实例化为 `LlmRuntimeHost` 挂根；`_scopeRoute` 留 AmbientSlot |
| S3 | `SkillRuntimeState` | `Agent.Runtime\State\Skills\SkillRuntimeState.cs:12-28` | `_hot` / `_rare` 技能快照 | 可重建缓存 | 挂根分区；可投影为图节点 |
| S4 | `WorkflowRuntimeState` | `Agent.Runtime\State\Loops\WorkflowRuntimeState.cs:9-16` | `StaticizedWorkflows` / `CompiledWorkflows` | 幂等缓存（可重建） | 挂根分区；`generation` 变更即失效 |
| S5 | `AgentCoreRuntime` | `Agent.Runtime\State\Infrastructure\AgentCoreRuntime.cs:10-25` | `ConfigDirectory` / `StrictConfig` | **装配配置**（非数据） | 迁根 `Registry/Config` 消费面（可豁免"数据"口径） |
| S6 | `AgentStateLocator` | `Agent.Axioms\Infrastructure\AgentStateLocator.cs:11-33` | `IStateLocationProvider? Current` | 路径缝（宿主注入） | 保留语义，改为**根持有的 Provider 实例**（消除静态 setter） |

### 1.3 C 类：实例态对象（挂在 `AgentFramework` / `AgentOrchestratorState` 上）

| 对象 | 位置 | 内容 | 接入方式 |
|---|---|---|---|
| `AgentOrchestratorState` | `Agent.Runtime\State\Orchestration\AgentOrchestratorState.cs:16-46` | Modules / Disposables / Executors / Initialized / Disposed / Runtime / RuntimeState / BoundaryFramework / PersistenceStore | 作为根分区产物注册 |
| `RuntimeStateBroker` | `...\State\Runtime\RuntimeStateBroker.cs:12` | 最近运行态快照 + 订阅者集合 | 同上；其快照可投影为图 |
| `BoundaryRuntimeState` | `...\State\Boundaries\BoundaryRuntimeState.cs:10` | ProgressTracker（无进展步数 / 长度窗口 / EWMA） | 同上（跨轮累计量 → KV 或分区，二选一） |
| `ReactiveConsumptionState` | `...\State\DataStream\ReactiveConsumptionState.cs:14` | 消费游标 / 启动标志 / LoopCts / 订阅句柄 / 回填任务 | 同上（游标属数据，句柄属资源） |
| `PromptRuntimeState` | `...\State\Prompts\PromptRuntimeState.cs:11` | L1 提示词缓存 + 生成委托 | 同上（**当前持有方待 A1 核实**） |
| `AgentRuntimeManager` | `...\State\Runtime\AgentRuntimeManager.cs:17` | 执行器注册表 | 装配期注册，运行期只读 |
| `AgentFramework` | `Agent.Launcher\Composition\AgentFramework.cs:40-68` | `_orchestrator` / `_reactive` / `_taskPool` / `_apiPort` / `_frameworkCt` | 整体作为根分区 `agent` 的产物 |

### 1.4 D 类：双写 / 漂移对（须收敛）

| 双写对 | 证据 | 收敛方向 |
|---|---|---|
| `vars`(Transient) ↔ `ur_nodes` | `NodeExecutionService`「先内存 `vars.Set`，后落库 `WriteOutputAsync`」 | 读侧 ur_nodes 优先（已收敛）→ 写侧再收敛为「单写 + 投影重算」 |
| `AgentTaskItem.PayloadJson` ↔ `state.Data` | `RoundStateMapper.Harvest` 二次收割 vars 进跨轮状态 | 以任务快照为唯一权威根，`state.Data` 降为缓存 / 投影 |

### 1.5 E 类：明确不接入（诚实边界）

| 项 | 位置 | 理由 |
|---|---|---|
| `RunContext`（两份） | `Agent.Core\Nodes\RunContext.cs`、`Agent.Runtime\State\Loops\RunContext.cs` | **执行栈上下文**（AsyncLocal / `Modules.Threading.AmbientSlot`），属"计算过程"非"数据"；进图会污染 DF6 时间/空间正交 |
| `AgentRunCancellation` | `Agent.Runtime\State\Loops\AgentRunCancellation.cs` | 无状态只读桥（委托 `RunContext.Current`） |
| UI 控件 / ECS 挂载 / native | `FrontEndDriven`、`Plate.*`、`UiBackend.*` | 不可热换（架构设计 §8 三域清单） |
| 进程外资源 | Cline daemon、LocalLlmHttp 端点（`Agent.Modules`）、凭据目录 | 非数据；只登记句柄归属 |
| `Modules.Threading.AmbientSlot` | `Frame\CsharpPrj\Modules\Threading` | 是机制不是数据，**保留并作为「per-run 取根」的载体** |

---

## 2. 目标架构：整体根实例由 `Frame\DataCenter` 持有

### 2.1 分层与职责

```
┌──────────────────── L5 使用层（Agent / FrontEndDriven / Engine）──────────────────────┐
│  只经「域 Gateway / IRootProvider」读写；不再出现 static 运行时槽、不再 new StateCenter │
└──────────▲──────────────────────────────────────────────────────┬────────────────────┘
           │ 域视图（强类型）                                    │ 图 / 追踪查询
┌──────────┴──────────────────────────────────────────────────────▼────────────────────┐
│ L4 域扩展  DataCenter.Extensions.{LoopDecision, AgentState, GraphProjection}          │
│   · 域 Keys（AgentStateKeys 收敛）  · 域投影（状态/对象槽 → 节点/边）  · 域 Gateway      │
└──────────▲──────────────────────────────────────────────────────┬────────────────────┘
           │ 根分区（RuntimeSlot）                               │ 端口实现
┌──────────┴──────────────────────────────────────────────────────▼────────────────────┐
│ ★ DataCenter.Hosting（`Frame\DataCenter` 新工程）：单根宿主                            │
│   DataCenterHost（唯一实例 + Boot/Shutdown/SwapAsync）                                │
│   DataCenterHostOptions（cell 工厂 / projector / registry / generation / 槽装配委托）  │
│   RuntimeSlotHost（根的分区承载）  ·  HostRootProvider（业务取根唯一路径）               │
│   ── 根实例在此被创建与持有（进程内 ≤ 1，守卫 fail-fast）；业务侧仅经 IRootProvider     │
└──────────▲──────────────────────────────────────────────────────┬────────────────────┘
┌──────────┴──────────────────────────────────────────────────────▼────────────────────┐
│ L1 端口 / 契约   DataCenter.Abstractions（零依赖）                                     │
│                  DataCenter.Protocol（图契约 + IStateProjector + IDataCenterRoot +     │
│                                      IRootProvider + IRuntimeSlot）                    │
│ L0 协议          CSG.Unified（CanonicalForm：只扩不改，R-DC1）                          │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

**职责边界（新增工程的定位）**

| 工程 | 职责 | 引什么 | 门禁 |
|---|---|---|---|
| `DataCenter.Hosting` | 进程内**单根宿主**：装配、持有、生命周期、换代交换；`RuntimeSlot` 分区承载对象槽 | Abstractions + Protocol + Core + Adapters + 宿主设施（ALC / 宿主 cell） | 归入 **宿主装配层**：K1/K2 设施面按白名单单列（不适用 Core 的零设施规则） |
| `DataCenter.Protocol` | 图契约与端口（已落地 D0/D1） | CSG.Unified + Abstractions | G1（领域词汇零出现） |
| `DataCenter.Core` | 通用实现（StateCenter / TierStateProjector / DataCenterRoot / …） | Protocol + CSG + Share 契约 | G1/G2（现状：命中 0 / 违规 0） |

### 2.2 跨 ALC 前提（关键技术事实，必须满足）

| # | 前提 | 依据 / 落点 |
|---|---|---|
| P1 | `DataCenter.{Abstractions,Protocol,Core,Hosting}.dll` 必须由**宿主默认 ALC** 加载 | 进入宿主烘焙清单（`CsharpPrj\BuildOutput` + `PrjBridge.deps.json`） |
| P2 | Agent 插件经**二进制引用**消费契约面 | `Projects\Plug\Agent\Directory.Build.props` 的 `DataCenterBinaryRefs`（现含 Core / Abstractions / Extensions.LoopDecision，需补 Protocol / Hosting 契约面） |
| P3 | `ExtensionLoadContext.Load` 的"默认 ALC 优先复用"保证类型身份唯一 → 根实例可跨边界传递 | `Frame\CsharpPrj\Modules\Registry\ExtensionAssemblyHost.cs:226-249`（实测：先查 `AssemblyLoadContext.Default.Assemblies`，再 `LoadFromAssemblyName`，失败才落 scoped 自装） |
| P4 | 换代回收要求 ALC 可收集 | `ExtensionAssemblyHost.cs:218` 现为 `isCollectible: false` → A5 改 `true` + 实例化代数工厂（通用化 G7.2） |

> **纪律**：契约程序集（Abstractions / Protocol）**不得**下沉到插件私有目录被 scoped ALC 自装，否则 `IDataCenterRoot` 出现两份类型身份 → 根实例跨边界即 `InvalidCastException`。A0 必须加"同一实例"断言测试。

### 2.3 根持有什么 / 不持有什么

| 持有 | 说明 | 写口 |
|---|---|---|
| 状态真源 `IDataCell` / `StateCenter` | 唯一状态写入路径（分层 / scope / 事务） | 单写口（既有，DF1 不变） |
| 投影器 `IStateProjector` | 图的唯一产出点 | 只读派生 |
| 注册表 `IStateRegistry` | tier / nodeKind / edgeKind / 域 Keys 只读消费面 | 装配期注册 |
| 代际 `IStateGeneration` | generation / fingerprint / schemaVersion 读口 | 换代流程写 |
| **运行时槽分区 `IRuntimeSlot`** | Agent 侧对象槽（B/C 类：LLM 网关、技能/工作流缓存、编排器状态、边界状态、提示词缓存、运行态 broker、`AgentFramework`） | 装配期注册、处置期替换 |
| 端口实现组 | 存储后端适配、`IStateDispatch`、`IStateInitializer` | 装配期注入 |

| **不持有** | 理由 |
|---|---|
| 域语义与业务逻辑 | 属 L4 域 Gateway（R12：根只持有与转发） |
| 执行栈上下文（`RunContext`） | E 类：属计算过程 |
| 进程外资源（daemon / 端口 / 凭据） | 只登记句柄归属，不做生命周期承诺 |

---

## 3. 四条接入通道

### 3.1 通道规范

| 通道 | 适用（§1 分类） | 机制 | 不变量 | 判据 |
|---|---|---|---|---|
| **A · 状态 KV** | A 类、D 类 | 域 Gateway → `LayeredStoreClient` → `StateCenter.Write`（单写口） | DF1 单一真源、DF8 单根 | 写路径唯一（`grep` 无第二写口） |
| **B · 对象槽** | B 类、C 类 | `root.Slot(domain)` 分区持有对象引用；**不可序列化数据不进 KV** | DF8（挂根而非静态） | 产品侧 `static` 可变槽归零（K10） |
| **C · 投影** | A/B/C 的**可观测面** | 域投影 → `CanonicalForm`（`IStateProjector`）；`TraceStamp` 盖章 | DF1 只读派生、DF6 时间/空间正交 | K3/K4/K5/K7 |
| **D · 域真源对接** | `ur_tasks` / `ur_nodes` / `graph-log.db` | 只读接入 + 边类型映射归一（设计文档 §4.3） | DF5（边即断言）；`ReferenceGraphCoordinator` 保持唯一写边 owner | 既有边 100% 可归类 |

### 3.2 写入纪律（判定规则）

> **可序列化 + 需跨代际连续 → 通道 A；进程资源 / 句柄 / 委托 → 通道 B；两者皆可 → 以 A 为准，B 仅作引用缓存。**

配套三条硬纪律：

1. **状态与追踪同生共死**：状态写失败 ⇒ 无追踪痕迹；追踪写失败 ⇒ 整次写入失败（设计文档 §5.4 三阶段提交）；
2. **投影失败不得升级为状态写失败**：投影是派生物，可重算；
3. **对象槽不参与持久化承诺**：崩溃后由「任务快照 Seed（`state.Data` → `ur_tasks`）+ `ur_nodes`」重建（对齐 `StateBackedDataSpace` 既有声明）。

### 3.3 域命名空间对齐（实测发现，需决策）

| 现状 | 问题 | 推荐方案 |
|---|---|---|
| `AgentStateKeys`（`Agent.Contract\StateAdapters\AgentStateKeys.cs:12-14`）使用三个独立命名空间：`Agent.Scheduler` / `Agent.Session` / `Agent.Round`，由 tier 决定用哪个 | `StateUriMapper.DomainOf` 按 `{domain}.{Tier}` 后缀拆域；这三个名**都不带 tier 后缀** → 会被投影为 **3 个逻辑域**（`csg://Agent.Scheduler/config/...`、`csg://Agent.Session/session/...`、`csg://Agent.Round/transient/...`），跨层因果链无法闭合 | **不动落盘键**，在映射点登记**域别名表**（`Agent.{Scheduler,Session,Round}` → domain=`Agent`，tier=`config/session/transient`），零落盘迁移 |
| 改名方案（`Agent.Config` / `Agent.Session` / `Agent.Transient`） | 落盘键格式变更 → 需迁移与回放兼容 | 仅在"必然要迁移"时采用（A4 合并考虑） |

---

## 4. 实施阶段（A0–A5）

> 通则：每阶段**独立可编译、可回退**；判据必须可执行（命令 / 断言）；不改真源、不改 CSG 主 schema。

### A0 · `DataCenter.Hosting` 落地（`Frame\DataCenter` 持有根）★ 硬前置

| 项 | 内容 |
|---|---|
| 交付 | ① 新工程 `Frame\DataCenter\DataCenter.Hosting`（注册进 `DataCenter.slnx` + `DataCenter.csproj` 聚合）<br>② `DataCenterHost`：`Boot(DataCenterHostOptions)` / `Lifetime` / `ShutdownAsync` / `SwapAsync(GenerationPlan)`；内部复用 `RootInstanceGuard`（进程内 ≤ 1）<br>③ `DataCenterHostOptions`：cell 工厂 / projector / registry / generation / 运行时槽装配委托<br>④ `IRuntimeSlot`（Protocol 新端口）+ `RuntimeSlotHost`（Hosting 实现）= 根的分区承载（P10）<br>⑤ `HostRootProvider : IRootProvider`（业务取根唯一路径） |
| 关键决策 | **根实例由 Hosting 创建与持有**；业务侧**不给** `static Current`（只给 `IRootProvider`）；Hosting 属"宿主装配层"，允许引宿主设施（ALC / 宿主 cell），但必须与 Core 的门禁口径分离 |
| 判据 | ① `DataCenter.slnx` 绿、0 警告 ② 单根 ≤ 1：二次 `Boot` → fail-fast ③ 缺根 `IRootProvider.Root` → `RootMissingException` ④ **跨 ALC 同一实例断言**（契约程序集由默认 ALC 加载，插件侧 `typeof(IDataCenterRoot).Assembly == 宿主侧`）⑤ 既有 34/34（DataCenter.Tests）、73/73（StateHoldDriven.Tests）零回归 |
| 回退 | 纯新增工程，撤出即回退（不触碰既有装配） |

### A1 · 槽清单化 + 去静态化（Agent 侧）

| 项 | 内容 |
|---|---|
| 交付 | ① `Agent.Contract\Runtime\AgentRuntimeSlotKeys.cs`：槽键注册表（`llm` / `skills` / `workflows` / `state-locator` / `config` / `orchestrator` / `boundary` / `reactive` / `prompt` / `runtime-state` / `framework`）—— DF7 注册制，扩展 = 注册一行<br>② **逐个槽改造**（每槽一个提交、可独立回退），改造模式统一为：<br>`public static X Current` → 依赖 `IRootProvider` → `root.Slot(d => d.X)`（装配期注册、运行期只读）<br>③ 旧静态口保留一期：`[Obsolete("改经 IRootProvider 取根")]` 转发到根分区（防 UI 函数库 / 自检脚本 / StorageBridge 断裂） |
| 改造顺序（低风险 → 高风险） | 1. `AgentStateLocator`（`Agent.Axioms\Infrastructure\AgentStateLocator.cs:11-33`）——路径缝，改为根持有的 Provider 实例<br>2. `WorkflowRuntimeState`（`...\State\Loops\WorkflowRuntimeState.cs:9-16`）——纯幂等缓存<br>3. `SkillRuntimeState`（`...\State\Skills\SkillRuntimeState.cs:12-28`）——纯缓存快照<br>4. `AgentCoreRuntime`（`...\State\Infrastructure\AgentCoreRuntime.cs:10-25`）——装配配置<br>5. `LlmRuntime`（`...\State\Infrastructure\LlmRuntime.cs:23-36`）——**含进程资源**，改为 `LlmRuntimeHost` 实例挂根；`_scopeRoute` 保留 `AmbientSlot` 语义<br>6. `AgentUiHost`（`Agent.Launcher\Modules\AgentUiHost.cs:27-35`）——**静态持有整个框架，最高风险，最后做** |
| 关键决策 | 静态槽 → 实例槽属**公开面变更**（`AgentUiHost` 被 `AgentUiLibrary*` / HTML UI 函数库 / 自检脚本读取）；以"新实例路径 + 旧静态口转发"过渡一期，避免一次性断链 |
| 判据 | ① `Agent.sln` 绿 ② `Agent.Core.Tests` 396/396、`Agent.Axioms.Tests` 84/84 ③ 既有 UI/任务池自检（`agent_mcpinvoke_create_task_test`、`Test-AgentTaskPool.ps1`）通过 ④ K10（产品侧无新增 `static` 可变运行时槽） |
| 回退 | 每槽独立提交；静态口转发期保证行为等价 |

### A2 · `AgentFramework` 挂根（对象槽分区）

| 项 | 内容 |
|---|---|
| 交付 | ① `AgentFrameworkAssembler`（`Agent.Launcher\Composition\AgentFrameworkAssembler.cs`）的装配产物注册进 `root.Slot("agent")`：`AgentOrchestrator` / `AgentOrchestratorState` / `RuntimeStateBroker` / `IBoundaryFramework` / `ReactiveComponents` / `PromptRuntimeState` / `DataStoreEngine` / `AgentTaskPoolAdapter` / `AgentApiPort`<br>② `AgentFramework`（`Agent.Launcher\Composition\AgentFramework.cs:40-68`）不再被静态持有；`AgentUiHost.Current` 读根分区<br>③ 释放顺序收敛为根 `Dispose` 单点：**先停写 → 再关 Actor / 任务池 → 再释放存储** |
| 关键决策 | 对象槽进"根分区"而非 KV（含句柄 / 委托 / 资源，不可序列化）；分区按域键细分（`agent` / `engine` / `ui`），不逐对象开槽 |
| 判据 | ① `root.Slot("agent")` 可枚举全部装配产物 ② 重复装配同一域键 → fail-fast（不给"第二根/第二份运行时"） ③ 释放后访问 → `ObjectDisposedException` ④ 既有 396/396 + Agent.sln 绿 |
| 回退 | 分区注册表开关；未注册时回退旧持有路径 |

### A3 · 投影与追踪（让接入"可观测、可追踪"）

| 项 | 内容 |
|---|---|
| 交付 | ① 新 L4 工程 `DataCenter.Extensions.AgentState`：**域 Keys**（`AgentStateKeys` 收敛，含 §3.3 域别名表）+ **域投影**（A/B/C 类 → 节点与边：`state.value` / `decision.record` / `task` / `node.output`）<br>② `TraceStamp` 盖章接入写路径：`writer` = `AgentRunner` / `NodeExecutionService` / `RoundStateMapper` 等（域显式传入，Core 不认领域语义）<br>③ `ITraceQuery` 查询面（`Ancestors(终端)` 闭合回根、`ReverseRefs`、`Validate`）<br>④ 对象槽的**可观测投影**（只读、非真源）：LLM 路由模式 / 后端清单、技能热集计数、工作流编译缓存键 |
| 关键决策 | 投影只读派生（DF1）；对象槽投影到图的是"快照值"而非对象引用；时间戳不进投影产物（保幂等） |
| 判据 | ① K3（CSG 校验）/ K4（悬空边 0）/ K5（Kind 注册完备）/ K7（追踪四要素）② 投影幂等（两次 `ToJson` 全等）③ `Ancestors(终答节点)` 可闭合（回答"这一步为什么是这样"） |
| 回退 | 投影与查询是只读旁路，撤出不影响写入与执行 |

### A4 · 双写收敛（风险最高，最后做）

| 项 | 内容 |
|---|---|
| 交付 | ① `vars`(Transient) ↔ `ur_nodes` 写侧收敛：单写 + 投影重算（读侧 ur_nodes 优先已具备）<br>② `AgentTaskItem.PayloadJson` ↔ `state.Data` 权威单一化：以统一任务快照为唯一权威根，`state.Data` 降为缓存 / 投影<br>③ 落盘键迁移（仅当 §3.3 选择改名方案时，与域别名表二选一） |
| 关键决策 | 收敛必须**按后端可开关**（Sqlite / 内存占位分别验证）；不做"一次到位" |
| 判据 | ① 故障注入（写后崩溃）无漂移（状态与落盘一致） ② `NodeGraphReconciler` 跨介质缺口 0 ③ 396/396 + Agent.sln + 既有持久化测试全绿 |
| 回退 | 开关关闭即回旧路径；本阶段独立提交、不与其它阶段混 |

### A5 · 换代闭环（热更新带数据）

| 项 | 内容 |
|---|---|
| 交付 | ① `ExtensionLoadContext` 可收集化（`ExtensionAssemblyHost.cs:218` `isCollectible: false` → `true`）+ **实例化代数工厂**（消除 `static ExtensionAssemblyHost`，通用化 G7.2）<br>② `DataCenterHost.SwapAsync`：Drain（暂停新写、在途推到边界）→ Export（Agent 分区状态）→ 新 ALC 装配新根 → Import（`Seq` 连续、`generation+1`、`fingerprint` 更新）→ Resume → 旧 ALC `Unload`<br>③ 产品侧 `new StateCenter(` 归零（`AgentOrchestrator.cs:93` / `WorkflowRunner.cs:89` 两处兜底造根移除） |
| 关键决策 | 换代时对象槽策略 = **重建 + 状态迁移**（LLM 网关 / daemon / 句柄不跨代数复用，避免跨 ALC 持有已卸载程序集的类型）；状态经 Export/Import 迁移以保连续 |
| 判据 | ① 换代前后同 scope 值 / `Seq` 连续（不重置、不丢） ② 旧代数 `WeakReference.IsAlive == false` ③ 换代失败保留旧代数（不半迁） ④ K9（`new StateCenter(` 白名单 = Hosting / 测试） |
| 回退 | 换代流程独立；ALC 可收集化与根交换可分两步提交 |

### 4.1 阶段依赖图

```
A0 Hosting（根由 Frame\DataCenter 持有）──▶ A1 槽清单 + 去静态化 ──▶ A2 对象槽挂根
                                                       │
                                                       ├──▶ A3 投影与追踪（可并行于 A2 后半）
                                                       ├──▶ A4 双写收敛（最后）
                                                       └──▶ A5 换代闭环（A2 后即可启动 ALC 可收集化）
```

> **A0 是总前置**：对象槽、投影、换代都必须挂在根上（而非各自的静态持有），否则每加一件都要再改一次持有关系。
> **A1 是接入主体**：B 类 6 个槽是"Agent 运行时数据"的绝大部分；A 类与 C 类风险更低，可穿插进行。

### 4.2 构建 / 烘焙顺序（实测要求，A0 落地时踩到）

| # | 事实 | 动作 |
|---|---|---|
| B1 | 契约程序集（`DataCenter.{Abstractions,Protocol,Core,Adapters}`）会被 **CsharpPrj 烘焙副本**（`Frame\CsharpPrj\BuildOutput\$(Configuration)`）与 **CentralTest 共享输出**（`Environment\CentralTest\BuildOutput\$(Configuration)\net10.0-windows`）各自复制一份 | 改契约后若不刷新烘焙副本，测试运行期会加载**旧契约**（实测症状：`TypeLoadException: Could not load type 'DataCenter.Protocol.Roots.IRuntimeSlotPartition'`） |
| B2 | 刷新触发点 = 唯一引用 DataCenter 的 CsharpPrj 工程 | `dotnet build Frame\CsharpPrj\Operation\StateHoldDriven\StateHoldDriven.csproj -c Debug`（随后常规 `dotnet test` 会同步 CentralTest 共享输出） |
| B3 | 判定"是否已刷新" | `Get-ChildItem Frame\CsharpPrj\BuildOutput\Debug -Filter 'DataCenter.*.dll'`：时间戳须晚于本次契约改动 |

---

## 5. 门禁扩展（把纪律变成可回归的检查）

| # | 检查 | 失败判据 | 对应不变量 | 落点 |
|---|---|---|---|---|
| K1/K2 | 领域词汇零出现 / 设施端口化（Core + Protocol） | 命中 > 0 | DF2 / G1 / G2 | **已实现**（`Check-DataCenterGenericity.ps1`，现状 29 文件 / 命中 0 / 违规 0） |
| K9 | **单根与无静态槽**：产品侧不得 `new StateCenter(`；不得静态持有根 | 命中 > 0（白名单 = `DataCenter.Hosting` / 测试） | DF8 | A0/A5 扩脚本（扫描面 +`Projects\Plug\Agent`） |
| K10 | **无新增静态运行时槽**：产品侧不得新增 `static` 可变运行时槽 | 命中 > 0（白名单 = 注册表常量 / `AmbientSlot`） | DF8 / R11 | A1 新增（`Check-AgentRuntimeSlots.ps1`） |
| K3–K8 | 图契约 / 悬空边 / 注册完备 / 环 / 追踪完备 / 序列连续 | 见设计文档 §6.2 | DF5 / DF7 / DF3 / DF4 | A3 落脚本（当前由单测覆盖 K3/K4/K5） |

---

## 6. 关键决策点（需确认）

| # | 议题 | 建议默认 | 理由 |
|---|---|---|---|
| **D1** | 根实例持有形态 | **`DataCenter.Hosting` 进程内单根宿主**（`Frame\DataCenter` 持有实例）；业务只经 `IRootProvider` | 满足"整体根实例由 `Frame\DataCenter` 持有"；同时不违反 §12.4（不给业务 `static` 取根口） |
| D2 | `RuntimeSlot` 粒度 | 一个逻辑根分区按**域键**细分（`agent` / `engine` / `ui`），不逐对象开槽 | 避免"每对象一槽"退化为服务定位器 |
| D3 | Agent 静态槽的公开面变更 | 新实例路径 + 旧静态口 `[Obsolete]` **转发一期** | `AgentUiHost` 被 `AgentUiLibrary*` / HTML UI 函数库 / 自检脚本读取，一次性断链风险高 |
| D4 | 域命名空间 | **不动落盘键**，映射点登记域别名表（§3.3） | 零落盘迁移、零回放兼容成本 |
| D5 | 换代时对象槽策略 | **重建 + 状态迁移** | 句柄 / 委托 / daemon 不跨代数复用；避免跨 ALC 类型失效 |
| D6 | `DataCenter.Hosting` 是否兼作 `--dump-state` 宿主 | **兼作**（并入原 D5 的 `DataCenter.Host` 规划） | 避免两个 Host 工程与两套装配入口 |
| D7 | K10 白名单口径 | 仅放行"注册表常量 / `AmbientSlot` 机制 / 纯静态只读计算" | 防白名单泛化导致门禁失效 |

---

## 7. 风险与诚实边界

| # | 风险 / 边界 | 说明 | 缓解 |
|---|---|---|---|
| R1 | **跨 ALC 类型分裂** | 契约程序集若被 scoped ALC 自装 → `IDataCenterRoot` 两份身份 → 根实例跨边界抛错 | §2.2 P1–P3；A0 增"同一实例"断言测试；契约程序集进宿主烘焙清单 |
| R2 | **`AgentUiHost` 静态槽多消费方** | UI 函数库 / 自检脚本 / `StorageBridge` 直读静态槽 | A1 先出消费方清单 → 转发期 → 分步切换（D3） |
| R3 | **LLM 网关单根换代抖动** | 后端连接 / 本地 HTTP 端点随代数重建 | Drain 语义 + 重连超时；**不承诺零停机**（对齐架构设计 §8） |
| R4 | **测试并行 vs 单根守卫** | 单根守卫为进程级，测试并行会互相干扰 | 提供测试重置钩子（`RootInstanceGuard` 内部 `Exit` 已具备）；相关测试类 `[DoNotParallelize]` |
| R5 | **双写收敛回归面最大** | `vars ↔ ur_nodes` / 任务快照 ↔ `state.Data` 涉及写热路径 | A4 独立开关 + 故障注入测试 + 单独评审 |
| R6 | **域命名空间语义漂移** | 各域自拼 URI / 自定 Kind（DF7 已约束） | 域别名表 + 图域注册表（L4 单点） + K3/K5 |
| R7 | **不承诺** | ① 进程外资源（Cline daemon / 端口 / 凭据）② UI 控件 / native / 共享栈（`WorkFlowMethod` / `Share.*` / `Plate.*`）不可热换 ③ 执行栈上下文（`RunContext`）不进图 ④ 图查询面向诊断观测，不承诺 OLTP 级并发 | 清单化（§1.5）+ 文档显式口径 |

---

## 8. 附：Agent 运行时槽登记表（A1 执行台账）

> 用途：A1 逐槽改造的台账（每槽一行 = 一个提交）。状态列在实施时勾选。

| # | 槽键（`AgentRuntimeSlotKeys`） | 现位置 | 分类 | 目标形态 | 状态 |
|---|---|---|---|---|---|
| 1 | `state-locator` | `Agent.Axioms\Infrastructure\AgentStateLocator.cs` | B | 根持有的 `IStateLocationProvider` 实例 | ☐ |
| 2 | `workflows` | `Agent.Runtime\State\Loops\WorkflowRuntimeState.cs` | B | 根分区（`generation` 变更即失效） | ☐ |
| 3 | `skills` | `Agent.Runtime\State\Skills\SkillRuntimeState.cs` | B | 根分区 + 可观测投影 | ☐ |
| 4 | `config` | `Agent.Runtime\State\Infrastructure\AgentCoreRuntime.cs` | B（配置） | 根 `Registry/Config` 消费面 | ☐ |
| 5 | `llm` | `Agent.Runtime\State\Infrastructure\LlmRuntime.cs` | B（含资源） | `LlmRuntimeHost` 实例挂根；`_scopeRoute` 留 AmbientSlot | ☐ |
| 6 | `framework` | `Agent.Launcher\Modules\AgentUiHost.cs` | B（**持有整体**） | 根分区 `agent`；`AgentUiHost` 转发一期后退役 | ☐ |
| 7 | `orchestrator` | `Agent.Runtime\State\Orchestration\AgentOrchestratorState.cs` | C | 根分区产物 | ☐ |
| 8 | `runtime-state` | `Agent.Runtime\State\Runtime\RuntimeStateBroker.cs` | C | 根分区产物 + 快照投影 | ☐ |
| 9 | `boundary` | `Agent.Runtime\State\Boundaries\BoundaryRuntimeState.cs` | C | 根分区（或 KV：跨轮累计量，A1 定） | ☐ |
| 10 | `reactive` | `Agent.Runtime\State\DataStream\ReactiveConsumptionState.cs` | C | 根分区（游标值 + 句柄引用分离） | ☐ |
| 11 | `prompt` | `Agent.Runtime\State\Prompts\PromptRuntimeState.cs` | C | 根分区（**持有方待核实**） | ☐ |

**A 类（已外置，无需改造，仅接投影/追踪）**：`Agent.Round`（Transient）/ `Agent.Session` / `Agent.Scheduler` / `TaskRecord`+`ExecutionNode`+`AgentLoopState`（`ur_tasks` PayloadJson）/ `ur_nodes` / `graph-log.db`

**E 类（明确不接入）**：`Agent.Core\Nodes\RunContext.cs`、`Agent.Runtime\State\Loops\RunContext.cs`、`AgentRunCancellation`、UI/native/共享栈、进程外资源

---

## 9. 与既有成果的衔接

| 既有成果 | 衔接点 |
|---|---|
| SSOT（`StateRootSingleSource`） / DF8 | 本方案是其落地形态：单根 = 唯一承载者；对象槽以**根分区**表达，而非第二根 |
| 通用化 G2 / G7.1–G7.3 | 本方案补齐 **A0 的端口契约与取得纪律** + A1 的"删插件内 `new StateCenter` / 静态槽" |
| DF1–DF7（图框架设计） | 通道 A/C/D 分别对应"单写口 / 投影 / 注册制与域真源对接" |
| D0/D1（已落地） | `DataCenter.Protocol`（图契约 + `IStateProjector` + `IDataCenterRoot`/`IRootProvider`）与 Core 的 `TierStateProjector`/`GraphRegistry`/`GenerationState`/`DataCenterRoot` 即本方案的基座；A0 只在其上加"单根宿主 + 运行时槽分区" |
| 门禁 K1/K2（已落地） | 本方案扩 K9/K10，并把扫描面从 `DataCenter.Core + Protocol` 扩到 `Projects\Plug\Agent` |







