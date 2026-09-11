# DataCenter · Agent 侧运行时数据接入 —— 下一步实施计划（A3a-2 → A5）

> **日期**：2026-09-12 | **权威边界**：本文件是「**下一步怎么做**」的唯一权威（步骤 / 改动点 / 判据 / 回退 / 提交粒度）；
> 设计语义以 `DataCenter-Agent运行时数据接入-设计与实施.md` 为唯一权威；**已完成阶段的实测记录**见 `DataCenter-Agent运行时数据接入-实施进展与后续.md`。
> **通则**（沿用设计文档 §4）：每步**独立可编译、可回退**；判据必须可执行（命令 / 断言）；不改真源（SQL/LiteDB）、不改 CSG 主 schema。

---

## 0. 当前基线（开工前核对）

| 项 | 状态 |
|---|---|
| A0 / A1 / A2 / A3a-1 | ✅ 已完成并提交（明细见进展台账） |
| 门禁 | `Check-DataCenterGenericity.ps1 -Check` = **EXIT 0**（G1/G2/K9；K9 待迁移 2 项 = A5 目标）；`Check-AgentRuntimeSlots.ps1 -Check` = **EXIT 0**（0 新增；22 项存量基线） |
| 测试基线 | DataCenter.Tests **44/44**、StateHoldDriven.Tests **73/73**、Agent.Core.Tests **396/396**、Agent.Axioms.Tests **84/84**、Agent.Runtime.Tests **21/21**、Agent.Launcher.Tests **26/26** |
| 关键纪律 | 改契约后必须刷新烘焙副本（设计文档 §4.2），否则测试运行期加载旧契约（`TypeLoadException`） |

---

## 1. 阶段依赖与建议批次

```
[S1] A3a-2 域扩展工程 ──┬──► [S3] A4 双写收敛（独立开关，最后做）
                        │
[S2] A3b 追踪面 ◄────────┘   （S2 前置 = D2/D3 契约入 Protocol，待框架侧排期）
[S4] A5 换代闭环（宿主侧；与 S1 落点/根注入强相关，可与 S3 并行）
[S5] 收尾（文档回写 + 门禁基线清零）
```

| 批次 | 内容 | 说明 |
|---|---|---|
| 批次 1 | **S1** | 无外部依赖，落点确认后可立即开工 |
| 批次 2 | **S4-1**（ALC 可收集化） | 独立可提交、可回退；不触碰根交换 |
| 批次 3 | **S3**（A4） | 风险最高：必须独立开关 + 故障注入 + 单独评审 |
| 批次 4 | **S4-2..S4-4**（换代闭环 + K9 归零） | 依赖批次 2 与宿主引导改动 |
| 待排期 | **S2**（A3b） | 前置：框架侧 D2（`TraceStamp`/`GraphNode`/`GraphEdge`）/ D3（`ITraceQuery`）落地 |

---

## 2. 开工前决策点（需确认）

| # | 议题 | 选项 | 建议 | 影响面 |
|---|---|---|---|---|
| **D-A3** | 域扩展工程落点 | ① Agent 侧（`Projects\Plug\Agent\src`）② Frame 侧（严格按设计文档 §2.1） | **①**：域键权威 `AgentStateKeys` 在 `Projects`；②会造成 Frame→Projects 逆向引用（层级倒置），并击穿现有门禁口径 | 新工程位置、`Agent.sln` 注册、文档 §2.1 回写 |
| **D-A4** | A4 收敛开关形态 | ① `AgentConfig` 开关（默认关）② `project.md` 参数 ③ 环境变量 | **①**：与既有 `EnableTaskPool` 等同构，配置注入链已具备 | 失效注入测试与回退路径 |
| **D-A5** | 换代期间是否承诺零请求失败 | ① 不承诺（对齐架构设计 §8）② Drain 超时后拒绝新写 | **①** | `SwapAsync` 的 Drain/Resume 超时语义 |

---

## 3. 步骤明细

### S1 · A3a-2 域扩展工程（`AgentState` 域 Keys + 域投影）

| 项 | 内容 |
|---|---|
| **目标** | 把 `Agent.{Scheduler,Session,Round}` 归并为同一逻辑域（使跨层因果链闭合），并提供域 Kind 注册与对象槽只读投影 |
| **改动点** | ① 新增扩展工程（依 D-A3 落点；方案①→ `Projects\Plug\Agent\src\Agent.DataCenter\Agent.DataCenter.csproj`），注册进 `Projects\Plug\Agent\Agent.sln`<br>② 域别名 + Kind 注册（建议文件 `AgentStateDomain.cs`）：`WithDomainAliases(("Agent.Scheduler","Agent"),("Agent.Session","Agent"),("Agent.Round","Agent"))`、`WithNodeKinds("decision.record", ...)`<br>③ 装配期接入：`AgentFrameworkAssemblyContext` 增 `IStateRegistry? Registry` 并在 **E 段**（`AgentComposition.RunSegmentERootSlotsAsync`）完成注册<br>④ 域投影适配（复用 Core `TierStateProjector`，不新造投影器）；对象槽可观测投影（只读快照：LLM 路由模式/后端清单、技能热集计数、工作流编译缓存键） |
| **实现要点** | 机制已在 A3a-1 落地（`IStateRegistry.DomainAliases` / `GraphRegistry.WithDomainAliases` / `StateUriMapper.Resolve(entry, registry)`）；本步**只登记、不新增机制**（Core 仍零领域词汇 → G1/K1 保持绿） |
| **判据①** | `dotnet build Projects\Plug\Agent\Agent.sln -c Debug` → 0 错 0 警 |
| **判据②** | 新增单测（复用 `DataCenter.Tests` 或新 `Agent.DataCenter.Tests`）：三个命名空间投影后 `Metadata["domain"] == "Agent"`；值节点 `domain` 全等；投影幂等（两次 `ToJson` 全等） |
| **判据③** | K3（CSG 校验通过）/ K4（悬空边 0）/ K5（未注册 Kind 数 0） |
| **回退** | 纯新增（工程 + 注册行）；撤出即回退 |
| **提交粒度** | Projects 一处提交（+ 主仓测试/子模块指针） |
| **风险** | 低（机制已就绪）；唯一外部依赖 = D-A3 决策 |

### S2 · A3b 追踪面（`TraceStamp` 盖章 + `ITraceQuery`）

| 项 | 内容 |
|---|---|
| **前置** | 框架侧 **D2**（`TraceStamp` 值类型、`GraphNode`/`GraphEdge`、schema 扩展 `seqRange/writers/assertion`、`StateCenter.Write` 统一盖 `writer/at/seq`）与 **D3**（`ITraceQuery` 查询/索引/导出）—— 见 `DataCenter-可追踪数据流图框架-设计与实施.md` §7 D2/D3、§4.1 |
| **目标** | 让「接入」可追踪：回答"这一步为什么是这样"（`Ancestors(终答节点)` 闭合） |
| **改动点** | ① Protocol：`IDataCenterRoot` 增 `ITraceQuery Trace`（框架侧 D3 落地后）<br>② 盖章接入（域显式传 `writer`）：`Agent.Core\Nodes\NodeExecutionService.cs`、`Agent.Core\Nodes\RoundStateMapper.cs`、`Agent.Core\Loops\AgentRunner.cs`（运行轮边界）<br>③ 域侧查询面接入（`Ancestors`/`ReverseRefs`/`Validate`） |
| **判据** | K7（追踪四要素：`writer/at/seq/causedBy`，`causedBy` 允许 null 但须显式声明为根）缺失 0；`Ancestors(终答节点)` 可闭合 |
| **回退** | 追踪为只读旁路，撤出不影响写入与执行 |
| **提交粒度** | Frame（契约/盖章）+ Projects（域接入）分开提交 |
| **风险** | 中；**当前阻塞**：Protocol 尚无追踪面契约（`IDataCenterRoot` 注释已声明随 D3 加入） |

### S3 · A4 双写收敛（风险最高，独立开关）

| 项 | 内容 |
|---|---|
| **目标** | 消除两类双写/漂移对，使权威单一 |
| **收敛点 1** | `vars`(Transient) ↔ `ur_nodes`：`Projects\Plug\Agent\src\Agent.Core\Nodes\NodeExecutionService.cs`（现「先内存 `vars.Set`，后落库 `WriteOutputAsync`」）→ 写侧收敛为「**单写 + 投影重算**」（读侧 ur_nodes 优先已具备） |
| **收敛点 2** | `AgentTaskItem.PayloadJson` ↔ `state.Data`：`Agent.Core\TaskPool\AgentTaskItem.cs`（唯一权威根）+ `Agent.Core\Nodes\RoundStateMapper.cs`（`Harvest` 二次收割 vars 进跨轮状态）→ `state.Data` 降为缓存/投影 |
| **开关** | 依 D-A4；建议 `AgentConfig` 布尔开关（默认**关**），Sqlite / 内存占位两种后端分别验证 |
| **判据①** | 按后端可开关：开关关闭 = 旧路径行为等价（全量测试绿）；开关开启 = 无漂移 |
| **判据②** | 故障注入（写后崩溃）：重启后状态与落盘一致（`state.Data` 与 `PayloadJson` 不冲突） |
| **判据③** | `NodeGraphReconciler` 跨介质缺口 0；Agent 全量测试绿（6 个测试项目） |
| **判据④** | 落盘键迁移仅当选择"改名方案"（设计文档 §3.3 ②）时执行；**默认走域别名表（已具备），不做落盘迁移** |
| **回退** | 开关关闭即回旧路径；本阶段**独立提交，不与其它阶段混** |
| **提交粒度** | Projects 独立提交（+ 主仓失效注入测试） |
| **风险** | **高**：触及写热路径，须单独评审 + 独立开关 + 故障注入证据 |

### S4 · A5 换代闭环（热更新带数据）

| # | 子步 | 改动点 | 判据 | 回退 |
|---|---|---|---|---|
| S4-1 | ALC 可收集化 + 实例化代数工厂 | `Frame\CsharpPrj\Modules\Registry\ExtensionAssemblyHost.cs:218`（`isCollectible: false → true`）；消除 `static ExtensionAssemblyHost`（G7.2） | 旧代数 `WeakReference.IsAlive == false`（新增单测）；既有插件加载测试零回归 | 独立提交；`isCollectible` 一行可回滚 |
| S4-2 | `SwapAsync` 接入新 ALC | `Frame\DataCenter\DataCenter.Hosting\Hosting\DataCenterHost.cs`（现为同 ALC 代际推进）：Drain → Export（agent 分区）→ 新 ALC 装配新根 → Import（`generation+1`/`fingerprint`/Seq 连续）→ Resume → 旧 ALC `Unload` | 换代前后同 scope 值/Seq 连续；换代失败**保留旧代数**（不半迁） | 换代流程独立；与 S4-1 分步提交 |
| S4-3 | 宿主引导注入根 | 宿主（`Projects\FileDriven\FileDriven.Launcher\Bootstrap\LauncherBootstrap.cs` / `Projects\PrjBridge`）经 `DataCenterHost.Boot` 持有根，并注入 `AgentFrameworkAssemblyContext.RootProvider` | 端到端：热换 + 数据连续 + ALC 回收证 | 引导开关；不注入时回落本地容器（A1 已有） |
| S4-4 | K9 待迁移归零 | 移除 `Projects\Plug\Agent\src\Agent.Core\Composition\AgentOrchestrator.cs:93`、`Agent.Core\Nodes\WorkflowRunner.cs:89` 的兜底造根（改经 `IRootProvider`） | `Check-DataCenterGenericity.ps1 -Check` 中 K9「待迁移」计数 = 0；白名单表删两行 | 每处独立提交 |

> **对象槽策略（文档 D5）**：换代时对象槽 = **重建 + 状态迁移**（LLM 网关 / daemon / 句柄不跨代数复用）；状态经 Export/Import 迁移以保连续。

### S5 · 收尾（文档与门禁基线）

| # | 动作 | 落点 |
|---|---|---|
| S5-1 | 回写设计文档：L4 扩展命名（依 D-A3 结论）、§5 门禁表补 K9/K10 实测状态、§8 槽登记表勾选状态列 | `DataCenter-Agent运行时数据接入-设计与实施.md` |
| S5-2 | K10 存量基线（22 项）纳入 A2+ 收敛计划：逐项判定「保留（注册表/机制类）」或「迁槽」，归零后清空 `$baseline` | `Frame\PowerShellScript\Operation\DSL\Check-AgentRuntimeSlots.ps1` |
| S5-3 | K9 白名单移除 A5 待迁移两行；实施进展台账状态列更新为 ✅ | `Check-DataCenterGenericity.ps1`、进展台账 |
| S5-4 | 四条接入通道验收小结（A/B/C/D 各自判据实测截图或命令输出） | 进展台账 §2 追加 |

---

## 4. 判据命令清单（复制即用）

```powershell
# ── 构建 ──
dotnet build Frame\DataCenter\DataCenter.slnx -c Debug                                    # 契约/宿主
dotnet build Projects\Plug\Agent\Agent.sln -c Debug                                       # Agent 侧
dotnet build Frame\CsharpPrj\Operation\StateHoldDriven\StateHoldDriven.csproj -c Debug    # 契约变更后刷新烘焙（必须先做，见 §4.2）

# ── 回归（基线：44 / 73 / 396 / 84 / 21 / 26）──
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Tests\DataCenter.Tests.csproj
dotnet test Environment\CentralTest\CSharpTest\Operation.Tests\StateHoldDriven.Tests\StateHoldDriven.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Core.Tests\Agent.Core.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Axioms.Tests\Agent.Axioms.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Runtime.Tests\Agent.Runtime.Tests.csproj
dotnet test Environment\CentralTest\AgentTest\Agent.Launcher.Tests\Agent.Launcher.Tests.csproj

# ── 门禁 ──
pwsh Frame\PowerShellScript\Operation\DSL\Check-DataCenterGenericity.ps1 -Check   # G1/G2/K9 → 期望 EXIT 0
pwsh Frame\PowerShellScript\Operation\DSL\Check-AgentRuntimeSlots.ps1 -Check      # K10      → 期望 EXIT 0
```

---

## 5. 提交规范（每步三处提交）

| 仓库 | 内容 | 消息模板 |
|---|---|---|
| `Frame`（子模块） | 契约 / Core / Hosting / 门禁脚本 / 文档 | `A<n> <主题>：<改动摘要>` + 判据行 |
| `Projects`（子模块） | Agent 侧改动 | 同上 |
| 主仓 `Main` | CentralTest 测试 / 子模块指针 | `A<n> 判据：<测试>；子模块指针同步` |

> 提交顺序：先 Frame（契约）→ 再 Projects（消费方）→ 最后主仓（测试 + `git add Frame Projects` 同步指针）；**契约变更后必须刷新烘焙副本**（§4.2），否则 CI/本地测试会因旧契约假失败。

---

## 6. 风险与回退一览

| # | 风险 | 缓解 / 回退 |
|---|---|---|
| R-1 | 契约变更后测试加载旧契约（`TypeLoadException`） | 强制刷新烘焙副本（§4.2，B2 命令） |
| R-2 | S1 落点选择错误导致层级倒置（Frame→Projects） | 开工前定 D-A3；方案①（Agent 侧）避免逆向引用 |
| R-3 | S3 双写收敛触及写热路径（文档 R5） | 独立开关（默认关）+ 故障注入 + 单独评审；关即回旧路径 |
| R-4 | S4 换代抖动（LLM 网关/后端连接随代数重建，文档 R3） | Drain/Resume 钩子 + 重连超时；**不承诺零停机**（架构设计 §8） |
| R-5 | 单根守卫为进程级，测试并行互相干扰（文档 R4） | 相关测试类 `[DoNotParallelize]`；`RootInstanceGuard` 释放后配平 |
| R-6 | 对象槽跨代数复用导致跨 ALC 类型失效 | 换代 = 重建 + 状态迁移（不跨代复用句柄/委托） |

