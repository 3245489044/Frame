# DataCenter · Agent 侧运行时数据接入 —— 下一步实施计划（S4-2 → S5）

> **日期**：2026-09-12（S4-4a 完成后重编） | **权威边界**：本文件是「**下一步怎么做**」的唯一权威（步骤 / 改动点 / 判据 / 回退 / 提交粒度）；
> 设计语义以 `DataCenter-Agent运行时数据接入-设计与实施.md` 为唯一权威；**已完成阶段的实测记录**见 `DataCenter-Agent运行时数据接入-实施进展与后续.md`（执行台账）。
> **通则**（沿用设计文档 §4）：每步**独立可编译、可回退**；判据必须可执行（命令 / 断言）；不改真源（SQL/LiteDB）、不改 CSG 主 schema。

---

## 0. 当前基线（开工前核对，全部为实测值）

| 项 | 状态 |
|---|---|
| **已完成** | A0 / A1 / A2 / A3a-1 / **A3a-2（S1）** / **S4-1（A5-1）** / **S4-2（A5-2）** / **B1+B2（P0 前置）** / **S4-3（批次 4）** / **S4-4a（注入面 + K9 归零）** / **T3（投影器装配单一工厂）** / **S4-4b（批次 5b：换代生产闭环）** / **B26+B27（第四轮余项：F-61/F-62/F-63取证/F-64分工/F-65/F-66边界/F-67/F-70，2026-09-12 单提交）** / **W0+W1（第五轮：台账归零 + 静态槽归零 20→0，2026-09-12）** —— 明细见进展台账 §2、缺陷清单 §5、`Agent-DataOwnership.md` §8 |
| **未完成** | S3（A4 双写收敛，W2）· S1-④（对象槽可观测投影，W3-1）· S2（A3b，**阻塞**，W3-3）· S5（收尾，W6）· 残项：① 对象槽产物的新代数 ALC 重建（**前置 Batch 3 已归零 ⇒ 已解阻塞，W-DEC-5 单独立批**）② **静态槽退役**（W1 已将 15 文件 20 处静态槽归零；`AgentRuntimeRoot` 取根通道按 W-DEC-3① 保留，退役属独立工作项）③ API `node.write` 缺省形态与执行链单形的口径收敛（F-63 残余，W-DEC-4） |
| 门禁 | `Check-DataCenterGenericity.ps1 -Check` = **EXIT 0**（G1 命中 0 / G2 违规 0 / K9 白名单 **4** + 待迁移 **0** + 违规 0）；`Check-AgentRuntimeSlots.ps1 -Check` = **EXIT 0**（457 文件 / **违规 0** / **存量基线 0**〔W1 已归零并清空 `$baseline`〕）；`Check-AgentDecisionCoreBoundary.ps1 -Check` = **EXIT 0**（494 文件 / 违规 0）；`Check-SingleWriter.ps1` = **EXIT 0**（告警 8 不计失败）；`Check-AgentDataSeparation.ps1 -Check` = **EXIT 0**（D5 无主 0 / D1 K10 0 = 冻结 0 / D2·D3·D4 全 0 / 台账待迁移 2 未回涨） |
| 测试基线 | DataCenter.Tests **95/95** · Agent.Core.Tests **405/405** · Agent.Axioms.Tests **86/86** · Agent.Runtime.Tests **21/21** · Agent.Launcher.Tests **37/37** · Agent.Persistence.Tests **88/88** · Agent.Session.Tests **20/20** · Agent.Modules.Tests **20/20** · Agent.Operation.Tests **47/47** · Agent.Observe.Tests **11/11** · Agent.Guardrails.Tests **10/10** · Agent.Api.Tests **34/34** · Agent.Api.Contract.Tests **22/22** |
| 构建基线 | `Frame\CsharpPrj\MainProj.sln` · `Projects\Plug\Agent\Agent.sln` · `Projects\FileDriven\FileDriven.sln` 均 **0 错 0 警** |
| 关键纪律 | ① 改契约后必须刷新烘焙副本（设计文档 §4.2，命令见 §4）② Agent 侧不得 `ProjectReference` Frame（`Directory.Build.props:134`）③ 新工程命名空间不得在 `Agent.*` 内遮蔽全局命名空间（A3a-2 实测 `CS0234`）④ 域别名须在 `DataCenterHost.Boot` **之前**并入 `DataCenterHostOptions.Registry` |
| 既有缺陷（**非本线引入**） | `Environment\CentralTest\CSharpTest\Share.Tsets\Registry.Tests\AutoRegisterTests.cs` 引用 `Share.Registry.AutoRegister.*`，源码实为 `Modules.Registry.AutoRegister.*` → 该测试工程**编译失败**（Frame `02d9b20` 遗留）。**建议**：独立小提交把 `using Share.Registry.` → `Modules.Registry.`，随后纳入回归集（见 S5-5） |
| 待决策 | **D-A4**（A4 开关形态，建议已给） —— 其余已定：**D-批次序** ①（S4-2→S4-3→S4-4 已按此执行）· **D-S42** ①（`Func<RootSwapContext, IDataCenterRoot>`，S4-2 落地）· **D-A5** ①（换代不承诺零停机，S4-2 落地）· **D-S43** ①（引导开关默认关，S4-3 落地；**默认切开**归 S4-4b/1e） |

---

## 1. 剩余阶段依赖与建议批次

```
[S4-2] SwapAsync 接新 ALC ──► [S4-3] 宿主引导 Boot 根 + 注入域表 ──► [S4-4a] 注入面 + K9 归零 ✅ ──► [S4-4b] RootFactory 生产注入 + 触发 + 开关 ✅ ──┐
                                                                                                              ├──► [S5] 收尾
[S3] A4 双写收敛（独立开关；与上述无依赖，但风险最高）─────────────────────────────────────────────────────────┘
[S1-④] 对象槽可观测投影（纯新增，可任意穿插）
[S2] A3b 追踪面（⏸ 阻塞于框架侧 D2/D3：`IDataCenterRoot` 现无 `Trace` 成员 —— 已实测）
```

| 批次 | 内容 | 说明 |
|---|---|---|
| 批次 3 | **S4-2**（`DataCenterHost` 换代接口）**✅ 已完成** | 无外部依赖；`DataCenter.Hosting` **不得**引 `Modules.Registry`（平台层 vs 设施层）→ 经委托注入（`RootFactory` + `RootSwapContext`） |
| 批次 4 | **S4-3**（宿主引导：PrjBridge 侧 Boot 根 + 注入 `AgentStateDomain` 域表）**✅ 已完成** | 落点由 `FileDriven.Launcher` 修正为 `PrjBridge`（薄启动层零上游静态引用）；实现与判据见进展台账 §2 S4-3。端到端"带着运行数据换代"证据（热换 + ALC 回收）归 S4-4/S5 |
| 批次 5 | **S4-4a**（状态真源注入面 + K9 待迁移归零）**✅ 已完成** | 落地与设计口径的差异见 §3 S4-4；实测 K9 白名单 3 / 待迁移 **0** / 违规 0 |
| 批次 5b | **S4-4b**（`RootFactory` 生产注入 + `SwapAsync` 生产触发 + 引导开关默认切开）**✅ 已完成（2026-09-12）** | 与分离方案 **Batch 1c/1d/1e** 为同一工作项；落地与判据见 §3 S4-4b 行 + 进展台账 §2 |
| 批次 6 | **S3**（A4 双写收敛） | 风险最高：独立开关（默认关）+ 故障注入 + 单独评审 |
| 可穿插 | **S1-④**（对象槽只读投影） | 纯新增、可独立提交 |
| 待排期 | **S2**（A3b） | 前置 = 框架侧 D2/D3 |
| 收尾 | **S5** | 文档回写 + 门禁基线归零 + 既有缺陷修复 |

> **为何把 S4-2/S4-3 提到 S3（A4）之前**：S4-1 已交付"可收集 ALC + 代数工厂"，换代链路自此**无阻塞**；
> 而"整体数据流由 `Frame\DataCenter` 持有 + 带着运行数据热更新"是本方案的**目标本身**，越早端到端跑通，后续 S3 的收敛才有真实宿主可验证。
> 反方意见（保持原序）见 §2 D-批次序 —— **需确认后开工**。

> **缺陷清单（S4-3/S4-4 的前置修复，2026-09-12 审计）**：见 `DataCenter-Agent运行时数据接入-调用链逻辑缺陷清单.md`（F-01..F-26）。
> **开工前必做**：S4-3 前置 **B1/P0**（F-16/F-17：换代窗口取根/取注册表抛异常而非回落）+ **B2/P0**（F-18：换代无指纹时既有 fingerprint 被静默清空）—— ✅ **均已完成（2026-09-12）**，实测：`DataCenter.Tests` **56/56**、`Agent.Launcher.Tests` **28/28**、其余回归零变动（见缺陷清单 §5 B1/B2 行）；
> S4-4 前置 **B6/P1**（F-24：`AgentOrchestrator` 无 `StateCenter` 注入面 ⇒ 通道 A 实际未接通；须先补注入面再删兜底造根，否则 K9 归零会变成"没有状态真源"）—— ✅ **已完成（2026-09-12，S4-4a）**，实测 `Agent.Core.Tests` **399/399**（新增 3 项注入面断言）、K9 待迁移 **2 → 0**。

---

> **第四轮余项（B26 · B27）**：`F-61`~`F-67` / `F-70` 的"怎么做"已独立为**下位子计划** ——
> `DataCenter-Agent运行时数据接入-第四轮剩余收敛计划.md`（含 DEC-1..DEC-6 决策点、逐子项改动点/判据/回退、建议执行顺序）。
> **该子计划中未决的 DEC 不得视为已定**；缺陷事实仍以缺陷清单为准。已完成批次 **B22–B25**（`F-55`~`F-60`/`F-68`/`F-69`）的实测基线见子计划 §0.2。
> 基线变化：`Agent.Core.Tests` **404/404** · `Agent.Axioms.Tests` **86/86** · `Agent.Launcher.Tests` **34/34** · `DataCenter.Tests` **95/95** · 宿主 `PrjBridge` 构建 0 错 0 警 · K9 违规 0 / 待迁移 0。
>
> **第五轮收口（W0–W6）**：剩余阶段 **S3 / S1-④ / S2 / S5** 与分离方案 **Batch 2–7** 的执行口径已独立为下位子计划 ——
> `DataCenter-Agent运行时数据接入-第五轮收口实施计划.md`（含 `W-DEC-1..5` 决策点、逐子项改动点/判据/回退/风险、判据命令清单、建议执行顺序）。
> **开工纪律**：`W-DEC` 未标注"已采纳（日期）"前视为**未决**；其中 **W3-3（追踪面）依赖框架侧 D2/D3**（`IDataCenterRoot` 实测无 `Trace` 成员），不得以 Agent 侧变通绕行。
> **开工前实测（2026-09-12）**：K10 存量基线 **20 命中 / 15 文件仍命中**（`AgentUiHost.cs` 已随 F-65 收敛出）；台账 20 条 = registered 8 + migrated 1 + **pending 11**；D5 无主 **0 处**（**W0 已归零（2026-09-12）**：`round-state-kv` 条目 `pattern` 追加 `Agent.Contract/Runtime/AgentRuntimeRoot.cs`；冻结值同步 22/16 → 20/15）⇒ `Check-AgentDataSeparation.ps1 -Check` = **EXIT 0**。

## 2. 开工前决策点（需确认）

| # | 议题 | 选项 | 建议 | 影响面 |
|---|---|---|---|---|
| **D-批次序** | S3(A4) 与 S4-2..S4-4 的先后 | ① 先换代闭环（S4-2 → S4-3 → S4-4）② 先 A4 双写收敛（原序） | **①**：S4-1 已解除换代阻塞；换代闭环是本方案目标本身，且能给 S3 提供**真实宿主**验证面 | 批次顺序、评审安排 |
| **D-S42** | 新代数装配入口形态 | ① `DataCenterHostOptions` 增委托 ② Protocol 增端口 `IRootFactory` | **①（已采纳，S4-2 落地；签名修正为 `Func<RootSwapContext, IDataCenterRoot>`）**：Hosting 保持零设施依赖（G2）；文档原拟 `Func<GenerationPlan, IDataCenterRoot>` 拿不到 cell/registry/generation/slots，无法装配合法根，故引入 `RootSwapContext` 承载装配输入（含旧代际快照） | `DataCenter.Hosting` 公开面、宿主注入点 |
| **D-S43** | 宿主引导开关默认值 | ① 默认**关**（现行为：Agent 回落本地容器）② 默认开 | **①**：与 A1「根未绑定 → 回落进程本地容器」语义一致；先开关验证再切默认 | 宿主启动路径、回归面 |
| D-A4 | A4 收敛开关形态 | ① `AgentConfig` 布尔（默认关）② project.md 参数 ③ 环境变量 | **①**：与 `AgentConfig.EnableTaskPool`（`Agent.McpRegistryHost\Configuration\AgentConfig.cs:128`）同构 | S3 的失效注入与回退路径 |
| D-A5 | 换代期间是否承诺零请求失败 | ① 不承诺 ② Drain 超时后拒绝新写 | **①（已采纳；S4-2 落地：`Resume` 于 finally 放行写入，不承诺零停机）**（对齐架构设计 §8） | S4-2/S4-3 的 Drain/Resume 语义 |
| **D-第四轮余项** | 第四轮余项收敛方向（观测事实源 / 对象槽真源 / `ur_nodes` 读写形态 / 死实现处置 / 未落地槽键 / 词表关系） | 见子计划 §1 的 **DEC-1..DEC-6**（各含 2–3 选项） | **已采纳（2026-09-12）**：DEC-1②（先分工，①为目标态）· DEC-2①（根分区承载 + 回落）· DEC-3（**取证结案**：执行链已单形，残余在 API `node.write`，形态收敛另立编号）· DEC-4① · DEC-5① · DEC-6②；落地记录见子计划 §8 | 观测查询口径、UI 槽真源、`ur_nodes` 数据形态、公开面清理 |
| **D-第五轮** | 第五轮收口决策点（D5 无主处置 / 静态槽归零口径 / 观测源唯一化 / API `node.write` 缺省 / 对象槽新代数 ALC） | 见第五轮子计划 §2 的 **W-DEC-1..5**（各含 2–3 选项） | **已采纳（2026-09-12）：W-DEC-1① · W-DEC-3①**（W0 已落地）；W-DEC-2② / W-DEC-4 / W-DEC-5 待后续批次确认 | 台账冻结值、D5 聚合门禁 EXIT 码、K10 归零目标 |

---

## 3. 步骤明细

### S4-2 · `SwapAsync` 接入新 ALC（换代宿主侧核心）—— ✅ 已完成（2026-09-12，Frame `S4-2` 提交 + 主仓判据提交）

| 项 | 内容 |
|---|---|
| **交付** | ① `DataCenterHostOptions.RootFactory`：`Func<RootSwapContext, IDataCenterRoot>?`（宿主注入的新代数装配入口；null = 同 ALC 路径，等价回退）<br>② `RootSwapContext`（新）：`(RootCell, Registry, Generation, PreviousGeneration, Slots)` —— 装配入口的全部输入；旧根因单根不变量先释放，故只暴露其代际快照<br>③ `GenerationPlan` 增 `Export` / `Import` 钩子 + `GenerationPayload`（新；只承载**不在状态真源里**的跨代迁移数据，禁止塞状态 KV）<br>④ `SwapAsync` 新流程：Drain → Export → 解绑/释放旧代数根 → 装配新代数根（generation+1）→ 绑根 → Import → Resume（**finally 放行**）<br>⑤ 装配入口交出物校验（fail-fast）：非 null / 必须挂当前真源 / 代际必须为目标代数；校验不过先释放交出物再抛错 |
| **实施发现（3 条，已写进代码注释）** | ① **ALC 边界**：`RootInstanceGuard` 为 `DataCenter.Core` 内 static → 若在 scoped ALC 装载 Core 副本，单根守卫各持一份计数（DF8 静默失效）+ 跨 ALC 类型身份分裂。故"新 ALC"只覆盖**扩展装配产物**，根/真源/守卫必须由默认 ALC 承载<br>② **Seq 语义**：Core 无独立 `Seq` 字段；同一 `(namespace, scope)` 的版本单调由 `StateSubscriptionHub._lastSeen` 承担且归 `StateCenter` 私有 → 只要沿用同一 `_cell/_center`，Seq 天然连续。故 `Export/Import` **不承载状态 KV**（否则=第二真源）<br>③ **非法交出物须先释放**：交出物若挂异真源/代际不符而直接抛错，其已占用的单根守卫计数会泄漏 → 回建旧代数二次占用失败（实测踩到并修复） |
| **Resume 语义（新增口径）** | `Resume` 在 `finally` 中以 `CancellationToken.None` 调用：换代成功/失败/取消都必须放行写入，否则宿主写路径永久停在停写态（D-A5 ①） |
| **判据（实测）** | `DataCenter.slnx` **0 错 0 警**；`DataCenter.Tests` **53/53**（新增 4 项：① 装配入口被调用 + 上下文真源/槽/代际 + 状态连续 + 默认 ALC；② 边界顺序 Drain→Export→Import→Resume；③ 装配失败保留旧代数、放行写入、失败可重复（守卫配平）；④ 交出异真源 fail-fast 且保留旧代数）；`Integration.Tests` 30 通过 / 1 跳过；`StateHoldDriven.Tests` 73/73；`Agent.Core/Axioms/Runtime/Launcher` 396/84/21/26；K9/K10 **EXIT 0** |
| **回退** | 不注入 `RootFactory` 即完全等价 A0 路径（单提交可撤） |
| **风险** | 中；Drain/Resume 语义决定抖动面（R3：**不承诺零停机**） |

### S4-3 · 宿主引导注入根（首次 Boot + 注入域表）—— ✅ 已完成（2026-09-12）

> **落点修正（实测，2026-09-12）**：原计划落点 `FileDriven.Launcher` 与既有引用边界冲突 —— `FileDriven.Execution.csproj:71-72` 已明确「本层**删除**对 `Projects\Agent` 的直接引用」，且 FileDriven 为薄启动层（零上游静态引用），在该处 Boot 必然要同时引 `DataCenter.Hosting` + Agent 域表。
> 实际落点：**Boot 触发点 = `Projects\PrjBridge\Agent\AgentSelfHostPlugin`**（宿主侧组合根，经 `IDisposable` 释放）；**Boot 装配实现 = `Agent.DataCenter` 的 `AgentRuntimeRootBootstrap`**（域表与 Boot 同程序集 ⇒ 纪律④"Boot 前并入域别名"顺序天然成立）。
> 实现/判据明细见进展台账 §2 S4-3；本表以下内容保留为原设计口径（改动点 ①②③ 已落地，④⑤ 落点随之下移）。

| 项 | 内容 |
|---|---|
| **目标** | 产品宿主经 `DataCenterHost.Boot` **持有根**并把 `IRootProvider` 注入 Agent 装配，落实「整体数据流的根实例由 `Frame\DataCenter` 持有」 |
| **现状（实测）** | ① **无任何产品宿主 Boot 根**：全仓 `DataCenterHost` 仅出现在 `DataCenter.Tests`<br>② 宿主侧**零 DataCenter 引用**：`Projects\FileDriven\**` 与 `Projects\BuildFramework\**` 的 props/csproj 中 `DataCenter` 命中 **0**<br>③ `DataCenter.Hosting` **不在** CsharpPrj 烘焙输出（仅 `Abstractions/Adapters/Core/Protocol` 在）<br>④ Agent 侧注入点已就位：`AgentFrameworkAssemblyContext.RootProvider`（`AgentFrameworkAssembler.cs:86`，A2）+ E 段 `ctx.Registry`（A3a-2） |
| **改动点** | ① `Projects\FileDriven\FileDriven.Launcher\Bootstrap\LauncherBootstrap.cs`（`EnableExtensions` 之后）Boot 根：`Registry = AgentStateDomain.Default`（**Boot 前**并入，§0 纪律 ④）、`RootCellFactory`/`PartitionCellFactory` 按宿主存储适配<br>② 把 `host.Root` 注入 `AgentFrameworkAssemblyContext.RootProvider`（`AgentFrameworkHosting` → `AgentComposition.StartAsync` 调用链）<br>③ 扩展代数与数据代数对齐（`ExtensionGenerationFactory.Generation` ↔ `host.Generation`）<br>④ 宿主侧登记 `DataCenter.{Protocol,Core,Hosting,Adapters}` 到二进制引用/烘焙清单（**P1**：契约程序集须由宿主**默认 ALC** 加载，跨 ALC 类型身份唯一）<br>⑤ 引导**开关**（依 **D-S43**，默认关） |
| **判据** | ① 端到端：热换一轮后「状态连续 + `Seq` 连续 + 旧 ALC 回收（`WeakReference` 证）」② 开关**关** = 现行为（回落本地容器，既有测试全绿）③ `FileDriven.sln` 0 错 0 警 + `Check-BakeFramework.ps1` 通过 ④ K9 扫描面（含 `Projects\Plug\Agent`）无新增违规 |
| **回退** | 引导开关关即回旧路径；本步独立提交 |
| **提交粒度** | Projects（宿主引导）+ Frame（补烘焙/引用清单）+ 主仓（端到端判据） |
| **风险** | 中高（宿主启动面首次引入 Frame 平台库）；换代抖动见 R3 |

### S4-4a · K9 待迁移归零（移除产品侧兜底造根）—— ✅ 已完成（2026-09-12，批次 5）

| 项 | 内容 |
|---|---|
| **目标** | 产品侧不再有「兜底造根」，`StateRootSingleSource` / DF8 在产品侧成立 |
| **改动点（已落地）** | ① `AgentRuntimeSlotKeys` 注册 `state-center` 键（注册一行）<br>② `AgentRuntimeRoot.ResolveStateCenter()`（槽注册优先 → **显式回落**）+ `RegisterStateCenter()`（装配期注册，`Replace` 覆盖语义）<br>③ `AgentOrchestrator(IAgentLoopConfig, StateCenter?)` 末位注入 + 只读 `StateCenter` 属性；`AgentModuleRegistrar.SetupAgentAsync` 末位透传；`AgentFrameworkAssemblyContext.StateCenter` → C 段注入 / E 段登记槽分区<br>④ 删 `AgentOrchestrator.cs:93` 与 `WorkflowRunner.cs:89` 两处 `?? new StateCenter(...)`<br>⑤ 回落真源构造下沉 Frame 宿主装配层：`DataCenter.Hosting.FallbackStateCenter`（显式命名；单元格工厂由调用方提供 ⇒ 后端语义不变）<br>⑥ `$k9Whitelist` 删两行 `Pending=$true` |
| **与设计口径的差异（实测记录）** | 设计原拟「经 `IRootProvider.Root.Root`（`IDataCell`）取状态真源」—— 实测不可行：`IDataCenterRoot` 只暴露 `Root`（真源 cell），**不暴露 `StateCenter`**；在 Agent 侧 `new StateCenter(cell)` 既会绕开宿主 `StateCenter` 的订阅/生命周期，也会重新触发 K9。故改为「**槽注册（装配方注入真源）优先 + 显式回落**」，真源由宿主经 E 段登记（`host.State`）；`IDataCenterRoot` 是否增开 `State` 成员留待 S4-4b 评审（若开，则回落分支可退化为「根可用即必得真源」） |
| **前置** | 用点清单（实测）：`AgentOrchestrator._stateCenter` 唯一消费点 `:117 new AgentRunnerCore(...)` → `AgentRunnerFactory` → `AgentRunner.cs:133 new WorkflowRunner(center: core.StateCenter, …)`；`WorkflowRunner._center` 唯一消费点 `:200 new StateContextAdapter(...)` |
| **判据（实测）** | ① K9 白名单 3 /「待迁移」**0** / 违规 0 → **EXIT 0** ② `Agent.Core.Tests` 396 → **399/399**（新增 3 项：注入真源原样采用 / 未注入同实例幂等 / 注册优先于回落）③ `Check-AgentDataSeparation.ps1 -Check` EXIT 0（D5 疑似持有 24 → 22）④ 根未注入路径仍可用（回落策略显式化，不静默造根） |
| **回退** | 每处独立提交；回落真源在 Frame 侧独立提交；白名单删行与代码分离提交 |
| **风险** | 中（改动面在 Agent.Core 组合点与节点执行入口）—— 已实测零回归 |

### S4-4b · 换代生产闭环（`RootFactory` 注入 + 触发 + 开关）**✅ 已完成（2026-09-12）**

| 项 | 内容 |
|---|---|
| **权威** | 步骤/判据/回退以 `Projects\Plug\Agent\doc\Agent数据与代码分离-架构整理与通用化方案.md` §7 **Batch 1c/1d/1e** 为准（本文件不重复定义） |
| **前置（已交付）** | **T3**：`DataCenterHost` 抽 `CreateProjector` 单一工厂 + 公开 `CreateCoreRoot(IStateGeneration)` —— 宿主 `RootFactory` 委派 `bootstrapped.CreateCoreRoot(ctx.Generation)`，复用同一装配口径（关 F-02）；实测 `DataCenter.Tests` **59/59** |
| **落地（实测）** | ① **1c**：`AgentRuntimeRootBootstrap` Boot 注入 `RootFactory`（缺省委派 `CreateCoreRoot`；可覆写以在新扩展代数 ALC 内重建对象槽产物）+ 引导后把宿主真源登记进 `agent` 分区 `state-center` 键（通道 A 生产挂根）② **1d**：新增 `AgentRuntimeRootBootstrap.SwapAsync(GenerationPlan?, ct)`（扩展代数 `Swap` → 数据代数换根 → 旧代数 `Unload`；`SemaphoreSlim` 串行化）+ 薄重载 `SwapGenerationAsync(ct)`；宿主触发点 `PrjBridge.Agent.AgentSelfHostPlugin.SwapGenerationAsync(ct)`（消 F-12）③ **1e**：开关缺省**开**（仅 `AGENT_DATACENTER_ROOT=0\|false` 关） |
| **判据（实测）** | ① `DataCenter.Tests` **59 → 65/65**（+6：开关默认切开语义 / 宿主真源登记进根分区 + Agent 侧读路径命中 / 换代双代数同序推进 + 真源复用（同 scope 值连续、`ReferenceEquals` 真源）/ 注入装配入口被采用 + `RootSwapContext` 校验 / 旧扩展代数卸载（ALC 卸载态可观测）/ 未引导换代 fail-fast）② 回归零变动（73 / 399 / 84 / 21 / 30 / 30+1）③ 开关**关** = 旧行为（回落本地容器）仍绿 ④ 五处构建 0 错 0 警 + 四门禁 EXIT 0 |
| **实测修正（诚实边界）** | 「换代后槽对象类型来自新代数 ALC」需 **Agent 框架在新代数重装配**（依赖 Batch 3 静态槽归零），本批只交付装配入口（`RootFactory` 覆写点）+ 数据跨代连续 + 旧 ALC 卸载；「旧 ALC `WeakReference.IsAlive == false`」在进程内**不可达**（实测引用根不在本批代码内，工厂复位 + 宿主停机后仍存活）→ 端到端判据取「卸载态可观测」（`Unload` 后禁止再装载），机制面回收判据仍由 `Integration.Tests` 覆盖 |
| **回退** | 1e 置 `AGENT_DATACENTER_ROOT=0`（或 `Enabled=false`）即回旧路径；1c/1d 单文件可撤（`RootFactory` 留空 = A0 路径） |
| **风险** | 中（触及宿主启动默认路径 + 换代触发面；不触碰真源后端与 CSG schema） |

### S3 · A4 双写收敛（风险最高，独立开关）

| 项 | 内容 |
|---|---|
| **目标** | 消除两类双写/漂移对，使权威单一（设计文档 §1.4） |
| **收敛点 1（实测）** | `vars`(Transient) ↔ `ur_nodes`：`Agent.Core\Nodes\NodeExecutionService.cs`（`:254` `vars.Set(key, v)` 与 `:325` `_store.WriteOutputAsync(...)` = 双写）→ 写侧收敛为「**单写 + 投影重算**」（读侧 ur_nodes 优先已具备；`:349` 注释已声明两者互不覆盖） |
| **收敛点 2（实测）** | `AgentTaskItem.PayloadJson` ↔ `state.Data`：`Agent.Core\Nodes\RoundStateMapper.cs` `Harvest`（`:117`；`:124` `state.Data[key] = v!`）二次收割 vars 进跨轮状态 → 以**统一任务快照为唯一权威根**（唯一写入口 `AgentRunner.Persistence.cs:114`），`state.Data` 降为缓存/投影（`:132` 注释已声明 ur_nodes 权威，需把 `ExecResult` 之外的键一并归类） |
| **S3-1 开关** | `AgentConfig` 布尔（默认**关**），注入链同 `EnableTaskPool`（`Agent.McpRegistryHost\Configuration\AgentConfig.cs:128`） |
| **S3-2 / S3-3** | 按开关分别落两个收敛点（`NodeExecutionService` / `RoundStateMapper`），**每处独立提交** |
| **S3-4 证据** | 故障注入（写后崩溃）+ 两后端（Sqlite / 内存占位）分别验证 |
| **判据** | ① 开关**关** = 旧路径行为等价（全量测试绿）② 开关**开** = 无漂移；故障注入后重启状态与落盘一致（`state.Data` 与 `PayloadJson` 不冲突）③ `NodeGraphReconciler` 跨介质缺口 0 ④ 7 个测试项目全绿 |
| **落盘迁移** | **不做**（走域别名表，A3a-2 已落地；设计文档 §3.3 ② 改名方案仅作备选） |
| **回退** | 开关关闭即回旧路径；本阶段**独立提交、不与其它阶段混** |
| **风险** | **高**：触及写热路径 → 单独评审 + 独立开关 + 故障注入证据 |

### S1-④ · 对象槽可观测投影（A3a-2 未落地项，纯新增）

| 项 | 内容 |
|---|---|
| **目标** | 对象槽（B/C 类）进入图的可观测面：LLM 路由模式 / 后端清单、技能热集计数、工作流编译缓存键 |
| **改动点** | ① `Agent.DataCenter` 工程（命名空间 `Agent.StateDomain`）增 `IRuntimeSlotEntrySource : IStateEntrySource`：读 `root.Slots.Partition(AgentRuntimeSlotKeys.Domain)` **只读快照** → `StateEntry`（nodeKind `slot.snapshot` 已在 `AgentStateDomain.NodeKinds` 预登记）<br>② 快照提供者按槽类型各出一行（LLM / 技能 / 工作流），**只含快照值，不含对象引用** |
| **判据** | ① 投影幂等（两次 `ToJson` 全等；**时间戳不进产物**）② K5（未注册 Kind = 0）③ 投影失败不升级为状态写失败（只读旁路，撤出不影响写入） |
| **回退** | 纯新增；不注册即无投影 |
| **风险** | 低 |

### S2 · A3b 追踪面（⏸ 阻塞，含等待期可做项）

| 项 | 内容 |
|---|---|
| **前置（阻塞已实测）** | 框架侧 **D2**（`TraceStamp` 值类型 / `GraphNode`·`GraphEdge` / schema 扩 `seqRange/writers/assertion` / `StateCenter.Write` 统一盖 `writer/at/seq`）与 **D3**（`ITraceQuery`）—— 见 `DataCenter-可追踪数据流图框架-设计与实施.md` §7 D2/D3。**现状**：`IDataCenterRoot` **无** `Trace` 成员、Protocol **无** `TraceStamp`/`ITraceQuery` |
| **改动点** | ① Protocol：`IDataCenterRoot` 增 `ITraceQuery Trace`（D3 落地后）② 盖章接入：`Agent.Core\Nodes\NodeExecutionService.cs`、`Agent.Core\Nodes\RoundStateMapper.cs`、`Agent.Core\Loops\AgentRunner.cs`（`writer` 由域显式传入，Core 不认领域语义）③ 域侧查询接入（`Ancestors` / `ReverseRefs` / `Validate`） |
| **等待期可做（无依赖）** | Agent 侧 **writer 身份常量表**（`AgentRunner` / `NodeExecutionService` / `RoundStateMapper`），纯新增、可独立提交 |
| **判据** | K7（追踪四要素 `writer/at/seq/causedBy`，`causedBy` 允许 null 但须显式声明为根）缺失 0；`Ancestors(终答节点)` 可闭合 |
| **回退** | 追踪为只读旁路，撤出不影响写入与执行 |

### S5 · 收尾

| # | 动作 | 落点 |
|---|---|---|
| S5-1 | 设计文档回写：L4 命名（D-A3 已回写）、§5 门禁表补 K9/K10 实测、§8 槽登记表勾状态、`database` 槽补齐（已回写） | 设计文档 |
| S5-2 | K10 存量基线（22 项）逐项判定「保留（注册表/机制类）」或「迁槽」，归零后清空 `$baseline` | `Check-AgentRuntimeSlots.ps1` |
| S5-3 | ~~K9 白名单移除 A5 待迁移两行~~ **✅ 已完成（S4-4a，2026-09-12）**：白名单 3（Hosting 路径）/ 待迁移 0；台账状态列已更新 | `Check-DataCenterGenericity.ps1`、进展台账 |
| S5-4 | 四条接入通道（A 状态 KV / B 对象槽 / C 投影 / D 域真源）验收小结 | 进展台账 §2 |
| S5-5 | 既有缺陷修复（独立提交）：`Share.Tsets\Registry.Tests\AutoRegisterTests.cs` 的 `Share.Registry.` → `Modules.Registry.`，随后纳入回归集 | 主仓 |

---

## 4. 判据命令清单（复制即用）

```powershell
# ── 构建 ──
dotnet build Frame\CsharpPrj\MainProj.sln -c Debug                                        # Frame 平台层
dotnet build Frame\DataCenter\DataCenter.slnx -c Debug                                    # 契约 / 单根宿主
dotnet build Projects\Plug\Agent\Agent.sln -c Debug                                       # Agent 侧
dotnet build Projects\FileDriven\FileDriven.sln -c Debug                                  # 宿主侧（S4-3 起）
dotnet build Frame\CsharpPrj\Operation\StateHoldDriven\StateHoldDriven.csproj -c Debug    # ★ 契约变更后先刷新烘焙（设计文档 §4.2）

# ── 回归（基线：59 / 73 / 399 / 84 / 21 / 30 / 30+1跳过）──
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Tests\DataCenter.Tests.csproj
dotnet test Environment\CentralTest\CSharpTest\Integration.Tests\Integration.Tests.csproj    # S4-1 起纳入（代数回收 / 换代语义）
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\StateHoldDriven.Tests\StateHoldDriven.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Core.Tests\Agent.Core.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Axioms.Tests\Agent.Axioms.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Runtime.Tests\Agent.Runtime.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Launcher.Tests\Agent.Launcher.Tests.csproj

# ── 门禁 ──
pwsh Frame\PowerShellScript\Operation\DSL\Check-DataCenterGenericity.ps1 -Check   # G1/G2/K9 → EXIT 0（S4-4a 后「待迁移」= 0）
pwsh Frame\PowerShellScript\Operation\DSL\Check-AgentRuntimeSlots.ps1 -Check      # K10      → EXIT 0
pwsh Frame\PowerShellScript\Operation\DSL\Check-SingleWriter.ps1                  # 单写者   → EXIT 0（告警 12 不计失败）
pwsh Frame\PowerShellScript\Operation\DSL\Check-AgentDataSeparation.ps1 -Check    # D1–D5 聚合 → EXIT 0（Batch 0 交付）
```

> 实测提示：本仓 shell 集成偶发吞输出 → 长命令统一用 `> $env:TEMP\x.log 2>&1; Get-Content $env:TEMP\x.log -Tail N` 取结果；
> `FunctionStorage.Tests` 是 WinExe 演示程序（无 Test SDK），**不属** `dotnet test` 判据集。

---

## 5. 提交规范（每步三处）

| 仓库 | 内容 | 消息模板 |
|---|---|---|
| `Frame`（子模块） | 契约 / Core / Hosting / CsharpPrj（S4-1 的 ALC 工厂）/ 门禁脚本 / 文档 | `S<n> <主题>：<改动摘要>` + 判据行 |
| `Projects`（子模块） | Agent 侧 / 宿主引导（FileDriven·PrjBridge） | 同上 |
| 主仓 `Main` | CentralTest 测试 / 子模块指针 | `S<n> 判据：<测试>；子模块指针同步` |

> 顺序：先 Frame（契约）→ 再 Projects（消费方 / 宿主）→ 最后主仓（测试 + `git add Frame Projects`）；
> **契约变更后必须刷新烘焙副本**（§4），否则 CI/本地测试会因旧契约假失败。

---

## 6. 风险与回退一览

| # | 风险 | 缓解 / 回退 |
|---|---|---|
| R-1 | 契约变更后测试加载旧契约（`TypeLoadException`） | 强制刷新烘焙副本（§4 的 StateHoldDriven 命令） |
| R-2 | 换代抖动（LLM 网关 / 后端连接随代数重建，设计文档 R3） | Drain/Resume 钩子 + 重连超时；**不承诺零停机**（架构设计 §8） |
| R-3 | 对象槽跨代数复用 → 跨 ALC 类型失效（文档 D5） | 换代 = **重建 + 状态迁移**；不跨代复用句柄 / 委托 / daemon |
| R-4 | **可收集 ≠ 必然回收**（默认 ALC 侧长期持有扩展类型） | 回收证须在 S4-3 端到端断言；不达标则退为「不回收但可换根」并显式登记 |
| R-5 | S3 双写收敛触及写热路径（设计文档 R5） | 独立开关（默认关）+ 故障注入 + 单独评审；关即回旧路径 |
| R-6 | 单根守卫为进程级，测试并行互相干扰（设计文档 R4） | 相关测试类 `[DoNotParallelize]`；`ResetForTesting()` 配平 |
| R-7 | 宿主侧首次引入 Frame 平台库 → 烘焙 / 引用边界漂移 | S4-3 与 `Check-BakeFramework.ps1` 联动；引导开关默认关 |
| R-8 | 既有缺陷（`Registry.Tests` 编译失败）掩盖回归信号 | S5-5 独立修复；修复前该工程回归**不纳入判据**（已显式声明） |

