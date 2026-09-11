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

## 3. 后续步骤

> **权威**：下一步的**步骤 / 改动点 / 判据 / 回退 / 提交粒度**以 `DataCenter-Agent运行时数据接入-下一步实施计划.md` 为唯一权威（含 D-批次序 / D-S42 / D-S43 / D-A4 / D-A5 决策点与 S4-2–S5 明细）。此处仅保留阶段级状态与阻塞原因，避免两处重复维护。

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

