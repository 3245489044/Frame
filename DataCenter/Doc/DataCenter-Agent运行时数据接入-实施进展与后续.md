# DataCenter · Agent 侧运行时数据接入 —— 实施进展与后续（A0→A5）

> **日期**：2026-09-12 | **配套**：`DataCenter-Agent运行时数据接入-设计与实施.md`（设计与阶段定义）
> **口径**：本文件是**执行台账**（每阶段 = 一个提交 + 可执行判据 + 实测结果）；设计语义以设计文档为唯一权威。

## 1. 进展总览

| 阶段 | 状态 | 提交 | 实测判据 |
|---|---|---|---|
| **A0** Hosting 单根宿主 | ✅ 完成 | Frame `4d90380`、Projects `261984c`、主仓 `add6f65`/`5c730ea` | `DataCenter.slnx` 0 错 0 警；`DataCenter.Tests` **40/40**（单根 fail-fast / 缺根 / 换代代际推进+状态连续 / 槽幂等与释放 / 跨 ALC 同一实例）；`StateHoldDriven.Tests` **73/73** |
| **A1** 槽清单化 + 去静态化 | ✅ 完成 | Projects `c4c22e3`、Frame `2982254` | `Agent.sln` 0 错 0 警；`Agent.Core.Tests` **396/396**、`Agent.Axioms.Tests` **84/84**、`Agent.Runtime.Tests` **21/21**、`Agent.Launcher.Tests` **26/26**；**K10** 绿 |
| **A2** AgentFramework 挂根 | ✅ 完成 | Frame `ad2002c`、Projects `36645c5`、主仓 `a3f8090` | 同上全绿 + 基线序同步（`FrameworkSegmentE`） |
| **A3a-1** 域别名注册机制（Core 通用） | ✅ 完成 | 见本次提交 | `DataCenter.Tests` **44/44**（新增 4 项域别名判据） |
| **A3a-2** Agent 域扩展（Agent 侧 `Agent.DataCenter`，命名空间 `Agent.StateDomain`） | ✅ 完成 | Projects 见本次提交、主仓见本次提交 | `Agent.sln` 0 错 0 警；`DataCenter.Tests` **49/49**（新增 5 项 Agent 域判据）；`StateHoldDriven.Tests` 73/73、`Agent.Core.Tests` 396/396、`Agent.Axioms.Tests` 84/84、`Agent.Runtime.Tests` 21/21、`Agent.Launcher.Tests` 26/26；K9/K10 均 **EXIT 0**（0 新增违规） |
| **A3b** `TraceStamp` + `ITraceQuery` | ⏸ 阻塞于 D2/D3 | — | Protocol 尚无追踪面契约 |
| **A4** 双写收敛 | ⏸ 未开始（风险最高，须独立开关 + 独立评审） | — | — |
| **A5** 换代闭环 | 🚧 进行中（**S4-1 / S4-2 / S4-3 / S4-4a 已完成**；S4-4b 待做） | Frame `f0dc593`（S4-1）、`2520832`（T3）；Projects `32f5043`（S4-1 消费方）、`46ac754`（S4-4a）；其余见本条各行 | S4-1/S4-2/S4-3 判据见下行各行；**S4-4a**：K9 待迁移 **2 → 0**（白名单 3/违规 0）、`Agent.Core.Tests` **399/399**、`Check-AgentDataSeparation -Check` EXIT 0 |
| **B1+B2** P0 前置（F-16/F-17/F-18） | ✅ 完成 | 见本次提交（Frame 契约/Hosting/Core、Projects Agent 侧、主仓判据） | `DataCenter.slnx` **0 错 0 警**、`Agent.sln` **0 错 0 警**；`DataCenter.Tests` **56/56**（新增 3 项：非抛探测语义 / 换代解绑窗口回落 + 换代后重绑 / 无指纹沿用）；`Agent.Launcher.Tests` **28/28**（新增 2 项：未绑定回落 / 已注入未绑根回落）；StateHoldDriven 73/73、Core 396/396、Axioms 84/84、Runtime 21/21、Integration 30+1；K9/K10 **EXIT 0** |
| **S4-3** 宿主引导注入根（批次 4） | ✅ 完成 | 见本次提交（Projects：PrjBridge + Agent；Frame：文档） | `DataCenter.Tests` **58/58**（新增 2 项引导判据）、`Agent.Launcher.Tests` **30/30**（新增 2 项注入面判据）；`Agent.sln`/`FileDriven.sln`/`PrjBridge.csproj` 均 **0 错 0 警**；回归零变动（73/396/84/21/30+1）；K9/K10 **EXIT 0**；`Check-BakeFramework.ps1` #2 为既有工具问题（非本步引入） |
| **S4-4a** 状态真源注入面 + K9 待迁移归零（批次 5） | ✅ 完成 | Frame `ff5da13`/`73f3773`；Projects `46ac754`/`5431283`；主仓 `cd5a3eb` | K9 白名单 3 / 待迁移 **0** / 违规 0 → **EXIT 0**；`Agent.Core.Tests` **396 → 399**（+3 注入面断言）；`Agent.Launcher.Tests` 30/30、`DataCenter.Tests` 58/58、`Integration.Tests` 30+1；`Agent.sln`/`FileDriven.sln`/`MainProj.sln`/`PrjBridge` 全 **0 错 0 警**；`Check-AgentDataSeparation -Check` **EXIT 0**（D5 疑似持有 24 → 22） |
| **T3** 投影器装配单一工厂（关 F-02） | ✅ 完成 | Frame `2520832`；Projects `b984950`；主仓 `b231b8f` | `DataCenter.Tests` **58 → 59**（新增「首建/换代同投影器实例」断言）；`StateHoldDriven.Tests` 73/73；`DataCenter.slnx`/`MainProj.sln`/`Agent.sln`/`FileDriven.sln` 全 **0 错 0 警**；四门禁 EXIT 0 |
| **S4-4b** 换代生产闭环（`RootFactory` 注入 + `SwapAsync` 触发 + 开关默认切开） | ✅ 完成 | 见本次提交（Projects：Agent 侧 + PrjBridge；Frame：文档；主仓：判据） | `DataCenter.Tests` **59 → 65/65**（新增 6 项：开关默认切开语义 / 宿主真源登记进根分区 + Agent 侧读路径命中 / 换代双代数同序推进 + 真源复用 / 注入装配入口被采用 + 上下文校验 / 旧扩展代数卸载〔ALC 卸载态可观测〕 / 未引导换代 fail-fast）；回归零变动（73/399/84/21/30/30+1）；`DataCenter.slnx`/`Agent.sln`/`FileDriven.sln`/`MainProj.sln`/`PrjBridge` 全 **0 错 0 警**（薄输出保持）；K9 白名单 3 / 待迁移 0 / 违规 0、K10 457 文件 / 新增 0 / 基线 22 / 违规 0、D1–D5 与单写者均 **EXIT 0** |

## 2. 已落地实现要点

### A0（`Frame\DataCenter\DataCenter.Hosting`）
- Protocol 新增 `IRuntimeSlot` / `IRuntimeSlotPartition`（**Register** = 装配期注册 fail-fast；**Replace** = 处置期替换，对齐 §2.3 写口定义）+ `IDataCenterRoot.Slots`。
- Core 新增 `EmptyRuntimeSlot`；`DataCenterRoot` 增可选第 5 参（既有调用零破坏）。
- Hosting：`DataCenterHost.Boot/ShutdownAsync/SwapAsync` + `DataCenterHostOptions` + `HostRuntimeSlot` + `HostRootProvider` + `GenerationPlan`；单根守卫复用 `RootInstanceGuard`。
- K9 门禁：`Check-DataCenterGenericity.ps1` 扩 K9（产品侧 `new StateCenter(` / 静态持有根），扫描面含 `Projects\Plug\Agent`；A5 待迁移 2 项。
- **构建纪律（实测）**：改契约后必须 `dotnet build Frame\CsharpPrj\Operation\StateHoldDriven\StateHoldDriven.csproj -c Debug` 刷新烘焙副本，否则测试加载旧契约（`TypeLoadException`）。详见设计文档 §4.2。

### A1（六槽改造）
- 新增 `Agent.Contract\Runtime\AgentRuntimeSlotKeys`（11 槽键注册表）与 `AgentRuntimeRoot`（取根通道：`Bind/Unbind/IsBound` + `Partition/Resolve/TryResolve`；取根解析私有，业务侧无静态取根口 → K9 通过）。
- 六槽：`AgentStateLocator`→state-locator；`WorkflowRuntimeState`(+Host)→workflows；`SkillRuntimeState`(+Host)→skills；`AgentCoreRuntime`(+Host)→config；`LlmRuntime`(+`LlmRuntimeHost`)→llm（作用域路由 AsyncLocal 留门面）；`AgentUiHost`(`UiHostState`)→framework。
- 旧静态口保留一期为门面转发；根未绑定 → 回落进程本地容器（既有单测行为等价）。
- K10 门禁：`Check-AgentRuntimeSlots.ps1`（S1 静态可变字段 / S2 静态可写自动属性；白名单 + 22 项存量基线，新增即违规）。

### A2（挂根 + 释放纪律）
- `AgentFrameworkAssemblyContext.RootProvider`（宿主注入）+ **E 段** `RunSegmentERootSlotsAsync`：绑定取根面并把 `orchestrator`/`runtime-state`/`boundary`/`reactive` 注册进 agent 分区（覆盖式替换）。
- `AgentFramework.DisposeAsync`：释放顺序注释化 + 仅在槽仍指向本实例时 `AgentUiHost.Detach()`。

### A3a-1（域别名机制）
- `IStateRegistry` 增 `DomainAliases` / `TryResolveDomainAlias`；`GraphRegistry.WithDomainAliases(...)`（增量复制，原表不变）。
- `StateUriMapper.Resolve(entry, registry?)` 别名优先；`TierStateProjector` 传注册表 → `Agent.{Scheduler,Session,Round}` 可在装配期登记为同一 domain（零落盘迁移）。

### A3a-2（Agent 域扩展：落点 = Agent 侧，D-A3 ①）
- 新增工程 `Projects\Plug\Agent\src\Agent.DataCenter\`（程序集 `Agent.DataCenter`，**命名空间 `Agent.StateDomain`**；注册进 `Agent.sln`），`Agent.Launcher` 经 ProjectReference 消费。
- `AgentStateDomain`：域别名表（`Agent.{Scheduler,Session,Round}` → `domain=agent`，与 `AgentRuntimeSlotKeys.Domain` 同源）+ 域节点种类 `decision.record`；`Apply/Default/Resolve/MissingRegistrations/IsFullyRegistered`（只登记，不新增机制；Core 保持零领域词汇）。
- 装配期接入（E 段）：`AgentFrameworkAssemblyContext.Registry` + `AgentComposition.RunSegmentERootSlotsAsync` 登记装配期生效注册表。
- **实施发现（两条硬约束，已回写设计文档 §2.1/§3.3/§4 A3）**：
  ① 命名空间不得取 `Agent.DataCenter` —— 会在 `Agent.*` 内遮蔽全局 `DataCenter` 命名空间（`DataCenter.Protocol.*` → `CS0234`，实测）；
  ② 域别名/Kind 必须在宿主 `DataCenterHost.Boot` **之前**并入 `DataCenterHostOptions.Registry`（投影器 Boot 时固化注册表实例，之后登记对已建投影器无效）。
- 判据测试：`DataCenter.Tests\AgentStateDomainTests.cs`（无别名三域分裂对照 / 别名登记齐备 / 原表不可变 / 三命名空间同域且 tier 保持区分 / 投影单域闭合 + 幂等）。
- **A3a-2 ④ 未落地（独立提交）**：对象槽可观测投影（LLM 路由模式/后端清单、技能热集计数、工作流编译缓存键）—— 需 `IStateEntrySource` 的槽快照实现，随 A2+/A3b 排期。

### A5-1（S4-1 ALC 可收集化 + 实例化代数工厂）
- `ExtensionAssemblyHost`（static 状态持有者）**退役** → `ExtensionGenerationHost`（**一个代数 = 一个 scoped ALC + 装配清单**；`Unload` + `ContextReference` 回收判据）+ `ExtensionGenerationFactory`（进程级代数序列 + **当前代数唯一持有者**：`Initialize` 幂等首调生效 / `Swap` 换代返回旧代数 / `ResolveType`·`Load`·`ValidateDeployment` 便捷转发）。
- `ExtensionLoadContext`：**`isCollectible: false → true`**（换代回收前提；ALC 名按代数号 `ExtensionContext#{n}`）。
- 消费方迁移（消除 static 面）：`FunctionLibraryLoader`（Frame）、`LauncherBootstrap`、`PrjBridgeWiringHook`（Projects\FileDriven）；测试 `ExtensionAssemblyHostTests` → `ExtensionGenerationTests`。
- 判据：`Unload_AfterGenerationDropped_ContextIsCollectible`（**含已装载程序集的 ALC** 在 Unload + 释放引用后 `WeakReference.IsAlive == false`）；`Swap_AdvancesGeneration_AndReturnsPreviousGeneration`；原 4 项插件加载断言零回归。
- 诚实边界（已写进类注释）：**可收集 ≠ 必然回收** —— 默认 ALC 侧长期持有扩展类型（反射注册的回调/委托/句柄）时旧代数仍会存活；故换代须 Drain + 不跨代复用句柄/委托（文档 D5）。
- **发现（既有缺陷，与本步无关）**：`Environment\CentralTest\CSharpTest\Share.Tsets\Registry.Tests\AutoRegisterTests.cs` 引用 `Share.Registry.AutoRegister.*`，而源码 AutoRegister 命名空间为 `Modules.Registry.AutoRegister.*` → 该测试工程**编译失败**（Frame `02d9b20` 位置框架化后遗留；测试文件自 `430f17e` 未改）。本步未修（越界），建议独立小提交把 `using Share.Registry.` → `Modules.Registry.` 后纳入回归集。

### A5-2（S4-2 换代装配入口：宿主注入 + 跨代载荷）
- `DataCenterHostOptions.RootFactory`：`Func<RootSwapContext, IDataCenterRoot>?`（**宿主注入的新代数装配入口**；null = 同 ALC 路径，等价回退）。
- 新增 `RootSwapContext`（装配输入）：`(RootCell, Registry, Generation, PreviousGeneration, Slots)`；新增 `GenerationPayload`（跨代载荷，只承载"不在状态真源里"的数据）。
- `GenerationPlan` 增 `Export` / `Import` 钩子；`SwapAsync` 新序：Drain → Export → 解绑/释放旧根 → 装配新根（generation+1）→ 绑根 → Import → Resume（**finally 放行**）。
- **三条实施发现（已写进代码注释与《下一步实施计划》S4-2 段）**：
  ① **ALC 边界**：`RootInstanceGuard` 为 `DataCenter.Core` 内 static → "新 ALC" 只覆盖扩展装配产物；根/真源/守卫必须默认 ALC（否则 DF8 静默失效 + 类型身份分裂）。
  ② **Seq 语义**：Core 无独立 `Seq`；版本单调归 `StateSubscriptionHub._lastSeen`（`StateCenter` 私有）→ 复用 `_cell/_center` 即天然连续，故 `Export/Import` 不承载状态 KV（避免第二真源）。
  ③ **非法交出物须先释放**：交出物若挂异真源/代际不符，直接抛错会泄漏单根守卫计数 → 回建旧代数二次占用失败（已修复：先 `Dispose` 再抛）。
- 判据：`DataCenter.Tests` **53/53**（新增 4 项：装配入口被调用 + 上下文真源/槽/代际 + 状态连续 + 默认 ALC；边界顺序 Drain→Export→Import→Resume；装配失败保留旧代数 + 放行写入 + 失败可重复；交出异真源 fail-fast 且保留旧代数）。

### B1+B2（P0 前置：换代窗口取根回落 + 指纹保留，2026-09-12）
- **B1（F-16/F-17）**：`IRootProvider` 增**非抛探测** `TryGetRoot(out root)`（`DataCenter.Protocol` 契约）；`HostRootProvider`（Hosting）与 `RootProvider`（Core，含 `Unbound` 哨兵）实现；`AgentRuntimeRoot` 以私有 `TryGetCurrentRoot` 统一"根当前可用"判定 —— `IsBound` 与 `Partition` 回落语义一致（未注入 / 未绑根 / **解绑窗口** 均回落进程本地容器）；`AgentStateDomain.Resolve` 窗口内回落 `Default`。
- **语义分工（本轮口径）**：**装配错误** → `Root`（fail-fast 保留，`RootMissingException`）；**窗口内暂时无根** → `TryGetRoot`（回落，不得升格为异常）。
- **B2（F-18）**：`DataCenterHost.Advance` 改取 `plan.Fingerprint ?? state.Fingerprint`（未提供指纹**不再静默置空**）；`GenerationPlan.Fingerprint` 与 `GenerationState.Next` 注释口径同步（null = 沿用 / 置空的分工显式化）。
- **判据（实测）**：`SwapAsync` **解绑窗口**（`Export` 之后至绑根前，观测点 = `RootFactory` 内）`TryGetRoot`=false 且 `Root` 仍 fail-fast，换代后取根面重新可用；`swap("fp-1")` → `swap(无指纹)` 后 `Fingerprint == "fp-1"`。
- 新增落点：`Environment\CentralTest\AgentTest\Agent.Launcher.Tests\AgentRuntimeRootFallbackTests.cs`（该 csproj 显式接线 `DataCenter.Protocol`，AgentTest 通配清单只取 `Agent.*.dll`）。

### S4-3（宿主引导注入根 + 取根面注入面，2026-09-12）
- **落点（实测修正）**：Boot 触发点 = **`Projects\PrjBridge\Agent\AgentSelfHostPlugin`**（宿主侧组合根）；Boot **装配实现** = `Agent.DataCenter` 新增 `AgentRuntimeRootBootstrap`（命名空间 `Agent.StateDomain`）。
  - 修正理由（实测）：`FileDriven.Execution.csproj:71-72` 已明确「本层删除对 `Projects\Agent` 的直接引用」，且 `FileDriven` 为**薄启动层**（零上游静态引用）；故 Boot 不能落在 `LauncherBootstrap`（原计划字面落点），否则须打破既有引用边界。
  - 实现上把 Boot 门面放在 Agent 侧：域表（`AgentStateDomain`）与 Boot 同程序集 ⇒ **纪律④（域别名须在 Boot 前并入）顺序天然成立**；PrjBridge 仅新增 1 条 `Agent.DataCenter` 引用（`Private=false`，保持薄输出）+ 1 条 `DataCenter.Protocol`（`Agent.DataCenter` 公开签名需要）。
- **`AgentRuntimeRootBootstrap`**（`Projects\Plug\Agent\src\Agent.DataCenter\AgentRuntimeRootBootstrap.cs`）：**实例**持有唯一 `DataCenterHost`（不设静态槽 → K9/K10 零命中）；`Enabled` 实例属性 = **开关默认关**（env `AGENT_DATACENTER_ROOT=1|true` 可开，D-S43 ①）；`TryBoot()` 交出 `IRootProvider`（开关关 → `null`）；域表并入 + **`IsFullyRegistered` 装配期自检**（F-10 首个消费方）；根代际起点 = `ExtensionGenerationFactory.Generation`（③ 扩展代数↔数据代数对齐）；`Shutdown()` 幂等。
- **取根面注入面（3d）**：`AgentFrameworkHosting.TryBootstrapGuiAsync(..., IRootProvider? rootProvider)` → `AgentComposition.StartAsync` → `AgentFramework.StartAsync` → `AgentFrameworkAssembler.AssembleAsync` → `AgentFrameworkAssemblyContext.RootProvider`（**新参数一律追加末位**，既有调用零破坏）→ E 段 `RunSegmentERootSlotsAsync` 绑定取根面 + 对象槽落根分区。
- **ALC 边界（3a）**：`LauncherBootstrap` 的 `Default.Resolving` 探针增 `Frame\DataCenter\BuildOutput\$(Config)` 解析（懒求值）——宿主/桥侧首次消费平台库时由**默认 ALC** 就地解析 ⇒ 契约程序集身份唯一（避免 scoped ALC 自装副本 → 类型分裂 + 单根守卫失效）。
- **判据（实测）**：`DataCenter.Tests` **58/58**（新增 2 项：开关关不引导 / 开关开引导 + 代数对齐 + 域表齐备 + 幂等同实例）；`Agent.Launcher.Tests` **30/30**（新增 2 项：注入取根面 → E 段绑定 + 槽落根分区〔同实例断言〕 / 未注入 → 回落本地容器 + 默认域表）；`PrjBridge.csproj` **0 错 0 警**（薄输出保持：bin 仍仅 dll/pdb/deps.json/runtimeconfig.json）；`Agent.sln` / `FileDriven.sln` **0 错 0 警**；回归零变动（73/396/84/21/30+1）；K9 **EXIT 0**（白名单 2 / 待迁移 2 / 违规 0）、K10 **EXIT 0**（457 文件 / 新增 0 / 基线 22 / 违规 0）。
- **诚实边界**：`Check-BakeFramework.ps1` #1/#3/#4/#5 通过、**#2 失败**（`csharp-bake.manifest.json` 缺失）—— 既有工具口径问题（2026-09-11 已记录并保持基线回退：生成 manifest 会使 #3 转为失败），**非本步引入**。
- **未做（下一批）**：`RootFactory`（跨 ALC 装配入口）接线、端到端「热换一轮 + 旧 ALC 回收（`WeakReference` 证）」判据 —— 归 **S4-4b**（= 分离方案 Batch 1c/1d/1e）。

### S4-4a（状态真源注入面 + K9 待迁移归零，2026-09-12）
- **用点清单（1a 前置，实测）**：`AgentOrchestrator._stateCenter` 唯一消费点 `:117 new AgentRunnerCore(_loopConfig, _stateCenter)` → `AgentRunnerFactory` → `AgentRunner.cs:133 new WorkflowRunner(center: core.StateCenter, …)`；`WorkflowRunner._center` 唯一消费点 `:200 new StateContextAdapter(_center, …)`。两处原各有一个 `?? new StateCenter(...)` 兜底（同一职责两个真源）。
- **注入面**：`AgentRuntimeSlotKeys` 增 `state-center` 键（注册一行）；`AgentRuntimeRoot.ResolveStateCenter()`（槽注册优先 → 显式回落）+ `RegisterStateCenter()`（装配期注册，`Replace` 覆盖语义）；`AgentOrchestrator(IAgentLoopConfig, StateCenter?)` 末位注入 + 只读 `StateCenter` 属性；`AgentModuleRegistrar.SetupAgentAsync` 末位透传；`AgentFrameworkAssemblyContext.StateCenter` → C 段注入编排器 / E 段登记根槽分区（`host.State` 经此进入通道 A）。
- **兜底造根归零**：删 `AgentOrchestrator.cs:93` 与 `WorkflowRunner.cs:89` 的 `?? new StateCenter(...)`，两处改经 `ResolveStateCenter()`；`$k9Whitelist` 删两行 `Pending=$true`（不再命中，保留会掩盖回流）。
- **回落真源落点（Frame）**：新增 `DataCenter.Hosting.FallbackStateCenter.Create(Func<IStorageCell>)` —— `new StateCenter(` 属「根的创建」，只允许出现在宿主装配层（命中既有 `\DataCenter.Hosting\` 白名单路径）；存储后端不在此绑定（单元格工厂由调用方提供）⇒ 后端语义零变化。
- **与设计口径的差异（实测）**：原拟「经 `IRootProvider.Root.Root`（`IDataCell`）取真源」不可行 —— `IDataCenterRoot` 只暴露 `Root`（真源 cell），不暴露 `StateCenter`；Agent 侧 `new StateCenter(cell)` 会绕开宿主 `StateCenter` 的订阅/生命周期，且重新触发 K9。故取「槽注册优先 + 显式回落」；是否给 `IDataCenterRoot` 增开 `State` 成员留待 S4-4b 评审。
- **判据（实测）**：K9 白名单 **3** / 待迁移 **0** / 违规 0 → **EXIT 0**；`Agent.Core.Tests` **399/399**（+3：注入原样采用 `ReferenceEquals` / 未注入同实例幂等 / 注册优先于回落）；`Agent.Launcher.Tests` 30/30、`DataCenter.Tests` 58/58、`Integration.Tests` 30+1；`Agent.sln`/`FileDriven.sln`/`MainProj.sln`/`PrjBridge` 全 0 错 0 警；`Check-AgentDataSeparation.ps1 -Check` EXIT 0（D5 疑似持有 24 → 22）。

### T3（投影器装配单一工厂，2026-09-12；关 F-02）
- **病灶**：`DataCenterHost.cs` 的 `Boot(:115)` 与 `BuildRoot(:254)` 两处重复投影器装配表达式（`new TierStateProjector(new StateCenterEntrySource(...))`）⇒ 首建/换代口径可能分叉（改一处漏一处）。
- **实现**：抽 `private static CreateProjector(projectorOverride, center, registry, codec, generation)` 单一工厂（`Boot` 与 `BuildRoot` 共用）+ 实例口径 `CreateProjector(generation)`；**新增公开** `IDataCenterRoot CreateCoreRoot(IStateGeneration)` —— 宿主注入 `RootFactory` 时经此复用同一口径（交出物必挂本宿主真源/槽，故必然通过交出物校验），消除宿主侧再复制一遍投影器表达式。
- **判据（实测）**：`DataCenter.Tests` **59/59**（新增 `CoreRootFactory_SingleAssemblyPath_BootAndSwapShareSameProjector`：注入投影器在首建与换代两路径同实例）；既有 `SwapAsync_WithInjectedRootFactory` / `SwapAsync_DuringRebindWindow` 两用例改用 `CreateCoreRoot` 后继续通过（证明「委派即合法交出物」）；`StateHoldDriven.Tests` 73/73；四门禁 EXIT 0。
- **意义**：为 S4-4b（1c）扫清障碍 —— 宿主 `RootFactory` 无需重复装配表达式即可交出合法新代数根。

### S4-4b（换代生产闭环：1c/1d/1e，2026-09-12）
- **1e 开关默认切开**：`AgentRuntimeRootBootstrap.Enabled` 缺省由「关」改为**开** —— 仅显式 `AGENT_DATACENTER_ROOT=0|false` 关闭（关 = 返回 `null`，装配侧回落进程本地容器，等价存量路径）；环境变量读取失败按开处理（不因环境异常静默失去根）。语义反转点 `ReadEnvSwitch`（原「显式 1/true 才开」）。
- **1c 生产注入装配入口**：`TryBoot` 的 `DataCenterHostOptions.RootFactory` 不再留空 —— 注入 `context => (RootFactory ?? DefaultRootFactory)(context)`；`DefaultRootFactory` 委派新增公开 `CreateCoreRoot(IStateGeneration)`（T3 同一装配口径，不在宿主侧重复投影器表达式），宿主可覆写 `RootFactory` 属性在该入口内于**新扩展代数 ALC** 中重建对象槽产物（重建落点 = `RootSwapContext.Slots`）。
- **1c-补 通道 A 挂根（生产路径）**：原先生产装配不注入 `StateCenter`（S4-4a 只落地注入面），`ResolveStateCenter()` 实际走回落真源。现由引导侧在 Boot 后把宿主真源登记进 Agent 域分区（`state-center` 键，`Replace` 语义）⇒ 「开 = 走根路径」对通道 A 真正成立；换代复用同一槽承载 ⇒ 该登记跨代延续（不产生第二份真源）。
- **1d 换代触发接线（消 F-12）**：新增 `AgentRuntimeRootBootstrap.SwapAsync(GenerationPlan?, ct)` —— ① `ExtensionGenerationFactory.Swap(当前清单)`（扩展代数 N→N+1，返回旧代数）→ ② `DataCenterHost.SwapAsync(plan)`（数据代数同序推进，经注入装配入口）→ ③ 旧扩展代数 `Unload()`；`SemaphoreSlim(1,1)` 串行化换代。失败语义显式声明：根已由宿主回建旧代数（真源连续、值不丢）并外抛；扩展代数**不回滚**、旧 ALC 不卸载（工厂无回退语义）。另设 `SwapGenerationAsync(ct)` 薄重载（签名不含 `DataCenter.Hosting` 类型）供宿主薄启动层触发，避免桥层为单一触发点引入引用。
- **1d 宿主触发点**：`PrjBridge.Agent.AgentSelfHostPlugin.SwapGenerationAsync(ct)`（宿主侧组合根唯一触发点；成功返回当前自读 Readiness，失败返回 `NotReady` 归因且**不改变**已装配可用性）。
- **判据（实测）**：`DataCenter.Tests` **65/65**（59 → 65，新增 6 项，见 §1 总览行）；`StateHoldDriven.Tests` 73/73、`Agent.Core.Tests` 399/399、`Agent.Axioms.Tests` 84/84、`Agent.Runtime.Tests` 21/21、`Agent.Launcher.Tests` 30/30、`Integration.Tests` 30+1；`DataCenter.slnx`/`Agent.sln`/`FileDriven.sln`/`MainProj.sln`/`PrjBridge` 全 0 错 0 警（PrjBridge 薄输出保持：bin 仅 dll/pdb/deps.json/runtimeconfig.json）；K9/K10/`Check-AgentDataSeparation`/`Check-SingleWriter` 四门禁 **EXIT 0**。
- **实测发现（诚实边界，写入判据注释）**：换代后旧扩展代数 ALC 的**卸载**可观测（`Unload` 后向其装载抛 `InvalidOperationException: AssemblyLoadContext is unloading or was already unloaded`），但**运行时回收（`WeakReference.IsAlive == false`）在进程内不可达** —— 实测旧代数宿主对象在「工厂复位 + 宿主停机 + 强制 GC」后仍存活（引用根不在本批新增代码内），对齐既有诚实边界 R-3/R-4「可收集 ≠ 必然回收」。故端到端判据取「卸载态可观测」，机制面回收判据仍由 `Integration.Tests\ExtensionGenerationTests` 覆盖。
- **未纳入本批（依赖 Batch 3）**：对象槽产物类型的**新代数 ALC 重建**（= Agent 框架在新代数重装配）需先完成 16 项静态槽归零；本批交付的是换代机制的生产接线 + 数据跨代连续 + 旧 ALC 卸载（`RootFactory` 即宿主重建入口）。

## 3. 后续步骤

> **权威**：下一步的**步骤 / 改动点 / 判据 / 回退 / 提交粒度**以 `DataCenter-Agent运行时数据接入-下一步实施计划.md` 为唯一权威（含 D-批次序 / D-S42 / D-S43 / D-A4 / D-A5 决策点与 S4-2–S5 明细）。此处仅保留阶段级状态与阻塞原因，避免两处重复维护。
> **缺陷台账**：调用链的**重复 / 冗余 / 错误逻辑**见 `DataCenter-Agent运行时数据接入-调用链逻辑缺陷清单.md`（F-01..F-26；S4-3 前置 = F-16/F-17/F-18，S4-4 前置 = F-24）。

| 阶段 | 状态 | 阻塞 / 前置 |
|---|---|---|
| **A3a-2** 域扩展工程（域 Keys + 域投影） | ✅ 完成（实现要点见 §2 A3a-2） | ④ 对象槽可观测投影未落地（独立提交，待排期） |
| **A3b** `TraceStamp` + `ITraceQuery` | ⏸ 阻塞 | 前置 = 框架侧 **D2/D3** 追踪面契约入 Protocol（见 `DataCenter-可追踪数据流图框架-设计与实施.md` §7 D2/D3） |
| **A4** 双写收敛 | ⏸ 未开始 | 风险最高：须独立开关（默认关）+ 故障注入 + 单独评审 |
| **A5** 换代闭环 | ✅ **A5-1 / A5-2 / S4-3 / S4-4a / S4-4b 已完成**（见 §2 各条） | 后续强化（对象槽产物新代数 ALC 重建）依赖 Batch 3 静态槽归零 |
| **S4-4b** `RootFactory` 生产注入 + `SwapAsync` 触发 + 引导开关默认切开 | ✅ 完成（= 分离方案 Batch 1c/1d/1e，2026-09-12） | 见 §2 S4-4b；残项 = 框架重装配（Batch 3） |

## 4. 复用命令清单

```powershell
dotnet build Frame\DataCenter\DataCenter.slnx -c Debug                       # 契约/宿主
dotnet build Projects\Plug\Agent\Agent.sln -c Debug                          # Agent 侧
dotnet build Frame\CsharpPrj\Operation\StateHoldDriven\StateHoldDriven.csproj -c Debug   # 契约变更后刷新烘焙
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Tests\DataCenter.Tests.csproj
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\StateHoldDriven.Tests\StateHoldDriven.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Core.Tests\Agent.Core.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Axioms.Tests\Agent.Axioms.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Runtime.Tests\Agent.Runtime.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Launcher.Tests\Agent.Launcher.Tests.csproj
pwsh Frame\PowerShellScript\Operation\DSL\Check-DataCenterGenericity.ps1 -Check   # G1/G2/K9
pwsh Frame\PowerShellScript\Operation\DSL\Check-AgentRuntimeSlots.ps1 -Check      # K10
```



### B26 + B27 · 第四轮余项收敛（F-61/F-62/F-63/F-64/F-65/F-66/F-67/F-70，2026-09-12，单提交）

| 子项 | 内容 | 判据 |
|---|---|---|
| B26-1（F-62） | 会话完成**单次 upsert**：删 `FinalizeAsync` 二次 `WriteStatusAsync` | `ConversationFinalizeSingleWriteTests`（Update=1 / Status=0） |
| B26-2（F-66） | 确定性核心**边界化**（注释 + 新门禁 `Check-AgentDecisionCoreBoundary.ps1`，不删代码） | 门禁 EXIT 0（494 文件 / 违规 0） |
| B26-3（F-63） | **取证结案**：执行链已单形（`NodeType=node` + 内嵌 `Outputs`）；残余 = API `node.write` 缺省形；注释按实证改写 + 读一致性判据 | `NodeOutputStoreTests.ReadView_TaskScoped_...`；Persistence 88/88 |
| B27-1（F-61） | 删死实现 `SnapshotSerializer` + 3 测试消费点；`ur_snapshots` 表路由保留 | grep 命中 0；Core 405/405 |
| B27-2（F-64） | 观测双源**分工声明**（零行为改动）+ 设计文档 §10 | Observe 11/11 |
| B27-3（F-65） | UI 宿主 9 对象槽改**根分区承载**（`RootSlot<T>` + 9 槽键，未绑根回落本地容器） | `AgentUiSlotRootPartitionTests`；Launcher 37/37 |
| B27-4（F-67） | 删未落地槽键 `Database`/`Prompt` | grep 命中 0；Agent.sln 0/0 |
| B27-5（F-70） | 词种分工 + 覆盖断言 + `Reset/Clear` 语义表 | `DecisionSurfaceWordCoverageTests`；Core 405/405 |

**统一验证**：`Agent.sln` 0/0 · `PrjBridge.csproj` 0/0 · 13 测试工程全绿（Core 405 / Launcher 37 / Persistence 88 / DataCenter 95 / 其余同上级 §0）· 四门禁全过。
**实测偏差**：① F-63"写两形"已过期（执行链单形，残余在 API 写路径）② F-61"零消费方"仅对生产代码成立（测试 3 处，已删）③ F-65 的 `Framework` 槽此前已在根分区，本次收敛 `Attach*` 系列 9 槽。

