# DataCenter · Agent 侧运行时数据接入 —— 第五轮收口实施计划（W0–W6）

> **日期**：2026-09-12 | **性质**：`DataCenter-Agent运行时数据接入-下一步实施计划.md` 的**下位子计划**（该文件为"下一步怎么做"的唯一权威，本文件仅细化其剩余阶段 S3 / S1-④ / S2 / S5 及分离方案 Batch 2–7 的执行口径）。
> **权威边界**：
> ① **缺陷事实**（位置/证据/严重度/状态）以 `DataCenter-Agent运行时数据接入-调用链逻辑缺陷清单.md` 为唯一权威；本文件不复制缺陷表。
> ② **设计语义**以 `DataCenter-Agent运行时数据接入-设计与实施.md` 为唯一权威（§1 分类 / §3 通道 / §4 阶段 / §5 门禁）。
> ③ **批次口径**以 `Projects\Plug\Agent\doc\Agent数据与代码分离-架构整理与通用化方案.md` §7 Batch 0–7 + §4 归属台账（`data-ownership.json`）为准。
> ④ **基线数值**以 `DataCenter-Agent运行时数据接入-实施进展与后续.md` 为准；本文件 §0.2 为开工前实测快照。
> **通则**（沿用上级）：每子项**独立可编译、可回退**；判据必须可执行（命令 / 断言）；**不改真源**（SQL/LiteDB）、**不改 CSG 主 schema**、**不动落盘键**（域别名表已落地，改名方案不采纳）。

---

## §0 范围与前置基线

### 0.1 覆盖范围（剩余工单 → 批次映射）

| 本文件 | 上级计划 | 分离方案 | 内容 | 风险 |
|---|---|---|---|---|
| **W0** | S5-2 前置 | Batch 7-2 前置 | 台账登记归零 + 冻结值同步（D5 无主 1 处） | 低 |
| **W1** | S5-2 | **Batch 3** | 静态槽归零（数据面收口，工作量最大） | 高（后 3 序含 UI/HTTP/进程资源） |
| **W2** | **S3** | **Batch 4** | 双写收敛（单一权威，独立开关） | **最高**（触写热路径） |
| **W3** | **S1-④** + **S2** | **Batch 5** | 投影与观测统一（对象槽只读投影 + T4 观测内核 + 追踪面） | 中低（只读旁路） |
| **W4** | 残留 | **Batch 2 残余** | 通用机制下沉（T1 取根门面 / T2 槽实现合并；T3 已完成） | 中 |
| **W5** | — | **Batch 6** | 通用性回灌验证（第二消费方） | 中 |
| **W6** | **S5** | **Batch 7** | 收尾（文档回写 / 基线归零 / 可换域清单 / 四通道验收） | 低 |

> **不在本文件范围**：LLM 网关链、任务池调度链、持久化骨干链、UI 函数库链的深层数据流（沿用第三轮边界）；B26/B27（F-61~F-67/F-70）已完成，见第四轮子计划 §8。

### 0.2 前置实测基线（开工前核对，全部为 2026-09-12 实测值）

| 项 | 实测值 |
|---|---|
| 构建 | `Projects\Plug\Agent\Agent.sln` **0 错 0 警**；宿主 `Projects\PrjBridge\PrjBridge.csproj` **0 错 0 警** |
| 测试 | Core **405/405** · Axioms 86/86 · Launcher **37/37** · Persistence 88/88 · Session 20/20 · Modules 20/20 · Operation 47/47 · Observe 11/11 · Guardrails 10/10 · Api 34/34 · Api.Contract 22/22 · Runtime 21/21 · DataCenter 95/95 |
| 门禁 | `Check-DataCenterGenericity`：G1 命中 0 / G2 违规 0 / **K9 违规 0 · 待迁移 0**（白名单 4）· `Check-SingleWriter`：**OK**（告警 12 不计失败）· `Check-AgentRuntimeSlots`：457 文件 · **违规 0** · 存量基线 **20 命中 / 15 文件仍命中**（基线表 16 条，`AgentUiHost.cs` 已收敛出）· `Check-AgentDecisionCoreBoundary`：494 文件 · **违规 0** |
| **门禁（异常）** | `Check-AgentDataSeparation.ps1 -Check` = **EXIT 1** —— D5 无主数据 **1 处**（`Agent.Contract\Runtime\AgentRuntimeRoot.cs:128` S1 静态字段 `_stateCenterFallbackUsed`，台账无匹配 `pattern`）；D1 K10 命中 20（冻结 22）· D2/D4 全 0 · D3 违规 0。**该失败为既有状态**（非 B26/B27 引入：`git show HEAD~1` 证明该字段在 B26/B27 提交前不存在于仓库、但存在于当时工作树，本批未触碰该文件） |
| 台账 | `data-ownership.json` **20 条** = registered 8 + migrated 1 + **pending 11** |
| 双写（未收敛） | ① `NodeExecutionService.cs:254 vars.Set(...)` ↔ `:265 PersistAsync`（写侧双写仍在）② `RoundStateMapper.cs:117 Harvest` → `:124 state.Data[key] = v!`（`:132` 注释自述 ur_nodes 权威、`state.Data` 仅派生缓存） |
| 追踪面（阻塞） | `IDataCenterRoot` 成员实测 = `Root` / `Projector` / `Registry` / `Generation` / `Slots` —— **无 `Trace` 成员**（S2 前置 D2/D3 未落地） |
| 已确立不变量（实施须保持） | ① 根已绑 ⇒ 槽键 `state-center` 必须已登记 ② 状态真源出现**不同实例** ⇒ 装配期 fail-fast ③ 轮内 `Transient` 只按 **round scope** 清理 ④ 决策**观测契约词表**与**骨架词表**互不混用 ⑤ 会话作用域锚 = 稳定 `task_id` ⑥ 域别名须在 `DataCenterHost.Boot` **之前**并入注册表 ⑦ 对象槽值寄存根分区（F-65），未绑根回落进程本地容器 |

---

## §1 剩余工单总表（判据 ↔ 依赖）

| # | 工单 | 判据（可执行） | 依赖 | 建议批次 |
|---|---|---|---|---|
| W0-1 | D5 无主 1 处登记归属 | `Check-AgentDataSeparation.ps1 -Check` = EXIT 0 | 无 | 批次 5b（可立即） |
| W0-2 | 冻结值同步（K10Hits 22 → 20） | 台账 §5 + 脚本 `$frozen` 一致 | W0-1 | 同上 |
| W1-1..9 | 静态槽归零（9 序 / 20 命中 / 15 文件） | `Check-AgentRuntimeSlots.ps1 -Check` 违规 0 且**存量基线逐序下降至 0** | 无（可长期分项推进） | 批次 6 |
| W2-1 | `vars` ↔ `ur_nodes` 写侧单写 + 投影重算 | 开关关 = 行为等价（全量绿）；开关开 = 故障注入后重启状态与落盘一致 | 无硬依赖（需真实宿主验证面，建议 W1 前若干序完成后） | 批次 6（**单独评审**） |
| W2-2 | `PayloadJson` ↔ `state.Data` 权威单一化 | 同上；`NodeGraphReconciler` 跨介质缺口 0 | W2-1 同批 | 同上 |
| W3-1 | 对象槽只读投影（`IRuntimeSlotEntrySource`） | 投影幂等（两次 `ToJson` 全等，时间戳不进产物）+ K5 未注册 Kind = 0 | 无（纯新增） | 批次 5（可穿插） |
| W3-2 | T4 观测内核下沉 Frame + 四族契约实例化 | `WHERE ScopeId=RunId` 一次查全「提议→裁决→工具命中→落库」 | 框架侧 T4 | 批次 5 |
| W3-3 | 追踪面（`IDataCenterRoot.Trace` + 盖章） | K7 追踪四要素缺失 0；`Ancestors(终答节点)` 闭合 | **阻塞**：框架侧 D2/D3 | 批次 5（等待期先做 W3-3a） |
| W3-3a | writer 身份常量表（无依赖等待期项） | 常量集与盖章点一一对应（编译期断言） | 无 | 可立即 |
| W4-1 | T1 取根门面下沉（去 AmbientSlot/进程字段叠加） | Frame 侧不引任何 Agent 类型即编译通过 | 无 | 批次 5b |
| W4-2 | T2 三套槽实现合并为一份 | 同上 + 同键同实例/异实例 fail-fast 判据 | W4-1 | 批次 5b |
| W5 | 第二消费方复用同一根/槽/观测内核 | **不引用任何 Agent 类型**的最小应用：注册契约 + 埋点 + Timeline 跑通 | W1/W3/W4 | 批次 6 |
| W6 | 收尾（文档 / 基线归零 / 可换域清单 / 四通道验收 / 既有缺陷修复） | 各脚本 `$frozen`/`$baseline` 清空后仍 EXIT 0 | 全部 | 批次 7 |

---

## §2 开工前决策点（W-DEC，需确认后动代码）

| # | 议题 | 关联 | 选项 | **建议** | 影响面 / 不可逆性 |
|---|---|---|---|---|---|
| **W-DEC-1** | D5「无主数据 1 处」处置 | W0-1 | ① 在 `data-ownership.json` 既有条目 `pattern` 追加 `Agent.Contract/Runtime/AgentRuntimeRoot.cs`（1 行）<br>② 为该回落标记单列一条 `status=pending` 条目<br>③ 仅修正台账 §5 冻结说明、不登记 | **①**：该字段是 A1「取根通道」的回落一次性置位标记（K10 白名单成员），归属 = 条目 `round-state-kv`（根真源解析面）语义最贴；不新增台账行、不改判据逻辑 | 台账冻结值、聚合门禁 EXIT 码；**不可逆度：低** |
| **W-DEC-2** | 观测事实源唯一化（DEC-1①）时机 | W3-2 | ① 紧跟 W3-2 之后做（`events.jsonl` 降级为导出/投影）<br>② 先冻结两侧查询口径，唯一化推迟到 W6 之后 | **②**：`ObservabilityQuery` 的投影语义不得被库表 schema 改写；先做 W3-2 的 T4 内核下沉（同源化查询面），再谈介质合并 | 观测查询口径、历史事件形态；**不可逆度：中** |
| **W-DEC-3** | 静态槽「归零」口径 | W1 | ① 仅 `AgentRuntimeRoot`（取根通道）永久保留在白名单，其余基线**清零**<br>② 连取根通道一并迁槽 | **①**：`AgentRuntimeRoot` 是 A1 的进程级取根承载（迁移期载体，白名单已有登记理由），迁它等于取消无根回落语义 | K10 门禁、无根宿主行为；**不可逆度：低** |
| **W-DEC-4** | API `node.write` 缺省形态（F-63 残余） | W1 后续 | ① 缺省 `NodeTypes.Node`（与执行链单形对齐）<br>② 保持 `NodeTypes.Output` 缺省，仅补文档 | **①**：使 API 写入的节点记录可被执行链读侧（`EnumerateOutputsAsync` 只枚举 `NodeType=node`）读回；显式 `nodeType` 参数仍可覆盖（向后兼容靠显式传参） | API 契约缺省行为；**不可逆度：中**（旧调用方未显式传 `nodeType` 时行为改变，须先全域 grep 调用面） |
| **W-DEC-5** | 对象槽产物「新代数 ALC 重建」口径 | W1 后 | ① W1 归零后单独立批（新 ALC 重装配 + 槽产物归属断言）<br>② 与 W2 合并 | **①**：该残项的**唯一前置**是静态槽归零（Batch 3）；与写热路径收敛（W2）不同风险面，合并会污染回退粒度 | 换代端到端判据强度；**不可逆度：低** |

---

## §3 批次与步骤明细

### 3.1 W0 · 台账与门禁归零（低风险，可立即做）

| 项 | 内容 |
|---|---|
| **现状（实测）** | `Check-AgentDataSeparation.ps1 -Check` = **EXIT 1**：D5 无主 **1 处** = `Agent.Contract\Runtime\AgentRuntimeRoot.cs:128`（`private static int _stateCenterFallbackUsed;`，S1 静态字段）。台账 20 条无匹配 `pattern`；D1 K10 命中 **20**（冻结 **22**）→ 冻结值已失真 |
| **改动点** | ① 按 **W-DEC-1①**：`data-ownership.json` 的 `round-state-kv` 条目 `pattern` 追加 `Agent.Contract/Runtime/AgentRuntimeRoot.cs`（1 行，无逻辑改动）；② 台账 `Agent-DataOwnership.md` §5 冻结表：`K10 存量基线命中 22（16 文件）` → **20（15 文件仍命中 + 1 文件已收敛）**、`D5 无主数据 0` 的表述改为**当前实测 0**（登记后）；③ 脚本 `Check-AgentDataSeparation.ps1` 的 `$frozen.K10Hits` 同步为 20（只允许下降的判据口径不变） |
| **判据** | ① `Check-AgentDataSeparation.ps1 -Check` = **EXIT 0**（D1–D5 全过）② `Check-AgentRuntimeSlots.ps1 -Check` = EXIT 0（基线 20 ≤ 冻结 20）③ 无源码改动 ⇒ 全量测试零回归 |
| **回退** | 纯台账/脚本改动，单提交可撤 |
| **风险** | 低；**注意**：只登记**既有**持有物，不得借机放宽 `$frozen` 兜底 |

### 3.2 W1 · 静态槽归零（分离方案 Batch 3；工作量最大，逐序独立提交）

**收敛口径（W-DEC-3①）**：仅 `Agent.Contract\Runtime\AgentRuntimeRoot.cs`（A1 取根通道，白名单已登记理由）保留；其余 15 文件 20 处**清零**，随后清空脚本 `$baseline` 与账台 `frozenBaseline`。

| 序 | 文件（当前实测命中） | 命中数 | 风险 | 动作 | 单序判据 |
|---|---|---|---|---|---|
| 1 | `Agent.Operation\Skill\SkillsOptions.cs`（:11/:14/:17） | 3 | 低 | 构造注入 / 槽 | K10 总命中 −3；技能面测试绿 |
| 2 | `Agent.McpRegistryHost\RegistryToolService.cs:30` · `RegistrySkillProvider.cs:20` · `McpToolFunction.cs:27` | 3 | 低 | 槽 + 构造注入 | −3；MCP 工具面测试绿 |
| 3 | `Agent.McpRegistryHost\AgentMcpLoopback.cs`（:25/:27/:30） · `MCP\GraphTaskTools.cs:37` · `MCP\LocalDbMcpTools.cs:31` | 5 | 低 | 槽 | −5；回环/图任务工具测试绿 |
| 4 | `Agent.Core\Prompts\PromptFrameworkFunction.cs:43` | 1 | 中 | 槽 `prompt` | −1；提示词面测试绿 |
| 5 | `Agent.Axioms\Infrastructure\ClineCredentials.cs`（:28/:29） · `Loops\DataContractRegistrar.cs:19` | 3 | 中 | 配置注入 / 槽 | −3；凭据/契约注册测试绿 |
| 6 | `Agent.Launcher\Modules\Libraries\AgentUiLibraryDb.cs:116` | 1 | 中 | 槽 `database` | −1；DB 函数库测试绿 |
| 7 | `Agent.Launcher\Composition\ConversationHosting.cs:67` · `AgentFrameworkHosting.cs:21` | 2 | 中 | 槽 + 生命周期句柄 | −2；Launcher/Api 测试绿 |
| 8 | `Agent.Launcher\Modules\AgentUiHost.cs` | **0（已收敛）** | — | ✅ 本批 F-65 已实现（`RootSlot<T>` 根分区承载） | 已完成，无需再动 |
| 9 | `Agent.Modules\Cline\ClineClient.cs:21` · `Pool\ClineDaemonPool.cs:27` | 2 | **高** | 槽 + 释放顺序（进程资源） | −2；Cline 链测试绿 + 释放顺序断言 |

| 项 | 内容 |
|---|---|
| **每序通用判据** | ① `Check-AgentRuntimeSlots.ps1 -Check` 违规 0 且**基线命中按序递减**（不得回涨）② 该序对应**同实例/幂等**断言（槽内实例 ≡ 注入实例）③ `Agent.sln` 0 错 0 警 + 相关测试工程零回归 |
| **回归面** | UI 函数库（`AgentUiLibrary*`）、MCP 工具面、Cline 链（含进程资源释放顺序）、会话装配链 |
| **回退** | **逐序独立提交**，单序可撤；未收敛序保持基线（门禁不判失败） |
| **风险** | 高（序 9 含进程资源；序 7 含生命周期句柄）⇒ 序 9 单独评审 |
| **注** | 本序完成后可解 **W-DEC-5**（对象槽产物的新代数 ALC 重建）——该残项此前一直以"依赖 Batch 3"为由挂起 |

### 3.3 W2 · 双写收敛（上级 S3 / 分离方案 Batch 4；**风险最高，单独评审**）

| 项 | 内容 |
|---|---|
| **目标** | 消除两类双写/漂移对，使权威单一（设计文档 §1.4） |
| **收敛点 1（实测）** | `vars`(Transient) ↔ `ur_nodes`：`Agent.Core\Nodes\NodeExecutionService.cs:254 vars.Set(key, v)` 与 `:265 PersistAsync`（内部 `:325 _store.WriteOutputAsync`）= 双写 → 写侧收敛为「**单写 + 投影重算**」（读侧 ur_nodes 优先已具备） |
| **收敛点 2（实测）** | `AgentTaskItem.PayloadJson` ↔ `state.Data`：`Agent.Core\Nodes\RoundStateMapper.cs:117 Harvest` → `:124 state.Data[key] = v!` 二次收割 → 以**统一任务快照为唯一权威根**（唯一写入口 `AgentRunner.Persistence.cs`），`state.Data` 降为缓存/投影（`:132` 注释已声明 ur_nodes 权威，需把 `ExecResult` 之外键一并归类） |
| **开关（W2-0）** | `AgentConfig` 布尔，**默认关**，注入链同 `EnableTaskPool`（`Agent.McpRegistryHost\Configuration\AgentConfig.cs`） |
| **步骤** | W2-0 开关；W2-1 收敛点 1（`NodeExecutionService`）；W2-2 收敛点 2（`RoundStateMapper`）——**各自独立提交**（沿用上级 S3-1..S3-3 口径） |
| **判据** | ① 开关**关** = 旧路径行为等价（13 测试工程全绿）② 开关**开** = 无漂移：故障注入（写后崩溃）后重启，`state.Data` 与 `PayloadJson` 不冲突 ③ `NodeGraphReconciler` 跨介质缺口 0 ④ 两后端（Sqlite / 内存占位）分别验证 |
| **落盘迁移** | **不做**（域别名表已落地；改名方案仅备选） |
| **回退** | 开关关闭即回旧路径；本批**独立提交、不与其它批次混** |
| **风险** | 高（触写热路径）⇒ 独立开关 + 故障注入证据 + 单独评审 |

### 3.4 W3 · 投影与观测统一（上级 S1-④ + S2 / 分离方案 Batch 5）

| 步 | 内容 | 判据 |
|---|---|---|
| **W3-1** 对象槽只读投影（S1-④） | ① `Agent.DataCenter` 工程（命名空间 `Agent.StateDomain`）增 `IRuntimeSlotEntrySource : IStateEntrySource`：读 `root.Slots.Partition(AgentRuntimeSlotKeys.Domain)` **只读快照** → `StateEntry`（nodeKind `slot.snapshot` 已预登记）② 快照提供者按槽类型各出一行（LLM 路由模式/后端清单、技能热集计数、工作流编译缓存键），**只含快照值、不含对象引用** | ① 投影幂等（两次 `ToJson` 全等，**时间戳不进产物**）② K5 未注册 Kind = 0 ③ 投影失败不升级为状态写失败 |
| **W3-2** T4 观测内核下沉（Batch 5b） | Frame 通用层（`Modules`/`BasicMethod`）+ `observability_semantics.json` 契约格式与加载器；Agent 四族契约实例化 | `WHERE ScopeId=RunId` 一次查全「提议→裁决→工具命中→落库」 |
| **W3-3a** writer 身份常量表（**可立即，无依赖**） | Agent 侧 `AgentRunner` / `NodeExecutionService` / `RoundStateMapper` 的 writer 名称常量（与 `Check-SingleWriter` 的 `AssertSingleWriter` 固定写者名口径统一） | 常量集与盖章点一一对应（编译期/单测断言） |
| **W3-3** 追踪面（S2，**当前阻塞**） | ① 前置 = 框架侧 D2（`TraceStamp` 值类型 / schema 扩 `seqRange/writers/assertion` / `StateCenter.Write` 统一盖 `writer/at/seq`）与 D3（`ITraceQuery`）② Protocol `IDataCenterRoot` 增 `ITraceQuery Trace`（**实测现无此成员**）③ 盖章接入：`NodeExecutionService` / `RoundStateMapper` / `AgentRunner`（`writer` 由域显式传入）④ 域侧查询接入（`Ancestors` / `ReverseRefs` / `Validate`） | K7 追踪四要素（`writer/at/seq/causedBy`，`causedBy` 允许 null 但须显式声明为根）缺失 0；`Ancestors(终答节点)` 可闭合 |
| **回退** | 只读旁路，撤出不影响写入与执行 |
| **风险** | 中低；W3-3 受框架侧排期约束（阻塞项，不得以 Agent 侧变通绕行） |

### 3.5 W4 · 通用机制下沉残余（分离方案 Batch 2；T3 已完成）

| 步 | 内容 | 判据 |
|---|---|---|
| **W4-1** T1 取根门面下沉 | 取根门面下沉 Frame 通用件（单通道，去 `AmbientSlot` / 进程字段叠加）；保留旧签名重载做过渡 | Frame 侧**不引任何 Agent 类型**即编译通过；`DataCenter.Tests` / `Agent.*.Tests` 全绿 |
| **W4-2** T2 槽实现合并 | 三套槽实现合并为一份（同键同实例幂等 / 同键异实例 fail-fast 语义统一） | 同上 + 槽语义断言；消费方零改动（`Agent.Contract` 引用面） |
| **回退** | 每步独立提交；通用件保留旧签名重载 |
| **风险** | 中（引用面在 `Agent.Contract`） |

### 3.6 W5 · 通用性回灌验证（分离方案 Batch 6；"通用"的唯一可证伪证据）

| 项 | 内容 |
|---|---|
| **动作** | 让**第二个消费方**（`Engine` / `FrontEndDriven` 的 `EngineStateCenter` / `StateCenterFactory`）复用同一根 / 槽 / 观测内核 |
| **判据** | 一个**不引用任何 Agent 类型**的最小应用：注册契约 + 埋点 + Timeline 查询跑通 |
| **意义** | 单消费方无法证明通用；此步是"通用性"从声明变事实的分界 |
| **依赖** | W1（槽归零）/ W3（观测内核）/ W4（通用件落点稳定） |
| **风险** | 中（跨产品引用面） |

### 3.7 W6 · 收尾（上级 S5 / 分离方案 Batch 7）

| # | 动作 | 落点 |
|---|---|---|
| W6-1 | 文档回写：设计文档 §5 门禁表补实测、§8 槽登记表勾状态；分离方案 §4.2 台账勾状态 | 设计文档 / 分离方案 |
| W6-2 | 基线**归零后清空**：`Check-AgentRuntimeSlots.ps1 $baseline` · `Check-DataCenterGenericity.ps1 $k9Whitelist.Pending` · `Check-AgentDataSeparation.ps1 $frozen` · `data-ownership.json frozenBaseline` | 三个脚本 + 台账 |
| W6-3 | **可换代域清单**成文（默认 ALC 不可换 vs scoped ALC 可换） | 设计文档 / 分离方案 §9 |
| W6-4 | 四条接入通道（A 状态 KV / B 对象槽 / C 投影 / D 域真源）验收小结 | 进展台账 §2 |
| W6-5 | 既有缺陷修复（独立提交）：`Share.Tsets\Registry.Tests\AutoRegisterTests.cs` 的 `Share.Registry.` → `Modules.Registry.`，随后纳入回归集 | 主仓 |
| W6-6 | 台账 11 条 `pending` 全部转 `registered`（批次落地后状态回标） | `data-ownership.json` + 台账 |
| **判据** | ① 三脚本 `-Check` 全 EXIT 0（冻结段清空后）② 13 测试工程全绿 ③ 台账 `pending` = 0 |
| **风险** | 低 |

---

## §4 判据命令清单（复制即用）

```powershell
# ── 构建 ──
dotnet build 'D:\Project\Main\Projects\Plug\Agent\Agent.sln' -v q --nologo
dotnet build 'D:\Project\Main\Projects\PrjBridge\PrjBridge.csproj' -v q --nologo

# ── 回归（基线见 §0.2）──
cd 'D:\Project\Main\Environment\CentralTest\AgentTest'
foreach ($p in @('Agent.Core.Tests','Agent.Axioms.Tests','Agent.Launcher.Tests','Agent.Runtime.Tests',
                 'Agent.Persistence.Tests','Agent.Session.Tests','Agent.Modules.Tests','Agent.Operation.Tests',
                 'Agent.Observe.Tests','Agent.Guardrails.Tests','Agent.Api.Tests','Agent.Api.Contract.Tests')) {
  $r = (dotnet test "$p\$p.csproj" -v q --nologo 2>&1 | Select-String -Pattern '已通过!|失败:|error CS' | Select-Object -Last 1) -join ' '
  Write-Output "$p => $r"
}
cd 'D:\Project\Main\Environment\CentralTest\CSharpTest\Operation.Tests\DataCenter.Tests'; dotnet test -v q --nologo

# ── 门禁（五道；W0 后应全 EXIT 0）──
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-DataCenterGenericity.ps1'
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-SingleWriter.ps1'
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-AgentRuntimeSlots.ps1' -Check
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-AgentDecisionCoreBoundary.ps1' -Check
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-AgentDataSeparation.ps1' -Check

# ── 取证（只读，用于 W1/W2 改动前定位）──
$all = (Get-ChildItem 'D:\Project\Main\Projects' -Recurse -File -Include *.cs,*.ps1,*.md |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' }).FullName
Select-String -Path $all -Pattern 'vars\.Set|PersistAsync|Harvest|state\.Data\['           # W2 收敛点
Select-String -Path $all -Pattern 'NodeTypes\.Output|AgentRuntimeSlotKeys\.'               # 形态与槽键面
& 'D:\Project\Main\Frame\PowerShellScript\Operation\DSL\Check-AgentRuntimeSlots.ps1'       # W1 基线逐序核对
```

---

## §5 提交与验证纪律（沿用于上级 §4/§5）

| 场景 | 动作 |
|---|---|
| 每子项 | **一处变更 + 一条最小断言**；W1 逐序、W2 逐收敛点独立提交（不与其它批次混） |
| 批末 | 跑 §4 全集（构建 + 13 回归 + 5 门禁） |
| 契约变更 | 必须刷新烘焙副本（设计文档 §4.2）；本文件各子项**默认不改契约**（W3-3 例外：Protocol 增 `Trace` 成员） |
| 文档回写 | 子项完成后回写**缺陷清单**对应条目 + 台账 `data-ownership.json` 状态列 + 上级计划 §0 基线 |
| 决策项 | W-DEC 采纳后须在本文件 §2 标注"已采纳（日期）"，并在上级计划 §2 决策点表登记一行 |
| 取证项 | 结论写入缺陷清单"影响/状态"列（含符号 + 行号），不得只留在会话记录 |

---

## §6 风险与回退一览

| 批次 | 风险 | 回退方式 |
|---|---|---|
| W0 | 低（纯台账/脚本） | 单提交可撤 |
| W1 序 1–3 | 低 | 逐序单提交 |
| W1 序 4–7 | 中 | 逐序单提交；槽未收敛即保持基线 |
| W1 序 9 | **高**（进程资源 / 释放顺序） | 逐序单提交 + 单独评审；失败即 `git revert` 该序 |
| W2 | **最高**（写热路径） | 开关默认关 ⇒ 关即回旧路径；独立提交、不与其它批次混 |
| W3 | 中低（只读旁路） | 撤出不影响写入与执行；W3-3 阻塞在框架侧，不做 Agent 侧变通 |
| W4 | 中（`Agent.Contract` 引用面） | 每步独立提交；通用件保留旧签名重载过渡 |
| W5 | 中（跨产品） | 新消费方独立工程，撤出即回退 |
| W6 | 低 | 文档 + 基线表清空可撤 |

**总回退纪律**（沿用上级）：任一子项若触发判据失败且 30 分钟内无法定位 → **立即回退该子项**（`git revert` / 撤销提交），不在同一提交内叠加修复。

---

## §7 诚实边界

| # | 说明 |
|---|---|
| 1 | **业务真源不搬**：`ur_tasks` / `ur_nodes` / `ur_histories` / `graph-log.db` / `agent.db` 按设计属通道 D（**只读接入 + 边类型映射归一**），DataCenter 不做其写路径 owner —— 本计划完成后，这些真源**仍由 Agent 侧存储栈写**，这是设计口径而非欠账 |
| 2 | **不承诺全栈热更**：默认 ALC 能解析的一律复用默认副本；`WorkFlowMethod` / `Share.*` / `Plate.*` / UI / native 按设计不可换；验收口径 = **实现域热更新 + 数据跨代连续** |
| 3 | **跨 ALC 边界**：根 / 真源 / 单根守卫必须由默认 ALC 承载；对象槽跨代复用句柄/委托会钉住旧 ALC（策略固定"重建 + 状态迁移"） |
| 4 | **W3-3 受外部排期约束**：`IDataCenterRoot.Trace` 依赖框架侧 D2/D3；等待期只做无依赖项（W3-3a），不得以 Agent 侧变通绕行 |
| 5 | **E 类不接入**（设计文档 §1.5）：`RunContext`/AsyncLocal 执行栈、UI 控件/ECS/native、进程外资源（Cline daemon / LocalLlmHttp / 凭据目录）、`Modules.Threading.AmbientSlot`（机制非数据） |
| 6 | **基线会漂移**：§0.2 为 2026-09-12 实测；每次开工前须重新实测并更新 |
| 7 | **本计划不含缺陷事实**：位置/证据/严重度一律以缺陷清单为准；W-DEC 未标注"已采纳"前视为**未决** |

---

## §8 建议执行顺序

```
[可立即，零/低风险]
  ├─ W0（台账登记归零 + 冻结值同步）                    ──► 门禁 D1–D5 复归 EXIT 0
  ├─ W3-1（对象槽只读投影，纯新增）                      ┐
  ├─ W3-3a（writer 身份常量表）                          ├─► 只读旁路，可任意穿插
  └─ W1 序 1 → 2 → 3（低风险静态槽）

[主力推进]
  W1 序 4 → 5 → 6 → 7 → 9（逐序独立提交；序 9 单独评审）
      └─► 归零后：清 $baseline / frozenBaseline（W6-2 前置）
      └─► 解 W-DEC-5：对象槽产物新代数 ALC 重建（单独立批）

[最高风险，单独评审]
  W2-0 开关 ► W2-1 收敛点1 ► W2-2 收敛点2（含故障注入证据）

[框架侧就绪后]
  W3-2（T4 观测内核）► W3-3（追踪面）► W-DEC-2（观测源唯一化）
  W4-1（T1 取根门面）► W4-2（T2 槽实现合并）

[收尾]
  W5（第二消费方回灌验证）► W6（文档 / 基线归零 / 可换域清单 / 四通道验收 / 既有缺陷修复）
```

**批末统一动作**：① §4 全集实测；② 回写缺陷清单对应条目 + 台账状态列；③ 同步上级计划 §0 基线；④ 若基线数值变化，更新本文件 §0.2。




