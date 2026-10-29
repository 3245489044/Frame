# Agent 门禁模块化分类与通用框架抽取

> 分析对象：`Frame\PowerShellScript\Operation\DSL\Agent`（2026-10-29 实测）
> 结论定位：**分类轴补一维（判据面）+ 目录落一维；通用面抽 5 个原语；判据语义一律留在门内。**

---

## 0. 结论摘要

| 维度 | 现状 | 问题 | 建议 |
|---|---|---|---|
| 规模 | 88 件 `Check-*.ps1` + 2 件工具件，约 11,570 行（均值 131 行） | 单件体积尚可，**样板占比高** | 不拆门，只收样板 |
| 分类 | 单轴 `Domain`（已由 `GateTaxonomy` T1–T5 强制） | Agent 域内 88 件无二级分类；3 件被他域登记、3 件未登记，Domain 同时承担"落点"与"被检对象"两种语义 | 补 `Family`（落目录）+ `Form`（只登记）两轴；`Domain` 收敛为"落点域" |
| 复用 | 5 个原语模块已存在（GateRuntime / GateFileIndex / GateBaseline / GateClosure / VerifyScope） | 采用率低：Agent 域内 `Write-GateVerdict` **0 次**、`Test-ClosureSet` **0 次**、`Get-GateFiles` 仅 9 件、`New-GateResult` 全仓 1 件 | 原语下沉到头部/尾部，判据主体不动 |
| 组合 | 聚合门通过**输出文本正则**委托其它门（`Get-CountByMark`、`-match '发现 (\d+) 处违规'`） | 文本协议 = 隐式契约，任何一门改文案即静默降级 | 结构化结果（JSON）+ 门图调度 |
| 治理 | `Check-GateTaxonomy` 实测 **12 处违规**（11 条索引漂移 + 1 条未登记件） | 分类索引产物无人刷新 | Batch 0 先清零，再谈新增轴 |

---

## 1. 现状盘点

### 1.1 运行机制（四层，已有且成熟）

```
GateManifests.psd1  ──(Build-GateIndex.ps1)──▶  index.generated.md
      │ 分类单源（227 件 / 11 域）                    │ 编译产物（禁手改）
      ▼                                              ▼
Invoke-GateCheck.ps1 ──按 Domain 解析落点──▶ Check-<Gate>.ps1 ──▶ 0/1 退出码
      │ fast/full 分层、并行、共享文件索引                 ▲
      └────────────── Check-GateTaxonomy.ps1 T1–T5 三方对账 ┘
                        （清单 ⇄ 索引 ⇄ 脚本）
```

关键机制（决定了搬迁成本，后文反复引用）：

- `Resolve-GateScript`（[DirectoryMap.ps1](file:///D:/Project/Main/Frame/PowerShellScript/PScriptManager/Framework/DirectoryMap.ps1#L184-L212)）在 Domain 候选目录内**递归**按叶子名发现 ⇒ **门脚本可自由下沉子目录，运行侧零改动**。
- `Check-GateTaxonomy` T3 用 `Get-ChildItem -Recurse` 枚举 `DSL\` 全域 ⇒ 目录分包不影响归属可达判据。
- `Get-GateScriptCell` 输出仓库相对路径并喂给 `Build-GateIndex` ⇒ 重跑生成器即自动更新索引路径。
- 因此：**目录改布局是低成本的；真正贵的是"分类字段没人校验"**（当前 11 条 T4 漂移即证据）。

### 1.2 量化事实（实测，可复算）

| 指标 | 数值 | 说明 |
|---|---|---|
| `Check-*.ps1` 件数 | 88 | 另有 `Generate-RecognizedKinds.ps1`、`Invoke-StorageProbe.ps1` 两件工具件 |
| 总行数 / 均值 | ≈11,570 / 131.5 | 最长 `CircuitSemantics` 781 行，最短 `ContextGeometryPrefixPurity` 55 行 |
| 域归属 | Agent 82 / Usharp 1 / KeySpace 1 / Meta 1 / 未登记 3 | `DispatchTraceSchema`、`AgentStorageIsolation`、`FallbackInventoryRate` 三件被检对象是 Agent 却在他域 |
| 根定位风格 | 4 种：固定上溯串 57 件（含 2 件工具件共 59 处）/ while 上溯 12 件 / `-Root` 参数 4 件 / 无根逻辑 15 件 | 同一件事 4 套写法，`Get-SuperRoot` 已存在 |
| 裁决风格 | 手写 `exit` 87 件 / `Write-GateVerdict` 0 件 | 全仓 `Write-GateVerdict` 仅 23 件在用（且不在 Agent 域） |
| 正/负对照 | 65 件含"负对照"字样（286 处），25 件含"正对照"（137 处） | 全部内联手写，无统一自证原语 |
| 源文件枚举 | `Get-ChildItem -Recurse` 35 件 52 处；构建产物排除（obj/bin）14 件 16 处 | `Get-GateFiles` 仅 9 件 10 次（全仓 25 件） |
| 域路径硬编码 | `Projects\Plug\Agent` 80 件 173 次 | `directory-map.json` 无对应键 ⇒ 不可换根、不可复用 |
| 基线 | `Import-GateBaseline` 10 件 | `Test-BaselineNoIncrease` 全仓 3 件 |
| 集合闭合 | `Test-ClosureSet` 全仓 3 件 8 次，Agent 域 **0** | 但至少 8 件自写"声明↔实现"比对 |
| 结构化结果 | `New-GateResult` 全仓 1 件 3 次 | 组合门只能读文本 ⇒ 见 1.3 第 9 条 |
| 模式协议 | `-Check` 86/88；另见 `-NegativeControl/-ShowDetail/-Emit/-FailOnWarning/-AllowDataRootLogic/-AgentRoot` 等 | 无统一"报告/判定/自检/JSON"四模式契约 |
| 退出码 | `exit 1` 106 处 / `exit 2` 20 处（11 件），文案各异 | 0/1/2 语义（通过/违规/前置缺失）无统一原语 |

### 1.3 十类重复样板（按可抽取度排序）

| # | 样板 | 代表件 | 可抽性 |
|---|---|---|---|
| 1 | 根定位（4 套写法） | `Check-AgentCarrierSurface.ps1`、`Check-ContextGeometryPrefixPurity.ps1`、`Check-AgentDataSeparation.ps1` | **高**（已有 `Get-GateRepoRoot`/`Get-SuperRoot`） |
| 2 | 标题 / 汇总 / 退出码尾巴 | 全部 87 件 | **高**（已有 `Write-GateHeader`/`Write-GateVerdict`） |
| 3 | 参数与模式协议、前置缺失 `exit 2` | `Check-AgentRuntimeUIConfig.ps1`（`-FailOnWarning`）、`Check-AgentDefaultDispatch.ps1`（无 `-Check`） | **高** |
| 4 | 源文件枚举 + 注释行/构建产物排除 | 35 件 | **高**（已有 `Get-GateFiles`） |
| 5 | 正/负对照（判据自证） | 65 件 | **高**（纯样板，无语义） |
| 6 | 棘轮基线（只减不增） | 10 件 | 中（已有 `Test-BaselineNoIncrease`） |
| 7 | 集合闭合（declared ↔ implemented ↔ exempt） | `ActionCatalogSingleSource`、`WorkflowRegistration`、`PsSkillToolMap`、`SendDeclarationCoverage`、`LaneCatalog`、`TransferKey` | 中（已有 `Test-ClosureSet`） |
| 8 | 声明面装载（JSON / `*.ushar` / psd1） | `AgentSpec`、`AgentDefaultDispatch`、`LaneCatalog`、`TransferKey` | 中（需限定原语，防万能解析器） |
| 9 | 门间组合（**经输出文本正则**） | `Check-AgentDataSeparation.ps1`、`Check-PowerShellHostSingleSource.ps1` | 中（需先有结构化结果） |
| 10 | 域路径硬编码 `Projects\Plug\Agent` | 80 件 | **高**（`directory-map.json` 增键） |

第 9 条当前实现（[Check-AgentDataSeparation.ps1](file:///D:/Project/Main/Frame/PowerShellScript/Operation/DSL/Agent/Check-AgentDataSeparation.ps1#L48-L80)）：`& $gate @Parameters 6>&1` 捕获文本，再用 `Get-CountByMark` 与正则 `'发现\s*(\d+)\s*处违规'` 反推计数。**任何被委托门改一行文案 ⇒ 上游静默降级为 0 违规（假绿）**，这是当前最贵的一处耦合。

---

## 2. 模块化分类模型

### 2.1 现有轴的边界（先诊断，再补轴）

| 轴 | 载体 | 现状语义 | 问题 |
|---|---|---|---|
| `Domain` | `GateManifests.psd1` 字段 → `directory-map.gateDomains` | **落点域**（决定去哪个目录找脚本） | 被当成"被检对象域"使用 ⇒ 3 件 Agent 语义脚本被登记到 Usharp/KeySpace/Meta |
| `Tier` | `fast` / `full` | 运行成本分层 | 与语义无关，**不应进目录** |
| `Expect` | 整数 | 期望退出码 | 同上 |
| `Note` | 文本 | 人读摘要 | 未参与任何判据 |

结论：`Domain` 是"组织轴"，必须与"语义轴"分离。当前 88 件里 **11 件声明面 + 13 件契约 + 23 件装配 + 17 件表示 + 9 件观测 + 10 件边界 + 5 件元**，却全部平铺在一个目录里，检索、复用、责任归属都靠文件名前缀硬猜。

### 2.2 建议：三轴模型（两轴落目录，一轴只登记）

| 轴 | 取值 | 是否进目录 | 判定载体（结构事实） |
|---|---|---|---|
| **A. Domain**（既有） | Agent / Meta / Data / … | ✅ 一级目录（不变） | `GateManifests.Domain`（`GateTaxonomy` T1 已强制） |
| **B. Family（判据面）** | `Declaration` `Contract` `Runtime` `Presentation` `Observability` `Boundary` `Meta` | ✅ Agent 域内二级目录 | 新增 `GateManifests.Family`；`GateTaxonomy` 新增 T6 枚举校验 |
| **C. Form（判据形态）** | `SingleSource` `Closure` `Boundary` `Ratchet` `Drift` `Consistency` | ❌ 只登记 | `GateManifests.Form`；`GateTaxonomy` 新增 T7 枚举校验 |

**为什么 Form 不进目录**：与 `Baselines` 分类模型（[README.md](file:///D:/Project/Main/Frame/PowerShellScript/Baselines/README.md#L15-L28)）同一公理——「判据轴可以多，目录轴必须少；按形态分目录会让同一文件分裂，销账即触发无意义 `git mv`」。一门常态是"单源形态 + 棘轮基线"混合体，按 Form 落目录必然两难。

Family 与 Form 的关系：Family 决定"看哪一类结构"，Form 决定"用什么运算判它"。同一 Family 下常见多形态（如 `Runtime` 下既有 `SingleSource` 也有 `Consistency`）。

### 2.3 目录落点

```
Operation\DSL\Agent\
├── Declaration\      (11)  Config\*.json / *.ushar 声明合格性、末路完备、映射闭合
├── Contract\         (13)  注册表/词表/能力位/车道/工作流/键址的声明↔实现对账
├── Runtime\          (23)  装配入口、单源槽、单入口、生效面、上下文几何
├── Presentation\     (17)  渲染入口/区域归属/视图目录/文本安全/读模型
├── Observability\    (9)   trace schema、失败信封、空态与未知可见化、诊断投影
├── Boundary\         (10)  层边界、依赖方向、零领域词、存储隔离、承载面度量
├── Meta\             (5)   脚本纪律、重放对拍、生成物布局、兜底登记（门）
└── Meta\_tooling\    (2)   Generate-RecognizedKinds.ps1 / Invoke-StorageProbe.ps1（**非门禁件**）
```

三点约束（均有机器判据兜底）：

1. **搬迁零成本**：`Resolve-GateScript` 递归发现 + T3 递归枚举 + `Build-GateIndex` 复算 ⇒ 只改 `Family` 字段与物理位置，不动 `GateManifests` 的键。
2. **写件与只读面分离**：`Generate-RecognizedKinds.ps1 -Emit` 会写 `Agent.Contract\NodeDomain\RecognizedKinds.cs`，与"门禁目录 = 只读面"冲突；`Invoke-StorageProbe.ps1` 起子进程产出 JSON。二者归 `Meta\_tooling\`，并从"可被 `-All` 调度"的集合里排除（T3 白名单按 Gate 名匹配，不受路径影响）。
3. **跨域件按被检对象复核**：`DispatchTraceSchema`（分发 trace）、`AgentStorageIsolation`（Agent 存储零落盘）、`FallbackInventoryRate`（Agent 兜底）建议 Domain 改回 `Agent`，与 Family 一致；若因历史原因保留他域，则在 `Family` 中体现真实判据面，避免"目录在 Agent、域在 KeySpace"的二次错位。

### 2.4 88 件分类表

> 载体列取脚本头注的判据载体；`Form` 取主判据形态（多形态件取第一判据）。

#### Declaration（11）

| Gate | Form | 判据载体 |
|---|---|---|
| `AgentSpec` | Closure | `recognizers.json` + `round-shape.ushar` + `conversation-canon.ushar` 映射闭合 |
| `AgentDefaultDispatch` | Closure | `Config\subagents.ushar` / `history-modes.ushar` / `history-loop.ushar` 意图→标签→腿成对 |
| `GatherSpec` | Closure | 聚拢声明面合格性 + 末路完备 |
| `IdentityTemplatePrefix` | Closure | `identityTemplate` 引用可解析 ∧ 模板封闭 ∧ 前缀可判定 |
| `LaneCatalog` | Closure | `lanes.ushar` 结构事实 + PS 域声明一致 |
| `NodePromptBinding` | Closure | 模板槽 / 绑定域 / 读路径 |
| `PromptSegments` | SingleSource | 执行体入口必须经段装配，短常量不得充当 L0 |
| `RoundShape` | Closure | 样本轮次满足 `round-shape.ushar` |
| `SendDeclarationCoverage` | Closure | 发送面声明载体覆盖 |
| `SignalDeclaration` | Closure | 信号词表/迁移/判定表/六段流/端口登记对账 |
| `WorkflowRegistration` | Closure | 文件名常量 / 流号映射 / 登记表三集合等价 |

#### Contract（13）

| Gate | Form | 判据载体 |
|---|---|---|
| `ActionCatalogSingleSource` | SingleSource | 运行期注册表 → 加载期校验器投影（禁第二张手写白名单） |
| `AgentProcessSurface` | SingleSource | `proc.` 键族字面量单源（`ProcessKeys.cs`） |
| `CanonicalForm` | Closure | 未映射 type 必须被拒绝 |
| `DelegateEntry` | Closure | 委派入口：可发现 / 已装配 / 进模型面 / 真等待 |
| `ExecutionLaneGate` | Closure | 声明 workflow 的域不得同时声明单步直调 + 默认车道能力门 |
| `IntentContract` | Closure | IEF 三张表的注册表声明结构事实 |
| `IntentMappingNoLiteral` | Boundary | 映射表键引用常量，零 wire 字面量 |
| `IntentParity` | Consistency | IEF 写读同源对账 |
| `IntentSupportsProjected` | SingleSource | 能力位注册表投影（禁逐 intent 分支） |
| `KeyspaceMirror` | Drift | 仓库声明 vs Data 运行时声明（family 模板 + port 集） |
| `PsSkillToolMap` | Closure | SKILL 表 ↔ `ActionToTool` 对账 |
| `ToolCallConvergence` | SingleSource | 工具装配/注册/连接/观测单点 + 内核纯净 |
| `TransferKey` | Closure | 键文法/词表/存储/轨/双写声明齐备 |

#### Runtime（23）

| Gate | Form | 判据载体 |
|---|---|---|
| `ActorBindingSingleWriter` | SingleSource | 执行体身份单写 |
| `ActorInvocationSingleSource` | SingleSource | 调用登记单源 |
| `AgentBuildPoint` | SingleSource | 装配入口唯一 + 根引导唯一 + 绑定先于解析 |
| `AgentRuntimeSlots` | Boundary | 静态可变运行时槽 K10 + 取用面 S4–S6（含基线） |
| `CircuitSemantics` | SingleSource | CircuitFramework 唯一宿主接线，禁平行引用抽象 |
| `ContextGeometryConvergence` | SingleSource | 白名单外零内层端口解析 / 裁剪单入口 |
| `ContextGeometryPrefixPurity` | Boundary | 前缀正文/规范字节/段禁含信封字段 |
| `DetailReadWriteSamePort` | SingleSource | 行身份聚拢点 G3 |
| `DomainResolveSingleEntry` | SingleSource | 裸标签别名表 + 解码分派各一处 |
| `EffectiveFace` | Drift | 运行时装载面 vs 构建产物 SHA256 |
| `EffectiveModeSingleSource` | SingleSource | 会话期上下文单源 |
| `EntrySingleSource` | SingleSource | 写库即实现的单一入口 |
| `GatherClosure` | Closure | 直呼域方法 / 未声明写回的可复算清单 |
| `GatherDecl` | Boundary | 框架公开面与装配点受控 |
| `HostAccessNoRestate` | SingleSource | 接口/实现不各写一遍逐槽成员 |
| `LlmRoutingSurface` | Boundary | 缺省/兜底不得为 local、禁静默降级 |
| `PowerShellHostSingleSource` | SingleSource | 全仓仅一处宿主定位实现，其余只委托 |
| `ReadinessSingleEntry` | SingleSource | 就绪/诊断单点 |
| `RecordDraftSingleSource` | SingleSource | 记录草稿单源 |
| `RowIdentityBareKey` | Ratchet | 行身份裸键回退路径 = 1 |
| `SaveDispatchNoDomainSwitch` | Boundary | 编排函数零域分支 |
| `TemplateAccessSingleEntry` | SingleSource | 模板访问单入口 |
| `WriteCapabilitySinglePredicate` | SingleSource | 域写能力判定入口唯一 |

#### Presentation（17）

| Gate | Form | 判据载体 |
|---|---|---|
| `AgentRuntimeUIConfig` | Consistency | JSON 驱动 UI 配置：布局重叠 + 绑定链 |
| `DbRenderPurity` | Boundary | 渲染路径只读交互态 |
| `DbToolbarDictAlloc` | Ratchet | 渲染路径零逐命令 `new Dictionary` |
| `HistoryAssemblySurface` | Consistency | 空态预装配 + 档案分档 + 动态视口单入口 |
| `ReadModelOnly` | Boundary | Turn 渲染面只消费读模型 |
| `RecordSingleTextForm` | Boundary | 详情态只保留整记录编辑器 JSON |
| `RenderNoDomainBranch` | Boundary | 呈现投影聚拢点 G4，零域分支 |
| `UiAnchorWrites` | SingleSource | 选择态只由渲染入口写 |
| `UiNoViewBranch` | Boundary | 视图差异只允许是声明 |
| `UiReadOutcome` | SingleSource | 读取代数经 `UiReadState`，禁字符串哨兵 |
| `UiRegionOwnership` | SingleSource | 每个 `RegionKind` 恰一个生产者 |
| `UiRenderEntry` | SingleSource | 页面产物必经 `RenderOutputCoordinator` |
| `UiSelectionExternalization` | Consistency | 选择态外置一致性 |
| `UiTemplateSet` | Closure | 生效模板集含区域渲染必需键 |
| `UiTextSafe` | Boundary | 进 DOM 的值必须经 `HtmlText` |
| `UiViewCatalog` | SingleSource | 视图类型键/作用域/解析只在目录声明 |
| `UiWriteFidelity` | Consistency | 整页原始 HTML 与 Tree/Bulk 往返等价 |

#### Observability（9）

| Gate | Form | 判据载体 |
|---|---|---|
| `ActorObservationEmptyReason` | Boundary | 空态/结果通道可见（NoSilentOutlet） |
| `ActorObservationSurface` | Consistency | 调用实例驱动的观测面 |
| `DispatchTraceSchema` | Closure | 六阶段词表封闭 ∧ 新遥测 msgId 已登记 |
| `EchoConstants` | SingleSource | 回显投影单源（禁叙事式常量回显） |
| `GatherDiagnosticsSingleSource` | SingleSource | 诊断文本由模型字段投影 |
| `OperationRowCarriesActor` | Consistency | 操作流承载执行体 G5 |
| `RoundFailureEnvelope` | Consistency | 失败信封 + 单轮 trace |
| `SignalTraceSchema` | Closure | 相位封闭 + 日志键登记 + 迁移可见化 |
| `UnknownVisible` | Boundary | 禁静默出口 U |

#### Boundary（10）

| Gate | Form | 判据载体 |
|---|---|---|
| `AgentCarrierSurface` | Ratchet | 承载面/装配入口/配置 API/状态解析入口计数（R1–R5） |
| `AgentDataSeparation` | Consistency | D1–D5 聚合（委托 3 门 + 归属台账） |
| `AgentDecisionCoreBoundary` | Boundary | 产品侧禁引用确定性核心状态化承载 |
| `AgentLayerBoundary` | Boundary | 运行层 × 数据层三条不变式 |
| `AgentModulesFrameworkLayout` | Boundary | 文件归属 / 契约内联 / 依赖方向 / 零 Agent 件 |
| `AgentStorageIsolation` | Boundary | 零落盘 / 零 SQLite / 观测面断言 |
| `ContextGeometryBoundary` | Boundary | 领域无关 / 零依赖 / 禁时钟网络 |
| `RefreshFrameworkBoundary` | Boundary | 刷新框架零领域/零 Plate |
| `ShareMessagingBoundary` | Boundary | 域无关 / 零依赖 / 单状态机 / 直连禁令 |
| `VmKernelReferenceBoundary` | Boundary | 内核热路径零编译依赖 |

#### Meta（5 + 2 工具件）

| Gate | Form | 判据载体 |
|---|---|---|
| `CanonReplay` | Consistency | 同一输入必得同一节点集；丢帧必产生可见差集 |
| `FallbackInventoryRate` | Ratchet | 静默兜底必须登记基线（未登记即红） |
| `GenerationLayout` | Consistency | `gen-<n>` 产物契约 |
| `GenerationRebuildChain` | Consistency | 换代重建链 |
| `ScriptNoFixedSleep` | Ratchet | 脚本禁固定等待（只降不升基线） |
| `Generate-RecognizedKinds.ps1` | — | 声明 → C# 镜像生成/校验（写模式） |
| `Invoke-StorageProbe.ps1` | — | Node 探针产出 JSON（起进程） |

分布汇总：`SingleSource` 27 · `Boundary` 23 · `Closure` 20 · `Consistency` 12 · `Ratchet` 4 · `Drift` 2 —— **判据形态只有 6 种，全部可由"集合运算 + 文本扫描 + 正负对照"三件原语表达**，这正是通用框架可抽的边界。

---

## 3. 可提取为通用框架的逻辑

### 3.1 抽取边界（先划红线）

| 可抽 | 不可抽 |
|---|---|
| 根定位、落点解析、参数/模式协议、退出码 | 判据语义（哪条规则算违规） |
| 源文件枚举、注释/产物排除、行号定位 | 领域词表与正则（如 `proc.`、信封字段、`NonScalar`） |
| 正/负对照执行与自证 | 阈值与基线数值（归 `Baselines\*.psd1`） |
| 集合差/闭合/覆盖率计算 | 判据载体选择（"看哪个文件/哪个符号"是领域知识） |
| 声明面文件读取（受限） | 声明语义解释（`*.ushar` 的领域含义） |
| 结果结构、JSON 输出、门图调度 | 组合门里"D1 委托哪三门"这类领域编排 |

与 `Baselines\README.md` 的既有纪律一致：**「解析归各门，防万能解析器」**。抽取只下沉"怎么做"，不下沉"判什么"。

### 3.2 复用面清单（复用既有模块，不新建同类）

> 约束：新增任何 `.psm1` 必须进 `Baselines\_catalog.psd1`（`Role='Lib'`），否则 U12 双射判红；同名函数禁第二实现（U8 教训：`Import-GateBaseline` 曾两实现导致运行时绑错）。

| 模块 | 现状 | 建议增量 | 消样板（估算） |
|---|---|---|---|
| `GateRuntime.psm1` | `Get-GateRepoRoot` / `Find-GateScript` / `New-GateResult` / `Write-GateHeader` / `Write-GateVerdict` / `Test-BaselineNoIncrease` | `New-GateContext`（一次解析 `-Check/-Detail/-Json/-NegativeControl/-Root`）、`Test-GatePrerequisite`（缺前置 ⇒ 统一 exit 2） | ≈1,200 行（根定位 + 头尾 + 前置） |
| `GateFileIndex.psm1` | `Get-GateFiles`（git ls-files 单源 + 共享缓存） | `Find-GatePattern -SkipComments -SkipGenerated`（把 35 件的 `Get-ChildItem` + 注释排除收敛） | ≈350 行 |
| `GateClosure.psm1` | `Test-ClosureSet`（Missing/Orphans/Coverage/Verdict） | 增加 `-RequireFull` 严格档（对齐 `DeclarationClosure` 的 Strict 用法） | ≈250 行 |
| `GateBaseline.psm1` | `Import-GateBaseline` + 事实行 Value/Origin | 无（已足够） | — |
| **新增** `GateControl.psm1` | 无 | `Assert-GateControls -Cases @(@{ Good=…; Bad=…; Predicate=… })`：正样本必过、负样本必红；**未提供对照 ⇒ 直接判红**（"判据无自证 = 不可采信"） | ≈390 行（65 件 × 6 行） |
| **新增** `GateDecl.psm1` | 无 | `Import-GateJson` / `Get-UsharpEntries` / `Get-DeclarationFile -Key`（按目录映射键取声明面，禁硬编码路径） | ≈200 行 |
| **新增** `GateReport.psm1` | 无 | `ConvertTo-GateJson`（`New-GateResult` 序列化）、`Read-GateResult`（供组合门读） | ≈150 行 |
| **新增** `GateCompose.psm1` | 无 | `Invoke-GateDelegation -Gates @(...)`：拓扑序、去重、共享文件索引、结构化取回；替代 `Invoke-DelegateGate + Get-CountByMark` | ≈120 行 |

合计可消除样板 **≈2,600 行**（约占 Agent 域 22%）；单件平均可从 131 行降至 ~90 行，新增一门的样板成本从 ~60 行降到 ~20 行。

### 3.3 关键 API 形状（示意，非最终实现）

```powershell
# 门脚本新形态（判据主体不变，仅头尾收敛）
[CmdletBinding()]
param([switch]$Check, [switch]$Detail, [switch]$Json, [switch]$NegativeControl)
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Check:$Check -Detail:$Detail -Json:$Json

$rules = @(
    # 每条判据自带正/负样本 ⇒ Assert-GateControls 可直接执行自证
    @{ Id='C1'; Form='SingleSource'
       Carrier='Projects\Plug\Agent\src\Agent.Ui\Ui\Views\HtmlText.cs#NonScalar'
       Predicate={ param($t) $t -match 'NonScalar' }
       Good='class HtmlText { /* NonScalar */ }'; Bad='class HtmlText { }' }
)

Invoke-GateRules -Context $ctx -Rules $rules -Root $ctx.Root |
    Write-GateVerdict -Check:$Check -Key 'UiTextSafe'
```

```powershell
# 组合门（替代文本协议）
$results = Invoke-GateDelegation -Gates @('AgentRuntimeSlots','DataCenterGenericity','SingleWriter') `
                               -Context $ctx -Json
# $results[i] = @{ Gate; ViolationCount; Exit; Violations[]; ElapsedMs } —— 零文案依赖
```

### 3.4 配套的落点治理（第 10 类样板）

`directory-map.json` 的 `paths` 增键（禁在门内硬编码）：

```json
{
  "AgentRoot":   "Projects/Plug/Agent",
  "AgentSrc":    "Projects/Plug/Agent/src",
  "AgentConfig": "Projects/Plug/Agent/Config",
  "AgentDoc":    "Projects/Plug/Agent/doc"
}
```

> 80 件 173 处 `Projects\Plug\Agent` 的替换必须与抽取同批完成；否则"通用框架"仍被单一工程的物理路径钉死。

---

## 4. 落地批次

| 批次 | 范围 | 入口条件 | 出口判据（机器可判） |
|---|---|---|---|
| **0 清零** | 重跑 `Build-GateIndex.ps1`；`ReadModelOnly` 登记为门或入 T3 白名单；复核 3 件跨域件的 `Domain` | 现状 | `Check-GateTaxonomy -Check` 违规 0（当前 12） |
| **1 分类轴** | `GateManifests` 增 `Family`/`Form`；`Build-GateIndex` 增两列；`Check-GateTaxonomy` 增 T6（Family 枚举）/T7（Form 枚举）；按 2.4 表搬迁 7 个子目录 | Batch 0 绿 | T6/T7 0 违规；`Invoke-GateCheck -All` 全绿；索引无漂移 |
| **2 P0 原语** | `GateRuntime` 增 `New-GateContext`/`Test-GatePrerequisite`；`GateFileIndex` 增 `Find-GatePattern`；新增 `GateControl`；选 20 件高样板件试点（`AgentDataSeparation`/`AgentRuntimeSlots`/`CircuitSemantics`/`HistoryAssemblySurface`/`ActorObservationSurface` 等） | Batch 1 绿 | 试点件行为等价（同输入同退出码）；`-NegativeControl` 全覆盖试点件；`_catalog.psd1` 双射仍绿 |
| **3 P1 闭合/声明** | `Test-ClosureSet` 推广 8 件自写比对；新增 `GateDecl`；`directory-map` 路径键替换 80 件硬编码 | Batch 2 绿 | 闭合类判据覆盖率可复算；硬编码计数从 173 → 0 |
| **4 P2 组合** | 新增 `GateReport`/`GateCompose`；改 `AgentDataSeparation`/`PowerShellHostSingleSource` 为结构化取回；退役 `Get-CountByMark` 文本反推 | Batch 3 绿 | 组合门输出不再依赖被委托门文案（负对照：改被委托门文案仍应得同结论） |
| **5 收口** | 更新 `Baselines\README.md`（登记新模块 + 分类模型对齐）、`Registry\index.generated.md` 复算 | Batch 4 绿 | 文档与实现单源一致；新增一门只需 ~20 行样板 |

每批都必须附**负对照**：故意破坏一处判据（或改被委托门文案），验证对应守卫确实判红。

---

## 5. 风险与反模式

| 风险 | 表现 | 处置 |
|---|---|---|
| 抽成"万能解析器" | 一个 `Parse-Anything` 吃 JSON/ushar/C#，语义漂移到框架 | 只提供三类受限读取；语义解析留门内（对齐既有纪律） |
| 抽成"第二套门框架" | 新旧并存、同名函数两实现 | 只在 `Baselines\` 增量，禁另立根；新模块进 `_catalog`（U12） |
| 分类字段变"文档纪律" | 加 `Family` 但无守卫 ⇒ 下次再加门就漂移 | T6/T7 与 T4 同批上线，缺一不落 |
| 用行数当 KPI | 为减行而合并判据 ⇒ 判据变粗 | 以"新增门样板行数"与"负对照覆盖率"为判据，不设总行数硬指标 |
| 搬迁破坏声明面引用 | 头注/README 里的 `…\Baselines\<X>.psd1`、`Consumer` 路径失效 | T2/T3 已覆盖落点；U10 覆盖声明面路径 ⇒ 搬迁同批跑 `Check-GateBaselines -Check` |
| 组合门隐式契约 | 结构化结果字段中途改名 | 结果结构由 `GateReport` 单源定义；组合门只读该结构 |
| 只读面混写件 | `-Emit` 生成器留在门禁目录被 `-All` 调度 | 归 `Meta\_tooling\`，排除出调度集合 |

---

## 附录 A. 分类轴字典

**Family**

| 值 | 定义 | 典型结构 |
|---|---|---|
| `Declaration` | 声明面自身合格性（配置/词表/模板的形态与完备性） | `Config\*.json`、`*.ushar` |
| `Contract` | 声明 ↔ 实现 ↔ 注册表的对账 | 注册表、词表、能力位、车道、工作流 |
| `Runtime` | 装配与运行期的单入口/单源/生效面 | 组合根、槽位、解析器、宿主定位 |
| `Presentation` | 表示层只读性、归属唯一、往返保真 | 渲染器、视图目录、模板集、DOM 文本 |
| `Observability` | 遥测 schema、失败信封、空态与未知可见化 | trace、event key、诊断投影 |
| `Boundary` | 层/域/依赖边界与度量 | csproj 依赖、命名空间、承载面计数 |
| `Meta` | 门禁自身纪律与过程件 | 脚本纪律、重放对拍、生成物布局 |

**Form**

| 值 | 定义 | 判定运算 |
|---|---|---|
| `SingleSource` | 同一知识仅一处定义/写入 | 全仓计数 + 白名单 |
| `Closure` | 声明集 ↔ 实现集（含豁免） | `Test-ClosureSet` |
| `Boundary` | 禁引用/禁依赖/禁词/禁出口 | 文本/依赖扫描 |
| `Ratchet` | 只减不增 | `Test-BaselineNoIncrease` + 基线文件 |
| `Drift` | 产物/镜像与声明对拍 | 复算 + 逐字比对 |
| `Consistency` | 多视图/多路径等价 | 结构等价断言 + 样本对拍 |

## 附录 B. 与 `Baselines` 分类模型的映射

| 本文件轴 | `Baselines` 轴 | 关系 |
|---|---|---|
| `Domain`（A） | A2 域 | 同名同源（`gateDomains`） |
| `Family`（B） | 无 | 本文件新增：门禁侧的唯一二级组织轴 |
| `Form`（C） | A4 形态（FactRow/Grid/Table…） | 正交：前者是"判据形态"，后者是"基线数据形态"，均只登记不落目录 |
| — | A1 角色 / A3 来源 | 门禁侧对应 `Role=Data/Lib` 与基线 `Origin` |

> 一句话：**两条轴（Domain、Family）进目录；`Form`/`Origin` 只进登记表**——与 `Baselines\README.md`「判据轴可以多，目录轴必须少」保持同一公理，避免两套分类互相打架。

## 附录 C. 实施记录（2026-10-29）

### C.1 已完成

| 批次 | 交付 | 落点 |
|---|---|---|
| Batch 0 | 索引重生成（消 11 条 T4 漂移）；`ReadModelOnly` 入 T3 伴随白名单（基线未事实行化，暂不登记为门） | `Registry\index.generated.md`、`Meta\Check-GateTaxonomy.ps1` |
| Batch 1（元数据） | 85 件 Agent 域门写入 `Family`/`Form`；生成器输出两列；新增 T6（Family ∈ 7 值）/T7（Form ∈ 6 值）+ 谓词正负对照 | `Registry\GateManifests.psd1`、`Tooling\Build-GateIndex.ps1`、`Meta\Check-GateTaxonomy.ps1` |
| Batch 2（P0 原语） | `New-GateContext` / `Assert-GatePrerequisite` / `Assert-GateControls`（`GateRuntime.psm1`）；`Find-GatePattern`（`GateFileIndex.psm1`）；`directory-map.json` 增 `AgentRoot/AgentSrc/AgentConfig/AgentDoc` | 见左 |
| Batch 2（试点 6 件） | `ActionCatalogSingleSource`、`ContextGeometryPrefixPurity`、`DbRenderPurity`、`RowIdentityBareKey`、`DetailReadWriteSamePort`、`ReadinessSingleEntry` 改为「上下文 + 自证 + 统一裁决」 | `Operation\DSL\Agent\` |

试点前后行为等价（退出码不变）；试点件内 `Get-ChildItem -Recurse` 归零（`Check-GateFileIndex` 的 R1 债务可销账）。

### C.2 验证

| 判据 | 结果 |
|---|---|
| `Check-GateTaxonomy -Check` | 0 违规（T1–T7 全绿） |
| `Invoke-GateCheck -Only <6 试点>,GateTaxonomy` | 7/7 PASS |
| 框架自检：坏谓词（恒真）/空转（恒假）/缺对照 | 各上报 1 条；正常对照 0 条 |
| `New-GateContext` 根定位 | 从 `DSL\Agent` 上溯得 `D:\Project\Main`（与深度无关） |

### C.3 偏差与暂缓（实施中发现的约束）

1. **目录搬迁暂缓**：文档 §2.3 假设「搬迁零成本」——实测不成立。88 件中 57 件用固定五级上溯 `'..\..\..\..\..'` 定位仓库根，搬入二级子目录即断；另有 2 件按同级路径引用彼此脚本（`Check-GenerationRebuildChain` → `Check-EffectiveFace` / `Check-GenerationLayout`）、3 件在 `param()` 默认值里写相对路径。**搬迁前必须先统一根定位（推广 `New-GateContext`）**，否则 57 件同时失效。
2. **`Family` 目前是登记轴，不落目录**：T6/T7 已机器校验，物理目录仍平铺；待根定位统一后作为纯机械批次搬迁（`Resolve-GateScript` 递归发现 ⇒ 清单/索引零改动）。
3. **`GateDecl` / `GateReport` / `GateCompose` 未建**：当前无消费者，按「无消费者不建模块」暂缓；`AgentDataSeparation` 的文本协议仍在（改文案即假绿），需先有 `GateReport` 再改组合门。
4. **3 件跨域件保持原 `Domain`**（`DispatchTraceSchema`=KeySpace、`AgentStorageIsolation`=Usharp、`FallbackInventoryRate`=Meta）：改 Domain 需同步 `_catalog.psd1` 的 `Domain` 字段（U12），收益仅为目录语义一致；其真实判据面已由 `Family`（Observability / Boundary / Meta）表达。
5. **环境限制**：本机仅 Windows PowerShell 5.1（无 `pwsh`）。10 件 `#Requires -Version 7.0` 的门禁、以及 `Check-GateFileIndex` / `Check-GateBaselines` 中依赖 `.NET Core` API 的判据无法本地执行；上述验证均限于 5.1 可运行面。

### C.4 后续批次（按依赖排序）

1. 根定位统一：把 57 件固定上溯替换为 `New-GateContext`（或 `Get-GateRepoRoot`），同步替换 19 件 `Baselines` 相对导入。
2. 目录搬迁 + `_catalog.psd1` 的 `Consumer` 路径同步（13 处）。
3. 试点扩散：其余 82 件按 `Family` 分批接 `New-GateContext` / `Assert-GateControls`；每批附负对照。
4. `GateReport` + `GateCompose`：先结构化结果，再改 `AgentDataSeparation` 的 `Get-CountByMark` 文本反推。
5. `GateDecl`：随 8 件声明面门禁（`AgentSpec`/`LaneCatalog`/`TransferKey` 等）一起落地，避免空模块。
