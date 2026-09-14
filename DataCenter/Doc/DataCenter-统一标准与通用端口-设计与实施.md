# DataCenter 统一标准与通用端口 — 设计与实施（Bridge 消除）

> **定位**：把「测试侧端口桥接（`Agent.TestShared\Bridge\`，15 文件 / 124,202 B）」的职责**上移**到框架：
> **数据标准落 `Frame\DataCenter`**、**通用端口机制落 `Strategy.Observability`**、**产品 API 直接成为端口实现**，于是测试侧不再需要桥接层。
> **前置**：本方案是《AgentTest 适配器归零 · 统一数据标准与观测内核方案》（下称「上位方案」）§四 UDS-1/2/3 与 §五 目标架构的**落地补强**——术语、判据沿用上位方案，不另立。
> **快照日期**：2026-09-15 ｜ 现状数字为**实测**（命令可复现）。**性质**：待执行设计（**未实施**）；随实施就地回填 §8 状态列与 §11 实测项。
> **红线**：框架层不得含领域词汇（loop/decision/tool/skill…）；内核（`Strategy.Observability`）出现 Agent 术语即越界。

---

## §0 一句话结论

> **Bridge 不是"测试的私事"，而是"框架缺口"的体征**：内核已有端口原语（`IPortInput`/`IPortBinding`/`PortCatalog`/`StandardFakes`/四根投影），
> 但**缺"标准"**（三本台账的机器校验与装载）、**缺"声明式装配"**（从台账 + 绑定源批量登记端口）、**缺"用例侧声明式替身"**（`.specs` 无法声明 `given ports`）。
> 这三缺由测试侧 `Bridge\` 用 15 个文件手工补上——把三缺补齐到框架，Bridge 即自然消失（不是"删代码"，是"职责归位"）。

---

## §1 现状取证（实测）

### 1.1 已有（框架侧，不需新建）

| 层 | 位置 | 已有件（实测文件名） | 职责 |
|---|---|---|---|
| 端口机制 | `Frame\CsharpPrj\Strategy\Observability\Ports\`（7 文件） | `IPortInput`/`PortInput`、`IPortBinding`、`IPortCatalog`/`PortHandle`、`PortCatalog`、`StandardFakes`、`CallLog`、`ObservedPortDecorator` | 端口键 + 位置/具名实参 + 指纹；绑定唯一实现面；**唯一注册点**（Register/Replace/Resolve）；5 个标准替身；调用记录（每次恰好一条）；埋点装饰器 |
| 边界契约 | `...\Boundary\`（7 文件） | `BoundaryContract`、`BoundaryContractJson(.Fields/.Tokens)`、`BoundaryContractRegistry`、`BoundaryKind`、`FieldSpec`、`PortDependency` | 契约声明 + JSON 读取 + 注册表（`in`/`out` 合并去重、重复键抛） |
| 投影四根 | `...\Projection\`（5 文件）+ `Query\` | `PortProjection`、`ObservationProjection`、`TimelineProjection`、`AggregateProjection`、`ObservationOutcome` | `Port(...)`/`Event(...)`/`Timeline(...)`/`Aggregate(...)` |
| 数据标准（初版） | `...\HierarchySupport\` | `DataContract`/`DataContractRegistry`/`DataDirection`/`DataScope` | 数据项：键 + 类型 + 方向 + 作用域 + 版本 + 生产/消费方 |
| 契约桥 | `...\Registry\` | `BoundaryBackedContractRegistry`、`BoundaryEventContractBridge`、`EventContract`、`ObservabilityContractRegistry`、`ObservabilitySemanticsJson` | 边界契约 ↔ 事件契约 ↔ 语义 JSON |
| 数据中枢 | `Frame\DataCenter\` | `DataCenter.Core`（StateCenter/StateStore/Layering/Projection/Registry/Roots）、`DataCenter.Protocol`（IStateRegistry/IStateProjector/…）、`DataCenter.Abstractions`、`DataCenter.Adapters`、`DataCenter.Hosting`、`schemas\csg-runtime-data.schema.json` | 跨语言运行时数据中枢（CSG 数据面 + 控制面）；**已有 `schemas\` 落点** |

### 1.2 已有（Agent 产品侧）

| 件 | 路径 | 实测 |
|---|---|---|
| 端口契约台账 | `Projects\Plug\Agent\src\Agent.Contract\Observability\ports.json` | `ports` **10** 条（含 `AgentApiPort.HandleAsync`/`IAgentRunOrchestrator.RunAsync`/`RoundStepInvoker.ExecuteStepAsync`/`NodeExecutionStepSkeleton.ExecuteAsync`/`AiSchedulerStateMachine.AdvanceAsync`/`IExecutorRegistry.*`/`BackendRegistry.TryCreate`/`WireAdapterRegistry.TryCreate`/`LlmToolRegistry.TryInvoke`）+ `data` **36** 条 |
| 契约读取 | 同目录 `PortsJson.cs` / `DataStandard.cs` | 只读 JSON 文本，不引内核类型（同层约定） |
| Agent 端口目录 | `Agent.Operation\Observe\Ports\AgentPortCatalog.cs` | Agent 侧封装 |
| 生产绑定 | `Agent.Launcher\Composition\AgentPortBindings.cs` | 7 可绑 + 3 冻结（N3 实测，`PortBindingSurfaceTests` 锁定） |

### 1.3 缺口（= Bridge 存在的原因）

| # | 缺口 | 现由谁补（Bridge） | 影响 |
|---|---|---|---|
| **G1** | **声明式装配装载器**：内核 `PortCatalog` 只有单端口 `Register`，无「台账 + 绑定源 → 批量登记 + fail-safe 记账」 | `Bridge\PortRegistry.cs`（第 19/28/45 行：播种契约 → `PortBindings.RegisterAll` → 逐端口 `Bind`） | 装配逻辑与测试绑定耦合；`Unbound/Frozen` 记账只在测试侧 |
| **G2** | **声明式绑定**：按 `target/method/args` 反射调用 | `Bridge\ReflectivePortBinding.cs`（11,544 B） | 通用能力被写在测试侧；产品侧无法复用 |
| **G3** | **读参小件**：`IPortInput` 无强类型读参扩展 | `Bridge\PortArgs.cs`（`Arg/ArgOrNull/ArgBool/With<T>`） | 每个绑定者自写读参 |
| **G4** | **标准一致性校验**：`ports.json` 的 `emits` ↔ 事件契约、`in/out` 跨切字段 ↔ `data` 段、数据项 owner 存在性、版本只增——**均无机器判据** | 无（人力纪律） | 声明漂移静默；`data` 段只被读取（`DataStandard.Load`），不被校验 |
| **G5** | **标准落点分散**：数据标准散在 `Agent.Contract\Observability\DataStandard.cs`（读 `data` 段）与 `Share.HierarchySupport\DataContract`（模型） | 无 | 跨语言（TS 侧）无共基；DataCenter 只承载 `csg-runtime-data.schema.json` |
| **G6** | **用例侧声明式替身 / 多步编排**：`.specs` 无 `given ports` 指令 | `Bridge\PortBindings.*.cs`（4 族 55KB：探针分派 + 替身装配 + 多步调用 + `Product` 投影）+ `PortInlineBindings.cs` | **用例步骤被写进 C#**——这是 Bridge 体量的主要来源 |
| **G7** | **端口宿主（用例驱动侧）**：`SpecStepContext → IPortInput` + 四根求值 | `Bridge\AgentPortHost.cs`（9,770 B）+ `PortProjections.cs`（3,473 B） | 驱动层能力落在桥接层，L0 只能出接口（`ISpecPortHost`） |

### 1.4 量化基线（实测）

| 项 | 值 |
|---|---|
| `Agent.TestShared\Bridge\` | **15 文件 / 124,202 B**（其中 5 个族实现文件 `PortBindings.<族>.cs` 合计 67,686 B = 54%） |
| `Agent.TestShared\Adapters\` | 11 文件 / 89 kind（S6-①a 后；目标 0） |
| 测试侧端口台账 | 26 条（Axioms 2 + Operation 13 + S6-①a 11） |
| 内核端口机制 | `Ports\` 7 文件 + `Projection\` 5 文件（**原语齐备**） |
| 标准校验 | **0 条**（三本台账均无 schema 校验与交叉一致性判据） |

---

## §2 目标架构（四层，Bridge 不在其中）

```
┌ L0 内核机制     Strategy.Observability（Frame\CsharpPrj\Strategy\Observability）
│                 Ports\（IPortInput/IPortBinding/PortCatalog/StandardFakes/CallLog/ObservedPortDecorator）
│                 Boundary\（BoundaryContract + JSON 读取 + Registry）
│                 Projection\（Port/Event/Timeline/Aggregate 四根）
│                 +【新增】PortAssembler（声明式装配）、ReflectiveBinding、PortInputExtensions
├ L0.5 标准        Frame\DataCenter\Standards（【新增】跨语言、语言无关、无领域词汇）
│                 PortStandard / DataStandardItem / EventStandard / FakeStandard
│                 StandardCatalogLoader（读三本台账） + StandardConsistency（六条机器判据）
│                 schemas\*.schema.json（机器校验）
├ L1 产品          Agent：API 即端口（组合根 Register(契约, 绑定)）
│                 Agent.Contract\Observability\ports.json = **唯一契约面**（含 data 段）
│                 Agent.Launcher\Composition\AgentPortBindings.cs = 声明式绑定表（impl 段驱动）
├ L2 驱动          TestInfrastructure\DataDriven：SpecRunner / SpecPathResolver /【新增】SpecPortHost（只依赖内核接口）
└ L3 用例          <工程>\Cases\**\*.specs：given（ports/events/workflow）→ when/and <PortKey> 多步 → then 四根/产物
                   （Bridge 层 = 0）
```

**职责边界（谁不许做谁的事）**

| 层 | 必须做 | 禁止做 |
|---|---|---|
| L0 内核 | 端口机制、契约模型、记录、投影、标准替身 | 领域词汇（decision/tool/skill/loop…）；读产品台账文件（由装配器注入文本） |
| L0.5 标准 | 台账**模型 + 校验 + 装载**（纯数据，零运行时依赖） | 引 `Strategy.Observability` 类型（避免机制↔标准互依赖）；含领域语义 |
| L1 产品 | 声明契约（`ports.json`）、声明绑定（`impl`）、真实实现、装饰器 | 在测试侧重复声明契约；把用例步骤写进产品 |
| L2 驱动 | `SpecStepContext → IPortInput` 翻译、四根求值、行级隔离 | 断言领域语义；直接 `new` 领域对象 |

> **依赖方向**：`L3 → L2 → L1 → {L0.5, L0}`；`L0.5 ⊥ L0`（互不依赖，由 L1 装配时把两者拼起来）。
> 若按 Layer 表核对后发现 `Observability` 须在 `DataCenter` 之下，则**映射器落 L1 组合根**（见 §9 D1 待裁决）——两种落点都不改变上文分层语义。

---

## §3 通用数据标准（`Frame\DataCenter\Standards`）

### 3.1 为什么放 DataCenter

| 理由 | 依据 |
|---|---|
| DataCenter 的立项动因就是「**运行时数据统一收敛 + 语言无关**」（CSG 数据面），数据标准与状态/投影**同源** | `DataCenter\Doc\DataCenter-架构设计.md` §1/§3 |
| 已有 `schemas\`（`csg-runtime-data.schema.json`）与「只扩不改、schema 冻结」纪律 | 同上 §2/§3（R-DC1） |
| 现有数据标准散在两处（`Share.HierarchySupport\DataContract` 模型 + `Agent.Contract\DataStandard.cs` 读取），**跨语言无共基** | 本卡 §1.3 G5（实测） |
| 端口/事件/数据三本台账需**同一套校验**，落产品侧会造成「每个产品一套校验」 | 上位方案 §4.1（三标准共同载体 = 一份契约） |

### 3.2 落点与文件（建议）

```
Frame\DataCenter\
├── DataCenter.Protocol\Standards\            ← 纯契约（无实现、零依赖）
│   ├── PortStandard.cs          键/入参/产物/依赖/作用域/版本/发射事件/绑定声明
│   ├── DataStandardItem.cs      键/方向/作用域/字段/生产方/消费方/版本
│   ├── EventStandard.cs         事件类型/族/carries（字段或度量）/作用域/版本
│   ├── FakeStandard.cs          端口键/替身种类（recording|fixed|echo|notExpected|sequence）/seed
│   └── IStandardCatalog.cs      Ports / Data / Events / Fakes 四视图 + 原始文本（供内核播种）
├── DataCenter.Standards\                      ← 【新增子工程】模型读取 + 校验 + 装载
│   ├── StandardJson.cs          JSON → 模型（含 `$comment` 元数据键忽略；未知键 fail-fast）
│   ├── StandardCatalog.cs       实现 IStandardCatalog（惰性、确定性排序、键唯一）
│   ├── StandardConsistency.cs   六条判据（见 §3.4）
│   └── StandardSchemaValidator.cs  schemas\*.schema.json 结构校验（先结构、后语义）
└── schemas\
    ├── csg-runtime-data.schema.json   （既有）
    ├── ports.schema.json              （新增）
    ├── data.schema.json               （新增）
    ├── events.schema.json             （新增）
    └── fakes.schema.json              （新增）
```

> **与既有件的关系**：`BoundaryContract`/`DataContract`（内核）**不删不改**（只加不改，B-1/B-2）；
> 标准模型是它们的**声明式中立形态**，由 L1 装配器映射（`PortStandard → BoundaryContract`、`DataStandardItem → DataContract`）。
> 渐进期两套并存：内核仍可直接被 C# 注册（现状），标准装载是**新增的第二入口**，迁移完成后成为唯一入口。

### 3.3 三本台账的定位（谁是真源）

| 台账 | 真源位置 | 谁写 | 谁读 |
|---|---|---|---|
| 端口契约 `ports` | `Agent.Contract\Observability\ports.json`（**产品侧，唯一真源**） | 产品开发者 | 内核 `BoundaryContractJson`（契约）+ DataCenter `StandardCatalog`（校验）+ 测试驱动（端口面） |
| 数据标准 `data` | 同上文件 `data` 段 | 产品开发者 | `DataStandard.cs`（读侧）+ `StandardConsistency`（校验） |
| 事件标准 `events` | `observability_semantics.json`（现状）→ 收敛进 `DataCenter` 标准目录 | 产品开发者 | `ObservabilityContractRegistry` + `StandardConsistency` |
| 替身 `fakes` | **用例侧**（`.specs` 的 `given ports`）→ 运行期模型；仓库级默认替身落 `fakes.json` | 用例作者 | 驱动层装配（`StandardFakes`）+ `PortCatalog.Replace` |

**纪律**：测试侧**不再有第二本 `ports.json`**（现 `Bridge\ports.json` 的 `legacyKind` 是 S6 过渡身份，随族退场消失）。

### 3.4 六条机器判据（`StandardConsistency`）

| # | 判据 | 失败语义 | 现状 |
|---|---|---|---|
| C1 | 键唯一：`ports`/`data`/`events`/`fakes` 各自键唯一且 Ordinal | `StandardViolationException` | 部分（`BoundaryContractRegistry` 重复即抛） |
| C2 | `ports[x].emits` ⊆ `events` 键集 | 声明了不存在的事件 | **无**（新增） |
| C3 | `events[x].carries` ⊆ `data` 键集 | 事件携带未登记数据项 | **无**（新增） |
| C4 | `ports[x].in/out` 的**跨切字段**（如 `runId`/`roundId`/`sessionId`）⊆ `data` 键集 | 端口契约与数据标准脱节 | **无**（新增） |
| C5 | `data[x].producer`（若声明）⊆ `ports` 键集 ∪ `agents` 白名单 | 数据项无生产方 | **无**（新增） |
| C6 | 版本单调：同键 `version` 只增不减（与上一快照比对） | 破坏只增纪律 | **无**（新增） |

**判据落点**：① 编译期（`DataCenter.Standards.Tests` 单测）；② 测试守卫（`Agent.Spec.Guard.Tests` 增 `StandardConsistencyGuardTests`，对 Agent 台账跑 C1–C6）。
**纪律**：校验失败**阻断**（不 warn）——声明漂移必须当场可见（对齐上位方案「契约即断言」）。

---

## §4 通用端口框架补齐（内核件上移）

### 4.1 Bridge 15 文件 → 归宿

| Bridge 文件（实测体积） | 归宿 | 目标形态 |
|---|---|---|
| `PortRegistry.cs`（3,532） | **L0** `Ports\PortAssembler.cs` | `PortAssembler.Assemble(catalog, ledgerJson, bindings)`：播种契约 → 逐端口绑定 → `Unbound`/`Frozen` 记账（现 `AgentPortHost.MarkUnbound/MarkFrozen` 语义上移为内核 `PortAvailability`） |
| `ReflectivePortBinding.cs`（11,544） | **L0** `Ports\ReflectiveBinding.cs` | 按声明 `impl: {target, method, args[]}` 反射（含类型三级可达化，G-10 实测结论随之上移） |
| `PortArgs.cs`（2,763） | **L0** `Ports\PortInputExtensions.cs` | `Arg/ArgOrNull/ArgBool/ArgInt/With<T>/WithBool`（`IPortInput` 扩展方法） |
| `AgentPortHost.cs`（9,770） | **L2** `TestInfrastructure\DataDriven\SpecPortHost.cs` | `SpecStepContext → IPortInput`；`Reset` 行级隔离；投影根委派内核；**只依赖内核接口**（保留 C-1 禁领域引用） |
| `PortProjections.cs`（3,473） | **L0**（已在 `Projection\` 有 `Port/Timeline/Aggregate`） | 删除 Bridge 版，直接消费内核件 |
| `PortInlineBindings.cs`（1,766）+ `PortBindings.*.cs`（5 文件 67,686） | **L3 用例**（`.specs` 声明）+ **L1 产品**（真实实现） | 见 §5：`given ports` 声明替身 + 多步 `when/and` 调端口；真值走产品绑定 |
| `PortLedger.cs`（8,558）+ `ports.json`（11,893） | **L0.5** `StandardCatalog`（模型+校验） | 测试侧台账消亡；契约面唯一 = 产品 `ports.json` |
| `SpecWorkflows.cs`（926） | **L3 用例**（`given workflow` 声明）或 `Agent.TestShared\Fixtures\` | 夹具声明化，不再由端口实现携带 |
| `PortBindings.cs`（2,291） | **L0/L2** 装配入口 | 各工程 `specgen.json` 的 `registerCall` 收敛为**一行**（或 0 行，由测试基类默认装配） |

**上移纪律**：上移 = **逐字节搬运 + 命名空间/可见性调整**，不改语义；每次只搬一件、跑全绿再搬下一件（可回滚粒度 = 1 文件）。

### 4.2 内核新增件的签名草案

```csharp
// Strategy.Observability.Ports（L0）—— 机制，零领域语义
public static class PortInputExtensions {           // G3
    public static string  Arg(this IPortInput input, int index);
    public static string? ArgOrNull(this IPortInput input, int index);
    public static bool    ArgBool(this IPortInput input, int index);
    public static int     ArgInt(this IPortInput input, int index);
    public static T       With<T>(this IPortInput input, string key);
}

public sealed class ReflectiveBinding : IPortBinding {   // G2
    public ReflectiveBinding(PortBindingSpec spec);      // target/method/args（JSON 声明）
}

public sealed class PortAssembler {                       // G1
    public PortAssembler(IPortCatalog catalog);
    public IReadOnlyDictionary<string, string> Unbound { get; }   // fail-safe 记账（原 Bridge 语义）
    public IReadOnlyDictionary<string, string> Frozen  { get; }
    public int Assemble(string contractsJson, string bindingsJson);  // 返回成功绑定数
}
```

> **兼容**：全部为**新增**（B-1/B-2/B-4）；既有 `PortCatalog` 的 `Register/Replace/Resolve/Log` 不动。
> **单一实现点**：上移后 Bridge 的旧件**立即删除**（不留薄壳，避免双实现）——两者不可并存（`SingleImplementation`）。
>
> **P1-4 实测补件（2026-09-15，就地更新为实测形态）**：投影根另需两件（原测试侧 `Bridge\PortProjections.cs`，与内核既有件语义不兼容，故并列新增）：
> `Projection\EventProjection.cs`（`Of(kind, events)`：`Kind`/`Count`/`Observed`/`Events`/`Statuses`/`Rounds`；内核 `ObservationProjection.Event` 仅「首个匹配」）
> 与 `Projection\MetricTotalsProjection.cs`（`Of(events)`：`Count`/`Metrics`/`Samples`/`Metric(key)`；内核 `AggregateProjection` 仅「单度量键」）。
> 宿主侧注入面实测形态：`ISpecPortSurface{PortKeys,Handles,TryGetProductFields,InlinePortKeys,ContractKeys}` / `ISpecScopeProvider{AmbientScopeId}` / `ISpecEventSource{IsAvailable,Read}`（`IDisposable`，flush 与释放归实现）。

---

## §5 用例侧声明式装配（消除内联实现的唯一路径）

### 5.1 诊断：`PortBindings.*` 是"用例步骤写进了 C#"

现 `Bridge\PortBindings.*.cs` 的每个方法都是同一形状：**探针分派（scenario）→ 装配替身 → 多步调用产品 API → `Product(...)` 投影**。例（S6-①a 实测形态）：

```
when TaskOrchestrator.Run createTask
  ⇔  ① 建 RecordingHierarchyReferenceManager / RecordingLifecyclePort
     ② new TaskOrchestrator(refs, store, lifecycle)
     ③ CreateTaskAsync("h1","task1","input-1")
     ④ Product(historiesCount, historyTaskLinked, createdPair)
```

其中 ②③ 是**产品调用**（应属真实端口实现 / 或用例步骤），①④ 是**替身装配 + 投影**（应用例声明）。这些都**不是**产品逻辑，把它们留在 C# 里 = 66+ 个 kind 的体量留在桥接层。

### 5.2 目标形态：`.specs` 增一类 `given`（仅此一处语法扩展）

```
case task-orchestrator.create-task…
  given ports {
    IHierarchyReferenceManager: recording
    ITaskLifecyclePort: recording
    INodeOutputStore: fixed { seed: { "agent-round|nodeA|result": "A产出" } }
  }
  when TaskOrchestrator.Run createTask      # 产品端口（真实绑定）或 scenario 端口
  then path historiesCount eq @1
  then path historyTaskLinked eq @true
  then path Port(ITaskLifecyclePort).Calls[0].Method eq Create
```

| 要素 | 现（Bridge） | 目标（声明式） |
|---|---|---|
| 替身装配 | `new RecordingLifecyclePort()` 等私有类 | `given ports { <端口键>: <fake 种类> [seed] }` → 驱动层 `StandardFakes.*` + `PortCatalog.Replace` |
| 多步调用 | 方法体内顺序调用 | `when` / `and` 多步（**已支持**，现状即可表达） |
| 产物投影 | 手写 `Product((...))` | 直接断言 `Port(...)` / `Event(...)` / 四根（**已支持**） |
| 期望值 | 在 C# 里 | 全在 `.specs`（现状已如此，P2 原则不变） |

> **与现决策 D2（`when <PortKey>` 零语法改动）的关系**：`when` 形态**不变**；本方案**只新增 `given ports`**（+可选 `given workflow`）。
> 这是上位方案 §6.1 `invoke/ports` 形态的**最小化落地**——把"装配声明"从 C# 提回 `.specs`，但不改 `when/then` 语法与 17 算子封顶。

### 5.3 为什么必须做（否则 Bridge 删不掉）

| 若不做的后果 | 说明 |
|---|---|
| 内联实现无处可去 | 其内容 65% 是"替身 + 多步 + 投影"，不属产品、不属内核 → 只能继续留测试侧 |
| `Adapters` 删完，Bridge 反而更重 | kind 词表消失但场景脚本仍在（S6-①a 实测：11 kind → 4 文件 55KB） |
| 契约面无法唯一 | 测试台账必须继续携带 `legacyKind`/`binding`/`productFields` 三元驱动元数据 |

---

## §6 Agent API 端口化（产品侧改造）

### 6.1 目标：API 即端口，绑定表声明化

| 项 | 现状（实测） | 目标 |
|---|---|---|
| 契约 | `Agent.Contract\Observability\ports.json`（10 条 + data 36） | **加 `impl` 段**（声明 binding：`reflective{target,method,args}` / `delegate{id}` / `frozen{reason}`）；位置不变（唯一真源） |
| 装配 | `Agent.Launcher\Composition\AgentPortBindings.cs` 手写绑定（7 + 3 冻结） | 组合根**一行**：`PortAssembler.Assemble(catalog, portsJson, implJson)`；手写绑定降级为 `delegate{id}` 的实现字典（产品真实实现仍 C#） |
| 目录 | `Agent.Operation\Observe\Ports\AgentPortCatalog.cs` | 收敛为内核 `PortCatalog` 的薄包装（或直接使用内核类型，删除包装） |
| 埋点 | `ObservedPortDecorator` + 散点 `AgentTrace.*` | 装饰器在装配期统一包裹（已具备），散点随 S6/S7 收敛 |
| 事件 | `observability_semantics.json` | 收敛进标准目录（与 `events` 台账同源），保留兼容读取 |

**in/out 与数据标准的关系（E-N6-2 实测结论的标准化）**：
- `in` 列入参、`out.fields` 只列**非入参**产物（内核 `BoundaryContractRegistry.Validate` 对字段名合并去重）；
- `data` 段承载**跨切数据项**（`runId`/`roundId`/`sessionId`/`exec_result`…），由 C4 判据与端口契约绑定。

### 6.2 产品改造清单（改动面 = 装配层，业务实现不动）

| # | 动作 | 落点 | 判据 |
|---|---|---|---|
| A1 | `ports.json` 增 `impl` 段（10 条各给 binding 声明） | `Agent.Contract\Observability\ports.json` | 装配成功数 = 10；`Unbound=0`；重建 `Agent.Contract`（G-1） |
| A2 | `AgentPortBindings.cs` 改为实现字典（`delegate{id}` → 真实实现工厂） | `Agent.Launcher\Composition\` | `PortBindingSurfaceTests` 全绿（7 绑 + 3 冻不变） |
| A3 | 组合根统一走内核装配器 | 同上 | `Agent.Observe.Tests` 46 全绿（B-5 哨兵） |
| A4 | `AgentPortCatalog` 收敛/删除 | `Agent.Operation\Observe\Ports\` | 编译 0 错；守卫不漏 |
| A5 | 事件语义 JSON 迁标准目录（兼容读取保留） | `Agent.Contract\Observability\` | `ObservabilityContractRegistry` 装载一致（无 `UnregisteredEventException`） |

---

## §7 Bridge 消除判据（可验证）

| # | 判据 | 现状 | 目标 |
|---|---|---|---|
| J1 | `Agent.TestShared\Bridge\` 文件数 | 15（124,202 B） | **0**（目录不存在） |
| J2 | `Agent.TestShared` 中端口装配/台账类型 | `PortLedger`/`PortRegistry`/`AgentPortHost`/`ReflectivePortBinding`/`PortInlineBindings`/`PortArgs`/`PortBindings.*` | **0**（全部由 L0/L2/L0.5 承担） |
| J3 | 测试侧 `ports.json` 台账 | 26 条（`legacyKind` 过渡身份） | **0**（契约面唯一 = 产品 `ports.json`） |
| J4 | 各工程 `specgen.json` 的 `registerCall` | 逐族注册（Core 5 族 / Axioms 2 族 / Operation 1） | **≤1 行**（`Standards.PortHost.Mount(registry, "Agent")`）或 0 行 |
| J5 | `Adapters\` 目录 | 11 文件 / 89 kind | **0**（S6/S6b 收尾） |
| J6 | 标准一致性 | 0 条判据 | C1–C6 全绿（编译期 + 守卫） |
| J7 | 用例表达 | `when <PortKey>` + 内联场景 C# | `.specs` 声明式（`given ports` + 多步），用例数守恒 |
| J8 | 守卫面 | `SpecPortHostTests`（测试侧装配断言） | 判据 1–5 迁 L0（`TestInfrastructure.Tests`），`Agent.Spec.Guard` 只留台账与退场断言 |

---

## §8 迁移阶段（每阶段独立可回滚 + 判据）

| 阶段 | 内容 | 落点 | 回滚粒度 | 判据（全绿才进下一阶段） |
|---|---|---|---|---|
| **P0** | 冻结现状：S6 继续进行（①②④⑤/S6b），**不新增 Bridge 能力** | — | — | 现有 24 守卫 + 各工程基线 |
| **P1** | 内核件上移（**不改行为**）：`PortInputExtensions` → `ReflectiveBinding` → `PortAssembler` → `L2 SpecPortHost`；每件搬完即删 Bridge 旧件 | `Strategy\Observability\Ports\`、`TestInfrastructure\DataDriven\` | 1 文件 | 每件：`TestInfrastructure.Tests` 168/169 + Guard 24 + Core 422 + Observe 46；Bridge 体积单调下降 |
| **P2** | `DataCenter.Standards` 落地：模型 + 4 份 schema + Loader + C1–C6 + 单测；产品 `ports.json` 增 `impl` | `Frame\DataCenter\{Protocol,Standards,schemas}\` | 1 子工程 | `DataCenter` 构建 + `DataCenter.Standards.Tests` 全绿；六判据对 Agent 台账跑通（首轮必定报错 → 修数据，禁改判据） |
| **P3** | Agent 端口化（§6）：A1–A5 | `Agent.Contract`/`Agent.Launcher`/`Agent.Operation` | 1 项 | `PortBindingSurfaceTests` + `Agent.Observe.Tests` 46 + `/Agent\.Api` 基线（44 + 18/22，E-13 守恒） |
| **P4** | 声明式装配：`.specs` 增 `given ports`（驱动层支持）→ **按族**改写 → 删 `PortBindings.*` 对应实现 | `TestInfrastructure\DataDriven\`（驱动）+ 各 `Cases\**\*.specs` | 1 族 | 该族：双跑对拍报告为空 + 用例数守恒 + 退场集逐键一致（沿用 S6 既有守卫） |
| **P5** | Bridge 删除（目录清空）+ 守卫换代（J8） | `Agent.TestShared\`、守卫工程 | 1 提交 | J1–J8 全绿 + `Adapters\` 为空 + 重烘焙（`Check-BakeFramework.ps1`） |

**顺序强制**：P1 与 P2 可并行（不同仓库）；**P4 依赖 P2/P3**（无标准与产品绑定，声明式装配无目标）；**P5 依赖 P1–P4 全部**。

### 8.1 实施进度（就地回填，2026-09-15）

| 阶段/子步 | 状态 | 实测证据 |
|---|---|---|
| **P0** 冻结与基线 | ✅ 完成 | 快照 `Docs\analysis\2026\9\15\DataCenter-统一标准与通用端口-P0基线快照.md`：15 文件 SHA256/字节（合计 124,202 B，与 §1.4 一致）+ 迁移期引用面（`PortArgs.*` 21 处 / 5 族） |
| **P1-1** `PortArgs` → 内核 `PortInputExtensions` | ✅ 完成 | 新增 `Strategy\Observability\Ports\PortInputExtensions.cs`（`Arg/ArgOrNull/ArgBool/ArgInt/With<T>`）+ 内核异常族 `PortInputMissingException`/`PortInputTypeException`（替代测试侧 `SpecRunException`，见 §8.2 偏差 X3）；`Bridge\PortArgs.cs` 已删（不留薄壳）；5 族 21 处调用点改静态形式（`PortInputExtensions.Arg(input, i)`，零语义变更）；判据：Observability.Tests **88/88** ✅、`Agent.Spec.Guard.Tests` **24/24** ✅ |
| **P1-2** `ReflectivePortBinding` → `Ports\ReflectiveBinding.cs` | ✅ 完成 | 新增内核 `Ports\PortBindingSpec.cs`（`PortBindingSpec` + `PortArgSpec` 声明模型）与 `Ports\ReflectiveBinding.cs`（`ReflectiveBinding` + `ReflectivePortResolver` + `PortTypeLoader`）+ 内核异常 `PortBindingViolationException`；**单一实现点**：测试侧 `Bridge\PortLedger.cs` 自持的 `PortArgSpec` 已删并改用内核类型；`Bridge\ReflectivePortBinding.cs` 已删；`PortRegistry` 增 `ToBindingSpec` 映射（`legacyKind`/`productFields` 不入内核）；判据：Observability **88** ✅、Guard **24** ✅、Core **422** ✅ |
| **P1-3** `PortRegistry` → `Ports\PortAssembler.cs` | ✅ 完成 | 新增内核 `Ports\PortAssembler.cs`（`PortAssembler.Seed/Bind` + `PortBindingPlan`/`PortBindingPlanKind` + `Unbound/Frozen` 记账）；**记账唯一权威**上移内核：`AgentPortHost` 的 `Unbound/Frozen` 改为读装配器视图（`MarkUnbound/MarkFrozen` 删除），测试侧 `PortRegistry` 只提供 `Plans`（台账条目 → 绑定计划）；内核只接 JSON 文本与计划，不读文件/嵌入资源；判据：Observability **88** ✅、Guard **24** ✅（含 Unbound 负向探针 + 装配幂等） |
| **P1-4** 端口宿主 → L2（按 D5） | ✅ 完成 | 新建 L2 工程 `Environment\CentralTest\SpecHost\`（`SpecPortHost` + 三注入接口，零领域引用）；内核新增 `Projection\EventProjection.cs`/`MetricTotalsProjection.cs`（U7/U8 实测：内核既有件语义不兼容 → 并列新件，只加不改）；Agent 面经 `Agent.TestShared\Fixtures\AgentSpec{PortSurface,ScopeProvider,EventSource}.cs` 注入；`Bridge\AgentPortHost.cs`+`PortProjections.cs` 同批删除；判据：内核 **92**、L0 **168/169**、Guard **24**、Core **422**、Observe **46** 全绿；`Bridge\` 110,395 B → **98,162 B**（−12,233 B）；细节见 `Docs\analysis\2026\9\15\DataCenter-统一标准与通用端口-P1-4开工卡.md` §9/§5.1 |
| **P2** `DataCenter.Standards` | ✅ 完成 | 新增 `DataCenter.Protocol\Standards\`（5 模型：`PortStandard`/`DataStandardItem`/`EventStandard`/`FakeStandard`/`IStandardCatalog`）+ 新子工程 `DataCenter.Standards\`（`StandardJson` 读取含未知键 fail-fast、`StandardCatalog` 装载、`StandardConsistency` C1–C6、`StandardSchemaValidator` 结构门、`StandardViolationException`）+ `schemas\{ports,data,events,fakes}.schema.json`；`DataCenter.slnx`/`DataCenter.csproj`/`Directory.Build.props`（GlobalUsings 豁免）已登记；判据：`DataCenter.Standards.Tests` **11/11** ✅（含产品台账 C1–C6 + 结构门）、`Agent.Spec.Guard.Tests` **24 → 26** ✅（§3.4 落点② `StandardConsistencyGuardTests`）；**U1** 无环（Standards → Protocol → Abstractions）、**U2** 仅用 `System.Text.Json`（未引第三方）、**U5** 首轮基线 = **0 违例**（与 §8 预判「首轮必定报错」不符：产品台账现状已合规，无需修数据） |
| **P3** Agent 端口化 A1–A5 | ⏳ 待开工 | — |
| **P4** `.specs` `given ports` | ⏳ 待开工 | — |
| **P5** Bridge 删除 + 守卫换代 | ⏳ 待开工 | — |

### 8.2 实施期实测偏差（就地留痕）

| # | 偏差 | 实测依据 | 处置 |
|---|---|---|---|
| **X1** | §4.1 原定把宿主落 `TestInfrastructure\DataDriven\SpecPortHost.cs`（L2） | `SpecKernelLayeringGuardTests` 扫描 `DataDriven\*.cs` 顶层，禁 `Agent.*/Strategy.*/Operation.*/Share.*/DataCenter.*`；宿主需引 `Strategy.Observability` + Agent 作用域面 → 落此即破 I2 | 改落新 L2 工程（§9 **D5**） |
| **X2** | §5.2 记 `.specs` 语法扩展「6 处联动」 | 实测改动面 **9 处**：`SpecCompactParser`（`DirectiveNames` 白名单 + `switch`）/`SpecCompactModel`/`SpecSchemaRules`/`SpecIrBinder`（键序）/`test-spec.schema.json`/`SpecRunner`+`JsonSpecTestBase`（驱动注入）/`Analyzer` 词表与诊断/`Tools\Expand-SpecSource.ps1`（参考实现同语义）/`README`+`SKILL.md` | P4 按 9 处排工 |
| **X3** | §4「逐字节搬运」 | `Bridge\PortArgs.cs` 抛测试侧 `SpecRunException`（定义于 `TestInfrastructure\DataDriven\SpecModel.cs:183`）；内核（Frame 平台库）不可引测试件 | 新增内核异常族（消息体逐字保留，仅前缀与异常类型随内核惯例） |

---

## §9 待裁决（开工前定，避免返工）

| # | 问题 | 影响 | 推荐 |
|---|---|---|---|
| **D1** | 标准模型落 `DataCenter.Protocol\Standards`（跨语言共基）vs 直接扩内核 `Share.HierarchySupport` | 依赖方向与 TS 侧复用 | **落 DataCenter**（跨语言共基是本方案的立项理由）；映射器（`PortStandard → BoundaryContract`）落点须按 **Layer 表**核对——若 `Observability` 在 `DataCenter` 之下则落 `Observability`，否则落 **L1 组合根** |
| **D2** | `.specs` 是否新增 `given ports` | 决定 Bridge 能否归零 | **新增**（仅此一处语法扩展；`when`/`then`/17 算子不动） |
| **D3** | 产品绑定是否走声明式 `impl` 段 | 决定 `AgentPortBindings.cs` 是否可缩 | **走声明式**（`reflective` 可直接声明；需要替身/工厂的用 `delegate{id}` 索引到实现字典） |
| **D4** | 测试侧台账何时删 | 决定 J3 达成时点 | 随 S6 各族退场**渐进删**（每族退场即删该族 `legacyKind` 条目），S6 结束即 J3 达成 |
| **D5** | 端口宿主的 L2 落点（`AgentPortHost.cs` + `PortProjections.cs` 归宿；本卡 §4.1 原建议 `DataDriven\SpecPortHost.cs`） | 决定 I2 能否与 P1-4 同时成立 | **落新 L2 工程 `Environment\CentralTest\SpecHost\`**（只引 `TestInfrastructure` 的 `ISpecPortHost` + `Strategy.Observability`；Agent 特有面 `AgentTrace.AmbientScopeId`/`AgentObservability` 经注入接口）。**不可落 `DataDriven\`**：实测 `SpecKernelLayeringGuardTests`（C-1）扫描 `DataDriven\*.cs` 顶层并禁 `Agent.*/Strategy.*/Operation.*/Share.*/DataCenter.*`，落此即破 I2（本卡原落点据实测校正） |
| **D6** | 仓库级 `fakes.json` 是否随 P4 建 | 决定 P4 表达力与体量 | **暂不建**：只做 `given ports` **内联**声明；5 原语覆盖不到的族**暂不迁**（回滚端口实现），缺口登记为新原语提案（内核评审后加） |

---

## §10 不变量与风险

### 10.1 不变量（违反即回退）

| # | 不变量 | 来源 |
|---|---|---|
| I1 | 内核零领域词汇（`Observability` 内不得出现 decision/tool/skill/llm/…） | `Observability\README.md` 通用性边界 |
| I2 | L0（`TestInfrastructure\DataDriven\*.cs`）禁领域引用（`Agent.*`/`Strategy.*`/`Operation.*`/`Share.*`/`DataCenter.*`） | `SpecKernelLayeringGuardTests`（C-1） |
| I3 | 断言算子 17 封顶；四根不变（`Port`/`Event`/`Timeline`/`Aggregate`） | `DataDriven\README.md` §2.4、上位方案 §6.5 |
| I4 | 公开 API **只加不改**（新维度用新增重载 / `init` 属性 / 新类型） | 上游卡 §7（B-1/B-2/B-4） |
| I5 | 单一实现点：端口绑定、契约、装配入口各**恰一处** | `SingleImplementation`；`PortCatalog.Register` 重复即抛 |
| I6 | Frame 为 git submodule：内核/DataCenter 改动**各自独立提交** | 上游卡 §7（B-6） |
| I7 | 生成物（`*.spec.json`/`*SpecTests.g.cs`）禁手改；只改 `.specs` | 通用处理文档 R1（N-2） |
| I8 | 契约即断言：`Emit` 未注册即抛；标准校验失败即阻断（不 warn） | 上位方案 §4.3 |

### 10.2 风险与缓解

| 风险 | 触发条件 | 缓解 | 回退 |
|---|---|---|---|
| 标准与内核**双写**（一处 JSON 一处 C# 各自为政） | P2/P3 期间两套入口并存 | 过渡期定死：**JSON 为声明源、C# 只给实现**；两套差异由 C1–C6 与既有守卫暴露 | 停用标准入口，保留 C# 注册（现状可用） |
| 上移引入行为漂移 | 搬运时"顺手优化" | 逐字节搬运 + 每件跑全套基线；**禁止在上移提交里改语义** | 恢复单文件 |
| `.specs` 语法扩展引发 6 处联动（分析器/展开器/驱动/守卫/模板/文档） | D2 决策后 | 只加 `given ports` 一种指令，不做 `invoke/observe` 全套（E-7 实测教训：语法联动面越大越难收） | 撤回该指令，回到现 `when <PortKey>` |
| 替身语义表达力不足（现有 5 原语覆盖不到的场景脚本） | P4 改写某族时发现无法声明 | 该族**暂不迁**，保留端口实现（Bridge 局部延后）；把缺口登记为新的 fake 原语提案（内核评审后加） | 该族回滚 |
| DataCenter 反向依赖 Observability | 映射器落位选错 | D1 落点先核对 Layer 表；L0.5 只出模型、不引机制类型 | 映射器移 L1 |
| 测试侧"假绿"（跑了同一实现） | 装配双路径风险 | 沿用 S6 对拍守卫谓词：**端口侧必须有调用记录**（J7 判据） | — |

---

## §11 未实测声明与回填位

**本文为设计（未实施）**，以下为**尚待实测**的判定点（实施时逐条回填）：

| # | 待实测项 | 回填位置 |
|---|---|---|
| U1 | `DataCenter.Protocol` 能否承载 `Standards\` 模型（依赖是否出现环） | §3.2 / §9 D1 ｜ **实测（P2）**：可承载且**无环** —— `DataCenter.Protocol\Standards\` 为纯模型（零引用）；`DataCenter.Standards` → `DataCenter.Protocol` → `DataCenter.Abstractions` 单向；`Directory.Build.props` 已把 `DataCenter.Standards` 加入 GlobalUsings 豁免（否则 CSG/TConfig 外部命名空间击穿零依赖约束） |
| U2 | `StandardSchemaValidator` 是否引第三方 JSON Schema 库（现库内仅 `System.Text.Json`） | §3.2 ｜ **实测（P2）**：不引第三方，自实现 `JsonDocument` 子集校验器（`type`/`required`/`properties`/`additionalProperties`/`propertyNames.pattern`/`items`/`minItems`/`minProperties`/`enum`/`pattern`） |

| U3 | 反射绑定上移后类型可达化在**产品进程**（非测试进程）的行为 | §4.1（G-10 结论适用性） |
| U4 | `given ports` 在 `SpecSourceExpander`/分析器/驱动三处的改动量 | §5.2 / §9 D2 |
| U5 | 现有 26 条端口在 C2–C6 判据下的实际违例数（首轮基线） | §3.4 ｜ **实测（P2）**：产品台账（10 端口 / 36 数据项 / 33 事件）首轮 **0 违例**（C2/C3/C4/C5 现状因 `emits` 全登记、无 `carries`/跨切字段/`producer` 声明而通过；C6 无基线跳过）；测试侧 26 条端口台账不参与（P5 退场件） |
| U6 | Bridge 各件上移后的 TestInfrastructure/内核 API 命名冲突与可见性（`internal`→`public` 面） | §4.2 |

---

## 附录 A · 关联文档

| 文档 | 关系 |
|---|---|
| `Docs\analysis\2026\9\9.14\AgentTest_适配器归零_统一数据标准与观测内核方案.md` | **上位方案**（UDS-1/2/3、目标架构、113 kind 台账）；本卡为其 §四/§五 的落地补强 |
| `Docs\analysis\2026\9\9.14\AgentTest_适配器归零_N5-N7实施卡.md` | 当前执行卡（S6 子批、守卫、实测偏差 E-N5-*/E-N6-*） |
| `Docs\analysis\2026\9\9.14\AgentTest_适配器归零_N5收尾执行卡.md` | S5 收尾与 N6 交接（台账/生成器/接线状态） |
| `Frame\DataCenter\Doc\DataCenter-架构设计.md` | DataCenter 定位（跨语言数据中枢、CSG 数据面、schema 纪律） |
| `Frame\CsharpPrj\Strategy\Observability\README.md` | 内核 E1–E8 职责与通用性边界 |
| `Environment\CentralTest\TestInfrastructure\DataDriven\README.md` | L0 分层 / 17 算子封顶 / 守卫清单 |

## 附录 B · 术语对齐（勿另立）

| 术语 | 权威 | 本文用法 |
|---|---|---|
| UDS-1/2/3 | 上位方案 §四 | 分别对应：数据/边界契约标准、端口标准（`PortCatalog`）、观测标准（边界事件） |
| 端口键 | 上位方案 §6.2 + 生产 `ports.json` | `<类型/接口>.<方法>`，与生产端口面同一命名空间 |
| `fake` 五种原语 | 上位方案 §6.3 + 内核 `StandardFakes` | `recording/fixed/echo/notExpected/sequence`（**已实现**，勿再造） |
| 四根 | 上位方案 §6.5 | `Port(...)`/`Event(...)`/`Timeline(...)`/`Aggregate(...)` |
| 台账 | 本卡 | 特指三本 JSON：`ports`（含 `impl`）/ `data` / `events`（+ `fakes`） |

> **文档维护**：本卡为**活卡**——每完成一个阶段（P1–P5）就地回填 §8 状态列与 §11 实测项；与上位方案冲突时以**实测**为准并双向留痕。




