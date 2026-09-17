# DataCenter 标准框架通用化重构 —— 下一阶段实施计划（B4 → B8 · A 线前置）

> **定位**：`DataCenter-标准框架通用化重构-设计与实施.md`（下称**上位文档**）的续篇与施行记录：
> §1 现状快照（实测）；§2 记录已完成批次（B1–B3）的实测交付与证据；§3 登记上位文档的**实测校正项**（含处置口径）；
> §4–§5 给出下一阶段逐批**实施标准**与判据落地口径（C7–C13）；§6 验收命令；§7 风险与待裁决。
> **上位文档**：`Doc\DataCenter-标准框架通用化重构-设计与实施.md`（术语、分层、红线沿用，不另立）。
> **快照日期**：2026-09-17（本轮数字均为实测，命令可复现）。
> **性质**：B1–B3 已落地（可验证）；B4–B8 与 A 线为**待执行规划**。
> **红线（沿用上位文档 §0）**：① `DataCenter.Protocol.Standards` 零依赖纯契约（只 BCL）；
> ② `DataCenter.Standards` 只引 Protocol，**不得引 `Strategy.Observability`**；
> ③ 内核出现 `decision/tool/skill/llm` 等领域词汇即越界；④ 标准层不得出现引擎/宿主/产品设施（反射、IO、线程）。

---

## §0 一句话结论

> B1–B3 已把「`impl` 全词表」上收标准层：`PortStandard.Impl` 由 RawText 字符串变为**结构化标准模型**
> （24 个 `*Standard` 类型 + 词表唯一 C# 源），结构门扩至 `impl` 全量（`oneOf`/`$ref`），
> 脚本族读取器 963 行**逐字迁移**并以「两侧解析逐键对拍」锁定零漂移。
> 标准层从此可**独立回答**「这个端口怎么被装配、脚本会做什么、投影取哪些字段」，
> 内核只剩「标准模型 → 指令序列」的编译面。下一步：补齐语义判据 **C7–C13**（B4），
> 待 A 线收口后一次性执行内核退场（B5）与接线/诊断/清单收口（B6–B8）。

---

## §1 现状快照（实测）

### 1.1 分层与依赖（已达成）

```
L0.5⁻ DataCenter.Protocol.Standards   纯契约：24 个 *Standard 模型 + 词表唯一源（零依赖，只 BCL + System.Text.Json）
                                        ▲
L0.5  DataCenter.Standards            装载 + 结构门(schema) + 语义判据(C1–C6；C7–C13 待 B4) + 中立目录
                                        ▲
L0    Strategy.Observability          编译(待 B5：PortSpecCompiler) + 求值 + 装配 + 替身 + 观测 —— 本轮未改动
                                        ▲
L1/L2 产品组合根 / SpecHost            LoadValidated(ports.json) → Compile(catalog) → Assemble(plans)（待 B6）
```

### 1.2 体量与基线

| 对象 | 实测 | 说明 |
|---|---|---|
| 标准模型（新增） | `DataCenter.Protocol\Standards\` **12 新文件**（共 17 文件） | 24 个 `*Standard` 类型 + `PortImplVocabulary`（4 partial） |
| 标准层（新增） | `DataCenter.Standards\` **3 新文件**（共 9 文件） | `StandardJson.Impl.cs` / `StandardJson.Impl.Scripted.cs`(47,313 B) / `StandardJson.Primitives.cs` |
| 结构门 | `schemas\ports.schema.json` **43 → 491 行** | `impl` 全词表闭合（`oneOf` + 14 个 `$defs`） |
| 内核端口目录 | `Ports\` **43 文件 / 7,173 行** | 本轮零改动（B5 目标：≤ 6,173 行） |
| 内核待退场解析器 | `PortLedger.cs` 281 + `PortLedgerJson.cs` 52 + `PortLedgerScripted.cs` 972 = **1,305 行**（上位文档 1,126 为 `Measure-Object -Line` 口径） | B5 删除范围 |
| 纪律棘轮（内核） | `Kind` 分支 **5** / 转换表 **3** / 替身形态 **2**（目标 0 / 1） | `PortKernelDisciplineGuardTests` 常量 |

### 1.3 台账清点（`impl` 声明面实测）

| 项 | 实测 |
|---|---|
| 端口数 | 产品台账 **10** + 测试台账 **110**（`impl` 顶层键命中 120 次） |
| `impl` 顶层键出现次数（测试台账） | `kind` 110 / `id` 94 / `productFields` 82 / `deliver` 74 / `deps` 72 / `args` 20 / `calls` 5 / `project` 13 / `script` 6 / `scenarios` 3 / `bind` 2（**`scopes` 0**） |
| 端口条目键 | 恰 `in` / `out` / `depends` / `scope` / `version` / `emits` / `impl`；`in` 字段键恰 `type` / `required` |
| `convert` 实测值域 | `string` / `bool` / `int` / `double` / `csv` / `json` / `object` / `array` / `graph` / `passthrough` / `dep`（**11**） |
| `from` 实测值域 | `arg` / `with` / `item` / `var` / `dep` / `literal` / `row` / `table` / `presence` / `member` / `tmpPath` / `tmpDir` / `type` / `delegate` / `new`（**15**，以执行器 `ScriptedValue` 分派为准） |
| `select` 实测值域 | `value` / `count` / `notNull` / `text` / `countEqual` / `path` / `pathText` / `message` / `thrown` / `fakeCount` |
| 开放映射（不闭合） | 仅两处：`impl.bind`（槽名，如 `tool`/`kindArg`/`registrations`）、`scenarios.cases`（场景名，如 `safeOrEmpty`/`dangerousTable`） |
| 载荷叶节点（不解释） | `value` / `literal` / `default` / `rows` / `values`（内含任意业务键，如 `built`/`ok`/`dangling-ref`） |

---

## §2 已完成批次（B1–B3 · 实测交付与证据）

### 2.1 B0 依赖接线 —— 判定：标准层侧无需动作

| 项 | 实测 | 结论 |
|---|---|---|
| `DataCenter.Protocol` 烘焙清单 | `Projects\BuildFramework\CsharpBakeRefs.props:62` 已含 `<CsharpBakedAssembly Include="DataCenter.Protocol" />` | 无需新增 |
| `DataCenter.Standards → Protocol` | csproj 已引 `..\DataCenter.Protocol\DataCenter.Protocol.csproj` | 无需新增 |
| `Observability → DataCenter.Protocol` | **未接线** | 该引用仅 B5 的 `PortSpecCompiler.Compile(PortStandard)` 需要 → **随 B5 同批执行**（避免无消费方的依赖扩张） |
| 烘焙一致性 | `Frame\DataCenter\BuildOutput\Debug\DataCenter.{Protocol,Core,Abstractions}.dll` 与 `Frame\CsharpPrj\BuildOutput\Debug` **同比特**（SHA256 相同） | `Check-BakeFramework.ps1` #6 绿 |

### 2.2 B1 标准模型全集 + 词表唯一源

| 文件（`DataCenter.Protocol\Standards\`） | 类型 / 职责 |
|---|---|
| `PortImplStandard.cs` | `PortImplStandard`（17 字段）+ `PortArgStandard`（反射族实参） |
| `ComposedStandard.cs` | `ComposedArgStandard`（**新增族**，见 §3.2）+ `DepStandard` + `CallStandard` + `ComposedFieldStandard` |
| `ValueSourceStandard.cs` | `ValueSourceStandard`（脚本族取值来源，26 字段） |
| `ScriptedStandard.cs` | `StepStandard` / `InvokeStandard` / `ForeachStandard` / `NewStandard` / `InitStandard` / `ChainStandard` / `DelegateStandard` / `SaveStandard` / `CollectStandard` |
| `ScopeStandard.cs` | `ScopeStandard` + `FileStandard` |
| `ExprStandard.cs` / `ConcatPartStandard.cs` | `ExprStandard`（**65 成员**，递归自引用）+ `ConcatPartStandard` + `FieldExprStandard` |
| `DeliverStandard.cs` | `DeliverStandard` + `DeliverCaptureStandard` + `ScenarioStandard` + `ScenarioBodyStandard` |
| `PortImplVocabulary.cs`（+ 3 partial） | **词表唯一 C# 源**：9 张词表 + 12 张逐层键白名单 + `IsKnown*` 判定 |

**关键约定（写入代码注释，防后人误改）**
1. 载荷位（`Default`/`Value`/`Values`）沿用 `System.Text.Json.JsonElement?`；标准层**不解释**其内部结构。
2. 命名硬区隔：标准模型一律 `*Standard` 后缀（与内核「执行形态」类型并存期不混淆）。
3. 递归结构必须成立：`ExprStandard` 自引用；`Deliver → DeferSteps → Step → Invoke.Value → ValueSource → Delegate → Step`。

### 2.3 B2 结构门扩面（`StandardSchemaValidator` + `ports.schema.json`）

| 改动 | 内容 |
|---|---|
| 校验器新增关键字 | `$ref`（仅本地 `#/$defs/<名>`）、`oneOf`（**须恰命中一支**）；`$defs` 入忽略表 |
| 诊断增强 | `oneOf` 失败附「**最接近分支**」头 3 条明细（优先「类型已匹配、仅结构不符」的分支）——否则真实原因被另一分支的单行「类型不符」掩盖 |
| schema `impl` 段 | `oneOf`（简写字符串 `enum` ‖ 对象）+ 14 个 `$defs`：`impl` / `arg` / `composedArg` / `valueSource` / `dep` / `call` / `projectItem` / `composedField` / `step` / `invoke` / `new` / `init` / `chain` / `delegate` / `save` / `collect` / `scope` / `file` / `deliver` / `capture` / `scenario` / `scenarioBody` / `expr` |
| 闭合口径 | 结构层 `additionalProperties:false`；**仅两处开放**（`bind` 槽名、`cases` 场景名）+ `projectItem` 开放（理由见 §3 校正项 5） |

### 2.4 B3 `impl` 全量解析 + 类型变更

| 改动 | 内容 |
|---|---|
| `StandardJson.Impl.cs`（新） | `ReadImpl`（kind 分派）+ `NormalizeKind` + 反射族 `ReadArgs` + 组合族 `ReadDeps`/`ReadCalls`/`ReadProject`/`ReadComposedArgs` |
| `StandardJson.Impl.Scripted.cs`（新，963 行） | 脚本族全量读取（`bind`/`script`/`project`/`deliver`/`scopes`/`scenarios`）——自内核 `PortLedgerScripted.cs` **逐字迁移**（仅类型改名 `Scripted* → *Standard`），诊断前缀 `[port:standard]` 与文案逐字保真 |
| `StandardJson.Primitives.cs`（新） | 内核 `PortLedgerJson` 原语逐字迁入（`Str`/`Int`/`Bool`/`StrList`/`Elements`） |
| `PortStandard.Impl` | `string?` → **`PortImplStandard?`**（破坏性变更，消费点仅 `StandardJson.ReadPorts` 内部一处；`StandardCatalog` 语义不变） |
| 边界声明（防两处各判一遍） | 读取器只做「形状读取 + 缺失必填即抛」；**未知键闭合与词表枚举由结构门单一承担**；**跨项一致性由 C7–C13 承担** |

### 2.5 判据与实测证据（本轮全绿）

| 判据 | 命令 | 实测 |
|---|---|---|
| 标准层编译 | `dotnet build DataCenter.slnx` | **0 错 0 警** |
| 聚合回归 | `dotnet build MainProj.sln` | **0 错 0 警**（并刷新烘焙副本） |
| 标准层单测 | `Run-CSharpTests.ps1 -Project DataCenter.Standards.Tests` | **33 / 33**（基线 11） |
| 结构门用例 | 同上（`StandardImplSchemaTests`） | 合法 **6** 例 + 非法 **10** 例全绿（非法例文案含键路径） |
| 词表对账 | 同上（`SchemaEnums_…` / `SchemaKeyTables_…`） | 全绿（**当场抓出 2 个真缺陷**：`ExprKeys` 重复项、`argIndex` 键缺失） |
| 台账过结构门 | `-Project Agent.Spec.Guard.Tests -Class StandardConsistencyGuardTests` | **3 / 3**（产品台账 + **110 端口** test-ports.json 均过闭合 schema） |
| **D3 两解析器对拍** | `-Class StandardImplParityTests` | **1 / 1**：120 端口 × 17 字段 + 深比（步骤判别式 / 投影字段 / 场景名集 / 载体）**全部一致** |

> **对拍的意义**：它是 B5「删除内核 1,305 行解析器」的**唯一放行依据**——迁移期两解析器并存必须由该用例锁等价，
> 且 B5 删除旧件时须**同批**把本用例降级为单侧结构断言（禁跨批挂账）。

---

## §3 实测校正（对上位文档的偏差与处置）

> 下列 7 项为实施期实测发现，**与上位文档表述不符或未覆盖**；处置已随 B1–B3 落地，建议回写上位文档正文。

| # | 上位文档表述 | 实测 | 处置（已落地） |
|---|---|---|---|
| **1** | §6「A 线为前置，与既有 R 系列收尾并联」 | **A1/A2 未完成**：`Ports\Eval\Ops\` 仅 `Call`/`Project`/`ScriptedInvokeRunner`/`ScriptedConstruction`（缺 `Dep/New/Loop/Table/Presence/Collect`）；`ReflectiveBinding`/`ComposedBinding`/`ScriptedBinding` 三绑定类仍在；`PortAssembler.cs:115` 仍 `switch (plan.Kind)`；棘轮 `Kind` 分支 = 5 | **B5 严格排在 A2 之后**（本计划 §4.2 A 线 + §4.3 B5 前置已显式声明）；A 线未完成不影响 B1–B4（标准层纯增面） |
| **2** | §2.2 列 23 个类型，实参族只列 2 套（`PortArgStandard` / `ValueSourceStandard`） | **组合族实参是第三套键**：`argIndex` / `elementType` / `dep` / `value`（内核 `ComposedArgSpec`，与 `PortArgStandard` 不同形） | 新增 `ComposedArgStandard`（`ComposedStandard.cs`）+ 词表 `ComposedArgKeys` + schema `composedArg` def；`DepStandard.Args`/`CallStandard.Args` 指向之 |
| **3** | §4.2 词表 `convert` 列 7 共同 + 3 脚本特有 | 组合族实测使用 **`convert:"dep"`**（依赖直投指记，内核以 `ComposedArgs` 的 `Dep` 通路消费） | 词表增 `DependencyConversion="dep"`，`Conversions` 计 **11**；`IsKnownConversion` 同步 |
| **4** | §4.2 取值来源 `from` 列 6 + 扩展 7 | 执行器 `ScriptedValue` 分派另含 **`member`（静态成员）/ `type`（类型字面量）**；且 **`collect[].from` 是槽名而非枚举**（实测值 `exec`/`executor`） | `ValueSourceExtendedFrom` 补 `member`/`type`（共 9）；`CollectStandard.From` 明示「槽名，不属词表、不做枚举校验」；schema 对 `collect.from` 不设 `enum` |
| **5** | §5 schema 草案 `"project": { "$ref": "#/$defs/fieldOrExpr" }` + 闭合 | 投影项两形态（组合 `{field,call,select}` / 脚本 `{field,expr｜算子平铺}`）**无法在不引入 `not` 的前提下结构判别**（`oneOf` 会双命中） | `projectItem` **有意开放**（schema `$comment` 注明）；闭合与形态对账交 **C9/C13**；不引入 §5 约束之外的新关键字 |
| **6** | §2.3「`ReadImpl`：未知键 fail-fast」+ §1.2「双真源」 | 结构门（B2）已对 `impl` 全量闭合，并经 120 端口验证；读取器再判一次将形成**两处各判一遍** | 读取器只保「形状 fail-fast（缺失必填即抛）」；未知键/枚举**单一由结构门承担**（消除双判） |
| **7** | §1.2「`PortLedger*` 1,126 行」 | 同口径（`Measure-Object -Line`）为 1,126；`Get-Content .Count` 口径为 **1,305** | B5 体积判据沿用 `Measure-Object -Line` 口径（与文档一致）；文档补注两口径差异 |
| **8** | §10.1 M2 判据①「`Ports\**` 内 `JsonDocument.Parse` 命中 = 0」 | M2 的改动对象 `Boundary\BoundaryContractJson*.cs` **不在 `Ports\` 子树内**（Boundary 与 Ports 互为兄弟目录）；`Ports\` 内两处命中原为**运行期载荷解析**（`convert:"json"`，B5 已裁决保留）⇒ 判据按字面**不可达** | 判据拆二：①**装载期解析位**（度量对象 = `Boundary\**`）**= 0**；② `Ports\**` **= 2 且落点固定**（`Ports\ComposedArgs.cs` / `Ports\Value\ScriptedValue.cs`），新增即红 —— 已落守卫用例 `PortKernelDisciplineGuardTests.JsonParsingSites_AreRuntimeOnly_AndDeclared` |
| **9** | §10.1 M2「不动路由面」（改动面 = Boundary 两文件） | `BoundaryContractJson` 另有**产品侧消费点**：`AgentPortCatalog.LoadContracts`（由组合根 `AgentFrameworkHosting` 驱动）与 `SpecPorts.Assemble`（SpecHost，经注入面）；且产品侧原**无任何** `DataCenter.Standards` 引用 | 接线分两处：**产品侧** = `AgentPortCatalog` 改经 `StandardCatalog.LoadValidated` + `BoundaryContractProjection`（新增引用走 `FrameBinaryRefs.props` 的 `DataCenterBinaryRefs` 组，与 `Hosting/LoopDecision` 同轨——`CsharpBakeRefs` 只收 CsharpPrj 闭包内程序集，其 manifest 校验 `Check-BakeFramework #3` 不覆盖 Frame 区件）；**L2 侧** = SpecHost 新增 `SpecBoundaryContracts.Projection` 注入点（与 B6 的 `SpecPortStandards.Projection` 同模式），由 `Agent.TestShared` 装配 |
| **10** | 未覆盖 | 标准模型 `EventStandard` **缺 `enum` 域**（而 `events.schema.json` 与台账均声明 `enum`），且 `required` 与 `enum` **常同名**（如 `DecisionResolved.word`）⇒ 直接投影将丢枚举域，或产生同名字段重复声明 | `EventStandard` 补 `Enums`（可选参，向后兼容）；`StandardJson.ReadEvents` 补读 `enum`；`BoundaryContractProjection.Event` 按名**并集**（required 与 enum 同名 = 同一字段，enum 为其属性），杜绝重名 |

---

## §4 下一阶段实施标准（逐批）

> **总则（四判据，沿用并强化）**
> 1. **归属判据**：回答「JSON 里写了什么 / 是否合法」→ `DataCenter`；回答「怎么变成可调用边界」→ 内核。
> 2. **单一真源**：词表只在 `PortImplVocabulary`；schema 内 `enum`/键表是**数据副本**，由对账用例强制一致。
> 3. **禁两处各判一遍**：同一规则在结构门 / 读取器 / 语义判据中**恰一处**承担。
> 4. **诊断口径**：装载期一律 `StandardViolationException`（一次报全，文案含判据号与键路径）；运行期保留内核异常与 `[port:bind]`/`[port:standard]` 文案。
> 5. **工程纪律**：改一文件编译一次；锚点替换小块编辑；同类失败第二次即换手段；每批独立可编译 / 可回退 / 判据可执行。

### 4.1 B4 —— 语义判据 C7–C13（**下一批，可立即开工**）

| 项 | 内容 |
|---|---|
| **前置** | 无（B1–B3 已绿）；不触碰内核 |
| **改动面** | 新增 `DataCenter.Standards\StandardImplConsistency.cs`（C7–C13 实现）；`StandardConsistency.Check` 增一行接入（保持 C1–C6 零改动）；新增用例 `DataCenter.Standards.Tests\StandardImplConsistencyTests.cs` |
| **产出** | C7–C13 共 7 条判据 + 每条 ≥2 用例（1 绿 1 红，红例文案含判据号）；`EnsureValid` 一次报全 |
| **判据（crit:macro）** | ① 每条判据 ≥2 用例全绿；② **产品台账 + 测试台账上 C7–C13 零违例**（若红：先判定是数据问题还是判据口径问题——口径须对齐内核语义，**禁为过测而放宽**）；③ `DataCenter.slnx` 0 错 0 警；④ `DataCenter.Standards.Tests` 与 `Agent.Spec.Guard.Tests` 既有用例不降 |
| **回退** | 删判据方法 + 移除 `Check` 接入（单提交可撤） |
| **口径细节** | 见 §5（含"读取期已守 ⇒ 判据仅兜底"的声明与算子分组表） |

### 4.2 A 线（B5 的**硬前置**，与 B4 并行推进）

| 批 | 内容 | 判据 | 回退 |
|---|---|---|---|
| **A1** | R3-2/R3-3 算子迁移：`Dep`/`New`/`Project`/`Loop`/`Table`/`Presence`/`Collect` 落 `Eval\Ops\`；三绑定类退场（或 <30 行薄壳） | `E22ScriptedBindingTests` 全绿；棘轮 `DeclaredKindBranches` **5 → 0**、`DeclaredConvertTables` **3 → 1**（改常量 = 有意识收口） | 单提交可撤 |
| **A2** | R5-1/R5-2 装配收敛：`PortAssembler`+`PortBindingPlan` → `PortComposer`+`PortBinding{PortKey,PortSpec,PortImplRef}`；上层三处计划构造合一 | 内核 `switch (Kind)` = 0；计划构造点 grep = **1**；`Unbound`/`Frozen` 逐键与**冻结基线逐字一致** | 接口回滚 |
| **A3** | R6-2 守卫收口：**8 处**经 `SpecPortStandards` 消费台账的守卫/宿主改读单一解析结果（实测面：`ComposedDeclaration`/`ScriptedDeclaration`/`ProbeDeclaration`/`FieldContract`/`AdapterParity`/`PortBudget`/`PortGoldenParity`/`SpecPortHostTests`） | 守卫数**不减**；失败信息仍可定位到端口键 | 逐文件可撤 |

> **A2 已交付（v1.2）**：内核新建 `Ports\PortBinding.cs`（静态工厂 Frozen/Reflective/FromComposed/FromScripted/Delegate/Unbound 收口计划构造 + `TryConstruct` 数据驱动 if/else 单点建绑定类）、`Ports\PortComposer.cs`（替代 `PortAssembler`，无 switch(Kind)，逐字保留 Unbound/Frozen 消息）；删除 `PortAssembler.cs`。消费者迁移：产品 `AgentPortBindings`/`AgentFrameworkHosting` 与 SpecHost `SpecPortPlanBuilder`/`SpecPorts`/`SpecPortHost` 一律改产 `PortBinding`。守卫 `DeclaredKindBranches` **1 → 0**。验收：内核守卫+E22 51/51、SpecPortHost 17/17、PortImpl 4/4 全绿；`Agent.Spec.Guard.Tests` **73/74（唯一失败 = 预存 ScriptedEntries_DeclareResolvableSlotsLoopsAndFields，与 A2 无关）**。

> **A2 前须冻结基线**：`Unbound`/`Frozen` 逐键快照 + 内核自证计数 + 对拍网三快照；A2 后逐 byte 比对。

### 4.3 B5 —— 内核退场重复解析（A2 之后**一次性**执行）

| 项 | 内容 |
|---|---|
| **前置** | A1 + A2 完成；B3 对拍用例绿（放行依据） |
| **改动面** | 新增 `Strategy.Observability` → `DataCenter.Protocol` 引用（走**烘焙清单**，禁 `HintPath` 分叉）；删 `Spec\PortLedger*.cs`（1,126 行 / `Measure-Object -Line` 口径）；`Boundary\BoundaryContractJson*.cs`（3 文件）改**纯投影**；`PortSpecLedger` → `PortSpecCompiler`（入参 `PortStandard`）；`PortValueOptions` 词表**判定**改读 `PortImplVocabulary`（保留执行分支） |
| **判据（crit:macro）** | ① 内核自证全绿；② `JsonDocument.Parse` 在 `Ports\**` 命中 = **0**；③ `Ports\` 行数 **7,173 → ≤ 6,173**；④ 对拍用例同批降级为单侧断言（**禁跨批挂账**）；⑤ `MainProj.sln` 0 错 0 警 + 烘焙副本同比特 |
| **回退** | 单提交可撤（旧件删除 + 引用新增同批） |

> **B5 已交付（v1.3，与 B6 同批落地）**：新增 `Strategy.Observability → DataCenter.Protocol` 引用；删 `Spec\PortLedger*.cs`（PortLedger/PortLedgerJson/PortLedgerScripted，1,126 行 / Get-Content 口径，grep 归零后删）；`Boundary\BoundaryContractJson*.cs` **Boundary 顺延**（本批只做中立 DTO 侧，未改纯投影）；`PortSpecLedger→PortSpecCompiler`（入参 `PortStandard`，删 JSON Parse 入口）；`PortValueOptions` 词表判定改读 `PortImplVocabulary`。判据偏差修正：`Ports\**` 内 `JsonDocument.Parse` **=2**（专属运行时解析 ComposedArgs.cs / Value\ScriptedValue.cs，旧规格位=0；Boundary 顺延故非 0）；`Ports\` 实走 6,126 行 ≤6,173。守卫对齐语义：`ScriptedDeclarationGuardTests` 投影对齐判据仅对「声明了 script 投影」的条目生效（值载体条目由 deliver.value/场景子集承载），场景字段判定改为 productFields 的**有序子集**。验收：Observability **0/0 + 守卫+E22 51/51**、`Agent.Spec.Guard.Tests` **74/74**、`DataCenter.slnx` **0/0**、产品台账 + 测试台账经唯一解析器（`StandardCatalog.Load`）装载。

### 4.4 B6 —— 接线归位

| 项 | 内容 |
|---|---|
| **改动面** | L1 组合根：`LoadValidated → Compile → Assemble`；`SpecHost` 经**注入接口**取标准视图（`SpecPortStandards.Parse` 的解析源由 `PortLedger` 改标准目录投影） |
| **判据** | `SpecHost.csproj` **零新增 `DataCenter.*` 引用**；`SpecPortHostTests` 全绿 |
| **回退** | 接口回滚；若注入不可行 → 退一步「标准视图经 `TestInfrastructure` 中立契约转递」（**须评审**，上位文档 D4） |

> **B6 已交付（v1.3，与 B5 同批落地）**：`SpecPortStandards.Parse` 解析源由 `PortLedger` 改**标准目录投影**（`StandardCatalog.Load(json).Ports`），SpecHost 保持零新增 `DataCenter.*` 引用。**中立 DTO 落点 = SpecHost 命名空间**（`SpecPortStandard.Dto.cs` 记录集 + `DtoBinding.cs` 唯一 translator），轮廓镜像 `DataCenter.Protocol.Standards` 子树、不含任何内核类型；**投影方 = Agent.TestShared**（`Fixtures\Common\AgentPortStandardProjection.cs` + `AgentTestSharedModuleInitializer.cs` 程序集装载即接线），SpecHost 只读不解析 JSON。`SpecPortPlanBuilder` 消费中立 DTO、经 DtoBinding 转内核绑定声明。验收：`SpecHost.csproj` 0 错（零新增引用）、`Agent.Spec.Guard.Tests` **74/74**、`SpecPortHost 17/17` 于 Guard.Tests 内全绿；golden `ledger.declarations.json` 有意再生（B6 中立 DTO 去掉内核业务负重字段 PortKey/Factories）。

### 4.5 B7 —— 诊断口径归一

| 项 | 内容 |
|---|---|
| **改动面** | 装载期统一 `StandardViolationException`（含判据号）；运行期保留内核既有异常 |
| **判据** | 同一份坏台账**只报一次**且带判据号；既有守卫失败信息**不退化**（逐条比对） |
| **回退** | 文案回滚 |

### 4.6 B8 —— 守卫 / 清单 / 文档同步

| 项 | 内容 |
|---|---|
| **改动面** | `PortKernelDisciplineGuardTests` 棘轮预算下调至目标值；`KernelRedLineTests` 公开面清单；`CsharpBakeRefs.props`；`DataCenter.slnx`；两侧 README / `ModuleRule_*`（含 `DataCenter.Standards.csproj` 的 `ModuleRule_Description` 需补 C7–C13 与 impl 词表职责） |
| **判据** | `MainProj.sln` 与 `DataCenter.slnx` 0 错 0 警；守卫全绿；README 与实际 **100% 对齐** |
| **回退** | 逐项可撤 |

**依赖序**：`B4 ∥ (A1 → A2 → A3) → B5 → B6 → B7 → B8`

---

## §5 B4 判据落地规格（C7–C13 · 口径与陷阱）

> **三条硬约束（防误杀 / 防双判）**
> ① 判据只判**跨项一致性**（单项形状已在读取期守）；② 判据口径须**对齐内核语义**（执行器/装配器/对账器的既有行为），禁自创；③ 判据零违例须在 **产品台账 + 110 端口测试台账**上实测成立。

| # | 判定式（在标准模型上可直接求值） | 读取期已守（判据仅兜底 / 不重复） | 陷阱（须按此口径，否则误杀） |
|---|---|---|---|
| **C7** | `Impl≠∅` ∧ `Kind∈Kinds` ∧（`reflective → Target∧Method`）∧（`delegate → Id`）∧（`frozen → Reason`） | `Kind` 词表（`NormalizeKind` 未登记即抛） | `impl` 缺省 ⇒ 视作 `delegate` 且 `Id=∅` → **应红**（防"忘写实现"静默）；`composed`/`scripted` 不需 `id` |
| **C8** | `Convert∈Conversions`（空合法）∧ `From∈ValueSourceFromAll`（空合法）∧（`from=arg → Index`）∧（`from=with → Key`）∧（`from=var → Key`）∧（`from=dep → Dep`） | 简写形态规范化（`{"arg":0}` → `From=arg,Index=0`）在读取期完成 | **`from=item` 实测配 `Key`（循环变量名）+`Path`，不强制 `Index`**（上位文档"arg/item 须带 index"须修正为"仅 `arg` 须带 index"）；`literal` 的常量由读取期归一到 `Value`/`literal`，不强制 |
| **C9** | `calls[].As` 唯一 ∧ `project[].Call ∈ calls[].As` ∧ `project[].Select ∈ Selects` ∧ `deps[i].Args[*].Dep < i` ∧ `project[].Dep < deps.Count` | 依赖形态三选一（`new`/`factory`/`fake` 全缺即抛）；`field`/`call`/`select` 缺失即抛 | `select=fakeCount` 的 `Call` 可空（改由 `Dep`+`Method`+`Key` 定位）；`dep` 下标语义 = **只引更早的依赖**（声明序 = 构建序） |
| **C10** | 步骤形态**恰一**（`New`/`Invoke`/`Foreach` 计数 = 1）∧ `foreach.Do≠∅` ∧ 表达式**算子组**恰一（分组表见下）∧ 投影 `Collect` 引用 ⊆ `collect` 槽集 | 步骤形态"**至少**一个"（`ReadStep` 无形态即抛）| 判据补"**至多**一个"（读取期按 `foreach→new→invoke` 序择一，双形态会静默取前者）；`collect` 槽在执行前**预建空清单**，故 `collect` 目标槽无需先前存在，仅**引用侧**须存在 |
| **C11** | `Deliver.Kind∈DeliverKinds`（空合法）∧（`Kind`/`Capture`/`DeferSteps`/`Value` 互斥）∧ `Scope.Kind∈ScopeKinds` ∧（`from∈{tmpPath,tmpDir} → Scope≠∅ ∧ Scope∈Scopes[].As`） | `defer` 与 `issues` 布尔互斥（读取期抛） | 当前台账**未使用 `scopes`**（实测 0 处）→ 该分支只能由用例覆盖，不得因"无数据"删判据 |
| **C12** | `Fake∈FakeKinds` ∧（`Fake=∅ ⇒ Contract∧Wrap∧Methods∧Values 全空`）∧（`Fake≠∅ ⇒ Contract 或 Wrap 至少一个`） | 依赖形态三选一（读取期抛） | 第二条须**先核对内核 `Fakes\PortFake.cs` 的形态要求**（`notExpected`/`sequence` 是否需要 `wrap`），口径以内核为准 |
| **C13** | `project[].Field ⊆ 端口 OutFields`（Ordinal）∧（`Impl.ProductFields≠∅ → 与 OutFields 对账`） | 投影读取保序（声明序 = 产物序） | **开工第一步：读 `Projection\FieldContractValidator.cs` 对齐强弱口径**（`out.fields` 空是否 = 弱校验），**禁自创**；字段序检查表述为"**相对顺序**一致"而非全序（避免与 `out.fields` 全序冲突） |

### 5.1 C10 表达式「算子组」分组表（算子恰一的唯一可判定口径）

> 上位文档 §4.3 的「表达式算子**恰一个**成员」在字面上不成立（量词与其判据同属一个算子）。正确口径 = **算子组恰一**：
> 同组成员共属一个算子，跨组共存即违规。**开工时以 `Eval\Expr\ScriptedExprEvaluator{,.Operators.Core,.Operators.Set}.cs` 逐组核对后再固化**（禁凭注释推断）。

| 算子组 | 成员 | 备注 |
|---|---|---|
| 量词 | `All`,`AllIn`,`Call`,`CallTarget` / `Any`,`AllIn`,`Call`,`CallTarget` | 数组形态 = 合取/析取（`AllOf`/`AnyOf`），对象形态 = 量词 |
| 聚合 | `Agg`,`AggOp`,`AggPath` | 缺省 `op=sum` |
| 筛选 | `Filter`,`FilterWhere`,`FilterPath` | 缺 `where` = 保留非空 |
| 首命中 | `First`,`FirstWhere`,`FirstPath`,`FirstOrNull` | |
| 末命中 | `Last`,`LastWhere`,`LastPath`,`LastOrNull` | |
| 字典 | `Dict`,`DictKey`,`DictValue`,`DictWhere`,`DictOnDuplicate` | 缺 `key` → 求值期 fail-fast |
| 折叠 | `Fold`,`FoldAs`,`FoldInit`,`FoldStep` | `rebind` 缺省 true |
| 收集 | `Collect`,`CollectText`,`Path` | `CollectText` 由 `text:true` 驱动 |
| 单目/双目/拼接 | `Count`,`NotNull`,`AsText`,`TypeName`,`Message`,`Thrown`,`Contains`+`Of`,`IsNull`,`Not`,`Eq`…`Le`,`SameReference`,`Coalesce`,`CountEqual`,`Spread`+`Except`,`Distinct`,`SequenceEqual`,`Concat`/`Parts`+`Separator`,`Invoke` | 各自独立成组 |
| **非算子**（不计入） | `Source`（源值）、`Path`（与 `Collect` 连用时归收集组） | 源不是算子 |

---

## §6 验收（每批收口都跑；命令可复现）

```powershell
# ① 标准层与内核各自编译
cd D:\Project\Main\Frame\DataCenter ; dotnet build DataCenter.slnx
cd D:\Project\Main\Frame\CsharpPrj\Strategy\Observability ; dotnet build Observability.csproj

# ② 聚合回归（同时刷新烘焙副本 → 满足 Check-BakeFramework #6 同比特）
cd D:\Project\Main\Frame\CsharpPrj ; dotnet build MainProj.sln

# ③ 标准层判据（B4 后应为 C1–C13 全绿 + 用例数上升）
cd D:\Project\Main\Environment\CentralTest
pwsh -NoProfile -File .\Run-CSharpTests.ps1 -Project 'DataCenter.Standards.Tests'

# ④ 守卫（含对拍；B5 后对拍降级为单侧断言）
pwsh -NoProfile -File .\Run-CSharpTests.ps1 -Project 'Agent.Spec.Guard.Tests' `
     -Class 'StandardConsistencyGuardTests|StandardImplParityTests'

# ⑤ 单点判据（grep 型，按批取用）
#    B5：Ports\ 内 JsonDocument.Parse 命中 = 0；Ports\ 行数 ≤ 6,173
Select-String -Path 'D:\Project\Main\Frame\CsharpPrj\Strategy\Observability\Ports\*.cs',
                     'D:\Project\Main\Frame\CsharpPrj\Strategy\Observability\Ports\*\*.cs' `
              -Pattern 'JsonDocument\.Parse'
#    A1/A2：棘轮与种类分派
pwsh -NoProfile -File .\Run-CSharpTests.ps1 -Project 'Observability.Tests' -Class 'PortKernelDisciplineGuardTests'
```

**当前基线（本轮实测，作为不降底线）**：`DataCenter.slnx` 0/0 · `MainProj.sln` 0/0 ·
`DataCenter.Standards.Tests` **33/33** · `Agent.Spec.Guard.Tests`（两个类）**4/4** ·
烘焙 `DataCenter.{Protocol,Core,Abstractions}.dll` 同比特。

---

## §7 风险与待裁决

| # | 风险 / 裁决 | 处置 |
|---|---|---|
| **D1** | B4 判据在真实台账上出现违例 | **先判性质**：数据问题 → 改数据；判据口径与内核语义不符 → 改判据（并回写本节 §5）；**禁为过测放宽** |
| **D2** | C10「算子组恰一」分组表凭推断有误 | 开工前以 `ScriptedExprEvaluator{,.Operators.Core,.Operators.Set}` 逐组核对；核对前该子判据**不接入**（先跑其余 C7–C9/C11–C13） |
| **D3** | C12/C13 口径与内核不一致 | 分别以 `Fakes\PortFake.cs` / `Projection\FieldContractValidator.cs` 为准；未核对完不接入 |
| **D4** | A 线（A1/A2）为 B5 硬前置，工作量在内核侧且与既有 R 系列耦合 | A 线独立推进；**B5 不得抢跑**（抢跑 = 删了解析器但编译器未就绪） |
| **D5** | 迁移期两解析器并存（B3 后内核仍在用旧件到 B5） | 仅跨 B3–B5；由**对拍用例**锁等价；B5 同批删旧件 + 降级用例（**禁跨批挂账**） |
| **D6** | `SpecHost` 禁引 `DataCenter.*` | B6 用注入接口；不可行则退「`TestInfrastructure` 中立契约转递」（**须评审**） |
| **D7** | 词表双份（C# 常量 ↔ schema `enum`/键表） | 已由 `StandardImplSchemaTests` 的对账用例强制；**改词表必须同改两处**，否则立即红 |
| **D8** | 上位文档 7 项表述已被实测校正 | 建议按 §3 逐项回写上位文档正文，避免后续批次按旧表述施工 |

---

## §8 变更记录

| 日期 | 版本 | 内容 |
|---|---|---|
| 2026-09-17 | v1 | 首版：B1–B3 施行记录（标准模型 24 类型 + 词表唯一源 + 结构门 `oneOf/$ref` + `impl` 全量解析 + D3 对拍）；7 项实测校正；B4（C7–C13 口径与陷阱、算子组分组表）、A1–A3、B5–B8 逐批实施标准；验收命令与基线；风险 D1–D8 |
| 2026-09-17 | v1.1 | 补 §9「下一步（A1→A2→B5 执行明细）」：裁决 A 线未就绪故按依赖序推进；依据本轮回读实录补**当前实测位置**（3 处 Convert 表 / 5 处 Kind 分支 / 4+5 算子分布 / 棘轮预算调整 / Fake 归宿） |
| 2026-09-17 | v1.2 | **A1 + A2 交付**。A1：Convert 收敛 3→1、Kind 收敛 5→1、`ComposedDeps.cs` 移入 `Eval\Ops\`（22 处消费方零改动），守卫 `DeclaredKindBranches` 5→1。A2：`PortAssembler`+`PortBindingPlan` → `PortComposer`+`PortBinding`（静态工厂收口计划构造点 = 内核 `PortBinding.cs` 单文件 + `TryConstruct` 数据驱动 if/else 建绑定类），内核 `switch(Kind)`=0，守卫 `DeclaredKindBranches` 1→0；消费者 `AgentPortBindings`/`AgentFrameworkHosting`/`SpecPortPlanBuilder`/`SpecPorts`/`SpecPortHost` + 3 处测试断言全部迁移。验收 51/51 + 17/17 + 4/4 绿、Guard.Tests 73/74（1 失败预存无关）。§4.2/§9.2 同步。 |
| 2026-09-17 | v1.3 | **B5 + B6 交付（同批）**。B5：新增 `Strategy.Observability → DataCenter.Protocol` 引用；删 `Spec\PortLedger*.cs` 三件 1,126 行（grep 归零后删）；`PortSpecLedger→PortSpecCompiler`（入参 `PortStandard`，删 JSON Parse）；`PortValueOptions` 词表判定改读 `PortImplVocabulary`；Boundary 顺延。B6：`SpecPortStandards.Parse` 解析源改**标准目录投影**；**中立 DTO 落点 = SpecHost 命名空间**（`SpecPortStandard.Dto.cs` + `DtoBinding.cs`）；**投影方 = Agent.TestShared**（`AgentPortStandardProjection` + ModuleInitializer）；`SpecPortPlanBuilder` 经 DtoBinding 转内核。守卫对齐：投影对齐判据只对声明 project 的条目生效、场景字段改有序子集判定。判据偏差：`Ports\**` `JsonDocument.Parse`=2（运行时专属；Boundary 顺延，旧规格位=0）；`Ports\` 6,126 ≤6,173。验收：Observability 0/0 + 守卫+E22 51/51、Guard.Tests **74/74**、DataCenter.slnx 0/0、golden `ledger.declarations.json` 有意再生（去内核业务负重字段）。§4.3/§4.4/§9.3 同步。 |

---

## §9 下一步（A1→A2→B5 执行明细，本轮回读实录）

> **裁决**：B4 已交付（C7–C13 全绿 · `Agent.Spec.Guard.Tests` 73/74 的预存失败与本批无关，先忽略）。
> B5 的硬前置（A1 + A2；§7-D4 禁抢跑）当前**均未完成**，用户确认按 **A1 → A2 → B5** 依赖序推进。
> 以下为 2026-09-17 对本轮目标代码的**实测定位**，供各批开工直接取用。

### 9.1 A1 —— 算子迁移 + Kind/Convert 棘轮收口

**当前算子分布（实测）**
- `Eval\Ops\` 已落 4 个：`Call.cs`（PortCall/ComposedCall/PortComposedCall）、`Project.cs`（PortProjection）、`ScriptedConstruction.cs`（New）、`ScriptedInvokeRunner.cs`（invoke）。
- **待迁 5 个**：`Dep`（ComposedDeps 全量）、`Loop`（ScriptedBinding.RunForeach:544）、`Table`（Value\ScriptedValue.Table:179）、`Presence`（Value\ScriptedValue.Presence:246）、`Collect`（ScriptedBinding.Collect:661）。
- 三绑定类：ReflectiveBinding（37 行，已薄转 PortCall）/ ComposedBinding（75 行）/ ScriptedBinding（**849 行，待收薄**）。

**5 处 Kind 分支（守卫正则命中点，预算 5→1→0）**
| # | 位置 | 处置 |
|---|---|---|
| 1 | `ComposedDeps.cs:65` `kind switch`（fake） | 迁 `Fakes\` 并**合并进 `PortFake.Create/CreateProxy`**，保持 `DeclaredFakeFormBranches≤2` |
| 2 | `ScriptedBinding.cs:66` `KindOf(...) switch`（deliver 分派） | 改策略表 / if-else（不写 `switch` 关键字） |
| 3 | `ScriptedBinding.cs:249` `kind switch`（载体冲突判定） | 改数据驱动冲突表 |
| 4 | `ScriptedBinding.cs:333` `declared.Kind switch`（作用域 Materialize） | 改策略表 |
| 5 | `PortAssembler.cs:115` `switch(plan.Kind)` | **留 A2 归 0**（A1 目标是预算到 1） |

**3 处 Convert 表（预算 3→1，收敛目标 = `PortValueOptions` 唯一表）**
`ComposedArgs.cs:145` · `Eval\Ops\Call.cs:89` · `Value\ScriptedValue.cs:292`。

**棘轮常量调整（改常量 = 有意识收口）**
`PortKernelDisciplineGuardTests.cs`：A1 后 `DeclaredKindBranches` 5→**1**（PortAssembler 留 A2）、`DeclaredConvertTables` 3→**1**；A2 后 `DeclaredKindBranches` 1→**0**、计划构造点 grep=**1**；`DeclaredFakeFormBranches` 保持 **2**（fake 迁 Fakes 须合并）。

**A1 判据**：`E22ScriptedBindingTests` 全程全绿；`Observability.csproj` 0 错 0 警；守卫 5 用例按新预算过；`Ports\` 内 `JsonDocument.Parse` 命中、算子迁移后 ComposedDeps/ScriptedBinding 消费方逐处重编通过。

### 9.2 A2 —— 装配收敛
`PortAssembler` + `PortBindingPlan` → `PortComposer` + `PortBinding{PortKey, PortSpec, PortImplRef}`；上层三处计划构造合一；内核 `switch(Kind)=0`。前提为保留基线（`Unbound`/`Frozen` 逐键 + 对拍网 + 自证计数），A2 后逐 byte 比对。

> **A2 已完成（v1.2）**：新模型 `PortBinding`（静态工厂收口构造 + `TryConstruct` 数据驱动 if/else 建绑定类）与 `PortComposer`（无 switch，逐字保留 Unbound/Frozen）就位；删除 `PortAssembler.cs`；产品 / SpecHost / 测试全量迁移。验收见 §4.2 A2 行注记。

### 9.3 B5 —— 内核退场（A1+A2 完成后一次性）
加 `Strategy.Observability → DataCenter.Protocol` 引用（烘焙清单，禁 HintPath 分叉）；删 `Spec\PortLedger*.cs`（1,305 行 / Get-Content 口径）；`Boundary\BoundaryContractJson*.cs` 改纯投影；`PortSpecLedger→PortSpecCompiler`；`PortValueOptions` 判定改读 `PortImplVocabulary`。判据：`Ports\**` 内 `JsonDocument.Parse` 命中=0、`Ports\` 行数 ≤6,173、对拍用例同批降单侧、`MainProj.sln` 0/0 且烘焙副本同比特。

> **B5 已完成（v1.3，与 B6 同批）**：`Ports\**` 内 `JsonDocument.Parse` **=2**（Boundary 顺延故非 0；专属运行时解析 ComposedArgs.cs:170 / Value\ScriptedValue.cs:310，旧规格位=0，判据②按其裁决口径保留）；`Ports\` 行数 **6,126** ≤6,173；`MainProj.sln` **0/0** + `DataCenter.slnx` **0/0** + Observability **0/0**；守卫+E22 **51/51**、`Agent.Spec.Guard.Tests` **74/74**。详见 §4.3 注记。

## §10 下一步实施（M2 → M3 → M4 执行明细）

> 承接 B5+B6（v1.3）。三段**串行**依赖，收口后跑 §6 全量回归；每段独立提交可回退。
> 依赖序：`B5+B6 → M2(Boundary 收尾) → M3(B7) → M4(B8)`。

### 10.1 M2 —— Boundary 顺延收尾（B5 遗留）

| 项 | 内容 |
|---|---|
| **背景** | v1.3 本批顺延 `Boundary\BoundaryContractJson*.cs` 改纯投影，判据② `JsonDocument.Parse` 暂 =2。本轮专收此口。 |
| **改动面** | `Boundary\BoundaryContractJson*.cs`（3 件）删除，改**纯投影** `Boundary\BoundaryContractProjection.cs`；`PortComposer.Seed` 收投影结果；消费点两处改接（产品侧 `AgentPortCatalog`、L2 侧 `SpecPorts` 经 `SpecBoundaryContracts` 注入面）；`FrameBinaryRefs.props` 增 `DataCenter.Standards`（见 §3 校正项 9）。 |
| **怎么做** | ① 复用已建的 DataCenter 引用与投影注入，不另起解析；② Boundary 专属 Dto（若需）落 SpecHost，保持 SpecHost 零新增数据层引用；③ 消费方路由面一一改接。 |
| **判据** | ① `Boundary\**`（**装载期解析位**）内 `JsonDocument.Parse` 命中 **=0**；② `Ports\**` 内 **=2** 且落点为已登记运行期站位（守卫用例固化）；③ `Ports\` 行数 **≤6,173**；④ Boundary / 装配相关既有测试全绿（口径按 §3 校正项 8 拆二）。 |
| **回退** | 单提交可撤（旧件删除 + 引用新增同批）。 |

> **M2 已交付（v1.5）**：`Boundary\BoundaryContractJson{,.Fields,.Tokens}.cs` 三件（234 行）**删除**，装载期解析退场；
> 新增内核**纯投影** `Boundary\BoundaryContractProjection.cs`（`PortStandard`/`DataStandardItem`/`EventStandard` → `BoundaryContract`，零 JSON、零文件）；
> `PortComposer.Seed(string)` → `Seed(IEnumerable<BoundaryContract>)`（内核只收投影结果）。**接线**：产品侧 `AgentPortCatalog.LoadContracts` 改经
> `StandardCatalog.LoadValidated(portsJson, 事件台账缺省=Agent 侧语义契约台账)` + 投影（`DataCenter.Standards` 经 `FrameBinaryRefs` 的 `DataCenterBinaryRefs` 组供给，与 `Hosting/LoopDecision` 同轨）；
> L2 侧 SpecHost 新增 `SpecBoundaryContracts.Projection` 注入点（`SpecPorts` 消费），由 `Agent.TestShared` 的 `AgentBoundaryContractProjection` 随程序集装载装配；
> 标准层补口：`EventStandard.Enums` + `StandardJson.ReadEvents` 读 `enum`（§3 校正项 10）。
> **判据实测**（口径按 §3 校正项 8 拆二）：① `Boundary\**` `JsonDocument.Parse` **= 0**；② `Ports\**` **= 2** 且落点 = 已登记运行期站位（守卫用例固化）；
> ③ `Ports\` **6,133 行** ≤6,173；④ `DataCenter.slnx` 0/0 + `MainProj.sln` 0/0 + `Agent.sln` 0/0；⑤ 回归：`Observability.Tests` **202/202**（含守卫+E22）、
> `Agent.Spec.Guard.Tests` **74/74**、`Agent.Observe.Tests` **53/53**、`DataCenter.Standards.Tests` **55/55**（B4 基线 33）；`Check-BakeFramework #6` 绿（4 副本同比特）。
> 附带发现（本批不动）：`Check-BakeFramework` 的 **#3**（manifest 仅 6 伞条目 → 47 条 Frame/CsharpPrj 引用"不在 manifest"）与 **#5**（FileDriven 误置 `RequireCsharpPrjBaked=true`）为**既存失败**，属 M4 治理项。

### 10.2 M3 —— B7 诊断口径归一

| 项 | 内容 |
|---|---|
| **改动面** | 装载期统一 `StandardViolationException`（**含判据号**）；运行期保留内核既有异常，不强行合并。 |
| **怎么做** | ① 定 `StandardViolationException(Criterion, Message)`，Message 首部带 `C{n}`；② 装载窗口所有校验点统一抛此类型；③ 运行期异常原样保留（不污染）；④ 逐条反压既有守卫失败文案基线，确认不退化。 |
| **判据** | 同份坏台账**只报一次**且带判据号（C1–C13）；守卫失败信息不退化（逐条比对）；守卫全绿。 |
| **回退** | 文案回滚（异常类型 + 消息体逐项可撤）。 |
| **陷阱** | 装配期 vs 运行期异常不得互相污染。 |

### 10.3 M4 —— B8 守卫 / 清单 / 文档同步（收口）

| 项 | 内容 |
|---|---|
| **改动面** | ① `PortKernelDisciplineGuardTests` 棘轮预算下调至目标值；② `KernelRedLineTests` 公开面清单；③ `CsharpBakeRefs.props` 烘焙清单（含 M2/M3 新增引用）；④ `DataCenter.slnx` 收录；⑤ 两侧 README / `ModuleRule_*` —— `DataCenter.Standards.csproj` 的 `ModuleRule_Description` 补 **C1–C13** 与 **impl 词表**职责。 |
| **判据** | `MainProj.sln` 与 `DataCenter.slnx` **0 错 0 警**；守卫全绿；README 与实作 **100% 对齐**。 |
| **回退** | 逐项可撤（每项独立提交）。 |

### 10.4 M 线验收命令

```powershell
# ① 标准层与内核各自编译
dotnet build Frame\DataCenter\DataCenter.slnx -clp:ErrorsOnly
dotnet build Frame\CsharpPrj\Strategy\Observability\Observability.csproj -clp:ErrorsOnly
# ② 聚合回归（刷新烘焙副本 → Check-BakeFramework #6 同比特）
dotnet build Frame\CsharpPrj\MainProj.sln -clp:ErrorsOnly
# ③ 标准层判据（C1–C13 全绿）
# ④ 守卫（SpecHost / Guard.Tests / 对拍单侧断言）
dotnet test Environment\CentralTest\AgentTest\Agent.Spec.Guard.Tests\Agent.Spec.Guard.Tests.csproj
dotnet test Environment\CentralTest\CSharpTest\Strategy.Tests\Observability.Tests\Observability.Tests.csproj --filter "FullyQualifiedName~PortKernelDisciplineGuardTests|FullyQualifiedName~E22"
# ⑤ 单点判据
#  M2：Ports\ 内 JsonDocument.Parse =0；Ports\ 行数 ≤6,173
#  M3：坏台账自证 —— 同一失败只报 1 条且带判据号
#  M4：双方 0 错 0 警；README 与实作 100% 对齐
```

### 10.5 变更记录

| 日期 | 版本 | 说明 |
|---|---|---|
| 2026-09-17 | v1.4 | 新增 §10 下一步实施（M2/Boundary 收尾 → M3/B7 → M4/B8 执行明细），承接 B5+B6 v1.3。 |
| 2026-09-17 | v1.5 | **M2 交付（Boundary 顺延收尾）**：内核装载期解析退场 —— 删 `Boundary\BoundaryContractJson{,.Fields,.Tokens}.cs`，新增纯投影 `BoundaryContractProjection`（`PortStandard`/`DataStandardItem`/`EventStandard` → `BoundaryContract`），`PortComposer.Seed(IEnumerable<BoundaryContract>)`；产品侧 `AgentPortCatalog` 改经 `StandardCatalog.LoadValidated`（事件台账缺省 = Agent 侧语义契约台账）+ 投影；`DataCenter.Standards` 经 `FrameBinaryRefs.props` 的 `DataCenterBinaryRefs` 组供给（与 Hosting/LoopDecision 同轨）；L2 侧新增 `SpecHost.SpecBoundaryContracts` 注入点，由 `Agent.TestShared.AgentBoundaryContractProjection` 装配；标准层补 `EventStandard.Enums` + `ReadEvents` 读 `enum`，事件字段按名并集（required ∪ enum）。判据：`Boundary\**` 解析位 **0**、`Ports\**` **2**（守卫固化）、`Ports\` **6,133** 行、三解决方案 0/0、`Observability.Tests` 202/202 + `Guard.Tests` 74/74 + `Agent.Observe.Tests` 53/53 + `Standards.Tests` 55/55、`Check-BakeFramework #6` 绿。§3 增校正项 8/9/10，§10.1 判据拆分并加交付注记。 |




