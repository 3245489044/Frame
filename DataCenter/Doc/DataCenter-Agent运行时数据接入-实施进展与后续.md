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
| **A5** 换代闭环 | 🚧 进行中（**S4-1 / S4-2 已完成**；S4-3..S4-4 待做） | Frame `f0dc593`（S4-1）、S4-2 见本次提交；Projects `cac5b26`（A3a-2）/`32f5043`（S4-1 消费方）；主仓 `ab5e8aa`（S4-1/A3a-2 判据） | S4-1：`Frame\CsharpPrj\MainProj.sln` **0 错 0 警**、`Projects\FileDriven\FileDriven.sln` **0 错 0 警**；`Integration.Tests` **30 通过 / 1 跳过**（代数回收 + 换代语义判据；原插件加载 4 项零回归）；S4-2：`DataCenter.slnx` **0 错 0 警**、`DataCenter.Tests` **53/53**、K9/K10 **EXIT 0** |
| **B1+B2** P0 前置（F-16/F-17/F-18） | ✅ 完成 | 见本次提交（Frame 契约/Hosting/Core、Projects Agent 侧、主仓判据） | `DataCenter.slnx` **0 错 0 警**、`Agent.sln` **0 错 0 警**；`DataCenter.Tests` **56/56**（新增 3 项：非抛探测语义 / 换代解绑窗口回落 + 换代后重绑 / 无指纹沿用）；`Agent.Launcher.Tests` **28/28**（新增 2 项：未绑定回落 / 已注入未绑根回落）；StateHoldDriven 73/73、Core 396/396、Axioms 84/84、Runtime 21/21、Integration 30+1；K9/K10 **EXIT 0** |
| **S4-3** 宿主引导注入根（批次 4） | ✅ 完成 | 见本次提交（Projects：PrjBridge + Agent；Frame：文档） | `DataCenter.Tests` **58/58**（新增 2 项引导判据）、`Agent.Launcher.Tests` **30/30**（新增 2 项注入面判据）；`Agent.sln`/`FileDriven.sln`/`PrjBridge.csproj` 均 **0 错 0 警**；回归零变动（73/396/84/21/30+1）；K9/K10 **EXIT 0**；`Check-BakeFramework.ps1` #2 为既有工具问题（非本步引入） |

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
- **未做（下一批）**：`RootFactory`（跨 ALC 装配入口）接线、端到端「热换一轮 + 旧 ALC 回收（`WeakReference` 证）」判据（属 S4-4 前置与 S5 验收）。

## 3. 后续步骤

> **权威**：下一步的**步骤 / 改动点 / 判据 / 回退 / 提交粒度**以 `DataCenter-Agent运行时数据接入-下一步实施计划.md` 为唯一权威（含 D-批次序 / D-S42 / D-S43 / D-A4 / D-A5 决策点与 S4-2–S5 明细）。此处仅保留阶段级状态与阻塞原因，避免两处重复维护。
> **缺陷台账**：调用链的**重复 / 冗余 / 错误逻辑**见 `DataCenter-Agent运行时数据接入-调用链逻辑缺陷清单.md`（F-01..F-26；S4-3 前置 = F-16/F-17/F-18，S4-4 前置 = F-24）。

| 阶段 | 状态 | 阻塞 / 前置 |
|---|---|---|
| **A3a-2** 域扩展工程（域 Keys + 域投影） | ✅ 完成（实现要点见 §2 A3a-2） | ④ 对象槽可观测投影未落地（独立提交，待排期） |
| **A3b** `TraceStamp` + `ITraceQuery` | ⏸ 阻塞 | 前置 = 框架侧 **D2/D3** 追踪面契约入 Protocol（见 `DataCenter-可追踪数据流图框架-设计与实施.md` §7 D2/D3） |
| **A4** 双写收敛 | ⏸ 未开始 | 风险最高：须独立开关（默认关）+ 故障注入 + 单独评审 |
| **A5** 换代闭环 | 🚧 S4-1 / S4-2 已完成（见 §2 A5-1/A5-2）；S4-3..S4-4 待做 | 依赖宿主侧引导注入根 + K9 待迁移两项归零 |

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

