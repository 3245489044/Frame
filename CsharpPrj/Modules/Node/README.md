# Modules.Node — 节点通用框架（与位置无关 · JSON 键连接版）

> **状态**：`P0 骨架` + `P1 提取融入`（D-1 案 A 口径）+ `P2–P5 框架件与合同测试` **已落地并通过验证**（构建 0 警告 0 错误、合同测试 95 例全绿）；`P6 应用` 中 **A-1（端口键空间/端口提取/节点单写门三件）、A-2 七件（形态/缺省/运行态/合并/校验/引用/指令）、A-3/A-3b（键格式与阈值声明化）、D-3（委托接线门禁）已落地**（Operation 侧接线，见[下一步计划](./docs/下一步计划.md) §1.1）；P6 剩余 A-4（Agent 边写点收敛）、P6-B 阶段 A–D 与 `P7 语言接入` 待开展。
> **设计权威**（本 README 只做导航，不重复定义）：
> - [`docs/节点通用框架-与位置无关的设计.md`](./docs/节点通用框架-与位置无关的设计.md) —— 总设计（模型 / 不变式 N1–N12 / 声明面 / 组件清单 / 阶段 S1–S5 / 合同测试 CT）
> - [`docs/节点通用框架-提取清单与复用映射.md`](./docs/节点通用框架-提取清单与复用映射.md) —— WorkFlowMethod 侧逐项裁决 + 14 项缺口 G1–G14 + 反模式 P12–P24 + 实现前必查的复用映射表
> - [`docs/节点通用框架-表层语言与Usharp接入.md`](./docs/节点通用框架-表层语言与Usharp接入.md) —— 端口清单 / 校验分工 / loc 位置反转 / 迁移 N0–N3
> - [`docs/节点通用框架-Agent应用篇.md`](./docs/节点通用框架-Agent应用篇.md) —— Agent 侧应用与分期迁移 A–D
> - [`docs/Agent应用篇-实施步骤.md`](./docs/Agent应用篇-实施步骤.md) —— **Agent 应用篇的可执行细化版**：P0（接线/声明/载体适配器）+ 阶段 A–D 逐步「文件/动作/验收/回退」+ 实测口径校正 + 执行顺序与门禁 + 已落地验收记录
> - [`docs/Agent应用篇-后续阶段细化方案.md`](./docs/Agent应用篇-后续阶段细化方案.md) —— **下一层细化**：框架改造难度评估（七条结构性原因 + 实测证据）、改动前取证清单、A2/A3/A4/A5 与 B/C/D 的**四段式实施卡**（前置取证/改动含行号/判据/回退）、判据命令矩阵、推荐顺序与待裁决
> - [`docs/P1-P5-实现记录.md`](./docs/P1-P5-实现记录.md) —— 本批**已落地事实**：逐件落点、提取融入口径（D-1 案 A）、测试清单、验证证据、剩余边界
> - [`docs/下一步计划.md`](./docs/下一步计划.md) —— **未做项清单**：P6（Operation 侧委托逐件清单 + Agent 阶段 A–D）/ P7（loc 反转 → 端口 → 适配 → 退役）/ 载体与治理项，含前置依赖、验收判据、建议顺序、待裁决未决项与风险回退

## 概述

**节点 = ⟨Key(身份), JSON(载荷), Placement\*(位置), Form(形态)⟩**

| 维度 | 含义 |
|------|------|
| `Key` | 全系统唯一的节点身份（同时是地址）；由 `IdentitySpec` 声明的段序列拼接，序数由 `ISequenceAuthority` 单源分配 |
| `JSON` | 载荷**恒为 JSON 文档**（不引入第二载体形态）；形态差异由 `Form` 声明 |
| `Placement` | 键在哪个容器/载体上物化（可多个）；**位置不改变身份**，是 `Key` 的函数 |
| `Form` | 该位置的载荷形态（`raw` / `view` / `digest`） |

**框架定位**：本模块是既有三套机制**之间的位置层**，复用而非替代（`Strategy.SingleImplementation`）：

| 既有机制 | 视角 | 本框架关系 |
|---|---|---|
| `Operation.LoopFoldMethod.Node` | 内存装配（不落库，`NodeItem.Key` 可空） | 作为 `Origin=Live` 层，产出装配结果，不负责位置 |
| `Operation.WorkFlowMethod.NodeProcess` | 执行与端口（JSON 定义 → 执行 → 端口物化 → 单写门 → 读视图） | 作为执行与物化通道；框架只声明"写到哪个位置" |
| `Operation.HierarchyMethod` + `DataStoreDriven` | 持久与图（库行 + 引用边） | 作为 `ICarrier` 实现，经适配器接入 |

> 核心主张：把节点降为「键 + JSON 文档」、把连接降为「键引用」之后，**不再有"节点 → 历史/任务"的转换逻辑**（组合推论 `N1 ∧ N2 ∧ N3 ∧ N12`）。

## 目录结构

```
Modules/Node/
├── docs/                            设计四篇 + 实现记录（权威；本 README 为其导航）
├── Modules.Node.csproj              工程 + ModuleRule（层=2，零 Frame 依赖）
├── README.md
└── 已落地实现分区（P1–P5）
    ├── Identity/        IdentitySpec / NodeIdentity / KeyTemplateEvaluator / ISequenceAuthority
    ├── Placement/       PlacementSpec / NodePlacement / PlacementRegistry / IPlacementResolver(+Null)
    ├── Carriers/        INodeCarrier / ICarrierRegistry(+Null)
    ├── Materialization/ NodeMaterializer(唯一写入口) / NodeWriteGate / NodeFormCodec / NodeCompensation / SpecDefaults
    ├── Ports/           PortKeySpace / NodePortBinding
    ├── Spec/            NodeFrameworkSpecLoader / NodeSpecValidator / NodeReferenceIndex / NodeDirectiveSet / NodeChainComposer
    ├── Projection/      NodeReadProjection
    ├── Governance/      NodeReconciler
    └── Diagnostics/     NodeFrameworkDiagnostics（实例级，禁进程静态）
```

> **退役记录（2026-09-25）**：`RunState/`（`NodeRunState` + `RunStateStage` 枚举）已**退役删除** ——
> 运行态受控词与转移表的唯一来源 = `Operation.HierarchyMethod\Hierarchy\Dataflow\RunState\RunStateStage`
> （位置层不引上层契约：本工程零 `Frame` 依赖，无法也不应持有第二套运行态模型）；
> 消费方 `WorkFlowMethod\…\Session\NodeExecutionState.cs` 已同步摘除 `ToRunState` / `StageOverrides`。
> 判据：`Check-AgentLayerBoundary.ps1 -Check`（INV-L3 定义点只降不升）。

## 契约边界（D-1 裁决 · 案 A）

**位置层不引上层契约类型**。实测：`NodeOutput` / `INodeOutputStore` 在 `Operation\WorkFlowMethod\Nodes\Contracts`（Operation 层），`Chain<TKey,TNode>` / `UniversalMiddlewareEntry` 在 `Share\`（Share 层）。若把它们写进本工程公共签名，即违反 K1/K6 与 P20（位置层被上层绑定 + 重量闭包污染）。

因此：

| 项 | 裁决 |
|---|---|
| 框架对外契约 | **以 JSON 字符串 + BCL 类型为界**（如 `INodeCarrier.UpsertAsync(key, json)`） |
| DTO 与序列化单源 | 由上层适配器持有并沿用既有 `NodeConfig` / `NodeOutput` / `Chain` / `UniversalMiddlewareEntry.FromJson`（禁第二套 DTO、禁第二 JSON 库） |
| 依赖面 | 本工程**零 `ProjectReference`**（仅 BCL）；确需通用件时按层序显式添加 |
| 门禁 | `Check-NodeFramework.ps1` 检查 D：源码零 `using Operation.*` / `using Share.*` |

## 端口（未装配即 Null 实现 + 可见化诊断，不静默兜底）

| 端口 | 语义 | 未装配行为 |
|---|---|---|
| `ISpecSource` | 声明来源（JSON 或 `.ushar` 只是它的两种装配） | 空声明集 + 诊断（不假装通过） |
| `IDefinitionDiagnostics` | 定义期诊断单向投影 | 丢弃 + 计数可见化 |
| `IOrientationAuthority` | 运行态来源（`Origin`）判定 | 无来源 ⇒ ∅ |
| `IReferenceIndexPort` | 引用字段清单 / 反向引用 | 无对账 |
| `IPlacementResolver` | `loc → Placement` 反解（**唯一来源=声明**） | ∅ + 诊断（禁回退默认载体） |
| `INodeCarrier` / `ICarrierRegistry` | 载体四原语（`Ensure/Upsert/Read/Delete/Enumerate`） | 该位置 `Unbound` + 诊断，不阻断其它位置 |
| `ISequenceAuthority` | 序数分配（键内序数单源） | 拒绝生成含序数的键 + 诊断 |

## 不变式与合同测试（权威见设计文档 §2 / §9.2）

- **N1** 身份唯一 · **N2** 位置不改变身份 · **N3** 单写者 · **N4** 幂等 · **N5** 只增不丢 · **N6** 顺序内嵌（顺序仅由键内序数表达） · **N7** 读侧裁剪 · **N8** 声明驱动 · **N9** 来源可辨 · **N10** 位置可退役 · **N11** JSON 形态单源 · **N12** 键连接不外溢。
- 合同测试编号：`CT-1…CT-10`（框架）· `CT-E1/CT-E2`（既有基线，见下）· `CT-U1…CT-U5`（语言接入）· `CT-L1…CT-L6`（loc 反转）。
- **既有基线（P1 等价门的物理落点）**：`Environment/CentralTest/CSharpTest/Operation.Tests/WorkFlowMethod.Tests/UsageTests/{NodeOutputStorageUsageTests, NodeParameterHydratorFirstWinTests, NodeResultExtractorSpecializedTests}.cs` —— P1 迁址后必须**零回归**。

## 落地阶段（提取优先）

| 阶段 | 内容 | 出口 |
|---|---|---|
| **P0 立项骨架** | 工程 / `modules.json` 登记 / 聚合接线 / 测试工程 / README / 门禁脚本 | ✅ 已落地；`Check-NodeFramework.ps1` EXIT 0 |
| **P1 提取融入**（优先） | 按提取分册 §4/§6：`原样复用` 登记 + 11 件薄适配**契约化收编**（D-1 案 A：契约中立，DTO/序列化留上层）+ 反模式 P12–P15 治理 | ✅ 框架侧已落地（逐件落点见[实现记录](./docs/P1-P5-实现记录.md) §2）；Operation 侧**委托接线**列入 P6 |
| **P2 新建** | S1 键（`IdentitySpec`…）+ S2 物化（`NodeMaterializer`/`INodeCarrier`/`NodeWriteGate`） | ✅ 已落地（CT-1/CT-2/CT-3/CT-6/CT-8 用例全绿） |
| **P3 声明面** | S3 `NodeFrameworkSpecLoader` + 装配校验（补 G7/G9/G13/G14/G10/G11） | ✅ 已落地（改声明零代码生效的装载/校验路径 + 用例） |
| **P4 读投影** | S4 `NodeReadProjection` + selection + Origin（枚举序=键序数序） | ✅ 已落地（CT-4/CT-5/CT-7 用例） |
| **P5 治理** | S5 `NodeReconciler` + 实例级诊断 + 门禁全绿 | ✅ 已落地（对账/漂移/多解用例 + 门禁 EXIT 0） |
| **P6 应用** | Operation 侧既有件改为委托（`NodeOutputReadView`/`NodeResultExtractor`/`NodePersistCoordinator`/`StrategyResolver`/`NodeConfigFactory`…）+ Agent 阶段 A–D（键与 JSON 单源 → 实时物化 → 回写合一 → 读侧退役） | **A-1/A-2/A-3/A-3b/D-3 已落地**（见[下一步计划](./docs/下一步计划.md) §1.1）；A-4 与 Agent 阶段见同文 §2/§3 |
| **P7 语言接入** | Usharp 侧 N0（loc 反转）→ N1 端口 → N2 `Usharp.Adapters.Node` → N3 退役 | 见[下一步计划](./docs/下一步计划.md) §3.5；CT-U1…CT-U5 + CT-L1…CT-L6 |

## 验收记录（P0–P5，实测）

| # | 动作 | 命令 | 结果 |
|---|---|---|---|
| P0.1 | 工程骨架 | `dotnet build Frame\CsharpPrj\Modules\Node\Modules.Node.csproj` | ✅ 成功（0 警告 0 错误）→ `Frame\CsharpPrj\BuildOutput\Debug\Modules.Node.dll` |
| P0.2 | 模块登记 | `PowerShellScript\Share\Solution\Generate-ModulePaths.ps1` | ✅ `Proj_Node` / `Mod_Node` 已生成；"清单与磁盘一致" |
| P0.3 | 聚合接线 | `dotnet build Frame\CsharpPrj\Modules\Modules.csproj` | ✅ 0 警告 0 错误 |
| P0.4 | 测试工程 | `Run-CSharpTests.ps1 -Test node` | ✅ 别名 `node` 生效（`TestRunner\TestTargets.psd1`） |
| P1–P5 | 框架件 + 合同测试 | `dotnet test ...\Modules.Tests\Node.Tests\Modules.Node.Tests.csproj` | ✅ **通过 95 / 失败 0**（含 CT-E1/CT-E2 锚点、CT-1…CT-10 核心用例；P6-A 值缝 V1 新增 6 例） |
| P0.5 | 门禁脚本 | `PowerShellScript\Modules\Node\Check-NodeFramework.ps1 -ShowDetail` | ✅ A/B/C/D 通过、告警 0（扫描 26 个源码文件） |
| — | 层序校验 | `PowerShellScript\Share\Solution\Test-ProjectReferences.ps1` | ✅ Node **零新增项**（本工程零 `ProjectReference`，无向上引用告警）；该脚本当前退出码 1 由既有 `FunctionExecutionFramework` 悬空引用与 3 条未属性化相对路径导致，与 Node 无关 |
| 整理批 | 重复/冗余/错误逻辑收敛（22 项） | `dotnet test ...\Node.Tests\Modules.Node.Tests.csproj` + `Check-NodeFramework.ps1` | ✅ **通过 100 / 失败 0**；`Operation.Tests` Node 相关 75 例无回归；门禁 A/B/C/D 通过、告警 0；逐项事实与未做项见[实现记录](./docs/P1-P5-实现记录.md) §6 |

> 门禁 A 的判定形态为「限定名/路径」（`Proj_Usharp*` / `Usharp\<...>` / `Usharp.<Token>`），
> 规则说明中散文式提及"禁 Usharp 类型"不判违规（避免自指误报）。

## 门禁与自检

```powershell
# 框架不变式（Usharp 命中 / AsyncLocal / loc 硬编码 / 上层 using）
Frame\PowerShellScript\Modules\Node\Check-NodeFramework.ps1

# 层序 / 悬空引用 / 未属性化 / 循环依赖（仓库统一）
Frame\PowerShellScript\Share\Solution\Test-ProjectReferences.ps1
```

## 边界（不做）

| 不做 | 原因 |
|---|---|
| ❌ 引 `Usharp*` 类型或桥接类型进本工程 | P20 / K1 / K6；桥接只在 `Usharp.Adapters.Node` |
| ❌ 引 `Operation.*` / `Share.*` 契约类型进公共签名 | D-1 案 A；DTO 与序列化由上层适配器持有 |
| ❌ 新造链/节点/产出 DTO 或第二 JSON 库 | N11（JSON 形态单源） |
| ❌ 自建第二套端口键空间 / 单写门 / 读视图 / 键格式全局 | N8 / P14 / P22；P1 为**迁址收编**，非重写 |
| ❌ 写侧为某消费者裁剪副本 | N7（裁剪只发生在读侧 `selection`） |
| ❌ 承接执行体/状态根/终止裁决 | 属引擎与领域层（见 9.22 方案 §7） |

## 测试

```
Environment/CentralTest/CSharpTest/Modules.Tests/Node.Tests/Modules.Node.Tests.csproj
```

MSTest；被测程序集经 `CentralTest/build/props/CsharpPrj.BinaryRefs.props` 的 CsharpPrj 二进制闭包注入，测试工程不逐处手写 leaf `ProjectReference`。
