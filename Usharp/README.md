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
| `US1001` | error | 引用了未声明的空间（`over=` / `space=` 指向不存在） |
| `US1002` | error | `[Ref]` 坐标未在空间中声明（闸A） |
| `US1003` | error | `[Matrix]` 输出坐标未声明（闸A） |
| `US1004` | error | `[Matrix]` 行内引用坐标未声明（闸A） |
| **`US1005`** | **error** | **`[Flow]` 步骤键未声明（闸A）—— 对应「链声明被静默丢弃」类缺陷** |
| `US1006` | error | 子链 `~ <SubFlowId>` 未声明（工作流连接链引用不可解析） |
| `US1007` | error | `[Ref]` 形状非法（缺 `space`/`key`） |
| `US2001` | warn | `[CR]` 悬空（符号索引不可解析；跨库引用需同批装载） |
| `US3001` | warn | `[Flow]` 步骤键尚无对应 `[Node]` 声明（可执行体未登记） |

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
  tests/Usharp.Tests/         ← MSTest（23 例；核心）
  tests/Usharp.Emit.Tests/    ← MSTest（8 例；与 agent-loop.json 等价比对）
```

| 接线决策 | 内容 | 理由 |
|---|---|---|
| **引用方式** | 以**相对路径 `ProjectReference`** 引 CsharpPrj 项目（经 `$(UsharpCsharpPrjRoot)`）；`Usharp.Syntax` 只引 `Plate\DslMethod`（零依赖）；`Usharp.Matrix` 引 `Strategy\DecisionRules`（矩阵实例载体；闭包 = `Map\DecisionEngine` + `Map\Declarative` + `Plate\JsonMethod`），**仍不引 UniversalReference** | `Frame\Usharp` 在 CsharpPrj 之外，`modules.json`/`$(Proj_*)` 属性只服务 CsharpPrj 内部（`ModulePaths.Common.psm1` 以 CsharpPrj 为根） |
| **发射层例外（已知偏差）** | `Usharp.Emit` 引 `Operation\HierarchyMethod`（`RoundSpec` 单一权威）⇒ 构建闭包**含 UniversalReference 与平台大半**：H2「仅适配工程引 UR」的重量隔离只对**核心**成立，对发射层失效。故核心/发射层解决方案分离，核心回路不被平台闭包拖慢 | 骨架契约单一权威（SingleImplementation）优先——禁止在发射层另立第二份 `RoundSpec`；U-V7 判据只约束 `Usharp.Syntax`/`Usharp.Semantics` |
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
| **U-V5** | linter 单一实现 | 悬空/废弃/循环/反向 四检收敛为语言级 | 待批 U4（现为 `US2001` + 既有 PowerShell 脚本） |
| **U-V6** | dogfooding：`agent-loop` 由 Usharp 编译且等价 | 样例零 error + 既有测试零回归 | ✓ 样例零 error；`RoundSpec` 与 `agent-loop.json` 等价（8 例，批 U3） |

**规模取数（防幻觉，可直接复现）**：`UsharpCompilation.Snapshot()` ⇒ 样例 `agent-loop.ushar` = 空间 4 / 坐标 16 / 矩阵 1 / 流 3 / 引用 1 / 边 13 / 错误 0。
（16 = 闸A 覆盖坐标数；13 = 边数，即「引用即边」的可见产出 = 1 Ref + 1 Over + 3 RowRef + 8 StepRef/Invokes。）

**矩阵装配取数**：`UsharpMatrixEmitter.Emit(compilation)` ⇒ 样例 `agent-loop.ushar` = 绑定 1（`ProgressMatrix`：读法=值域矩阵、`over=Param`、行 3 = progress/budget/halt）/ 装配诊断 0。
（闸A 唯一权威 = `ParameterMatrix.Row`；`[Matrix]` 行右式为线性式 `bias + Σ weight·axis`，解析器 = `UsharpLinearExpression`。）

**发射取数**：`UsharpRoundSpecEmitter.Emit(compilation, "AgentLoop")` ⇒ 步骤 4（guard_input → provide_prompt → cline_execute → rule_match）/ 转移表 3（尾步不在表内 = 链终点）/ 发射诊断 0；
与 `Config\workflows\agent-loop.json` 经既有加载路径产出的骨架**逐条等价**（FuncName / 入参键 / 结果键 / 节点实例 ID / 成功准则）。

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
| **U4** | `Usharp.Cli`：`build`/`lint`/`graph`/`matrix`；把 PowerShell 四检提为语言级 linter | U-V5 | 待开工 |
| **U5** | UniversalReference 接入：`IUsharpGraphBackend` + `NullGraphBackend` ＋ `Usharp.Adapters.UniversalReference`（唯一引 UR 处）；环检测/影响域/流完整性/报表 + `ReferenceSchema` 产出 + 落库（宿主注入）；**禁引** UR `WorkflowExecution` 执行器 | U-V7…U-V12 | 待开工 |
| **U6** | 规范固化：README 增「UR 接入」章节 + 迁移看板（旧声明退役日） | 文档评审 | 待开工 |

---

## 9. 边界与风险

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


