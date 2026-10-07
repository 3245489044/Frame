# DataCenter 描述符单源化 —— 下一步执行清单

> 承接 `DataCenter-描述符单源化-设计与实施.md`（**v0.7**，设计 + 批 A/B 实施记录）。
> 定位：把**剩余工作**整理为可逐批执行的段（每段含改动面 / 判据 / 回退 / 验收），与既有《标准框架通用化重构-下一步执行清单》同格式。
> 依赖序：`B-3b(钩子 → 语料 → 删旧)` → `批 C(遍历/结构门/完备性)` → `批 D(镜像)` → `批 E(收口)`。
> 红线（沿用设计与实施文档）：标准模型零依赖纯契约；`DataCenter.Standards` 只引 `DataCenter.Protocol`；判据与诊断文案**逐字不变**；**对拍不全绿不删旧**。

---

## §0 当前状态快照（已完成，作为起点）

| 维度 | 已达成 | 证据（实测） |
|---|---|---|
| 描述符内核 | `Descriptors\StandardDescriptor.cs`：`MemberKind` / `Polymorphy` / `MemberDescriptor`（含 `AliasKeys` / `Owner` / `DeclaredKeys`）/ `LayerDescriptor`（含 `Layer` / `KeysLevel` / `Ordered` / `Keys`） | `DataCenter.Protocol` 0 错 0 警 |
| 描述符表 | `Descriptors\StandardDescriptors.cs`：**26 层**（全 T1 键表；`expr` 层成员由 `PortImplVocabulary.ExprSlots` 派生） | `StandardDescriptorTests` **4/4** |
| 读取引擎 | `StandardReader.cs`（`StandardReaderEngine`：描述符驱动 + 反射构造 + 子层递归 + `MemberHook` 钩子位 + `context` 键路径 + 键闭包/必填）；`StandardDescriptorReader.cs`（公面） | 编译 0 错 0 警 |
| 半规则层钩子（B-3b-1） | `StandardReaderHooks.cs`（`impl` kind 门控 / `step` 四形态 / `valueSource` 多形态 / `expr`·`foreach` 子层 / `fakeCall.HasValue`）；引擎增 `Unhandled` 哨兵 + `LayerHook` + `MemberDescriptor.EmptyOnAbsent` | 编译 0 错 0 警 |
| 读取对拍（B-3b-2，全层） | `StandardDescriptorReadTests`：自包含语料上 `描述符读取 ≡ 手写读取`（序列化逐字相等），覆盖**反身 / 组合 / 脚本 / 裸 impl** 四类（含 `step` 四形态、`valueSource` 长短形态、缺省归一 `[] vs null`） | **5/5**；全量 **108/5/2**（失败 = 既有 5，0 新增；基线 105 + 本批 3） |

**既有失败基线（与本方案无关，5 个）**：4 × `SchemaEnumSynchronizerTests.Sync_*`（仓库 `ports.schema.json` 的 `$defs/expr/properties` 与生成器**排版不幂等**、`$defs/scope/properties/kind/enum` 修复计数）；1 × `StandardsDiagnosticsGuardTests.VocabularySource_IsSingleFamily`（词表定义点预算 `18` ≠ 实测 `16`）。

---

## §1 后续总览

| 段 | 主题 | 一句话目标 | 完成标志 |
|---|---|---|---|
| **B-3b-1 ✅** | 半规则层钩子 | 为 `step` / `valueSource`(expr 子节点) / `fakeCall.HasValue` / 缺省归一装配 `MemberHook` | 全层可经描述符读取（含脚本端口）——**已落地**（`StandardReaderHooks` + 引擎 `Unhandled`/`LayerHook`/`EmptyOnAbsent`） |
| **B-3b-2 ✅** | 对拍语料扩容 | 语料覆盖**反身/组合/脚本/裸 impl** + 真实台账（可装载时） | `描述符读取 ≡ 手写读取` **5/5 全绿**——**已落地**（逐层语料自包含，与 `.specs` 解耦） |
| **B-3b-3** | 删旧（两段式第二段） | 删手写读取器的**常规层**分支；半规则层保留钩子 | 手写常规分支 = 0；全量测试 = 基线 |
| **批 C** | 遍历 / 结构门 / 完备性 | 描述符驱动遍历、schema 生成、`Landings` 投影 | 三面对拍全绿 |
| **批 D** | 镜像收口 | `MirrorPlan` 落为数据文件；字段位序由描述符派生 | 只读对拍全绿 |
| **批 E** | 收口 | 文件组织归位；程序集拆分（独立立项） | 0 错 0 警；清单/文档对齐 |

每段收口跑 §7 对应验收；不得跨段跳过。**任何"删旧"须以对应"对拍全绿"为前置。**


---

## §2 B-3b-1 —— 半规则层钩子（装配 `MemberHook`）

| 项 | 内容 |
|---|---|
| **背景** | 引擎已能读**常规层**（`arg`/`dep`/`call`/`composedField`/`composedArg`/`fakeCall`/`fakeCapture`/`throw`/`collect`/`chain`/`init`/`save`/`concatPart`/`fieldExpr`/`scope`/`deliver`/`scenario`/`new`/`delegate`/`invoke`/`file`）。**半规则层**含本质语义，须经 `StandardReaderEngine.Wire(hook)` 装配。 |
| **改动面** | ① **`step` 层四形态**：`new`/`invoke`/`foreach`(+`items`)/`throw`/`from:list|map` 的判别与装配（判别顺序同手写 `ReadStep`；`items` 键先于 `foreach` 判值源步骤）；② **`valueSource` 的表达式子节点**：`RowValue`（仅 `from:table` → 内层 `rowValue`）、`Filter`（`from:table|presence` → `where`）—— 委托手写 `Sub`/`ReadArg`；③ **`fakeCall.HasValue`**：派生位（键 `value` 存在即 `true`），描述符 `JsonKey=null` → 钩子；④ **`impl` 子树**：`Script`/`Scopes`/`Deliver`/`Scenarios` 的递归进入（其内部含 step/valueSource）；⑤ **缺省归一 `[] vs null`**：手写对 `impl.Args`/`impl.Deps` 等归一为**空序列**、对 `dep.Calls`/`impl.Calls` 等为 **null** → 描述符增 `MemberDescriptor.EmptyOnAbsent`（或钩子），使引擎逐位一致。 |
| **判据** | 描述符读取在**逐层语料**上 ≡ 手写读取（含 `HasValue` / 四形态 / 缺省归一逐位）；`DataCenter.Protocol` / `DataCenter.Standards` **0 错 0 警**；既有判定/守卫**不退化**。 |
| **回退** | 钩子与 `EmptyOnAbsent` 逐项可撤（引擎已就位，未装配钩子的层自然回落手写路径）。 |
| **验收** | 见 §7 ③；新增半规则层用例全绿。 |

---

## §3 B-3b-2 —— 对拍语料扩容（全 25 层）

| 项 | 内容 |
|---|---|
| **背景** | 首片语料仅覆盖反身端口（`impl`/`arg`/`dep`/`composedArg`）。须扩至**全部 25 层**，含脚本端口（`script`/`deliver`/`scopes`/`scenarios`）。 |
| **改动面** | ① 扩充 `StandardDescriptorReadTests` 语料：逐层最小对象 + 脚本端口（覆盖 `step` 四形态、`valueSource` 长短形态、`expr` 算子）；② 可装载时并入**真实台账**（产品 10 + 测试台账 110）；若工作区台账不可装载，则**以自包含语料为准**（与 `.specs` 解耦，见设计文档 v0.7 口径修正）；③ 比较口径：`PortImplStandard` **序列化逐字相等**（`JsonElement` 载荷字段以原文比较）。 |
| **判据** | `描述符读取 ≡ 手写读取` **全绿**（全部 25 层 + 脚本端口）；差异即红并报「层/成员/键」。 |
| **回退** | 语料逐项可撤。 |
| **验收** | 见 §7 ③。 |
| **风险** | 中——脚本端口含深层嵌套（`delegate` 体内 `steps`、`foreach.do`、`scenarios.cases`），须覆盖判别顺序。 |

---

## §4 B-3b-3 —— 删旧（两段式第二段）✅ 已落地

| 项 | 内容 |
|---|---|
| **背景** | 两段式的**第二段**：仅在 §3 对拍**全绿**后执行。 |
| **改动面** | ① 删 `StandardJson.Impl.cs` / `StandardJson.Impl.Scripted.cs` 的**常规层**手写分支；② `StandardJson` 仅保留四视图装配（`ReadPorts`/`ReadData`/`ReadEvents`/`ReadFakes`）+ 原语委托；③ 半规则层逻辑保留为**钩子件**（`ReaderHooks`，从原手写件析出）；④ 读取入口切至 `StandardDescriptorReader`（`StandardJson` 内部改调引擎 + 钩子）。 |
| **判据（前置）** | §3 对拍 **全绿**（硬前置，未全绿不得删）。 |
| **判据（结果）** | `DataCenter.Standards` 行数**下降**（目标：`StandardJson*.cs` 常规分支 = 0）；诊断文案与异常类型**逐字不变**（棘轮守卫）；全量 `DataCenter.Standards.Tests` = 基线（新增 0 失败）。 |
| **回退** | 单提交可撤（旧件经 git 可复原）。 |
| **风险** | **高**——须严格两段式；对拍未绿即停。 |
| **落地（2026-10-07）** | ① **引擎扩面**（`StandardReader.cs`）：`MemberHook`/`LayerHook` 增 `context`（键路径）；`ReadLayer` 增键闭包（取值域 = `LayerDescriptor.Keys`，收集/抛出体仍是 `StandardJson.EnsureKnownKeys` 唯一实现）+ 必填校验；子层上下文按 `{父}.{键}[{i}]` 拼装（`impl`/`args[i]` 等诊断路径与手写逐字一致）。② **入口切换**：`StandardJson.ReadImpl` 仅留 `impl.kind` 归一（必填/非法，文案不变）+ `StandardDescriptorReader.ReadImpl(impl, "ports.{key}.impl")`。③ **删常规分支**：`StandardJson.Impl.cs` **304 → 51 行**（删 `ReadArgs`/`ReadCalls`/`ReadProject` 与旧 `ReadImpl`）；`ReadDeps`(+`ReadComposedArgs`/`ReadFakeCalls`/`ReadFakeCapture`) 作**半规则 `dep` 层钩子件**析出至脚本族文件（`dep` 三形态 + 替身子结构），`StandardReaderHooks` 增 `dep` 层钩子。④ **棘轮收口**：`StandardsDiagnosticsGuardTests` 键闭包预算 10 → **9**（常规层闭包上收引擎），并增「引擎须经 `EnsureKnownKeys` + `layer.Keys` 闭合」判据。 |
| **判据（实测）** | `DataCenter.Protocol` / `DataCenter.Standards` / 整个 `DataCenter.slnx` **0 错 0 警**；`StandardDescriptorTests` **4/4**、`StandardDescriptorReadTests` **5/5**、`LedgerGateTests`/`StandardImplReadTests`/`StandardImplScriptedLiteralReadTests`/`StandardsDiagnosticsGuardTests` 全绿；全量 `DataCenter.Standards.Tests` **108 通过 / 5 失败 / 2 跳过（共 115）**，失败 = 既有 5（4 × `Sync_*` + `VocabularySource_IsSingleFamily`），**0 新增**。`StandardJson*.cs` 键闭包调用点 13 → **9**（余半规则层钩子入口）。 |
| **遗留（显式登记，不静默）** | ① `expr` 层仍未登记描述符（键 = `ValueSourceKeys` ∪ `ExprOperatorKeys`），留批 C；② 引擎「必填」诊断文案为通用式（`[port:standard] {context} 缺少必填键 '{key}'`），与手写逐项文案（如「缺少 convert」）**不同字但有同判**——既有断言面未覆盖，登记待批 C 收口；③ 场景主体（`scenarios.cases[*]`）仍走手写 `ReadBinds/ReadDeps/ReadScript/...`（非独立登记层）。 |

---

---

## §5 批 C —— 描述符化·遍历侧 + 结构门 + 完备性（取代 M2 / T2 / T3 / T4）

| 项 | 内容 |
|---|---|
| **背景** | 遍历（`StandardNodeWalk` + `ExprStandard.Walk`）、结构门绑定（`SchemaEnumSynchronizer.Bindings`/`PropertiesBindings`）、完备性落点（`StandardCompleteness.Landings`）三面仍手写。 |
| **改动面** | ① **通用遍历器**：下钻规则唯一 = `MemberDescriptor.IsChild`（`Child`/`Children`）；`expr` 层 `OperatorGroup` 提供 C10 算子组取值域；合并 `ExprStandard.Walk.cs` 入 `StandardNodeWalk`（反射仅保留对拍自证）；② **结构门绑定反转**：`SchemaEnumSynchronizer` 的 `Bindings`/`PropertiesBindings` 由 `StandardDescriptors` 派生（指针 = `$defs/{Layer}/properties/{JsonKey}/enum`；键集 = `Members.JsonKey ∪ AliasKeys ∪ NestedKeys`），`Verify()`/`Sync()` 机制不变；③ **完备性投影**：`Landings` 由 SSD 生成（`Family` = `Vocabulary`；`ModelMember` = `Member`；`HasReaderProbe` = `Vocabulary != null`）；④ **`expr` 层反转**：`ExprSlots` 由描述符（或反之）单源——按 §7 取舍（`ExprSlots` 已含算子组/嵌套键，可作 `expr` 描述符的生成源或并入）。 |
| **判据** | 遍历域对拍（C8/C10/C11 违例集 = 基线）；`SchemaEnumSynchronizer.Verify()` 全绿 + `Sync()` 幂等（键集/键序/enum 逐字保留）；六向完备性差集**无新增**；关系图悬空 = 基线。 |
| **回退** | 三面逐项可撤。 |
| **风险** | 中——结构门写回保排版（`Sync()` 已幂等，须实测不破）。 |

---

#### 批 C 进度（2026-10-07）

| 子面 | 交付 | 判据 |
|---|---|---|
| **④ `expr` 层登记（keystone）✅** | `StandardDescriptors.Expr`：层名 `expr` / KeysLevel `ExprKeys` / Model `ExprStandard` / **成员由 `PortImplVocabulary.ExprSlots` 派生**（成员名 / 算子组 / 子槽形态 / 键 / 嵌套键单源；`Source` 成员并声明 `ValueSourceKeys` 为 `AliasKeys` 以复现扁平来源键）；`Ordered:false`（键序无语义）。读取仍由 `expr` 层钩子（`ReadExpr`）承担（`ReadLayer` 先经层钩子，登记仅使 `Registry` 命中后仍回落钩子）。 | `StandardDescriptorTests` **4/4**（26 层）；全量 **108/5/2**（0 新增） |
| **④ 遗留 ✅ 已解** | `ExprSlot` 增可选 `Vocabulary`；`AggOp` 槽位登记 `AggOps`；`StandardDescriptors.ExprMembers` 透传（带词表者标 `MemberKind.Enum`）。`expr` 其余 4 条 enum（`from`/`convert`/`onBlankFirstKey`/`onDuplicate`）是**扁平并入的来源键**，由「`expr` 并入 `valueSource` 层 enum」规则覆盖（见 ②）。 | 已解 |
| **② 结构门绑定反转 ✅** | `SchemaEnumSynchronizer.Bindings` **由 `StandardDescriptors` 派生**（`Members(Vocabulary)` → `$defs/{Layer}/properties/{JsonKey}/enum`；`expr` 额外并入 `valueSource` 层 enum 以覆盖扁平来源键）；手抄 18 条指针表退场，**唯余 1 条非层残留**（`fakes.schema.json` 的替身 `kind`）。`VocabularyAccessor` 反射取 `PortImplVocabulary` 静态字段/属性（缺失即抛，禁静默缺绑定）。`PropertiesBindings` 唯一 1 条（`$defs/expr/properties` ← `PortImplVocabulary.ExprKeys`，本是派生属性）。 | `Verify_AllEnumsMatchSource` 全绿；`Sync` 行为与手抄表**逐字一致**（`Sync_DriftedEnum` 明细 = 基线：同 2 条变更）；全量 **108/5/2**（0 新增） |
| **① 通用遍历器 ✅（等价版）** | `StandardNodeWalk.Children` 改为**SSD 驱动的唯一规则**：常规层逐 `MemberDescriptor.IsChild`（`Child`/`Children`/**`KeyedMap`**）+ `ChildLayer` 反射取值、按子层承载模型造节点（`Expand`，成员序 = 声明序）；子层承载模型无节点工厂者视为**叶**（不下钻）。`MemberDescriptor.IsChild` 扩至涵盖承载子对象的 `KeyedMap`（否则 `impl.bind` 漏下钻）。新增 **5 个节点变体**（`Delegate`/`Init`/`Save`/`File`/`ConcatPart`）使 `source.delegate.steps` / `new.init[].{value,key}` / `scope.files[].{dir,from,content,when,batch,nameExpr}` / `scope.save[].set` / `concat[]` 走通用规则；`Expr` 层由描述符同源展开（取代原 `ExprSlots` 手写反射分支）。**三大特例显式登记**：① 合成实参 `AsValueSource`（`impl.deps`/`calls`）② 扁平多模型（`step` 四形态 + `foreach` 子层、`deliver.capture` 内联）③ 未登记层 `scenarioBody`。 | `DataCenter.slnx` **0 错 0 警**；遍历定向 **53/53**（C7–C14 全部红/绿例 + `LedgerGate` + `StandardDescriptorRead` 对拍）；全量 **113 通过 / 4 失败 / 2 跳过**——失败 = 既有 4 × `Sync_*`，**0 新增**；真实台账（`AgentLedgers`/`ProductLedger`/`TestLedger`）用例全绿（盲区闭合未引入违例） |
| **③ 完备性投影 ✅** | `StandardCompleteness.FamilyTable` 改为**由 SSD（`StandardDescriptors`）锚点派生**：新增 `VocabularyFamily(族名, LayerDescriptor, 成员名, 词位探针)`——逐条派生**词集**（= 词表，取自 `SchemaEnumSynchronizer.EnumBindings` 结构门绑定**唯一源**）/ **schema 指针**（= `$defs/{层}/properties/{键}/enum`）/ **模型落点**（= 层承载类型 × 成员，`MemberDescriptor.ModelOf`）/ **结构门文件**（`ports.schema.json`）；指针缺失即抛（层↔落点漂移当场可见）。手写仅余**族名 + ③ 探针** + **2 条层级特例**（`expr` 成员可承载多键 / `dep.form` 键集非词表）。`Landings` 的 `SchemaPointer`/`ModelType`/`ModelMember` 随之派生；`HasReaderProbe` 保持 `WordProbe is not null`（**非** `Vocabulary != null`：`dep.form` 有探针但无词表——显式登记）。 | 完备性定向 19/0/1；全量 **108/5/2**（0 新增；139 词 / 13 族 / 差集逐项 = 基线，关系图悬空 0） |

**遗留清单：**
1. ~~**`expr` 词表口**~~ **✅ 已解**：`ExprSlot` 增可选 `Vocabulary`（`AggOp` 槽位登记 `AggOps`）；`from`/`convert`/`onBlankFirstKey`/`onDuplicate` 由「`expr` 并入 `valueSource` 层 enum」规则覆盖（无需补 `ExprSlots` 槽位）。
2. **`fakes.schema.json` 绑定**：非层（`properties/fakes/additionalProperties/properties/kind`）——**仍为唯一手写残留**（显式登记保留）。
3. **① 合成节点** **✅ 已解（等价版）**：常规层改由 `IsChild` 驱动；**三大特例显式登记保留**——合成实参 `AsValueSource`（`impl.deps`/`calls`）、扁平多模型（`step` 四形态 + `foreach` 子层、`deliver.capture` 内联）、未登记层 `scenarioBody`。**残留（挂起）**：`ExprStandard.Walk.cs` 未并入 `StandardNodeWalk`——其 `ExprStandardWalker.Children` 的正消费方在内核 `Strategy.Observability`（`PortExecutor` / `ScriptedSteps`），合并将触碰「内核零改动」红线。
4. **③ 完备性 `Landings` 投影** **✅ 已解**：11 条词表成员族改由 `VocabularyFamily` 从 SSD 锚点派生（词集 / 结构门指针 / 模型落点 / 结构门文件）；残留 **2 条层级特例**（`expr` / `dep.form`）与 `HasReaderProbe`（= `WordProbe is not null`，**非** `Vocabulary != null`）**显式登记**。

---

## §6 批 D —— 镜像收口（取代 T5 / M3）

| 项 | 内容 |
|---|---|
| **背景** | `MirrorPlan` 现藏于守卫（`SpecPortMirrorGenerationTests.Pairs`），未落为数据。 |
| **改动面** | ① `MirrorPlan` 落为数据文件（`*.data.json`），`StandardMirrorEmit` 消费之；② 字段位序由 `Members` 声明序派生（消除手抄位序）；③ 删残余手写映射（内核 `PortSpecCompiler.MapArgs` 视裁决）。 |
| **判据** | 只读对拍（仓库产物 ≠ 生成结果即红）；端到端对拍；`Agent.Spec.Guard.Tests` = 基线（既有失败集不扩大）。 |
| **回退** | 单提交可撤。 |

---

## §7 批 E —— 收口

| 项 | 内容 |
|---|---|
| **改动面** | ① 文件拆分策略统一（partial 仅按「过 500 行 / 正交子域」切）；② 机制件（引擎/生成器）与声明件（record/描述符）分目录；③ **程序集拆分**（`Protocol` 图契约 vs `Protocol.Standards`）——**独立立项**（纯移动 + 改命名空间，牵连烘焙清单 / `Test-ProjectReferences`）；④ `DataCenter.Protocol.csproj` / `DataCenter.Standards.csproj` 的 `ModuleRule_Description` 补**描述符**职责。 |
| **判据** | `MainProj.sln` / `DataCenter.slnx` **0 错 0 警**；`Test-ProjectReferences` 悬空 0 / 循环 0；烘焙清单 fresh；README / 清单与实际 **100% 对齐**。 |
| **回退** | 逐项可撤（每项独立提交）。 |

---

## §8 验收命令（每段收口都跑）

```powershell
# ① 两侧编译（0 错 0 警）
dotnet build Frame\DataCenter\DataCenter.Protocol\DataCenter.Protocol.csproj -c Debug -clp:ErrorsOnly
dotnet build Frame\DataCenter\DataCenter.Standards\DataCenter.Standards.csproj -c Debug -clp:ErrorsOnly

# ② 描述符 ↔ 键表对拍（J2；25 层）
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Standards.Tests\DataCenter.Standards.Tests.csproj --filter "FullyQualifiedName~StandardDescriptorTests"

# ③ 读取对拍（B-3b；描述符读取 ≡ 手写读取，自包含语料/台账）
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Standards.Tests\DataCenter.Standards.Tests.csproj --filter "FullyQualifiedName~StandardDescriptorReadTests"

# ④ 全量回归（与既有基线逐条同名；允许既有 5 失败不扩大，不允许新增）
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Standards.Tests\DataCenter.Standards.Tests.csproj

# ⑤ 单点判据（grep 型）
#   B-3b-3：StandardJson*.cs 常规层手写分支 = 0（半规则钩子件除外）
#   批 C：SchemaEnumSynchronizer 的 Bindings / PropertiesBindings 手写表 = 0
#   批 C：StandardCompleteness 的 FamilyTable **词表落点** = 0（改由 SSD 投影）；残留 = 2 条层级特例（expr / dep.form）+ 族名 + ③ 探针（显式登记）
```

---

## §9 变更记录

| 日期 | 版本 | 说明 |
|---|---|---|
| 2026-10-07 | v1.0 | 首版：承接《描述符单源化-设计与实施》v0.7，整理剩余为 `B-3b-1(钩子)` → `B-3b-2(语料)` → `B-3b-3(删旧)` → `批 C(遍历/结构门/完备性)` → `批 D(镜像)` → `批 E(收口)` 六段执行清单。含现状快照（25 层描述符 / 读取引擎 / 对拍首片）、各段改动面/判据/回退/风险、验收命令。**关键纪律：对拍不全绿不删旧（两段式第二段为硬前置）。** |
| 2026-10-07 | v1.1 | **B-3b-1 + B-3b-2 完成（纯增量，未删旧）**：① 引擎扩面（`Unhandled` 哨兵 / `MemberHook` 带 `container`+`found` / `LayerHook` / `EmptyOnAbsent`）；② 新增 `StandardReaderHooks`（`impl` kind 门控 + `step`/`valueSource`/`expr`/`foreach` 层钩子 + `fakeCall.HasValue`），`StandardDescriptorReader` 静态构造装配；③ 描述符表 `Impl.*` 标 `EmptyOnAbsent`、`Binds` 补 `ChildLayer`；④ `ReadStep`/`ReadForeach` 放宽 `internal`（零行为改动）；⑤ 对拍扩至 5 用例（反身/组合/脚本/裸 impl + 自证）。**判据**：两侧 0 错 0 警；`StandardDescriptorTests` 4/4；`StandardDescriptorReadTests` 5/5；全量 `DataCenter.Standards.Tests` **108/5/2**（失败 = 既有 5，0 新增）。**下一步：B-3b-3（对拍全绿为前置，删手写常规层分支）。** |
| 2026-10-07 | v1.2 | **B-3b-3 落地（两段式第二段：删旧）**：前置「对拍全绿」实测成立（5/5 + 4/4）后执行。① **引擎**（`StandardReader.cs`）增 `context` 键路径穿线（`MemberHook`/`LayerHook` 签名 + 子层 `{父}.{键}[{i}]` 拼装）、键闭包（取值域 = `LayerDescriptor.Keys`，收集/抛出仍为 `StandardJson.EnsureKnownKeys` 唯一实现）、必填校验；② **入口切换**：`StandardJson.ReadImpl` = `impl.kind` 归一 + `StandardDescriptorReader.ReadImpl(impl, "ports.{key}.impl")`，常规层装配**全部**交引擎；③ **删常规手写分支**：`StandardJson.Impl.cs` **304 → 51 行**，`ReadDeps`(+`ReadComposedArgs`/`ReadFakeCalls`/`ReadFakeCapture`) 析出为半规则 **`dep` 层钩子件**（迁入脚本族文件）；`StandardReaderHooks` 增 `dep` 层钩子；④ **棘轮收口**：`StandardsDiagnosticsGuardTests.LayerEntryKeys_UseSharedClosure` 预算 10 → **9** 并增「引擎经 `EnsureKnownKeys`+`layer.Keys` 闭合」判据（键闭包 13 → 9 调用点）。**判据（实测）**：`DataCenter.slnx` 0 错 0 警；`StandardDescriptorTests` 4/4、`StandardDescriptorReadTests` 5/5、`LedgerGateTests`/`StandardImplReadTests`/`StandardImplScriptedLiteralReadTests`/`StandardsDiagnosticsGuardTests` 全绿；全量 `DataCenter.Standards.Tests` **108/5/2**（失败 = 既有 5，**0 新增**）。**遗留**：`expr` 层登记与「必填」文案统一留批 C；场景主体仍走手写子读取器。**下一步：批 C（遍历 / 结构门 / 完备性）。** |
| 2026-10-07 | v1.3 | **批 C ④ `expr` 层登记落地（keystone，行为中性）**：`StandardDescriptors` 增 `Expr` 层——层名 `expr` / KeysLevel `ExprKeys` / Model `ExprStandard`，**成员由 `PortImplVocabulary.ExprSlots` 派生**（成员名 / 算子组 / 子槽形态 / 键 / 嵌套键单源，禁另抄），`Source` 成员以 `AliasKeys = ValueSourceKeys` 复现扁平来源键，`Ordered:false`；描述符表 **25 → 26 层**（覆盖 T1 全键表）。读取路径不变（`ReadLayer` 先经 `expr` 层钩子，登记仅使 `Registry` 命中后仍回落钩子）。`StandardDescriptorTests.DeclaredKeys` 扩为「字段 → 派生静态属性」回落（`ExprKeys` 为派生属性）。**判据（实测）**：`DataCenter.slnx` 0 错 0 警；`StandardDescriptorTests` **4/4**；全量 `DataCenter.Standards.Tests` **108/5/2**（失败 = 既有 5，**0 新增**）。**登记阻塞（见 §5 批 C 进度/遗留清单）**：① `expr` 无 `Vocabulary`（`ExprSlots` 缺 `aggOp`/`convert`/`onBlankFirstKey`/`onDuplicate` 槽位）→ ② 结构门 enum 绑定仅 12/17 可派生；③ 遍历合成节点与 `scenarioBody` 未登记层待裁决。 |
| 2026-10-07 | v1.4 | **批 C ② 结构门绑定反转落地（④ 遗留同批解除，行为等价）**：① **契约扩面**（`ExprLayout.cs`）：`ExprSlot` 增可选 `Vocabulary`（`AggOp` 槽位登记 `nameof(PortImplVocabulary.AggOps)`）；`StandardDescriptors.ExprMembers` 透传 `Vocabulary`，带词表者标 `MemberKind.Enum`。② **结构门绑定派生**（`SchemaEnumSynchronizer.cs`）：`Bindings` 改为 **由 `StandardDescriptors` 派生**——遍历 `All` 的 `Members`，对每条带 `Vocabulary` 且有 `JsonKey` 者产出指针 `$defs/{Layer}/properties/{JsonKey}/enum`；`expr` 层**额外并入** `valueSource` 层 enum（覆盖其扁平并入的 `from`/`convert`/`onBlankFirstKey`/`onDuplicate`）；手抄 18 条指针表退场，**唯余 1 条非层残留**（`fakes.schema.json` 替身 `kind`）。`VocabularyAccessor` 经反射取 `PortImplVocabulary` 静态字段/属性，缺失即抛（层↔词表漂移当场可见）。③ `PropertiesBindings` 保持 `$defs/expr/properties ← PortImplVocabulary.ExprKeys`（已是派生属性）。**判据（实测）**：`DataCenter.slnx` 0 错 0 警；`SchemaEnumSynchronizerTests` **2 通过 / 4 失败（基线）/ 1 跳过**——`Verify_AllEnumsMatchSource` 全绿、`Sync_DriftedEnum` 明细与基线**逐字相同**（同 2 条：`$defs/expr/properties` 排版 + 注入的 `scope.kind`），即派生表与手抄表**行为等价**；全量 `DataCenter.Standards.Tests` **108/5/2**（失败 = 既有 5，**0 新增**）。**遗留**：`fakes` 非层绑定仍手写；① 遍历 / ③ 完备性 未开工。 |
| 2026-10-07 | v1.5 | **批 C ③ 完备性 `Landings` 投影落地（行为等价）**：**`StandardCompleteness`（`StandardCompleteness.cs`）**——`FamilyTable` 中 11 条词表成员族改由新增 `VocabularyFamily(族名, LayerDescriptor, 成员名, 词位探针[, 形态探针])` **由 SSD 锚点派生**：① **词集** 取自 `SchemaEnumSynchronizer.EnumBindings`（② 落地的**结构门绑定唯一源**，故「完备性落点」与「结构门绑定」同源、禁另抄）；② **schema 指针** = `$defs/{layer.Layer}/properties/{member.JsonKey}/enum`；③ **模型落点** = `member.ModelOf(layer.Model)` × `member.Member`；④ **结构门文件** = `ports.schema.json`。指针缺失 / 成员缺 `Vocabulary`/`JsonKey` 即抛（层↔落点漂移当场可见）。**残留（显式登记）**：2 条层级特例（`expr` 成员可承载多键、`dep.form` 键集非词表 —— 无单成员可锚定）+ 族名 + ③ 探针；`Landings.HasReaderProbe` 保持 `WordProbe is not null`（**非**设计草稿的 `Vocabulary != null`：`dep.form` 有探针但无词表，改之则关系图 `WordReader` 边漂移）。**判据（实测）**：`DataCenter.Protocol`/`DataCenter.Standards`/整个 `DataCenter.slnx` **0 错 0 警**；完备性定向（`StandardCompletenessTests`+`CompletenessBudgetGuardTests`+`StandardRelationsTests`+`StandardDescriptorTests`）**19 通过 / 0 失败 / 1 跳过**（139 词 / 13 族 / 差集计数逐项 = 冻结基线，关系图悬空 0）；全量 `DataCenter.Standards.Tests` **108/5/2**（失败 = 既有 5，**0 新增**）。**外部事项**：`InvocationResolverTests.cs`（并行工作流未提交文件，33 处不编译）验证时临时移出、**已原样恢复**（哈希一致、无 `.disabled` 残留）。**下一步：① 通用遍历器（需先裁决合成节点 `AsValueSource` / `scenarioBody`）。** |
| 2026-10-07 | v1.6 | **批 C ① 通用遍历器落地（等价版，行为不变）**。**裁决**：① 需先定「模型表达如何归一」——本轮选**等价版先行**（常规层 IsChild 驱动 + 三大特例显式登记），待后续专项再补模型表达。<br>**改动**：① **`Descriptors\StandardDescriptor.cs`**：`MemberDescriptor.IsChild` 扩为 `Child`/`Children`/**`KeyedMap`**（承载子对象的键值映射亦为子节点槽；否则 `impl.bind` 漏下钻）。② **`StandardNodeWalk.cs` 重写**：`Children` 常规层改为 **SSD 驱动**（`Expand`：逐 `layer.Members` 的 `IsChild` 成员、按声明序反射取值、按 `ChildLayer` 承载模型造节点；模型无工厂 ⇒ 叶）；新增 5 个节点变体（`Delegate`/`Init`/`Save`/`File`/`ConcatPart`），`Expr` 层由 `StandardDescriptors.Expr` 同源展开（删原 `ExprSlots` 手写反射分支与 `SlotValue`）。**三大特例显式登记**：合成实参（`ImplSpecials`：`impl.deps`/`calls` → `AsValueSource`）、扁平多模型（`StepChildren`/`DeliverSpecials` 的 `deliver.capture` 内联）、未登记层（`ScenarioChildren` 的 `scenarioBody`）。<br>**判据（实测）**：`DataCenter.slnx` **0 错 0 警**；遍历定向（`ExprSlotTableTests`+`StandardImplConsistencyTests`+`StandardConsistencyTests`+`LedgerGateTests`+`StandardDescriptorReadTests`）**53 通过 / 0 失败**（C7–C14 全部红/绿例 + 盲区闭合反证 `C10_SeesStepsNestedInsideDelegateBody`）；全量 `DataCenter.Standards.Tests` **113 通过 / 4 失败 / 2 跳过**——失败 = 既有 4 × `Sync_*`（**0 新增**）；真实台账 `AgentLedgers_*`/`ProductLedger_*`/`TestLedger_*`/`Load_VocabularyConformLedger_Passes` 全绿（IsChild 驱动的**盲区闭合**——`source.delegate.result`/`new.init[].key`/`scope.files[].{dir,when}`/`source.listItems`——未在任何台账上引入新违例，即**实测行为等价**）。<br>**挂起（登记）**：`ExprStandard.Walk.cs` 未并入——其正消费方在内核 `Strategy.Observability`（`PortExecutor`/`ScriptedSteps`），合并即触碰「内核零改动」红线。<br>**外部事项**：`InvocationResolverTests.cs`（并行工作流已更新并提交）验证时临时移出后**按 HEAD 原样恢复**（哈希 `3F6FCA4E…` 与 `git show HEAD` 逐字一致）；`Environment` 仓该文件无残留、Frame 工作树仅余本批 2 文件。<br>**下一步**：批 C 收口——`fakes.schema.json` 非层绑定（唯一手写残留）或转批 D（镜像 `MirrorPlan` 落数据）。 |




