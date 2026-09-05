# Agent「方案 B」体系维护脚本架构设计

> 状态：设计评审稿（暂不落地脚本）
> 范围：`Projects/Agent/Directory.Build.props` 所锚定的「方案 B（CsharpPrj 二进制集中引用）+ 权威烘焙 + 运行时部署」体系的 **PowerShell 维护脚本**架构。
> 约束：脚本**只做"运维视图"**——显式化/可调/可查/可恢复既有 MSBuild 规则，不改动 props/targets/CS 行为，避免双写冲突。

---

## 1. 背景：机制事实与维护痛点

下表整理自 `Directory.Build.props`（仓库根）、`CsharpPrj/Directory.Build.props`、`Projects/Agent/Directory.Build.props`、`Projects/Agent/Directory.Build.targets`、`Projects/PrjBridge/PrjBridge.csproj`。

| 机制 | 承载点 | 维护痛点 |
|---|---|---|
| 方案 B（Agent 不把 CsharpPrj 拉进自身构建图） | `Agent/Directory.Build.props` → `CsharpPrjBinaryRefs`（**唯一集中引用清单**，`<Reference HintPath="$(CsharpBuildOutputDir)\X.dll">`） | 新增/改名 CsharpPrj 程序集必须手工同步该清单，漏同步 → Agent 侧类型不可见（CS0246/CS0234） |
| CsharpPrj 权威烘焙目录（单一真源，无 TFM 层） | `$(CsharpBuildOutputDir)` = `CsharpPrj\BuildOutput\$(Configuration)\` | ① 历史曾并存"扁平层"与 `Debug\` 层 → 产物目录分裂 ② `$(Configuration)` 在 props 求值期未就位会把目录**固化**成无配置层的扁平 `BuildOutput\`（sln/msbuild 直跑必现）③ MSBuild 增量 up-to-date 按 **obj 中间产物**判断，权威层 dll 可能缺失而 build 显示"最新" → 需强制全量重出 |
| 权威层兜底烘焙 | `Agent/Directory.Build.targets` → `EnsureCsharpBaked`（探针 3 件 dll，缺则 build `MainProj.sln`） | 探针集远小于 37 项清单 → 常漏烘；烘焙与 Agent 并行写同一权威目录曾致 CS0006 竞态 |
| 双构建竞态 | 冗余把 CsharpPrj 项目塞进 `Agent.sln` 顶层 | 需维护"Agent.sln 不含 CsharpPrj 顶层项目"的不变式（已移除 AiScheduling/Share.Conversation） |
| Agent 轻量部署 | `DeployAgentToFileDrivenRuntime`（刷 `Agent\BuildOutput\Debug → FileDriven\BuildOutput\Debug`） | 只刷 Agent 闭包；不含 Engine / PrjBridge / 装配清单 |
| PrjBridge 完整部署 | `PrjBridge.csproj` → `DeployBridgeToFileDrivenOutput`（整个 `$(TargetDir)` + `PrjBridge.deps.json` 扩展闭包清单） | 运行时扩展层（`Agent.*` / `GameEditor.*`）靠它；`PrjBridge.deps.json` 缺失 → 扩展层全部"无法加载类型" |
| 运行时扩展装载 | `Modules.Registry.ExtensionAssemblyHost`（`AppContext.BaseDirectory\PrjBridge.deps.json` 建 scoped ALC） | 进程内幂等一次；部署后必须**重启 FileDriven** 才生效 |

**设计目标**：把这些分散在 MSBuild props/targets/CS 中的规则显式化为一套 ps1 —— 校验（只读）、执行（烘焙/构建/部署）、清理（残留）、诊断（部署态），并全部纳入仓库既有 `PowerShellScript` 框架。

---

## 2. 架构定位：纳入既有框架

仓库既有约定（本设计全部遵循）：

- 目录：`PowerShellScript/Modules/<域>/{函数.ps1|.psm1}`；测试镜像 `CentralTest/PowerShellTests/<域>/<函数>.Tests.ps1`（Pester）。
- 命名：`动词-名词`；无硬编码路径；路径真源来自 props（`Modules/Core/ReadProjectConfig.ps1` 的 `Get-ProjectRoot`/`Get-FileDrivenDir`/`Import-PropsProperties`）。
- 加载：`Modules/Import-Modules.ps1` 的 IoC（脚本头 `@requires` 按需加载 / 无声明全量回退）。
- 入口：`PowerShellScript/ScriptLauncher.ps1` + `launcher.config.json`（分类 / alias / 描述 / 透传 `-Tool` 后参数）。

新增两个域，其余复用既有 `Core` / `Csproj` / `Tools(Cleanup)`：

```
PowerShellScript/
  Modules/
    Bake/    Invoke-CsharpPrjBake.ps1  Test-BakeOutput.ps1
             Sync-BinaryRefManifest.ps1  Clear-ResidualBuildLayers.ps1
    Deploy/  Invoke-AgentOpsFlow.ps1  Deploy-AgentClosure.ps1
             Deploy-PrjBridge.ps1  Test-RuntimeDeployment.ps1
  launcher.config.json          # 新增 Bake/Deploy 分类与 alias
CentralTest/PowerShellTests/
  Bake/    # 对应 *.Tests.ps1 镜像
  Deploy/
```

---

## 3. 单一真源原则（脚本不重复配置）

脚本**只做"验证 & 执行"**，一切路径/清单从 props 读取，不维护第二份配置：

- **路径**：`CsharpProjectRoot` / `AgentProjectRoot` / `EngineProjectRoot` / `FileDrivenProjectRoot` / `CsharpBuildOutputDir` → 从根 `Directory.Build.props` 解析。复用 `ReadProjectConfig.ps1` 的 `Import-PropsProperties`（正则抽取 `<Key>Value</Key>`），并新增便捷函数：
  - `Get-CsharpProjectRoot` / `Get-AgentProjectRoot` / `Get-EngineProjectRoot`
  - `Get-CsharpBuildOutputDir [-Configuration]`（读根 props 的 `CsharpBuildOutputDir` 公式并展开）
  - `Get-FileDrivenRuntimeDir [-Configuration]` = `FileDrivenProjectRoot\BuildOutput\$(Configuration)\`
- **引用清单**：`CsharpPrjBinaryRefs`（`Agent/Directory.Build.props`）为**唯一权威**；脚本求差集时**只读清单、不复制清单**。
- **运行目录**：一律经 `Get-FileDrivenRuntimeDir`，不散落硬编码绝对路径。

> 护栏：任何"会改写 props/csproj/sln"的操作（如清单 `-Fix`）默认关闭、写前 `.bak`、可回滚——对齐 `Modules/Csproj/Convert-CsharpPrjToHintPath.ps1` 既有安全策略。

---

## 4. 脚本清单（职责 / 命令 / 参数 / 调用链）

> 命令路径以 `PowerShellScript\Modules\<域>\` 为基准；所有脚本接受 `-Configuration`（默认 Debug）。

### 4.1 Bake 域（CsharpPrj 权威烘焙）

**`Invoke-CsharpPrjBake.ps1`** — 权威烘焙（替代"手工逐个聚合 build"）
```powershell
Invoke-CsharpPrjBake.ps1 [-Configuration Debug] [-Full] `
    [-Targets All|BasicMethod|Share|Modules|Operation|Plate|Strategy] `
    [-SkipRuntimeKill]
```
职责：把 CsharpPrj 烘焙到 `$(CsharpBuildOutputDir)`。
- 默认增量（快）；`-Full` 对目标聚合 csproj 逐个传 `--no-incremental` 强制全量重出到权威层（修"obj 最新、权威层缺 dll"的锁死，即本次根因）。
- `-Targets` 选子集（沿用"分聚合规避单条命令超时"的既有实操）。
- 前置门：`Test-BakeOutput` 报告当前缺口；后置调用 `Test-BakeOutput` 复检。
- 调 `dotnet build <聚合.csproj> -c <cfg> [-m] [--no-incremental]`，复用 `BuildHelpers` 退出码处理。

**`Test-BakeOutput.ps1`** — 权威层自检（封装探针 + Configuration 空洞 + 层分裂检测）
```powershell
Test-BakeOutput.ps1 [-Configuration Debug] [-Strict]
```
输出：状态对象 `{ missing:[...], residualLayers:[...], configHole:bool, ok:bool }`
职责：
1. 读 `CsharpPrjBinaryRefs` 清单（Agent props），对照权威层 dll 求差集 → `missing`（本会话曾 38 项缺到 0 的复现护栏）。
2. Configuration 空洞回归检测：`dotnet msbuild <任一Agent csproj> -getProperty:CsharpBuildOutputDir`（**不带 `-c`**）须展开到 `$(Configuration)` 层（即含 `Debug\`），否则 `configHole=true`（针对本会话修复的 props 求值期 Configuration 空洞）。
3. 残留层识别：`CsharpPrj\BuildOutput\` 根扁平 dll、`net10.0-windows\` 层 → `residualLayers`（白名单可清理）。
4. 复用 `EnsureCsharpBaked` 的 3 件套探针作快速门。
调用链：`Invoke-CsharpPrjBake` / `Invoke-AgentOpsFlow` 的前置门；`Sync-BinaryRefManifest` 的补充校验。

**`Sync-BinaryRefManifest.ps1`** — 引用清单 ↔ 实际 dll 一致性（唯一集中维护点辅助）
```powershell
Sync-BinaryRefManifest.ps1 [-DryRun] [-Products Agent,Engine,Produce]
```
职责：
1. 扫描 CsharpPrj 全部 csproj 的 `<AssemblyName>`（真实产出集合）。
2. 读各产品 props 的 `CsharpPrjBinaryRefs` `Include` 集合。
3. 报告三类漂移：清单有/权威层缺（缺产物）；权威层有/清单无（未登记）；AssemblyName 改名。
4. `-DryRun` 仅报告；`-Fix` 白名单补齐默认关闭（写前 `.bak`）。
设计理由：把"CsharpPrj 新增/改名程序集须同步各产品 props 清单"的手工义务，收敛为一条可校验命令。

**`Clear-ResidualBuildLayers.ps1`** — 清理历史残留输出层（可逆）
```powershell
Clear-ResidualBuildLayers.ps1 [-DryRun] [-Layers Flat,Net10] [-WhatIf]
```
职责：列出/清除 `CsharpPrj\BuildOutput` 根扁平 dll、`net10.0-windows` 层等旧约定残留；跳过 `Debug` 权威层与被占用文件（沿用 `CoreClean` 防锁思路）。默认 `-DryRun`。

### 4.2 Deploy 域（FileDriven 运行目录部署）

**`Invoke-AgentOpsFlow.ps1`** — 顶层编排（一个入口覆盖 main 生命周期）
```powershell
Invoke-AgentOpsFlow.ps1 -Mode clean|bake|build|deploy|all `
    [-Configuration Debug] [-Full] [-SkipRuntimeKill]
```
| Mode | 流水线 |
|---|---|
| `clean` | 释放占用进程（调既有 `Clean-Processes`）→ 清残留层（`Clear-ResidualBuildLayers`） |
| `bake` | `Test-BakeOutput`（门）→ `Invoke-CsharpPrjBake [-Full]` → `Test-BakeOutput`（复检） |
| `build` | `Test-BakeOutput`（门）→ `dotnet build Agent.sln`（可 `-Full`） |
| `deploy` | `Deploy-AgentClosure`（快）或 `Deploy-PrjBridge`（全，含装配清单） |
| `all` | `clean → bake(-Full) → build → deploy(full) → Test-RuntimeDeployment` |

**`Deploy-AgentClosure.ps1`** — Agent 轻量部署（封装 `DeployAgentToFileDrivenRuntime`，CLI 化）
```powershell
Deploy-AgentClosure.ps1 [-Configuration Debug]
```
职责：`Agent\BuildOutput\Debug` 闭包 → `FileDriven` 运行目录。占用/锁定文件降级为 `warn`（跳过、标记），不因锁中断（吸取 MSB3026 教训）。

**`Deploy-PrjBridge.ps1`** — 完整部署（封装 `DeployBridgeToFileDrivenOutput` + Engine 依赖门）
```powershell
Deploy-PrjBridge.ps1 [-Configuration Debug] [-SkipEngineCheck]
```
职责：
1. 门：确认 `GameEditor.Host.dll` 等 Engine 产物存在（PrjBridge 聚合 Engine；缺则 `PrjBridge.deps.json` 闭包不全 → 运行时 Engine 扩展仍不可解析）。
2. `dotnet build Projects\PrjBridge\PrjBridge.csproj` → 触发 `DeployBridgeToFileDrivenOutput`（整闭包 + 重生成 `PrjBridge.deps.json`）。
3. 拷贝遇"被占用"时改**只补缺失项**（`PrjBridge.deps.json`/Engine/runtimes）并 `warn`，返回 `requiresRestart` 标志。

**`Test-RuntimeDeployment.ps1`** — 部署后运行态校验
```powershell
Test-RuntimeDeployment.ps1 [-Configuration Debug]
```
输出：状态对象 `{ ready, depsOk, resolvable, requiresRestart, missing:[...] }`
职责：
1. 运行目录具备 `PrjBridge.deps.json` / `PrjBridge.dll` / Engine / runtimes native。
2. deps 清单覆盖目标扩展（`Agent.Launcher/Core/Guardrails/McpRegistryHost`、`GameEditor.Host` 等）且磁盘 dll 存在。
3. 用 `AssemblyDependencyResolver` 试解析任一 Agent 库类型（复现 `ExtensionAssemblyHost.ResolveType` 语义）。
4. 探测 FileDriven 进程是否在跑 → `requiresRestart=true`（`ExtensionAssemblyHost` 进程内幂等一次，需重启才生效）。

---

## 5. 与既有模块的复用关系

| 既有模块 | 复用点 |
|---|---|
| `Core/ReadProjectConfig.ps1` | 项目根检测、`Get-FileDrivenDir`/`Import-PropsProperties`；新增 `Get-*Root`/`Get-CsharpBuildOutputDir`/`Get-FileDrivenRuntimeDir` |
| `Modules/Csproj/Convert-CsharpPrjToHintPath.ps1` | 迁移期 ProjectReference→HintPath 转换器；与本套"清单同步 + 烘焙 + 部署"互补（它负责改写引用，本套负责产物生命周期） |
| `Tools/Cleanup` 既有 `Clean-Processes` | Bake/Deploy 前置释放编译输出占用 |
| `BuildHelpers.psm1` / `OutputHelpers.psm1` | `dotnet build` 包装、退出码、分色输出 |
| `Import-Modules.ps1` | 脚本头 `@requires` 按需加载 |

---

## 6. 错误处理与锁策略（吸取本会话教训）

- **权威层/运行目录同文件并发写读竞态**（CS0006/CS0234/CS0006 竞态、MSB3026 锁定）：所有拷贝统一策略 —— 仅补缺失、`SkipUnchanged`、占用则降级为 `warn` + 标记，**不把锁当 fatal**、不删占用文件。
- **Configuration 空洞回归护栏**：`Test-BakeOutput` 每次执行都校验"无 `-c` 时 `CsharpBuildOutputDir` 仍展开到配置层"（本会话修复的根因防复发）。
- **破坏性默认关**：清残留/写清单默认 `-DryRun`/`-WhatIf`；写 props/csproj/sln 前 `.bak`，可回滚。
- **需重启项不代杀**：部署后若 FileDriven 在跑，脚本不杀进程，仅返回 `requiresRestart` 并提示。
- **单命令超时**：保留 `-Targets` 分聚合、脚本内 `-m` 并行、必要时顺序降级，规避长 bake 超时。

---

## 7. Pester 测试规划（镜像 `CentralTest/PowerShellTests/<域>/`）

| 被测 | 用例 |
|---|---|
| `Test-BakeOutput` | 权威层缺 dll 样本 → `missing` 正确；Configuration 空洞（无 `-c` 展开）；扁平残留识别 |
| `Sync-BinaryRefManifest` | 清单-磁盘差集样本；AssemblyName 改名漂移 |
| `Deploy-PrjBridge` | 运行目录缺清单→补齐；占用→降级 `warn`；Engine 门 |
| `Test-RuntimeDeployment` | deps 含目标程序集且磁盘 dll 在 → `ready`；缺 → 不 ready |
| `Clear-ResidualBuildLayers` | 白名单清理；`-DryRun` 不改文件 |

---

## 8. 接入 ScriptLauncher（`launcher.config.json`）

新增分类「Agent 方案B 运维」，注册工具（`alias` + `path` + `description`，`-Tool` 后参数透传目标脚本）：

| alias | 脚本 | 说明 |
|---|---|---|
| `agent-ops` | `Modules/Deploy/Invoke-AgentOpsFlow.ps1` | 顶层编排（`-Mode clean/bake/build/deploy/all`…） |
| `bake-check` | `Modules/Bake/Test-BakeOutput.ps1` | 权威层自检（缺 dll / 配置空洞 / 残留） |
| `bake` | `Modules/Bake/Invoke-CsharpPrjBake.ps1` | CsharpPrj 权威烘焙 |
| `ref-sync` | `Modules/Bake/Sync-BinaryRefManifest.ps1` | 引用清单 ↔ dll 一致性 |
| `clean-layers` | `Modules/Bake/Clear-ResidualBuildLayers.ps1` | 清理历史残留输出层 |
| `bridge-deploy` | `Modules/Deploy/Deploy-PrjBridge.ps1` | PrjBridge 完整部署（含装配清单重建） |
| `run-check` | `Modules/Deploy/Test-RuntimeDeployment.ps1` | 运行目录部署态校验 |

---

## 9. 实施顺序（每阶段独立可验收）

| 阶段 | 内容 | 风险 |
|---|---|---|
| 1 · 只读/校验 | `Test-BakeOutput`、`Sync-BinaryRefManifest`、`Clear-ResidualBuildLayers`（+ Pester 镜像） | 低（纯读/自检/白名单清理） |
| 2 · 执行封装 | `Invoke-CsharpPrjBake`、`Deploy-AgentClosure`、`Test-RuntimeDeployment` | 中（执行、但均有门与 DryRun） |
| 3 · 编排 + 部署 | `Deploy-PrjBridge`、`Invoke-AgentOpsFlow`、`launcher.config.json` 接入 | 中高（触发完整链路，配合锁策略与重启提示） |

建议每阶段交付后跑 `bake-check`/对应 Pester 验收，再进下一阶段。

---

## 10. 风险与护栏

- **不改 MSBuild 语义**：脚本是运维视图；`EnsureCsharpBaked` / `DeployAgentToFileDrivenRuntime` / `DeployBridgeToFileDrivenOutput` 等 targets 维持现状。若未来要重构这些 targets（如把探针扩为全清单、合并 Deploy 目标），作为独立议题，本架构为其提供 CLI 化前置校验与回归测试。
- **双写冲突**：脚本不与 targets 抢写；同一生命周期（烘焙/部署）二选一入口（脚本或 targets），由 `Invoke-AgentOpsFlow` 统一编排，避免并行写权威层。
- **写操作护栏**：`-DryRun`/`-WhatIf`/`.bak`/白名单 `-Fix` 默认关。
- **进程/锁**：占用的目标文件只降级不删；`Clean-Processes` 由用户显式触发（`-SkipRuntimeKill` 可跳）。
- **可复用于其它产品**：`Sync-BinaryRefManifest`/`Test-BakeOutput` 天然多产品（`-Products Agent,Engine,Produce`），本架构以"配置根 props"为唯一输入，Agent 是首个落地者。
