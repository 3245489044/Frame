# DataCenter 声明式通用框架提取 —— 框架优先 · 设计与实施

> 版本：v1.0（2026-09-17）
> 范围：把 `DataCenter.Standards` 中**与端口/数据领域无关的声明式机制**提取为通用框架，
> **先建通用框架（含自有测试），再让 DataCenter 改为消费它**；迁移期对外行为零变化。

---

## §0 一句话结论

> DataCenter 的标准体系（JSON 台账 → 强类型声明 → 结构门 + 语义判据 → 目录）已经是一套完整的
> 「收敛治理型」声明式管道，但其中真正属于领域的只有 24 个 `*Standard` 模型、四个 schema 词表与 C1–C13
> 判据体；其余（fail-fast 读取原语、schema 子集校验、重复键扫描、违规异常、判据宿主、键控目录）
> 都是可复用机制。新建**零依赖**程序集 `BasicMethod.Declarative.Ledger` 承载这些机制，
> 与现有开放组合型 `BasicMethod.Declarative` 概念同族、工程独立；DataCenter 随后切换为消费方，
> 公共 API 与违规文案**逐字不变**。

---

## §1 背景取证

### 1.1 两套声明式设施同构（管道级对照）

| 阶段 | 现有 `BasicMethod.Declarative`（开放组合型） | DataCenter（收敛治理型，手工长成） |
|---|---|---|
| 声明模型 | `IDeclaration`（Name/Description/DeclarationType） | 24 个 `*Standard` record，以 `Key` 标识 |
| 解析 | `DeclarationResolver`：`IDictionary` + 外部 `Func` 工厂 | `StandardJson`：JSON 文本 fail-fast 强类型读取 |
| 校验 | `IConstraint<T>` + 5 种约束（AND/OR/NOT 可组合） | **双门**：`ports.schema.json` 结构门 + C1–C13 语义判据，一次报全 |
| 结果 | `ConstraintResult` / `ValidationResult` | `List<string>` + `StandardViolationException` |
| 注册/目录 | `DeclarationCollection<T>`（名/类型查找、动态增删） | `StandardCatalog`（四视图、Ordinal 确定性排序、原文同行） |
| 声明→运行时 | `IDeclarativeMapper` + `MappingRegistry`（按名注册） | `PortSpecCompiler`（静态编译）+ `PortBinding.TryConstruct` + fail-safe 记账 |
| 关系代数 | Relation/Select/Project/Join/Rename | 无对应物 |

### 1.2 依赖现状（决定切分方式的硬事实）

| 工程 | 依赖 | 约束 |
|---|---|---|
| `BasicMethod.Declarative` | TConfig → CSG.Unified | `IDeclaration` 继承 `TConfig.Declarative.IDeclaration` |
| `DataCenter.Protocol` | 仅 BCL（+ CSG 二进制引用） | 纯契约，零机制依赖 |
| `DataCenter.Standards` | 仅 `DataCenter.Protocol` | ModuleRule 明写「只用 BCL + System.Text.Json」 |

**推论**：DataCenter **不能**直接引用现有 `BasicMethod.Declarative`（会经 TConfig 拖入 CSG.Unified，
违反 L0.5 层纪律）。通用机制必须落在一个**零项目引用**的新程序集。

### 1.3 消费半径（二进制兼容面）

`DataCenter.Standards.dll` 经 HintPath（方案 B 二进制消费）被 4 侧引用：
`DataCenter` 主工程、`Observability.Tests`、`DataCenter.Standards.Tests`、
`Agent.TestShared` / `Agent.Spec.Guard.Tests`。迁移必须保持：
- `StandardConsistency.Check / EnsureValid`、`StandardCatalog.Load / LoadValidated` 签名不变；
- `StandardViolationException` 类型身份不变（消费侧有 catch）；
- 违规文案逐字不变（守卫测试按文案断言）；
- 新增依赖 DLL 须随 `DataCenter\BuildOutput` 烘焙产出（见 §7）。

---

## §2 目标与原则

1. **框架优先**：先独立建成 `BasicMethod.Declarative.Ledger` 并配自有测试；DataCenter 在框架验收后再切换。
2. **零依赖**：框架工程只引 BCL + `System.Text.Json`，不引 TConfig，也不引现有 Declarative 核心。
3. **行为零变化**：切换后 DataCenter 公共 API、异常类型、违规文案、排序口径全部逐字保持。
4. **搬迁而非重写**：机制代码逐字迁移（含中文诊断），只换命名空间与异常类型名；分歧语义（如 `Strings` vs `StrList`）原样保留。
5. **只提机制，不提政策**：判据体、词表、schema、领域模型留 DataCenter；框架只提供判据**宿主**。
6. **不超前抽象**：编译/绑定/未记账账本（`PortComposer` 范式）本次只登记不提取，等第二个台账实例出现再收口。

---

## §3 框架形态

### 3.1 工程布局

```
Frame\CsharpPrj\Plate\
├── JsonMethod\               平台 JSON 唯一权威（JSON 机制归属地）
│   ├── Utilities\JsonTolerantReader.cs        容错读取骨架 + 元素原语（v1.3 上收）
│   ├── Utilities\JsonDuplicateKeyScanner.cs   原文层重复键扫描（v1.3 上收）
│   └── Validation\JsonSchemaSubsetValidator.cs schema 子集结构校验（v1.3 上收）
└── DeclarativeLedger\        收敛治理型（契约台账机制），零项目引用
    ├── DeclarativeLedger.csproj
    ├── DeclarationViolationException.cs
    ├── LedgerCatalog.cs                 单段键控目录（泛型）
    └── Rules\
        ├── ILedgerRule.cs               判据接口（Code + Check）
        └── LedgerConsistency.cs         判据宿主（一次跑全、一次报全）
```

> ★ v1.3 依赖反转（IoC）：JSON 机制**归属平台 JSON 框架 `Plate.JsonMethod`**，使用端（`DataCenter.Standards`）
> 直接复用；本框架只承载台账治理机制（目录 / 判据接口 / 判据宿主 / 违规异常），故仍为**零 ProjectReference**。
> 机制层不抛消费侧异常、不内置域文案：未知键以 `UnknownKeys` / `CollectUnknownKeys(..., messageFactory)`
> 返回违规，异常身份（`DeclarationViolationException` / `StandardViolationException`）与文案留消费方。

- 程序集/命名空间：`BasicMethod.Declarative.Ledger`；`net10.0-windows`，与同族工程一致；
  物理位置 `Frame\CsharpPrj\Plate\DeclarativeLedger\`（模块路径单一真源 = `CsharpPrj\modules.json`）。
- 与 `BasicMethod.Declarative` **无项目引用**（依赖方向不允许 + 哲学不同，见 §5）；
  概念同族仅体现在命名与文档。未来如需桥接（判据→`IConstraint` 适配），另建三方可选桥接工程，本次不做。

### 3.2 API 草案

```csharp
namespace BasicMethod.Declarative.Ledger;

// ① 违规异常（搬 StandardViolationException，逐字）
public sealed class DeclarationViolationException : Exception
{
    public IReadOnlyList<string> Violations { get; }
    public DeclarationViolationException(string message);
    public DeclarationViolationException(string message, IReadOnlyList<string> violations);
}

// ② JSON 台账读取骨架（搬 StandardJson 的机制部分）
public static class JsonLedgerReader
{
    public static T ParseRoot<T>(string json, Func<JsonElement, T> body);   // = Read<T>：Skip 注释 / 尾随逗号 / Root.Clone
    public static bool TrySection(JsonElement root, string name, out JsonElement section);
    public static void EnsureKnownKeys(string context, JsonElement obj, IReadOnlyList<string> known); // 未知键 fail-fast

    // 容错原语（缺失/类型不符 → null 或空；语义逐字保留）
    public static string?  Text(JsonElement e, string name);
    public static double?  Number(JsonElement e, string name);
    public static int?     Int(JsonElement e, string name);
    public static bool?    Bool(JsonElement e, string name);
    public static string?  Str(JsonElement e, string name);
    public static IReadOnlyList<string> Strings(JsonElement e, string name);   // 裸字符串包单元素
    public static IReadOnlyList<string>? StrList(JsonElement e, string name);  // 不接受裸字符串
    public static IReadOnlyList<JsonElement>? Elements(JsonElement e, string name);
    public static IReadOnlyList<string> PathStrings(JsonElement e, string dottedPath);
}

// ③ 重复键扫描（C1 载体：JsonDocument 静默合并重复键，须在原文层扫）
public static class JsonDuplicateKeyScanner
{
    public static IReadOnlyList<string> Scan(string json);
}

// ④ schema 子集结构校验（搬 StandardSchemaValidator，关键字子集零增减）
public static class JsonSchemaSubsetValidator
{
    public static IReadOnlyList<string> Validate(string json, string schemaJson, string? context = "$");
    public static void EnsureValid(string json, string schemaJson, string? context = "$");
    // 支持：type(含数组)/required/properties/additionalProperties/propertyNames.pattern/
    //       items/minItems/minProperties/enum/pattern/$ref(仅 #/$defs/*)/oneOf
}

// ⑤ 单段键控目录（StandardCatalog 四视图的泛化单件）
public sealed class LedgerCatalog<TEntry>
{
    public LedgerCatalog(IReadOnlyList<TEntry> entries, Func<TEntry, string> keySelector, string? rawJson = null);
    public IReadOnlyList<TEntry> Entries { get; }      // 键 Ordinal 升序
    public IReadOnlyList<string> Keys { get; }
    public string? RawJson { get; }
    public TEntry? Find(string key);
}

// ⑥ 判据宿主（StandardConsistency.Check/EnsureValid 的泛化）
public interface ILedgerRule<in TCatalog>
{
    string Code { get; }        // 判据编号，如 "C1"；编号拼在违规文案前缀
    void Check(TCatalog catalog, List<string> violations);
}

public static class LedgerConsistency
{
    public static IReadOnlyList<string> Check<TCatalog>(TCatalog catalog, IEnumerable<ILedgerRule<TCatalog>> rules);
    public static void EnsureValid<TCatalog>(TCatalog catalog, IEnumerable<ILedgerRule<TCatalog>> rules);
    // 失败即抛 DeclarationViolationException（阻断，不 warn；一次报全）
}
```

### 3.3 与现有 Declarative 的概念分工

| | `BasicMethod.Declarative` | `BasicMethod.Declarative.Ledger`（新） |
|---|---|---|
| 气质 | 开放、运行时可注册、可组合 | 收敛、判据只此一处、fail-fast |
| 校验对象 | 单条声明是否满足谓词 | 整本台账的结构与跨项引用完整性 |
| 结果观 | 布尔 + 原因（可 AND/OR/NOT） | 违规字符串列表（带判据编号），阻断装载 |
| 输入 | 内存对象 / `IDictionary` | JSON 文本（强类型、确定性） |
| 依赖 | TConfig | **零项目引用** |

---

## §4 提取清单（逐件归属）

### 4.1 机制 → 框架（搬迁，行为逐字）

| # | 源（DataCenter.Standards） | 目标（Ledger） | 备注 |
|---|---|---|---|
| 1 | `StandardViolationException.cs`（全件 19 行） | `DeclarationViolationException.cs` | DataCenter 侧保留同名子类（§6-F2） |
| 2 | `StandardSchemaValidator.cs`（全件 239 行） | `Json/JsonSchemaSubsetValidator.cs` | 支持的 schema 关键字子集零增减；异常类型换名 |
| 3 | `StandardJson.DuplicateKeys`（114–139） | `Json/JsonDuplicateKeyScanner.cs` | Utf8JsonReader 栈扫描，逐字 |
| 4 | `StandardJson.Read<T>/TrySection/EnsureKnownKeys`（142–174） | `JsonLedgerReader.ParseRoot/TrySection/EnsureKnownKeys` | 未知键异常换框架异常 |
| 5 | 读取原语 `Text/Number/Strings/PathStrings`（199–255） | `JsonLedgerReader.*` | `Strings` 收裸字符串的分歧语义保留 |
| 6 | `StandardJson.Primitives.cs`（全件 60 行：`StrList/Elements/Int/Str/Bool`） | `JsonLedgerReader.*` | 与上合并到同一静态类 |
| 7 | `StandardCatalog` 的「Ordinal 排序 + 键字典 + Find + 原文」单件模式 | `LedgerCatalog<TEntry>` | DataCenter 的**四视图组合**留领域层 |
| 8 | `StandardConsistency.Check/EnsureValid` 的**宿主骨架**（跑全部判据、汇总、抛异常） | `ILedgerRule<TCatalog>` + `LedgerConsistency` | 判据体不搬 |

### 4.2 留 DataCenter（领域政策与模型，不搬）

| 件 | 位置 | 为什么不搬 |
|---|---|---|
| 24 个 `*Standard` record | `DataCenter.Protocol\Standards\` | 端口/数据/事件/替身领域模式 |
| `ReadPorts/ReadData/ReadEvents/ReadFakes/ReadImpl` | `StandardJson*.cs` | 领域台账的段结构与键词表；改为**调用** `JsonLedgerReader` 原语 |
| 四 schema | `schemas\*.schema.json` | 领域词表（kind/convert/from 枚举） |
| C1–C13 判据体 | `StandardConsistency.cs` / `StandardImplConsistency.cs` | 领域政策；改造为 13 个 `ILedgerRule<IStandardCatalog>` 实现 |
| 四视图目录组合 | `StandardCatalog.cs` | 内部改为持有四个 `LedgerCatalog<T>`，公共 API 不变 |
| `PortSpecCompiler/PortBinding/PortComposer` | `Strategy.Observability` | 编译执行面；范式登记进 §8 路线图，等第二实例 |
| `StandardConsistencyOptions` | DataCenter.Standards | 判据参数（跨切字段表/白名单/版本快照）是领域配置 |

### 4.3 切换后 DataCenter 侧文件形态（示意）

```
StandardViolationException.cs     → sealed class : DeclarationViolationException（两 ctor 转发）
StandardSchemaValidator.cs        → 薄外观（内部转调 JsonSchemaSubsetValidator）；公开方法签名不变
StandardJson.Primitives.cs        → 删除（原语已在框架）；using static JsonLedgerReader 或限定调用
StandardJson.cs                   → 仅留领域 Read* 与段键表白名单
StandardConsistency.cs            → 静态外观不变；内部 = LedgerConsistency.Check(catalog, RuleSet.All)
StandardImplConsistency.cs        → C7–C13 拆为规则类（或规则适配器包住现有私有方法，首批选后者降 diff）
```

---

## §5 五个冲突点的裁决

| # | 冲突 | 裁决 |
|---|---|---|
| 1 | `IDeclaration` 强制 Name/Description/DeclarationType；台账条目以 `Key` 标识、无三件套 | Ledger **不继承** `IDeclaration`，不搞共同根接口；键控由 `keySelector` 注入。两套抽象并存 |
| 2 | `DeclarationResolver` 吃弱类型 `IDictionary + Func`，承载不了 fail-fast/重复键/确定性排序 | 不共用 Resolver；`JsonLedgerReader` 独立提供强类型 JSON 读取骨架 |
| 3 | `IConstraint` 开放可组合 vs「判据只此一处、改数据不改判据」的 ratchet 纪律 | 新增独立宿主 `ILedgerRule`：编号、跨项、一次报全、阻断；**不**实现为 IConstraint 的组合 |
| 4 | Mapper 按名运行时注册 vs 静态编译 + `switch(Kind)=0` | 本次不引入编译抽象；`PortSpecCompiler` 留内核，第二实例出现后再提取 `IDeclarationCompiler` |
| 5 | DataCenter 零机制依赖 vs Declarative 拖 TConfig→CSG 链 | 新工程零项目引用；与现有 Declarative 仅命名同族，不做工程引用 |

---

## §6 分批实施（每批独立可编译、独立可回退）

### F1 — 框架独立成型（不碰 DataCenter）

1. 建 `Map\DeclarativeLedger\BasicMethod.Declarative.Ledger.csproj`（零 `ProjectReference`）。
   <!-- v1.3：已迁至 Plate\DeclarativeLedger\（见 §3.1 与 §10 v1.3），本步为历史执行记录 -->
2. 按 §4.1 第 1–6、8 项**逐字搬迁**六件机制（仅命名空间/异常名替换）。
3. 落 `LedgerCatalog<TEntry>`（从 StandardCatalog 提炼单件行为）。
4. 新建测试工程（MSTest，与 `DataCenter.Standards.Tests` 同范式），
   建议路径 `Environment\CentralTest\CSharpTest\Operation.Tests\Declarative.Ledger.Tests\`，
   用例来源 = DataCenter 测试中**不涉及端口领域**的部分（schema 子集各关键字、重复键、未知键、
   原语容错、目录排序/查找、判据宿主一次报全），覆盖 ≥ 30 例。
5. **DataCenter 与任何消费侧零改动。**

验收：
- 新工程 `dotnet build` 0 error / 0 warning；
- 框架自有测试全绿；
- 确认程序集引用闭包只有 BCL + System.Text.Json（`ProjectReference` 数 = 0）；
- DataCenter.slnx 与现有全部测试结果与改动前完全一致。

回退：删目录即可，无外部引用。

### F2 — DataCenter 切换为消费方（行为零变化）

1. `DataCenter.Standards.csproj` 加 `BasicMethod.Declarative.Ledger` 引用（**烘焙轨**）：
   `<Reference HintPath="$(CsharpBuildOutputDir)BasicMethod.Declarative.Ledger.dll" Private="false">` +
   **仅顺序边** ProjectReference（`ReferenceOutputAssembly=false`）——与 `DataCenter.Core/Protocol` 对 CsharpPrj 的既有范式一致；
   按伞项目序列 `dotnet build` 烘焙，确认 `BasicMethod.Declarative.Ledger.dll` 落 `Frame\CsharpPrj\BuildOutput\$(Configuration)\`。
   注：**不可**期待其复制进 `Frame\DataCenter\BuildOutput\`——`Projects\BuildFramework\ThinOutput.targets` 会剪除源路径含
   `\CsharpPrj\BuildOutput\` 的 copy-local（薄输出纪律）；消费侧改为「Agent 具名清单 `CsharpBakeRefs.props` 增一行 +
   CentralTest 目录通配自动覆盖」，见 §7。
2. §4.3 形态改造：异常子类化、schema 校验薄外观、原语删除并改调用。
3. 判据 rule 化（**低 diff 路线**）：
   - 建 13 个内部 `ILedgerRule<IStandardCatalog>` 规则类，每类体直接转发到
     `StandardConsistency` / `StandardImplConsistency` 现有私有静态方法（先搬宿主、不动判据逻辑）；
   - 待全绿后，可选再把私有方法逐个体内联进规则类（独立小批，便于对拍）。
4. `StandardCatalog` 内部四字典可委托四个 `LedgerCatalog<T>`（可选；不委托也不违规）。

验收（全部满足才算完）：
- `DataCenter.slnx` 构建 0 error / 0 warning；
- `DataCenter.Standards.Tests` 全绿；
- `Agent.Spec.Guard.Tests` 74/74 全绿；Observability 51 guards + E22 系列全绿；
- **黄金文案对拍**：C1–C13 违规消息逐字不变（现有断言即对拍器，不新增）；
- 公共 API 面零增减（签名核对清单见 §9）；
- `DataCenter.Standards` 净减行数 ≥ 250（机制搬出去掉转发后；仅参考指标，不为压行数牺牲可读性）。

回退：移除引用 + git 还原本工程；框架工程留着不影响任何东西。

### F3 — 收尾（可选，独立小批）

1. C7–C13 规则类体内联（消除转发薄皮），逐判据提交、逐判据对拍。
2. 在《标准框架通用化重构-设计与实施》§10 变更记录登记本提取（v1.4 条目）。
3. 待第二个契约台账领域出现时，按本框架落地第二实例，反向检验泛化是否够用；
   届时再评估提取编译/绑定范式（§8 路线图）。

---

## §7 风险与兼容性

| 风险 | 应对 |
|---|---|
| **二进制消费**（教训：给二进制消费侧改签名会 `MissingMethodException`） | 本批**不改任何公共方法签名**，只换内部实现；新增依赖以新增 DLL 方式出现，不动旧成员 |
| 新 DLL 未随烘焙输出 → 测试侧 `FileNotFoundException` | F2 第 1 步把「Ledger.dll 出现在 DataCenter\BuildOutput 且被复制到测试输出目录」列为显式检查项；伞项目串行构建避免 DLL 拷贝竞争 |
| 搬迁期语义被「顺手修正」 | 纪律：F1 只做命名空间/类名替换，`Strings`/`StrList` 等分歧原样保留；搬迁提交不得夹带逻辑改动 |
| 判据 rule 化引入文案漂移 | 低 diff 转发路线先行；文案断言（既有守卫测试）全程做对拍 |
| 框架被领域词污染 | Ledger 代码审查清单：零端口/数据/事件词汇、零 C1–C13 字样（编号由消费侧 Code 属性传入） |
| 超前抽象（YAGNI） | 编译/绑定/版本快照比对等不进 F1；`Violation` 强类型结构、$ref 外部引用等增强一律挂 v2 |

---

## §8 明确不做

- 不改 `BasicMethod.Declarative` 既有任何代码（避免波及 EventFramework / Orchestration / ValidationFramework 消费方）。
- 不让 DataCenter 引用 TConfig 或现有 Declarative 工程。
- 不提取 `PortSpecCompiler` / `PortBinding` / `PortComposer` 到框架（无第二实例，抽象无依据）。
- 不把违规字符串重构为强类型 `Violation(Code, Message)`（会破黄金文案，收益不足）。
- 不动关系代数（Relation/Select/Project/Join）；台账领域无消费者。
- 不引入第三方 JSON Schema 库（现有子集已覆盖全部 schema 关键字）。

---

## §9 验收清单（可执行核对）

**F1 门**
- [ ] `BasicMethod.Declarative.Ledger.csproj` 存在且 `ProjectReference` 数 = 0
- [ ] 框架工程独立 `dotnet build` 0/0
- [ ] 框架测试 ≥ 30 例全绿（schema 子集 / 重复键 / 未知键 / 原语 / 目录 / 判据宿主）
- [ ] 框架内 grep 无领域词：`port|emit|carries|C1|C7|Standard`（类型名 `*Standard` 也不得出现）

**F2 门**
- [ ] `DataCenter.slnx` 0 error / 0 warning
- [ ] `DataCenter.Standards.Tests` 全绿
- [ ] `Agent.Spec.Guard.Tests` 74/74；Observability 51 guards + E22 全绿
- [ ] C1–C13 违规文案逐字不变（既有断言全过即证）
- [ ] 公共签名核对：`StandardConsistency.Check/EnsureValid`、`StandardCatalog.Load/LoadValidated/Find*`、
      `StandardSchemaValidator.Validate/EnsureValid`、`StandardViolationException : Exception`（含 `Violations`）
- [ ] `Frame\CsharpPrj\BuildOutput\$(Configuration)\BasicMethod.Declarative.Ledger.dll` 存在（权威烘焙轨），
      且 `csharp-bake.manifest.json` 含该程序集；`Projects\BuildFramework\CsharpBakeRefs.props` 具名清单含该项
      （Agent 侧显式引用；CentralTest 侧由 `CsharpPrj.BinaryRefs.props` 目录通配自动覆盖）
- [ ] DataCenter.Standards 私有判据逻辑除「宿主替换」外零改动（diff 仅机制替换 + rule 转发）

**v1.3 校准（JSON 机制上收后）**
- [ ] 框架工程仍为 `ProjectReference` 数 = 0（Ledger 不含任何 JSON 机制，JSON 机制在 `Plate.JsonMethod`）
- [ ] 机制用例分宿：`Plate.JsonMethod.Tests`（读取原语 / 重复键 / schema 子集）+ `Declarative.Ledger.Tests`（目录 / 判据宿主）
- [ ] `DataCenter.Standards` 烘焙引用含 `Plate.JsonMethod.dll`（JSON 机制唯一实现的传递依赖，需随行部署）
- [ ] `Plate.dll` 不含 `BasicMethod.Declarative.Ledger` 类型（聚合工程 `Compile Remove` 生效，防 CS0433）

---

## §10 变更记录

| 版本 | 日期 | 内容 |
|---|---|---|
| v1.0 | 2026-09-17 | 首版：框架优先提取方案；六件机制清单、零依赖新工程 `BasicMethod.Declarative.Ledger`、F1–F3 分批与验收 |
| v1.1 | 2026-09-17 | 补下游引用（无内容变更）：《DataCenter-标准单源化-重复逻辑收敛-设计与实施.md》P3-E 复用本框架 `ILedgerRule` 作判据单源。 |
| v1.2 | 2026-09-17 | **F1+F2 已落地**（实施记录）：① 框架工程建 `BasicMethod\Map\DeclarativeLedger\`（装配名/命名空间 = `BasicMethod.Declarative.Ledger`，**零 ProjectReference**），六件机制逐字搬迁（异常类名 `DeclarationViolationException`，**非 sealed** 以允许消费侧派生保留异常身份）；② `modules.json` 增 `DeclarativeLedger` 并挂入 `BasicMethod` 伞工程（`MainProj.sln` 烘焙必经）；③ 新增框架自有测试 `Environment\CentralTest\CSharpTest\Operation.Tests\Declarative.Ledger.Tests\`，**63 例全绿**（schema 子集各关键字/重复键/原语容错/目录/判据宿主）；④ F2 切换：`DataCenter.Standards` 改为消费框架（烘焙二进制引用 + 仅顺序边；异常子类化；`StandardSchemaValidator` 薄外观；`StandardJson` 原语委托、`StandardJson.Primitives.cs` 删除；C1–C13 规则化为 13 个 `ILedgerRule<IStandardCatalog>` 类，宿主 `LedgerConsistency`）；⑤ §6-F2 第 1 步与 §9 F2 门的「Ledger.dll 落 `DataCenter\BuildOutput`」已按 `ThinOutput.targets` 事实改写为烘焙轨交付。验收：框架 63/63；`DataCenter.Standards.Tests` 65/65（C1–C13 文案逐字）；`Agent.Spec.Guard.Tests` 74/74；Observability guards 52/52；`MainProj.sln` 与 `DataCenter.slnx` 0 错 0 警。 |
| v1.3 | 2026-09-17 | **归位 + JSON 依赖反转（IoC）**：① 工程迁至 `Frame\CsharpPrj\Plate\DeclarativeLedger\`（模块路径单一真源 `modules.json` 同步、`Generate-ModulePaths.ps1` 重生成、`Plate.csproj` 补 `Compile Remove` + 聚合 `ProjectReference`，防 `Plate.dll` 重复编译）；② **JSON 机制上收平台框架**：`JsonLedgerReader`→`Plate.JsonMethod.Utilities.JsonTolerantReader`（新增 `UnknownKeys`）、`JsonDuplicateKeyScanner`→`Plate.JsonMethod.Utilities`、`JsonSchemaSubsetValidator`→`Plate.JsonMethod.Validation`，机制**只返回违规不抛**、域文案外置；③ 使用端直接复用：`DataCenter.Standards` 的 `StandardJson` / `StandardJson.Impl*` / `StandardSchemaValidator` 改调 `Plate.JsonMethod`，`UnknownKeyMessage` 域文案随之下沉本层；④ Ledger 只留目录 / 判据接口 / 判据宿主 / 违规异常，**零 ProjectReference 复归**（Ledger.dll 23.0KB→10.8KB）；⑤ `DataCenter.Standards.csproj` 增 `Plate.JsonMethod` 烘焙引用 + `$(Proj_*)` 化顺序边，`DataCenter\Directory.Build.props` 导入生成式模块路径；⑥ 机制用例随机制迁移：新建 `Plate.JsonMethod.Tests`（50 例），`Declarative.Ledger.Tests` 保留台账机制（13 例）。验收：`Plate.JsonMethod.Tests` 50/50；`Declarative.Ledger.Tests` 13/13；`DataCenter.Standards.Tests` 74/74（C1–C14 文案逐字）；`MainProj.sln` / `DataCenter.slnx` 0 错 0 警；`Test-ProjectReferences.ps1 -Check` 悬空 0 / 硬违规 0 / 循环 0；烘焙清单 94 程序集全就绪。 |
