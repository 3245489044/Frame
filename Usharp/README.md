# Usharp —— 以矩阵为框架、引用关系为核心的逻辑语言

> **层级**：语言框架（表层语言 + 编译器） | **状态**：U1 已落地（四前缀 + 闸A + 引用即边 + 测试 14/14 通过）
> **日期**：2026-09-22 | **基座**：`Frame\CsharpPrj`（不新造运行时）
> **本文件 = 语言规范唯一权威**：语法/规则/诊断码在此定义，实现里不得重复语义（防抽象再生）。

---

## 0. 结论先行

| # | 结论 |
|---|---|
| 1 | Usharp **不是新运行时**，而是「唯一表层语言 + 编译器」：文本 → 编译器 → 落到 CsharpPrj 既有类型（`RoundSpec` / `ParameterMatrix` / `DecisionGraph` / `ReferenceSchema` / `ReferenceGraphEngine`） |
| 2 | **矩阵只有一种**：空间到自身的一步映射 `x′ = M·x`。决策矩阵 / 转移矩阵 / 判据矩阵 / 治理矩阵 / 引用邻接矩阵是它的五种读法（§1.1） |
| 3 | **引用关系为核心**：一切声明都是节点，一切引用都是边；编译器的首要产物是引用图，矩阵只是图上的属性（§1.2） |
| 4 | **解析器零自有实现**：四前缀注册进 `Plate.DslMethod` 的既有前缀表，直接复用 `DslParser`/`DslIndex`/`Registry` |
| 5 | **闸A 是存在理由**：装载期校验「引用必须落在已声明空间」；未声明即**编译报错**（对齐 `ParameterMatrix.Row` 闸A 语义，覆盖面从参数轴扩到空间键/词元） |
| 6 | 现状静默截断类缺陷（工作流 `NextNodeConfig` 只取 `on_success`、其余分支无告警）在本语言里由 `US1005` 拦下 |

---

## 1. 两个核心

### 1.1 矩阵 = 同一空间到自身的一步映射（一种矩阵，五种读法）

| 读法 | 空间（坐标） | 元素含义 | 一次变换 | 承载实现（既有） |
|---|---|---|---|---|
| 值域矩阵 | `(AxisRole, axisId)` | 权重 `w` | `x′ = Quantize(W·x+b)` | `Strategy.DecisionRules.ParameterMatrix` |
| 判据矩阵 | `(axis, term)` 词元轴 | 规则命中 | `label′ = Eval(evidence)` | `BasicMethod.Declarative.Graph.DecisionGraph` |
| 转移矩阵 | 步骤键集 | 0/1 邻接 | `n′ = T·n` | `Operation.LoopFoldMethod.Signal.ChainSignalResolver` |
| 治理矩阵 | `(module, axis)` | 绑定归属 | 桶计数（缺口 = 空单元格） | `Plate.DslMethod.Logic.ModuleAxisMatrix` |
| 引用邻接矩阵 | 节点集 | 边类型 + 强度 | `n′ = Adj·n`（可达 / 环 / 影响域） | `Share.Frameworks.UniversalReference.ReferenceGraphEngine` |

**算子能力不对等是正常的**（有的带量化 `Quantize`/`Clamp`，有的只查表）——量化下沉到实例，**不进语法**。

### 1.2 引用关系 = 节点 + 边（引用即边）

| 引用形式 | 节点（源） | 边（目标） | 边类型来源 |
|---|---|---|---|
| `[Ref]` | 所在声明（或 `src=`） | 坐标键（或 `tgt=`） | `edge=` ∨ `intent=` ∨ `RefersTo` |
| `[Flow] step <key>` | 流 | 步骤键 | `StepRef` |
| `[Flow] step <key> ~ <subflow>` | 流 | 子流 | `Invokes`（= 工作流连接链） |
| `[Matrix] over=<space>` | 矩阵 | 空间 | `Over` |
| `[Matrix]` 行内坐标引用 | `矩阵.输出行` | 坐标键 | `RowRef` |
| `[CR: lib:STYP.name@intent]` | 所在声明 | 符号索引键 | `CrRef`（`intent` 即语义意图） |

⇒ 反向引用、影响分析、环检测由既有图算法免费给出（`DslIndex.Reverse` / `UniversalReference`）。

---

## 2. 表层语法（复用行首前缀式）

### 2.1 四前缀（已注册进 `Plate.DslMethod.DslConstants.Prefixes`）

| 前缀 | 语义 | 符号类型 | 编译到 |
|---|---|---|---|
| `[Space]` | 坐标空间（键型 / 值型） | `Space` | `ParameterSchema` / 步骤键集 / 词元轴集 |
| `[Matrix]` | 空间上的一次变换（行 = 输出坐标） | `Matrix` | `ParameterMatrix` / `DecisionGraph` / `ChainSignalResolver` |
| `[Flow]` | 链 / 工作流（以链为节点） | `Flow` | `RoundSpec` + `ChainTransitionMatrix` |
| `[Ref]` | 带空间归属的引用 | `Ref` | `CoordinateRef` + 引用图边 |

既有 `[Node]` / `[Edge]` / `[Constraint]` / `[CR]` / `%` / `#` 等前缀**继续沿用**（同一解析器、同一符号索引）。

### 2.2 书写模板

```
[Space] <SpaceId>: kind=keys
  <key1>
  <key2>

[Space] <SpaceId>: kind=values
  <axis>:<flag|ordinal|numeric>
  <axis>:<kind>(<min>..<max>)

[Matrix] <MatrixId>: over=<SpaceId>
  <outAxis> = <线性表达式>
  <outAxis> = <线性表达式>

[Flow] <FlowId>: over=<SpaceId>
  step <key>
  step <key> ~ <SubFlowId>

[Ref] <Name>: <SpaceId>:<Key> | src=<src> | tgt=<tgt> | intent=<intent> | edge=<EdgeType>
```

**硬性书写规则**（由既有解析器决定，非 Usharp 自定）：

| 规则 | 说明 |
|---|---|
| 字段一律 `|` 分隔 | `src=F \| tgt=G`；`k=v` 之间**不得**只用空格（否则整段被当作一个字段值） |
| 名称 = 前缀后至首个 `:` | `[Matrix] ProgressMatrix: over=Param` ⇒ 名 = `ProgressMatrix` |
| 缩进块 = 坐标/行/步骤 | 缩进（≥1 空格）行进入 `Continuation`；**块内不得有空行**（空行终止块） |
| 引用即 `[CR]` | `[CR: axioms:AX.MacroFirst@consume]` 与既有 clinerules 引用同文法、同索引 |

### 2.3 样例（`samples\agent-loop.ushar`，与 `Config\workflows\agent-loop.json` 对齐）

```
[Space] Step: kind=keys
  start
  guard_input
  provide_prompt
  cline_execute
  task_orchestrate
  rule_match

[Matrix] ProgressMatrix: over=Param
  progress = progress + 1
  budget = budget + 1

[Ref] TaskOrchestrateLink: Step:task_orchestrate | src=AgentLoop | tgt=task_orchestrate | intent=consume

[Flow] AgentLoop: over=Step
  step guard_input
  step cline_execute ~ SessionCline      # 工作流连接链
  step rule_match
```

---

## 3. 三条规则

| 规则 | 内容 | 收益 | 实现点 |
|---|---|---|---|
| **R1 引用即边** | 任何坐标引用/`[CR]` 一律落为引用图的一条边；**不写第二份清单** | 反向引用与影响分析天然可得，替代手工报表 | `UsharpGraphBuilder.Build` |
| **R2 矩阵即函数** | 语法层**无 `if`/`loop`/`switch`**：分支 = 矩阵的行/单元格，循环 = `[Flow]` 的折叠 | 决策不再散落多层（结构性排除「同一裁决两处实现」） | 前缀表中不存在分支前缀（U-V4 反证） |
| **R3 装载期闸A** | 每个 `[Space]` 即校验域；`[Matrix]`/`[Flow]`/`[Ref]` 与矩阵行内的坐标必须可解析，否则**编译报错** | 未声明即失败，禁止运行期静默计 0 / 静默丢弃分支 | `UsharpValidator.Validate` |

> **R2 的边界**：`[Flow]` 内**不允许**写条件分支（那是判据矩阵的职责）；子链引用 `~` 是**结构组合**，不是分支。

---

## 4. 编译器四段（与基座一一对应）

```
Usharp.Syntax     文本 ──► DslDocument + 四类语法声明        ← Plate.DslMethod.Parsing.DslParser（复用）
Usharp.Semantics  ──► 空间表 + 引用图 + 闸A 诊断             ← DslIndex / UniversalReference（复用）
Usharp.Matrix     ──► 矩阵实例（值域/判据/转移/治理）        ← ParameterMatrix / DecisionGraph / ChainSignalResolver
Usharp.Emit       ──► RoundSpec / ReferenceSchema / 公理类    ← RoundSpecLoader 载体 / AxiomCodeGenerator
```

| 段 | 当前状态 | 说明 |
|---|---|---|
| `Usharp.Syntax` | ✓ 已落地 | `UsharpLanguage`（语言面常量）+ `UsharpSpaceDecl`/`UsharpMatrixDecl`/`UsharpFlowDecl`（含 `UsharpRefDecl`）+ `UsharpParser`（薄包） |
| `Usharp.Semantics` | ✓ 已落地（闸A + 边） | `SpaceRegistry` / `UsharpValidator` / `UsharpGraphBuilder` / `UsharpCompilation`（门面 + `Snapshot()` + `Report()`） |
| `Usharp.Matrix` | 批 U2 | 把四类读法接上既有矩阵实例（含 `IMatrixOver<TSpace>` 适配） |
| `Usharp.Emit` | 批 U3 | `[Flow]` → `RoundSpec` + `ChainSignalResolver`；接线 `agent-loop`（dogfooding） |

---

## 5. 诊断码（`UsharpCodes` 单一真源）

| 码 | 严重度 | 含义 |
|---|---|---|
| `US0001` | error | 空间重复声明 |
| `US0002` | error | 空间内坐标重复声明 |
| `US0003` | error | 声明块为空（未声明任何坐标/行/步骤） |
| `US0004` | error | `[Matrix]` 行右式不可解析（非 `bias + Σ weight·axis` 形态） |
| `US0005` | warn | 值型坐标声明了未知值类（按 numeric 装配，不静默） |
| `US1001` | error | 引用了未声明的空间（`over=` / `space=` 指向不存在） |
| `US1002` | error | `[Ref]` 坐标未在空间中声明（闸A） |
| `US1003` | error | `[Matrix]` 输出坐标未声明（闸A） |
| `US1004` | error | `[Matrix]` 行内引用坐标未声明（闸A） |
| **`US1005`** | **error** | **`[Flow]` 步骤键未声明（闸A）—— 对应「链声明被静默丢弃」类缺陷** |
| `US1006` | error | 子链 `~ <SubFlowId>` 未声明（工作流连接链引用不可解析） |
| `US1007` | error | `[Ref]` 形状非法（缺 `space`/`key`） |
| `US2001` | warn | `[CR]` 悬空（符号索引不可解析；跨库引用需同批装载） |
| `US2002` | warn | 标记 `dep` 的 `[Node]`/`[Edge]` 仍被流步骤 / 引用边使用（废弃指向） |
| `US3001` | warn | `[Flow]` 步骤键尚无对应 `[Node]` 声明（可执行体未登记） |
| `US4001` | error | 引用图成环（环检测经 `IUsharpGraphBackend`；归因到参与环的边） |
| `US4002` | warn | 边类型未映射到 UR 词汇表（将塌缩为 `Other`，须显式 `ur=`） |
| `US4003` | error | `ur=` 映射值不是合法 `ReferenceType`（映射表闸A） |
| `US4004` | error | 流完整性违例（唯一入口 / 有终点 / 节点唯一 / 边端存在） |
| `US5001` | error | 发射目标流未声明 |
| `US5002` | error | 步骤键无 `[Node]` 声明（无法解析 FuncName，不可发射） |
| `US5003` | warn | `[Flow]` 成功准则未知（按 decision 处理） |
| `US9001` | info | 未装配图后端（`NullGraphBackend`）：图类判据跳过，**显式可见化**（不假装通过） |

所有诊断一律携带 `文件:行` 归因（`UsharpDiagnostic.ToString()`）。

---

## 6. 工程与接线

```
Frame/Usharp/
  README.md                  ← 语言规范（唯一权威，即本文件）
  Directory.Build.props      ← Usharp 编译约定（net10.0-windows；输出 Usharp\BuildOutput\$(Configuration)\）
  Usharp.sln                 ← 核心解决方案（语言核心 + 矩阵层 + 核心测试；零 UR，快回路）
  Usharp.Emit.sln            ← 发射层解决方案（连带平台闭包，慢回路；见「发射层例外」）
  samples/*.ushar            ← 样例（单一真源：测试直接拷用，不复制文本）
  src/Usharp.Syntax/          ← 语法层（复用 Plate.DslMethod 解析器）
  src/Usharp.Semantics/       ← 语义层（空间注册 + 闸A + 引用图）
  src/Usharp.Matrix/          ← 矩阵实例化层（[Matrix] → ParameterMatrix / 转移表）
  src/Usharp.Emit/            ← 发射层（[Flow] → RoundSpec + 链转移表）
  src/Usharp.Cli/             ← 命令行（build / lint / graph / matrix；零 UR）
  src/Usharp.Adapters.UniversalReference/  ← UR 适配层（**唯一引 UR 处**：端口实现 / 边类型映射 / Schema / 落库）
  tests/Usharp.Tests/         ← MSTest（39 例；核心，零 UR）
  tests/Usharp.Emit.Tests/    ← MSTest（8 例；与 agent-loop.json 等价比对）
  tests/Usharp.Adapters.Tests/ ← MSTest（18 例；唯一引 UR 的测试工程）
```

| 接线决策 | 内容 | 理由 |
|---|---|---|
| **引用方式** | 以**相对路径 `ProjectReference`** 引 CsharpPrj 项目（经 `$(UsharpCsharpPrjRoot)`）；`Usharp.Syntax` 只引 `Plate\DslMethod`（零依赖）；`Usharp.Matrix` 引 `Strategy\DecisionRules`（矩阵实例载体；闭包 = `Map\DecisionEngine` + `Map\Declarative` + `Plate\JsonMethod`），**仍不引 UniversalReference** | `Frame\Usharp` 在 CsharpPrj 之外，`modules.json`/`$(Proj_*)` 属性只服务 CsharpPrj 内部（`ModulePaths.Common.psm1` 以 CsharpPrj 为根） |
| **发射层例外（已知偏差）** | `Usharp.Emit` 引 `Operation\HierarchyMethod`（`RoundSpec` 单一权威）⇒ 构建闭包**含 UniversalReference 与平台大半**：H2「仅适配工程引 UR」的重量隔离只对**核心**成立，对发射层失效。故核心/发射层解决方案分离，核心回路不被平台闭包拖慢 | 骨架契约单一权威（SingleImplementation）优先——禁止在发射层另立第二份 `RoundSpec`；U-V7 判据只约束 `Usharp.Syntax`/`Usharp.Semantics` |
| **图能力端口（U4 提前落地）** | 核心内含 `IUsharpGraphBackend` + `NullGraphBackend` + `UsharpGraphService`（**零第三方依赖**）：环检测/影响域/流完整性经端口调用；未装配后端时判据**跳过并显式可见化**（US9001），绝不假装通过。真实图算法由 U5 的 `Usharp.Adapters.UniversalReference` 提供 | 让闸A/引用即边/诊断/四检在核心内自足，同时把"重量图框架"严格留在适配层（H2 对核心成立） |
| **UR 适配工程（唯一引 UR 处）** | `Usharp.Adapters.UniversalReference`：端口实现（转发 UR 图算法）+ 边类型映射（`ur=` → `ReferenceType`；未映射报 US4002、非法映射报 US4003）+ `ReferenceSchema` 产出（复用 `ReferenceSchemaRegistry`）+ 诊断投影（单向）+ 落库写入（`IResourceReferenceStore` 宿主注入）。**禁引** `UniversalWorkflowFramework.WorkflowExecution`（第二套执行机制） | U-V7：核心零 UR（csproj + 公共签名）；U-V8：UR 类型只出现在本工程且无执行器命中；UR 闭包只由本工程与发射层承载 |
| **不导入 CsharpPrj `Directory.Build.props`** | Usharp 自建 props，只作用于本子树 | 避免继承 `LayerBoundary.targets` 与模块属性 |
| **输出目录** | `Frame\Usharp\BuildOutput\$(Configuration)\`（props 预置 `Configuration=Debug`：直接 build csproj 与经 sln build 产物同落一处，避免目录分裂与 DEBUG 常量两态） | 不污染 CsharpPrj 权威烘焙目录；但被引的 CsharpPrj 工程按各自 props 输出到 `CsharpPrj\BuildOutput\`（构建会连带写该目录，属预期副作用） |
| **测试后置** | 暂为 Usharp 自有测试工程；烘焙成熟后再迁 `Environment\CentralTest`（其口径是二进制消费权威烘焙目录） | 避免未烘焙时引入 `RequireCsharpPrjBaked` 依赖 |

**构建/验证命令**

```powershell
cd D:\Project\Main\Frame\Usharp
# 核心回路（快；零 UR）
dotnet build Usharp.sln
dotnet test  tests\Usharp.Tests\Usharp.Tests.csproj
# 发射层回路（慢；连带平台闭包）
dotnet build Usharp.Emit.sln
dotnet test  tests\Usharp.Emit.Tests\Usharp.Emit.Tests.csproj
dotnet test  tests\Usharp.Adapters.Tests\Usharp.Adapters.Tests.csproj
# 命令行（核心回路，零 UR）
dotnet run --project src\Usharp.Cli -- build  samples\agent-loop.ushar
dotnet run --project src\Usharp.Cli -- lint   samples\agent-loop.ushar
dotnet run --project src\Usharp.Cli -- graph  samples\agent-loop.ushar --reachable AgentLoop --path AgentLoop answer
dotnet run --project src\Usharp.Cli -- matrix samples\agent-loop.ushar
```

**CsharpPrj 侧唯一改动**（additive，遵循该文件自述扩展规则「新增前缀 = 在此增条目，文法不变」）：
`Plate\DslMethod\DslConstants.cs` 增 `DslSymbolKind.{Space,Matrix,Flow,Ref}` + 4 条前缀注册；`AxiomCodeGenerator.IsAxiomSymbol` 的 `_ => false` 兜底保持不变（无穷尽 switch 破坏）。

---

## 7. 验收判据

| # | 判据 | 通过阈值 | 当前 |
|---|---|---|---|
| **U-V1** | 单一表层语言（不新增第二运行时） | 表层零自有行解析器；全部落到既有基座类型 | ✓ `UsharpParser` 仅薄包 `DslParser` |
| **U-V2** | 引用即边 | 反向引用/边由编译器产出（不依赖外部脚本清单） | ✓ `UsharpGraphBuilder`；`Edges` 6 类 |
| **U-V3** | 闸A：未声明坐标编译期报错 | `US1002/1003/1004/1005` 各有用例，含 `文件:行` | ✓ 5 例（`UsharpGateATests`） |
| **U-V4** | 矩阵即函数：语法层无分支 | 前缀表中无 `if`/`loop`/`switch` 类前缀 | ✓（四前缀 + 既有声明前缀均无分支语义） |
| **U-V5** | linter 单一实现 | 悬空/废弃/循环/反向 四检收敛为语言级 | ◐ `Usharp.Cli lint` 四检已可跑（悬空 US2001 / 废弃 US2002 / 循环 US4001 经图后端 / 反向引用报表，单一实现复用核心）；clinerules 侧 PowerShell 四检迁移 → **U4b 待开工** |
| **U-V6** | dogfooding：`agent-loop` 由 Usharp 编译且等价 | 样例零 error + 既有测试零回归 | ✓ 样例零 error；`RoundSpec` 与 `agent-loop.json` 等价（8 例，批 U3） |
| **U-V7** | 核心零 UR 依赖 | `Usharp.Syntax`/`Usharp.Semantics` 的 csproj 不含 `UniversalWorkflowFramework`；核心公共签名无 UR 类型 | ✓ 4 个核心 csproj 零命中；`Usharp.sln`（核心解决方案）UR 条目 0 |
| **U-V8** | 适配隔离 | UR 类型**只**出现在 `Usharp.Adapters.UniversalReference`；该工程无 `WorkflowExecution` 引用 | ✓ 命中文件仅适配 csproj（1 条 `ProjectReference` + 规则文本）；`WorkflowExecution` 零命中 |
| **U-V9** | 闭环不依赖 UR | 未装配适配器（`NullGraphBackend`）时闸A/引用即边/诊断全可用；≥3 例在该模式下通过 | ✓ 7 例（`UsharpGraphTests`：US9001 可见化 / 环不误报 / 可达仅自身 / 核心判据仍可用） |
| **U-V10** | 词表单选 | 全库仅一份 `[Node]/[Edge]/[Constraint]` 声明来源；UR `ReferenceSchema` 由编译产出 | ◐ 能力已具备（`UsharpReferenceSchemaEmitter` 由 Usharp 声明产 Schema，复用 `ReferenceSchemaRegistry`）；旧 `reference-schema.dsl.md` 退役 → **U5b 待开工**（CentralTest 3 处直接读取该文件，与 U6 一并收口） |
| **U-V11** | 边类型不塌缩 | `[Edge] E: ur=Dependency` → `refType="Dependency"`（非 `Other`）；未给 `ur=` → US4002 | ✓ 5 例（显式映射优先 / 边名即词汇表 / 内置边表 / US4002 / US4003 / 映射表闸A） |
| **U-V12** | 图能力可用 | `A→B→A` 报 `US4001`；影响域与流完整性各 ≥1 例 | ✓ 4 例（UR 真实环检测 / 可达集+两点路径 / 流完整性 / 落图子集裁决） |
| **U-V13** | 改名收敛 | 全库无 `Ushar.` 命名空间（保留：`.ushar` 扩展名、`US` 诊断前缀、`U-V*` 编号） | ✓ `src`/`tests`/`samples` 零命中（已清改名期陈旧产物与缓存；仅 README 改名说明行含旧名） |

**规模取数（防幻觉，可直接复现）**：`UsharpCompilation.Snapshot()` ⇒ 样例 `agent-loop.ushar` = 空间 4 / 坐标 16 / 矩阵 1 / 流 3 / 引用 1 / 边 13 / 错误 0。
（16 = 闸A 覆盖坐标数；13 = 边数，即「引用即边」的可见产出 = 1 Ref + 1 Over + 3 RowRef + 8 StepRef/Invokes。）

**矩阵装配取数**：`UsharpMatrixEmitter.Emit(compilation)` ⇒ 样例 `agent-loop.ushar` = 绑定 1（`ProgressMatrix`：读法=值域矩阵、`over=Param`、行 3 = progress/budget/halt）/ 装配诊断 0。
（闸A 唯一权威 = `ParameterMatrix.Row`；`[Matrix]` 行右式为线性式 `bias + Σ weight·axis`，解析器 = `UsharpLinearExpression`。）

**发射取数**：`UsharpRoundSpecEmitter.Emit(compilation, "AgentLoop")` ⇒ 步骤 4（guard_input → provide_prompt → cline_execute → rule_match）/ 转移表 3（尾步不在表内 = 链终点）/ 发射诊断 0；
与 `Config\workflows\agent-loop.json` 经既有加载路径产出的骨架**逐条等价**（FuncName / 入参键 / 结果键 / 节点实例 ID / 成功准则）。

**命令行取数**：`Usharp.Cli build samples\agent-loop.ushar` ⇒ `空间 4 / 坐标 16 / 矩阵 1 / 流 3 / 引用 1 / 边 13 / 错误 0 / 警告 0`；
`graph` ⇒ `引用图：节点 9 / 边 9`（落图子集 = 13 - 1 Over - 3 RowRef）；未装配图后端时输出 `US9001` 可见化提示（判据跳过而非假装通过）。

---

## 8. 批次

> **U0′–U6 的详细方案（含 UniversalReference 引入的可行性判定、映射表、硬约束 H1–H3、验收判据 U-V7–U-V13）见**
> `Docs\analysis\2026\9\9.22\Usharp_UniversalReference引入分析与下一步批次.md`。

| 批 | 内容 | 判据 | 状态 |
|---|---|---|---|
| **U0′** | 命名收敛：`Ushar.*` → `Usharp.*`（工程文件/命名空间/类型名/测试目录；保留 `.ushar` 扩展名、`US` 诊断前缀、`U-V*` 编号） | U-V13 | ✓ **已完成**（0/0 构建，14/14 测试） |
| **U1** | 四前缀 + `[Space]`/`[Ref]`/`[Matrix]`/`[Flow]` 语法模型 + **闸A** + 引用即边 | U-V1..U-V4 | ✓ **已完成** |
| **U2** | `Usharp.Matrix`：**值域矩阵**（`[Space] kind=values` → `ParameterSchema`/`ParameterMatrix`）＋**转移矩阵**（`[Space] kind=keys` → 步骤键转移表 = `ChainSignalResolver` 的构造输入）；`[Matrix]` 行复用 `ParameterMatrix.Row` 装载期闸A | 矩阵可由 `[Matrix]` 声明驱动 | ✓ **已完成**（两读法；0/0 构建，23/23 测试） |
| **U2b** | 判据读法（`DecisionGraph`，需 `kind=terms` 词元轴与规则体语法）＋治理读法（`ModuleAxisMatrix`，需 `kind=modules` 与绑定声明面） | 语法扩张须先有真实消费方 | 暂缓（拒绝无校验的新前缀） |
| **U3** | `Usharp.Emit`：`[Flow]` → `RoundSpec` + 链转移表；样例 `agent-loop.ushar` 与 `Config\workflows\agent-loop.json` 等价（步骤序 / FuncName / 入参键 / 结果键 / 成功准则逐条一致）；`on_success` 之外的键由闸A 编译期拦下 → 解 C1 | U-V6 | ✓ **已完成**（8 例；发射层解决方案 `Usharp.Emit.sln`） |
| **U4** | `Usharp.Cli`：`build`/`lint`/`graph`/`matrix` 四命令；lint 四检（悬空/废弃/循环/反向）；**图能力端口**（`IUsharpGraphBackend` + `NullGraphBackend` + `UsharpGraphService`）提前落核心（零 UR） | U-V5（语言侧部分）/ U-V9 | ✓ **已完成**（9 CLI 例 + 7 图例） |
| **U4b** | clinerules 侧的 PowerShell 四检（悬空/废弃/循环/反向 + `references.md` 生成）迁移/委托到 `Usharp.Cli lint` | U-V5（全量） | 待开工（涉及 `references.md` 模板生成与 CI 接入） |
| **U5** | UniversalReference 接入：`IUsharpGraphBackend` + `NullGraphBackend` ＋ `Usharp.Adapters.UniversalReference`（唯一引 UR 处）；环检测/影响域/流完整性/报表 + `ReferenceSchema` 产出 + 落库（宿主注入）；**禁引** UR `WorkflowExecution` 执行器 | U-V7…U-V12 | ✓ **已完成**（适配层 18 例；端口 `IUsharpGraphBackend`/`NullGraphBackend` 已于 U4 提前落核心） |
| **U5b** | 词表单选落地：`reference-schema.dsl.md` 的 `[Node]/[Edge]/[Constraint]` 声明迁至 Usharp 侧声明文件，由 CLI 写到既有提交路径 `reference-schema.generated.json`，旧 `.dsl.md` 退役 | U-V10 | 待开工（CentralTest `ReferenceSchemaDslTests` 3 处直接读取该文件，须与 U6 迁移看板一并收口） |
| **U6** | 规范固化：README 增「UR 接入」章节（装配模式 / 映射表 / 禁引清单 / 诊断码真源）+ 迁移看板（旧声明退役条件）；回写分析文件结论 | 文档评审 | ✓ **已完成**（见本文件 §9） |

---

## 9. UR 接入（批 U5 / U6）

> UniversalReference（`Share\Frameworks\UniversalReference`）是**既有共享框架**：Usharp 接入 = 接既有图能力，不新造图算法、不引入第二套执行器。
> **唯一引 UR 处** = `src\Usharp.Adapters.UniversalReference`（发射层 `Usharp.Emit` 因取 `RoundSpec` 亦携带平台闭包，见 §6「发射层例外」）。

### 9.1 装配模式（宿主决定）

| 模式 | 装配 | 能力 |
|---|---|---|
| **纯声明校验**（默认） | 不注入后端（`NullGraphBackend`） | 闸A + 引用即边 + 结构化诊断 + lint 四检（环检/影响域/流完整性**跳过并报 US9001**，不假装通过） |
| **图分析** | `new UsharpGraphService(new UniversalReferenceGraphBackend())` | 增环检测（US4001）/ 影响域可达 / 两点路径 / 流完整性（US4004） |
| **图分析 + 落库** | 再注入 `IResourceReferenceStore`（既有 `UniversalReferenceResourceReferenceStore`） | 增引用图落库——由宿主显式调用 `UsharpReferenceStoreWriter.WriteAsync`；**编译期不写库** |

> **CLI 的装配缝**：`Usharp.Cli` 自身不引适配层（保持核心零 UR），故命令行默认走「纯声明校验」模式（`graph` 会输出 US9001）。
> 需要图分析的宿主可注入后端：`UsharpCliCommands.Run(args, output, new UniversalReferenceGraphBackend())`，或直接用 `UsharpGraphService`。

### 9.2 映射表（Usharp → UniversalReference）

| 面 | 映射 | 实现 |
|---|---|---|
| 落图边 | `StepRef`/`Invokes`/`RefersTo`/`CrRef` 与 `[Ref] edge=` 自定义边 → 落图；`Over`/`RowRef` → **不落图** | `UsharpEdgeSelection.IsGraphEdge`（编译面从属与运算面不进影响域） |
| 边类型（枚举通道） | `ur=` 显式映射 → `ReferenceType` 六值；边名本身即词汇表 → 直接采用；内置表 `StepRef→Includes` / `Invokes→Uses` / `CrRef→Requires` / `RefersTo→Other`；其余 → `Other` + **US4002** | `UsharpEdgeTypeMap.Resolve`；非法 `ur=` → **US4003**；映射表闸A = `Validate` |
| 边类型（string 通道） | `IResourceReferenceStore.refType` = `ur=` ∨ 边类型原名 | **不塌缩**（任意 `[Edge]` 名可用；塌缩只发生在 UR 枚举通道） |
| 端点 → 资源 | 键型坐标 → `(ResourceType=空间号, Id=坐标键)`；流号 → `("workflow", flowId)`；`[Node]` 名 → `("node", name)`；兜底 → `("resource", token)` | `UsharpReferenceStoreWriter`；值型空间坐标不落图（属性面） |
| Schema | `[Node]`/`[Edge]`/`[Constraint]` → `ReferenceSchema`（复用 `ReferenceSchemaRegistry`） | `UsharpReferenceSchemaEmitter`；**编译产出**，禁止手改 |
| 诊断 | `UsharpDiagnostic`（结构化、含 `文件:行`）→ `DiagnosticResult`（字符串报表），**单向** | `UsharpDiagnosticProjector`；禁反向解析 |

**禁引清单**：`UniversalWorkflowFramework.WorkflowExecution`（`IWorkflowExecutor` / `WorkflowExecutor` / `WorkflowExecutionValidator`）
—— 执行机制唯一 = `[Flow]` → `RoundSpec` → 既有 `FoldEngine`；引入第二套执行器即 `ANTI-DUPLICATED-IMPLEMENTATION`。

### 9.3 诊断码

本批新增诊断码（`US0004`/`US0005`/`US2002`/`US4001`–`US4004`/`US5001`–`US5003`/`US9001`）已并入 **§5 单一真源表**，此处不重复。

### 9.4 迁移看板（旧声明 → 新声明：每类旧声明恰有一个退役条件）

| 旧声明 | 新声明（单一真源） | 状态 | 退役条件 |
|---|---|---|---|
| `Config\workflows\agent-loop.json`（`on_success` 主路径） | `samples\agent-loop.ushar` + `UsharpRoundSpecEmitter`（`RoundSpec` 逐字段等价） | ✓ 等价已验证（U3，8 例） | Agent 侧改为消费 Usharp 产出后退役（含 `RoundSpecLoader` 回退路径保留期） |
| `Operation\DataStoreDriven\ReferenceSchema\reference-schema.dsl.md` | Usharp 侧 `[Node]/[Edge]/[Constraint]` 声明 + `UsharpReferenceSchemaEmitter` 产出同名 JSON | ◐ 能力具备（U5），声明未迁（**U5b**） | CentralTest `ReferenceSchemaDslTests` 三处直读取值改到产出路径 + 重烘焙通过 |
| `.clinerules` 四检（`PowerShellScript\Operation\DSL\Check-ClinerulesReferences.ps1`） | `Usharp.Cli lint`（悬空 / 废弃 / 循环 / 反向，四检单一实现） | ◐ 语言侧四检可跑（U4），clinerules 侧未迁（**U4b**） | PS 脚本改为委托 `Usharp.Cli lint`（含 `references.md` 生成通道迁移）后退役 |
| `[Matrix]` 判据读法（`DecisionGraph`）/ 治理读法（`ModuleAxisMatrix`） | 待 U2b：需 `kind=terms|modules` 空间词法与规则体语法 | 暂缓（无真实消费方） | 出现真实消费方且语法面通过闸A 设计评审 |

---

## 10. 边界与风险

| 边界 | 内容 |
|---|---|
| **不新增决策机制** | `IDecisionMatrix` / `DecisionPointSemantics` 仍是唯一裁决权威；Usharp 只做「声明面 + 编译 + 组合面」 |
| **终态域不变** | `TerminationSignal`（`Success`/`Timeout`/`TripTerminated`/`Halt`）与推进域（步骤键）分属不同值域，不塞进同一空间 |
| **语法风格锁定** | 行首前缀式（已选）；**不得**再引入第二种表层风格（否则解析器双实现 = 抽象再生） |

| 风险 | 对策（硬约束） |
|---|---|
| **双语言并存**（Usharp 与既有 4 类声明长期并行） | 迁移看板：每类旧声明有且仅有一个退役日；Usharp 不得只为新增而存在 |
| **抽象再生**（先例：`Convert` 3→1、`Kind` 5→1 修后又长出新分散） | 四前缀之外不得新增语言单元（U-V4）；语言面常量只在 `UsharpLanguage` 一处 |
| **语法先行、语义滞后** | 先有 U1 闸A（真实收益）再扩语法（拒绝无校验的新前缀） |
| **闸A 被绕过** | 一切消费方必须经 `UsharpCompilation`（`HasErrors` 为闸）；禁止直接用 `DslParser` 后跳过 `UsharpValidator` |
| **项目接入断裂** | 引用一律经 `$(UsharpCsharpPrjRoot)`；Usharp 不参与 CsharpPrj `modules.json`（避免双向耦合） |


