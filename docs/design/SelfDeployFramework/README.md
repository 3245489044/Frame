# 自投放部署通用化框架设计

> 状态:设计评审稿(暂不改代码)
> 范围:把分散在 Agent / PrjBridge / Produce 等产品的「构建后自投放部署 + 运行时扩展装载 + 权威烘焙兜底」收敛为一套**全局属性驱动的通用框架**,消除跨产品重复,统一"部署到运行目录"的唯一入口。
> 约束:只做"规则显式化与收敛",不改既有单产品的行为语义;一切路径/清单从仓库根 props 单一真源读取,不维护第二份配置。

***

## 1. 背景:当前部署机制的事实与痛点

### 1.1 机制分布现状

| 机制               | 承载点                                                                                   | 现状                                                                                                                  |
| ---------------- | ------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| 仓库根单一真源属性        | `Directory.Build.props`(仓库根)                                                          | `RepoRoot` / `AgentProjectRoot` / `EngineProjectRoot` / `FileDrivenProjectRoot` / `CsharpBuildOutputDir` 均已定义,供全库引用 |
| CsharpPrj 权威烘焙兜底 | `Projects/Agent/Directory.Build.targets` & `Projects/Produce/Directory.Build.targets` | 同名 `EnsureCsharpBaked` target **逐字重复**,探针 3 件 dll,缺则构建 `CsharpPrj/MainProj.sln`                                     |
| Agent 轻量部署       | Agent 侧 `DeployAgentToFileDrivenRuntime`                                              | 只刷 Agent 闭包(不含 Engine / PrjBridge / 装配清单)                                                                           |
| PrjBridge 完整部署   | `Projects/PrjBridge/PrjBridge.csproj` → `DeployBridgeToFileDrivenOutput`              | 构建后把整个 `$(TargetDir)` 递归 Copy 到 `FileDriven\BuildOutput\$(Configuration)\`,并携带 `PrjBridge.deps.json` 扩展闭包清单         |
| 运行时扩展装载          | `Modules.Registry.ExtensionAssemblyHost`                                              | 用 `AppContext.BaseDirectory\PrjBridge.deps.json` 建 scoped ALC,进程内幂等一次                                               |
| 运维脚本层            | `docs/design/AgentBakeScriptArch/README.md`                                           | `Sync-BinaryRefManifest` / `Test-BakeOutput` 已设计为 `-Products Agent,Engine,Produce` 多产品                              |

### 1.2 痛点

1. **跨产品重复**:`EnsureCsharpBaked` 在 Agent / Produce 两个 targets 逐字重复;任何一处改动须同步另一处,易漏。
2. **部署入口分裂**:Agent 轻量部署与 PrjBridge 完整部署是两套独立 target,各自硬编码目标路径与清单策略,第三/第四产品(Produce 等)接入时又要新增一套。
3. **目标路径硬编码**:`DeployBridgeToFileDrivenOutput` 把目标写死在 `$(FileDrivenProjectRoot)\BuildOutput\$(Configuration)\`,不可被其它产品复用或覆盖。
4. **运行时装载器单一绑定**:`ExtensionAssemblyHost` 与 `PrjBridge.deps.json` 强绑定,未来多产品各自闭包清单时缺乏统一协议。
5. **运维脚本覆盖不足**:`Deploy-*` 侧尚未像 `Test-BakeOutput` 那样多产品化。

### 1.3 通用化机会

- 三处重复(`EnsureCsharpBaked` ×2 + 部署 target ×2)可收敛为**一个全局 target + 一组属性**。

- 现有机制内部已是"拷贝清单 == 装载清单"(deps.json 即清单),此设计可自然泛化为通用协议。

***

## 2. 目标与非目标

### 2.1 目标(做什么)

- **G1 构建期统一**:把"构建后自投放整个 TargetDir 到指定运行目录"抽成根级全局 Target(`DeploySelfToRuntime`),由属性驱动、按产品 opt-in。

- **G2 兜底烘焙上收**:把重复的 `EnsureCsharpBaked` 收为全局一份,探针可配置,消除 Agent/Produce 双写。

- **G3 装载协议统一(可选分层)**:把 `ExtensionAssemblyHost` 从"PrjBridge.deps.json 专用"放宽为"任意装配清单"(不强制本期落地,见 §4)。

- **G4 可见性**:任一产品构建后可被 `Test-RuntimeDeployment` 同一套校验,运维脚本 `Deploy-*` 多产品化。

- **G5 单一真源**:所有路径、清单、探针从根 props 解析,产品项目只声明"我是谁 + 部署到哪 + 要不要清单"。

### 2.2 非目标(不做什么)

- 不改既有单产品运行行为(Agent/PrjBridge/Produce 各自 `AfterTargets="Build"` 的触发时机保留)。

- 不合并"轻量部署"与"完整部署"的语义差异——保留"是否携带 deps 清单"这一开关,只是把决策收敛到一个 target 的参数。

- 不把 CsharpPrj 拉进任何产品构建图(方案 B 红线保留)。

- 不新增运行时 ALC 装载的强制迁移(§4 为可选演进)。

***

## 3. 通用化方案设计

### 3.1 统一部署属性契约

新增一组全局约定属性,由需要自投放的产品在**自身 csproj**(或产品级 `Directory.Build.props`)声明,根级 `Directory.Build.targets` 消费:

```xml
<PropertyGroup>
  <!-- 开关:开启构建后自投放 -->
  <SelfDeployEnabled>true</SelfDeployEnabled>

  <!-- 目标运行目录:默认 FileDriven 运行目录,允许产品覆盖 -->
  <SelfDeployTargetDir>$(FileDrivenProjectRoot)\BuildOutput\$(Configuration)\</SelfDeployTargetDir>

  <!-- 是否携带 deps.json 扩展闭包清单(完整部署=PrjBridge 语义;false=轻量部署=Agent 语义)-->
  <SelfDeployGenerateDeps>false</SelfDeployGenerateDeps>

  <!-- 装配清单文件名:GenerateDeps=true 时使用的清单(默认当前项目名) -->
  <SelfDeployDepsJson>$(SelfDeployDepsJson)</SelfDeployDepsJson>

  <!-- 覆盖开关:留空则用 SelfDeployTargetDir,便于个别产品临时改投 -->
  <SelfDeployLauncherOutputDir></SelfDeployLauncherOutputDir>
</PropertyGroup>
```

- **路径真源**:`SelfDeployTargetDir` 的默认值引用根 props 已定义的 `FileDrivenProjectRoot`(§1.1),不新增硬编码。

- **最小声明**:一个产品接入只需 `SelfDeployEnabled=true`(用默认路径)或再加 `SelfDeployGenerateDeps=true`(完整清单)。

### 3.2 全局 Target `DeploySelfToRuntime`

位置:**仓库根** **`Directory.Build.targets`**(当前仓库根无 targets 文件,需新建;或新建 `build/SelfDeploy.targets` 并由根导入,二选一)。

```xml
<Project>
  <!-- 任何声明 SelfDeployEnabled=true 的项目,构建完成后自投放整个 TargetDir -->
  <Target Name="DeploySelfToRuntime" AfterTargets="Build"
          Condition="'$(SelfDeployEnabled)' == 'true'">
    <PropertyGroup>
      <_Dest Condition="'$(SelfDeployLauncherOutputDir)' != ''">$(SelfDeployLauncherOutputDir)</_Dest>
      <_Dest Condition="'$(_Dest)' == ''">$(SelfDeployTargetDir)</_Dest>
    </PropertyGroup>
    <ItemGroup>
      <_SelfOutputFiles Include="$(TargetDir)**\*.*"
                        Condition="'$(_Dest)' != ''" />
    </ItemGroup>
    <Copy SourceFiles="@(_SelfOutputFiles)"
          DestinationFiles="@(_SelfOutputFiles->'$(_Dest)%(RecursiveDir)%(Filename)%(Extension)')"
          SkipUnchangedFiles="true" />
    <!-- GenerateDeps=true 时:重生成/核对装配清单(见 §3.3),并在 _Dest 落一份 -->
  </Target>
</Project>
```

要点:

- **无条件拷贝 exclusion**:完整部署(带清单)与轻量部署共用同一 Copy,差异仅由 `SelfDeployGenerateDeps` 决定是否额外处理清单。

- **Copy 语义沿用既有**:`SkipUnchangedFiles="true"`(沿用 PrjBridge 现状),避免无谓 IO。

- **占用/锁定处理**:沿用 Agent 侧「只补缺失、占用降级 warn、不把锁当 fatal」策略(见 §7)。

### 3.3 deps 闭包清单约定

`SelfDeployGenerateDeps=true` 时,目标是保证运行时 `AssemblyDependencyResolver` 依据的清单 == 实际拷贝集合:

1. 依赖项 `PrjBridge.deps.json` 由 SDK 在**输出目录**(`$(TargetDir)`)正常生成——既有的"拷贝整个 TargetDir"天然带出,无需手工复制。
2. 框架只在 `_Dest` 缺失 `$(SelfDeployDepsJson)`(或对应 `.deps.json`)时补一份,并追加到拷贝清单:

   ```xml
   <_SelfDepsOutput Include="$(TargetDir)$(AssemblyName).deps.json"
                    Condition="'$(SelfDeployGenerateDeps)'=='true'"
                    DestinationPath="$(_Dest)$(AssemblyName).deps.json" />
   ```
3. 一致性守卫交给运维侧 `Test-RuntimeDeployment`(§6),不在此处引入运行时双写。

### 3.4 运行时通用扩展装载器(可选分层,不强绑)

现状 `ExtensionAssemblyHost` 绑定 `PrjBridge.deps.json`。通用化方向是放宽为"装配清单 + scoped ALC"的统一装载器:

- 清单路径由**运行时配置**(如运行目录下的索引文件:哪些清单、装载顺序)而非单一点名。

- 每个产品闭包各一个清单,运行时按需解析,互不污染。

- 本期**建议仅记录为演进项**,不实施——因为它触及运行时行为与幂等语义,风险高于 G1/G2。

> 决策点:若未来出现"单产品独立扩展装载"需求再触发;当前单一运行目录下多产品并存时,继续共用一份运行时清单即可。

### 3.5 兜底烘焙上收 `EnsureCsharpBaked`

把 Agent / Produce 两份逐字重复的 `EnsureCsharpBaked` 收为全局一份:

- 位置:同一全局 targets 文件,`BeforeTargets="ResolveAssemblyReferences"`。

- 条件加入按产品命名的开关,允许关闭:

  ```xml
  <PropertyGroup>
    <SkipEnsureCsharpBaked Condition="'$(SkipEnsureCsharpBaked)'==''">false</SkipEnsureCsharpBaked>
  </PropertyGroup>
  <Target Name="EnsureCsharpBaked" BeforeTargets="ResolveAssemblyReferences"
          Condition="'$(SkipEnsureCsharpBaked)' != 'true'
                     And ('$(EnsureCsharpBakedProbe)' != '')
                     And (!Exists('$(CsharpBuildOutputDir)$(EnsureCsharpBakedProbe).dll'))">
    <!-- 此处由全局触发一次,而非每个产品各自触发 -->
  </Target>
  ```

- **探针收敛**:保留"哨兵来自不同族"的思路,但探针集上收为全局常量(默认仍是 BasicMethod / Operation.HierarchyMethod / Share.HostAssembly 3 件),产品可微调。

- **去重收益**:Agent 与 Produce 两份 targets 中的该 target 删除,逻辑全局仅一份。

***

## 4. 单一真源原则

- **路径**:`SelfDeployTargetDir` 默认值直接引用根 props 的 `FileDrivenProjectRoot`,不复制第二份路径。

- **清单**:`SelfDeployGenerateDeps` 的默认 = 轻量部署(Agent 语义);完整清单仅在显式开启时生成,清单文件名取自 `$(AssemblyName)`。

- **探针**:兜底探针集全局一份,产品不各自抄。

- **运维读取**:`AgentBakeScriptArch` 的 `Get-FileDrivenRuntimeDir` 读根 props 公式展开,框架不重复维护。

> 护栏:任何会改写 props/targets/csproj 的操作由脚本层以 `-DryRun`/`.bak` 保护,此处仅为 MSBuild 规则静态收敛。

***

## 5. 三层落地路径与取舍

| 层                    | 内容                                                                  | 风险 | 说明                                    |
| -------------------- | ------------------------------------------------------------------- | -- | ------------------------------------- |
| **L1 · 构建期统一(推荐先做)** | 新建全局 `DeploySelfToRuntime` + 兜底烘焙上收;PrjBridge/Agent 切到框架;Produce 接入 | 低  | 只改 targets/csproj 声明,不改运行时行为,回归面小、见效快 |
| **L2 · 运行时装载协议统一**   | `ExtensionAssemblyHost` 放宽为任意清单                                     | 中  | 触及运行时语义与幂等,建议(L1 稳定后)单独立项             |
| **L3 · 运维脚本多产品化**    | `Deploy-*` 侧 `-Products` 化 + `Test-RuntimeDeployment` 验证运行目录        | 中  | 与既有 AgentBakeScriptArch 无缝衔接          |

建议顺序:**L1 → L3 → L2**(先收敛构建期并补齐校验,运行时改造殿后)。

***

## 6. 实施阶段与验收标准

| 阶段                    | 内容                                                                                   | 验收标准                                                                                                               |
| --------------------- | ------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------ |
| **P1 · 只读核对**         | 盘点三处部署/两份烘焙 target 的差异;编写框架样例(临时 `SelfDeployEnabled=true` 的最小 csproj)在 `obj` 层验证属性求值 | 用 `dotnet msbuild <项目> -getProperty` 能取到 `SelfDeployTargetDir` 展开到配置层;三处语义差异罗列成表且无遗漏                               |
| **P2 · 全局 target 落地** | 建根级 targets;实现 `DeploySelfToRuntime` + 上收 `EnsureCsharpBaked`                        | ① PrjBridge 构建后运行目录闭包与迁移前**字节一致**(比对 `PrjBridge.deps.json` 与 dll 集合)② `Test-RuntimeDeployment` 通过 ③ Agent 轻量部署行为不变 |
| **P3 · 产品切换**         | PrjBridge 声明完整部署属性、Agent 声明轻量属性、Produce 声明 opt-in;删除各自重复 target                      | 三产品各自单独构建均正确投放;`EnsureCsharpBaked` 全局仅一份且 Agent/Produce 干净机可独立构建                                                   |
| **P4 · 运维多产品化**       | `Deploy-*` 支持 `-Products`;`Test-RuntimeDeployment` 校验任意产品闭包                          | 同一套脚本可验证 Agent / Engine / Produce / PrjBridge 运行目录部署态                                                              |
| **P5 ·(可选)装载协议**      | `ExtensionAssemblyHost` 通用化                                                          | 多清单并存场景下的类型解析通过;参照 Pester 镜像回归                                                                                     |

每阶段以「对照既有产物 + 幂等重跑」验收,回归靠 §7 护栏。

***

## 7. 风险与护栏

- **不改语义**:框架是既有 target 的显式化与收敛;切到框架后各产品 `AfterTargets="Build"` 触发时机不变。

- **写操作防竞态**:同一生命周期(烘焙/部署)二选一入口(全局 target 或运维脚本),避免并行写同一权威目录。

- **占用不删**:拷贝遇被占用文件只补缺失、降级 `warn`,不把锁当 fatal;沿用 Agent 侧已治根的 `CoreClean` 覆写思路。

- **Configuration 空洞回归**:`SelfDeployTargetDir` 默认值沿根 props 的 `Configuration` 预置(Debug),沿用既有「props 先于 csproj 求值」约定;`-c Release` 显式传时不被覆盖。

- **兜底探针收敛不破坏独立构建**:干净机上首个产品构建仍触发一次全局烘焙,不因上收而失效。

- **配置漂移**:清单/探针/路径统一经 §4 单一真源,`Sync-BinaryRefManifest` 可校验清单-磁盘差集。

***

## 8. 迁移对照(现 → 框架)

| 现状                                                            | 框架方式                                                                           |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| `PrjBridge.csproj` 内 `DeployBridgeToFileDrivenOutput` + 硬编码目标 | csproj 声明 `SelfDeployEnabled=true` + `SelfDeployGenerateDeps=true`;该 target 删除 |
| Agent `DeployAgentToFileDrivenRuntime`(轻量)                    | csproj 声明 `SelfDeployEnabled=true`(默认 `GenerateDeps=false`);该 target 删除        |
| Agent & Produce 两份 `EnsureCsharpBaked`                        | 上收为全局一份;两处 targets 中的同名 target 删除                                              |
| Produce(待接入)                                                  | 声明 `SelfDeployEnabled=true`,即可复用统一部署与校验                                        |
| `ExtensionAssemblyHost`(PrjBridge.deps.json 专用)               | (L2)清单路径改为运行时索引,装载器通用                                                          |

***

## 9. 与该体系既有文档的关系

- 本设计承接 `docs/design/AgentBakeScriptArch/README.md` 的运维脚本架构:其 `Test-BakeOutput` / `Sync-BinaryRefManifest` 已多产品化,本框架为**构建期 target 层**的收敛,二者互补(脚本不改 MSBuild 语义,框架只收敛 MSBuild 规则)。

- 部署/装载的红线(FileDriven 不直接引用 PrjBridge/Agent/Engine,维持"唯一引用 Projects\*"中间层)不变。

***

## 10. 待确认决策点

1. **根级 targets 落位**:新建仓库根 `Directory.Build.targets`,还是独立 `build/SelfDeploy.targets` 由根导入?(前者少一个文件、全库自动生效;后者更内聚、可单测导入)
2. **L2 装载器是否本轮落地**:仅 L1+L3 先行,还是含 L2?(影响周期与风险,此为可选分水岭)
3. **混合部署语义**:是否需要一个产品同时"轻量刷闭包 + 完整清单"两种模式并存,还是收敛为"要么轻量、要么完整"二选一。

