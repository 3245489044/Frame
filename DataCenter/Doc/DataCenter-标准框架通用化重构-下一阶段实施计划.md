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

> **A2 前须冻结基线**：`Unbound`/`Frozen` 逐键快照 + 内核自证计数 + 对拍网三快照；A2 后逐 byte 比对。

### 4.3 B5 —— 内核退场重复解析（A2 之后**一次性**执行）

| 项 | 内容 |
|---|---|
| **前置** | A1 + A2 完成；B3 对拍用例绿（放行依据） |
| **改动面** | 新增 `Strategy.Observability` → `DataCenter.Protocol` 引用（走**烘焙清单**，禁 `HintPath` 分叉）；删 `Spec\PortLedger*.cs`（1,126 行 / `Measure-Object -Line` 口径）；`Boundary\BoundaryContractJson*.cs`（3 文件）改**纯投影**；`PortSpecLedger` → `PortSpecCompiler`（入参 `PortStandard`）；`PortValueOptions` 词表**判定**改读 `PortImplVocabulary`（保留执行分支） |
| **判据（crit:macro）** | ① 内核自证全绿；② `JsonDocument.Parse` 在 `Ports\**` 命中 = **0**；③ `Ports\` 行数 **7,173 → ≤ 6,173**；④ 对拍用例同批降级为单侧断言（**禁跨批挂账**）；⑤ `MainProj.sln` 0 错 0 警 + 烘焙副本同比特 |
| **回退** | 单提交可撤（旧件删除 + 引用新增同批） |

### 4.4 B6 —— 接线归位

| 项 | 内容 |
|---|---|
| **改动面** | L1 组合根：`LoadValidated → Compile → Assemble`；`SpecHost` 经**注入接口**取标准视图（`SpecPortStandards.Parse` 的解析源由 `PortLedger` 改标准目录投影） |
| **判据** | `SpecHost.csproj` **零新增 `DataCenter.*` 引用**；`SpecPortHostTests` 全绿 |
| **回退** | 接口回滚；若注入不可行 → 退一步「标准视图经 `TestInfrastructure` 中立契约转递」（**须评审**，上位文档 D4） |

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




