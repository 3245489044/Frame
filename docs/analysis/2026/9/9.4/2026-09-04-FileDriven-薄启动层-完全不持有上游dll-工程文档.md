# FileDriven 薄启动层：完全不持有上游 dll（Agent / CsharpPrj）

> **日期**：2026-09-04
> **定位**：`FileDriven`（宿主/启动层）构建与运行时优化。目标：让 FileDriven 的 `BuildOutput` **彻底不再持有**上游 Agent 与 CsharpPrj 的 dll（当前是"聚合工程引用 + 自投放复制"的厚目录），改为一层**薄启动层**，运行期从上游权威目录**原地解析** dll。
> **范围**：本工程文档只覆盖**设计 + 落点清单 + 验证标准**，不含本次改动。涉及：`FileDriven/`、`Projects/PrjBridge`、`Projects/Agent`、`CsharpPrj`、仓库根 `Directory.Build.*`。
> **与既有"方案B"（Agent 侧）的区别**：Agent 的"方案B"是"**二进制 HintPath 引用，不再把 CsharpPrj 拉进自身构建图**"，但 `Private=true` 仍把上游 dll **复制进自身输出目录自含运行**。FileDriven 本方案走得更彻底：**`Private=false` + 不复制 + 运行期 probe 上游权威目录**，输出目录对上游为中空——**完全不持有**。

***

## 〇、结论速览（TL;DR）

| 维度 | Agent 既有方案B（对照） | FileDriven 本方案（B-完全不持有） |
| --- | --- | --- |
| 上游编译 | 不拉 CsharpPrj 进构建图 | 同：不拉 CsharpPrj/Agent 进构建图 |
| 编译期可见类型 | 二进制 `<Reference HintPath>` | 同：二进制 `<Reference HintPath>` |
| 构建产物去向 | 复制进自身输出（自含运行） | **不复制**，dll 留在上游权威目录 |
| 运行期解析 | 默认 ALC 自 base 目录命中 | **启动 bootstrap 注册 resolve probe → 从权威目录原地解析** |
| 输出目录形态 | 厚（含 CsharpPrj dll 拷贝） | **薄（只留 FileDriven 自有 + 配置 + 少量 native/卫星资产）** |

- **核心机制**：① 构建侧把 FileDriven 的聚合 `ProjectReference` 换成二进制 `HintPath`（`Private=false`），关闭 PrjBridge 的 `DeploySelfToRuntime` 自投放；② 运行侧新增 `LauncherBootstrap`：在 `Program.Main` 第一行（触碰任何共享类型前）注册 `AssemblyLoadContext.Default.Resolving` 探针指向 `CsharpPrj\BuildOutput`，并显式 `ExtensionAssemblyHost.Initialize` 喂**绝对路径**的 Agent 清单（扩展层本就支持 rooted manifest）。
- **收益**：FileDriven `BuildOutput\Debug` 删除约数百个上游 dll；单一真源 = 上游各自权威目录；改上游只需重烘焙其本身，无需重灌 FileDriven。
- **验证**：构建后 FileDriven 输出无 `Agent.*`/`GameEditor.*`/`PrjBridge.dll`，且核心域 + 扩展域均正常装配运行。

***

## 一、现状：FileDriven 为什么持有上游 dll

`FileDriven\BuildOutput\Debug` 当前为"厚目录"，上游产物分两条路径流入：

### 1.1 CsharpPrj dll —— 聚合 ProjectReference 复制

[FileDriven\Directory.Build.props](file:///d:/Project/Program/WorkFlowPrj/FileDriven/Directory.Build.props#L61-L67) 以聚合 `ProjectReference` 引入五大 CsharpPrj 聚合工程（BasicMethod/Modules/Share/Operation/Plate）：

```xml
<ItemGroup Label="CsharpPrjAggregationRefs">
  <ProjectReference Include="$(CsharpProjectRoot)\BasicMethod\BasicMethod.csproj" />
  <ProjectReference Include="$(CsharpProjectRoot)\Modules\Modules.csproj" />
  <ProjectReference Include="$(CsharpProjectRoot)\Share\Share.csproj" />
  <ProjectReference Include="$(CsharpProjectRoot)\Operation\Operation.csproj" />
  <ProjectReference Include="$(CsharpProjectRoot)\Plate\Plate.csproj" />
</ItemGroup>
```

- 上游已按"方案B"烘焙到 `CsharpPrj\BuildOutput\Debug`（无 TFM 层），故 `ProjectReference` 二次构建为**增量 no-op**、不再重编译源码——但这并不阻止**复制**：`ProjectReference` 的 `CopyFilesToOutputDirectory` 阶段仍会把整闭包 CsharpPrj dll **灌进 FileDriven 输出目录**。注释自述其语义为"取产物后仅做运行依赖复制"（这也是"持有"的根源）。
- 这批 dll 进入 FileDriven 自己的 `deps.json`，运行期默认 ALC 从 app base 目录命中。

### 1.2 Agent / Engine dll —— PrjBridge 自投放复制

[PrjBridge.csproj](file:///d:/Project/Program/WorkFlowPrj/Projects/PrjBridge/PrjBridge.csproj#L38-L47) 声明 `SelfDeployEnabled=true`，触发仓库根 [Directory.Build.targets](file:///d:/Project/Program/WorkFlowPrj/Directory.Build.targets#L22-L43) 的 `DeploySelfToRuntime`：构建后把 PrjBridge 的**完整输出**（`Agent.*`/`Engine.*`/`PrjBridge.dll` + `PrjBridge.deps.json` + `runtimes\` native 资产）递归搬进 FileDriven 启动目录，并以 `PrjBridge.deps.json` 作为扩展层装载清单。

### 1.3 运行期：两套解析机制

1. **核心域（共享栈）**：FileDriven 自己的代码直接引用 CsharpPrj 类型，运行期由默认 ALC 按 `deps.json` 从 app base 解析——**必须在 FileDriven 输出目录里有 dll**。
2. **扩展域（Agent/Engine）**：经 [PrjBridgeWiringHook.cs](file:///d:/Project/Program/WorkFlowPrj/FileDriven/FileDriven.Execution/Lifecycle/PostInitHooks/PrjBridgeWiringHook.cs#L35) → `ExtensionAssemblyHost.Load("PrjBridge")`，由 [ExtensionAssemblyHost.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/Registry/ExtensionAssemblyHost.cs#L40-L51) 建专属 scoped ALC + `AssemblyDependencyResolver`，按 `PrjBridge.deps.json` 装载。当前清单靠"恰好躺在 `AppContext.BaseDirectory`"的默认回退命中。

***

## 二、目标与非目标

### 目标
- FileDriven `BuildOutput\Debug` **不再含** `Agent.*`/`GameEditor.*`/`PrjBridge.dll`（扩展域）与 CsharpPrj 核心 dll（核心域）。
- 编译期类型仍对 FileDriven 可见（二进制引用，不丢 GlobalUsings/强类型）。

### 非目标
- 不改上游各自权威目录结构（`CsharpPrj\BuildOutput\$Config` / `Agent\BuildOutput\$Config` 保持为单一真源）。
- 不做"聚合即不复制"的中间态（C 方案）；本方案直接走 B-完全不持有。
- 不追求删除 FileDriven 自身 dll、配置、模板、`runtimes\`（框架 native 资产属 .NET/WebView2 运行时必要件）。

***

## 三、原理：默认 ALC 为什么能"异地解析"

.NET 应用（FileDriven 是 apphost exe）的 `AssemblyLoadContext.Default` 解析顺序为：
1. `deps.json` 登记的 library（相对 app base / probing paths）；
2. **已注册的 resolve 处理器**：`AppDomain.CurrentDomain.AssemblyResolve` 或 `AssemblyLoadContext.Default.Resolving`（自定义回填）。

关键结论：**只要在触碰共享类型前注册一个 `Default.Resolving` 处理器，默认 ALC 就能把"deps.json 未落盘"的共享 dll 从外部权威目录兜底解析**。这是"完全不持有 CsharpPrj/Agent dll 仍能运行"的机制基础。

> 补充：`runtimeconfig` 的 `additionalProbingPaths` **不适合**本场景——它按 `{probe}\{rid}\{tfm}\{asm}` 的 NuGet 包层级布局探测，而上游权威目录是**扁平**摆放的 dll，探不到。故用显式 `Resolving` 处理器而非 probing path。

同时，扩展域的 scoped ALC 其 `ExtensionAssemblyHost.ResolveManifests` **已支持绝对/根化路径清单**（见 [ExtensionAssemblyHost.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/Registry/ExtensionAssemblyHost.cs#L54-L100)），`AssemblyDependencyResolver` 以**清单所在目录为基座**解析（见 [ExtensionLoadContext](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/Registry/ExtensionAssemblyHost.cs#L213-L273)）——因此**把清单指向 `Agent\BuildOutput\Debug\PrjBridge.deps.json`，扩展域 dll 就能在 Agent 自家权威目录原地加载**，无需灌进 FileDriven。

***

## 四、方案 B（FileDriven 版）设计

薄启动层 = **构建侧去依赖化 + 运行侧启动 probe**，两段协同。

### 4.1 构建侧：FileDriven 不再"持有"

1. **核心域：聚合 ProjectReference 加 `Private="false"`**（落地采用，非二进制枚举）
   - 在 [FileDriven\Directory.Build.props](file:///d:/Project/Program/WorkFlowPrj/FileDriven/Directory.Build.props#L65-L71) 的 `CsharpPrjAggregationRefs` 五条聚合引用上统一加 `Private="false"`。
   - `Private=false` → 编译期保留聚合全闭包类型可见，但**不再把 CsharpPrj dll 复制进 FileDriven 输出**（含其传递子树，MSBuild 剪枝 CopyLocal）；dll 留在权威目录。选择它而非逐程序集二进制枚举，是为了避免维护 60+ 条 `HintPath` 清单、降低漏引/双路径风险，运行效果与本方案目标一致。**不设** `RequireCsharpPrjBaked`（FileDriven 仍经此聚合 ProjectReference 构建 CsharpPrj，产物自然烘焙进其权威目录；且 `CsharpBuildOutputDir` 仅定义于 CsharpPrj props，FileDriven 上下文不可见，误设会触发 `EnsureCsharpBaked` 空路径误判）。
2. **扩展域：关闭 PrjBridge + Agent 自投放**
   - [PrjBridge.csproj](file:///d:/Project/Program/WorkFlowPrj/Projects/PrjBridge/PrjBridge.csproj#L38-L45) 置 `SelfDeployEnabled=false`；[Agent\Directory.Build.props](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/Directory.Build.props#L104-L114) 同步置 `false`（Agent 另有一处 `SelfDeployEnabled` 亦会向 FileDriven 灌 Agent.*，`PrjBridge`+`Agent` 两处都取消）。取消后 FileDriven 输出不再出现 `Agent.*`/`Engine.*`/`PrjBridge.dll`。
3. **保留**：FileDriven 自身 dll、`Configs/`、`TempletConfig/`、`samples/`、`runtimes\`（框架 native 必需）及各 `*.runtimeconfig.json`。

### 4.2 运行侧：`LauncherBootstrap`（ModuleInitializer 探针 + 扩展清单锚定）

新增 `FileDriven.Launcher\Bootstrap\LauncherBootstrap.cs`：
- `[ModuleInitializer] InitModule()`：在**模块装载期**（早于 `Main` 的 JIT 触碰任何共享类型）注册 `AssemblyLoadContext.Default.Resolving`，把缺失的 CsharpPrj 共享 dll 从权威目录原位 `LoadFromAssemblyPath` 兜底；仅依赖 System，不触碰共享类型。
- `EnableExtensions()`：在 `Program.Main` 第一行、`new FileDrivenExecutor()` 之前，显式 `ExtensionAssemblyHost.Initialize(<PrjBridge bin 的 PrjBridge.deps.json 绝对路径>)`，替代"文件躺在 base 目录"的默认回退；底层 `AssemblyDependencyResolver` 以清单目录为基座就地加载 Agent/Engine。

```
LauncherBootstrap.EnableExtensions();   // Program.Main 第一行
```

职责二段分离是刻意的：**探针必须在任何共享类型解析前就位**（放 ModuleInitializer 而非 Main 体，规避 Main JIT 提前加载 `FileDrivenExecutor`/其基类链）。

### 4.3 权威目录定位（单一真源）

- 启动期不硬编码：从仓库根环回导出（`LauncherBootstrap.FindRepoRoot` 自 `AppContext.BaseDirectory` 向上找一个同时含 `CsharpPrj` 目录与 `Directory.Build.props` 的目录为仓库根），再按：
  - 核心域 `CsharpPrj\BuildOutput\$(Config)`（无 TFM 层，与 `CsharpBuildOutputDir` 一致）；
  - 扩展域 `Projects\PrjBridge\bin\$(Config)\net10.0-windows\PrjBridge.deps.json`（清单基座即扩展集目录）。
- `$(Config)` 由 `AppContext.BaseDirectory` 推断（含 `\Release\` → Release，否则 Debug）。
- 也可改用 `ExtensionLoaderConfig.json` / 环境变量注入，机制与 [ExtensionAssemblyHost.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/Registry/ExtensionAssemblyHost.cs#L33-L39) 的清单来源台阶兼容。

### 4.4 边界与例外（哪些仍须保留在 FileDriven 输出）

- **native / runtimes**：WebView2、SQLite 等 unmanaged 资产随扩展/框架 deps 走 `LoadUnmanagedDll`（[ExtensionLoadContext](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/Registry/ExtensionAssemblyHost.cs#L262-L272) 经 resolver 解析 `runtimes\`）——若 Agent 权威目录已含同一 `runtimes\`，则 FileDriven 无需重复持有；否则保留最小 `runtimes\` 副本。
- **satellite 资源包**（`cs/`、`de/` …）：随 dll 就近兜底，探不到再从 FileDriven 输出回退。
- **Configs / TempletConfig / samples / 各 json**：属 FileDriven 自身配置，必须保留。
- **子进程工具 exe（已知边界）**：`ProcessJsonConfig.exe`（CsharpPrj 的 Exe 工具，被 BasicMethod 聚合引用）作为**可部署可执行**被拉进输出（其托管 `ProcessJsonConfig.dll` 已被本剪枝剔除，仅留下 apphost `.exe` + deps/runtimeconfig 供工作流以子进程方式拉起）。此为"可执行工具被运行"而非"库被解析"，若要彻底归零需把工作流启动路径改为 CsharpPrj 权威目录，待单独评估。

***

## 五、文件清单

### 修改
| 文件 | 动作 |
| --- | --- |
| [FileDriven\Directory.Build.props](file:///d:/Project/Program/WorkFlowPrj/FileDriven/Directory.Build.props#L65-L71) | `CsharpPrjAggregationRefs` 五条聚合引用加 `Private="false"`（不设 `RequireCsharpPrjBaked`） |
| [FileDriven\Directory.Build.targets](file:///d:/Project/Program/WorkFlowPrj/FileDriven/Directory.Build.targets) | 新增：`PruneUpstreamCopyLocal` 剪枝 target —— 剔除 `ReferenceCopyLocalPaths` 中源路径位于 `CsharpPrj\BuildOutput` **或** `Projects\` 的 dll（含传递叶与全部自研产品），保住编译闭包/NuGet 流转又不复制 |
| [Projects\PrjBridge\PrjBridge.csproj](file:///d:/Project/Program/WorkFlowPrj/Projects/PrjBridge/PrjBridge.csproj#L38-L45) | `SelfDeployEnabled=false`（关闭向 FileDriven 自投放） |
| [Projects\Agent\Directory.Build.props](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/Directory.Build.props#L104-L114) | `SelfDeployEnabled=false`（关闭 Agent 向 FileDriven 自投放） |
| [Projects\Engine\Directory.Build.props](file:///d:/Project/Program/WorkFlowPrj/Projects/Engine/Directory.Build.props#L76-L84) | `SelfDeployEnabled=false`（关闭 Engine 向 FileDriven 自投放） |
| [Projects\Produce\Directory.Build.props](file:///d:/Project/Program/WorkFlowPrj/Projects/Produce/Directory.Build.props#L100-L108) | `SelfDeployEnabled=false`（关闭 Produce 向 FileDriven 自投放） |
| [FileDriven.Launcher\Program.cs](file:///d:/Project/Program/WorkFlowPrj/FileDriven/FileDriven.Launcher/Program.cs) | 第一行调用 `LauncherBootstrap.EnableExtensions()`，再 `new FileDrivenExecutor()` |

### 新增
| 文件 | 职责 |
| --- | --- |
| `FileDriven.Launcher\Bootstrap\LauncherBootstrap.cs` | `[ModuleInitializer]` 注册核心域 `Default.Resolving` 探针 + `EnableExtensions` 锚定扩展域清单至 PrjBridge 权威目录；仓库根/权威目录环回推导 |

> 注：`ExtensionAssemblyHost.Initialize` 现有**幂等守卫**（`_initialized`），首次成功即固化；`PrjBridgeWiringHook`/`FunctionLibraryLoader` 消费路径不变（[FunctionLibraryLoader.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/Registry/FunctionLibraryLoader.cs#L110-L123) 两段式解析：默认 ALC → 扩展 ALC）。

***

## 六、验证标准

1. **构建后目录**：`FileDriven\BuildOutput\Debug` 无 `Agent.*`/`GameEditor.*`/`PrjBridge.dll`；无 CsharpPrj 核心 dll（仅本地化回退与 `runtimes\` 例外）。
2. **核心域编译**：FileDriven 各工程以二进制引用编译通过（GlobalUsings 强类型齐全，无 CS0246/CS0121）。
3. **核心域运行**：`MainForm_Boot_from_config` 时序回调正常；`LauncherBootstrap` 日志显示核心域 probe **命中数 > 0**（证明异地解析生效，非误走默认）。
4. **扩展域运行**：`PrjBridge_wiring_success`（`ports_wired`）、Agent Readiness = Available、四核心对账 `ThrowIfCoreIncomplete` 通过；`AgentUi_ConversationListHtml` 正常。
5. **回归**：CLI 跑一份既有脚本（如 `demo_commands.txt`）全绿；`ValidateDeployment()`（ExtensionAssemblyHost）报告无缺失 dll。

***

## 七、风险与回退

| 风险 | 说明 | 缓解 |
| --- | --- | --- |
| probe 时序 | 若共享类型在注册探针前加载 → `FileNotFoundException` | 探针必须为 `Program.Main` 第一语句，且 `LauncherBootstrap` 不引用共享类型 |
| 双副本身份分裂 | 核心域与扩展域若各自持一份共享 dll → 类型身份分裂 | `ExtensionLoadContext.Load` 已实现"默认 ALC 可解析即复用"守卫（[ExtensionAssemblyHost.cs](file:///d:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/Registry/ExtensionAssemblyHost.cs#L231-L259)），保持不变 |
| 单一真源失效 | 上游权威目录改动后 FileDriven 不同步 | 关闭自投放后 FileDriven 天然失效，必须重上游；配合 `EnsureCsharpBaked` 兜底重建 |
| native/卫星缺口 | `runtimes\` 重复或缺失 | 见 §4.4，用 `ValidateDeployment` 诊断 |

**回退**：恢复 `CsharpPrjAggregationRefs` + `SelfDeployEnabled=true` 两处即可回到原位，改动集中、可逆。

## 八、与既有文档的关系

- 本方案是 Agent"方案B"在 FileDriven 侧的**完全去持有**延伸：Agent = 持有（自含），FileDriven = 完全不持有（薄启动层）。
- 不改动 `CsharpPrj`/`Agent` 的权威烘焙目录与 `ExtensionAssemblyHost` 扩展机制，仅是"清单根化 + 启动 probe"两处接入。