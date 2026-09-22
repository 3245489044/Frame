# Usharp —— 以矩阵为框架、引用关系为核心的逻辑语言

> **层级**：语言框架（表层语言 + 编译器） | **状态**：五前缀（`[Space]`/`[Matrix]`/`[Flow]`/`[Ref]`/`[Obs]`）+ 闸A + 引用即边 + UR 接入 + 观测接入（批 U1–U6 / O0–O5 已落地）
> **日期**：2026-09-22 | **基座**：`Frame\CsharpPrj`（不新造运行时）
> **本文件 = 语言规范唯一权威**：语法/规则/诊断码在此定义，实现里不得重复语义（防抽象再生）。

---

## 0. 结论先行

| # | 结论 |
|---|---|
| 1 | Usharp **不是新运行时**，而是「唯一表层语言 + 编译器」：文本 → 编译器 → 落到 CsharpPrj 既有类型（`RoundSpec` / `ParameterMatrix` / `DecisionGraph` / `ReferenceSchema` / `ReferenceGraphEngine`） |
| 2 | **矩阵只有一种**：空间到自身的一步映射 `x′ = M·x`。决策矩阵 / 转移矩阵 / 判据矩阵 / 治理矩阵 / 引用邻接矩阵是它的五种读法（§1.1） |
| 3 | **引用关系为核心**：一切声明都是节点，一切引用都是边；编译器的首要产物是引用图，矩阵只是图上的属性（§1.2） |
| 4 | **解析器零自有实现**：五前缀注册进 `Plate.DslMethod` 的既有前缀表，直接复用 `DslParser`/`DslIndex`/`Registry` |
| 5 | **闸A 是存在理由**：装载期校验「引用必须落在已声明空间」；未声明即**编译报错**（对齐 `ParameterMatrix.Row` 闸A 语义，覆盖面从参数轴扩到空间键/词元） |
| 6 | 现状静默截断类缺陷（工作流 `NextNodeConfig` 只取 `on_success`、其余分支无告警）在本语言里由 `US1005` 拦下 |
| 7 | **观测是第五语言单元 `[Obs]`**（非第二观测机制）：观测语义唯一的书写面 = `.ushar` 声明，编译产出既有 `BoundaryContract`/`EventContract`，把运行期 `UnregisteredEventException` **前移到编译期**（`US6001`–`US6007`）；信封/契约/作用域/持久化一律复用 `Strategy.Observability` 内核（§9.5） |

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

### 2.1 五前缀（Usharp 包自持，注册进 `Plate.DslMethod.Parsing.DslPrefixRegistry`，id=`usharp`）

| 前缀 | 语义 | 符号类型 | 编译到 |
|---|---|---|---|
| `[Space]` | 坐标空间（键型 / 值型） | `Space` | `ParameterSchema` / 步骤键集 / 词元轴集 |
| `[Matrix]` | 空间上的一次变换（行 = 输出坐标） | `Matrix` | `ParameterMatrix` / `DecisionGraph` / `ChainSignalResolver` |
| `[Flow]` | 链 / 工作流（以链为节点） | `Flow` | `RoundSpec` + `ChainTransitionMatrix` |
| `[Ref]` | 带空间归属的引用 | `Ref` | `CoordinateRef` + 引用图边 |
| `[Obs]` | 观测声明（作用域 / 实体 / 事件；`kind=` 分子类） | `Obs` | `BoundaryContract` / `EventContract`（经适配层登记；核心零观测依赖） |

既有 `[Node]` / `[Edge]` / `[Constraint]` / `[CR]` / `%` / `#` 等前缀**继续沿用**（同一解析器、同一符号索引）。

### 2.2 书写模板

```
[Space] <SpaceId>: kind=keys
  <key1>
  <key2>

[Space] <SpaceId>: kind=values
  <axis>:<flag|ordinal|numeric>
  <axis>:<kind>(<min>..<max>)

[Space] <SubSpaceId>: kind=values | contains=<ParentSpaceId>:<EdgeType>[;<Parent2>:<EdgeType2>]   # 子空间划分（子声明父）
  <axis>:<flag|ordinal|numeric>

[Matrix] <MatrixId>: over=<SpaceId>
  <outAxis> = <线性表达式>
  <outAxis> = <线性表达式>

[Matrix] <BlockId>: over=<SubSpaceId> | process=<ProcessId>       # 小矩阵 = 该层的处理代数
  <outAxis> = <线性表达式>

[Flow] <FlowId>: over=<SpaceId>
  step <key>
  step <key> ~ <SubFlowId>
  step <key> emit=<EventKind1>,<EventKind2>          # 观测发射绑定（起始 / 完成事件）

[Flow] <PlanId>: over=<SpaceId> | process=<ProcessId> | order=<bottom_up|top_down> \
      | stop=<LevelId> | budget=<maxNodes>;<maxDepth>          # 处理编排（方向 / 止层 / 预算）
  step <key>

[Flow] <BlockId>: over=<SubSpaceId> | connects=<UpFlowId>:<EdgeType>[;…]   # 链间连接（**上游在前**）
  step <key>

[Ref] <Name>: <SpaceId>:<Key> | scope=<ScopeKey> | src=<src> | tgt=<tgt> | intent=<intent> | edge=<EdgeType>

[Obs] <Name>: kind=file                               # 语义文件头（至多一条，承载 version=）
[Obs] <ScopeKey>: kind=scope | parent=<ScopeKey>       # 关联键作用域（可声明层级；亦为 scope= 的白名单来源）
[Obs] <EntityType>: kind=entity | scope=<ScopeKey>      # 实体类型（+ 归属作用域）
[Obs] <EventKind>: kind=event | entity=<EntityType> | scope=<ScopeKey> \
      | required=<f1,f2> | metrics=<m1,m2> | enum=<field>:<v1,v2>[;<field2>:<v3>]
```

> `[Obs]` 字段：`required=`（别名 `fields=`）/ `metrics=` / `enum=`（多字段以 `;` 分段；`|` 与 `,` 已被字段/列表占用）。
> `step … emit=` 的正交绑定（不新前缀，符合 U-V4）：首个 kind = 起始事件，第二个（可选）= 完成事件。
> **划分 / 编排 / 连接字段**（P1–P7 增，零新前缀）：`contains=` = 划分归属（子声明父，喂 `HierarchyContainment`）；`process=` = 层处理器（`[Matrix]`/`[Flow]` 共域，一层恰一个 ⇒ `US1011`）；`order=`/`stop=`/`budget=` = 处理编排（喂 `HierarchyProcessPlan`，缺省不报错、非法报 `US1018`）；`connects=` = 链间连接（上游在前，只表达依赖可见性，**不放宽读域**）；`scope=` = 引用作用域（白名单 = `[Obs] kind=scope` 声明面）。多值一律以 `;` 分段。

**硬性书写规则**（由既有解析器决定，非 Usharp 自定）：

| 规则 | 说明 |
|---|---|
| 字段一律 `|` 分隔 | `src=F \| tgt=G`；`k=v` 之间**不得**只用空格（否则整段被当作一个字段值） |
| 名称 = 前缀后至首个 `:` | `[Matrix] ProgressMatrix: over=Param` ⇒ 名 = `ProgressMatrix` |
| 坐标标识符 | 坐标名 = `[A-Za-z_][A-Za-z0-9_]*`（**不含 `-`**：与线性表达式减号同形）；声明面（`[Space]` 坐标行）与引用面（矩阵行/表达式）**同一字符集**，权威 = `UsharpIdentifier`。声明名（`[Obs] agent-loop`、`process=progress-step` 等）不受此限 |
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
| **`US0006`** | **error** | **`[Space]` 的 `kind=` 取值非法（合法值：`keys`/`values`）—— 不静默按 `values` 装配** |
| **`US0007`** | **error** | **`[Space]` 缩进块内出现无法解析的坐标行 —— 禁止静默丢弃声明行** |
| `US1001` | error | 引用了未声明的空间（`over=` / `space=` 指向不存在） |
| `US1002` | error | `[Ref]` 坐标未在空间中声明（闸A） |
| `US1003` | error | `[Matrix]` 输出坐标未声明（闸A） |
| `US1004` | error | `[Matrix]` 行内引用坐标未声明（闸A；**可读域 = 自身层 ∪ 祖先闭包** —— 兄弟 / 跨子树块依赖须显式 `connects=`，否则归 `US1017`） |
| **`US1005`** | **error** | **`[Flow]` 步骤键未声明（闸A）—— 对应「链声明被静默丢弃」类缺陷** |
| `US1006` | error | 子链 `~ <SubFlowId>` 未声明（工作流连接链引用不可解析） |
| `US1007` | error | `[Ref]` 形状非法（缺 `space`/`key`） |
| `US1008` | error | 空间划分层图非法（`contains=` 形状非法 / 父空间未声明 / 层自根不可达 / 非自环层间环；**可达与环检测委托** `Modules.DeclarativeDataflow.HierarchySchemaValidator`） |
| `US1009` | error | 子空间坐标未在父空间声明（**子集不成立 ⇒ 划分非法**） |
| `US1010` | error | 矩阵行输出坐标属**祖先层**但不属自身层（越界写 / 父子双写；比 `US1003` 更精确的归因） |
| `US1011` | error | 处理器绑定非单源（`process=` 为空 / 同一层重复声明处理器） |
| `US1012` | warn | 含自环（递归划分 = 不定深度）的层未声明 `budget=`（预算风险前移；运行期由 `ProcessBudget` 强制收口） |
| `US1014` | error | `[Ref] scope=` 指向未声明作用域（白名单 = `[Obs] kind=scope` 声明面） |
| `US1015` | error | `connects=` 端点在本编译内不可解析（上游流号未声明 / 自连） |
| `US1016` | error | `connects=` 边类型为空（须形如 `<上游流号>:<连接边类型>`） |
| `US1017` | error | 跨块写依赖未声明：块读了由**非祖先块**写的坐标，而无 `connects=`（比 `US1004` 更精确的归因） |
| `US1018` | warn | 处理编排字段取值非法（`order=` 未知值 / `budget=` 非两段正整数）——按缺省处理，不静默兜底 |
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
| `US5004` | error | 层次 schema 不可产出（`contains=` 形状非法 / 单根不成立 / 一层多处理器绑定）——产出面域内判据，不复读闸A 码 |
| `US5005` | warn | 链尾重复连接：首个连接生效，后续 `connects=` 被忽略（可见化，不静默覆盖） |
| `US6001` | error | `[Obs]` 事件的 `entity=`/`scope=`（或实体的 `scope=`、作用域的 `parent=`）指向未声明实体/作用域（闸A） |
| `US6002` | error | `[Flow] step emit=` 引用未声明事件分类（**运行期 `UnregisteredEventException` 前移到编译期**） |
| `US6003` | error | `[Obs]` 同 `kind` 同名重复声明 |
| `US6004` | error | `[Obs]` 的 `enum=` 字段未出现在 `required=`（或 `fields=`）中（声明自洽性） |
| `US6005` | warn | `[Obs]` 事件声明无任何 `[Flow] step emit=` 消费（声明未接线，显式可见化） |
| `US6006` | error | `[Obs] kind=file` 多条（`version=` 版本双源；至多一条） |
| `US6007` | error | `[Obs]` 的 `kind=` 缺省或非法（合法值：`file`/`scope`/`entity`/`event`） |
| `US9001` | info | 未装配图后端（`NullGraphBackend`）：图类判据跳过，**显式可见化**（不假装通过） |
| `US9002` | info | 未装配观测后端（`NullObservabilityBackend`）：契约产出与编译期自观测跳过，**显式可见化**（不假装产出） |

所有诊断一律携带 `文件:行` 归因（`UsharpDiagnostic.ToString()`）。

---

## 6. 工程与接线

```
Frame/Usharp/
  README.md                  ← 语言规范（唯一权威，即本文件）
  Directory.Build.props      ← Usharp 编译约定（net10.0-windows；输出 Usharp\BuildOutput\$(Configuration)\）
  Usharp.sln                 ← 唯一聚合解决方案（Usharp 7 工程 + CsharpPrj 6 伞工程 = 13 条目）
                                 平台叶子经伞 csproj 递归纳入，不入 .sln；禁用 Sync-SlnToDisk.ps1 同步本文件
  samples/*.ushar            ← 样例（单一真源：测试直接拷用，不复制文本）
  src/Usharp.Syntax/          ← 语法层（复用 Plate.DslMethod 解析器）
  src/Usharp.Semantics/       ← 语义层（空间注册 + 闸A + 引用图）
  src/Usharp.Matrix/          ← 矩阵实例化层（[Matrix] → ParameterMatrix / 转移表）
  src/Usharp.Emit/            ← 发射层（[Flow] → RoundSpec + 链转移表）
    src/Usharp.Cli/             ← 命令行（build / lint / graph / matrix / obs；零 UR）
  src/Usharp.Adapters.UniversalReference/  ← UR 适配层（**唯一引 UR 处**：端口实现 / 边类型映射 / Schema / 落库）
  src/Usharp.Adapters.Observability/       ← 观测适配层（**唯一引 Strategy.Observability 处**：契约映射 / 端口实现 / manifest / 步骤自动埋点）
  tests/（已迁 Environment\CentralTest\UsharpTests）：
    Usharp.Tests/             ← MSTest（50 例；核心，零 UR）
    Usharp.Emit.Tests/        ← MSTest（9 例；与 agent-loop.json 等价比对）
    Usharp.Adapters.Tests/    ← MSTest（18 例；唯一引 UR 的测试工程）
    Usharp.Adapters.Observability.Tests/ ← MSTest（7 例；manifest 全量等价门 + 步骤自动埋点）
```

| 接线决策 | 内容 | 理由 |
|---|---|---|
| **引用方式** | 以**相对路径 `ProjectReference`** 引 CsharpPrj 项目（经 `$(UsharpCsharpPrjRoot)`）；`Usharp.Syntax` 只引 `Plate\DslMethod`（零依赖）；`Usharp.Matrix` 引 `Strategy\DecisionRules`（矩阵实例载体；闭包 = `Map\DecisionEngine` + `Map\Declarative` + `Plate\JsonMethod`），**仍不引 UniversalReference** | `Frame\Usharp` 在 CsharpPrj 之外，`modules.json`/`$(Proj_*)` 属性只服务 CsharpPrj 内部（`ModulePaths.Common.psm1` 以 CsharpPrj 为根） |
| **发射层例外（已知偏差）** | `Usharp.Emit` 引 `Operation\HierarchyMethod`（`RoundSpec` 单一权威）⇒ 构建闭包**含 UniversalReference 与平台大半**：H2「仅适配工程引 UR」的重量隔离只对**核心**成立，对发射层失效。故快回路改由「按工程构建」承载（如 `dotnet build src\Usharp.Syntax\Usharp.Syntax.csproj`）：聚合 `Usharp.sln` 只是视图，闭包由 `ProjectReference` 决定，分层不再由独立 sln 承载 | 骨架契约单一权威（SingleImplementation）优先——禁止在发射层另立第二份 `RoundSpec`；U-V7 判据只约束 `Usharp.Syntax`/`Usharp.Semantics` |
| **图能力端口（U4 提前落地）** | 核心内含 `IUsharpGraphBackend` + `NullGraphBackend` + `UsharpGraphService`（**零第三方依赖**）：环检测/影响域/流完整性经端口调用；未装配后端时判据**跳过并显式可见化**（US9001），绝不假装通过。真实图算法由 U5 的 `Usharp.Adapters.UniversalReference` 提供 | 让闸A/引用即边/诊断/四检在核心内自足，同时把"重量图框架"严格留在适配层（H2 对核心成立） |
| **UR 适配工程（唯一引 UR 处）** | `Usharp.Adapters.UniversalReference`：端口实现（转发 UR 图算法）+ 边类型映射（`ur=` → `ReferenceType`；未映射报 US4002、非法映射报 US4003）+ `ReferenceSchema` 产出（复用 `ReferenceSchemaRegistry`）+ 诊断投影（单向）+ 落库写入（`IResourceReferenceStore` 宿主注入）。**禁引** `UniversalWorkflowFramework.WorkflowExecution`（第二套执行机制） | U-V7：核心零 UR（csproj + 公共签名）；U-V8：UR 类型只出现在本工程且无执行器命中；UR 闭包只由本工程与发射层承载 |
| **不导入 CsharpPrj `Directory.Build.props`** | Usharp 自建 props，只作用于本子树 | 避免继承 `LayerBoundary.targets` 与模块属性 |
| **输出目录** | `Frame\Usharp\BuildOutput\$(Configuration)\`（props 预置 `Configuration=Debug`：直接 build csproj 与经 sln build 产物同落一处，避免目录分裂与 DEBUG 常量两态） | 不污染 CsharpPrj 权威烘焙目录；但被引的 CsharpPrj 工程按各自 props 输出到 `CsharpPrj\BuildOutput\`（构建会连带写该目录，属预期副作用） |
| **测试后置** | 暂为 Usharp 自有测试工程；烘焙成熟后再迁 `Environment\CentralTest`（其口径是二进制消费权威烘焙目录） | 避免未烘焙时引入 `RequireCsharpPrjBaked` 依赖 |

**构建/验证命令**

```powershell
cd D:\Project\Main\Frame\Usharp
# 聚合构建（唯一 sln：Usharp 7 工程 + CsharpPrj 6 伞；连带平台闭包，首次较慢、增量正常）
dotnet build Usharp.sln
# 快回路（核心零平台闭包 / 零 UR / 零观测）：按工程构建，不经聚合 sln
dotnet build src\Usharp.Syntax\Usharp.Syntax.csproj
# 测试（真源 = Environment\CentralTest\UsharpTests；二进制消费权威烘焙 dll）
cd D:\Project\Main\Environment\CentralTest\UsharpTests
dotnet test Usharp.Tests\Usharp.Tests.csproj
dotnet test Usharp.Emit.Tests\Usharp.Emit.Tests.csproj
dotnet test Usharp.Adapters.Tests\Usharp.Adapters.Tests.csproj
dotnet test Usharp.Adapters.Observability.Tests\Usharp.Adapters.Observability.Tests.csproj
# 命令行（核心回路，零 UR）
cd D:\Project\Main\Frame\Usharp
dotnet run --project src\Usharp.Cli -- build  samples\agent-loop.ushar
dotnet run --project src\Usharp.Cli -- lint   samples\agent-loop.ushar
dotnet run --project src\Usharp.Cli -- graph  samples\agent-loop.ushar --reachable AgentLoop --path AgentLoop answer
dotnet run --project src\Usharp.Cli -- matrix samples\agent-loop.ushar
dotnet run --project src\Usharp.Cli -- obs    ..\..\Projects\Plug\Agent\src\Agent.Contract\Observability\agent-semantics.ushar
```

**CsharpPrj 侧唯一改动**（additive，遵循该文件自述扩展规则「新增前缀 = 在此增条目，文法不变」）：
`Plate\DslMethod\DslConstants.cs` 增 `DslSymbolKind.{Space,Matrix,Flow,Ref,Obs}` + 5 条前缀注册；`AxiomCodeGenerator.IsAxiomSymbol` 的 `_ => false` 兜底保持不变（无穷尽 switch 破坏）。
`Operation\HierarchyMethod\Nodes\Workflows\RoundSpec.cs` 增 `RoundStep.Emits`（init 属性，加法式，不改位置参数）。

---

## 7. 验收判据

| # | 判据 | 通过阈值 | 当前 |
|---|---|---|---|
| **U-V1** | 单一表层语言（不新增第二运行时） | 表层零自有行解析器；全部落到既有基座类型 | ✓ `UsharpParser` 仅薄包 `DslParser` |
| **U-V2** | 引用即边 | 反向引用/边由编译器产出（不依赖外部脚本清单） | ✓ `UsharpGraphBuilder`；`Edges` 6 类 |
| **U-V3** | 闸A：未声明坐标编译期报错 | `US1002/1003/1004/1005` 各有用例，含 `文件:行` | ✓ 5 例（`UsharpGateATests`） |
| **U-V4** | 矩阵即函数：语法层无分支 | 前缀表中无 `if`/`loop`/`switch` 类前缀 | ✓（五前缀 + 既有声明前缀均无分支语义；`[Obs]` 为观测声明单元，无分支语义） |
| **U-V5** | linter 单一实现 | 悬空/废弃/循环/反向 四检收敛为语言级 | ◐ `Usharp.Cli lint` 四检已可跑（悬空 US2001 / 废弃 US2002 / 循环 US4001 经图后端 / 反向引用报表，单一实现复用核心）；clinerules 侧 PowerShell 四检迁移 → **U4b 待开工** |
| **U-V6** | dogfooding：`agent-loop` 由 Usharp 编译且等价 | 样例零 error + 既有测试零回归 | ✓ 样例零 error；`RoundSpec` 与 `agent-loop.json` 等价（8 例，批 U3） |
| **U-V7** | 核心零 UR 依赖 | `Usharp.Syntax`/`Usharp.Semantics` 的 csproj 不含 `UniversalWorkflowFramework`；核心公共签名无 UR 类型 | ✓ 4 个核心 csproj 零命中；`Usharp.sln` 条目仅 7 个 Usharp 工程 + 6 个 CsharpPrj 伞工程（平台叶子与 UR 路径零条目） |
| **U-V8** | 适配隔离 | UR 类型**只**出现在 `Usharp.Adapters.UniversalReference`；该工程无 `WorkflowExecution` 引用 | ✓ 命中文件仅适配 csproj（1 条 `ProjectReference` + 规则文本）；`WorkflowExecution` 零命中 |
| **U-V9** | 闭环不依赖 UR | 未装配适配器（`NullGraphBackend`）时闸A/引用即边/诊断全可用；≥3 例在该模式下通过 | ✓ 7 例（`UsharpGraphTests`：US9001 可见化 / 环不误报 / 可达仅自身 / 核心判据仍可用） |
| **U-V10** | 词表单选 | 全库仅一份 `[Node]/[Edge]/[Constraint]` 声明来源；UR `ReferenceSchema` 由编译产出 | ◐ 能力已具备（`UsharpReferenceSchemaEmitter` 由 Usharp 声明产 Schema，复用 `ReferenceSchemaRegistry`）；旧 `reference-schema.dsl.md` 退役 → **U5b 待开工**（CentralTest 3 处直接读取该文件，与 U6 一并收口） |
| **U-V11** | 边类型不塌缩 | `[Edge] E: ur=Dependency` → `refType="Dependency"`（非 `Other`）；未给 `ur=` → US4002 | ✓ 5 例（显式映射优先 / 边名即词汇表 / 内置边表 / US4002 / US4003 / 映射表闸A） |
| **U-V12** | 图能力可用 | `A→B→A` 报 `US4001`；影响域与流完整性各 ≥1 例 | ✓ 4 例（UR 真实环检测 / 可达集+两点路径 / 流完整性 / 落图子集裁决） |
| **U-V13** | 改名收敛 | 全库无 `Ushar.` 命名空间（保留：`.ushar` 扩展名、`US` 诊断前缀、`U-V*` 编号） | ✓ `src`/`tests`/`samples` 零命中（已清改名期陈旧产物与缓存；仅 README 改名说明行含旧名） |
| **U-O1** | 核心零观测依赖 | 核心 csproj 零命中 + 核心公共签名无观测类型 | ✓ 核心 `using Strategy.Observability` 命中 **0**（观测类型仅现于适配工程） |
| **U-O2** | 闭环不依赖观测 | `NullObservabilityBackend` 下闸A/引用即边/诊断全可用；≥3 例 | ✓ 9 例（`usharp.obs` 规格：闸A 各码 + `US9002` 可见化 + 绑定共存） |
| **U-O3** | 未声明即编译报错 | `US6001`/`US6002` 各 ≥1 反例（含 `文件:行`）；`US6003`/`US6004`/`US6006`/`US6007` 各 ≥1 例；`US6005` warn ≥1 例 | ✓ `usharp.obs.specs` 9 例逐码覆盖 |
| **U-O4** | 契约产出同源 | L1 结构门 + L2 C1–C14 门 + L3 逐条等价（35 事件 / 9 实体 / version 全等 + 规范化 JSON 全等） | ✓ 5 例（`Usharp.Adapters.Observability.Tests`） |
| **U-O5** | 零第二机制 | 观测类型只现于适配工程 | ✓ `src` 内 `namespace Strategy.Observability*` 零命中；`using` 命中仅适配工程 3 文件 |
| **U-O6** | 适配隔离 | 适配工程零 `WorkflowExecution`、零 UR 类型 | ✓ 零命中（适配工程只引 `Usharp.Semantics` + `Strategy.Observability`） |

**语义划分判据（P1–P7；详见 `Docs\analysis\2026\9\9.22\Usharp_语义划分_子空间与子矩阵_设计方案.md` §5/§10）**

| # | 判据 | 通过阈值 | 当前 |
|---|---|---|---|
| **U-P1** | 划分闸A 可拦 | `US1008`/`US1009`/`US1010`/`US1014` 各 ≥1 反例，含 `文件:行` | ✓ 5 例（`gate-a.subspace.*` / `ref-scope-unknown`） |
| **U-P2** | 校验器单一实现 | 层图可达性 / 层间环零 Usharp 自有实现（**全部委托** `HierarchySchemaValidator`） | ✓ `HierarchyGateRule` 仅投影 + 委托（`gate-a.subspace.cycle-delegated` 钉 `errorCount=3`） |
| **U-P3** | 处理器单源 | 每层恰一个 `process=`；同层重复 → `US1011` | ✓ 1 例（`gate-a.subspace.process-duplicate`；正例 `process-ok` 零错误） |
| **U-P4** | 预算必配 | 含自环层无 `budget=` → `US1012`（warn）；计划 `EffectiveBudget` 非 null | ✓ `emit.plan.self-recursive-needs-budget` + `emit.plan.ok` |
| **U-P5** | 节点定位等价 | 层定位三元组 `(LevelId, Id, Scope)` 值等价于 `LevelRef` 形状 | ✓ `gate-a.subspace.ref-scope-ok`（`Step:progress:agent-loop`） |
| **U-P6** | 骨架等价（真源替换门） | Usharp 产 schema JSON 与 `HierarchySchemas.HistoryTaskNodeJson` **规范化 JSON 全等** | ✓ `emit.hierarchy.equivalent-to-history-task-node`（常量 = 产出快照，单向依赖见 §10.9 ⑤） |
| **U-P7** | 闭包不受污染 | `Usharp.Syntax`/`Usharp.Semantics` 仍零 UR、零 Observability；新增引用仅零依赖 `Modules\DeclarativeDataflow` | ✓（P2 复核） |
| **U-P8** | 零新增语言单元 | 仅五前缀的**字段扩展**；前缀表无新条目 | ✓ `UsharpParser.PrefixesRegistered()` 5 前缀不变（`usharp.syntax` 用例） |
| **U-P9** | 连接可解析且不增第二路径 | `US1015`/`US1016` 各 ≥1 反例；连接产出经**既有** `RoundSpecChainMapper` → 建链路径 | ✓ `emit.connects.ok`（`linkCount=1`）+ 两反例 |
| **U-P10** | 连接进入图校验 | 跨块写依赖未声明 → `US1017` | ✓ `emit.connects.write-dependency-missing`（恰 1 条 error） |

### 7.1 质量收敛判据（`U-R*`；施工口径 = `Docs\analysis\2026\9\9.22\Usharp_重复冗余错误逻辑重构方案.md`）

| 判据 | 面 | 内容 | 状态 |
|---|---|---|---|
| **U-R0** | 构建 | 聚合 `Usharp.sln` 0 error；测试可定位 | ✓ 已闭合（Q0；7 工程 0 警告 / 0 错误） |
| **U-R1** | 止血 | E1/E4/E6/E7/E9 各 ≥1 反例用例 | ✓ 已实施（Q1；5 例 + `US0007` 复核项） |
| **U-R2** | 真源收敛 | 坐标标识符字符集只剩 `UsharpIdentifier` 一处；`ToParameterKind` 消费 `ValueKinds` | ✓ 已实施（Q2；字符集字面量全库仅 `UsharpIdentifier.Pattern` 命中） |
| **U-R3** | 声明索引 / 加载器 | `[Node]`/`[Edge]` 扫描点 = 1；文件加载点 = 1 | ✓ 已实施（Q3；`DslSymbolKind.Node\|Edge` 命中仅 `UsharpDeclarationIndex`；`File.ReadAllText` 命中仅 `UsharpSourceLoader`） |
| **U-R4** | 双判据归并 | 矩阵行闸A 只由 `UsharpValidator` 产出；US3001/US5002 口径一致 | ✓ 已实施（Q4；发射器不再映射闸A 码；US3001 去空守卫后与 US5002 同条件） |
| **U-R5** | 渲染 / 图视图 / 冗余 | 删除清单内 API 后 0 警告；`Report()` 只剩一份实现 | ✓ 已实施（Q5；`string.Join` 报表仅 `UsharpDiagnosticRenderer`；`RenderReport`/`Emitted`/`Pairs`/`ParseAll` 等全库 0 命中；`UsharpGraphView` 每编译缓存一次） |
| **U-R6** | 增量错误面 | E10/E13/E14/E15 各 ≥1 反例用例；产出面不复读闸A 码 | ✓ 已实施（Q6；US5004 不产出 + US5005 可见化 + 祖先层预算覆盖 + 无 `[Flow]` 不报 US1017） |
| **U-R7** | 层图 / 诊断码单源 | 层图遍历实现点 = 1（委托内核 `LevelOrderFromRoot`）；计划诊断码名单单源 | ✓ 已实施（Q7；`UsharpHierarchyGraph` + `UsharpCodes.PlanRelated`） |
| **U-R8** | 端口最小化 | 端口每成员均有消费方或已移除 | ✓ 已实施（Q8；观测端口编译期自观测面移除（H3 编译期不写库）、`connects` 语义记录独立、`budget=` 合法性单点） |

**规模取数（防幻觉，可直接复现）**：`UsharpCompilation.Snapshot()` ⇒ 样例 `agent-loop.ushar` = 空间 4 / 坐标 16 / 矩阵 1 / 流 3 / 引用 1 / 边 13 / 错误 0。
（16 = 闸A 覆盖坐标数；13 = 边数，即「引用即边」的可见产出 = 1 Ref + 1 Over + 3 RowRef + 8 StepRef/Invokes。）

**矩阵装配取数**：`UsharpMatrixEmitter.Emit(compilation)` ⇒ 样例 `agent-loop.ushar` = 绑定 1（`ProgressMatrix`：读法=值域矩阵、`over=Param`、行 3 = progress/budget/halt）/ 装配诊断 0。
（闸A 唯一权威 = `ParameterMatrix.Row`；`[Matrix]` 行右式为线性式 `bias + Σ weight·axis`，解析器 = `UsharpLinearExpression`。）

**发射取数**：`UsharpRoundSpecEmitter.Emit(compilation, "AgentLoop")` ⇒ 步骤 4（guard_input → provide_prompt → cline_execute → rule_match）/ 转移表 3（尾步不在表内 = 链终点）/ 发射诊断 0；
与 `Config\workflows\agent-loop.json` 经既有加载路径产出的骨架**逐条等价**（FuncName / 入参键 / 结果键 / 节点实例 ID / 成功准则）。

**命令行取数**：`Usharp.Cli build samples\agent-loop.ushar` ⇒ `空间 4 / 坐标 16 / 矩阵 1 / 流 3 / 引用 1 / 边 13 / 错误 0 / 警告 0`；
`graph` ⇒ `引用图：节点 9 / 边 9`（落图子集 = 13 - 1 Over - 3 RowRef）；未装配图后端时输出 `US9001` 可见化提示（判据跳过而非假装通过）。

**观测取数（批 O1–O5）**：`Usharp.Cli obs Projects\Plug\Agent\src\Agent.Contract\Observability\agent-semantics.ushar` ⇒
`[Obs] 作用域 0 / 实体 9 / 事件 35 / version=1.2` + `US9002` 可见化（核心默认不装配观测后端）；
装配 `UsharpObservabilityBackend` 后产出 manifest：`StandardSchemaValidator` 零违例（L1）/ `StandardConsistency.Check` 零违例（L2）/
与手写 `observability_semantics.json` **规范化 JSON 逐字符全等**（L3：35 事件 / 9 实体同序 / version 1.2）。
**步骤自动埋点取数**：`[Flow] step … emit=StepStarted,StepDone` ⇒ `RoundStep.Emits` 承载绑定；执行时经 `UsharpFlowObserver` 自动发 2 帧（起始无度量 / 完成补 `elapsed_ms`），未声明 `emit=` 的步骤零事件。

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
| **U3** | `Usharp.Emit`：`[Flow]` → `RoundSpec` + 链转移表；样例 `agent-loop.ushar` 与 `Config\workflows\agent-loop.json` 等价（步骤序 / FuncName / 入参键 / 结果键 / 成功准则逐条一致）；`on_success` 之外的键由闸A 编译期拦下 → 解 C1 | U-V6 | ✓ **已完成**（8 例；构建经聚合 `Usharp.sln`） |
| **U4** | `Usharp.Cli`：`build`/`lint`/`graph`/`matrix` 四命令；lint 四检（悬空/废弃/循环/反向）；**图能力端口**（`IUsharpGraphBackend` + `NullGraphBackend` + `UsharpGraphService`）提前落核心（零 UR） | U-V5（语言侧部分）/ U-V9 | ✓ **已完成**（9 CLI 例 + 7 图例） |
| **U4b** | clinerules 侧的 PowerShell 四检（悬空/废弃/循环/反向 + `references.md` 生成）迁移/委托到 `Usharp.Cli lint` | U-V5（全量） | 待开工（涉及 `references.md` 模板生成与 CI 接入） |
| **U5** | UniversalReference 接入：`IUsharpGraphBackend` + `NullGraphBackend` ＋ `Usharp.Adapters.UniversalReference`（唯一引 UR 处）；环检测/影响域/流完整性/报表 + `ReferenceSchema` 产出 + 落库（宿主注入）；**禁引** UR `WorkflowExecution` 执行器 | U-V7…U-V12 | ✓ **已完成**（适配层 18 例；端口 `IUsharpGraphBackend`/`NullGraphBackend` 已于 U4 提前落核心） |
| **U5b** | 词表单选落地：`reference-schema.dsl.md` 的 `[Node]/[Edge]/[Constraint]` 声明迁至 Usharp 侧声明文件，由 CLI 写到既有提交路径 `reference-schema.generated.json`，旧 `.dsl.md` 退役 | U-V10 | 待开工（CentralTest `ReferenceSchemaDslTests` 3 处直接读取该文件，须与 U6 迁移看板一并收口） |
| **U6** | 规范固化：README 增「UR 接入」章节（装配模式 / 映射表 / 禁引清单 / 诊断码真源）+ 迁移看板（旧声明退役条件）；回写分析文件结论 | 文档评审 | ✓ **已完成**（见本文件 §9） |
| **P1–P7** | 语义划分（子空间 / 子矩阵 / 节点）：`contains`/`process`/`order`/`stop`/`budget`/`scope`/`connects` 七个**字段**（零新前缀）+ 闸A `US1008`–`US1018` + 层次计划/schema 产出 + 链间连接（委托 `DeclarativeDataflow` / `WorkFlowMethod`，不复制其算法） | U-P1…U-P10（见 §7） | ✓ **已完成**（实施记录 = 设计方案 §10；施工口径 = P4–P7 施工计划） |

### 8.1 批 O0–O5（观测模块语言级引入；方案 = 第五语言单元 `[Obs]`）

> 详细方案见 `Docs\analysis\2026\9\9.22\Usharp_观测模块语言级引入分析与批次.md`（引入架构 / 映射表 / H1–H6 硬约束）；
> 施工计划见 `…\Usharp_观测模块语言级引入实施计划.md`；收敛裁决见 `…\Usharp_观测收敛裁决.md`；实施记录见 `…\Usharp_观测接入-实施记录.md`。

| 批 | 内容 | 判据 | 状态 |
|---|---|---|---|
| **O0** | 收敛裁决（纯文档）：三代观测机制的单真源与降级表 + 迁移看板（每行含退役条件与负责批次） | 文档评审（看板无「无退役条件」行） | ✓ **已完成** |
| **O1** | 第五前缀 `[Obs]` + 语法模型（`UsharpObsDecl`）+ `[Flow] step … emit=` 正交绑定 | U-V4 修订 + CLI 规模逐字不回归 | ✓ **已完成**（CLI 输出零回归） |
| **O2** | 闸A `US6001`–`US6007` + `US9002` + 观测端口 `IUsharpObservabilityBackend` + `NullObservabilityBackend`（核心零观测依赖） | U-O1/U-O2/U-O3 | ✓ **已完成**（9 例） |
| **O3** | 适配工程 `Usharp.Adapters.Observability`（唯一引观测处）+ manifest **全量等价门** | U-O4（L1+L2+L3）/U-O5/U-O6 | ✓ **已完成**（5 例；含规范化 JSON 全等） |
| **O4** | 发射层自动埋点：`RoundStep.Emits` + `UsharpRoundSpecEmitter` 填充 + 适配层 `UsharpFlowObserver` | 等价比对零回归 + 执行含 `Emits` 的骨架自动发事件 | ✓ **已完成**（2 例；`agent-loop` 8 例零回归） |
| **O5** | 编译期自观测接线（`obs` 命令 + `US9002` 可见化）+ README 固化 + 迁移看板 | U-O1…U-O6 并入 §7；文档评审 | ✓ **已完成**（Agent 台账切换/O5b 见 §9.5 看板） |

---

## 9. 接入（UR / 观测）

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

### 9.5 观测接入（批 O0–O5）

> `Strategy.Observability` 是**既有观测内核**：Usharp 接入 = 接既有契约/信封/作用域/持久化，不新造第二套观测机制（H2）。
> **唯一引观测处** = `src\Usharp.Adapters.Observability`（核心 `Usharp.Syntax`/`Usharp.Semantics` 经 `IUsharpObservabilityBackend` 端口调用，零观测类型）。

**装配三档（宿主决定）**

| 模式 | 装配 | 能力 |
|---|---|---|
| **纯声明校验**（默认） | 不注入后端（`NullObservabilityBackend`） | `[Obs]` 闸A（`US6001`–`US6007`）+ 结构化诊断；零观测依赖；产出面可见化 `US9002` |
| **契约产出** | 注入 `UsharpObservabilityBackend`（持 `IBoundaryContractRegistry`） | 增契约登记（`BoundaryContract`/`EventContract`）+ manifest 产出 |
| **契约产出 + 落库** | 再注入 `IObservabilityBus` + `IEventStore`（宿主显式 `FlushAsync`） | 增事件落库（ur_events）——**编译期不写库**（H3） |

**映射表（Usharp `[Obs]` → `Strategy.Observability`）**

| Usharp 声明 | 内核对应 | 实现 |
|---|---|---|
| `[Obs] S: kind=scope` | 关联键作用域词汇表（`CorrelationBag.ScopeKey`） | `BoundaryContract.Data("scope:S", …)`；`parent=` 只作闸A 判据 |
| `[Obs] E: kind=entity \| scope=S` | `EntityId.EntityType`（+ 归属作用域） | `BoundaryContract.Data("entity:E", …)`；`Seq` 由 `IEntitySequenceSource` 运行期分配 |
| `[Obs] k: kind=event \| entity=… \| required=… \| metrics=… \| enum=…` | `EventContract{EventKind,EntityType,Required,MetricKeys,Enums}` | `BoundaryContract.Event(...)`；字段并集复用内核单源 `FieldSpec.UnionRequiredAndEnums` |
| `[Flow] F step <key> emit=k1,k2` | `BoundaryContract.Port("F.<key>", emits:[k1,k2])` | `Emits` 字段已存在，不新增内核类型 |
| `[Obs] kind=file \| version=v` | manifest 顶层 `version` | `UsharpObsManifestEmitter`（声明序、2 空格缩进、不排序） |

**禁引清单**：适配工程禁引 `UniversalWorkflowFramework.WorkflowExecution`（第二套执行机制）与 `Share.Frameworks.Diagnostics`（含 UR 闭包）；核心禁引任何观测类型。

**迁移看板（旧载体 → 新真源；每行一个退役条件）**

| 旧载体 | 新真源 | 切换动作 | 退役条件 |
|---|---|---|---|
| `Agent.Contract\Observability\observability_semantics.json`（手写台账） | `agent-semantics.ushar`（`[Obs]` 书写面）→ `UsharpObsManifestEmitter` 产出 | Agent 侧嵌入资源改指生成物（或生成物入库替换同名文件） | 三处守卫（`SemanticsLedgerTests` / `StandardConsistencyGuardTests` / `StandardConsistencyTests`）全绿 + 生成物入库（**O5b**，需 Agent 构建顺序接线） |
| `LogPipeline`（批次 init/flush/emergency/summary） | 宿主编排 + `Obs.FlushAsync` | 降级为宿主管道，flush 对齐调用方驱动 | 宿主 Flush 接线后 |
| `DiagnosticScopeStore.CallChainId`（`AsyncLocal`） | `CorrelationBag` + `ObsScope`（`AmbientSlot`） | 经 `DiagnosticContextProxy` 兼容期并存 | 兼容期结束且 `grep` 断言无第二 ambient |
| SQLite `Logs` 表 | `IEventStore`（JSONL / ur_events） | 降为可选 sink | sink 接线或直接退役 |

> **恒真**：`[Obs]` 不进矩阵 / 流值域，不落引用图边（观测声明只产契约，不参与引用图与矩阵运算面）。

---

## 10. 边界与风险

| 边界 | 内容 |
|---|---|
| **不新增决策机制** | `IDecisionMatrix` / `DecisionPointSemantics` 仍是唯一裁决权威；Usharp 只做「声明面 + 编译 + 组合面」 |
| **终态域不变** | `TerminationSignal`（`Success`/`Timeout`/`TripTerminated`/`Halt`）与推进域（步骤键）分属不同值域，不塞进同一空间 |
| **语法风格锁定** | 行首前缀式（已选）；**不得**再引入第二种表层风格（否则解析器双实现 = 抽象再生） |
| **观测不进运算面** | `[Obs]` 不进矩阵 / 流值域、不落引用图边；观测契约由适配工程登记，核心零观测类型（H1/H2，见 §9.5） |

| 风险 | 对策（硬约束） |
|---|---|
| **双语言并存**（Usharp 与既有 4 类声明长期并行） | 迁移看板：每类旧声明有且仅有一个退役日；Usharp 不得只为新增而存在 |
| **抽象再生**（先例：`Convert` 3→1、`Kind` 5→1 修后又长出新分散） | **四前缀 + 观测单元（`[Obs]`，批 O1 引入）之外不得新增语言单元**（U-V4 修订）；观测单元新增须附真实消费方与闸A 判据；语言面常量只在 `UsharpLanguage` 一处 |
| **语法先行、语义滞后** | 先有 U1 闸A（真实收益）再扩语法（拒绝无校验的新前缀） |
| **闸A 被绕过** | 一切消费方必须经 `UsharpCompilation`（`HasErrors` 为闸）；禁止直接用 `DslParser` 后跳过 `UsharpValidator` |
| **项目接入断裂** | 引用一律经 `$(UsharpCsharpPrjRoot)`；Usharp 不参与 CsharpPrj `modules.json`（避免双向耦合） |


