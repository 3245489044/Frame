# DataCenter 描述符单源化 —— 设计与实施

> 版本：v0.7（草案） | 建立：2026-10-07
> 范围：`Frame\DataCenter\DataCenter.Protocol\Standards\` × `Frame\DataCenter\DataCenter.Standards\`
> 承接：`DataCenter-标准单源化-重复逻辑收敛-设计与实施.md`（重复审计 D1–D13 / 冗余 U1–U7 / 错误 E1–E8）
> 一句话：把「**每个方面各自收敛一次**」升级为「**一份声明描述符单源 → 全面派生**」——同一批模型元数据不再被登记 N 遍。
> 纪律（沿用上位文档红线）：标准模型零依赖纯契约（只 BCL + `System.Text.Json`）；`DataCenter.Standards` 只引 `DataCenter.Protocol`，**不得引 `Strategy.Observability`**；零领域词汇；装载期一律 `StandardViolationException`。

---

## §0 定位与关系

| 项 | 内容 |
|---|---|
| **上游** | 《标准单源化》已把词表 / 键闭包 / 结构门 / 判据 / 诊断**逐方面**收敛（P0–P4、v1.1–v1.5）。本文档收敛其**共同上游**——模型元数据。 |
| **与《标准框架通用化重构》** | 沿用其 L0.5⁻ / L0.5 分层与红线；不重述机制搬迁（`Plate.JsonMethod` / `BasicMethod.Declarative.Ledger` / `StructuralWalk`）。 |
| **不做（防越界）** | ① 不改判据语义与诊断文案（棘轮守卫必须保持逐字不变）；② 不改 `schemas\*.schema.json` 物理真源位置；③ 不动内核执行分支（`Strategy.Observability` 零改动，除非本方案显式要求消费侧转发）。 |
| **动机** | 现状：一个 `impl` 字段的元数据（名 / 键 / 类型 / 词表 / 子键 / 位序）被 **5 张手写表 + 3 套手写机制**重复表达（§1）。新增字段 = 改 6~8 处，靠测试对拍勉强同步——这是「零散零碎」的机器根因。 |

---

## §1 现状取证（实测）

### 1.1 五张手写元数据表（描述同一批 `*Standard` 模型）

| # | 表 | 位置 | 规模 | 内容 |
|---|---|---|---|---|
| **T1** | 键白名单 | `PortImplVocabulary.Keys.cs` / `.Keys.Expr.cs` / `.Keys.Scripted.cs`（3 partial） | 约 22 张键表；`KeysOf` 分派 26 项 | 层 → JSON 键集（读取器据此 fail-fast；schema 据此 `additionalProperties:false`） |
| **T2** | 表达式成员槽 | `ExprLayout.cs`（`ExprSlot` / `ExprSlotKind` / `PortImplVocabulary.ExprSlots`） | 约 65 成员逐项登记 | 成员名 ↔ 平铺键 ↔ 下钻形态 ↔ 算子组 ↔ 嵌套子键 |
| **T3** | 结构门绑定 | `SchemaEnumSynchronizer.Bindings` / `PropertiesBindings` | 18 词表绑定 + 1 键集绑定 | 词表 → `$defs/*/enum`；键集与键序 → `$defs/expr/properties` |
| **T4** | 家族落点 | `StandardCompleteness.FamilyTable` → `Landings` / `CompletenessLanding` | 约 14 家族 | 家族 / 词 → schema 指针 → 模型成员 → 读取探针 |
| **T5** | 镜像配对 | `StandardMirrorEmit.MirrorPlan`（当前藏于守卫 `SpecPortMirrorGenerationTests.Pairs`） | 约 25 对 | 标准 ↔ 中立 DTO ↔ 内核声明（**位序即语义**） |

### 1.2 三套手写机制

| # | 机制 | 位置 | 规模 |
|---|---|---|---|
| **M1** | 读取器 | `StandardJson.cs` + `StandardJson.Impl.cs` + `StandardJson.Impl.Scripted.cs` | **约 1435 行 / 44 方法**（`Scripted` 单文件 1016 行） |
| **M2** | 遍历器 | `StandardNodeWalk.cs` + `ExprStandard.Walk.cs` + `ExprSlots` 反射取值 | 3 处，各写各的下钻规则 |
| **M3** | 映射器 | SpecHost `DtoBinding.g.cs` + `Agent.TestShared\AgentPortStandardProjection.g.cs` + 内核 `PortSpecCompiler` | 已部分生成（v1.4），内核侧 `MapArgs` 仍手写 |

### 1.3 根因陈述

> 同一模型成员的元数据在 **T1–T5 五处 + M1–M3 三处**被重复表达。各处的「一致性」由**新写的对拍用例**兜底（`SchemaEnumSynchronizer.Verify` / `ExprSlots` 反射对拍 / 镜像只读对拍），而非由**构造**保证。这是「每加一个方面就要再补一张表 + 一条对拍」的零碎循环。

---

## §2 目标架构：标准声明描述符（Standard Declaration Descriptor, SSD）

### 2.1 描述符模型（唯一真源）

```csharp
// 层描述：一个 JSON 对象层（impl / arg / dep / call / step / scope / deliver / valueSource / expr …）
public sealed record LayerDescriptor(
    string                           Layer,      // 层名（= KeysOf 的 level）
    Type                             Model,      // 承载类型（PortImplStandard / ScopeStandard / ExprStandard …）
    IReadOnlyList<MemberDescriptor>  Members,    // 成员表（声明序 = 位序 = 结构门键序）
    bool                             OpenMap = false);   // bind / scenarios.cases（键为不透明名，不做闭合）

// 成员描述：模型成员 ↔ JSON 键 ↔ 形态 ↔ 词表
public sealed record MemberDescriptor(
    string      Member,                          // CLR 属性名（反射可对拍）
    string      JsonKey,                         // 平铺 JSON 键
    MemberKind  Kind,                            // 形态（见下）
    string?     Vocabulary   = null,             // 词表引用（PortImplVocabulary 成员名）
    bool        Required      = false,           // 结构门 required / 读取期必填
    string?     ChildLayer    = null,            // Child / Children 指向的层名
    IReadOnlyList<string>? NestedKeys = null,    // 对象形态内读取的子键（如 fold{over,init,step}）
    string?     OperatorGroup = null,            // 表达式算子组（C10「算子组恰一」取值域）
    Polymorphy? Polymorphy    = null);           // 长 / 短双形态（first / last / agg / filter / dict）

public enum MemberKind
{
    Scalar,     // 标量 / 字符串（原样）
    Enum,       // 词表枚举（须 ∈ Vocabulary）
    Child,      // 单子对象 → ChildLayer
    Children,   // 子对象序列 → ChildLayer
    KeyedMap,   // 键值映射（开放映射或定键）
    Payload,    // 原样 JSON 载荷（value / literal / default / rows …，不解释内部）
    Opaque,     // 保留手写：本质语义（自递归来源 / 长短形态判别 / 跨字段条件），由命名钩子接管
}
```

### 2.2 派生关系总图

```
                    ┌───────────────────────────────────┐
                    │  SSD：LayerDescriptor 表            │  ◀── 唯一手写真源
                    │  （Protocol\Standards\Descriptors） │
                    └──────────────────┬────────────────┘
     ┌───────────────┬─────────────────┼──────────────────┬───────────────┐
     ▼               ▼                 ▼                  ▼               ▼
 通用读取器       键白名单          结构门生成          通用遍历器      镜像/映射生成
 （取代 M1）      （取代 T1）        （取代 T3）         （取代 M2）     （取代 T5 / M3）
                                                        │
                                                        ▼
                                              完备性/关系图（取代 T4）
```

**关键**：T4（`Landings`）与关系图**不再手写**，而由 SSD 投影：
- 词源 = `MemberDescriptor.Vocabulary`
- 结构门指针 = `Layer + Member.JsonKey`（如 `$defs/{Layer}/properties/{JsonKey}/enum`）
- 模型落点 = `Layer.Model` + `Member.Member`
- 家族名 = `Vocabulary` 名

---

## §3 五张表 → 一张描述符 的映射（逐项）

| 现表 | 现字段 | → SSD 字段 / 派生方式 |
|---|---|---|
| **T1** `ImplKeys` / `DepKeys` / … | `["kind","id", "target","method", …]` | `LayerDescriptor("impl").Members[].JsonKey`；键集 = `Members.JsonKey ∪ NestedKeys`（保序去重） |
| **T2** `ExprSlot` | `Member` / `Key` / `NestedKeys` / `Kind(ExprSlotKind)` / `OperatorGroup` | `MemberDescriptor` 同名五字段（`ExprSlotKind` 并入 `MemberKind`：`Source→Child`+专属子层、`Parts/Chain/Invoke→Children` 特例、`None→Scalar/Payload`） |
| **T3** `EnumBinding` | `SchemaFile` + `Pointer` + `Vocabulary()` | **派生**：指针 = `$defs/{ChildLayer}/properties/{JsonKey}/enum`；词表 = `Vocabulary`；`SchemaFile` 按层归属 |
| **T3** `PropertiesBinding` | `$defs/expr/properties` ↔ `ExprKeys` | **派生**：层 `expr` 的键集 = 其 `Members.JsonKey ∪ NestedKeys`（= 现 `ExprOperatorKeys ∪ ValueSourceKeys`） |
| **T4** `Family` / `CompletenessLanding` | `Family` / `Word` / `SchemaFile` / `SchemaPointer` / `ModelType` / `ModelMember` / `HasReaderProbe` | **派生**：见 §2.2 四条；`HasReaderProbe` = `Vocabulary != null`（有词表即应有解析探针，探针信封由 `MemberDescriptor` 的层/键生成）。 **【v1.2 落地】** 11 条词表成员族经 `VocabularyFamily` 由 SSD 锚点派生（词集取 `SchemaEnumSynchronizer.EnumBindings` 唯一源 / 指针 / 模型落点 / 结构门文件）；`HasReaderProbe` 实测**保持** `WordProbe is not null`（`dep.form` 有探针但无词表 ⇒ 改之则关系图 `WordReader` 边漂移）；残留 2 条层级特例（`expr` 成员可承载多键 / `dep.form` 键集非词表） |
| **T5** `MirrorPair` | `Standard` / `MapperTarget` / `MapperMethod` | **半派生**：字段位序 = `Members` 声明序（自动）；`MapperTarget` / `MapperMethod` 属映射目标（不属标准层）→ 保留为**数据文件**（`*.data.json`），非硬编码 |

> **映射不变式**（对拍判据）：转换完成后 `SSD 派生的键集 / 指针 / 词表` 与迁移前**逐字相等**——零放宽、零收紧，由既有 `StandardImplSchemaTests` / `SchemaEnumSynchronizer.Verify` / `ExprSlots` 反射对拍三线自证。


---

## §4 通用引擎设计

### 4.1 通用读取器（取代 M1）

- **驱动**：遍历 `LayerDescriptor.Members`，按 `Kind` 分派 `ReadScalar / ReadEnum / ReadChild / ReadChildren / ReadKeyedMap / ReadPayload`。
- **未知键 fail-fast**：键集 = `Members.JsonKey ∪ NestedKeys`（取代 `KeysOf` + `EnsureLayerKeys` 的手抄表；`EnsureKnownKeys` 仍是唯一收集/抛出体）。
- **诊断文案**：`未知键 '{context}.{name}'（fail-fast…）` 逐字沿用；context 由层路径 + 成员键拼装。
- **必填 / 形态**：`Required` 缺则抛（文案沿用 `[port:standard]` 前缀）；`Enum` 校验 `Vocabulary`；`Child` 要求对象、`Children` 要求数组。
- **半规则层钩子**：`Opaque` 成员与 `Polymorphy` 声明的长短双形态，转 `ReaderHooks`（命名钩子，见 §7）。
- **迁移法**：**两段式**——先并行运行（手写读取器保留），跑 `描述符读取 ≡ 手写读取` 逐键对拍；全绿后再删 `StandardJson.Impl*.cs` 手写分支。

### 4.2 通用遍历器（取代 M2）

- 下钻规则**唯一**：`Kind ∈ {Child, Children}` 即下钻；`Payload / Opaque / Scalar / Enum` 不下钻。
- `expr` 层的 `OperatorGroup` 用于 C10「算子组恰一」判定（取值域来自 SSD，而非文档）。
- 合并 `StandardNodeWalk`（步骤 / 取值来源侧）与 `ExprStandard.Walk.cs`（表达式侧）为**同一遍历入口**；`ExprStandardWalker` 由反射改为描述符驱动（反射仅保留对拍自证）。

### 4.3 通用结构门生成器（扩展 T3 机制）

- `properties` 由 `Members`（保序）生成；`additionalProperties:false`（`OpenMap=true` 层除外）。
- `enum` 由 `Vocabulary` 生成。
- **反转**：`SchemaEnumSynchronizer` 不再持有手写 `Bindings` / `PropertiesBindings`，改为**由 SSD 派生绑定列表**；`Verify()`（只读对账）与 `Sync()`（原文范围替换、保排版、幂等）**原样保留**。

### 4.4 通用镜像生成器（补齐 T5 / M3）

- `MirrorPlan` 落为数据文件（`*.data.json`），`StandardMirrorEmit` 消费之（对齐 v1.4「未做」登记的离线工具前置条件）。
- 字段位序由 `Members` 派生 → 消除 D2「按位构造 = 静默错位」风险面。

---

## §5 分批实施（操作级）

> 每批**串行**、独立可回退；每批先「并行对拍」、绿了再删旧。判据命令见 §6。

> **剩余步骤的操作级明细见** [`DataCenter-描述符单源化-下一步执行清单.md`](./DataCenter-描述符单源化-下一步执行清单.md)（B-3b-1 / B-3b-2 / B-3b-3 / 批 C / 批 D / 批 E 六段，含改动面 / 判据 / 回退 / 风险 / 验收命令）。

### 批 A（止血；0 行为变更）—— 已按 2026-10-07 全仓 grep 取证校正

| 项 | 位置 | 取证结论（2026-10-07 实测） | 动作（校正后） |
|---|---|---|---|
| A1 | `PortImplVocabulary.Keys.Expr.cs` `KeysOf` | **有运行期消费方**：`StandardJson.EnsureLayerKeys`（`StandardJson.cs:127`）+ `StandardJson.Impl.Scripted.cs:542` → **不是死代码**。U1 的「无运行时消费方」已被 v1.1 P2 接线推翻（`EnsureLayerKeys` 取值域 = `KeysOf`） | **不可删**；改为在批 B 中**由描述符派生键集**（保留 `KeysOf` 为派生面） |
| A2 | `StandardCatalog.FindFake`（U2） | **确认死链**：`FindFake` 仅 `StandardCatalog.cs:128` 定义处，全仓 `.cs` 无消费方；`ReadFakes` 仅由 `Load` 调用（用于 C1/C6 键与版本） | 登记「去留裁决」；**本批不删**（public API，删需跨解验证），补注记 |
| A3 | `PortValueOptions.IsKnownConversion`（U4，Observability） | **确认死壳**：仅 `PortValueOptions.cs:41-42` 转发 `PortImplVocabulary.IsKnownConversion`，无消费方 | 登记「待删」；本批不删（跨程序集 public API），进入 U 线独立批 |
| A4 | `schemas\fakes.schema.json`（E1） | **已落地**：当前 enum = `[\"recording\",\"counting\",\"fixed\",\"sequence\",\"notExpected\"]`，与 `PortImplVocabulary.FakeKinds` **逐字一致**（幽灵词 `echo` 已在 v1.1 P0 清除） | **无需动作**（登记为已消除） |

- **判据**：`grep 消费方 = 0`（现仅 A2/A3 成立）；全量测试 = 基线；`0 错 0 警`。
- **风险**：极低（校正后本批实际只剩「补注记 / 登记」，无代码删除）。
- **教训（登记）**：旧审计的「无消费方」会在后续接线后过时——**批 A 的每一步以当期全仓 `.cs` grep 复核为准**，不直接采信文档结论。

### 批 B（描述符化·读取侧；取代 M1 / T1）

1. 新增 `Protocol\Standards\Descriptors\`：`LayerDescriptor` / `MemberDescriptor` / `MemberKind` / `Polymorphy` + **全层描述符表**（常规层全量）。
2. 新增 `Standards\StandardReader`（通用读取器）+ `ReaderHooks`（半规则层钩子）。
3. 对拍：`描述符读取 ≡ 手写读取`（产品台账 10 + 测试台账 110 端口），逐键全绿。
4. `KeysOf` / `Keys*` 改由描述符派生；`EnsureLayerKeys` 保留为闭包单源。
5. 删 `StandardJson.Impl.cs` / `StandardJson.Impl.Scripted.cs` 手写分支（仅留 `ReadPorts/ReadData/ReadEvents/ReadFakes` 四视图装配 + 原语委托）。

- **判据**：对拍全绿；装载 **0 违例**；诊断文案与异常类型**逐字不变**。

#### 批 B 进度（2026-10-07，B-1 / B-2 已落地）

| 步骤 | 交付 | 判据 |
|---|---|---|
| **B-1** | `DataCenter.Protocol\Standards\Descriptors\StandardDescriptor.cs`：描述符内核（`MemberKind` / `Polymorphy` / `MemberDescriptor` / `LayerDescriptor`），纯数据、零机制、additive | `DataCenter.Protocol` **0 错 0 警**（2.07s） |
| **B-2** | `StandardDescriptors.cs`：描述符表（**25 层** = 21 干净单模型层 + `valueSource` / `step` / `capture` / `file`），成员↔键↔形态↔词表↔子层；内核含 `AliasKeys`（简写键）/ `Owner`（扁平多模型）/ `Ordered`（键序语义标志） | 新增 `StandardDescriptorTests` **4/4 通过** |
| **B-2 判据** | ① 成员存在于归属模型（反射，`Owner` 感知）；② 层键集 = T1 键表（`Ordered` 逐位 / 多形态集合等价）；③ 层名/KeysLevel 唯一；④ 判据自证（移除成员必红） | 全绿 |
| **B-3a 未登记** | 仅 `expr`（其键 = `ValueSourceKeys` ∪ `ExprOperatorKeys`，由 `ExprSlots` 承载；待后续批把描述符作为算子源反转） | 待 §批 C 反转 |
| **B-3b（通用读取器，首片）** | `StandardReader.cs`（引擎：描述符驱动 + 反射构造 + 子层递归 + `MemberHook` 钩子位）+ `StandardDescriptorReader.cs`（公面门面）；对拍 `StandardDescriptorReadTests`：自包含语料上 **`描述符读取 ≡ 手写读取`**（序列化逐字相等），**不依赖任何 `.specs`** | **2/2 通过**；全量 **105/5/2**（失败 = 既有 5，0 新增） |

**批 B 首次运行即暴露的既有缺口（登记待裁决）**：
- **`ComposedArgKeys` 未登记进 `KeysOf`** → `StandardJson.Impl.cs:284` 的 `EnsureLayerKeys(nameof(ComposedArgKeys), …)` **静默空转**，`composedArg` 层**实际无未知键闭合**。对拍据此改以「键表字段」为 T1 真源。是否补齐闭合为独立裁决（属行为变更，不在 additive 批内）。

**基线快照（2026-10-07 实测）**：`DataCenter.Standards.Tests` = **103 通过 / 5 失败 / 2 跳过 / 共 110**。5 个失败**均为既有**（与本批无关）：4 个 `SchemaEnumSynchronizerTests.Sync_*`（仓库 `ports.schema.json` 的 `$defs/expr/properties` 与生成器**排版不幂等**、`scope.kind` enum 修复计数）；1 个 `StandardsDiagnosticsGuardTests.VocabularySource_IsSingleFamily`（词表定义点预算 `18` ≠ 实测 `16`）。**批 B 增量 = +4 通过、0 新增失败。**


### 批 C（描述符化·遍历侧 + 结构门 + 完备性；取代 M2 / T2 / T3 / T4）

1. 通用遍历器落地；删三套手写遍历（C8/C10/C11 违例集 = 基线）。
2. `SchemaEnumSynchronizer` 绑定反转为 SSD 派生；`DC_SCHEMA_SYNC` 再生成 → schema `Verify()` 全绿（键集/键序/enum 逐字保留）。
3. `StandardCompleteness.Landings` 改由 SSD 投影；六向差集**无新增**；`StandardRelations` 关系图随之（唯一取数口不变）。

- **判据**：遍历域对拍；schema 生成幂等；完备性差集 = 基线。

### 批 D（镜像收口；取代 T5 / M3）

- `MirrorPlan` → 数据文件；生成链跑通；删残余手写映射（内核 `MapArgs` 视裁决）。

- **判据**：只读对拍（产物 ≠ 生成即红）；端到端对拍。

### 批 E（收口）

- 文件拆分策略统一（partial 仅按「过 500 行 / 正交子域」切）；机制件与声明件分目录。
- 程序集拆分（`Protocol` 图契约 vs `Protocol.Standards`）——见 §8-Q3，建议**独立立项**。


---

## §6 判据与验收

| # | 判据 | 命令 / 证据 |
|---|---|---|
| J1 | 编译：`MainProj.sln` 与 `DataCenter.slnx` **0 错 0 警** | `dotnet build` 双解 |
| J2 | 描述符 ↔ 模型 **反射逐位对拍**（成员名 / 序 ≡ 属性名 / 序） | `DataCenter.Standards.Tests` 新增用例 |
| J3 | 读取对拍：`描述符读取 ≡ 手写读取`（120 端口逐键） | 批 B 用例 |
| J4 | 遍历域对拍：C8/C10/C11 违例集 = 基线 | 批 C 用例 |
| J5 | 结构门：`SchemaEnumSynchronizer.Verify()` 全绿 + `Sync()` 幂等 | 既有 `StandardImplSchemaTests` |
| J6 | 完备性：六向差集**无新增**；关系图悬空 = 基线 | `StandardCompleteness` 基线条目 |
| J7 | 装载：产品台账 + 测试台账 **0 违例** | `StandardCatalog.LoadValidated` |
| J8 | 文案棘轮：判据与诊断文案**逐字不变**（含 `impl.kind` 渲染、未知键文案） | 既有守卫 |
| J9 | 镜像：只读对拍（产物 ≠ 生成即红） | `SpecPortMirrorGenerationTests` |

**验收口径**：每批收口跑对应判据 + 全量回归（`DataCenter.Standards.Tests` / `Observability.Tests` / `Agent.Spec.Guard.Tests`）与既有基线**逐条同名**（允许已有失败集不扩大，不允许新增）。

---

## §7 风险与取舍

| 风险 / 取舍 | 处置 |
|---|---|
| **读取器重写破坏文案 / 分派口径** | 两段式（并行对拍 → 删旧）；J8 文案棘轮守卫不破 |
| **过度通用**：`args` / `expr` / `valueSource` 的长短双形态、`table/presence` 自递归来源、跨字段条件（C7–C14）**本质是语义** | **分级覆盖**：① 常规层（deps / calls / scope / deliver / save / collect / file / capture / scenario / init / chain / concatPart / fieldExpr）全描述符驱动；② 半规则层（args / valueSource / step / expr）描述符 + `Polymorphy` + 命名钩子；③ `Opaque` 成员保留手写。**不追求 100% 消除钩子**——通用性收益来自 ① ②，③ 强行通用化会得不偿失 |
| **描述符可读性下降** | 描述符即文档：保留注释列（JSON 键 → 语义）；`MemberKind` / `Vocabulary` 自解释 |
| **大改期间守卫失效** | 每批独立提交 + 完整回归；红灯即回退该批 |
| **判据（C7–C14）是否也描述符化** | **否**。判据是**跨项 / 跨字段关系**（schema 无法表达，见 `StandardImplConsistency` 注释），保留手写；描述符只提供其消费的键 / 词表 / 遍历，不替代其逻辑 |

---

## §8 未决问题（含建议默认值）

> 以下三条影响批 B 之后的排期与放置；本草案给出**建议默认值**，标注「待确认」。

| # | 问题 | 建议默认值（待确认） |
|---|---|---|
| **Q1** | 描述符（数据）放哪层？ | **随模型放 `DataCenter.Protocol\Standards\Descriptors\`**（纯数据、零机制，任何消费方可反射读取）；**引擎**（读取 / 遍历 / 生成）放 `DataCenter.Standards`，可下沉 `BasicMethod.Declarative.*` 者下沉。理由：描述符是「模型元数据」而非「机制」，不违 `Protocol` 纯契约定位。 |
| **Q2** | 分级阈值？ | **跨 ≥3 表登记 或 字段/子键 ≥6 → 描述符化**；否则保持手写（如 `FakeStandard` 4 字段不必）。 |
| **Q3** | 批 E 程序集拆分是否纳入本次？ | **不纳入**，独立立项（纯移动 + 改命名空间，牵连烘焙清单 / `Test-ProjectReferences`，风险与收益应单独评估）。 |

---

## 附录 A：与既有文档的关系（禁重复维护）

| 文档 | 覆盖 | 本文档的关系 |
|---|---|---|
| `DataCenter-标准单源化-重复逻辑收敛-设计与实施.md` | D1–D13 / U1–U7 / E1–E8 审计与 P0–P4 收敛 | 本文档 = 其**共同上游**收敛（把 T1–T5 五表合一） |
| `DataCenter-标准框架通用化重构-设计与实施.md` / `-下一阶段实施计划.md` / `-下一步执行清单.md` | 标准模型上收 / C7–C13 / 内核退场 / M2–M4 | 本文档复用其分层与红线，不重述机制搬迁 |
| `DataCenter-声明式框架提取-框架优先设计与实施.md` | 台账机制搬迁到框架件 | 本文档的通用引擎可沿用其「框架件 + 消费」模式 |

---

## 修订记录

| 版本 | 日期 | 说明 |
|---|---|---|
| v0.1 | 2026-10-07 | 首版草案：① §1 现状取证（T1–T5 五表 + M1–M3 三机制）；② §2 SSD 描述符模型与派生总图；③ §3 五表 → 一描述符逐项映射（含不变式）；④ §4 通用引擎（读取 / 遍历 / 结构门 / 镜像）；⑤ §5 批 A–E 操作级；⑥ §6 判据 J1–J9；⑦ §7 分级覆盖取舍（关键：不追求消灭所有钩子）；⑧ §8 三条未决问题的建议默认值。**状态：草案，待裁决 §8 后开工批 A。** |
| v0.2 | 2026-10-07 | **批 A 取证校正（全仓 `.cs` grep 实测）**：① **A1 推翻**——`KeysOf` 有运行期消费方（`StandardJson.EnsureLayerKeys` / `Scripted.cs:542`），U1「无运行时消费方」已过时（v1.1 P2 接线所致）→ 改为批 B 由描述符派生，**不可删**；② A2 `FindFake` 确认死链（仅定义处）；③ A3 `PortValueOptions.IsKnownConversion` 确认死壳（仅转发）；④ A4 `fakes.schema.json` enum 已与 `FakeKinds` 逐字一致（E1 已消除）→ 无需动作。校正后批 A 实际只剩「补注记 / 登记」，无代码删除。**教训登记：旧审计结论须以当期 grep 复核，不直接采信。** 另记：`search_codebase` 索引未覆盖 `DataCenter\*\*.cs`，取证须用文件系统 grep。 |
| v0.3 | 2026-10-07 | **批 B 开工：B-1 + B-2 已落地（additive，零行为变更）**。<br>**B-1**：新增 `DataCenter.Protocol\Standards\Descriptors\StandardDescriptor.cs` —— 描述符内核 `MemberKind`（Scalar/Enum/Child/Children/KeyedMap/Payload/Opaque）+ `Polymorphy` + `MemberDescriptor`（成员↔键↔形态↔词表↔子层↔算子组，含 `DeclaredKeys`/`IsChild`）+ `LayerDescriptor`（`Layer` 规范层名 / `KeysLevel` 键表 level / `Model` / `Members` / `OpenMap`，含 `Keys` 派生与 `MemberDescriptor.DeclaredKeys`）。<br>**B-2**：新增 `StandardDescriptors.cs` —— 描述符表首版 **8 层**（impl/arg/dep/call/composedField/composedArg/fakeCall/fakeCapture），成员名与承载类型同名、键集与 T1 键表逐字对齐。<br>**判据**：`DataCenter.Protocol` **0 错 0 警**；新增 `StandardDescriptorTests` **4/4 通过**（成员反射 / 键集逐字等价 / 层唯一 / 判据自证）。<br>**发现（登记待裁决）**：① **`ComposedArgKeys` 未登记进 `KeysOf`** → `StandardJson.Impl.cs:284` 的 `EnsureLayerKeys(nameof(ComposedArgKeys))` **静默空转**（`composedArg` 层无未知键闭合）——由对拍首次暴露；对拍据此改以「键表字段」为 T1 真源，是否补齐闭合留独立裁决。<br>**基线快照**：`DataCenter.Standards.Tests` = **103 通过 / 5 失败 / 2 跳过（共 110）**；5 失败均**既有**（4 × `SchemaEnumSynchronizerTests.Sync_*` 因仓库 `ports.schema.json` 的 `$defs/expr/properties` 与生成器**排版不幂等**；1 × `VocabularySource_IsSingleFamily` 预算 18≠16）。批 B 增量 = +4 通过、0 新增失败。<br>**未做（下一步）**：批 B-3（通用读取器 `StandardReader` + `ReaderHooks` + 读取对拍装 120 端口）、描述符表补齐其余层（step/scope/deliver/valueSource/new/invoke/foreach/throw/collect/file/save/chain/init/delegate/concatPart/fieldExpr/scenario/capture/expr）。 |
| v0.4 | 2026-10-07 | **批 B-3a：描述符表扩至 21 层（全部干净单模型层）**。<br>新增登记：`throw` / `collect` / `chain` / `init` / `save` / `concatPart` / `fieldExpr` / `scope` / `deliver` / `scenario` / `new` / `delegate` / `invoke`（13 层）+ B-2 的 8 层 = **21 层**。<br>**`invoke` 的 `on` 键已解**：读 `ReadInvoke`（`Scripted.cs:430-432`）确证 <c>on</c> ↔ <see cref="InvokeStandard.Value"/>（对表达式值调用，经 `ReadArg`）；故 `invoke` 亦为干净单模型层。<br>**判据**：`DataCenter.Protocol` / `DataCenter.Standards` **0 错 0 警**；`StandardDescriptorTests` **4/4 通过**（键集逐字等价覆盖 21 层）。<br>**剩余未登记（半规则层，待 `Polymorphy` + 钩子）**：`step` / `capture`（**扁平多模型**：`step.items/where/do` 属 `ForeachStandard`、`capture.call/as/dep/…` 属嵌套 `InvokeStandard`）；`valueSource`（40 键 vs 30 成员 = `from:arg/with/var/item/literal/…` 长短形态简写）；`file`（`NameExpr` 无独立键）；`expr`（值源键 ∪ 算子键）。<br>**未做（下一步 B-3b）**：通用读取器 `StandardReader` + `ReaderHooks` + 读取对拍（120 端口 `描述符读取 ≡ 手写读取`）。 |
| v0.5 | 2026-10-07 | **批 B-3a 收口：描述符单源覆盖 25 层（除 `expr` 外全部）**。<br>**内核扩面（`StandardDescriptor.cs`）**：`MemberDescriptor` 增 `AliasKeys`（简写键 → 同一成员，如 `arg`→<see cref="ValueSourceStandard.Index"/>、`with/var/slot/item`→<see cref="ValueSourceStandard.Key"/>、`literal`→<see cref="ValueSourceStandard.Value"/>、`table`→<see cref="ValueSourceStandard.Rows"/>、`presence`→<see cref="ValueSourceStandard.Items"/>、`map`→<see cref="ValueSourceStandard.Pairs"/>、`new`→<see cref="ValueSourceStandard.Construct"/>）与 `Owner`（扁平多模型层的成员归属）及 `ModelOf`；`LayerDescriptor` 增 `Ordered`（键序语义标志；多形态/扁平层为 false，对拍按键集合）。<br>**新增 4 层**：`valueSource`（30 成员 / 40 键，`Ordered=false`）、`step`（扁平跨 `StepStandard`+`ForeachStandard`+`ValueSourceStandard`）、`capture`（扁平跨 `DeliverCaptureStandard`+`InvokeStandard`）、`file`（`NameExpr` 无独立键）。<br>**判据**：`DataCenter.Protocol` / `DataCenter.Standards` **0 错 0 警**；`StandardDescriptorTests` **4/4 通过**（成员反射含 Owner 感知 / 键集有序+集合双模式覆盖 25 层 / 层唯一 / 判据自证）。<br>**未登记（1 层）**：仅 `expr`（键 = `ValueSourceKeys` ∪ `ExprOperatorKeys`，由 `ExprSlots` 承载，留 §批 C 反转）。<br>**未做（下一步 B-3b，本批主体）**：通用读取器 `StandardReader` + `ReaderHooks` + 读取对拍（120 端口 `描述符读取 ≡ 手写读取`）后删手写读取器。 |
| v0.6 | 2026-10-07 | **批 B-3b 阻塞登记（外部，非本方案缺陷）**：`DataCenter.Standards.Tests` 全量 = **101 通过 / 7 失败 / 2 跳过**。7 失败分三类：① **5 个既有**（4 × `SchemaEnumSynchronizerTests.Sync_*`：仓库 `ports.schema.json` 的 `$defs/expr/properties` 与生成器**排版不幂等**、`$defs/scope/properties/kind/enum` 修复计数；1 × `StandardsDiagnosticsGuardTests.VocabularySource_IsSingleFamily`：词表定义点预算 18≠16）；② **2 个由外部工作区改动所致**：`TestLedger_FullImplSurface_IsReadable` / `TestLedger_ScriptedSurfaces_AreReadable` 因 `Environment\CentralTest\AgentTest\Agent.TestShared\Standards\ports\NodeProcess.specs` **未提交修改**（`git diff` +44/−19）导致 `scenarios` JSON 区块未闭合（`AT1002`），**与批 B 改动无关**（本方案未触碰任何 `.specs`）。<br>**对 B-3b 的影响**：读取对拍（120 端口 `描述符读取 ≡ 手写读取`）依赖测试台账可装载；在该 `.specs` 修复前，**B-3b 的判据门无法运行**。修复该 `.specs` 为独立事项（属测试台账数据，非本方案范围）。<br>**本批代码增量的判据实测**：**新增 0 个因本方案代码引起的失败**；描述符对拍 4/4 全绿。 |
| v0.7 | 2026-10-07 | **批 B-3b 首片落地：通用读取器引擎 + 读取对拍（自包含，零 `.specs` 依赖）**。<br>**引擎（`DataCenter.Standards\StandardReader.cs`）**：`StandardReaderEngine` 由 `LayerDescriptor` 驱动——反射取承载类型主构造器，按 `MemberKind` 分派（Scalar/Enum/Payload/Child/Children/KeyedMap），子层经注册表递归；`MemberKind.Opaque` 与半规则成员经 `MemberHook` 钩子位外置（**不写死**）。<br>**公面（`StandardDescriptorReader.cs`）**：`ReadImpl(JsonElement)` / `Read(layer, JsonElement)`。<br>**对拍（`StandardDescriptorReadTests`，自包含语料，不依赖任何外部 `.specs`/台账）**：反身端口语料上 **`描述符读取 ≡ 手写读取`**（`PortImplStandard` 序列化**逐字相等**）——覆盖 `impl`/`arg`/`dep`/`composedArg` 常规层；另含语义自证（`Kind`/`Target`/`Args.Count`/`Deps.Count`）。**2/2 通过**。<br>**口径修正确认**：`.specs` 与 DataCenter 改动**无关**（v0.6 的"阻塞"表述已作废）；读取对拍改用自包含语料，**与测试台账解耦**。全量 `DataCenter.Standards.Tests` 恢复 **105 通过 / 5 失败 / 2 跳过（共 112）**，失败 = 既有 5，**0 新增**。<br>**未做（B-3b 余下）**：① 半规则层钩子（`step` 四形态 / `valueSource` 的 `expr` 子节点 / `fakeCall.HasValue` 派生位 / 缺省归一 `[] vs null`）；② 对拍语料扩至全部层与真实台账；③ 全绿后删手写读取器的常规层分支（两段式第二段）。 |
| v0.8 | 2026-10-07 | **批 B-3b-1 + B-3b-2 落地：半规则层钩子 + 对拍语料扩容（纯增量，未删旧）**。<br>**引擎扩面（`StandardReader.cs`）**：① 增「未处理」哨兵 `Unhandled`（`null` 现表「已处理且结果为 null」，修复原 `null=未处理` 二义）；② `MemberHook` 改版（增 `found`/`container`，先于「键缺失即默认」判定 → 无键 / 派生位成员可接管）；③ 新增 `LayerHook`（整层接管，如 `step` 四形态）；④ `MemberKind.Child` 未登记子层（`expr`/`foreach`）经层钩子；⑤ `MemberDescriptor.EmptyOnAbsent`（`Children`→空序列 / `KeyedMap`→空映射）复现手写 `[] vs null` 口径。<br>**钩子件（新增 `StandardReaderHooks.cs`）**：`impl` 成员 kind 条件分派（scripted/composed 门控；`Binds`/`Script` 交引擎递归，`ScriptProject`/`Scopes`/`Deliver`/`Scenarios` 整值委托）；`step`→`ReadStep`、`valueSource`→`ReadArg`、`expr`→`ReadExpr`、`foreach`→`ReadForeach`；`fakeCall.HasValue` 派生位。组合根 = `StandardDescriptorReader` 静态构造自动装配。<br>**描述符表**：`Impl.Args/Deps/Calls/Project/Script` 标 `EmptyOnAbsent`；`Impl.Binds` 补 `ChildLayer: valueSource`（键值映射下钻）。手写 `ReadStep`/`ReadForeach` 放宽为 `internal`（**零行为改动**）。<br>**对拍**：`StandardDescriptorReadTests` 由 2 扩至 **5 用例**（反身 / 组合 / 脚本 / 裸 impl + 非平凡自证）；脚本语料覆盖 `step` 四形态（new/invoke/foreach+items/throw/from:map）、`valueSource` 长短形态、`bind`、`deliver`、`scopes`、`scenarios`、`fieldExpr`。<br>**判据**：`DataCenter.Protocol` / `DataCenter.Standards` **0 错 0 警**；`StandardDescriptorTests` **4/4**；`StandardDescriptorReadTests` **5/5**（序列化逐字相等）；全量 `DataCenter.Standards.Tests` = **108 通过 / 5 失败 / 2 跳过（共 115）**，失败 = 既有 5（4 × `Sync_*` + `VocabularySource_IsSingleFamily`），**0 新增**（基线 105 + 本批 3 个新用例 = 108）。<br>**外部事项**：`DataCenter.Standards.Tests` 的 `InvocationResolverTests.cs`（另一并行工作流未提交文件）当前**不编译**（`SpecDiagnosticCodes` 命名空间 / `Share.LogicComposition.Conversion` 引用），验证时临时排除后恢复，**与本方案无关**。<br>**未做（下一步 B-3b-3）**：对拍全绿为前置，删手写读取器的常规层分支（两段式第二段；半规则层保留钩子）。 |
| v0.9 | 2026-10-07 | **批 B-3b-3 落地：删旧（两段式第二段；对拍全绿为硬前置，实测通过后执行）**。<br>**引擎扩面（`DataCenter.Standards\StandardReader.cs`）**：① `MemberHook` / `LayerHook` 增 `context`（键路径），子层上下文按 `{父}.{键}[{i}]` 拼装（`impl` / `args[i]` 等诊断路径与手写逐字一致）；② `ReadLayer` 增**键闭包**（取值域 = `LayerDescriptor.Keys`；收集 / 抛出体仍是 `StandardJson.EnsureKnownKeys` **唯一实现**，`EnsureKnownKeys` 由 `private` 放宽为 `internal`）；③ 增**必填**校验（`MemberDescriptor.Required`）。<br>**入口切换（`StandardJson.Impl.cs` 304 → 51 行）**：`ReadImpl` 仅留 `impl.kind` 归一（必填 / 非法，文案不变）+ `StandardDescriptorReader.ReadImpl(impl, "ports.{key}.impl")`；删常规层装配（`ReadArgs` / `ReadCalls` / `ReadProject` 与旧 `ReadImpl`）。<br>**半规则层钩子件**：`ReadDeps`(+`ReadComposedArgs` / `ReadFakeCalls` / `ReadFakeCapture`) 作为 **`dep` 层钩子**析出至脚本族文件（`dep` 三形态 + 替身子结构；`StandardReaderHooks` 增 `dep` 层钩子）。<br>**棘轮收口**：`StandardsDiagnosticsGuardTests.LayerEntryKeys_UseSharedClosure` 键闭包预算 10 → **9**（常规层闭包上收引擎，手写侧仅余半规则层钩子入口），并增「引擎须经 `EnsureKnownKeys` + `layer.Keys` 闭合」判据（防引擎另写一遍）。<br>**判据（实测）**：`DataCenter.Protocol` / `DataCenter.Standards` / 整个 `DataCenter.slnx` **0 错 0 警**；`StandardDescriptorTests` **4/4**、`StandardDescriptorReadTests` **5/5**、`LedgerGateTests` / `StandardImplReadTests` / `StandardImplScriptedLiteralReadTests` / `StandardsDiagnosticsGuardTests` 全绿；全量 `DataCenter.Standards.Tests` **108 通过 / 5 失败 / 2 跳过（共 115）**，失败 = 既有 5（4 × `Sync_*` + `VocabularySource_IsSingleFamily`），**0 新增**。键闭包调用点 `StandardJson*.cs` **13 → 9**。<br>**遗留（显式登记）**：① `expr` 层仍未登记描述符（键 = `ValueSourceKeys` ∪ `ExprOperatorKeys`），留批 C；② 引擎「必填」诊断文案为通用式，与手写逐项文案（如「缺少 convert」）**不同字但同判**（既有断言面未覆盖；批 C 收口）；③ 场景主体 `scenarios.cases[*]` 仍走手写子读取器。<br>**外部事项**：`DataCenter.Standards.Tests` 的 `InvocationResolverTests.cs`（另一并行工作流未提交文件）仍不编译（`SpecDiagnosticCodes` / `Share.LogicComposition.Conversion`），验证时临时排除后**原样恢复**，与本方案无关。 |
| v1.0 | 2026-10-07 | **批 C 首步：`expr` 层登记（④ keystone，行为中性）**。<br>**描述符表（`Descriptors\StandardDescriptors.cs`）**：新增 `Expr` 层——`Layer="expr"` / `KeysLevel=nameof(PortImplVocabulary.ExprKeys)` / `Model=ExprStandard` / `Ordered:false`；**成员由 `PortImplVocabulary.ExprSlots` 派生**（成员名 / 算子组 / 子槽形态 / 键 / 嵌套键单源，禁另抄）：`ExprSlotKind` → `MemberKind`（None→Scalar / One→Child / Many|Parts→Children / Source|Invoke|Chain→Child），子层名（One|Many→`expr` / Parts→`concatPart` / Source→`valueSource` / Invoke→`invoke` / Chain→`chain`）；`Source` 成员以 `AliasKeys = ValueSourceKeys` 复现「来源键扁平并入」。描述符表 **25 → 26 层**，覆盖 T1 全键表。<br>**读取路径不变**：`ReadLayer` 先经层钩子 → `expr` 层仍由 `StandardJson.ReadExpr` 承担；登记仅使 `Registry` 命中（`ReadChild` 转入 `ReadLayer` 后仍回落钩子）。<br>**对拍测试扩口（`StandardDescriptorTests.DeclaredKeys`）**：由「仅静态字段」扩为「字段 → 派生静态属性」回落（`ExprKeys` 是 `ValueSourceKeys ∪ ExprOperatorKeys` 的派生属性）。<br>**判据（实测）**：`DataCenter.slnx` **0 错 0 警**；`StandardDescriptorTests` **4/4**（26 层）；全量 `DataCenter.Standards.Tests` **108/5/2**（失败 = 既有 5，**0 新增**）。<br>**阻塞登记（续批 C 的前置裁决）**：① `expr` 层**无 `Vocabulary`** —— `ExprSlots` 不含 `aggOp`/`convert`/`onBlankFirstKey`/`onDuplicate` 槽位（这些键经 `ValueSourceKeys` 并入），故结构门 `enum` 绑定 **12/17 可派生，5 条 `expr` 无法派生**；② `fakes.schema.json` 绑定非层，需「非层绑定」口或保留显式登记；③ ① 通用遍历器的合成节点（`AsValueSource`）与 `scenarioBody`（未登记层）须在「`IsChild` 唯一规则」下给出等价表达。 |
| v1.1 | 2026-10-07 | **批 C ② 结构门绑定反转（④ 遗留同批解除；行为等价）**。<br>**契约扩面（`Protocol\Standards\ExprLayout.cs`）**：`ExprSlot` 增可选 `Vocabulary`（成员名 → `PortImplVocabulary` 静态成员）；`AggOp` 槽位登记 `nameof(PortImplVocabulary.AggOps)`。<br>**描述符透传（`StandardDescriptors.ExprMembers`）**：`MemberDescriptor.Vocabulary = slot.Vocabulary`；带词表者 `MemberKind.Enum`（否则 `Scalar`）。<br>**结构门绑定派生（`SchemaEnumSynchronizer.cs`）**：`Bindings` 由 `StandardDescriptors` 派生 —— 遍历 `All` 的 `Members`，每条带 `Vocabulary` 且有 `JsonKey` 者 → 指针 `$defs/{Layer}/properties/{JsonKey}/enum`；`expr` 层**额外并入** `valueSource` 层 enum（其 `from`/`convert`/`onBlankFirstKey`/`onDuplicate` 为扁平并入键）；手抄 18 条指针表退场，**唯余 1 条非层残留**（`fakes.schema.json` 的替身 `kind`）。`VocabularyAccessor`（缓存）反射取静态字段/属性，缺失即抛（层↔词表漂移当场可见）。`PropertiesBindings` 唯一 1 条保持 `$defs/expr/properties ← PortImplVocabulary.ExprKeys`（本为派生属性）。<br>**判据（实测）**：`DataCenter.slnx` **0 错 0 警**；`SchemaEnumSynchronizerTests` 2 通过 / 4 失败（既有基线）/ 1 跳过——`Verify_AllEnumsMatchSource` 全绿、`Sync_DriftedEnum` 明细与基线**逐字相同**（同 2 条：`$defs/expr/properties` 排版 + 注入 `scope.kind`）+ `ExprProperties_KeysAndOrderAreGeneratedFromSource` 全绿；全量 `DataCenter.Standards.Tests` **108/5/2**（失败 = 既有 5，**0 新增**）——派生表与手抄表**行为等价**。<br>**遗留**：`fakes` 非层绑定仍手写；① 通用遍历器（合成节点 `AsValueSource` / `scenarioBody` 未登记层）与 ③ 完备性 `Landings` 投影未开工。 |
| v1.2 | 2026-10-07 | **批 C ③ 完备性 `Landings` 投影落地（行为等价）**。<br>**改动（`DataCenter.Standards\\StandardCompleteness.cs`）**：`FamilyTable` 中 11 条词表成员族改由新增 `VocabularyFamily(name, LayerDescriptor, member, wordProbe[, formProbe])` **由 SSD 锚点派生** —— ① 词集 = `SchemaEnumSynchronizer.EnumBindings`（② 落地的**结构门绑定唯一源**，使「完备性落点」与「结构门绑定」同源、禁另抄）中匹配指针者的 `Vocabulary()`；② 指针 = `$defs/{layer.Layer}/properties/{member.JsonKey}/enum`；③ 模型落点 = `member.ModelOf(layer.Model)` × `member.Member`；④ 结构门文件 = `ports.schema.json`。成员缺 `Vocabulary`/`JsonKey` 或指针无绑定即抛（层↔落点漂移当场可见）。**残留（显式登记）**：族名 + ③ 探针 + 2 条层级特例（`expr` 成员可承载多键 / `dep.form` 键集非词表，无单成员可锚定）；`Landings.HasReaderProbe` 保持 `WordProbe is not null`（设计草稿的 `Vocabulary != null` 会漏 `dep.form`，致关系图 `WordReader` 边漂移）。<br>**判据（实测）**：`DataCenter.Protocol` / `DataCenter.Standards` / 整个 `DataCenter.slnx` **0 错 0 警**；完备性定向 **19 通过 / 0 失败 / 1 跳过**（139 词 / 13 族 / `DeclaredSchemaMissing=0` / `DeclaredSchemaUnregistered=5` / `DeclaredParseMissing=60` / `DeclaredParseUnregistered=65` 逐项 = 冻结基线；关系图悬空 0）；全量 `DataCenter.Standards.Tests` **108/5/2**（失败 = 既有 5，**0 新增**）。**外部事项**：`InvocationResolverTests.cs` 临时移出验证后**原样恢复**（哈希一致、无残留）。 |



