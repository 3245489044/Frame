# Usharp 接入 · 实施记录（P0 基线 / P1 L-6 / P2 端口 / P3 适配器 / 门禁 / 退役看板）

> **上游权威**：本目录 `节点通用框架-表层语言与Usharp接入.md`（§3 端口 / §8 迁移 N0–N3 / §13.7 L1–L4 / §14 表达形态实测）。
> **本文件只记录已落地事实与实测证据**，不重复设计语义。
> **全量收口方案**：`Projects\Plug\Agent\doc\节点域全量工作流化（声明驱动）总体方案.md`（目标态判据 W-1…W-5 / 缺口 G-1…G-6 / 施工单 W1–W8）；本文件记录其**已完成的 P0–P3**。
> **日期**：2026-09-27

---

## 1. P0 基线冻结（实测，2026-09-27）

### 1.1 门禁（`Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1 -Only <名>`）

| 门禁 | 结果 | 实测明细 |
|---|---|---|
| `NodeLocPlacement` | 0 违规 | 位置 id 8 / Loc 词表 1 / 反解域坐标 9 / 反解行 8 |
| `NodeKeyAddress` | 0 违规 | 域 4 / 位置 8 / 位置 id 并集 8 |
| `NodeFramework` | 0 违规 | 扫描源码文件 36 个；A/B/C/D/F/H 全通过，告警 0 |
| `KeyGrammar` | 0 违规 | 构造/解析/族判据/键序均走内核单源 |
| `NodeWriteBypass` | 0 违规 | — |

### 1.2 套件

| 套件 | 基线 | P1 机制后 | P1 调用点切换后 |
|---|---|---|---|
| `Usharp.Emit.Tests` | 22/22 | 22/22 | 22/22 |
| `Modules.Node.Tests` | 128/128 | 132/132 | **133/133** |
| `Agent.Modules.Tests` | 129/129 | 129/129 | 129/129（断言加固后仍绿 ⇒ 行为等价） |

> 基线数字与 `节点三转换-应用工作流化-下一步实施规划.md` §5 的记录一致 ⇒ 该文件基线未漂移。

### 1.3 未决项（沿用文档推荐值作为实施默认；**属 config 级，可再裁**）

| # | 未决项 | 本批默认（来源：文档推荐） |
|---|---|---|
| U13 | 顺序权威 | 声明序权威；转移表 = 派生缓存（判据 `表(键) ≡ 声明序相邻对` 待立） |
| U17 | loc 词表分层 | 单值 → 多值 + 分层（`session.*` / `cline.session.*` / `archive.*`）；落地前维持现状单值 |
| U18 | 反解表属性面 | 多矩阵（每属性一矩阵），零新增语言单元 |
| U19 | 反解方向 | 单向 `loc → placement`（双向即 P22） |
| U20 | loc 与 UR 同源 | 同源（L-7） |
| U21 | JSON 降级路径 | 不保留（仅语言路径）—— 已由 `NodePlacementDeclaration.Load` 的「缺失即抛」实现 |

---

## 2. P1 已落地：读侧位置来源接 L-6（`loc` 反解）

### 2.1 改动

| 件 | 变化 |
|---|---|
| `Projection/NodeReadProjection.cs` · `ReadScope` | 新增 `Loc`（loc 值）；给出 ⇒ 位置集合 = 反解产物；`PlacementIds` 降为**收窄面**（未给 `Loc` 时行为与现状逐字一致） |
| `Projection/NodeReadProjection.cs` · `NodeReadProjection` | 新增可选 `IPlacementResolver`；新增 `ResolveByLoc`（L-6 单源）；未装配 ⇒ ∅ + `projection.loc-without-resolver`（Error） |
| `NodeFramework.cs` | 组合根把 `PlacementResolver` 注入 `Projection`（读/写自此共用**同一**反解实例） |

**不变式对齐**：L-5（失败即 ∅ + 可见化，禁回退默认载体）· L-1（`loc` 不产身份）· L-6（读写同一反解）· P20（未引入任何 `Usharp*` 类型）。

### 2.2 新增合同用例（`Modules.Node.Tests/Projection/NodeReadProjectionTests.cs`）

| 用例 | 判据 |
|---|---|
| `Projection_LocResolvedPlacements_MatchWriteSideSet` | **CT-L6**：读侧位置集合 ≡ 写侧物化位置集合（同一 `IPlacementResolver`） |
| `Projection_UndeclaredLoc_YieldsEmptyWithDiagnostic` | **CT-L4**：未声明 loc ⇒ ∅ + `placement.loc-undeclared`（载体有数据也不兜底） |
| `Projection_LocWithNarrowing_ReadsOnlyRequestedPlacement` | `PlacementIds` 在 loc 来源下仅作收窄，不改变位置来源 |
| `Projection_LocWithoutResolver_IsVisibleNotSilent` | 端口未装配 ⇒ ∅ + 可见化（对齐 `US9001` 范式） |

夹具：`FrameworkFixtures.Loc`（= `workflowId`，反解表键单源）。

### 2.3 本次暴露的实测事实（R1 复现，已入规程）

**R1「Frame 二进制部署副本陈旧」在本次实测中复现两次**，且方向相反：

1. **改 `Frame\CsharpPrj` 后测试报「成员不存在」**（测试经烘焙面消费 `CsharpPrj\BuildOutput`）⇒ 必须重烘焙：
   `pwsh Frame\CsharpPrj\Bake-CsharpPrj.ps1`（内部 = `dotnet build MainProj.sln` + manifest/stamp 刷新）。
2. **重烘焙后 `Agent.Modules.Tests` 报 `MissingMethodException: ReadScope..ctor(5 参)`**（预编译 `Agent.Modules.dll` 仍按旧签名调用）⇒ 必须重刷 Agent 部署副本：
   `dotnet build Projects\Plug\Agent\Agent.sln -c Debug`。

> 结论：**改 `Modules.Node` 公共签名 = 三件套**：重烘焙 CsharpPrj → 重建 Agent → 再跑套件。缺任一步都是"假绿/假红"。

### 2.4 读侧调用点切换（本轮完成： (a) 的行为等价部分）

| 调用点 | 变化 |
|---|---|
| `Agent.Modules\NodeSpec\NodeDomainPort.cs` | `ReadScope` 增 `Loc: NodeKeys.DefaultWorkflowId`（`PlacementIds` 保留为读侧收窄） |
| `Agent.Modules\History\HistoryOpsFunctions.cs`（`history_project` / `history_compose`） | 同上（段域上下文面） |
| `Agent.Launcher\Modules\Libraries\AgentUiLibraryHistoryOps.cs`（`ComposeAsync`） | 同上 |

**行为等价依据（实测）**：`PlacementIds` 在 loc 来源下仅作**交集收窄**，故
`位置集合 = Resolve(loc) ∩ Domain ∩ PlacementIds` 恰等于原 `PlacementIds ∩ Domain`
⇒ Agent 套件 129/129 全绿（含段域 compose/project 用例），无回归。
`NodeKeys.DefaultWorkflowId` 为既有 loc 单源（不新增第三份常量）。

**判据加固（本轮）**

| 用例 | 位置 | 判据 |
|---|---|---|
| `EveryDomain_PlacementResolverIsBackedByDeclaration`（加固） | `Agent.Modules.Tests/NodeDomainCatalogTests.cs` | 字面量 `"agent-round"` → `NodeKeys.DefaultWorkflowId`；新增 **CT-L6（声明级）**：`Resolve(loc)` 位置集 ≡ `descriptor.DeclarationPlacementIds`（差异即"声明坐标 ≠ 运行期 loc 值"或键写成占位名 ⇒ 反解永久落空） |
| `Projection_LocChange_KeepsKeyAndChangesPlacement`（新增） | `Modules.Node.Tests/Projection/NodeReadProjectionTests.cs` | **CT-L3**：仅改 loc ⇒ 身份键与载荷不变、位置变（L-1 身份/位置解耦） |

### 2.5 新增门禁：`Check-NodeLocConsistency.ps1`（HV-1 / I8）

| 判据 | 内容 |
|---|---|
| **C1 loc 字面量定义面** | `"agent-round"` 只允许出现在**定义清单**（8 件：单源 `NodeKeys.cs` / 镜像 `WorkflowIds.cs` / 契约默认值 / 注释说明）；未登记文件出现即违规 |
| **C2 回退债务基线（只减不增）** | inline 回退仅允许出现在已登记 4 件（`TaskOrchestrateFunction` / `WritePlanner` / `AgentUiLibraryTaskPool` / `AgentInvocationRouter`）；新增即违规；收敛后须从清单删除 |
| **C3 读侧位置来源** | `new ReadScope(` 构造点须带 `Loc:`（L-6）；实测 **4/4** |
| **C4 正对照（防假绿）** | 单源文件确含 `DefaultWorkflowId` 定义 + 声明面确含 `[Space] Loc` + `ReadScope` 构造点数 ≥ 基线 4 |

登记：`GateManifests.psd1` → `NodeLocConsistency`（`Args = @{ Check = $true }`，`Expect = 0`）。
验证：干净态 0 违规 / exit 0；**负对照**（临时单行文件含裸字面量）1 违规 / exit 1。

> **教训（已修，值得复用）**：首版 C1 用 `Get-Content` 逐行索引，**单行文件**返回 `[string]` ⇒ `$lines[0]` 取到的是
> **首个字符**而非整行 ⇒ 单行文件永不命中 = 判据**假绿**。修正：`$lines = @(Get-Content ...)`（强制数组）。
> 负对照是发现它的唯一手段 —— 印证判据纪律「新增门禁必须带负对照」。

---

## 3. 续批补齐（CT-U4 / CT-L2 / P2 / P3）

### 3.1 CT-U4：转移表 ≡ 声明序相邻对（U13 顺序单源）

| 件 | 内容 |
|---|---|
| 场景面 | `Usharp.TestShared\Fixtures\UsharpEmitSurface.cs` 增 `transitionTableMatchesDeclarationOrder`（双向：条数相等 ∧ 每对相等；单步链 ⇒ 期望空表） |
| kind 契约 | `UsharpEmitKinds.cs` 字段声明同步（21 → 22 项，序 = 装配序） |
| 用例 | `usharp.emit` +2：`emit.agent-loop.transition-table-is-declaration-order`（4 步 ⇒ 3 条）/ `emit.subflow.transition-table-is-declaration-order`（单步 ⇒ 0 条，防「空表恒真」） |

### 3.2 CT-L2 迁移安全门 + 写侧 L-6 半边（Agent.Modules.Tests +3）

| 用例 | 判据 |
|---|---|
| `PlacementDeclarationAndDomainSpecs_AreBidirectionallyComplete` | **CT-L2**：声明面位置 id 集 ≡ 各域声明位置 id 并集（漏声明 ⇒ 反解落空；多声明 ⇒ 死声明） |
| `EveryDomain_PlacementAttributeFaceIsComplete` | 位置属性面（carrier / key / writer）非空（空值 = 不可物化的静默缺口） |
| `EveryDomain_WriteTargetsAreResolvableFromLoc` | **写侧 L-6 半边**：写目标位置 ⊆ `Resolve(loc)` 位置集（写侧与反解面漂移即失败） |

> 写侧**不改为** `Resolve(loc)` 供位（证据见 §2 之前的记要 / 下表 b 行）；本条以「写目标 ⊆ 反解面」的判据收口 L-6 的写侧语义。

### 3.3 P2：`ISpecSource` / `IDefinitionDiagnostics` 端口装配（CT-U2）

| 件 | 内容 |
|---|---|
| `NodeFramework.FromSpec` | 增可选 `specSource` / `orientationAuthority` / `definitionDiagnostics`（未给 ⇒ Null + 可见化） |
| `Agent.Modules\NodeSpec\DeclaredSpecSource.cs`（新） | JSON 装配面：域声明 + 位置反解声明面 → 定义集（零语义、零语言类型） |
| 同名文件 `NodeDefinitionDiagnostics` | `IDefinitionDiagnostics` 落地：`INodeDiagnosticsSink` + 模块日志（单向） |
| 装配 | `AgentNodeDomains.TryCreate` 注入二者；`IOrientationAuthority` 维持 Null（B-4 零消费方，不假装生效） |
| 用例 | `SpecSource_JournalsDeclaredDocuments_AndNullIsVisible`（CT-U2：未装配 ⇒ 空集 + `spec.source-unbound`；已装配 ⇒ 非空 + 来源可辨 N9） |

### 3.4 P3：`Usharp.Adapters.Node`（唯一引 Node 处）

| 件 | 内容 |
|---|---|
| 工程 | `Frame\Usharp\src\Usharp.Adapters.Node\`（csproj + `Usharp.sln` 登记 = 第 8 个 Usharp 聚合单元） |
| 实现 | `UsharpNodeSpecSource`（`ISpecSource`）：`.ushar` 编译 → 每 `[Flow]` 经 `UsharpRoundSpecEmitter` → `RoundSpecChainMapper.ToJson`（**唯一映射点**）→ `SpecDocument`；诊断单向投影（error 不降级） |
| 口径 | 编译含 error ⇒ **定义集为空**；单流 error / 零步骤 ⇒ **该链不产出**（不留残缺骨架） |
| 烘焙接线 | `UsharpTests\Directory.Build.props`：产品 dll 引用 + `EnsureUsharpBaked` 列表增本工程 |
| 用例 | `usharp.node-adapter` 套件 +4（链产出与骨架侧函数名逐项一致 / 子链 / US5002 不产链 / 闸A error 不产集） |

---

## 4. 退役看板（CT-U5：每类旧定义恰一个退役条件）

| # | 旧定义 | 现状（实测） | 退役条件 | 等价门 / 判据 |
|---|---|---|---|---|
| 1 | `Config\workflows\*.json`（agent-loop / collect-analyze-act / session-answer / session-cline） | **已退役**：移入 `_deprecated\` + `<Compile Remove>`；`workflows\agent-loop.json` 作为**冻结基线夹具**保留 | 已达成（退役动作已执行） | `Usharp.Emit.Tests` `emit.agent-loop.equivalent-to-config-workflow`（与冻结基线逐字段等价） |
| 2 | C# 内置 `RoundSpec` 回退常量（`RoundSpecLoader` basename→FlowId 映射 + 内置回退） | **并存**：`.ushar` 为真源，回退仍在 | `.ushar` 覆盖全部在用 flow ∧ 回退分支**调用点计数归零**（回退期须打日志） | 调用点数基线（`NodeLocConsistency` 同范式；待立） |
| 3 | 键集/位置 id 常量（`HistoryMaterializeService.*Placements`、`descriptor.DeclarationPlacementIds`） | **并存**：作为写目标/读收窄面 | 位置面全量声明化 ∧ 读写同源收口（本轮已立「写目标 ⊆ 反解面」判据；口径见 §3.2） | CT-L2 + 写侧 L-6 半边 + `NodeKeyAddress` A7 |
| 4 | `PlacementSpec` 的**属性面**手写声明（域 JSON 的 placements 字段） | **并存**：位置 id 已由 `placement.ushar` 承载，属性面（carrier/key/form/retention/writer）仍在域 JSON | **U18 裁决后**（属性面是否进 `[Matrix]`）方可退役；未裁前不得擅改语言 | 属性面完备判据（§3.2）+ `Check-NodeLocPlacement` C2/C3 |
| 5 | `Config\instances\nodes.json` 的位置 id 清单 | **并存**：与声明面双向对账 | 清单改由声明面**派生**（生成而非手写） | CT-L2（声明面 ≡ 域声明集）+ `NodeKeyAddress` A6 |

> 纪律（P18）：**并存期必须有等价门**（上表逐行给出）；**回退路径必打日志**；无「永续并存」条目 —— 第 2/3/4/5 行的条件均已可机器判定或已挂待裁标记。

---

## 5. 验证命令（P0–P3 批实测通过；W1 批实测见 §6.2）

```powershell
dotnet build Frame\CsharpPrj\Modules\Node\Modules.Node.csproj      # 0 警告 0 错误
pwsh Frame\CsharpPrj\Bake-CsharpPrj.ps1                            # 102 程序集就绪且新鲜
dotnet build Projects\Plug\Agent\Agent.sln -c Debug                # 0 警告 0 错误
dotnet build Frame\Usharp\Usharp.sln -c Debug                      # 0 警告 0 错误（含新桥接工程）
dotnet test Environment\CentralTest\CSharpTest\Modules.Tests\Node.Tests\Modules.Node.Tests.csproj   # 133/133
dotnet test Environment\CentralTest\AgentTest\Agent.Modules.Tests\Agent.Modules.Tests.csproj       # 133/133
dotnet test Environment\CentralTest\UsharpTests\Usharp.Emit.Tests\Usharp.Emit.Tests.csproj         # 28/28（W1 后）
dotnet test Environment\CentralTest\UsharpTests\Usharp.Adapters.Tests\Usharp.Adapters.Tests.csproj # 27/27（W1 后）
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\HierarchyMethod.Tests\HierarchyMethod.Tests.csproj # 146/146（W1 后）
Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1 -Only NodeLocPlacement   # 0 违规
Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1 -Only NodeFramework      # 0 违规
Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1 -Only NodeKeyAddress     # 0 违规
Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1 -Only AgentBuildPoint    # 0 违规
Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1 -Only NodeLocConsistency # 0 违规（负对照 1 违规 / exit 1）
Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1 -Only NodePortPurity     # 0 违规（负对照 1 违规 / exit 1）
Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1 -Only NodeHierarchyRefs  # 0 违规（W1 新增）
```

---

## 6. W1 归属边声明化（已落地，2026-09-27）

> 上游：《节点域全量工作流化（声明驱动）总体方案》§5 W1。
> **白名单裁决（用户裁定）**：按**实测收敛** = 4 类 `{history_owns_task, cross_db_link, session_owns, task_owns_task}`；
> `history_owns_node` 走**退役**（清理 `ReferenceGrounding` 存留写点）。
> 依据：方案文档只列 3 类，实测 `ReferenceGrounding` 实际写 4 类边 + 1 类待退役 ⇒ 以实测为准。

### 6.1 交付物

| # | 件 | 内容 |
|---|---|---|
| 1 | `Projects\Plug\Agent\Config\nodes\hierarchy-refs.ushar`（新） | 归属边事实**唯一真源**：`[Space] HierarchyRef`（4 坐标）+ 4 条 `[Ref] … edge=…`。**禁 `loc=`**（未注册字段被静默忽略 ⇒ 假声明）；端面只由 `src`/`tgt` 承载 |
| 2 | `Frame\Usharp\src\Usharp.Emit\UsharpRefEdgeEmitter.cs`（新）+ `UsharpDiagnostic.cs`（`US5007`） | 中立 JSON（`edgeTypes` + `refs`）；无带 `edge=` 的 `[Ref]` ⇒ **不产出** + `US5007`。边类型值**原样透出**（禁 kebab 归一，须与 `CrossDbReferenceTypes` 逐字对账）。<br>命名沿革：方案记 `UsharpRefLocEmitter`；因 W1 裁「禁 `loc=`」改名以名实相符 |
| 3 | `Frame\CsharpPrj\...\Abstractions\ReferenceEdgeCatalog.cs`（新） | `IReferenceEdgeCatalog` + 报告模式（`PermissiveReferenceEdgeCatalog`，**默认**）/ 拒绝模式（`DeclaredReferenceEdgeCatalog`）+ 层内中立可见化口 `IReferenceEdgeDiagnostics`（`DataStoreDriven` 不引 `Modules.Node`，故不自造上层诊断类型依赖） |
| 4 | `Frame\CsharpPrj\...\Core\ReferenceGrounding.cs` | 4 个写边方法（`AddSessionMessageLinkAsync` / `AttachHistoryTaskAsync` / `EnsureTaskNodeLinkAsync` / `LinkSubTaskAsync`）增 `AdmitEdge` 准入 ⇒ 未声明边**拒写**；属性注入（同 `NodeStore` 范式），**默认报告模式 = 零行为变更** |
| 5 | `history_owns_node` 退役 | `AttachHistoryNodeAsync` 写点移除 ⇒ **抛**（W-4 缺失即抛）+ 派生视图（禁独立存边回流）；`HistoryOwnsNode` 常量保留作旧数据兼容读；`IReferenceMaintenance` 成员保留仅为源兼容 |
| 6 | `Frame\PowerShellScript\Modules\Node\Check-NodeHierarchyRefs.ps1`（新）+ `GateManifests.psd1` | E1 闸A / E2 声明⇄代码**双向对账** / E2c 退役债务只减不增 / E3 `[Ref]` 字段白名单 / E4 正对照 / E5 负对照 |
| 7 | `Frame\Usharp\src\Usharp.Adapters.Node\UsharpDeclarationReferenceIndexPort.cs`（新） | `IReferenceIndexPort` 的**语言装配**（W1⑤）：字段面 = 本编译**实际用到**的 `[Ref]` 字段（派生，零域内硬编码）；反向引用归因 = 声明行 `文件:行`。<br>**域分离**：节点**载荷**引用字段（`NodeReferenceFields`：AddConfigAddKey / ParameterKeys / …）属节点域**属性面**，其上移依赖 U18 ⇒ W6；本端口不覆盖、不替代 |
| 8 | 文档 | `Frame\Usharp\README.md` §5 诊断码表补 `US5006`（原缺登记）+ `US5007` |

### 6.2 实测判据（本轮）

| 判据 | 结果 |
|---|---|
| `Check-NodeHierarchyRefs -Check` | **0 违规**（声明边类型 4 / 代码实测 4 / 常量 5 / `[Ref]` 字段命中 16） |
| ↳ 变异自测（负对照） | 真实声明面注入 `| loc=agent-round` ⇒ **4 处 E3 违规 + exit 1**（随后还原，门禁复绿）⇒ 判据非恒绿 |
| `NodeLocPlacement` / `NodeKeyAddress` / `NodeLocConsistency` / `NodePortPurity` | 全 exit 0（新声明面未扰动既有判据） |
| `Usharp.Emit.Tests` | 24 → **28/28**（+4：声明产出逐字段 / src-tgt 端面 / 退役类型不回流 / 缺声明不产出 US5007） |
| `Usharp.Adapters.Tests` | 25 → **27/27**（+2：声明面引用索引字段面 / 反向引用归因） |
| `HierarchyMethod.Tests` | 143 → **146/146**（+3：未声明边拒写+可见化 / 未装配=报告模式 / 空白名单=全拒+可见化） |
| `Modules.Node.Tests` / `Agent.Modules.Tests` | **133/133** / **133/133**（零回归） |
| 构建 | `DataStoreDriven` · `Usharp.sln` · `Agent.sln` 均 **0 警告 0 错误**；`Bake-CsharpPrj.ps1` = **102 程序集就绪且新鲜** |

> **判据纪律**：新增门禁带正对照（E4 三条基线）+ 负对照（E5 两条：判据命中 + 闸A 对 `loc=` **0 错**，
> 锁死 R3「未注册字段被静默忽略」实测结论，证明 E3 非冗余）。变异自测在**真实声明面**上做，非仅夹具。

### 6.3 回退口径（逐项）

| 项 | 回退 |
|---|---|
| 发射器 | 无人消费即等同未启用；删声明面 ⇒ 门禁 E4 正对照失效（可见），运行期零行为变化 |
| 写边准入 | 不注入 catalog ⇒ **报告模式**（= 现状逐字一致）；属性注入可单点置回 |
| `history_owns_node` | 源码级退役（抛）：回退 = 恢复原写点，**须同批**把门禁 E2c 债务基线改回 `@{'HistoryOwnsNode'=1}` |
| 语言侧 | 删 `UsharpRefEdgeEmitter` + `US5007` + 适配端口；声明面与门禁随之删 |

### 6.4 显式未做（登记，非静默跳过）

| # | 项 | 理由 / 归属 |
|---|---|---|
| 1 | W1③ 的**组合根接线**（切拒绝模式） | 默认报告模式 = 零行为变更；切换点 = `AgentComposition.cs:298` 与 `Agent.StorageBridge\Program.cs:254` 两处 `new ReferenceGrounding(...)` 注入 catalog —— 属 **W5**（双读对拍期一并切换，需等价门） |
| 2 | W1⑤ 的「`NodeReferenceFields.CreateIndexPort()` 字段面改由 `.ushar` 产出」 | 那 4 个字段是节点**载荷属性名**，不在 `hierarchy-refs.ushar` 表达力内 ⇒ 属 **W6**（属性面上移，**U18 阻塞**）；强做即臆断 + 越过 U18 |
| 3 | 等价门（迁移期逐字段） | 本批为**纯新增**（无旧定义被替换），无「迁移期等价门」对象；W1 判据只要求“声明产出逐字段 + 缺声明不产出”，已由 +4 例锁死 |
| 4 | `IReferenceIndexPort` 接入框架 options | 端口实现已就绪；注入点（`NodeFrameworkOptions.ReferenceIndex`）属装配面，随 W5 |

### 6.5 既有（非本轮引入）验证障碍，登记备查

| 套件 / 入口 | 现象 | 性质 |
|---|---|---|
| `Invoke-Regression.ps1 -Matrix default\|agent` | `Run-CSharpTests.ps1 -Test <targets>` 缺 `-Project` 触发护栏 ⇒ 矩阵不可用 | 预存（矩阵定义陈旧），本轮未改 |
| `LoopFoldMethod.Tests` | `ReferenceBoundary` 判其直接 `ProjectReference` 到 `Frame` ⇒ 构建拒 | 预存边界违规（该工程自身陈旧） |
| `UniversalReference.Tests` | 缺 `TestGroupAttribute` / `ILogger` 编译错 | 预存（该工程未跟随内核迁移） |
