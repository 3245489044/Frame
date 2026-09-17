# DataCenter 标准框架通用化重构 —— 设计与实施（标准↔机制归位 · 口径 2）

> **定位**：以 `Frame\CsharpPrj\Strategy\Observability\Ports`（已成熟的**端口声明 / 求值框架**）为基线，把
> `Frame\DataCenter\DataCenter.Standards`（现有实现**很浅**：只认外层 `in/out/depends/scope/version/emits`，`impl` 当不透明串）
> 重构为**完整的通用标准框架**——`impl` 完整词表纳入 `DataCenter.Protocol.Standards`，
> 内核退化为「标准模型 → 指令序列」的**纯编译面**。
> **上位文档**：`DataCenter-统一标准与通用端口-设计与实施.md`（本文件是其 §3.2/§6 的**深度补强**，术语/分层沿用，不另立）。
> **快照日期**：2026-09-17（现状数字均为实测，命令可复现）。**性质**：待执行设计（未实施）。
> **红线**：① `DataCenter.Protocol.Standards` 零依赖纯契约（只 BCL）；② `DataCenter.Standards` 只引 Protocol，
> **不得引 `Strategy.Observability`**；③ 内核出现 `decision/tool/skill/llm` 等领域词汇即越界；
> ④ 标准层不得出现引擎/宿主/产品设施（反射、IO、线程）。
>
> **施行与校正（2026-09-17 补注）**：B1–B3 已落地（标准模型全集 + 词表唯一源 + 结构门 `oneOf`/`$ref` + `impl` 全量解析 + 两侧解析对拍）。
> 实测交付、判据证据与 **7 项实测校正**（`ComposedArgStandard` 第三套实参键 / `convert:"dep"` / `from` 补 `member`·`type` /
> `collect[].from` 非枚举 / `projectItem` 有意开放 / 读取器不重复判未知键 / 解析器行数两口径），以及下一阶段逐批实施标准，
> 见 `Doc\DataCenter-标准框架通用化重构-下一阶段实施计划.md`（§2 已完成 · §3 校正 · §4–§6 待执行）。
> **施工口径以该文件 §3/§5 为准**（本文件 §2.2/§4.2/§5 的相应表述已被实测校正）。

---

## §0 一句话结论

> 端口台账（`ports.json`）目前被**两处各自解析、各自校验、各自建模型**：DataCenter 侧只解到"外层 7 个键"，
> 内核侧解到"完整 impl 声明 + 执行规格"。标准层因此**表达力不足**，无法独立回答
> "这个端口怎么被装配、脚本会做什么、投影取哪些字段"；而内核则**越位拥有标准语义**（词表、形状、fail-fast 文案）。
> 本次重构把**声明词表与结构判据全部上收标准层**（`impl` 全量），内核只保留**编译与执行**——
> 一句话：**「JSON 说什么」归 DataCenter，「怎么变成可调用边界」归 Observability。**

---

## §1 现状取证（实测）

### 1.1 基线框架：内核现有能力（= 标准层要对齐的目标形态）

`Strategy\Observability\Ports\`：**43 文件 / 7,173 行**（含 `Spec\`(7 文件/1,493 行)、`Eval\`、`Value\`、`Fakes\`）。

| 能力面 | 件（实测） | 说明 |
|---|---|---|
| **台账解析** | `Spec\PortLedger.cs`(245) `Spec\PortLedgerJson.cs`(45) `Spec\PortLedgerScripted.cs`(836) | 唯一解析器；`ports[key] → PortLedgerEntry`；impl 全量字段读取 + 逐项 fail-fast（`[port:standard]` 文案） |
| **声明模型** | `PortBindingSpec.cs` `ComposedBindingSpec.cs` `ScriptedBindingSpec.cs`(300) `Spec\PortExpr.cs`(170) | 21 个 record 承载 impl 全词表（见 §2.2/§4） |
| **执行规格** | `Spec\PortSpec.cs`(88) `Spec\PortValueOptions.cs`(49) `Spec\PortSpecLedger.cs`(60) | `PortSpec{PortKey, Impl, Params, Program, IsEvaluable}`；`PortOp` 8 操作码（已实现 `Call`） |
| **唯一转换表** | `PortValueOptions`：`CommonConversions` 7 项 + `ExtendedConversions` 3 项 | 三通道转换词表归一（R2-1） |
| **求值引擎** | `Eval\PortEvaluator.cs`(76) + `Eval\Ops\*` + `Eval\Expr\*.cs`(523) | 五段流水线 `BindParams→BuildDeps→RunProgram→Project→Deliver`；表达式算子 40+ |
| **装配** | `PortAssembler.cs`(142) `PortBindingSpec/PortBindingPlan` | `Seed(json) → Bind(plans)`；`Unbound/Frozen` fail-safe 记账 |
| **目录 / 替身 / 观测** | `PortCatalog.cs`(132) `Fakes\PortFake.cs`(328) `CallLog.cs` `ObservedPortDecorator.cs` | 唯一注册点；替身 5 形态；调用记录；埋点装饰 |

### 1.2 标准层现状：浅在哪（量化）

`Frame\DataCenter\`：`DataCenter.Protocol\Standards\`（5 文件）+ `DataCenter.Standards\`（5 文件 + csproj）。

| 项 | 实测 | 判断 |
|---|---|---|
| `PortStandard` 字段 | `Key, Inputs, OutFields, Depends, Scope, Version, Emits, **Impl: string?**` | **`Impl` 只是 RawText 字符串** → 标准层对"怎么装配"零表达力 |
| `StandardJson.ReadPorts` 已认键 | `in / out / depends / scope / version / emits / impl`（7 个） | 实现侧 5 类 kind 的 21 个 record 全在 `impl` 内，**标准层未展开** |
| `schemas\ports.schema.json` | **43 行 / 1,730 B**；`impl: { "type": ["string","object"] }` | 结构校验对 `impl` 形同空转 |
| 语义判据 | `StandardConsistency` C1–C6（键唯一 / emits⊆events / carries⊆data / 跨切⊆data / producer⊆ports / 版本单调） | **无一条触及 impl 内部** |
| 转换 / 取值 / 算子词表 | 标准层 **0 处**；全部在内核（`PortValueOptions`、`ValueSource.From`、`ScriptedExpr` 40+ 算子、`ComposedField.Select`） | 词表是"标准"却住在机制层 |
| 扩展面 | `StandardSchemaValidator` 支持子集：`type/required/properties/additionalProperties/propertyNames.pattern/items/minItems/minProperties/enum/pattern` | **子集足够表达 impl 词表**（无需引第三方库） |

### 1.3 双真源实证（同一份 `ports.json` 两个解析器）

| 关注点 | `DataCenter.Standards`（L0.5） | `Strategy.Observability`（L0） |
|---|---|---|
| 读 JSON | `StandardJson.ReadPorts`（`$comment` 忽略、未知键 fail-fast、`DuplicateKeys`） | `BoundaryContractJson.Load` + `PortLedger.Parse` |
| 建模型 | `PortStandard`（8 字段，`Impl=string`） | `BoundaryContract` + `PortLedgerEntry`（19 字段，impl 全展开） |
| 校验 | `StandardSchemaValidator`（结构）+ C1–C6（语义） | `BoundaryContractRegistry.Validate`（空键/重复字段/空依赖）+ `PortLedger.NormalizeKind` + 逐项 `[port:standard]` 抛 |
| 诊断 | `StandardViolationException`（含 `Violations` 明细） | `InvalidOperationException("[port:standard] …")` |
| 消费方 | `Agent.Spec.Guard.Tests\StandardConsistencyGuardTests`、`DataCenter.Standards.Tests` | `SpecHost\SpecPortStandards`（薄适配）→ 各守卫 / 装配 |

**结论**：口径 2 的落点不是"搬代码"，而是**把内核里属于"标准"的三件东西（词表、结构判据、声明模型）搬进协议/标准层**，
内核只留"标准模型 → `PortSpec` 指令序列"的编译。这样内核 `PortLedger*`（1,126 行）退场，标准层从"8 字段"扩到"全词表"。

---


## §2 目标架构

### 2.1 三层与依赖方向

```
L0.5⁻ DataCenter.Protocol.Standards   纯契约：标准模型全集（含 impl 全词表）；零依赖（只 BCL + System.Text.Json）
                                        ▲
L0.5  DataCenter.Standards            装载 + 结构校验(schema) + 语义判据(C1–C13) + 中立目录
                                        ▲
L0    Strategy.Observability          编译(PortSpecCompiler: 标准模型 → PortInstr 序列) + 求值 + 装配 + 替身 + 观测
                                        ▲
L1    产品组合根 / L2 SpecHost         LoadValidated(ports.json) → Compile(catalog) → Assemble(plans)
```

**依赖方向**：`L0 → L0.5⁻`（纯契约，允许）、`L0.5 → L0.5⁻`（允许）、`L0.5 ⊥ L0`（禁）、`L1/L2 → {L0, L0.5}`。
**注意**：`SpecHost` 现行纪律**禁引 `DataCenter.*` 命名空间** → 标准模型须经**注入接口**进入 L2（见 §6-B6）。

### 2.2 协议层模型全集（新增：`PortImplStandard` 及子形态）

落 `DataCenter.Protocol\Standards\`（新文件，**与原 5 文件同目录同风格**）。命名统一后缀 `Standard`，与内核"执行形态"类型明确区分：

| # | 类型 | 对应内核件（基线） | 字段（实测归来） |
|---|---|---|---|
| 1 | `PortImplStandard` | `PortLedgerEntry` | `Kind, Id, Target, Method, Reason, LegacyKind, ProductFields, Args, Binds, Deps, Calls, Project, Script, ScriptProject, Deliver, Scopes, Scenarios` |
| 2 | `PortArgStandard` | `PortArgSpec` | `Convert, Optional, NullLiteral, Default, From, Key` |
| 3 | `ValueSourceStandard` | `ScriptedArgSpec`（27 字段） | `From, Index, Key, Dep, Path, Convert, Optional, Default, NullLiteral, Value, Type, GenericArgs, Scope, Name, Rows, Items, RowValue, Filter, Separator, OnBlankFirstKey, OnDuplicate, Distinct, ElementType, Construct, Named, Delegate` |
| 4 | `DepStandard` | `ComposedDepSpec` | `New, Factory, Fake, Contract, Wrap, Methods, Values, GenericArgs, Args` |
| 5 | `CallStandard` | `ComposedCallSpec` | `As, Target, Method, IsProperty, Dep, Await, Expect, Args` |
| 6 | `ComposedFieldStandard` | `ComposedFieldSpec` | `Field, Call, Select, Other, Path, Dep, Method, Key` |
| 7 | `StepStandard` | `ScriptedStep` | 判别式步骤：`Invoke / Foreach / Save / Deliver / Collects` |
| 8 | `InvokeStandard` | `ScriptedInvokeSpec` | `As, Dep, Target, Method, IsProperty, Await, Expect, Args, Out, Break, GenericArgs, Value, NonPublic` |
| 9 | `ForeachStandard` | `ScriptedForeachSpec` | `Slot, Items, As, Where, Do` |
| 10 | `NewStandard` | `ScriptedNewSpec` | `As, Type, GenericArgs, Args, Chain, Init` |
| 11 | `InitStandard` | `ScriptedInitSpec` | `Member, Value` |
| 12 | `ChainStandard` | `ScriptedChainSpec` | `Method, Args, Rebind` |
| 13 | `DelegateStandard` | `ScriptedDelegateSpec` | `Kind, Steps, Result, Parameters, Collect, Await` |
| 14 | `SaveStandard` | `ScriptedSaveSpec` | `Slot, Type, Key, GenericArgs` |
| 15 | `CollectStandard` | `ScriptedCollectSpec` | `Slot, From` |
| 16 | `ScopeStandard` | `ScriptedScopeSpec` | `Kind(tempDir/using/restore), As, Prefix, Files, Invoke, Save, Open, Close` |
| 17 | `FileStandard` | `ScriptedFileSpec` | `Name, Dir, From, Content, When` |
| 18 | `FieldExprStandard` | `ScriptedFieldSpec` | `Field, Expr` |
| 19 | `ExprStandard` | `ScriptedExpr`（40+ 算子） | `Source, Path, Count, NotNull, AsText, TypeName, Message, Thrown, Concat, Separator, Contains, Of, All, Any, AllIn, Call, CallTarget, Spread, Except, CountEqual, Collect, CollectText, SameAsLast, IsNull, Not, Eq, Ne, Gt, Ge, Lt, Le, AllOf, AnyOf, Invoke, First, FirstWhere, FirstPath, Last, LastWhere, LastPath, Dict, DictKey, DictValue, DictWhere, DictOnDuplicate, Agg, AggOp, AggPath, Filter, FilterWhere, FilterPath, SameReference, Coalesce, Distinct, SequenceEqual, Fold, FoldAs, FoldInit, FoldStep, Parts, FirstOrNull, LastOrNull` |
| 20 | `ConcatPartStandard` | `ScriptedConcatPartSpec` | `Expr, When` |
| 21 | `DeliverStandard` | `ScriptedDeliverSpec` | `Kind(value/issues/action), Defer, Issues, IssueField, Capture, DeferSteps, Value` |
| 22 | `DeliverCaptureStandard` | `ScriptedDeliverCaptureSpec` | `Call, Expect, Message, MessageField, When` |
| 23 | `ScenarioStandard` | `ScriptedScenarioSpec` | `Select, Cases`（`Cases: IReadOnlyDictionary<string, ScriptedBodyStandard>`） |

**技术约束（实测）**：
- `Default / Value / Values` 位沿用 `System.Text.Json.JsonElement?`（BCL 内置；`DataCenter.Standards` 已在用 `System.Text.Json`）。
- **递归结构必须支持**：`ExprStandard` 自引用；`DeliverStandard.DeferSteps → StepStandard → InvokeStandard.Value → ValueSourceStandard → DelegateStandard → StepStandard`。现状 `StandardSchemaValidator.Walk` 为递归下钻，具备承载能力。
- `PortStandard.Impl` 由 `string?` 改为 **`PortImplStandard?`**（破坏性变更；消费点仅 `StandardJson`/`StandardCatalog`，见 §6-B4）。

### 2.3 装载与校验（标准层扩面）

| 件 | 现状 | 目标 |
|---|---|---|
| `StandardJson` | `Impl()` 取 RawText | `ReadImpl()`：按 kind 分派读全词表；未知键 **fail-fast**，context 逐层（如 `ports[X].impl.calls[0].args[1].convert`） |
| `StandardCatalog` | 四视图 | 四视图（`PortStandard.Impl` 已内嵌）+ `DuplicateKeys` 全段覆盖 |
| `StandardSchemaValidator` | 子集校验 | **不变**（子集已够）；扩 `schemas\ports.schema.json` 的 impl 段 |
| `StandardConsistency` | C1–C6 | **新增 C7–C13**（见 §4.3） |

### 2.4 内核编译面（`PortLedger` 退场）

| 现状 | 目标 |
|---|---|
| `PortLedger.Parse(json)` → `PortLedgerEntry` | **删除**（1,126 行：`PortLedger` / `PortLedgerJson` / `PortLedgerScriptedReader`） |
| `PortSpecLedger.Parse(json)` / `FromReflective(spec)` | 改为 `PortSpecCompiler.Compile(PortStandard)` / `FromReflective(spec)`（后者供程序内反射声明保留） |
| 声明形状校验（`[port:standard]` 抛） | **上收标准层**（结构判据 + 语义判据）；内核只保留**编译期不可求值**判定（`IsEvaluable=false`，不抛） |
| `PortValueOptions` 词表 | **保留为执行词典**（编译期把 `convert` 字符串解析为执行分支）；词表**校验**移标准层 |
| 三条旧绑定通道（`Reflective/Composed/Scripted Binding`） | 按既有 R3 路线**继续退场**（与本重构解耦，见 §6 A 线） |

---

## §3 能力迁移映射（逐件归属）

**归属判据**：只要该件回答的是「**JSON 里写了什么 / 是否合法**」→ 归 `DataCenter`；回答「**怎么把它变成可调用边界**」→ 留内核。

| 内核现件（实测行数） | 内容性质 | 归属 | 动作 |
|---|---|---|---|
| `Spec\PortLedgerJson.cs`(45) | JSON 取值原语 | **DataCenter.Standards** | 迁（并入 `StandardJson` 的读取原语区） |
| `Spec\PortLedger.cs`(245) | 台账外层 + impl 读取 + 形状 fail-fast | **DataCenter.Standards** | 迁（外层 → `ReadPorts`；impl → `ReadImpl`） |
| `Spec\PortLedgerScripted.cs`(836) | 脚本族声明读取（~35 方法） | **DataCenter.Standards** | 迁（`ReadImpl` 的 scripted 分派） |
| `PortBindingSpec.cs` 中 `PortArgSpec`(28) | 实参声明形状 | **Protocol.Standards** | 迁为 `PortArgStandard` |
| `ComposedBindingSpec.cs` 中 `ComposedArgSpec`/`ComposedDepSpec`/`ComposedCallSpec`/`ComposedFieldSpec` | 组合声明形状 | **Protocol.Standards** | 迁为 `PortArgStandard`(复用)/`DepStandard`/`CallStandard`/`ComposedFieldStandard` |
| `ScriptedBindingSpec.cs`(300) 全 14 类型 | 脚本声明形状 | **Protocol.Standards** | 迁为对应 `*Standard` |
| `Spec\PortExpr.cs`(170) 全 5 类型 | 表达式声明形状 | **Protocol.Standards** | 迁为 `ExprStandard`/`ConcatPartStandard`/`FieldExprStandard`/`DeliverStandard`/`DeliverCaptureStandard` |
| `PortBindingSpec.cs` 中 `PortBindingSpec`(类) | 程序内反射声明载体 | **内核保留** | 保留（非 JSON 面；`FromReflective` 消费） |
| `ComposedBindingSpec.cs` 中 `ComposedBindingSpec`(类) | 组合绑定执行载体 | **内核保留**（R3 退场目标） | 保留至 R3 收口 |
| `ScriptedBindingSpec.cs` 中 `ScriptedBindingSpec`(类) | 脚本绑定执行载体 | **内核保留**（R3 退场目标） | 保留至 R3 收口 |
| `Spec\PortSpec.cs`(88) `PortOp`/`PortInstr`/`PortImplRef`/`PortSpec` | 执行规格（指令序列） | **内核保留** | 不变（编译产物） |
| `Spec\PortValueOptions.cs`(49) | 执行词典（转换分支解析 + 形参位） | **内核保留** | 保留；词表**校验**移标准层 |
| `Spec\PortSpecLedger.cs`(60) | 声明 → 规格下行 | **内核改造** | 改为 `PortSpecCompiler`（输入 `PortStandard`） |
| `Eval\**` `Value\**` `Fakes\**` `PortCatalog` `PortAssembler` `ObservedPortDecorator` `CallLog` | 求值 / 替身 / 装配 / 观测 | **内核保留** | 不变 |
| `Boundary\BoundaryContractJson*.cs`（3 文件） | 契约 JSON 解析 | **DataCenter.Standards** | 退场；改为「`PortStandard`/`DataStandardItem`/`EventStandard` → `BoundaryContract`」**纯投影** |
| `Boundary\BoundaryContractRegistry.Validate` | 契约声明形状校验 | **DataCenter.Standards** | 退场上收（schema + 判据）；内核只保 `EnsurePortKind` 机制前置 |
| `Diagnostics\ObservabilityExceptions.cs` 中 12 个端口/边界异常 | 端口/边界诊断 | **保留内核**（装配/调用期）+ **标准层诊断**（装载期 `StandardViolationException`） | 分工：装载期用标准层，运行期用内核 |

## §4 词表与判据总表（标准层要机器判定的全部内容）

### 4.1 impl.kind 词表（5）

`delegate`（缺省 / 简写字符串）｜`reflective`｜`composed`｜`scripted`｜`frozen`
> 现由内核 `NormalizeKind` 判定（未知即抛）；目标由标准层 C7 判定，内核编译期只做"已知 kind → 指令序列"分派。

### 4.2 取值 / 算子词表（现散在内核，目标入标准层）

| 词表 | 取值 | 现权威 | 目标位置 |
|---|---|---|---|
| `convert` | `string` `bool` `int` `double` `csv` `json` `object`（共同）+ `array` `graph` `passthrough`（脚本特有） | `PortValueOptions.Common/ExtendedConversions` | 标准层 C8 + schema `enum`（内核保留执行分支） |
| 取值来源 `from` | `arg` `with` `item` `var` `dep` `literal`（+ 值源扩展：`tmpPath` `tmpDir` `table` `presence` `delegate` `new` `row`） | `ScriptedArgSpec.From` 语义 | 标准层 C8（schema `enum`） |
| 组合投影 `select` | `value` `count` `notNull` `text` `countEqual` `path` `pathText` `message` `thrown` `fakeCount` | `ComposedFieldSpec.Select` | 标准层 C9 |
| 表达式算子 | 40+（`count/text/path/agg/dict/first/last/contains/all/any/spread/collect/fold/distinct/…`） | `ScriptedExpr` 成员 + `ScriptedExprEvaluator` | 标准层 C10（成员唯一性 + 算子名登记） |
| 载体 `deliver.kind` | `value` `issues` `action`（+ 旧形态布尔 `defer`/`issues`） | `ScriptedDeliverSpec.Kind` | 标准层 C11 |
| 依赖形态 | `new` `factory` `fake` 三选一 | `PortLedger.ReadDeps` | 标准层 C12 |
| 替身 `fake` | `recording` `counting` `fixed` `sequence` `notExpected`（+ 代理 `proxy`） | `ComposedDepSpec.Fake` + `FakeKind` | 标准层 C12 |
| 作用域 `scope.kind` | `tempDir` `using` `restore` | `ScriptedScopeSpec.Kind` | 标准层 C11 |
| 表处置 | `onBlankFirstKey: fail\|skip`、`onDuplicate: fail\|last` | `ScriptedArgSpec` | 标准层 C8 |

### 4.3 语义判据（在 C1–C6 基础上新增 C7–C13）

| # | 判据 | 依据（内核现状） |
|---|---|---|
| C1–C6 | 既有：键唯一 / `ports.emits ⊆ events` / `events.carries ⊆ data` / 跨切字段 ⊆ data / `data.producer ⊆ ports ∪ 白名单` / 版本单调 | `StandardConsistency` 现实现 |
| **C7** | `impl.kind ∈ 词表` ∧ `kind=reflective → target ∧ method 非空` ∧ `kind=delegate → id 非空` ∧ `kind=frozen → reason 非空` | `PortLedger.NormalizeKind` + `PortSpecLedger.Reflective` |
| **C8** | 实参 / 取值源：`convert ∈ 词表`；`from ∈ 词表`；`arg`/`item` 须带 `index`；`with`/`var` 须带 `key`；`dep` 须带 `dep`；`literal` 须带 `value`；`table`/`presence` 须带 `rows` | `PortLedger.ReadComposedArgs` + `PortLedgerScriptedReader.ReadArg` |
| **C9** | 组合：`deps[i]` 形态三选一；`deps[i].args` 的 `dep` 下标 **< i**（只引更早）；`calls[j].as` 唯一且 `project[*].call ∈ calls[*].as`；`project[*].select ∈ 词表` | `PortLedger.ReadDeps/ReadCalls/ReadProject` |
| **C10** | 脚本：`script[*]` 步骤形态恰一个；槽名（`as`/`var`）在引用前已写入；`foreach.do` 非空；表达式算子**恰一个**成员；`collect` 槽已声明 | `ScriptedBinding` 运行期校验 + `ScriptedDeclarationGuardTests` |
| **C11** | 载体：`deliver.kind` 互斥（`value`/`capture`/`deferSteps` 不同时给）；`scope.kind ∈ 词表`；`tmpPath` 须有对应 `scopes[].as` | `ScriptedDeliverSpec` + `ScriptedScopeSpec` |
| **C12** | 依赖 / 替身：`deps[i]` 形态与字段一致（`fake` 时才可有 `contract/wrap/methods/values`）；`fake ∈ 词表` | `ComposedDepSpec` 注释 + `PortFake` |
| **C13** | 投影对账：`project[*].field ⊆ ports[key].out.fields`；脚本投影字段序与契约字段序一致 | `FieldContractValidator`（内核现为**运行期**对账；标准层做**装载期**对账） |

### 4.4 判据口径

> 判据**只此一处**、失败即阻断（`StandardViolationException` 一次报全）；数据不合规改数据、**禁改判据**。
> 运行期仍需要的机制性检查（如"未登记即抛"）保留在内核，但**不再重复表达标准语义**。

## §5 schema 扩面（`schemas\ports.schema.json`）

现状 43 行只覆盖外层；目标**把 impl 词表写进 schema**（结构层先挡，语义判据 C7–C13 后拦）：

```jsonc
"impl": {
  "oneOf": [
    { "type": "string", "enum": ["delegate", "reflective", "composed", "scripted", "frozen"] },
    { "type": "object",
      "properties": {
        "kind": { "enum": ["delegate", "reflective", "composed", "scripted", "frozen"] },
        "id": { "type": "string" }, "target": { "type": "string" }, "method": { "type": "string" },
        "reason": { "type": "string" }, "legacyKind": { "type": "string" },
        "productFields": { "type": "string" },
        "args":   { "type": "array", "items": { "$ref": "#/$defs/arg" } },
        "binds":  { "type": "object", "additionalProperties": { "$ref": "#/$defs/valueSource" } },
        "deps":   { "type": "array", "items": { "$ref": "#/$defs/dep" } },
        "calls":  { "type": "array", "items": { "$ref": "#/$defs/call" } },
        "project":{ "type": "array", "items": { "$ref": "#/$defs/fieldOrExpr" } },
        "script": { "type": "array", "items": { "$ref": "#/$defs/step" } },
        "deliver":{ "$ref": "#/$defs/deliver" },
        "scopes": { "type": "array", "items": { "$ref": "#/$defs/scope" } },
        "scenarios": { "$ref": "#/$defs/scenario" }
      },
      "additionalProperties": false }
  ]
}
```

**约束**：`StandardSchemaValidator` 现仅支持子集（`type/required/properties/additionalProperties/propertyNames.pattern/items/enum/pattern/minItems/minProperties`）——
上例未引入 `oneOf`/`$ref` **以外的**新关键字，但这两者**当前未实现**，需在 B2 批扩两条 Walk 分支（约 40 行，仍不引第三方库）。

## §6 分批实施（每批：独立可编译 + 独立可回退 + 判据可执行）

### A 线（前置，与既有 R 系列收尾并联，避免双线交叉）

| 批 | 内容 | 判据 |
|---|---|---|
| **A1** | R3-2/R3-3 算子迁移：`Dep`/`New`/`Project`/`Loop`/`Table`/`Presence`/`Collect` 落 `Eval\Ops\`，三条旧绑定通道退场 | 通道文件删除或 <30 行薄壳；`switch (Kind)` = 0；`E22ScriptedBindingTests` 全绿 |
| **A2** | R5-1/R5-2 装配收敛：`PortAssembler`+`PortBindingPlan` → `PortComposer`+`PortBinding{PortKey,PortSpec,PortImplRef}`；上层三处计划构造合一 | 内核 `switch (Kind)`=0；计划构造点 grep = 1；`Unbound/Frozen` 逐键与现基线一致 |
| **A3** | R6-2 守卫收口：五个声明守卫改读单一解析结果 | 守卫数不减；失败信息仍可定位到键 |

### B 线（本重构主体）

| 批 | 内容 | 改动面 | 判据 | 回退 |
|---|---|---|---|---|
| **B1** | `Protocol.Standards` 新增 23 个标准模型（§2.2） | `DataCenter.Protocol`（纯新增） | `DataCenter.slnx` 0 错 0 警 | 删文件 |
| **B2** | `StandardSchemaValidator` 支持 `$ref`/`oneOf`；`schemas\ports.schema.json` 扩至 impl 全量 | `DataCenter.Standards` + `schemas\` | 结构校验用例（合法/非法各 ≥5 例）全绿 | 还原 schema |
| **B3** | `StandardJson.ReadImpl` 实现（~35 方法从内核迁入）；`PortStandard.Impl` → `PortImplStandard?` | `DataCenter.Standards` + `Protocol.Standards` | `StandardConsistencyTests` 全绿 + 新增 impl 解析用例 | 类型回滚 |
| **B4** | `StandardConsistency` 新增 C7–C13 | 同上 | 每条判据 ≥2 用例（1 绿 1 红），错误文案含判据号 | 删判据方法 |
| **B5** | 内核退场重复解析：删 `PortLedger*`(1,126 行)；`BoundaryContractJson` 改为纯投影；`PortSpecLedger` → `PortSpecCompiler`（入参 `PortStandard`） | `Strategy.Observability` | 内核自证全绿；`JsonDocument.Parse` 在 `Ports\**` 命中 = 0；`Ports\` 行数单调下降 ≥1,000 行 | 单提交可撤 |
| **B6** | 接线归位：L1 组合根 `LoadValidated → Compile → Assemble`；SpecHost 经**注入接口**取标准视图（禁引 `DataCenter.*`） | 组合根 + `SpecHost` | `SpecHost.csproj` 零新增 DataCenter 引用；`SpecPortHostTests` 全绿 | 接口回滚 |
| **B7** | 诊断口径归一：装载期一律 `StandardViolationException`（含判据号）；运行期保留内核异常 | 双方 | 同一份坏台账**只报一次**、带判据号；既有守卫失败信息不退化 | 文案回滚 |
| **B8** | 守卫 / 清单 / 文档同步：`PortKernelDisciplineGuardTests` 预算下调、`KernelRedLineTests` 公开面清单、`CsharpBakeRefs.props`、`DataCenter.slnx`、两侧 README/ModuleRule | 守卫 + 清单 + 文档 | 全仓 `MainProj.sln` 与 `DataCenter.slnx` 0 错 0 警；守卫全绿；README 与实际 100% 对齐 | 逐项可撤 |

**批次依赖**：`B1 → B2 → B3 → B4 → B5 → B6 → B7 → B8`；**B5 必须在 A2 之后**（A2 会重写 `Ports\Spec` 与装配面）。

## §7 风险与裁决项

| # | 风险 / 裁决 | 处置 |
|---|---|---|
| D1 | `PortStandard.Impl` 类型变更破坏兼容 | 消费点实测仅 `StandardJson`/`StandardCatalog`（`FindPort` 不改语义）；一次性改，不设双读 |
| D2 | 标准模型与内核执行模型**短期同名并存**（如 `ScriptedStep` vs `StepStandard`） | 命名后缀硬区隔；B5 后内核 JSON 声明类型退场，并存期仅 1 批 |
| D3 | 迁移期 **1,126 行解析器**双份（B3 后内核仍在用旧件到 B5） | 允许双轨 **仅跨 B3–B5 两批**；以"两解析器对拍"用例锁等价，B5 同批删旧件（禁跨批挂账） |
| D4 | `SpecHost` 禁引 `DataCenter.*` | B6 用注入接口（L1 侧实现投影）；若无法注入，则退一步：标准视图经 `TestInfrastructure` 中立契约转递（**须评审**） |
| D5 | `$ref`/`oneOf` 需扩 schema 校验器 | 仅扩两条分支，仍零第三方依赖；不支持则退化为"`impl` 内嵌 properties 展开"（体积换能力） |
| D6 | 判据 C7–C13 与既有 5 个声明守卫**语义重叠** | 守卫改读标准层判据结果（与 A3 同批思路），**禁两处各判一遍** |
| D7 | 与 A 线交叉 | B5 严格排在 A2 之后；A3 与 B4 可并行（改面不重叠） |
| D8 | DataCenter 输出路径与烘焙目录双轨 | `DataCenter.Protocol` 已在 `CsharpBakeRefs.props`（L62）；新增引用统一走烘焙清单，避免 HintPath 分叉 |

## §8 不做

- 不引第三方 JSON Schema 库；不引 OTel / Prometheus。
- 不把**执行**搬进 DataCenter（无反射、无 IO、无线程、无求值）。
- 不改 `.specs` 书写语法；不改端口键命名规范；不动产品 `ports.json` 的 10 键面语义。
- 不改 CSG 主 schema（R-DC1：只扩不改）。
- 不在标准层出现领域词汇（`decision`/`tool`/`skill`/`llm`/`loop`）。
- 不删既有公开 API 语义（可回退原则）：内核 `PortBindingSpec`/`ComposedBindingSpec`/`ScriptedBindingSpec` 三个"执行载体类"保留至 A1/R3 收口。

## §9 验收（可执行）

```powershell
cd D:\Project\Main\Frame\DataCenter
dotnet build DataCenter.slnx                       # 期望 0 错 0 警
dotnet build ..\CsharpPrj\Strategy\Observability\Observability.csproj   # 内核独立编译
cd D:\Project\Main\Frame\CsharpPrj
dotnet build MainProj.sln                          # 聚合回归

# 判据（示例）
# ① 标准层表达力：PortStandard.Impl 为结构化模型（非 string）
# ② 单一解析：Ports\** 内 JsonDocument.Parse 命中 = 0
# ③ 判据齐备：StandardConsistency.Check 覆盖 C1–C13
# ④ 体积：Ports\ 行数 ≤ 基线 - 1,000
# ⑤ 守卫：Agent.Spec.Guard.Tests 全绿（含 StandardConsistencyGuardTests）
# ⑥ 等价：迁移期对拍（新标准解析 vs 旧内核解析）逐键一致
```

## §10 变更记录

| 日期 | 版本 | 内容 |
|---|---|---|
| 2026-09-17 | v1 | 首版：以 `Strategy.Observability\Ports` 成熟框架为基线，确立口径 2（impl 全词表入 `DataCenter.Protocol.Standards`，内核只做「标准→指令序列」编译）；含现状取证、23 类型目标模型、逐件迁移映射、9 张词表、C7–C13 判据、B1–B8 批次与验收 |


