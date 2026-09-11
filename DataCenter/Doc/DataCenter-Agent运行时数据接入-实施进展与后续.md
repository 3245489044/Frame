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
| **A3a-2** `DataCenter.Extensions.AgentState` | ⏳ 待确认落点（§3 D-A3） | — | — |
| **A3b** `TraceStamp` + `ITraceQuery` | ⏸ 阻塞于 D2/D3 | — | Protocol 尚无追踪面契约 |
| **A4** 双写收敛 | ⏸ 未开始（风险最高，须独立开关 + 独立评审） | — | — |
| **A5** 换代闭环 | ⏸ 未开始（依赖宿主侧 ALC/引导改动） | — | — |

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

## 3. 后续步骤

> **权威**：下一步的**步骤 / 改动点 / 判据 / 回退 / 提交粒度**以 `DataCenter-Agent运行时数据接入-下一步实施计划.md` 为唯一权威（含 D-A3/D-A4/D-A5 决策点与 S1–S5 明细）。此处仅保留阶段级状态与阻塞原因，避免两处重复维护。

| 阶段 | 状态 | 阻塞 / 前置 |
|---|---|---|
| **A3a-2** 域扩展工程（域 Keys + 域投影） | ⏳ 待开工 | 需先定 **D-A3 落点**（设计文档要求放 Frame，但代理域键权威在 Projects → 会造成逆向引用；推荐 Agent 侧） |
| **A3b** `TraceStamp` + `ITraceQuery` | ⏸ 阻塞 | 前置 = 框架侧 **D2/D3** 追踪面契约入 Protocol（见 `DataCenter-可追踪数据流图框架-设计与实施.md` §7 D2/D3） |
| **A4** 双写收敛 | ⏸ 未开始 | 风险最高：须独立开关（默认关）+ 故障注入 + 单独评审 |
| **A5** 换代闭环 | ⏸ 未开始 | 依赖宿主侧改动（可收集 ALC、引导注入根）与 K9 待迁移两项归零 |

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

