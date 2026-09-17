# DataCenter 标准单源化 —— 重复逻辑收敛 · 设计与实施

> 版本：v1.0（2026-09-17）
> 范围：`Frame\DataCenter` × `Frame\CsharpPrj\Strategy\Observability` 两侧的**重复 / 冗余 / 错误逻辑**审计结论，
> 与「**DataCenter 侧先建通用框架、内核只做消费**」的收敛路线。
> 纪律：本文档只写「怎么收敛」，不重写已有文档已覆盖的机制搬迁方案（分工见 §0.3）。

---

## §0 结论与定位

### 0.1 一句话结论

> 两侧的关系**不是**"双实现竞品"，而是"**声明面（DataCenter 标准层）× 执行面（Observability 内核）**"的既定分层
> （`Observability.csproj:32` 已单向引用 `DataCenter.Protocol`）。真正的问题在于：**这条分层的"单一真源"只落实到了
> 程序集依赖，没有落实到词表 / 键闭包 / 模型 / 判据四处**——
> 于是同一份 `impl` 声明在内存中被表达 4 次、词表副本 3~4 份、键白名单 3 族、"产物字段对账"这条规则写了 4 遍，
> 且已有 **3 处实质漂移**（`fakes` 词表、`convert` 文案、装载期异常类型）。
>
> 收敛方向：**在 DataCenter 侧把"词表 / 键闭包 / 结构门 / 判据 / 诊断"建成通用框架件（且优先建、先自证），
> 再让内核与测试基建改为消费**；内核**执行分支保留、语义与文案基线不动**。

### 0.2 审计基线（本次取证快照）

| 侧 | 位置 | 规模（行） | 角色 |
|---|---|---|---|
| 标准契约 | `DataCenter.Protocol\Standards\`（24 个 `*Standard` + `PortImplVocabulary` 4 partial） | — | 声明模型 + **词表唯一 C# 源（自称）** |
| 标准机制 | `DataCenter.Standards\`（9 文件） | **2,220**（Scripted 读 832 / ImplConsistency 390 / SchemaValidator 203 / StandardJson 221 / Impl 189 / Consistency 137 / Catalog 80 / Primitives 52 / Exception 16） | 读取 + 结构门 + 判据 C1–C13 + 目录 |
| 结构门 | `DataCenter\schemas\*.schema.json`（ports 491 行 / 17 个 `enum`） | — | JSON Schema 闭合 |
| 内核声明 | `CsharpPrj\Strategy\Observability\Ports\**` | 约 **6.1k**（口径见《下一阶段实施计划》v1.5 注记） | 绑定声明 + 求值引擎 + 装配 |
| 中立 DTO | `Environment\CentralTest\SpecHost\SpecPortStandard.Dto.cs` + `DtoBinding.cs` | — | 第二份镜像 + 第二个映射器 |
| 投影适配 | `Environment\CentralTest\AgentTest\Agent.TestShared\Fixtures\Common\AgentPortStandardProjection.cs` | — | 第三个映射器 |

### 0.3 与既有文档的关系（禁重复维护）

| 文档 | 覆盖 | 本文档的关系 |
|---|---|---|
| `DataCenter-声明式框架提取-框架优先设计与实施.md`（v1.3） | 台账治理机制搬迁到零依赖框架 `BasicMethod.Declarative.Ledger`（判据宿主 / 键控目录 / 判据接口）；JSON 机制（读取原语 / schema 子集校验 / 重复键扫描）归平台框架 `Plate.JsonMethod`，由使用端直接复用，分批 F1–F3 | **本文档消费其 F1 产出**（件 E 复用 `ILedgerRule`），不重述搬迁清单 |
| `DataCenter-标准框架通用化重构-下一阶段实施计划.md`（v1.3） | B 线/A 线已交付内容、§9 执行明细 | 本文档 **P3 与 M3(B7) 合流**（诊断口径），不另立方案 |
| `DataCenter-标准框架通用化重构-下一步执行清单.md`（v1.0；其中 **M2 已交付 v1.5**） | M2（Boundary 收尾，已交付）/ M3（B7 诊断口径）/ M4（收口） | 本文档 **P0/P1/P2 为 M3/M4 的前置**（M3 依赖 P0 的异常类型统一；M4 依赖 P1/P2 的棘轮目标值） |
| `Observability内核审查报告.md`（无日期，**部分已过期**） | 内核侧 3.1/3.2 已修复、1.4 半修 | 本文档 §2-U6/§3 覆盖其**未过期**项；建议该报告补日期与状态列 |

---

## §1 重复逻辑审计（D 组）

| # | 重复对象 | 副本数 | 证据 |
|---|---|---|---|
| **D1** | 同一 `impl` 声明树的**内存模型** | **4** | 标准 `PortImplStandard` 子树 / SpecHost `Dto*`（`SpecPortStandard.Dto.cs`，注释自称"镜像"）/ 内核 `*Spec.cs` / 内核 `PortSpec`（`Ports\Spec\PortSpec.cs`） |
| **D2** | 上述模型间的**手写映射器** | **3** | `SpecHost\DtoBinding.cs:13-45,105-184`（Dto→内核）/ `Agent.TestShared\...\AgentPortStandardProjection.cs:36-206`（标准→Dto）/ `Ports\Spec\PortSpecCompiler.cs:57-91`（标准→内核）；**全部按位构造**，字段顺序漂移 = 静默错位 |
| **D3** | `impl.kind` 词表 | **4** | 源 `PortImplVocabulary.cs:15-19`；schema `ports.schema.json:35,52`；归一 `StandardJson.Impl.cs:53-60`；分支 `StandardImplConsistency.cs:50-64,101,145,173` + `PortSpecCompiler.cs:39` + `PortBinding.cs:40-91` |
| **D4** | `convert` 词表 | **4+** | 源 `PortImplVocabulary.cs:22-35`；schema `ports.schema.json:77,101,156,419`；内核分支 `ScriptedValue.cs:290-343`、`ComposedArgs.cs:146-172`、`Call.cs:90`、`PortValues.cs:471-474`；**异常文案又各写一遍且已漂移**（见 E4） |
| **D5** | `from` / `select` / `scope.kind` / `fake` 词表 | 3~5 | 源 `PortImplVocabulary.cs:38-73`；schema `ports.schema.json:92,197,316,140`（+`fakes.schema.json:15`）；内核 `ScriptedValue.cs:23-100`、`Eval\Ops\Project.cs:17-72`、`ScriptedBinding.cs:273`、`Eval\Ops\ComposedDeps.cs:65-84`、`Fakes\PortFake.cs:11-33` |
| **D6** | 键白名单 | **3 族** | 台账级 `StandardJson.cs:15-23`（私有数组）/ impl 级 `PortImplVocabulary.Keys*.cs`（22 张）/ schema `additionalProperties:false` |
| **D7** | 「产物字段 ⊆ 声明字段」规则 | **4** | `StandardImplConsistency.cs:240-255`(C13) / `Projection\FieldContractValidator.cs:20-43`（内核）/ `Agent.Spec.Guard.Tests\FieldContractGuardTests.cs:34-63` + `ComposedDeclarationGuardTests.cs` + `ScriptedDeclarationGuardTests.cs:171-180,370-395` / `TestInfrastructure\DataDriven\SpecKindContract.ValidateProductFields` |
| **D8** | 台账内部事实双源 | 2 | `out.fields` ≡ `impl.productFields`（`FieldContractGuardTests.cs:15,28-31` 注释自认"台账双源…静态对拍"）：同源事实写两遍、靠守卫兜底 |
| **D9** | 事件契约三形态 + 双向转换 + 两处校验 | 3 | `EventStandard` / `EventContract`（`Registry\EventContract.cs`）/ `BoundaryContract`（`Boundary\BoundaryContract.cs`）；转换 `Registry\BoundaryEventContractBridge.cs:12-49`；校验 `StandardConsistency`(C1–C6) ∥ `ObservabilityContractRegistry.Validate` |
| **D10** | JSON 读取原语 | 2 组 | `StandardJson.cs:214-255`（Text/Number/Strings）vs `StandardJson.Primitives.cs:16-59`（Str/Int/Bool/StrList）；后者注释自认"迁移期不得互相替换" |
| **D11** | 同一 payload 在校验内解析 | 2 次/调用 | `ObservabilityContractRegistry.Validate`：`:99`（Required）+ `:137`（Enums）；旧报告 1.4 仅半修 |
| **D12** | 同程序集同名异义类型 | 2 | `Share.HierarchySupport.PortSpec`（`HierarchySupport\PortSpec.cs:9`）vs `Strategy.Observability.Ports.PortSpec`（`Spec\PortSpec.cs:95`）；同模式还有 `IObservabilitySink`（旧报告 1.1，未消除） |

---

## §2 冗余逻辑审计（U 组）

| # | 位置 | 冗余内容 | 证据 |
|---|---|---|---|
| **U1** | `PortImplVocabulary.Keys*.cs` / `KeysOf`（`Keys.Expr.cs:31-56`） | 22 张键白名单 + `KeysOf` **无运行时消费方**，唯一消费者是 `StandardImplSchemaTests.cs:151-170`；类注释称"读取器据此 fail-fast"与事实不符 | 全仓 grep：仅测试命中 |
| **U2** | `fakes` 台账全链 | `ReadFakes`→`_fakes`→`FindFake`（`StandardCatalog.cs:71,98`）**无任何调用方**（仅 C1 查重 + C6 版本比对用键/版本）→ 死台账，且其 schema 已漂移（E1） | `FindFake` 消费方 = 0 |
| **U3** | `Ports\Spec\PortSpec.cs:12-37` | `PortOp` 8 枚操作码仅 `Call` 有生产者（`PortSpecCompiler.cs:86-88`），Dep/New/Project/Loop/Table/Presence/Collect **永不为实例值** | 无构造点 |
| **U4** | `Ports\Spec\PortValueOptions.cs:41-42` | `IsKnownConversion` 纯转发壳（本体在 `PortImplVocabulary.IsKnownConversion`），**无运行时调用方** | grep 仅定义处 |
| **U5** | `DataCenter.Standards` 两组同义原语 | D10；`Strings`（收裸字符串）与 `StrList`（不收）分歧被有意保留 | 文件头注释 |
| **U6** | `Observability内核审查报告.md` | 无日期/状态列，3.1、3.2 已修复仍列"高优先"，误导排期 | `ObservabilityQuery.cs:54` 已用 `fromUtc`；`ObservabilityContractRegistry.cs:112-130` 已大小写不敏感 |
| **U7** | `Ports\Fakes\PortFake.cs:31-32` | `Proxy` 与 `Counting` 同义双词表（注释自认"保留双词表以兼容台账用语"）——若无台账实例使用 `proxy`（见 E2）应删其一 | 见 E2 |

---

## §3 错误逻辑审计（E 组）

### E1 `fakes` 词表三份互不相等（明确缺陷）

| 来源 | 取值 |
|---|---|
| `PortImplVocabulary.cs:72-73`（源） | recording, **counting**, fixed, sequence, notExpected, **proxy** |
| `ports.schema.json:140`（`deps.fake`） | 同上 6 个 ✔ 一致 |
| `fakes.schema.json:15`（`fakes[].kind`） | recording, fixed, **echo**, notExpected, sequence ✗（`echo` 幽灵 / 缺 `counting`、`proxy`） |
| `FakeStandard.cs:5` 注释 | 再抄一遍 `echo` |
| `Ports\Fakes\PortFake.cs:11-33` 枚举 `FakeKind` | 7 成员（含 `Echo`、`Proxy`） |

### E2 `deps.fake="proxy"` 被 schema 放行、运行期必炸（假阳性放行）

`ports.schema.json:140` 与源词表均允许 `proxy`，但 `ComposedDeps.BuildFake`（`:65-84`）只实现 5 种，其余直接：

```
端口 '…' 的 dep[i] fake 种类 'proxy' 未知（recording|counting|fixed|sequence|notExpected）
```

→ schema 放行 ∧ 执行器无分支 ∧ 异常文案所列 = 5 种（与源词表 6 种矛盾）。
**反向**：`fakes.json` 声明 `counting` 会被 `fakes.schema.json` 拒（合法值被拦）。

### E3 装载期异常类型不统一（判据口径破线）

`DataCenter.Standards` 内 `throw new InvalidOperationException` **32 处** vs `StandardViolationException` **12 处**；
32 处全部集中在 `StandardJson.Impl.Scripted.cs`（`:58, :82, :115, :118, :121, :140, :174, :204, :244, :260, :267, :280, :300, :305, :322, :340, :372, :398, :406, :439, :531, :599, :627, :631, :753, :757, :763, :767, :868, :917, :946, :957`）。
违反本层总则「装载期一律 `StandardViolationException`（一次报全，带判据号）」→ 调用方 `catch` 漏接；
"一次报全"被绕过（个别形态抛得早）。**与 M3(B7) 同目标，是关键路径。**

### E4 `convert` 异常文案已漂移（源词表外的第 4 份副本）

| 位置 | 文案所列 | 与源词表差异 |
|---|---|---|
| `ScriptedValue.cs:339-342` | `string\|bool\|int\|double\|csv\|json\|array\|object\|passthrough` | **漏 `graph`**（`:303` 实际支持）、漏 `dep` |
| `ComposedArgs.cs:172` | `string\|bool\|int\|double\|csv\|json\|object` | 漏 `array/graph/passthrough/dep` |
| `Eval\Ops\Project.cs:71` | `value\|count\|notNull\|text\|message\|thrown\|countEqual\|path\|pathText\|fakeCount` | 顺序与 `Selects`（`PortImplVocabulary.cs:59`）不一致 |

### E5 结构门（schema）未接线到产品装载路径

`StandardSchemaValidator` 全仓引用**只在测试**（`StandardConsistencyGuardTests.cs:73`、`StandardConsistencyTests.cs:48,174`、
`StandardImplSchemaTests.cs:190,197`）；生产路径 `AgentPortCatalog.cs:84` → `StandardCatalog.LoadValidated` 只跑 C1–C13。后果：

1. impl 层「未知键 fail-fast」（U1 所描述的能力）**运行期不存在**（`StandardJson.Impl.cs:10-12` 明确把闭包推给 schema）；
2. 全部 `enum` 闭合只是测试期约束；
3. `StandardImplConsistency.cs:15-18` 的「结构门单一承担枚举与未知键」分工声明与运行期事实不符。

### E6 `scopes[].kind` 装载期零校验，校验后置到装配期

`StandardJson.Impl.Scripted.cs:80`：`var kind = Str(item, "kind") ?? "tempDir";`（任意串放行）；
`StandardImplConsistency.cs:167-169` 注释称"由结构门守"（E5 未接线）→ 最终由内核 `ScriptedBinding.cs:273` 才拦
（`is not ("tempDir" or "using" or "restore")`）。错误定位从装载期退到运行期。

### E7 C8 覆盖面与运行期 fail-fast 不对等

`StandardImplConsistency.CheckC8`（`:73-91`）只判 `arg/with/var/dep` 的伴生键；运行期还覆盖
`tmpPath` 缺 name（`ScriptedValue.cs:161-165`）、`table` 缺 rows、`presence` 缺 items、`delegate` 未接线等。
同一规则两处表达且覆盖面不同 → "标准层放行、运行期炸"。

### E8 kind 择路判定源未单源化

`PortSpecCompiler.Compile`（`:39-42`）只把 `reflective` 下行（其余族空 `Program` + `IsEvaluable=false`），
`PortBinding.TryConstruct`（`:36-92`）再按 kind 择路到旧绑定类。判据分散在**两个文件的两套 if/else**，
且都以硬编码字符串为准（不与 `IsKnownKind` 同源）。当前行为正确，属 E5 同类风险。

---

## §4 目标与原则

### 4.1 目标（可判定）

| 目标 | 判据（crit） | 现状 → 目标 |
|---|---|---|
| **G1 词表单源** | 全仓 `convert/from/select/fake/scope.kind/kind` 词表 **定义点 = 1**（`PortImplVocabulary`）；schema 内 `enum` 为**生成物**（由源渲染，禁手改） | 3~4 份 → 1 源 + 1 生成物 |
| **G2 键闭包单源** | 未知键 fail-fast **实现点 = 1**（`PortImplVocabulary.KeysOf` 派生的判据函数），被 ① 装载期读取器 ② schema 生成 ③ 守卫测试**三处同一实现**消费 | 3 族且运行期缺席 → 1 实现 + 3 消费方 |
| **G3 结构门接线** | `LoadValidated` 默认跑 schema 校验；产品装载路径 `JsonDocument.Parse`/schema 校验命中 ≥ 1（不再是测试专属） | 测试专属 → 装载期默认 |
| **G4 模型镜像收敛** | 同一 `impl` 的内存表达 ≤ 2（标准 + 内核执行）；DTO 镜像要么**生成**，要么**加字段面守卫**（不许裸的手写按位映射） | 4 份 → ≤2 份（+生成物） |
| **G5 判据单源** | 「产物字段 ⊆ 声明字段」实现点 = 1（框架 `ILedgerRule` 实现），内核/测试改为**调用**；`out.fields` 与 `productFields` 双源消除或改派生 | 4 处 → 1 处 |
| **G6 诊断单源** | 装载期异常类型 `= StandardViolationException`（`InvalidOperationException` 命中 = 0）；文案词表段由源渲染 | 32 处例外 → 0 |

> 全部目标均满足 `crit:macro` 形式（可机械复算：grep 计数 / 定义点计数 / 构建与测试命令），不使用"无界全称命题"作门。

### 4.2 原则（DataCenter 优先）

1. **框架优先（DataCenter 侧）**：先在 `DataCenter.Protocol` + `DataCenter.Standards` 内把通用件建成**单一源 + 自证**，
   再让内核与测试消费。顺序**不可倒置**（内核先改＝再添第二套判定）。
2. **生成优于对账**：schema `enum` / 键表 / 文案词表段凡是能从源派生的，一律**派生/生成**（构建期或首次装载期），
   把「两份人工维护 + 守卫对拍」换成「一份维护 + 机械派生」——这是消除 D3–D6 的根本手段（对拍只是止血）。
3. **判据恰一处**：同一规则在"结构门 / 读取器 / 语义判据 / 守卫测试"四类位置中**恰一处**承担；
   其余位置只**调用**（`ILedgerRule` 或同源函数）。
4. **内核零语义变更**：只改"判定源"（改读 `PortImplVocabulary.*`）与"异常文案词表段"（改渲染），
   不改执行分支、不改报错基线、不改公共签名。
5. **零新依赖倒灌**：DataCenter 侧框架件不得引入内核类型；内核继续只依赖 `DataCenter.Protocol`（现有引用已足够）。
6. **不超前抽象**：仅当"第二实例"出现才提框架（沿用《声明式框架提取》§2.6）；本轮只提**已有两份以上实例**的机制。

---

## §5 通用框架形态（DataCenter 侧，件 A–F）

> 落点：`DataCenter.Protocol\Standards\`（纯契约，可被内核直接引用）与 `DataCenter.Standards\`（机制与判据）。
> 件 E 复用《声明式框架提取》已规划的 `BasicMethod.Declarative.Ledger.ILedgerRule`（F1 产出）。

| 件 | 名称 | 落点（新增/改造） | 职责（消除项） |
|---|---|---|---|
| **A** | 词表单源 + 渲染（Vocabulary） | `PortImplVocabulary` 增 `Render*`：`ConversionEnum` / `FromEnum` / `SelectEnum` / `FakeKindEnum` / `ScopeKindEnum` / `KindEnum` / `KeysOf(level)` → `IReadOnlyList<string>`；`DataCenter.Standards` 增 `SchemaEnumSynchronizer`（把源渲染写回 `schemas\*.schema.json` 的 `enum` 节点，幂等、可选生成式） | D3/D4/D5（词表多副本）；E1/E4（漂移） |
| **B** | 键闭包单源（KeyClosure） | `PortImplVocabulary` 增 `EnsureKnownKeys(string level, JsonElement obj)`（由 22 张键表派生，唯一实现）；`StandardJson.Impl*.cs` 的读取入口逐层调用它；`SchemaEnumSynchronizer` 用同一派生结果写 `additionalProperties:false` | D6；U1；E5① |
| **C** | 结构门接线（Gate） | `StandardCatalog.LoadValidated(..., LedgerValidationOptions)` 增 `ValidateSchema`（**默认 true**）；装载期把四 schema 与台账一并校验，违规并入 `StandardViolationException`（带判据号） | E5；E6（scope.kind 立即被拦） |
| **D** | 镜像收敛（Mirror） | 二选一（§6 P2 决策点）：**D-1 生成式**——`SpecHost\Dto*` 由标准模型生成（T4/脚本一次性生成 + 守卫测试比对）；**D-2 守卫式**——保留手写映射，但新增"字段面守卫"（逐 record 比对字段名/顺序/数量，任一漂移即红） | D1/D2（4 份模型 3 个按位映射） |
| **E** | 判据单源（Rule） | 新增 `Rules\ProductFieldRule`（实现 `ILedgerRule<IStandardCatalog>`）= 现 C13 唯一实现；`FieldContractValidator` 改为**转发**或删除（保留类名以兼容内核消费方）；测试侧三份判定改为调用同一规则 | D7/D8 |
| **F** | 诊断单源（Diagnostics） | ① `StandardJson.Impl.Scripted.cs` 32 处 `InvalidOperationException` → `StandardViolationException`（含判据号 C10–C13）；② 读取器收集违规而非首抛即断（沿用 M3 的"一次报全"）；③ 内核异常文案的**词表段**改由件 A 渲染（`ComposedArgs.cs:172` / `ScriptedValue.cs:339-342` / `Project.cs:71`） | E3/E4；M3(B7) 合流 |

### 5.1 件的依赖关系

```
A(词表源) ──┬─→ B(键闭包，派生自 A 的键表) ──→ C(结构门接线，消费 B 的闭合判据)
            └─→ F③(文案词表段渲染)

D(镜像收敛) ── 独立（消费 A 的词表做字段面守卫/生成校验）
E(判据单源) ── 依赖《声明式框架提取》F1（ILedgerRule 宿主）；与 D 无耦合
```

- A 是**根件**（其余三件都消费它）；C 是**风险最高的接线动作**（会把此前只在测试里暴露的结构错误搬到装载期 → 必须先跑全量台账自证，见 §6 P1 判据）。
- 顺序：**A → B → C**（不可跳步）；D、E、F 可在 A 之后并行。

---

## §6 分批路线（P0 → P4）

| 段 | 主题 | 一句话目标 | 件 | 依赖 |
|---|---|---|---|---|
| **P0** | 止血（错误项） | 消除 3 处实质漂移（fakes 词表 / proxy 放行 / 装载期异常类型） | F①② + 词表修数 | 无（立即可做） |
| **P1** | 词表单源（根件） | `PortImplVocabulary` 成为唯一源，schema `enum` 改为生成物 | A | P0（F 的异常口径先落地） |
| **P2** | 键闭包 + 结构门接线 | 未知键 fail-fast 在装载期生效；schema 校验接进 `LoadValidated` | B → C | P1 |
| **P3** | 镜像与判据收敛（与 M3 合流） | DTO 镜像有守卫或生成；产物字段规则单源 | D + E + F③ | P1；E 需《框架提取》F1 |
| **P4** | 收口 | 棘轮/守卫/文档/README 对齐，旧报告标注状态 | — | P1–P3 |

### P0 —— 止血（低风险，独立提交）

| 项 | 内容 |
|---|---|
| **改动面** | ① `fakes.schema.json:15` 的 `enum` 改为与源一致（删 `echo`、补 `counting`、`proxy`），`FakeStandard.cs:5` 注释同步；② `ComposedDeps.BuildFake` 增 `proxy` 分支（映射到 `ProxyFakes.Counting`）**或**把 `proxy` 从 `PortImplVocabulary.FakeKinds` + `ports.schema.json:140` 移除（二选一，需先取台账实测：`grep '"fake"' 台账` 是否出现 `proxy`）；③ `StandardJson.Impl.Scripted.cs` 32 处 `InvalidOperationException` → `StandardViolationException`（**只换类型，文案逐字不动**）。 |
| **判据（crit:macro）** | ① fakes 词表与 `PortImplVocabulary.FakeKinds` 逐字相等（用例 `CollectionAssert.AreEquivalent`）；② `grep -c 'new InvalidOperationException' DataCenter.Standards\*.cs` **= 0**；③ `DataCenter.Standards.Tests` + `Agent.Spec.Guard.Tests` **全绿且用例数不降**。 |
| **回退** | 单提交可撤（schema 数值 + 异常类型名 + 注释三处独立）。 |
| **风险** | ② 若台账确用 `proxy` 而又选"移除词表"会破产品台账 → **决策顺序：先 grep 台账，再决定加实现还是删词**。 |
| **验收** | §9 命令 ①③④。 |

### P1 —— 词表单源（件 A，根件）

| 项 | 内容 |
|---|---|
| **改动面** | ① `PortImplVocabulary` 增渲染面：`ConversionEnum/FromEnum/SelectEnum/FakeKindEnum/ScopeKindEnum/DeliverKindEnum/AggOps/OnBlankFirstKey/OnDuplicate/MessageModes/KindEnum`（全部**委托既有词表常量**，不新增第二份数据）；② 新增 `DataCenter.Standards\SchemaEnumSynchronizer`：读四 schema → 按**键路径 → 词表**映射替换 `enum` 数值 → 幂等写回（仅在 `--sync-schema` 显式调用或测试守卫中运行，**不在装载期写盘**）；③ `StandardImplSchemaTests` 的对账改为调用 `SchemaEnumSynchronizer.Verify()`（同一实现，测试不再自持映射表）。 |
| **判据（crit:macro）** | ① 全仓词表字面量定义点 = 1：`grep -c '"[a-z]*", *"[a-z]*", *"[a-z]*"'` 于 `DataCenter.Protocol\Standards\PortImplVocabulary*.cs`（其余位置命中 = 0，内核除外见原则 4）；② `SchemaEnumSynchronizer.Verify()` 全绿（schema 与源逐键相等）；③ 四 schema **无手工改动**（diff 仅由 `--sync-schema` 产生）。 |
| **回退** | 删除渲染面 + Synchronizer（无消费方即无行为变化）。 |
| **风险** | schema 是"数据副本"的既有纪律被改为"生成物"后，**手工热修 schema 会被下次同步覆盖** → 必须在 `$comment` 中写明"本文件 enum 由 PortImplVocabulary 生成，禁手改"，并在守卫中加"生成物指纹"断言。 |
| **验收** | §9 命令 ①③④ + 词表定义点计数。 |

### P2 —— 键闭包 + 结构门接线（件 B → C）

| 项 | 内容 |
|---|---|
| **改动面** | ① `PortImplVocabulary` 增 `EnsureKnownKeys(string level, JsonElement obj)`（**唯一实现**，由 22 张键表 + `KeysOf` 派生；未登记层 → 不判）；② `StandardJson.Impl.cs` / `StandardJson.Impl.Scripted.cs` 的每个读取入口（`ReadImpl/ReadArgs/ReadDeps/ReadCalls/ReadProject/ReadBinds/ReadStep/ReadInvoke/ReadNew/ReadScope/ReadDeliver/ReadCapture/ReadScenario/ReadExpr`）逐层调用它；③ `StandardCatalog.LoadValidated` 增 `ValidateSchema`（默认 true）→ 调 `StandardSchemaValidator.EnsureValid` 并把违规并入 `StandardViolationException`（与 C1–C13 **同一次报全**）。 |
| **判据（crit:macro）** | ① 未知键注入用例：在 `impl`/`args[]`/`step`/`scope` 各层各注一个未知键 → 装载期**全部报出且带键路径**（用例新增）；② `scope.kind="nope"` 在**装载期**即被拦（E6 消除）；③ 产品台账 + 110 端口测试台账（`test-ports.json`）装载**零违例**（自证不误杀）；④ `Ports\**` 内 `JsonDocument.Parse` **不增**（接线不得引入内核侧解析）。 |
| **回退** | `ValidateSchema` 开关置 false 即恢复旧行为（读取器键闭包可单独回退）。 |
| **风险** | **误杀风险最高的一段**：此前从未在装载期跑过的键闭包/枚举一旦有历史脏键，会立即阻断装载 → 必须**先离线跑一遍全量台账并列出违例清单**，逐条判定"是脏数据还是漏词表"（改数据 vs 改词表，禁改判据）。 |
| **验收** | §9 命令 ①③④。 |

### P3 —— 镜像与判据收敛（件 D + E + F③，与 M3 合流）

| 项 | 内容 |
|---|---|
| **改动面** | **D（决策点，二选一）**：<br>　D-1 **生成式**：`SpecHost\SpecPortStandard.Dto.cs` 由 `DataCenter.Protocol\Standards` 生成（脚本一次性产出，产物入库；守卫测试比对生成物与源模型字段面）；`AgentPortStandardProjection`/`DtoBinding` 保持手写（它们做的是**语义映射**而非镜像，且分处不同工程）。<br>　D-2 **守卫式**（低风险，先做）：新增 `PortStandardMirrorGuardTests`，反射比对 `Dto*` 与 `*Standard` 的字段名/顺序/数量/可空性，任一漂移即红；并在 Dto 文件头写明"镜像约束由守卫承担"。<br>**E**：新增 `Rules\ProductFieldRule`（唯一实现 C13 语义）→ `StandardConsistency` 迭代调用；`FieldContractValidator` 改转发；测试侧三份判定改调用；`out.fields` 与 `productFields` 双源**改派生**（`ProductFields` 由 `OutFields` 生成，或反之以 `productFields` 为源——需先定"谁是契约面"）。<br>**F③**：内核三处异常文案的词表段改由 `PortImplVocabulary` 渲染（`ComposedArgs.cs:172`、`ScriptedValue.cs:339-342`、`Project.cs:71`）；顺带修 E4 的 `graph` 缺失。 |
| **判据（crit:macro）** | ① 「产物字段 ⊆ 声明字段」实现点 = 1（全仓 grep 该判据实现类名，命中 = 1 + 转发）；② 镜像漂移注入用例：故意改 `DtoPortArg` 一个字段 → 守卫**必红**；③ 内核三处文案与实际支持词表逐字相等（含 `graph`）；④ D8 双源消除后，`FieldContractGuardTests` 的"双源对拍"用例**删除或改为恒真**（用例数允许下降，但需在提交信息说明）。 |
| **回退** | E 逐判据可撤（先搬宿主后内联）；D-2 纯新增；F③ 纯文案。 |
| **风险** | ① 文案改动会命中内核既有 Guards 的文案断言 → 必须**同批更新守卫基线并留痕**（沿用 M3 的"文案不退化"纪律）；② D8 判定"谁是源"会牵动产品台账结构 → 若台账无法同步改，则**保留双源但把对拍升为装载期判据**（退而求其次，记为已知偏差）。 |
| **验收** | §9 命令 ①③④ + 内核守卫全绿。 |

### P4 —— 收口

| 项 | 内容 |
|---|---|
| **改动面** | ① 守卫棘轮预算下调至 P1–P3 后的实测值（词表定义点、异常类型命中、`KeysOf` 消费方）；② `DataCenter.Standards.csproj` 的 `ModuleRule_Description` 补"词表单源 + 键闭包 + 结构门接线"职责；③ `Observability内核审查报告.md` 补日期/状态列（已修/未修/迁移中）；④ 在《下一阶段实施计划》§10 与《下一步执行清单》§6 各登记一条跳转（本文档 = 其前置 P0–P2）；⑤ `checks`：确认 `PortImplVocabulary` 的类注释（U1 的"读取器据此 fail-fast"）与事实一致。 |
| **判据（crit:macro）** | ① `MainProj.sln` 与 `DataCenter.slnx` **0 错 0 警**；② 守卫（含 SpecHost / Guard.Tests / Observability 51 guards + E22）全绿；③ 文档交叉引用**无悬空**（本文档 §0.3 四份引用全部可达）；④ 棘轮值 = 实测值（不是人工估值）。 |
| **回退** | 逐项可撤。 |
| **验收** | §9 全量。 |

---

## §7 风险与兼容性

| 风险 | 应对 |
|---|---|
| **结构门接线引发装载期阻断**（P2 最高风险） | 先离线跑全量台账出违例清单（改数据/改词表，禁改判据）；`ValidateSchema` 提供开关与 warning→error 两阶段切换 |
| **schema 从"数据副本"变"生成物"后手工热修被覆盖** | 写盘仅在显式 `--sync-schema`；`$comment` 标注"生成物禁手改"；守卫加生成物指纹断言 |
| **改异常类型 breaking 消费侧 catch** | 现状是"32 处 `InvalidOperationException` 不被任何 catch 期望"——统一为 `StandardViolationException` 只会**增加**可捕获面；须 grep 确认无 `catch (InvalidOperationException)` 依赖（P0 第 1 步） |
| **内核文案改动破守卫基线** | 同批更新守卫基线并留痕；文案**只改词表段**，前缀 `[port:bind]`/`[port:input]` 与句式不动 |
| **二进制消费侧（HintPath 方案 B）** | 本路线**不改公共签名**（`LoadValidated` 只**新增可选参数**）；新增类型随 `DataCenter\BuildOutput` 烘焙（参见《框架提取》§7 同款风险） |
| **镜像收敛伤及 SpecHost 零引用硬约束** | D-1 生成的 DTO 落在 SpecHost 自身（生成物不含 `DataCenter.*` 类型引用）；D-2 纯测试侧守卫，零生产改动 |
| **双源（D8）无法消除时留缺口** | 退路：把"双源对拍"从**测试判据**升为**装载期判据**（C14），缺口可见化而非隐形 |

---

## §8 明确不做

- **不改内核执行分支语义**（`ScriptedValue`/`ComposedArgs`/`Project`/`ScriptedBinding`/`ComposedDeps` 的取值与转换逻辑一行不动）——只改判定源与文案词表段。
- **不删内核词表 switch**：分支保留（执行需要），只把"是否已知/文案"改为消费 `PortImplVocabulary`（原则 4）。
- **不把 `PortImplCompiler`/`PortBinding`/`PortComposer` 提到通用框架**（沿用《框架提取》§8：无第二实例）。
- **不重构 `PortOp` 未实现操作码**（U3）：迁移未完成前保留"已登记待迁移"语义。
- **不动 `KeysOf` 的 22 张键表结构**（B 件只加派生判据，不重组数据）。
- **不引入第三方 JSON Schema 库 / 不改 schema 关键字子集**。
- **不在本轮处理 D9 三形态归一**（事件契约）：需先有第二事件台账实例；仅登记为已知重复。

---

## §9 验收清单（可执行核对）

```powershell
# ① 两侧编译（标准层 + 内核）
dotnet build Frame\DataCenter\DataCenter.slnx -clp:ErrorsOnly
dotnet build Frame\CsharpPrj\Strategy\Observability\Observability.csproj -clp:ErrorsOnly

# ② 聚合回归（同时刷新烘焙副本）
dotnet build Frame\CsharpPrj\MainProj.sln -clp:ErrorsOnly

# ③ 标准层判据（C1–C13 + 新增结构门判据）
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Standards.Tests\DataCenter.Standards.Tests.csproj

# ④ 守卫（标准面 + 内核面）
dotnet test Environment\CentralTest\AgentTest\Agent.Spec.Guard.Tests\Agent.Spec.Guard.Tests.csproj
dotnet test Environment\CentralTest\CSharpTest\Strategy.Tests\Observability.Tests\Observability.Tests.csproj --filter "FullyQualifiedName~PortKernelDisciplineGuardTests|FullyQualifiedName~E22"
```

**单点判据（grep 型，每批收口复算）**

| 段 | 判据 | 目标值 |
|---|---|---|
| P0 | fakes 词表 vs `PortImplVocabulary.FakeKinds` | 逐字相等 |
| P0 | `grep -c 'throw new InvalidOperationException' DataCenter\DataCenter.Standards\*.cs` | **0** |
| P1 | 词表字面量定义点（标准层 + schema） | 源 = 1；schema = 生成物 |
| P1 | `SchemaEnumSynchronizer.Verify()` | 全绿 |
| P2 | 未知键注入用例（impl/args/step/scope 四层） | 装载期全部报出且带键路径 |
| P2 | `scope.kind` 非法值 | **装载期**被拦 |
| P2 | 产品台账 + `test-ports.json` 装载 | **0 违例** |
| P3 | 镜像漂移注入（改 `Dto*` 一字段） | 守卫**必红** |
| P3 | 内核三处 convert 文案 | 与源词表逐字相等（含 `graph`） |
| P3 | 产物字段判据实现点 | 1（+ 转发） |
| P4 | `MainProj.sln` / `DataCenter.slnx` | 0 错 0 警 |

**审计项 → 段 映射（收口核对用）**

| 审计项 | 段 | 审计项 | 段 |
|---|---|---|---|
| D3/D4/D5（词表多副本） | P1 | E1/E2（fakes 漂移、proxy 放行） | P0 |
| D6/U1（键白名单三族/无消费方） | P2 | E3（异常类型） | P0（+M3 合流） |
| D1/D2（模型 4 份 / 映射 3 个） | P3-D | E4（convert 文案漂移） | P3-F③ |
| D7/D8（判据 4 处 / 台账双源） | P3-E | E5（结构门未接线） | P2 |
| D10/U5（读取原语两组） | 《框架提取》F1/F2（本路线不重复处理） | E6（scope.kind 后置） | P2 |
| D11（payload 解析两次） | P3（顺路，独立小提交） | E7（C8 覆盖面） | P2（并入键闭包后重评） |
| D12/U7（同名异义 / 双词表） | P4（改名/删词，需台账取证） | E8（kind 择路判定源） | P3-F（改读 `IsKnownKind`） |
| U2（fakes 死链） | P0/P4（要么接入要么删除） | U3/U4/U6 | P4 |

---

## §10 变更记录

| 版本 | 日期 | 内容 |
|---|---|---|
| v1.0 | 2026-09-17 | 首版：双目录重复/冗余/错误逻辑审计（D1–D12 / U1–U7 / E1–E8），确立「DataCenter 侧先建通用框架件 A–F」的收敛路线与 P0–P4 分批（含判据、回退、风险、验收清单）。 |
| v1.1 | 2026-09-17 | **P0 + P1 + P2 已落地**（实施记录；与《框架提取》F1/F2 同批）。<br>**P0（止血）**：① `fakes.schema.json` 的 `kind` enum 与 `PortImplVocabulary.FakeKinds` 对齐（删幽灵 `echo`、补 `counting`）；② 实测两份台账 `"fake"` 命中 = 0 → 采「删词」路线：`FakeKinds` 与 `ports.schema.json` 的 `deps.fake` enum 去 `proxy`（结构门放行 ∩ 执行器无分支的假阳性放行消除，内核执行分支零改动）；③ `StandardJson.Impl.Scripted.cs` **32 处** `InvalidOperationException` → `StandardViolationException`（只换类型、文案逐字不动；`grep` 命中 = 0）。<br>**P1（件 A）**：`PortImplVocabulary.Render.cs` 增渲染面（`KindEnum/ConversionEnum/FromEnum/SelectEnum/FakeKindEnum/ScopeKindEnum/DeliverKindEnum/...`，全部委托既有常量）；新增 `SchemaEnumSynchronizer`（16 条「键路径 → 词表」绑定；`Verify()` 只读对账、`Sync()` 按 `Utf8JsonReader` 原文范围精确写回，**幂等且保排版**；字节↔字符偏移显式换算）；`StandardImplSchemaTests` 的对账改调 `Verify()`（测试不再自持映射表）；四 schema 的 `$comment` 标注「enum 为生成物，禁手改」。<br>**P2（件 B→C）**：`StandardCatalog.LoadValidated(..., bool validateSchema = true)` —— 结构门默认接进装载路径（四 schema 经 `EmbeddedResource` 内嵌，零路径接线），结构与语义**一次报全**；`StandardJson.EnsureLayerKeys(level, context, obj)` 为键闭包唯一实现（取值域 = `KeysOf` 派生的 22 张键表），已接线 10 个层入口：`impl/args/deps/calls/project/composedArg/step/scope/deliver/valueSource`；`scope.kind` 非法值→**装载期**即拦（E6 消除）。<br>**判据**：`Verify()` 与 `Sync()` 幂等性/逐字复原（新用例）；未知键四层注入装载期报出且带键路径（新用例）；`LoadValidated` 默认结构门开；产品台账 + `test-ports.json` 装载 **0 违例**。<br>**未做（留后续）**：P3-D 镜像守卫、P3-E 内核 `FieldContractValidator` 转发与双源派生（C13 唯一实现已在 `StandardRuleSet.ProjectionRule`，内核侧转发未动）、P3-F③ 内核文案词表段渲染、P4 收口。<br>**验收**：`DataCenter.slnx` / `MainProj.sln` 0 错 0 警；`DataCenter.Standards.Tests` 65/65；`Agent.Spec.Guard.Tests` 74/74；Observability guards 52/52。 |
| v1.2 | 2026-09-17 | **P3（件 D-2 / E / F③ + E8）+ P4 已落地**。<br>**P3-D（件 D-2 守卫式；D-1 生成式留后续）**：新增 `Agent.Spec.Guard.Tests\PortStandardMirrorGuardTests.cs` —— 25 组 `Dto*↔*Standard` **显式配对** + 字段面逐位比对（数量/名称/**位序**/形状；形状含可空性、集合元素、嵌套 record 的**中性名**，不递归展开防环）+ 目录层例外登记（`PortStandard`/`PortFieldStandard`/`EventStandard`/`FakeStandard`/`DataStandardItem`）+ 非空性自证 + 判据自证。**漂移注入实测**：给 `DtoPortArg` 加一字段 → 守卫红且报 `DtoPortArg ↔ PortArgStandard：字段数 7 vs 6`（随即回退）。<br>**P3-E（判据单源 + D8-a）**：新增 `DataCenter.Protocol\Standards\OutFieldContract.cs` = 「产出字段 ⊆ 声明字段」的**唯一实现**（集合关系 + 渲染；弱校验面内置；零依赖，故内核可直接消费）；C13 判据体改调核（**文案逐字不动**）；内核 `FieldContractValidator` 改**转发**核（public 签名与文案不变，`SpecPortHost` 零改动）；**新增 C14** `StandardRuleSet.ProductFieldCoverageRule`（`out.fields ⊆ impl.productFields`）= **D8-a 裁决**（双源保留、对拍判据单源并升为装载期）；守卫 `FieldContractGuardTests` ①/③ 改调核（不再自持集合逻辑）。<br>**P3-F③ + E8（内核文案与择路判定源）**：`PortImplVocabulary.Render.cs` 增 `ScriptedConversionEnum`（**含 `graph`，E4 消除**）/`CommonConversionEnum`/`RenderList`/`KindName`；内核**四处**文案词表段改渲染 —— `ScriptedValue.cs:339`、`ComposedArgs.cs:173`、`Call.cs:107`（**审计 E4 漏列的第 4 处**）、`Project.cs:72`；kind 择路字面量 9 处改引用 `KindName`（`PortSpecCompiler` ×3、`PortBinding` ×5、`PortComposer` ×1）；新增守卫 `PortVocabularySourceGuardTests`（`Ports\**` 零手抄清单 + 三择路文件零 kind 字面量 + 正则自证）。<br>**P4（收口）**：新增 `StandardsDiagnosticsGuardTests`（三条棘轮：`DataCenter.Standards` 裸抛 `InvalidOperationException` = **0**；标准层 public 词表定义点全部落在 `PortImplVocabulary*` 家族且计数 = **16**；`EnsureLayerKeys` 调用点 ≥ **10**）；`DataCenter.Standards.csproj` 的 `ModuleRule_Description` 补四项职责（词表单源/键闭包/结构门/判据单源）与 C1–C14；`Observability内核审查报告.md` 补日期与**状态列**（3.1/3.2 已修，1.4 半修）；`PortImplVocabulary` 类注释与事实对齐（schema = 生成物、键闭包唯一判据 = `EnsureLayerKeys`）。<br>**判据（实测）**：`MainProj.sln` / `DataCenter.slnx` **0 错 0 警**；`DataCenter.Standards.Tests` **74/74**（65 + 渲染面 3 + C14 3 + 棘轮 3）；`Agent.Spec.Guard.Tests` **77/77**（74 + 镜像 3）；`Observability.Tests` **205/205**（含内核守卫 55/55）；C14 在**产品台账 + `test-ports.json`** 零违例（`StandardConsistencyGuardTests` 承载，自证不误杀）；全仓 `OutFieldContract` 消费方 = 核 1 + 转发方 4（C13/C14/内核/守卫）。<br>**未做（遗留，需取证或立项）**：D-1 生成式镜像；D12/U7 同名异义与双词表取证；U2/U3/U4（`PortOp` 未实现操作码 / `PortValueOptions.IsKnownConversion` 无运行期消费方 / `fakes` 死链）；映射器**构造点实参序**守卫（已登记为已知偏差，由 `PortGoldenParityTests` + `SpecPortHostTests` 兜底）。 |

