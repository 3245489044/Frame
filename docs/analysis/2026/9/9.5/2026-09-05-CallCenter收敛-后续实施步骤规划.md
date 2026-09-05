# CallCenter 收敛 · 后续代码实施步骤规划（基于源码核实的缺口盘点）

> **日期**：2026-09-05
> **源文档**：
> - `[源1] 2026-09-04-跨语言中央调用中心-函数运行时架构.md`（§16.9「下次续做清单」为起点）
> - `[源2] 2026-09-04-Agent调用逻辑收敛至CallCenter通用框架-收敛设计.md`（§十 实施裁决收口）
> **职责层级**：L3（流程/规范流程）| **依据**：`SingleImplementation/TH-12`、`RedundancyElimination/TH-13`（不制造平行实现）、`ANTI-DUPLICATED-IMPLEMENTATION`
>
> **重要前提**：本文档动工前先以源码核对现状。结论：**源 §16.9 记录滞后于代码**——CallCenter 库/CLI 层收敛已远超该清单（`CentralTest/CallCenterTest/CallCenter.Tests` **78/78 通过**；`CallCenter.Host` CLI `--list-tools/--call/--watch/--face/--mcp-list-tools` 全部实现并可实跑）。
> 因此本规划**不重做已落地项**，只列真实剩余缺口，并把源 §16.9 的状态行刷新为「已落地/剩余」两态，避免照旧清单重复劳动。

---

## §〇 现状核实摘要（2026-09-05 实测）

- 单测：`dotnet test CentralTest\CallCenterTest\CallCenter.Tests\CallCenter.Tests.csproj` → **78 通过 / 0 失败**（含 BuildCache / CsgToolCatalog / CSharpFunctionLibrarySource / CSharpTrack(+EF) / HotReloadVersionStore / McpBridgeOrigins / McpBridgeTrack / McpResult / SeedToolSource / ToolDescriptorFingerprint / ToolFunctionSchemaMapper / ToolRouter / TrackHotReloader / TsScriptCompiler / UnifiedToolFace / ToolRouter）。
- Host CLI 实测（本机 node v22.17 / MCPBridge build/index.js 存在）：
  - `--list-tools` → read_nodes + CallCenterHostLibrary(cc_*) + FileDriven.Business(MathLibrary:Add/Subtract/… + FunctionTesterFunctions:Func_*) 全量入轨；
  - `--call cc_add {a:40,b:2}` → `42`；`--call Add {a:1,b:2}` → `3`；`--call cc_echo {text:hi}` → `"hi"`（CSharpTrack 经 ExecutionFramework 适配器真实执行）；
  - `--mcp-list-tools` → 发现 6 个 MCPBridge 工具且正确分类（TypeScript：bridge_list/bridge_reload/bridge_status/node_project/node_select；PowerShell：invoke_ps_script）。
- 结构现状：CallCenter 根含 `CallCenter.Core`（Router/Tracks/Cache/Discovery/DataPlane/Schema/Tools）+ `CallCenter.Adapters`(ExecutionFramework 执行缝) + `CallCenter.Mcp`(MCPBridge 终端轨/分类/映射/TS 编译) + `CallCenter.Host`(CLI)；`CallCenter.slnx` 引用全部；Agent 侧 `Agent.Launcher`、`Agent.McpRegistryHost` 已引用 CallCenter，`CallCenterFoldToolProvider` 桥已存在，`ConversationHosting.FrontToolProvider` 已把 C# 函数库前置进折叠算子单一 `_toolProvider`。

## §一 源 §16.9 清单状态刷新

| §16.9 待办 | 代码现状核实 | 状态 |
|---|---|---|
| P4-a 持久宿主监视接线 | CLI `--watch`（FileSystemWatcher→ContentHash→`TrackHotReloader.Reload`→`ReplaceTrack`→版本登记→`PruneDrained`）已实现并可常驻 | **CLI 态已落地**；Agent/FileDriven GUI 宿主内未接（见 §二 P4-a-Agent） |
| P4-b 编译轨（C#/TS） | TS 腿 `TsScriptCompiler`+BuildCache 写入已落地（测试 `Inconclusive` 保护） | **TS 腿已落地**；**C# Roslyn 腿未实现**（见 §二 P4-b-CSharp） |
| P3 TS/PS 接入 | CallCenter.Mcp 全套 + `--mcp-*`/`--face` + UnifiedToolFaceTests | **主体已落地**；桥内工具真调用待宿主/外部子进程环境（见 §二 P3-桥内） |
| P2 宿主全量函数宇宙 | Host CLI 经 function-libraries.json 已含 FileDriven.Business 全量库并 EF 同栈 | **Host CLI 态已落地**；**Agent 组合根未同栈/未接宿主 UnifiedRegistryCenter 全量 registry**（见 §二 P2-Agent） |

## §二 真实剩余缺口（源码核实的落点）

### P3-桥内：MCPBridge 工具「真调用」待宿主依赖环境（验证闸型，非库代码缺口）
- 现象：`--mcp-list-tools` 发现/分类正常；但 `--mcp-call node_select` 报 `❌ 本地工具执行错误: Command failed: ...Agent.StorageBridge.exe node-select ...`；`--mcp-call bridge_status` 报 `❌ 未知本地工具: bridge_status`。
- 归因：CallCenter→MCPBridge 的发现/映射/路由/转发链路已通；失败发生在 **桥内工具对 Agent/FileDriven 侧外部子进程/参数/状态的依赖**（node_select 需真实 node 项目上下文，bridge_status 属桥自身状态工具），非 CallCenter 适配缺陷。
- 待办：非独立代码项，随 Agent/FileDriven GUI 宿主（`EnableMent\交互启动.bat`）E2E 一并验证；单测以内存客户端锁定链路即可（已具备）。

### P4-a-Agent：Agent/FileDriven 常驻 GUI 宿主内未接热切换监视
- 现状：`ConversationHosting.FrontToolProvider`（`Agent.Launcher\Composition\ConversationHosting.cs`）只做**一次性** `CSharpTrack(CSharpFunctionLibrarySource.FromConfig(config).DiscoverDefinitions())`，无 `FileSystemWatcher`/`TrackHotReloader` 生命周期 → 改 function-libraries.json / 库后需重启宿主才生效。
- 落点：把 `CallCenter.Core.Cache.TrackHotReloader` + 监视循环在 Agent/FileDriven 组合根装配为常驻后台任务（复用 Host `Program.cs` 的 watch 编排），使 GUI 宿主内改清单即热换 C# 轨。
- 依据：对齐 §16.9 P4-a「常驻宿主内 FileSystemWatcher→RegisterVersion→ReplaceTrack→PruneDrained」；不重复实现（复用 CallCenter 现成协调器）。

### P4-b-CSharp：C# 源码→IL Roslyn 编译轨未实现
- 现状：Core 刻意不拖 Roslyn 栈（§16.1 注释）；`CSharpFunctionLibrarySource` 走反射既有程序集，无「源码改码→内存编译→BuildCache」腿。仓库已有 `CsharpPrj/Plate/RoslynMethod/RoslynMethod.Core/Compilation/DynamicCompilation.cs`。
- 落点：新增独立装配（仿 `CallCenter.Mcp.TsScriptCompiler` 之于 tsc），以 `RoslynMethod.Core`（或独立 Roslyn 引用）实现 C# `CSharpCompilation`→内存/IL 产物并写 `BuildCache`（key=fingerprint）；复用 `ContentHash`。**决策点**：放在哪层 / 是否引入仓库 RoslynMethod（需组合根评审）。

### P2-Agent：Agent 组合根 C# 轨未接 EF 同栈 + 未接宿主全量 registry
- 现状：Host CLI `BuildRouter` 用 `ExecutionFrameworkDelegateInvoker`（与 `Func_Execute` 同栈），但 `ConversationHosting.FrontToolProvider` 用缺省 `DirectDelegateInvoker`（DynamicInvoke），执行栈分叉；且发现源是静态 function-libraries.json，非宿主运行时 `UnifiedRegistryCenter` 全量。
- 落点：Agent 组合根把宿主 registry 适配为 `CSharpFunctionLibrarySource.Registry` 并注入 EF invoker 建 CSharpTrack，与 Host CLI 同栈（TH-12）。
- 依据：对齐 §16.7 剩余「真 Agent/FileDriven 宿主内把 canonical FunctionStorageRegistry 注入 CSharpFunctionLibrarySource.Registry」。

---

## §三 后续代码实施步骤（仅剩真实缺口，每步带闸）

> 原则：每步先验证上一步基线（78/78 + Host CLI 实测）；改动尽量收在单落点文件，不制造平行实现。GUI 宿主类步骤以 `EnableMent\交互启动.bat` E2E 作运行闸。

### 步骤 1 —— P4-a-Agent：Agent 组合根内接热切换监视（改动 `Agent.Launcher`）
**目标**：`ConversationHosting` 装配的 C# 轨可热换，改 function-libraries.json 无需重启宿主。
**改动落点**：
1. `ConversationHosting.FrontToolProvider` 由「一次性 `CSharpTrack`」改为「`ToolRouter` + `TrackHotReloader`(router)」组合，router 即折叠算子前置单一工具面；
2. 新增常驻后台监视（复用 Host `Program.cs` watch 编排：FileSystemWatcher + 防抖 + 字节快照指纹 `ContentHash.Compute(bytes)` + `FromJson` + `coordinator.Reload(ToolBackend.CSharp, fp, BuildReloadTrack)`）；
3. 生命周期挂到宿主关停（取消令牌/`IDisposable`），不泄漏进程。
**注意（对齐源收敛裁决）**：折叠算子仍以「端口收敛」调用 LLM，不把 C# 轨硬迁 string seam；本步只加 C# 轨自身热换，不动 `ILlmChatPort`。
**验证闸**：Agent.Launcher 编译 0 错；`Agent.McpRegistryHost.Tests`/`Agent.Core.Tests` 回归；GUI `交互启动.bat` 下改 function-libraries.json → 日志见 `[watch] 热重载 C#Track` 且折叠工具面工具数随清单变化。

### 步骤 2 —— P2-Agent：Agent 组合根 C# 轨接 EF 同栈 + 宿主全量 registry
**目标**：`ConversationHosting` 的 CSharpTrack 与 `Func_Execute` 同执行栈（TH-12），并把宿主运行时 `UnifiedRegistryCenter`/canonical registry 注入 `CSharpFunctionLibrarySource.Registry`，使 Agent 折叠工具面 = 宿主全量函数宇宙（而非静态清单 3 库）。
**改动落点**：
1. 在 Agent 组合根（宿主已做 `WorkflowRegistryBootstrap.Initialize` / `UnifiedRegistryCenter` / `FunctionLibraryLoader` 之处）取得 canonical registry；
2. 用 EF invoker（`CallCenter.Adapters.ExecutionFrameworkDelegateInvoker` + `ExecutionFramework.Engine.FunctionExecutor`）建 `CSharpTrack`，替换 `FrontToolProvider` 里缺省 DirectDelegateInvoker；
3. `ConversationHosting` 与 Host CLI `BuildRouter` 共用同一装配 helper，消除两处重复（收敛 helper 到一处）。
**验证闸**：编译 0 错；`CSharpTrackExecutionFrameworkTests` 回归；GUI E2E 下折叠/骨架经 registry 调 Agent 注册函数命中全量。

### 步骤 3 —— P4-b-CSharp：C# 源码→IL Roslyn 编译轨（独立装配，仿 TsScriptCompiler）
**目标**：给 `C# 源码 → 内存编译 → BuildCache(key=fingerprint)` 补 C# 腿，支撑「改码即生效」的 Source/Build 轨（热加载的目标端）。
**改动落点（决策点先评审）**：
1. 决定承载层：Core 不拖 Roslyn（§16.1 注释约束）→ 建议仿 `CallCenter.Mcp.TsScriptCompiler`，新增独立装配（引用仓库既有 `RoslynMethod.Core.DynamicCompilation` 或独立 Roslyn 引用），不进 Core；
2. `CSharpCompilation`/DynamicCompilation → 内存/IL 产物 → 按 `ContentHash` 写 `BuildCache`；
3. 配套 MSTest（复用真实 Roslyn 后端，非测试内联）。
**注意**：本步是「编译腿就绪 + BuildCache 写读锁定」，激活（产物自动入 Router ReplaceTrack）属持久宿主 E2E 另一腿（步骤 1 watch 已覆盖激活侧）。不做则不阻塞 CallCenter 现状（现状 C# 轨走既有程序集反射）。
**验证闸**：新增编译腿测试绿；现有 78/78 不回归；不引入 Core→Roslyn 反向依赖。

### 步骤 4 —— P3-桥内（宿主 E2E 型，非独立代码项）
在步骤 1/2 的 GUI E2E 内一并验证 MCPBridge TS/PS 工具真调用（`node_select`/`invoke_ps_script` 在真实 Agent/FileDriven 上下文下成功），确认桥内工具对外部子进程依赖随宿主环境就绪而通过；单测保持以内存客户端锁定发现/映射/路由链路（已具备）。

### 步骤 5 —— 文档与基线归档
更新源文档 §16.9 状态（P3 主体/TS 腿/Host 态 P2/P4-a 标「已落地」；剩 Agent 宿主接线与 C# Roslyn 腿标「进行中」），并把本规划并入实施记录；最终跑全量 CallCenter 测试 + Host CLI 冒烟回归。

## §四 验证闸汇总

| 步骤 | 编译/单测闸 | 运行闸（环境可选） |
|---|---|---|
| 1 P4-a-Agent | Agent.Launcher 0 错；McpRegistryHost/Core.Tests 回归 | `交互启动.bat` 改清单 → 日志见热重载 |
| 2 P2-Agent | 编译 0 错；CSharpTrackExecutionFrameworkTests | GUI E2E 折叠经宿主全量 registry |
| 3 P4-b-CSharp | 新增 C# 编译腿测试绿；78/78 不回归 | 无（编译腿就绪态） |
| 4 P3-桥内 | 单测链路已锁定 | GUI E2E TS/PS 真调用 |
| 5 归档 | CallCenter 全量测试 + Host CLI 冒烟 | — |

> 编排建议：先做 **步骤 1 + 2（同文件域 `ConversationHosting`，可合并一次改动）**，再步骤 3（独立装配、可并行），步骤 4 随 GUI E2E 收口，步骤 5 收尾。

## §五 实施进度记录（滚动更新）

| 步骤 | 状态 | 说明 / 实测 |
|---|---|---|
| 3 P4-b-CSharp 编译腿 | ✅ 完成 | `CallCenter.Adapters/Compilation/CsSourceCompiler.cs`（复用仓库 RoslynMethod.Core 的 DynamicCompilation，产 IL 字节不 Assembly.Load）；Adapters.csproj 引 RoslynMethod.Core；`CsSourceCompilerTests` 3/3（PE 产物可 Assembly.Load 命中方法 / 写 BuildCache / 非法源抛诊断）。CallCenter.Tests 81/81（原 78 + 3） |
| 1 P4-a-Agent 热切换监视（机制层） | ✅ 核心机制完成（FileSystemWatcher 常驻接线待 GUI） | ① Core 新增 `ToolRouterReference`（原子换 router 快照，Thread-safe，Test 3/3）；② Core 新增 `ToolRouterFactory`（function-libraries 清单→全新 ToolRouter 单一入口，Test 3/3）；③ `CallCenterFoldToolProvider` 改经 `ToolRouterReference.Current` 每次取当前（保留固定 router 便捷构造零回归；Agent 侧 Test 2/2）；Agent.McpRegistryHost.Tests 8/8、CallCenter.Tests 86/86、Agent.Launcher 编译 0 错。**FileSystemWatcher 常驻监视 + ConversationHosting/AgentFramework 生命周期接线留待 GUI 环境（§六 障碍 1/2，static 组合根无生命周期句柄且无法无 GUI 验证）** |
| 2 P2-Agent EF 同栈 | ⏳ 待 GUI 环境 | 目标收窄：仅把 `ConversationHosting.FrontToolProvider` 的 CSharpTrack 由 DirectDelegateInvoker 收敛为 EF invoker（与 Host CLI 同栈）。需 Agent.Launcher 引 `CallCenter.Adapters` + ExecutionFramework，改组合根装配参数且有运行时行为影响（超时/异常映射），无 GUI 无法回归 → 留待 GUI 会话，勿在无验证下硬编组合根 |
| 4 P3-桥内 | ⏳ GUI E2E | 随宿主收口 |
| 5 归档 | ⏳ 收尾 | 源文档 §16.9 状态刷新 + 本规划并入 |

### §五.1 本轮（2026-09-05 二次会话）追加落地：共享热换监视 + Agent 装配接线（A1–A4）

> 依本规划 §四 编排「步骤 1 + 2 同文件域合并 + watch 彻底统一」，在本环境（无 GUI）完成全部**代码落地 + 单测 + Host CLI 冒烟**，
> 仅剩 GUI 折叠运行闸（见 §七 B2）。落地均复用共享实现（SingleImplementation/TH-12，不制造平行实现）。

| 落点 | 文件 | 验证 |
|---|---|---|
| 热换监视共享服务 | `CallCenter/CallCenter.Core/Router/RouterHotReloadService.cs`（FileSystemWatcher+防抖+字节快照指纹去重 → 建全新 router → `ToolRouterReference.Replace` 原子换快照） | `RouterHotReloadServiceTests` 6/6 |
| ToolRouterFactory 增「源→invoker 工厂」装配 | `CallCenter/Core/Router/ToolRouterFactory.cs` 增 `BuildCSharpRouterFromJsonWithFactory` | 92/92 无回归 |
| EF 同栈执行后端单一装配 | `CallCenter/CallCenter.Adapters/ExecutionFrameworkInvokerFactory.cs`（收敛 Host 私有 `BuildCSharpInvoker`） | — |
| Host `--watch` 收敛复用 | `CallCenter/CallCenter.Host/Program.cs`（删私有 watch 编排 + 本地 BuildCSharpInvoker） | `--list-tools`/`--call Add`→`3`/`cc_echo`→`"hi"` |
| Agent 组合根接线（步骤 1+2 合并） | `ConversationHosting.FrontToolProvider` 改共享 `ToolRouterReference`+`RouterHotReloadService`(EF 工厂)；`ConversationHosting.StopCallCenterHotReload`；`Agent.Launcher.csproj` 引 `CallCenter.Adapters` | Agent.Launcher 编译 0 错 |
| 监视生命周期上移宿主 | `AgentFramework.DisposeAsync` 调 `ConversationHosting.StopCallCenterHotReload`（消解 §六 障碍 1） | — |

进度汇总（本环境）：CallCenter.Tests **92/92**、Agent.McpRegistryHost.Tests **8/8**、Agent.Launcher 编译 0 错、Host CLI 冒烟通过。

## §六 步骤 1 动工前核实的架构障碍（决定落地方案，须先定案再改码）

1. **ConversationHosting 是 static 组合根、TryAttach 无生命周期句柄**：`ConversationHosting.cs` 为 `static class`，`TryAttach` 同步装配并返回 bool；折叠算子构建于 `TryBuildFoldOperator`（其 `_toolProvider` 前置 `FrontToolProvider` 一次性 `CSharpTrack`）。要把「监视 → 热换轨」做成常驻，需把监视/热换生命周期上移到**有明确 Dispose/取消令牌的宿主**（`AgentFramework`，持 `_frameworkCt` + `IAsyncDisposable`），而非塞进 static 方法体。
2. **ToolRouter 非线程安全 + 热换与折叠读取并发**：`ToolRouter.ReplaceTrack/RebuildIndex` 原地改共享字典；GUI 宿主折叠算子侧 `CallCenterFoldToolProvider.GetToolsAsync/CallAsync` 并发读 `router.Tools`。若在后台监视线程热换同一 router，会与折叠读取竞态（`ANTI-PARALLEL-RACE`）。Host CLI watch 单线程无碍，但 Agent GUI 是并发宿主 → **不能原位 ReplaceTrack**。
   → 拟定方案：热换以「构建全新 ToolRouter（含最新 CSharpTrack）→ 原子替换当前引用（volatile/Interlocked）」承载；`CallCenterFoldToolProvider` 由「构造注入固定 router」改为「每次取当前 router」——一次变更=换一个完整一致的路由快照，读取始终拿到一致版本，天然无原位改共享结构的竞态。
3. **折叠 LLM 调用保持端口收敛**：本步只热换 C# 轨，不动 `ILlmChatPort`（对齐收敛裁决，不把折叠硬迁 string seam）。

## §七 本会话完成边界与待 GUI 交接（handoff）

**已完成且本环境验证通过（可提交）**
- 步骤 3（P4-b-CSharp 编译腿）：`CallCenter.Adapters/Compilation/CsSourceCompiler.cs` + `Adapters.csproj` 引 RoslynMethod.Core + `CsSourceCompilerTests`(3)。CallCenter.Tests 81/81。
- 步骤 1（P4-a 机制层）：`CallCenter.Core/Router/ToolRouterReference.cs`(原子换 router 快照) + `ToolRouterReferenceTests`(3)；`ToolRouterFactory.cs`(清单→全新 router 单一入口) + `ToolRouterFactoryTests`(3)；`CallCenterFoldToolProvider` 改经 `ToolRouterReference.Current` 动态取当前（保留固定 router 便捷构造零回归）+ Agent 侧 `CallCenterFoldToolProviderTests`(2)。CallCenter.Tests 86/86、Agent.McpRegistryHost.Tests 8/8、Agent.Launcher 编译 0 错。
- **本轮追加（共享热换监视 + Agent 装配接线，A1–A4）**：见 §五.1。CallCenter.Tests 92/92、Agent.McpRegistryHost.Tests 8/8、Agent.Launcher 编译 0 错、Host CLI 冒烟（--list-tools / --call Add→3 / cc_echo→"hi"）通过。
- 规划文档：`docs/analysis/2026/9/9.5/2026-09-05-CallCenter收敛-后续实施步骤规划.md`。

**代码已落地、仅剩 GUI 折叠运行闸（B2）；其余步骤 4/5 待 GUI / 收尾**
1. **步骤 1+2（装配接线）运行闸**：宿主内改 function-libraries.json → 日志见 ConversationHosting_hotReload_csharpTrack 热换 → 折叠工具面工具数随清单变化；代码侧（共享 RouterHotReloadService + EF 工厂 + 生命周期收口）已落地，见 §五.1，运行验证依赖 GUI + 真实 LLM 折叠，留宿主会话。
2. **步骤 2（P2-Agent EF 同栈）运行闸**：折叠/骨架经 EF 同栈调 C# 函数命中（与 Host CLI --call 同栈行为）；代码侧 ExecutionFrameworkInvokerFactory 已落地并经 Host 冒烟。
3. **步骤 4（P3-桥内）**：GUI E2E 下 MCPBridge TS/PS 工具真调用（node_select/invoke_ps_script 在真实 Agent/FileDriven 上下文成功）；单测已以内存客户端锁定链路。
4. **步骤 5 归档**：源文档 §16.9 状态刷新并入。



