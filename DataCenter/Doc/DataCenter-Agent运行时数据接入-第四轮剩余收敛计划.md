# DataCenter · Agent 侧运行时数据接入 —— 第四轮剩余收敛计划（B26 · B27）

> **日期**：2026-09-12 | **性质**：`DataCenter-Agent运行时数据接入-下一步实施计划.md` 的**下位子计划**（该文件为"下一步怎么做"的唯一权威，本文件仅细化其第四轮余项的执行口径）。
> **权威边界**：
> ① **缺陷事实**（位置/证据/严重度/状态）以 `DataCenter-Agent运行时数据接入-调用链逻辑缺陷清单.md` 为唯一权威（条目 `F-55~F-70`，第四轮）；本文件不复制缺陷表，只写"怎么做"。
> ② **设计语义**以 `DataCenter-Agent运行时数据接入-设计与实施.md` 为唯一权威。
> ③ **已完成步骤的实测记录**以 `DataCenter-Agent运行时数据接入-实施进展与后续.md` 为准；本文件只登记归口与判据。
> **通则**（沿用上级 §4）：每子项**独立可编译、可回退**；判据必须可执行（命令 / 断言）；**不改真源**（SQL/LiteDB）、**不改 CSG 主 schema**、**不动落盘键**（除非条目明示并给出迁移）。

---

## §0 范围与前置基线

### 0.1 覆盖范围

| 批次 | 条目 | 严重度 | 是否可直接实施 |
|---|---|---|---|
| **B26** | F-62（会话完成双 upsert）· F-63（`ur_nodes` 写两形读一形）· F-66（确定性核心双承载） | 中 | F-62 ✅ 可直接；F-63 ⛔ 先取证；F-66 ✅ 文档化+门禁 |
| **B27** | F-61（`SnapshotSerializer` 死实现）· F-64（观测事件双事实源）· F-65（对象槽双真源）· F-67（未落地槽键）· F-70（冗余面/同名族） | 中 · 中 · 中 · 低 · 低 | ⛔ 均需**先决策**或**先取证**（见 §1 / §2.2） |

> **不在本文件范围**：`F-55~F-60` / `F-68` / `F-69` 已随 B22/B23/B24/B25 完成（状态见缺陷清单 §5 B22–B25 行）；LLM 网关链、任务池调度链、持久化骨干链、UI 函数库链（沿用第三轮边界，不纳入）。

### 0.2 前置实测基线（本文件开工前状态）

| 项 | 实测值 |
|---|---|
| 构建 | `Projects\Plug\Agent\Agent.sln` **0 错 0 警**；宿主 `Projects\PrjBridge\PrjBridge.csproj` **0 错 0 警** |
| 测试 | `Agent.Core.Tests` **405/405** · `Agent.Axioms.Tests` **86/86** · `Agent.Launcher.Tests` **37/37** · `DataCenter.Tests` **95/95** · `Agent.Runtime.Tests` 21/21 · `Agent.Persistence.Tests` 88/88 · `Agent.Session.Tests` 20/20 · `Agent.Modules.Tests` 20/20 · `Agent.Operation.Tests` 47/47 · `Agent.Observe.Tests` 11/11 · `Agent.Guardrails.Tests` 10/10 · `Agent.Api.Tests` 34/34 · `Agent.Api.Contract.Tests` 22/22 |
| 门禁 | `Check-DataCenterGenericity.ps1`：G1 命中 0 / G2 违规 0 / **K9 违规 0 · 待迁移 0**；`Check-SingleWriter.ps1`：**OK**（告警 12 不计失败）；`Check-AgentRuntimeSlots.ps1`：**违规 0**（存量基线 20，AgentUiHost 静态槽已从基线消失）；`Check-AgentDecisionCoreBoundary.ps1`：**违规 0**（B26-2 新增门禁） |
| 已确立不变量（本文件实施须保持） | ① 根已绑 ⇒ 槽键 `state-center` 必须已登记（否则 E 段 fail-fast）② 状态真源出现**不同实例** ⇒ 装配期 fail-fast ③ 轮内 `Transient` 只按 **round scope** 清理 ④ 决策**观测契约词表**（`DecisionContractWords`）与**骨架词表**（`DecisionWords`）互不混用 ⑤ 会话作用域锚 = 稳定 `task_id` |

---

## §1 开工前决策点（DEC，需确认后才动代码）

| # | 议题 | 关联条目 | 选项 | **建议** | 影响面 / 不可逆性 |
|---|---|---|---|---|---|
| **DEC-1** | 观测事件**事实源唯一化方向** | F-64 | ① 以 `ur_events`（`IZoneStore`）为唯一真源，`AgentObservability` 的文件事件区降级为导出/投影<br>② 保留双源，但**显式分工**（语义观测 vs 运行事件）并各自声明消费方<br>③ 以 `events.jsonl` 为唯一真源，把 `IZoneStore` 适配到该存储 | **②（先行）→ ①（目标）**：`Agent.Observe` 的查询面（`ObservabilityQuery`）与 `RunEvent` 查询面（`ur_events`）当前服务不同消费方（UI 语义时间线 vs 运行事件重放），在消费方清单落地前强行合并会破坏查询口径。先补"消费方清单 + 双源各自边界"（零行为改动），再定唯一化 | 观测查询口径、UI 时间线、诊断脚本；**不可逆度：中**（合并后历史数据形态变更） |
| **DEC-2** | **对象槽真源**收敛方向 | F-65 | ① `AgentUiHost` 改为"根分区适配视图"（经既有 `IAgentUiHostAccess` 注入根分区读取实现），静态槽仅过渡<br>② 保留静态槽为 UI 面真源，换代时显式重发布<br>③ 只收敛惰性槽（已在根分区），`Attach*` 系列保持 | **①**：`IAgentUiHostAccess` 与"组合根显式化后可由宿主注入其它实现"的既有设计意图一致；①可在**保静态槽**的前提下增量落地（先加根分区读取实现 + 断言，再切默认），回退成本低 | UI 函数库（`AgentUiLibraryRuntime`/`ConversationViewService`）、换代迁移；**不可逆度：低**（可逐实现切换） |
| **DEC-3** | `ur_nodes` **读写形态**收敛方向 | F-63 | ① 收敛到"业务节点单形"（`NodeTypes.Node` + 内嵌 Outputs），读侧改枚举业务节点<br>② 收敛到"output 记录单形"，去掉业务节点 Outputs 字段与读-改-写<br>③ 保持两形，但**读侧两形都枚举 + 明确优先级** | **先取证后定**：取证点 = `NodeExecutionService.EnumerateInputOutputsAsync` 的实际枚举源、`NodeOutputStore` 读路径、`NodeOutputCodec.DecodeBusiness/EncodeBusiness` 是否已含 Outputs。若 Outputs 已内嵌 ⇒ 选 **①**；若读侧仍以 output 记录为准 ⇒ 选 **③**（先补齐读一致性，再谈单形） | 节点产出检索/复用、跨 task 寻址、图关系推导；**不可逆度：中**（形态变更影响历史数据可读性） |
| **DEC-4** | `SnapshotSerializer` 处置 | F-61 | ① 删除（含 `Snapshot` DTO + 同步 README/注释）<br>② 接线为真实恢复路径（需先确认调用方）<br>③ 保留并标 `[Obsolete]` + 修正误导注释 | **① 删除**（前置：确认 `ur_snapshots` 表无外部写入/读取依赖 —— 表路由在 `Agent.StorageBridge/Storage/StorageRouteTable.cs` 通用保留，与 `SnapshotSerializer` 是否删除无关） | 仅影响死代码面；**不可逆度：低**（git 可回退） |
| **DEC-5** | 未落地槽键 `Database` / `Prompt` | F-67 | ① 删除两个常量<br>② 保留并在注释标注"预留"<br>③ 接线（补解析点） | **①**（前置：扩展到 `Projects\` 全域 grep，确认宿主仓/脚本/Skill 未以常量访问） | 槽键权威表 `AgentRuntimeSlotKeys` 公开面；**不可逆度：低** |
| **DEC-6** | 决策**曲面词**与**观测契约词**关系 | F-70-2 | ① 建立显式映射 + 断言双词种集一一对应<br>② 保留两套但文档化分工（零行为改动） | **②先行、①随后**：`DecisionSurfaceGraph` 的小写词（`ts/tm/to/continue/switch`）是**决策曲面/坐标标签**，`DecisionContractWords` 是**观测契约枚举**，用途不同；先文档化分工 + 加"词种覆盖"断言，再评估是否合并 | 决策曲面/坐标契约、埋点词；**不可逆度：低** |

> **开工纪律**：DEC-1 / DEC-2 / DEC-3 **未确认前不得动对应代码**（仅允许执行 §2.2 的取证子项）；DEC-4 / DEC-5 / DEC-6 建议采纳后即可进入 §2 实施。

> **采纳状态（2026-09-12 已采纳并落地）**：**DEC-1②**（双源分工声明，零行为改动；① 保留为目标态）· **DEC-2①**（根分区承载 + 静态槽回落，UI 宿主对象槽双真源消除）· **DEC-3**（**取证结案**：执行链已单形、真实残余在 API `node.write`，形态收敛另立编号）· **DEC-4①**（删死实现 + 连带 3 测试用例）· **DEC-5①**（删两未落地槽键）· **DEC-6②**（词种分工文档化 + 覆盖断言，不合并两表）

---

## §2 批次与步骤明细

### 2.1 B26（P2）—— 可直接实施项

#### B26-1 · F-62：会话完成**单次 upsert**

| 项 | 内容 |
|---|---|
| 现状（证据） | `Agent.Orchestration\SessionWorkflow\ConversationCentralPipeline.cs` `FinalizeAsync`：先 `UpdatePayloadAsync(key, item)` 再 `WriteStatusAsync(key, item.Status, publishRuntime:false)`；后者内部 `GetAsync` + `ApplySnapshotStatusAsync` → **再次** `UpdatePayloadAsync`（`Agent.Core\TaskPool\AgentTaskPool.cs`）。同一逻辑变更 = 2 次整行写 + 1 次额外读 |
| 改动点 | 收敛为**单次写**：会话路径已自行更新载荷（Round 递增、归档键），故**删除** `FinalizeAsync` 中的 `WriteStatusAsync` 调用，仅保留 `UpdatePayloadAsync`（Status 随载荷单源落库，`AgentTaskPool` 既有注释即此语义）；若需 UI 实时帧，走 `publishRuntime:true` 的既有单写路径而非二次 upsert |
| 判据 | ① 行为断言：`FinalizeAsync` 后 `Round` 已递增、归档键（`__backend_audit` 等）仍在、`Status ∈ {Completed, Cancelled, Faulted}`；② 写次数断言：以计数假 `IConversationStore` 断言同一 taskKey 的 `UpdatePayloadAsync` 调用 **= 1**、`WriteStatusAsync` **= 0** |
| 回归面 | `Agent.Orchestration.SessionWorkflow` 会话路径、任务池状态列消费方（无独立镜像列，注释已确认） |
| 回退 | 单提交可撤 |
| 风险 | 低（去掉的是**重复**写，非唯一写）；须确认 `IConversationStore.WriteStatusAsync` 在会话路径**无**其它副作用（当前实现仅 upsert） |

#### B26-2 · F-66：确定性核心**双承载**的边界化

| 项 | 内容 |
|---|---|
| 现状（证据） | 运行期内核 = `AgentTaskItem : ILoopState`（`Agent.Core\TaskPool\AgentTaskItem.cs`：Budget/Token/Flag）；DataCenter 侧 `DataCenter.Extensions.LoopDecision\LoopStateStore.cs` + `CoreStateView` 把同一内核落 Center Session 层，但 **Agent 侧零调用**（全仓 grep 仅 `DecisionCenter` 被 Agent 使用） |
| 改动点 | **不删代码、只定边界**：① 在 `LoopKeys` / `LoopStateStore` 注释标注"消费方 = StateHoldDriven 宿主线，Agent 侧不接线（Agent 运行期内核 = `AgentTaskItem`；Center 侧审计快照 = `DecisionCenter.Record`）"；② 在 `AgentSide` 侧对应位置（`AgentRunner.Persistence` 决策记录处）标注分工；③ 新增**门禁判据**：Agent 侧不得出现 `LoopStateStore` / `CoreStateView` 的引用（脚本或测试断言） |
| 判据 | ① 断言测试：`Agent.sln` 依赖闭包中不存在对 `Operation.StateBlock.Decision.LoopStateStore` 的引用（可用源码 grep 断言脚本）；② 文档注释可核对 |
| 回退 | 单提交可撤（纯注释 + 断言） |
| 风险 | 低；**禁止**在本子项中删除 `LoopStateStore`（跨仓消费方需独立取证，见 §6 诚实边界） |

#### B26-3 · F-63：`ur_nodes` 读写形态（**取证后**实施）

| 项 | 内容 |
|---|---|
| 现状（证据） | 写侧两处：`NodeExecutionService.WriteNodeOutputsAsync`（逐端口 `WriteOutputAsync` → output 记录）与 `PersistNodeReferencesAsync`（`PutAsync` 读-改-写 `NodeTypes.Node` 业务节点，保留既有 `Outputs`）；读侧 `EnumerateInputOutputsAsync` 只枚举**一种** |
| **前置取证（本子项第一步，零行为改动）** | ① 定位 `EnumerateInputOutputsAsync` 实现与实际枚举源；② 定位 `NodeOutputStore.WriteOutputAsync` / `NodeOutputCodec.DecodeBusiness`/`EncodeBusiness` 的字段归属；③ 汇总"本轮产出节点"的三处推导（`NodeExecutionService` / `NodeWritePipeline.EmitNodeSignals` / `SyncRelationsBlock.ResolveProducedNodeKeys`）是否同源 |
| 改动点 | 按 **DEC-3** 决策结果实施；取证结论未出前**只提交取证报告**（追加到缺陷清单对应条目），不改代码 |
| 判据 | 取证阶段：报告可核对（符号 + 行号）；实施阶段：① 同一产出在 `ur_nodes` 的**读**路径可完整取回（含跨 task 寻址）；② 既有节点检索/图关系用例不回归 |
| 回退 | 取证报告可撤（文档）；实施按 DEC-3 拆分可撤 |
| 风险 | **中–高**：形态变更影响历史数据可读性 ⇒ 必须有"读两形兼容期"或明确迁移 |

### 2.2 B27（P2 · P3）—— 先决策 / 先取证项

#### B27-1 · F-61：删除 `SnapshotSerializer` 死实现（DEC-4 采纳①后实施）

| 项 | 内容 |
|---|---|
| 现状（证据） | `Agent.Runtime\Persistence\Core\SnapshotSerializer.cs`（`Serialize`/`Deserialize`/`Apply` + `Snapshot` DTO）**零代码消费方**；反向引用仅 `Agent.Core\Boundaries\DefaultBoundaryFramework.cs` 注释与 `README.md` 提法；其还原逻辑本身有误（以 `Escalate()`/`Advance()` 循环还原，不校验 `Value ≤ Max`） |
| 前置取证 | ① `Projects\` 全域 grep `SnapshotSerializer` / `ur_snapshots`（含脚本、Skill、宿主仓）；② 确认 `Agent.StorageBridge` 的 `ur_snapshots` 路由为**通用表路由**（与死类无关，删除不影响表） |
| 改动点 | 删除 `SnapshotSerializer.cs`；同步修正 `DefaultBoundaryFramework.cs` 的误导注释与 `README.md` 的目录说明（若提法指向该文件） |
| 判据 | ① `Agent.sln` 构建 0 错 0 警；② 全域 grep `SnapshotSerializer` 命中 0；③ `Agent.Runtime.Tests` / `Agent.Persistence.Tests` 不回归 |
| 回退 | 单提交可撤 |
| 风险 | 低；若取证发现外部脚本依赖 `ur_snapshots` 表，**仅**删除死类并保留表路由（两者无耦合） |

#### B27-2 · F-64：观测事件事实源（DEC-1 采纳②后实施）

| 项 | 内容 |
|---|---|
| 现状（证据） | `Agent.Operation\Observe\AgentObservability.cs`（`FileAppendEventStore` = `events.jsonl` + `PersistentEventSink` + `HotRingSink`；装配点 `Agent.Launcher\Composition\AgentFrameworkHosting.cs`）与 `Agent.Core\DataStream\EventSink.cs`（`ur_events` via `IZoneStore`；装配点 `Agent.Launcher\Composition\DataStoreHosting.cs`）为两套事件事实源（不同介质 / 不同 schema / 不同查询面） |
| 前置取证（零行为改动） | ① 列出两侧**消费方清单**（`ObservabilityQuery` / `AgentTrace` / UI 时间线 / `ur_events` 查询 / 诊断脚本）；② 标注各自查询口径与保留策略（append-only 文件 vs 库表） |
| 改动点（②） | 在两侧入口注释与设计文档中**显式声明分工与边界**（语义观测 vs 运行事件），并各自标注"唯一真源范围"；不改行为、不改装配 |
| 改动点（①，目标态） | 待消费方清单确认后，把 `events.jsonl` 降级为导出/投影（或反向把 `IZoneStore` 适配到文件后端），单一写入路径落地 |
| 判据 | ②阶段：分工声明可核对 + `Agent.Observe.Tests` 11/11 不回归；①阶段：同一事件在**唯一**真源可查，另一侧仅投影 |
| 回退 | 按阶段可撤（②纯文档；①需保留兼容读） |
| 风险 | **中**：查询口径与历史数据形态；**禁止**在未确认消费方前合并两侧 |

#### B27-3 · F-65：对象槽真源（DEC-2 采纳①后实施）

| 项 | 内容 |
|---|---|
| 现状（证据） | 根分区：E 段 `RegisterSlot`（`AgentComposition.cs`）+ 惰性 `AgentRuntimeRoot.Resolve`（槽键 `Framework`/`Config`/`Llm`/`Workflows`/`Skills`/`StateLocator`/`StateCenter`）；静态槽：`Agent.Launcher\Modules\AgentUiHost.cs`（`Attach*` 系列，装配面 `AgentFramework.cs` / `AgentComposition.FromFramework`）⇒ 同一批装配产物两处发布，换代迁移只覆盖根分区 |
| 改动点（增量、可回退） | ① 新增 `IAgentUiHostAccess` 的**根分区读取实现**（读 `root.Slots.Partition("agent")`，缺失回落既有静态槽 → 行为等价）；② 生产者侧（`Attach*`）改为**同时**写根分区（或复用 E 段既有登记）；③ 消费方（`AgentUiLibraryRuntime` / `ConversationViewService`）切换注入；④ 断言稳定后再评估静态槽退役（**不在本子项内**） |
| 判据 | ① `AgentUiHost` 读到的实例 ≡ 根分区实例（引用相等断言）；② 换代（`SwapGenerationAsync`）后经 UI 面解析到的槽产物属**新代数**；③ `Agent.Launcher.Tests` / `Agent.Api.Tests` 不回归 |
| 回退 | 每步可撤（实现注入点切换） |
| 风险 | **中**：UI 函数库经静态槽访问面广（`AgentUiHost` 全类 + 多个 `Shared` 实例），须逐实现切换并保持回落 |

#### B27-4 · F-67：未落地槽键（DEC-5 采纳①后实施）

| 项 | 内容 |
|---|---|
| 现状（证据） | `Agent.Contract\Runtime\AgentRuntimeSlotKeys.cs` 声明 `Database` / `Prompt`，全仓（`Projects\Plug\Agent\src`）**无解析点/注册点** |
| 前置取证 | 扩展到 `Projects\` 全域 grep `AgentRuntimeSlotKeys`；确认无宿主仓 / 脚本 / Skill 以常量访问 |
| 改动点 | 删除两常量（若取证发现预留意图，则改为注释标注"预留，未接线"并登记） |
| 判据 | 构建 0 错 0 警 + 全域 grep 命中 0（或"预留"注释可核对） |
| 回退 | 单提交可撤 |
| 风险 | 低 |

#### B27-5 · F-70：冗余面 / 同名族（DEC-6 采纳②后实施）

| 子项 | 现状（证据） | 处置 |
|---|---|---|
| **F-70-1** 空壳类型 | `Agent.Core\Nodes\DecisionWordMapper.cs` 仅存 `[BindAxiom(DecisionOutputSet)]` 载体 | 🟦 **接受现状**（删除会失公理绑定）；保持注释口径即可，不改代码 |
| **F-70-2** 决策曲面词 vs 观测契约词 | `Agent.Operation\Routing\Semantic\DecisionSurfaceGraph.cs`（`TerminalTs="ts"` / `TerminalTm` / `TerminalTo` / `Continue` / `Switch` + `LoopDecision` 映射）vs `Agent.Contract\Observability\DecisionContractWords.cs`（`TS/TO/TM/CONTINUE/SWITCH`） | 先**文档化分工**（曲面/坐标标签 vs 观测契约枚举）；随后加"词种覆盖"断言（两词种集一一对应，防新增漏配）；**不合并**两表 |
| **F-70-3** `Reset` / `Clear` 同名族 | `StateCenter.Reset`（清整层 Session）· `StateStore.ResetObjectState`（仅对象态）· `StateContextAdapter.ClearTransient`（按 round scope 清 Transient，B22 后） | 文档化语义表，**不改行为**（命名已部分收敛，`ResetObjectState` 即 F-28 更名结果） |

| **判据** | ① 词种覆盖断言新增且通过；② 语义表可核对；③ 全量 Agent 测试不回归 |
| **回退** | 文档 + 断言可撤 |
| **风险** | 低 |

---

## §3 判据命令清单（复制即用）

```powershell
# ── 构建 ──
dotnet build 'D:\Project\Main\Projects\Plug\Agent\Agent.sln' -v q --nologo
dotnet build 'D:\Project\Main\Projects\PrjBridge\PrjBridge.csproj' -v q --nologo   # 宿主面

# ── 回归（基线见 §0.2）──
cd 'D:\Project\Main\Environment\CentralTest\AgentTest'
foreach ($p in @('Agent.Core.Tests','Agent.Axioms.Tests','Agent.Launcher.Tests','Agent.Runtime.Tests',
                 'Agent.Persistence.Tests','Agent.Session.Tests','Agent.Modules.Tests','Agent.Operation.Tests',
                 'Agent.Observe.Tests','Agent.Guardrails.Tests','Agent.Api.Tests','Agent.Api.Contract.Tests')) {
  $r = (dotnet test "$p\$p.csproj" -v q --nologo 2>&1 | Select-String -Pattern '已通过!|失败:|error CS' | Select-Object -Last 1) -join ' '
  Write-Output "$p => $r"
}
cd 'D:\Project\Main\Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Tests'; dotnet test -v q --nologo

# ── 门禁 ──
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-DataCenterGenericity.ps1'   # 期望 K9 违规 0 / 待迁移 0
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-SingleWriter.ps1'           # 期望 OK

# ── 取证（只读，不计入判据）──
$all = (Get-ChildItem 'D:\Project\Main\Projects' -Recurse -File -Include *.cs,*.ps1,*.md |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' }).FullName
Select-String -Path $all -Pattern 'SnapshotSerializer|ur_snapshots'
Select-String -Path $all -Pattern 'AgentRuntimeSlotKeys'
Select-String -Path $all -Pattern 'LoopStateStore|CoreStateView'
```

---

## §4 提交规范与验证纪律（沿用上级 §5）

| 场景 | 动作 |
|---|---|
| 每子项 | **一处变更 + 一条最小断言**；批末跑 §3 全集（构建 + 回归 + 门禁） |
| 契约变更 | 必须刷新烘焙副本（设计文档 §4.2）；本文件各子项**默认不改契约** |
| 文档回写 | 子项完成后回写**缺陷清单**对应条目状态（`✅` + 判据实测值 + 提交哈希），**条目不删**；并同步上级计划 §0 基线（测试/门禁数值） |
| 决策项 | DEC 采纳后须在本文件 §1 标注"已采纳（日期）"，并在上级计划 §2 决策点表登记一行 |
| 取证项 | 取证结论追加到**缺陷清单**对应条目的"影响/状态"列（含符号 + 行号），不得只留在会话记录中 |

---

## §5 风险与回退一览

| 子项 | 风险 | 回退方式 |
|---|---|---|
| B26-1（F-62） | 低 | 单提交 |
| B26-2（F-66） | 低（纯注释 + 断言） | 单提交 |
| B26-3（F-63） | **中–高**（历史数据可读性） | 取证 → 决策 → 分步；保留读兼容期 |
| B27-1（F-61） | 低 | 单提交 |
| B27-2（F-64） | 中（查询口径 / 历史形态） | 阶段化（文档 → 唯一化），保留兼容读 |
| B27-3（F-65） | 中（UI 访问面广） | 逐实现切换 + 静态槽回落 |
| B27-4（F-67） | 低 | 单提交 |
| B27-5（F-70） | 低 | 文档 + 断言可撤 |

**总回退纪律**：任一子项若触发判据失败且 30 分钟内无法定位 → **立即回退该子项**（`git revert`/撤销提交），不在同一提交内叠加修复。

---

## §6 诚实边界

| # | 说明 |
|---|---|
| 1 | **本文件不含缺陷事实**（位置 / 证据 / 严重度），只写"怎么做"；任何冲突以缺陷清单条目为准 |
| 2 | **DEC-1 / DEC-2 / DEC-3 未确认前**，对应子项只允许执行取证（零行为改动）；强行实施视为越权 |
| 3 | **F-61 / F-63 / F-66 / F-67** 的取证范围必须覆盖 `Projects\` 全域（含宿主 `PrjBridge`、`FileDriven` 脚本与 `skills\`），不得只扫 `Plug\Agent\src`；跨仓（Frame ↔ Projects）消费方须在实施前单独记录 |
| 4 | **B27-3（F-65）** 只承诺"UI 面读到根分区同一实例 + 换代可见新代数产物"，**不承诺**静态槽退役（退役属后续独立工作项，需单独决策） |
| 5 | 本轮**不覆盖** LLM 网关链、任务池调度链、持久化骨干链、UI 函数库链的深层数据流（沿用第三轮边界）；扩展须新立编号与批次 |
| 6 | 判据基线（§0.2 的测试 / 门禁数值）随代码演进会漂移；每次开工前须重新实测并更新本表 |
| 7 | 本文件的"已采纳"状态**只有**在 §1 显式标注后才生效；未标注的 DEC 一律视为**未决** |

---

## §7 建议执行顺序

```
[先决策/取证，零行为改动，可并行]
  ├─ REG-1 取证：F-63 ur_nodes 读写形态（EnumerateInputOutputsAsync / NodeOutputStore / NodeOutputCodec）
  ├─ REG-2 取证：F-61 + F-67 全域引用面（grep）      ──► DEC-4 / DEC-5 定案
  ├─ REG-3 取证：F-64 观测消费方清单                  ──► DEC-1 定案
  └─ REG-4 取证：F-66 跨仓 LoopStateStore 消费方       ──► 判定"边界化"或"跨仓保留"

[可立即实施，不依赖决策]
  B26-1（F-62 单次 upsert）┐
  B26-2（F-66 边界化）      ├─► 每项单提交 + 最小断言 ──► 批末跑 §3 全集
  B27-5（F-70 文档 + 词种断言）┘

[决策后实施]
  DEC-4 ► B27-1（删死实现）
  DEC-5 ► B27-4（删未落地槽键）
  DEC-1 ► B27-2（观测事实源：先文档 → 后唯一化）
  DEC-2 ► B27-3（对象槽真源：增量切换，保留回落）
  DEC-3 ► B26-3（ur_nodes 形态，含读兼容期）
```

**批末统一动作**：① §3 全集实测；② 回写缺陷清单条目状态；③ 同步上级计划 §0 基线；④ 若基线数值变化，更新本文件 §0.2。


## §8 落地记录（2026-09-12 · B26 + B27 全量，单提交）

| 子项 | 条目 | 落地内容 | 判据实测 |
|---|---|---|---|
| B26-1 | F-62 | `ConversationCentralPipeline.FinalizeAsync` 收敛为**单次** `UpdatePayloadAsync` | 新增 `ConversationFinalizeSingleWriteTests`（UpdatePayloadAsync=1 · WriteStatusAsync=0 · Round/Status/归档键不变） |
| B26-2 | F-66 | `LoopKeys`/`LoopStateStore`/`AgentRunner.Persistence` 边界注释 + 新门禁 `Check-AgentDecisionCoreBoundary.ps1` | 门禁 `-Check` 退出码 0（扫描 494 文件 · 违规 0） |
| B26-3 | F-63 | **取证结案**：执行链已单形（`NodeOutputStore` 只写 `NodeType=node` + 内嵌 `Outputs`）；残余 = API `node.write` 缺省形（另立编号）；`NodeExecutionService` 注释按实证改写 + 读一致性判据 | `NodeOutputStoreTests.ReadView_TaskScoped_ReadsBackEmbeddedOutputs_And_RecordIsSingleForm`；`Agent.Persistence.Tests` 88/88 |
| B27-1 | F-61 | 删 `SnapshotSerializer` 死实现 + `Snapshot` DTO + 3 测试消费点；修 `DefaultBoundaryFramework` 注释 / `README.md`；`ur_snapshots` 表路由保留 | 全域 grep `SnapshotSerializer` 命中 0；`Agent.sln` 0/0；`Agent.Core.Tests` 405/405 · `Agent.Persistence.Tests` 88/88 |
| B27-2 | F-64 | 双源分工声明（`AgentObservability` / `EventSink` 注释）+ 设计文档 §10（消费方清单 / 边界纪律 / DEC-1① 方向） | `Agent.Observe.Tests` 11/11 |
| B27-3 | F-65 | `AgentUiHost` 9 个 UI 对象槽改根分区承载（`RootSlot<T>` + 9 槽键），未绑根回落本地容器；`AgentUiHostAccess` 无二次存储 | `AgentUiSlotRootPartitionTests` 3 项（引用相等 / 根分区含键 / 未绑根回落 / 换代不残留）；`Agent.Launcher.Tests` 37/37 |
| B27-4 | F-67 | 删 `AgentRuntimeSlotKeys.Database`/`Prompt`；同步方案文档键数（13 → 21） | `Agent.sln` 0/0；全域 grep 两常量命中 0 |
| B27-5 | F-70 | 词种分工文档化 + `DecisionSurfaceWordCoverageTests`（5/5 一一对应）；`Reset/Clear` 语义表落 `Agent-DataOwnership.md` §9；F-70-1 接受现状 | `Agent.Core.Tests` 405/405 |

**统一验证（批末一次跑完）**：`Agent.sln` 0 错 0 警 · `PrjBridge.csproj` 0 错 0 警 · 13 个测试工程全绿（`Agent.Core.Tests` 405 · `Agent.Launcher.Tests` 37 · `Agent.Persistence.Tests` 88 · `DataCenter.Tests` 95 · 其余同 §0.2）· 四门禁全通过（Genericity G1/G2/K9 = 0 · SingleWriter OK · RuntimeSlots 违规 0 · DecisionCoreBoundary 违规 0）。

**实测偏差记录**：① B26-3 的"写两形"记载已过期（执行链单形，残余在 API 写路径）；② F-61 的"零消费方"仅对生产代码成立（测试有 3 个消费点，已连带删除）；③ F-65 的 `Framework` 槽此前已走根分区，本次收敛的是 `Attach*` 系列 9 槽。

