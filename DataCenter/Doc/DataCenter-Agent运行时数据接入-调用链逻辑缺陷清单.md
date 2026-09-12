# DataCenter · Agent 运行时数据接入 —— 调用链逻辑缺陷清单（重复 / 冗余 / 错误）

> **日期**：2026-09-12（S4-2 完成后审计） | **基线提交**：Frame `640e1fc` · Projects `32f5043` · 主仓 `dbb3437`
> **权威边界**：本文件是**调用链逻辑缺陷（重复 / 冗余 / 错误）的唯一台账**（发现 / 证据 / 严重度 / 修复归口 / 状态）；
> 设计语义以 `DataCenter-Agent运行时数据接入-设计与实施.md` 为唯一权威，"下一步怎么做"以 `DataCenter-Agent运行时数据接入-下一步实施计划.md` 为唯一权威；
> **已完成阶段的实测记录**仍见 `DataCenter-Agent运行时数据接入-实施进展与后续.md`。
> **通则**（沿用设计文档 §4）：每步独立可编译、可回退；判据必须可执行（命令 / 断言）；不改真源（SQL/LiteDB）、不改 CSG 主 schema。

## §0 口径与约定

| 约定 | 内容 |
|---|---|
| 编号 | `F-nn`（Finding，追加式；**一经编号不得复用、不得重排**）；修复后置 ✅ 并记录提交哈希，条目不删 |
| 证据等级 | `[已核]` = 已读源码核对行号；`[条件性]` = 需特定时序/并发才触发（未构造用例）；`[已登记]` = 文档已承认的既有待办（仅交叉引用，不重复展开方案） |
| 归属 | `(P)` 既有问题 · `(S4-2)` 本次 S4-2 引入或未覆盖 |
| 严重度 | **高** = 违反目标不变量或必然在典型路径触发；**中** = 特定路径触发或语义不成立；**中低** = 隐患/门禁弱化；**低** = 可维护性 |
| 类别 | 重复（同一职责多实现/多写点）· 冗余（无消费方/可删）· 错误（语义/契约违背） |

### 速览（按修复优先级）

| 优先级 | 条目 | 一句话 | 归口 |
|---|---|---|---|
| **P0** | F-16 · F-17 | ✅ 已修（B1）：换代窗口内取根/取注册表改为**非抛探测回落** | S4-3 前置 |
| **P0** | F-18 | ✅ 已修（B2）：换代未带指纹**沿用既有 fingerprint**（不再静默清空） | 契约口径修正 |
| **P1** | F-19 · F-21 | `Resume` 抛错**掩盖换代原始异常**；失败回建**不走装配入口**（"保留旧代数"在对象层不成立） | S4-2 补强 |
| **P1** | F-20 · F-04 | `Resolve` 并发注册竞态；`Register`/`Replace` 双语义并用 | Agent 取根面 |
| **P1** | F-24 | 编排器**无状态中心注入面** → 通道 A（状态 KV 接入）实际未接通 | ✅ 已修（S4-4a；原 S4-4 前置，拆两步已落地） |
| **P2** | F-01 · F-02 · F-03 · F-14 | 槽分区双实现 / 投影器装配重复 / 取根面双通道 / `Default` 每次新建注册表 | 收敛 |
| **P2** | F-09 ~ F-13 · F-15 | 无消费方公开面（`LiveCount` / 域校验 / `GenerationPayload` / `Swap` / 转发方法） | 收敛 |
| **P3** | F-05 · F-06 · F-07 · F-08 · F-22 · F-23 · F-25 · F-26 | 双造根（K9 待迁移）/ 双写键 / 重复判定 / 迁移期冗余 / 异常覆盖 / 门禁豁免过宽 / 惰性自举冲突 | 分散 |
| **P0** | F-33 · F-34 · F-35 | **第二轮（库内聚审计）**：契约-实现**父链回退矛盾** / 停机无异常隔离致**永久不可重启** / legacy 后端 **scope 分区落内存**（不持久化） | 批次 B10 · B11 · B12 |
| **P1** | F-36 · F-37 · F-38 | 第二轮：`Clear` 越前缀清空 + 双 `Reset` 同名 / `SwapAsync` 无并发与停机守卫 / 变更事件 `Tier` 在删除路径必失真 | 批次 B13 · B14 · B15 |
| **P2** | F-27 · F-30 | 第二轮：两套 Store 实现同构（**需先决策收敛方向**）/ 空端口与门面冗余面 | 批次 B16 |
| **P3** | F-28 · F-29 · F-31 · F-32 · F-39 | 第二轮：双公开面与双 `Reset` 命名 / 死分支与双重重载 / 适配层冗余 / 未接线统计与死锁字段 / 类型保真等小项 | 批次 B17 |
| **P1** | F-40 · F-41 | **第三轮（库内聚复审）**：`StateCenter.Shutdown` 无异常隔离致**停机后资源永久泄漏**；保留键 `__scope` 撞名致投影**静默丢值** | 批次 B18 · B19 |
| **P2** | F-42 · F-43 · F-44 · F-49 | 第三轮：注册表只读承诺被强转击穿 / `GetAll` 比较器口径不一致 / `Set` 非原子与槽工厂重入覆盖 / 订阅无退订 | 批次 B20 · B21 |
| **P3** | F-45 · F-46 · F-47 · F-48 | 第三轮：常量三处硬编码 / 决策载荷异常面与本地时间戳 / csproj 注释漂移 / `Decode` 零消费方 | 批次 B21 |
| **—** | F-50 · F-51 · F-52 · F-53 · F-54 | 第三轮**接受现状/已记录**（文档对齐 · 门禁判据不可用 · 无实现者 · 双实现收敛方向 · 架构约束） | 🟦 见 §7 |
| **P0** | F-55 · F-56 | **第四轮（Agent 数据流视角）**：轮内 `Transient` **越 scope 整层清空**（连带抹掉其它会话/轮的轮内状态）；根已引导而槽未登记真源 ⇒ 状态 KV **静默回落第二真源**（数据流分裂、投影不可见） | ✅ B22 · B23 |
| **P1** | F-57 · F-58 · F-59 · F-60 · F-68 · F-69 | 第四轮：装配时序（C 段固化实例 vs E 段登记 / 处置期写口做装配注册）；入参映射同语义两次套用；决策词双表写同一 `DecisionRecord.Word`；取根面 ambient+静态双通道；`state.Data` 同键双写点 | ✅ B24 · B25 |
| **P2** | F-61 · F-62 · F-63 · F-64 · F-65 · F-66 | 第四轮：死实现（`SnapshotSerializer`）；会话完成双 upsert；`ur_nodes` 写两形读一形 + Transient 双通道；观测事件双事实源；对象槽双真源；确定性核心双承载 | ⏸ B26 · B27（子计划） |
| **P3** | F-67 · F-70 | 第四轮：未落地槽键 / 空壳与同名族冗余面 | ⏸ B27（子计划） |

---

## §1 分析范围（四条调用链）

| 链 | 名称 | 入口 → 出口 |
|---|---|---|
| **链 1** | 根装配与取根 | `DataCenterHost.Boot` → `IDataCenterRoot`/`IRootProvider` → `AgentRuntimeRoot`（`Bind`/`Partition`/`Resolve`）→ 运行时槽分区 → Agent 侧静态门面转发 |
| **链 2** | 换代 | `DataCenterHost.SwapAsync`（Drain/Export/装配/Import/Resume）· `ExtensionGenerationFactory.Swap` · `ExtensionLoadContext.Unload` |
| **链 3** | Agent 组合根装配与槽发布 | `AgentFrameworkHosting.TryBootstrapGuiAsync` → `AgentComposition.StartAsync` → `AgentFramework.StartAsync` → `AgentFrameworkAssembler`（A–E 段）→ `AgentComposition.FromFramework` / `AgentRuntime.EnsureComplete` |
| **链 4** | 轮次状态写路径 | `WorkflowRunner` → `RoundStateMapper`（`Seed`/`Harvest`）→ `vars`(Transient) / `state.Data` → `AgentRunnerCore.StateCenter` |

> **未覆盖**：LLM 网关链、任务池/调度链、持久化骨干（SQLite/LiteDB）链、UI 函数库链（不在本工作线变更面内）。
>
> **第二轮（2026-09-12 追加）**：分析范围扩展为 `Frame\DataCenter` **库内聚审计**（对象 = 库内源码，而非上述四条调用链），条目续编 `F-27~F-39`；证据以**符号名 + 行号**联合定位（基线：Frame `4583c1c`）。
> 该轮为**只读审计 + 修复方案**（未改动产品代码）；与第一条链重叠的承载面（`IRootProvider`/`IRuntimeSlot` 多实现）交叉引用 F-01/F-15，**不另立编号**。
> 归属口径：`F-27~F-39` 全为 `(P)` **既有代码问题**（非本轮引入）；证据等级见各行标注（`[已核]` / `[条件性]`）。

---

## §2 重复逻辑（Repeat）

| # | 类别 | 位置（证据） | 现象 | 影响 / 严重度 | 状态 |
|---|---|---|---|---|---|
| **F-01** | 重复 | `HostRuntimeSlot.SlotPartition`（`Frame\DataCenter\DataCenter.Hosting\Hosting\HostRuntimeSlot.cs:66-187`）vs `AgentRuntimeRoot.LocalPartition`（`Projects\Plug\Agent\src\Agent.Contract\Runtime\AgentRuntimeRoot.cs:111-162`）`[已核]` | 两套 `IRuntimeSlotPartition` 实现逐行同构（`Register`/`Replace`/`TryGet`/`Get`），**且已语义漂移**：Host 版维护 `_order` 注册序 + 处置期逆序释放，Local 版无注册序、不释放 | 违背 `Strategy.SingleImplementation`；两套语义各自演进必然分叉（谁是真行为不可判） | ⏸ 待收敛 |
| **F-02** | 重复 | `DataCenterHost.cs:115`（`Boot`）与 `:254`（`BuildRoot`）`[已收敛 T3]` | 投影器装配表达式重复两次：`new TierStateProjector(new StateCenterEntrySource(_center), _registry, _codec, generation)` | 首建/换代两条路径的投影器口径可能不一致（改一处漏一处）；建议抽 `CreateProjector(generation)` | ✅ 已收敛（T3：单一 `CreateProjector` 工厂 + 公开 `CreateCoreRoot(generation)` 供宿主 `RootFactory` 复用；判据 `DataCenter.Tests` 59/59，新增「首建/换代同投影器实例」断言，宿主侧重复表达式已消除） |
| **F-03** | 重复 | `AgentRuntimeRoot.cs:25`（`ProviderSlot`）· `:32`（`Binding.Value.Provider`）· `:58`（`CurrentProvider => ProviderSlot.Current ?? Volatile.Read(...)`）`[已核]` | 取根面**双通道承载同一 provider**：AmbientSlot（逻辑链级） + 进程级字段；`:44` `Enter` 返回的句柄被丢弃（"设置语义"） | 两个"真源"叠加；AmbientSlot 值沿逻辑链泄漏且无人 `Restore`；语义边界仅靠注释 | ⏸ 待收敛 |
| **F-04** | 重复 | `AgentComposition.cs:111`（`partition.Replace`）vs `AgentRuntimeRoot.cs:77`（`partition.Register`）`[已核]` | 同一分区**两套写语义并用**：装配期 E 段用"覆盖式替换"，取根解析用"同键异实例即抛" | 同一动作在不同路径一覆盖一抛错；与 F-20 叠加即"并发首访炸"。建议统一为 `GetOrAdd` + 显式 `Replace` 仅限处置期 | ⏸ 待收敛 |
| **F-05** | 重复 | `AgentOrchestrator.cs:93` 与 `WorkflowRunner.cs:89` `[已收敛 S4-4a]` | 同一职责（"轮次状态真源"）两处构造 `StateCenter`（即 K9 待迁移 2 项） | 生产路径实际用 orchestrator 的（`AgentRunner.cs:133` 显式 `center: core.StateCenter`）→ `WorkflowRunner` 的 `?? new StateCenter(...)` 属**无生产调用者的兜底构造**；但仍是"静默第二真源"的入口面 | ✅ 已收敛（S4-4a：两处改经 `AgentRuntimeRoot.ResolveStateCenter()` 单一解析，K9 待迁移归零） |
| **F-06** | 重复 | `RoundStateMapper.cs:135`（`Harvest` 白名单含 `RoundKeys.SessionId`）与 `WorkflowRunner.cs:252`（`state.Data[RoundKeys.SessionId] = sid`）`[已核]` | `state.Data[SessionId]` **两个写点**（`ExecResult` 已在 `RoundStateMapper.cs:131-132` 收敛为单点，`SessionId` 漏收敛） | 双写漂移面（写入时序不同即出现"最后写者赢"）；`S3`（A4）收敛点 2 的一部分 | ⏸ S3 |
| **F-07** | 重复 | `ExtensionGenerationHost.cs:99-107` 与 `ExtensionLoadContext.cs:329-333 / :342` `[已核]` | "默认 ALC 已有同名程序集则复用"判定做了**两遍**：外层 `Load` 先经 `LoadFromAssemblyName` 触发 ALC，而 ALC 内部（`ExtensionLoadContext.Load`）自己又遍历 `Default.Assemblies` + `Default.LoadFromAssemblyName` | 冗余遍历（每次装载多扫一遍默认 ALC 程序集表）；两处判定未来可能给出不同答案 | ⏸ 待收敛 |
| **F-08** | 重复 `[已登记]` | `AgentFramework.cs:178-180`（`Attach` + `AttachTaskPool`）· `AgentComposition.cs:64`（`AttachDatabase`）· `AgentFrameworkHosting.cs:43-67`（再 `EnsureComplete`）`[已核]` | 三条装配入口分层"薄委托"，各自附带一次槽发布/对账 | 代码自注为 **B4-2 迁移期**（B4-3..B4-6 待收敛）；当前无功能错误，属结构性重复 | ⏸ 迁移期 |
| **F-27** | 重复 | `DataCenter.Core\Storage\TierStringStore.cs:27-95` vs `Share\Foundations\StateManagement\StateStorage\Core\StorageCellStateStore.cs:39-124`；`TierObjectStore.cs` vs `StateStorage\Core\ObjectKeyValueStore.cs` `[已核]` | 端口侧 Store 与 Share 侧 Store **逐方法同构**（`Set/Get/Has/Clear/GetAllKeys/GetOrAdd/Remove/RemoveByPrefix/SetRange/GetAll/Count/IsEmpty` + `_excludedKeyPrefix` 同形），且**两份都在活跃使用**（旧实现消费方 `StateManager.cs:24/39`） | 违背 `Strategy.SingleImplementation`：改一处漏一处（F-36 的 `Clear` 越前缀缺陷即被原样复制）。**收敛方向需先决策**（Core 仅依赖端口 vs 复用 Share 实现），故列 B16 前置决策项 | ⏸ P2（B16） |
| **F-28** | 重复 | `StateCenter.cs:77`（`GlobalStore`）vs `:206`（`Store`）；`StateCenter.cs:75` vs `StateStore.cs:58`（两个独立 `DataCellStorageView` 缓存）；`StateStore.Reset()`（`:74-82`）vs `StateCenter.Reset()`（`:219`→`StateOrchestrator.Reset()`→`Store.Clear()`）`[已核]` | ① 同一真源两个公开属性（`IStateManager.GlobalStore` / `IStateOrchestrator.Store`）+ 两个视图实例；② **同名 `Reset` 两义**（仅对象态 vs 清整层 Session） | 服务定位器语义回潮；按名字推断语义必错（与 F-36 同批，重命名区分） | ⏸ P3（B17） |
| **F-29** | 重复 | `LayeredStoreClient.cs:30-32`（兜底分支 `new StorageCellAdapter(center.Cell)`）· `DataCenterHost.cs:227-235`（`CreateProjector` 静态+实例双重重载）`[已核]` | ① 端口后端下 `center.Cell` 已是 `DataCellStorageView` ⇒ 兜底形成 `IStorageCell→IDataCell→IStorageCell` 双层往返；且 `StateCenter` 恒实现 `IDataCellProvider`（`:27`）⇒ **该分支库内不可达**；② 实例版重载仅做参数透传 | 死分支 + 双重适配反模式；薄包装叠层 | ⏸ P3（B17） |

---

## §3 冗余逻辑（Redundant）

| # | 类别 | 位置（证据） | 现象 | 影响 / 严重度 | 状态 |
|---|---|---|---|---|---|
| **F-09** | 冗余 | `Frame\DataCenter\DataCenter.Core\Roots\RootInstanceGuard.cs:14`（`internal static int LiveCount`）`[已核]` | 公开诊断面**零消费方**（注释称"诊断/判据用"，全库无引用） | `Strategy.RedundancyElimination` 判据（¬∃caller→redundant）成立；建议删除或补一条装配期断言使其有实义 | ⏸ |
| **F-10** | 冗余 | `AgentStateDomain.cs:60`（`MissingRegistrations`）· `:88`（`IsFullyRegistered`）`[已核]` | 域登记校验函数**零消费方** → **K5（Kind 注册完备）在装配期并未真正执行**，仅靠 `DataCenter.Tests\AgentStateDomainTests` 事后断言 | 冗余 + 判据虚化：装配期自检缺失（域分裂只能在测试里发现） | ✅ 已接线（S4-3：`AgentRuntimeRootBootstrap` 引导前 `IsFullyRegistered` 自检 + `Agent.Launcher.Tests` 断言） |
| **F-11** | 冗余 `(S4-2)` | `GenerationPayload`（`Frame\DataCenter\DataCenter.Hosting\Hosting\GenerationPayload.cs`，消费点仅 `DataCenterHost.cs:178`）`[已核]` | 跨代载荷**无真实生产/消费方**（`Export`/`Import` 钩子目前仅测试使用）；`Empty` 与 `new GenerationPayload()` 等价 | 预置空壳抽象：若 S4-3 仍无消费方，应显式收窄（明确唯一用途）或降级为内部类型，避免"看起来有机制"的误导 | ⏸ S4-3 判定 |
| **F-12** | 冗余 | `ExtensionGenerationFactory.Swap`（`Frame\CsharpPrj\Modules\Registry\ExtensionGenerationHost.cs:267`）`[已核]` | **零生产调用方**（仅 `Integration.Tests\ExtensionGenerationTests` 使用） | S4-1 交付未接线；S4-3 已消费 `Generation`（根代际 ↔ 扩展代数对齐），`Swap` 仍未接线（留待换代闭环批次） | ✅ 已接线（S4-4b/1d：`AgentRuntimeRootBootstrap.SwapAsync` 生产调用 → 扩展代数与数据代数同序推进；宿主触发点 `AgentSelfHostPlugin.SwapGenerationAsync`） |
| **F-13** | 冗余 | `AgentRuntimeRoot.cs:89`（`ResetForTesting() => Unbind()`）`[已核]` | 纯转发方法（与 `Unbind` 完全等价） | 低；可并入测试夹具 | ⏸ 低 |
| **F-14** | 冗余 | `AgentStateDomain.cs:51`（`Default => Apply(GraphRegistry.CreateDefault())`）`[已核]` | 每次属性访问都**新建并重建注册表**；`Resolve`（`:57`）在未绑定时亦每次新建 | 破坏"注册表同源/同实例"前提（`ReferenceEquals` 判据不可用）+ 重复建表开销 | ⏸ 建议惰性单例 |
| **F-15** | 冗余 | `IRuntimeSlot` 三实现（`HostRuntimeSlot` / `LocalRuntimeSlot`（`AgentRuntimeRoot.cs:102`）/ `EmptyRuntimeSlot`）+ 两个 cell 工厂（`DataCenterHostOptions.RootCellFactory` / `PartitionCellFactory`，`DataCenterHost.cs:113-114`）`[已核]` | 承载面重叠，职责边界仅靠注释区分（"哪个是真路径"不可从类型判断） | 与 F-01 同源；增加新宿主时的选择歧义 | ⏸ 待收敛 |
| **F-30** | 冗余 | `DataCenter.Abstractions\Dispatch\IStateDispatch.cs:7`（`Frame`/`Projects`/`Environment` 全域零引用）· `DataCenter.Core\Roots\RootProvider.cs`（生产零 `new`，仅测试 `DataCenterRootTests.cs:75/91`、`AgentFrameworkInjectedRootTests.cs:41`）· `Projection\JsonValueCodec.cs:31`（`Decode` 恒等直通）· `Cell\IDataCell.cs:22`（`SetObject` 为 `Set<object?>` 别名）`[已核]` | 端口/门面冗余面：`IStateDispatch` 为空端口（实际派发用 `Share.StateManagement.IPublishDispatcher`，`StateCenter.cs:41/56`）；`RootProvider`+`UnboundRootProvider` 测试专用；`Decode` 逆语义空实现；`SetObject` 仅适配器往返需要 | `Strategy.RedundancyElimination` 判据成立（¬∃caller→redundant）；与 F-11 同批处理 | ⏸ P2（B16） |
| **F-31** | 冗余 | `Compat\StorageCellAdapter.cs:29`（`_parentAdapter ??=` 非原子且无退订）· `:71-75`（`ProbeTier` 每次变更 3 次 `Contains`）· `Compat\DataCellStorageView.cs:78`（`GetSnapshotBehavior` 恒 null）`[已核]` | 兼容适配层：适配器与 inner 同寿、无退订 ⇒ 并发下多重订阅（事件放大）；写路径 3 次探测仅为一个诊断字段；引擎快照语义在端口视图上静默失效（`SnapshotManager` 经视图即无快照） | 写路径开销 + 静默降级；层信息来源与 F-38 同源 ⇒ 同批 | ⏸ P3（B17） |
| **F-32** | 冗余 | `Projection\TierStateProjector.cs:184-186`（`unparsedKeys`/`unregisteredTierEntries`/`filteredByTier` 仅写 metadata）· `Registry\GraphRegistry.cs:41/48/54`（`IReadOnlyCollection` 实为数组）· `Extensions.LoopDecision\LoopStateStore.cs:59-98`（`View`/`LoadWorking` 各读 6 键 + `HasPersisted` 再加 1）· `Core\StateStore.cs:27`（`_lock` 仅被 `Reset` 空锁块引用）`[已核]` | 报缺口无门禁消费（设计文档 §5.6 检测点未接线）；只读承诺可被强转改写；同一快照 2× 读放大；死字段 + 装饰性临界区（锁内无语句） | 低（可维护性/微弱开销） | ⏸ P3（B17） |
| **F-47** | 冗余 | `DataCenter.Abstractions\DataCenter.Abstractions.csproj:6` · `DataCenter.Core\DataCenter.Core.csproj:15,52`（注释仍宣称端口 `IStateDispatch`）`[已核]` | 注释/规则清单仍宣称**已删除**的 `IStateDispatch` 端口（`Abstractions\Dispatch\` 目录已不存在） | 文档-现实漂移（新宿主按注释找端口必扑空）；F-30 收尾遗漏 | ✅ 已修（B21：三处注释同步） |
| **F-48** | 冗余 | `DataCenter.Protocol\Projection\IStateEntrySource.cs:48`（`IStateValueCodec.Decode`）+ `Core\Projection\JsonValueCodec.cs:31`（恒等直通）`[已核]` | 全库检索 `.Decode(` **0 处调用**（含测试）→ 空实现端口面 | `Strategy.RedundancyElimination` 判据成立（¬∃caller→redundant） | ✅ 已修（B21：删除 `Decode`，仅保留 `Encode` 契约面） |
| **F-51** | 冗余 | `Projection\TierStateProjector.cs` metadata 缺口计数（`unparsedKeys`/`unregisteredTierEntries`/`filteredByTier`/`duplicateKeys`）`[已核]` | 缺口计数**只写 metadata、未接门禁** | 低；且**不可直接接 fail-fast 门禁**：对象 KV 键（`_obj:`）合法不可解析 ⇒ `unparsedKeys>0` 属正常态。本轮新增的 `duplicateKeys` 是唯一可直接判零的缺口面 | 🟦 接受现状（记录判据边界；F-41 测试已断言 `duplicateKeys`） |

---

## §4 错误逻辑（Erroneous）

| # | 位置（证据） | 问题 | 严重度 | 状态 |
|---|---|---|---|---|
| **F-16** | `AgentRuntimeRoot.cs:61`（`CurrentRoot => CurrentProvider?.Root`）+ `HostRootProvider.cs:16`（`Root => ... ?? throw RootMissingException.Throw(...)`）；窗口在 `DataCenterHost.cs:166-175` `[已核]` | `?.` 只防 `provider` 为 `null`，**不防"已注入但处于 Unbind 窗口"** → 换代期间 `IsBound`/`Partition`/`Resolve`/`TryResolve` 全部抛 `RootMissingException`，**而非类注释承诺的"回落进程本地容器"** | **高** | ✅ 已修（B1，2026-09-12；非抛探测 `IRootProvider.TryGetRoot` + `AgentRuntimeRoot` 回落；判据 `DataCenter.Tests` 56/56、`Agent.Launcher.Tests` 28/28） |
| **F-17** | `AgentStateDomain.cs:57`（`Resolve(provider) => provider?.Root.Registry ?? Default`）`[已核]` | 与 F-16 同型：provider 注入但未绑定时抛异常，落不到 `Default` | 中 | ✅ 已修（B1，2026-09-12；`AgentStateDomain.Resolve` 经非抛探测回落 `Default`；判据 `DataCenter.Tests` 56/56） |
| **F-18** | `DataCenterHost.cs:288`（`state.Next(plan.Fingerprint)`）+ `GenerationState.cs:18-19`（`Next(fingerprint = null)` 语义为**赋值**）`[已核]` | 换代未提供指纹 → **既有 `fingerprint` 被静默清空**；与 `GenerationPlan` 注释「null = 保持未计算」矛盾 → 世代指纹不可回溯（违反 `AX-CORE.InformationMonotonicity` 精神） | 中 | ✅ 已修（B2，2026-09-12；`Advance` 改取 `plan.Fingerprint ?? state.Fingerprint`；判据 `SwapAsync_WithoutFingerprint_PreservesPreviousFingerprint`） |
| **F-19** | `DataCenterHost.cs:193-197`（`finally { await plan.Resume(CancellationToken.None) }`）`[已核]` `(S4-2)` | `Resume` 自身抛错**覆盖** Drain/Export/装配/Import 的原始异常 → 换代失败根因被替换（错误处理逻辑瑕疵） | 中 | ⏸ P1 |
| **F-20** | `AgentRuntimeRoot.cs:74-78`（`TryGet` 未命中 → `partition.Register(key, factory())`）+ `HostRuntimeSlot.cs:93-100` / `AgentRuntimeRoot.cs:124-129`（冲突即抛）`[已核]` | **并发首访竞态**：两个线程同时 `Resolve` 同一键 → 后者 `Register` 抛 `InvalidOperationException`，而不是拿到既有实例 | 中 | ⏸ P1 |
| **F-21** | `DataCenterHost.cs:185-190`（失败回建走 `BuildRoot(previousGeneration)` 单参重载）`[已核]` `(S4-2)` | 回建**不调用 `RootFactory`** → 回建的是"默认 ALC 根"而非"旧代数的装配入口产物" ⇒ **「换代失败保留旧代数」在对象层并不成立**（当前判据只覆盖"值不丢 / 代际不前进"） | 中 | ⏸ P1（语义缺口，需回写文档） |
| **F-22** | `DataCenterHost.cs:182-191` `[条件性]` | 若回建 `BuildRoot(previousGeneration)` 自身抛错（守卫被占用等），`_root` 仍指向已 `Dispose` 的旧根、`_generation` 未回滚 → `Lifetime` 抛 `ObjectDisposedException`、`Root` 抛 `RootMissingException`，**无显式降级态且错误信息误导** | 中低 | ⏸ P3 |
| **F-23** | `ExtensionGenerationHost.cs:340-347`（只捕 `FileNotFoundException` / `BadImageFormatException`）`[已核]` | 未捕 `FileLoadException` / `IOException` → scoped ALC 装载期异常可冒泡；`ResolveType`/`Load` 有外层兜底，但 **ALC 装载上下文（`AssemblyResolve` 路径）无兜底** | 中低 | ⏸ P3 |
| **F-24** | `AgentOrchestrator.cs:59/93`（`private readonly StateCenter _stateCenter` 仅 ctor 内构造，**无 `SetStateCenter`/构造参数注入面**；消费点仅 `:117 new AgentRunnerCore(_loopConfig, _stateCenter)`）`[已修 S4-4a]` | 即便宿主已持根，编排器仍用私有 `StorageCell("agent-orchestrator")` ⇒ **通道 A（状态 KV 接入）在编排器路径上实际未接通**（A1/A2 只把对象槽挂根，状态真源没挂） | **高** | ✅ 已修（S4-4a：`AgentOrchestrator(IAgentLoopConfig, StateCenter?)` 注入面 + 只读 `StateCenter` 属性；槽键 `state-center` + E 段登记宿主真源；未注入 → 显式回落。判据：`Agent.Core.Tests` 399/399 含 3 项注入面断言） |
| **F-25** | `Check-DataCenterGenericity.ps1:200`（`if ($name -like 'static*' -and ($trimmed -match '=>' -or $trimmed -match '\(')) { continue }`）`[已核]` | K9 豁免规则过宽：命中行只要含 `(` 或 `=>` 即跳过 ⇒ `static IRootProvider p = Make();` 之类**可绕过门禁** | 中低（门禁） | ⏸ P3 |
| **F-26** | `AgentUiHost.cs:102-110`（`AttachDatabase` → `:107 EnsureSingleGraphOwnerLocked`）+ `:164-170`（比对 `_referenceGrounding.Graph` 与 `dbResources`）+ `:350-360`（惰性 DB-only 自举）`[条件性]` | **惰性 DB 自举与全框架装配的图 owner 冲突**：若"引用守卫已挂（`AgentComposition.cs:217`）但 `State.Database` 仍为空"的窗口内发生惰性自举，新模块自带**另一个** coordinator ⇒ 断言抛"图库分裂"，而真实原因是装配半途 | 中 | ⏸ P3（需构造用例确认） |
| **F-33** | `Abstractions\Cells\IDataCell.cs:34,37`（契约\"存在性探测（含父链回退）\"/\"移除（含父链回退定位）\"）vs `Core\Cells\InMemoryDataCell.cs:59-65`（`Contains` 只查本地三层）· `:67-90`（`Remove` 只删本地）· `:43-57`（`Get/TryGet` 走父链）；`Compat\StorageCellAdapter.cs:50/52` 直通 `IStorageCell`（有父链语义）`[已核]` | **契约与实现矛盾（父链回退）**：`Contains(key)`/`Remove(key)`（`tier=null`）**从不走 `Parent`**，而 `Get/TryGet` 走 ⇒ 同一单元可 `Get` 命中而 `Contains` 判 `false`；两后端（`IStorageCell` vs `IDataCell`）在 `tier=null` 路径行为分裂 ⇒ G2.2「双后端同构」前提被破坏。测试 `InMemoryDataCellTests.cs:93-104` 只覆盖 `Get` 的父链回退（缺陷被测试盲区掩盖） | **高** | ⏸ P0（B10） |
| **F-34** | `Hosting\DataCenterHost.cs:202-212`（`_provider.Unbind()` → `_slots.Dispose()` → `_center.Shutdown()` → `_root.Dispose()` 顺序无隔离）+ `HostRuntimeSlot.cs:56-57`（显式抛 `AggregateException`）；`Boot` catch `:125-130` `[已核]` | **停机无异常隔离**：槽释放一旦抛错，后续 `_center.Shutdown()` 与 `_root.Dispose()` **全部跳过** ⇒ `RootInstanceGuard._live` 不归零、`_shutdown` 已置 1 ⇒ 表现为**永久停机且进程内再也 Boot 不出根**（第二根 fail-fast）。`Boot` catch 同型：`slots.Dispose()` 抛错会**替换原始装配异常**（对比 `StateCenter.Shutdown` 明确承诺\"不抛\"） | **高** | ⏸ P0（B11） |
| **F-35** | `Share\Foundations\StateManagement\Abstractions\IStateCenter.cs:13/19-20`（\"StorageEngine 根会话\"/\"落 StorageEngine.CreateSession\"）vs `StateCenter.cs:86-90`（`_factory.CreateChild`）+ `Core\Cells\InMemoryDataCellFactory.cs:11`；未被复用的既有能力 `Modules\InternalStorage\Extensions\StorageCellExtensions.cs:10`（`CreateChildCell`）`[已核]` | **scope 分区语义漂移**：分区载体改由 `IDataCellFactory` 派生，`DataCenterHostOptions.PartitionCellFactory` 与测试默认 `InMemoryDataCellFactory` ⇒ legacy `IStorageCell` 后端（迁移期实际生产路径）下 scope 状态落在**内存子单元**：不持久化、无引擎快照语义；且 `IStateCenter` 注释仍承诺引擎会话。测试 `StateCenterPortBackendTests.cs:32-53` 把漂移固化为\"两后端一致\"的期望 | **高** | ⏸ P0（B12） |
| **F-36** | `Core\Storage\TierStringStore.cs:41`（`Clear() => _cell.ClearTier(Session)` **不遵守 `_excludedKeyPrefix`**，而 `GetAllKeys:43`/`GetAll:82-91`/`Count:93`/`IsEmpty:95`/`RemoveByPrefix:63-73` 均遵守）；后果链 `StateCenter.Reset():219`→`StateOrchestrator.Reset()`→`Store.Clear()`；`StateStore.Reset` 注释（`:73`）承诺\"字符串 KV 保留\" `[已核]` | 同一对象内**两套键可见性语义**：`Clear()` 会连带清掉 `_obj:` 对象态与同 cell 的 `StateBlock.Decision` 决策核心键（`LayeredStoreClient` 写入），与 `StateStore.Reset` 的承诺及\"字符串 KV / 对象 KV 隔离\"设计直接冲突（缺陷自 `StorageCellStateStore` 原样继承，未修） | 中高 | ⏸ P1（B13） |
| **F-37** | `Hosting\DataCenterHost.cs:146-197`（`_root`/`_generation`/`_provider` 跨 `await` 非原子改写；仅入口 `ThrowIfShutdown()`，await 后不复检）`[条件性]` | `SwapAsync` **无并发与停机守卫**：Swap×Shutdown 交错可让 `_provider.Bind` 在停机后\"复活\"根（`Root.TryGetRoot` 又为 true）；两个并发 Swap ⇒ 旧根双释放 / `RootFactory` 双调用 / 代际跳号（与 F-22 同族） | 中高 | ⏸ P1（B14） |
| **F-38** | `Compat\StorageCellAdapter.cs:71-75`（`ProbeTier`：键已删 ⇒ 三次探测全 false ⇒ 回退 `Session`）+ `Plate\TConfig\ContextFrame\Abstractions\IContext.cs:45-62`（`ContextDataChangedEventArgs` **无层字段**）`[已核]` | 变更事件的 `Tier` 契约字段在**删除路径必然错误**（Config/Transient 键的删除被报成 Session）⇒ 按 Tier 归类/审计的消费方出错；且该探测落在写路径上（F-31 同源） | 中 | ⏸ P1（B15） |
| **F-39** | `Hosting\DataCenterHost.cs:310-317`（`Advance` 把自定义 `IStateGeneration` 降级为 `GenerationState`；`:172-173` 赋值窗口 `_root.Generation` 与 `_generation` 短暂不一致）· `Core\Roots\EmptyRuntimeSlot.cs:10`（未实现 `IDisposable`）· `Projection\TierStateProjector.cs:128`（URI 去重**静默丢弃**且不计入缺口计数）· `Core\StateCenter.cs:165`（调用方自算版本，而 hub `StateSubscriptionHub.cs:77-83` 内部重复分配）· `Extensions.LoopDecision\DecisionCenter.cs:43-44`（只捕 `JsonException`）/ `LoopStateStore.cs:71`（硬编码 `TokenMax=3`）`[已核]` | 类型保真缺失、空槽不可处置、\"静默降级\"违反设计文档 §5.6、版本分配双实现、异常面过窄与常量重复 | 中低 | ⏸ P3（B17） |
| **F-40** | `Core\Core\StateCenter.cs:182-202`（`Shutdown`）`[已核]` | **停机无异常隔离（库内侧）**：`_disposed=true` 先置位，而 `_compositionLifecycle?.AppShutdown()` 未包裹 → 抛错即跳过 `_hub.Dispose()` / `_scopeStores.Clear()`，且后续 `Shutdown()` 因 `_disposed` 直接 return ⇒ **内部资源永久泄漏**（F-34 只修了 `DataCenterHost` 侧；本类注释自称"承诺不抛"却未隔离） | **中高** | ✅ 已修（B18：三段各自隔离 + 承诺不抛；判据 `Shutdown_WhenCompositionThrows_StillReleasesHub_AndIsIdempotent`） |
| **F-41** | `Projection\StateUriMapper.cs:22`（`ScopeNodeKey="__scope"`）+ `Projection\TierStateProjector.cs`（锚点与值节点共享 `emitted`）`[已核]` | **保留键撞名致静默丢值**：真实键名为 `__scope` 时，其值节点 URI 与 scope 锚点 URI 相同 → `emitted.Add` 失败 → 值节点**被静默丢弃且不计缺口**（F-39「静默丢弃」子项） | 中 | ✅ 已修（B19：保留键转义 `%5F%5F` + 新增 `metadata.duplicateKeys` 缺口计数；判据 `Project_StateKeyNamedAsScopeAnchor_KeepsBothAnchorAndValue`、`Project_DuplicateCoordinates_AreReportedAsGap`、`Resolve_ReservedScopeKey_EscapesValueSegment_SeparateFromAnchor`） |
| **F-42** | `Registry\GraphRegistry.cs:81`（`DomainAliases => _domainAliases`）`[已核]` | **只读承诺被强转击穿**：`IReadOnlyDictionary` 实为内部可变 `Dictionary` → 消费方强转即改写注册表（同类 `Tiers/NodeKinds/EdgeKinds` 已用 `Array.AsReadOnly`，此处为 F-32 族遗漏） | 中低 | ✅ 已修（B20：`ReadOnlyDictionary` 包装；判据 `DomainAliases_IsReadOnlyView_NotCastableToMutableDictionary`） |
| **F-43** | `Core\Storage\TierStringStore.cs:90-99`（`GetAll` 用 `OrdinalIgnoreCase`）· `:53-63`（`GetOrAdd` 探测-写非原子）；`Share\...\StorageCellStateStore.cs:117-128` / `:75-85` 同构 `[已核]` | **同一 store 两套键口径**：`GetAll` 合并大小写相异键（`Count ≠ GetAll().Count`）；`GetOrAdd` 非原子（并发双写/双事件）。两份实现**同构复制**（F-27 放大面） | 低中 | ✅ 已修（B20：比较器统一 `Ordinal` + 实例级锁；**两份实现同步修**；判据 `GetAll_And_Count_ShareSameKeyComparer`） |
| **F-44** | `Core\Cells\InMemoryDataCell.cs:29-40`（`Set` 读-改-写）+ `Core\Roots\OrderedRuntimeSlotPartition.cs:64-86`（工厂内重入）`[已核]` | ① `Set` 的 `ChangeKind`/`OldValue` 判定非原子（并发下误报 Added、回传失真旧值）；② `GetOrAdd` 在锁内执行用户工厂，工厂内重入注册同键会被外层返回值**静默覆盖**（先登丢失） | 低中 | ✅ 已修（B20：`AddOrUpdate` 原子判定 + 重入以先登为准；判据 `Set_ConcurrentSameKey_RaisesExactlyOneAdded`、`RuntimeSlotPartition_GetOrAdd_FactoryReentrantRegistration_IsNotOverwritten`；F-20「工厂至多生效一次」不回归） |
| **F-45** | `Extensions.LoopDecision\LoopStateStore.cs:73`（`DefaultView` 硬编码 3）· `:19`（`IsTripped >= 3`）· `Share\...\LoopStatePrimitives.cs:44`（`ProgressToken.Max=3`）`[已核]` | 同一 `MAX_PROGRESS`/`MaxLevel` 常量**三处硬编码**（F-39 子项） | 低 | ✅ 已修（B21：新增 `LoopKeys.DefaultTokenMax` + 引 `FailureBudget.MaxLevel`；判据 `LoopStateStoreTests` 新增断言） |
| **F-46** | `Extensions.LoopDecision\DecisionCenter.cs:43-44`（只捕 `JsonException`）+ `DecisionRecord.cs:33`（`DateTimeOffset.Now`）`[已核]` | 载荷不可读异常面过窄（其余反序列化异常外溢）；本地时间戳写入状态值（跨语言 date-time 契约不绑定时区） | 低 | ✅ 已修（B21：异常面收窄为 `JsonException`/`NotSupportedException` + `UtcNow`） |
| **F-49** | `Core\Core\StateStore.cs:43`（`DataChanged +=` 无退订）· `Core\Core\StateCenter.cs:88` · `Compat\DataCellStorageView.cs:23` · `Compat\StorageCellAdapter.cs:31` `[已核]` | **订阅无退订**：`StateStore` 与 `DataCellStorageView` 均无解绑路径（后者也未实现 `IDisposable`）⇒ 停机后 cell 仍持有 handler（事件放大 / 跨停机回调） | 中低 | ✅ 已修（B18：`StateStore : IDisposable` + `DataCellStorageView : IDisposable` + `StateCenter.Shutdown` 统一解绑；判据 `Dispose_UnbindsCellSubscription_IsIdempotent_AndKeepsData`）。`StorageCellAdapter` 保持"与 inner 同寿不退订"（迁移期语义，G5 退役） |
| **F-50** | `Core\Projection\StateCenterEntrySource.cs`（注释"scope 是键的一段而非独立单元"）vs `Core\Core\StateCenter.cs`（`BeginTransaction(scope)` 落 scope 分区子单元）；测试固化 `StateHoldDriven.Tests\StateCenterTests.cs:80-96`、`DataCenter.Tests\StateCenterPortBackendTests.cs:70-83` `[已核]` | 两处载体（根单元 / scope 分区子单元）**注释口径互相矛盾**；行为本身为**有意设计**（scope 隔离：根单元不可见，由既有测试锁定） | 中低 | ✅ 已修（B19：**只对齐文档、不改行为** —— `BeginTransaction` 增载体纪律 XML 注释；`StateCenterEntrySource` 注释改为"两处载体互不可见、须一并枚举"） |
| **F-52** | `Hosting\DataCenterHost.cs:410-417`（`Advance` 把 `IStateGeneration` 强制降级为 `GenerationState`）`[已核]` | 类型保真缺失（F-39 子项） | 低 | 🟦 接受现状（本轮实测：全库 `IStateGeneration` **唯一实现者 = `GenerationState`**，无自定义实现 ⇒ 无实际损坏面；为假想需求新增端口违反最小面纪律，留待出现第二个实现时引入 `IAdvancedGeneration` 端口） |
| **F-53** | `Core\Storage\TierStringStore/TierObjectStore` vs `Share\...\StorageCellStateStore/ObjectKeyValueStore`（F-27）`[已核]` | 两份 Store 逐方法同构、且都在活跃使用 | 中 | 🟦 决策已记录（B21）：**收敛方向 = Core 仅依赖端口 ⇒ 长期删除 Share 侧副本**（Share 降纯契约，R1'）；短期为避免分叉，本轮对**两份实现同步修复** F-43，使同构缺陷不再被复制 |
| **F-54** | `Hosting\FallbackStateCenter.cs:32-39`（`partitionCellFactory` 可选）+ `DataCenterHostOptions.cs:23`（默认内存工厂）+ 外部 `AgentRuntimeRoot.ResolveStateCenter`（不传工厂）`[已核]` | F-35 残留：无根回落 / 未注入工厂时 legacy 后端 scope 分区仍落内存（不持久化、无引擎会话/快照语义） | 中 | 🟦 接受现状（架构约束：`FallbackStateCenter` 注释明确"引擎实现程序集不进宿主装配层闭包"；修复须宿主侧装配注入分区工厂 —— 归口 S 步骤，非库内可修） |

#### 第四轮（2026-09-12，DataCenter 数据流视角审计：`Projects\Plug\Agent`）

> 口径：以「数据流承载者 / 写入点 / 读取点 / 键空间」四元组追真源唯一性、写点唯一性、消费方存在性（对齐 `AX.StateRootSingleSource`、`Strategy.SingleImplementation`、`Strategy.RedundancyElimination`）。编号续编 F-55~F-70。

| 条目 | 位置 / 证据 | 影响 | 严重度 | 状态 |
|---|---|---|---|---|
| **F-55** | `Agent.Axioms\StateAdapters\StateContextAdapter.cs:64-65`（`ClearTransient() => _client.ClearLayer(StorageLayer.Transient)`）；`LayeredStoreClient.cs:94-95`（`ClearLayer → _dataCell.ClearTier`）；Transient 命名空间为 `Agent.Round`（同文件 `:33`），scope 才按 `round-{n}` 分 | **越 scope 清空**：清整层 = 连带抹掉同进程其它会话/其它轮的轮内状态；同文件已有 `ClearPartition(layer, scope)` 却未使用（F-36 同族，粒度更粗）。触发点：`WorkflowRunner.cs:169`、`DataContainerBinder.cs:209` | **高** | ✅ 已修（B22：改按本适配器 round scope 的 `ClearPartition`；判据 `StateContextAdapterTests.T13_ClearTransient_OnlyClearsOwnRoundScope`；`Agent.Axioms.Tests` 85/85、`Agent.Core.Tests` 399/399） |
| **F-56** | `Agent.Contract\Runtime\AgentRuntimeRoot.cs:107-109`（槽未登记 → `FallbackStateCenter.Create(() => new StorageCell("agent-state"))`）+ 全仓唯一读点 `Agent.Launcher\Composition\AgentComposition.cs:105-106`（`AgentFrameworkAssemblyContext.StateCenter` **无任何写入方**） | **静默回落第二真源**：宿主未先调 `AgentRuntimeRootBootstrap.TryBoot()`（或 `AGENT_DATACENTER_ROOT=0`）时，工作流状态落与根无关的 `StorageCell`，而投影器/注册表/槽仍挂根 ⇒ 数据流分裂且零告警（F-24 残留：注入面存在但注入方缺失未被判据覆盖） | **高** | ✅ 已修（B23：E 段「根已绑而未登记 state-center ⇒ fail-fast」+ 未引导根回落留痕 `FrameworkStateCenter_fallback_unbound`；新增 `IsStateCenterRegistered`/`StateCenterFallbackUsed`；判据 `RunSegmentERootSlots_WithRootButNoStateCenter_FailFast`、`RunSegmentERootSlots_WithInjectedRootProvider_*`（新增真源恒等断言）；`Agent.Launcher.Tests` 31/31） |
| **F-57** | `AgentOrchestrator.cs:103`（`_stateCenter = stateCenter ?? AgentRuntimeRoot.ResolveStateCenter()`，C 段构造即**固化实例**）vs `AgentComposition.cs:105-116`（E 段才登记）`[条件性]` | 若 C 段解析到回落真源、E 段注入另一实例 ⇒ 槽内登记实例 ≠ 运行期写入实例（`WorkflowRunner.cs:91` 同型）；当前 `ctx.StateCenter≡null` 掩盖该路径 | 中 | ✅ 已修（B24：C 段在构造编排器**之前**解析并固化 `ctx.StateCenter`，E 段加「装配上下文 ≠ 编排器真源 ⇒ fail-fast」分叉防线；判据 `RunSegmentERootSlots_OrchestratorHoldingDifferentStateCenter_FailFast`、`RunSegmentERootSlots_SameInstanceTwice_IsIdempotent`） |
| **F-58** | `AgentComposition.cs:113-117`（E 段以「处置期写口」`partition.Replace(...)` 做装配期注册）vs `IRuntimeSlot.cs:34-49`（`Register` 同键异实例 fail-fast） | 装配期可**静默覆盖**既有槽实例，绕过 DF8「第二份运行时」防线（F-01/F-15 同族） | 中 | ✅ 已修（B24：`RegisterSlot` = 缺键 `Register`（并发同键异实例 fail-fast）/ 同实例幂等 / 异实例按处置期替换但**留痕** `FrameworkSlot_assembly_override`；状态真源异实例覆盖改为 fail-fast；判据 `RunSegmentERootSlots_SlotHoldsDifferentStateCenter_FailFast`） |
| **F-59** | `Agent.Core\Nodes\NodeExecutionService.cs:181-191`（`KeyProcessor.Mapper.MapBatch(node.InputMapping, globalMapping)`）+ `RoundStepInvoker.cs:150 → :161`（再经 `DataContainerBinder.BindStepInputPhase`，内部同一 `MapBatch`，两份 merge 各自计算，`DataContainerBinder.cs:62/77/229-244`） | 单步**同一映射语义被套用两次**（重复逻辑 + 双 merge 漂移面） | 中 | ✅ 已修（B25：`BindStepInputPhase` 基线改为**补位不覆盖**，恢复「节点输出端口 > 数据空间」文档优先级，双套用不再冲突；判据 `T7_StepInputBaseline_DoesNotOverwritePortResolvedValue`；`Agent.Axioms.Tests` 86/86） |
| **F-60** | `AgentRunner.Persistence.cs:99-112`（`DecisionWordOf` 硬编码 `"TS"/"TO"/"TM"/"SWITCH"/"CONTINUE"`）写入 `DecisionRecord.Word`（`:67-83`）vs 权威词表 `Share\...\TaskStatusPool\Routing\DecisionWords.cs:14-20`（`stop/continue/complete/error`）与 `DecisionRecord.cs:17-18` 字段语义 | 同一条审计记录**两种词表口径**（大写合同枚举 vs 小写骨架词）；下游 `DecisionWordClassifier` 按下写词解释 ⇒ 词表越界（F-39 同族） | 中 | ✅ 已修（B25：删除 `DecisionWordOf` 字面量表，改为 `ToDecisionContractWord` + 单一权威 `Agent.Contract.Observability.DecisionContractWords`（TS/TO/TM/CONTINUE/SWITCH），文档显式声明与骨架词表 `DecisionWords` 分工、不得写入 `DecisionRecord.Word`；判据 `ToDecisionContractWord_*`、`DecisionContractWords_SeparatedFromSkeletonVocabulary`） |
| **F-61** | `Agent.Runtime\Persistence\Core\SnapshotSerializer.cs:15-90`（`Serialize/Deserialize/Apply` + `Snapshot` DTO）；反向引用仅 `DefaultBoundaryFramework.cs:156` 注释与 README | **零代码消费方的死实现**，却被注释宣称为「持久化恢复」承担者；其还原逻辑本身错误（`:61-64` 以 `Escalate()/Advance()` 循环还原，不校验 `Value≤Max`/上限语义） | 中 | ✅ 已修（B27-1，2026-09-12：删除死实现 `SnapshotSerializer.cs` 及其 `Snapshot` DTO；连带删除 3 个测试消费点 —— `Agent.Core.Tests/SnapshotSerializerTests.cs` 整文件、`Agent.Persistence.Tests/SnapshotPersistenceTests.cs` 的 `SnapshotSerializer_AxiomContextRoundtrip` 用例；同步修正 `DefaultBoundaryFramework.cs:156` 注释与 `README.md:264` 提法；`ur_snapshots` 表路由**保留**（`StorageRouteTable.cs:25` 通用表路由，与死类无耦合）。判据：`Agent.sln` 0 错 0 警 + 全域 grep `SnapshotSerializer` 命中 0 + `Agent.Core.Tests` 405/405 · `Agent.Persistence.Tests` 88/88） |
| **F-62** | `Agent.Orchestration\SessionWorkflow\ConversationCentralPipeline.cs:247-249`（`UpdatePayloadAsync` 后紧接 `WriteStatusAsync`）+ `Agent.Core\TaskPool\AgentTaskPool.cs:307-313/336-343`（后者内部 `GetAsync` + `ApplySnapshotStatusAsync` → 再次 `UpdatePayloadAsync`） | 同一逻辑变更**两次整行 upsert**（写放大 + 版本推进两次） | 中 | ✅ 已修（B26-1，2026-09-12：`ConversationCentralPipeline.FinalizeAsync` 删除二次 `WriteStatusAsync`，收敛为**单次** `UpdatePayloadAsync`（Status 随载荷单源落库，`AgentTaskPool` 既有语义）；判据：`ConversationFinalizeSingleWriteTests.FinalizeAsync_WritesPayloadOnce_AndDoesNotUpsertStatusTwice`（计数会话存储断言同一 taskKey `UpdatePayloadAsync`=1 · `WriteStatusAsync`=0，且 Round/Status/`__backend_audit` 语义不变）） |
| **F-63** | `NodeExecutionService.cs:320-326`（`WriteOutputAsync` 按端口写，逐端口一次 IO）+ `:340-366`（`PutAsync` 读-改-写同一业务节点，`:348-349` 注释自认「两种记录形态互不覆盖」）+ `:151`（读侧只枚举一种） | `ur_nodes` **写两形、读一形**（语义缺口）；叠加 `:252-254`（`vars.Set` 扁平写 Transient）= F-06 同族「产出双通道未收敛」 | 中 | ✅ 已结案（B26-3 取证修正 + 判据，2026-09-12）——**取证结论修正**：执行链已**单形**（`NodeOutputStore.cs:42-51/:193-202` 只写 `NodeType=node` + `BusinessNodePayload{Symbol,Outputs}`；`:68-78/:158-159` 读侧 `EnumerateOutputsAsync` 只枚举 `NodeType=node`；`BuildOutput` 从 `payload.Outputs` 取端口 `:204-229`），原"写两形"记载已过期；**真实残余** = API `node.write` 缺省形可落 `NodeType.Output` 且载荷非 `NodeOutputDoc`（`AgentApiIntentSource.cs:256-266`）⇒ 执行链读侧读不回（形态收敛属 Frame 写路径，另立编号）。落地：`NodeExecutionService.cs:346-352` 注释按实证改写 + 读一致性判据 `NodeOutputStoreTests.ReadView_TaskScoped_ReadsBackEmbeddedOutputs_And_RecordIsSingleForm`（单形 + `NodeReadView` task 作用域读回）；判据：`Agent.Persistence.Tests` 88/88） |
| **F-64** | `Agent.Operation\Observe\AgentObservability.cs:56-78`（`FileAppendEventStore` events.jsonl + `PersistentEventSink` + `HotRingSink`；装配点 `AgentFrameworkHosting.cs:101-118`）vs `Agent.Core\DataStream\EventSink.cs:19-48`（`ur_events` via `IZoneStore`；装配点 `DataStoreHosting.cs:68-72`） | 「运行事件」**双事实源 / 双 schema / 双介质**（观测查询口径分裂） | 中 | ✅ 已分工化（B27-2 / DEC-1② 采纳，2026-09-12，零行为改动）：`AgentObservability` 类注释声明**语义观测**唯一真源（`events.jsonl`，消费方 `ObservabilityQuery`/UI 语义时间线/`AgentTrace`/`ObserveTerminalSink`）；`EventSink` 类注释声明**运行事件**唯一真源（`ur_events`，消费方运行事件查询/重放、反应式触发）；分工表 + 边界纪律 + 唯一化方向（DEC-1①，未实施）落设计文档 §10。判据：`Agent.Observe.Tests` 11/11 不回归） |
| **F-65** | 根分区（`AgentComposition.cs:107-110` + `AgentRuntimeRoot.Resolve` 惰性装配，槽键见 `AgentRuntimeSlotKeys`）vs `AgentUiHost` 静态槽（`AgentUiHost.cs:28+`，装配面 `AgentFramework.cs:179-182`、`AgentComposition.cs:66-71`） | 对象槽**双真源**：同一批装配产物在根分区与静态槽并行发布（换代迁移只覆盖前者） | 中 | ✅ 已修（B27-3 / DEC-2① 采纳，2026-09-12）：`AgentUiHost` 全部 UI 宿主对象槽（任务池/引用守卫/会话/历史关系/关系编排/执行标识/历史运行时/层次门面/会话渲染器）改由**根分区承载**（`RootSlot<T>` 值寄存于 `root.Slots.Partition("agent")`，槽键新增 9 条；未绑根经 `AgentRuntimeRoot.Resolve` 回落进程本地容器，与旧静态槽行为等价），双真源消除；`AgentUiHostAccess` 无二次存储 ⇒ UI 面实例与根分区实例恒同一引用。判据：`AgentUiSlotRootPartitionTests`（根分区含槽键 + 引用相等 + UI 访问面同源 + 未绑根回落 + 换代后不残留旧代数产物/新代数可重装配）；`Agent.Launcher.Tests` 37/37 · `Agent.Api.Tests` 34/34 不回归。静态槽**退役**不在本项内（§6 诚实边界）） |
| **F-66** | `Agent.Core\TaskPool\AgentTaskItem.cs:108-111`（`ILoopState` 内存内核）vs `DataCenter.Extensions.LoopDecision\LoopStateStore.cs:32-124` + `CoreStateView`（落 Center Session 层）；Agent 侧无 `LoopStateStore` 调用点（grep 仅 `DecisionCenter`） | **确定性核心双承载**（内存为主、状态层闲置）：`View/LoadWorking/Save/Reset` 零消费（F-27 同族收敛方向待裁） | 中 | ✅ 已边界化（B26-2 / DEC 边界，2026-09-12，**不删代码**）：`LoopKeys`/`LoopStateStore` 类注释标注消费方 = StateHoldDriven 宿主线、Agent 侧不接线（Agent 运行期内核 = `AgentTaskItem`；Center 侧审计快照 = `DecisionCenter.Record`）；`AgentRunner.Persistence.RecordDecisionAsync` 标注同一分工；新增门禁 `PowerShellScript/Operation/DSL/Check-AgentDecisionCoreBoundary.ps1`（扫描 `Projects\Plug\Agent`，断言 `LoopStateStore`/`CoreStateView`/`DataCenter.Extensions.LoopDecision` 命中 0）。判据：门禁 `-Check` 退出码 0（扫描 494 文件 · 违规 0）；跨仓消费方取证前**不得删除** `LoopStateStore`） |
| **F-67** | `Agent.Contract\Runtime\AgentRuntimeSlotKeys.cs:30`（`Database`）、`:47`（`Prompt`）；全仓 `AgentRuntimeSlotKeys.*` 使用面核对：二者**无解析/注册点** | 槽键声明未落地（声明面冗余） | 低 | ✅ 已修（B27-4 / DEC-5① 采纳，2026-09-12）：删除 `AgentRuntimeSlotKeys.Database`/`Prompt` 两常量（全 `Projects\` 全域取证：20 处 `AgentRuntimeSlotKeys` 引用中该两键**零解析/注册点**）；同步 `Agent数据与代码分离-架构整理与通用化方案.md:64` 键数（13 → 21，含 F-65 新增 9 键）；`RuleHub`/`DeepSeek` 等其余键不受影响。判据：`Agent.sln` 0 错 0 警 + 全域 grep 两常量命中 0） |
| **F-68** | `AgentRuntimeRoot.cs:45-64`（`ProviderSlot.Enter(provider)` 不释放句柄 + `CurrentProvider = ProviderSlot.Current ?? static Binding`）`[条件性]` | 取根面**双通道**（ambient + 进程静态）：解绑句柄不释放 ⇒ 残留到同上下文后续异步流；不同线程可解析到不同根（弱化 `AX.StateRootSingleSource`） | 中 | ✅ 已修（B24：`Bind` 保存 `Enter` 恢复句柄并在重绑时释放、`Unbind` 先释放句柄再清空静态绑定（不再沿逻辑链残留）；判据：`AgentRuntimeRootFallbackTests` 全绿 + `Agent.Launcher.Tests` 34/34） |
| **F-69** | `RoundStateMapper.cs:117-144`（`Harvest` → `state.Data[SessionId]`）vs `WorkflowRunner.cs:253-258`（`state.Data[SessionId] = execResult.SessionId`、`state.Data[ExecResult]`） | `state.Data` 同键**两个写点、两个来源**（Vars 会话键 vs 执行器返回）⇒ 跨轮真值取决于执行顺序 | 中 | ✅ 已修（B25：`ResolveSessionId` 作用域锚改为稳定 `task_id`（回退租约会话键 → 常量），**不再读**可能被执行器会话 ID 覆盖的 `state.Data[session_id]`，消除 Session 层跨轮换分区；判据 `ResolveSessionId_AnchorsOnStableTaskKey_NotExecutorSessionId`、`ResolveSessionId_FallsBackToLeaseThenConstant`；`Agent.Core.Tests` 404/404） |
| **F-70** | `Agent.Core\Nodes\DecisionWordMapper.cs:14-16`（空壳类型）、`Agent.Operation\Routing\Evidence\DecisionRoundEvidence.cs:57` + `Semantic\DecisionPointSemantics.cs:103` + `DecisionSurfaceGraph.cs:34`（`TerminalTs="ts"` 第三/第四词表形态）、`StateCenter.cs:268` / `StateStore.cs:78-81` / `StateContextAdapter.ClearTransient`（Reset/Clear 同名族） | 冗余面与同名族语义分派（F-28 同族，可维护性） | 低 | ✅ 已处置（B27-5 / DEC-6② 采纳，2026-09-12）：**F-70-1** 空壳类型 `DecisionWordMapper` **接受现状**（删除即失 `[BindAxiom(DecisionOutputSet)]` 公理绑定，不改代码）；**F-70-2** 决策曲面词（`DecisionSurfaceGraph` 小写坐标标签）与观测契约词（`DecisionContractWords` 大写枚举）**文档化分工 + 不合并两表**，新增覆盖断言 `DecisionSurfaceWordCoverageTests`（曲面标签经 `TerminalOf`/`RouteActionOf` + `AgentRunner.ToDecisionContractWord` 产出集合 ≡ `DecisionContractWords.All`，5/5 一一对应；曲面标签不得混入契约枚举）；**F-70-3** `Reset`/`Clear` 同名族语义表落 `Projects\Plug\Agent\doc\Agent-DataOwnership.md` §9（不改行为）。判据：`Agent.Core.Tests` 405/405） |

### 关键条目详注

**F-16（P0，热换代必然踩）**：`AgentRuntimeRoot` 的注释（`:20`）承诺"根未绑定 → 回落进程本地容器"，但实现把"未注入"与"已注入但解绑"合并成同一分支。

```csharp
// AgentRuntimeRoot.cs:55-65（现状）
public static bool IsBound => CurrentProvider is not null;              // 已注入但 Unbind → 仍报 true
private static IRuntimeSlotPartition Partition
    => (CurrentRoot?.Slots ?? FallbackSlot.Value).Partition(...);      // CurrentRoot 抛 → 永不回落
```
建议：在 `IRootProvider` 侧补非抛探测（如 `HostRootProvider.TryGetRoot(out root)`，`Hosting` 内已有 `_root` 字段可直接读），把 `CurrentRoot` 改为返回 `null` 而非抛。

**F-18（P0）**：`GenerationState.Next` 的默认参 `fingerprint = null` 读起来像"保持"，实际是"置空"。
```csharp
// GenerationState.cs:17-19（现状）
public GenerationState Next(string? fingerprint = null) => new(Generation + 1, fingerprint, SchemaVersion);
// DataCenterHost.cs:288
var next = state.Next(plan.Fingerprint);   // plan.Fingerprint 为 null → 指纹丢失
```
建议：`Advance` 改为 `state.Next(plan.Fingerprint ?? state.Fingerprint)`（并同步 `GenerationPlan` 注释口径）。

**F-21（P1，(S4-2)）**：装配入口只在"向前换代"调用，失败回建回到核心路径 ⇒ 注入方（宿主）无法保证回建物与旧代数同构。
建议：回建亦经同一入口（以 `previousGeneration` 构造上下文），或在文档与判据中**显式降级**该承诺为"值保留 + 代际不前进"。

**F-24（P1，目标级缺口）**：这是 S4-4 的**真正前置**——在补出 `StateCenter` 注入面之前，删两处 `new StateCenter(` 只会让 Agent 侧"没有状态中心"（或改成从槽取，但槽里没人放）。建议 S4-4 拆为：
1. `AgentRuntimeSlotKeys` 增 `state-center` 键 + 装配方（宿主/测试夹具）注册 `StateCenter`；`AgentOrchestrator`/`WorkflowRunner` 改为注入式取用；
2. 再删两处兜底造根 + 清 K9 白名单两行。

---

## §5 收口建议（修复批次与判据）

> 通则：一批次 = 一个独立可编译、可回退的提交；每条修复须带**可执行判据**（新增断言优先，其次命令/门禁）。

| 批次 | 内容 | 判据建议 | 回退 |
|---|---|---|---|
| **B1/P0** ✅ | F-16 · F-17：取根/取注册表改为**非抛探测**（`IRootProvider` 增 `TryGetRoot`；`AgentRuntimeRoot`/`AgentStateDomain` 改为"未绑定或已解绑 → 回落/默认"） | 新增（实测）：`SwapAsync` **解绑窗口**（`Export` 之后至绑根前，观测点 = `RootFactory` 内）`TryGetRoot`=false、`Root` 仍 fail-fast、`AgentRuntimeRoot.Partition` 不抛、`IsBound` 语义与注释一致；`DataCenter.Tests` 53→**56**、`Agent.Launcher.Tests` 26→**28** | 单提交可撤；行为等价于 A1 语义 |
| **B2/P0** ✅ | F-18：`Advance` 保留既有指纹（`plan.Fingerprint ?? state.Fingerprint`），同步 `GenerationPlan` 注释 | 新增（实测）：`swap("fp-1")` → `swap(无指纹)` 后 `Fingerprint == "fp-1"`（`SwapAsync_WithoutFingerprint_PreservesPreviousFingerprint`） | 单提交可撤 |
| **B3/P1** | F-19：`Resume` 异常不覆盖原始异常（原始异常优先，Resume 失败聚合/记录） | 新增：Drain 抛 + Resume 抛 → 对外异常为 **Drain 的**异常（且 Resume 失败可见于日志/聚合） | 单提交可撤 |
| **B4/P1** | F-20 · F-04：统一为 `GetOrAdd` 语义（冲突后重读既有实例），`Replace` 仅保留处置期语义 | 新增：并发 8 线程 `Resolve` 同一键 → 全部返回**同一实例**、无异常 | 单提交可撤 |
| **B5/P1** | F-21：回建亦经装配入口；若决定不修则**显式降级承诺**（文档 + 判据措辞） | 二选一：回建后 `RootFactory` 调用次数 = 2（成功 1 次失败路径 1 次）；或判据改为"值保留 + 代际不前进" | 单提交可撤 |
| **B6/P1** | F-24：拆两步（先补 `StateCenter` 注入面 + 槽键，再删兜底造根清白名单）**✅ 已完成（S4-4a）** | 步骤 1：注入后 orchestrator 使用注入实例（`ReferenceEquals` 断言）；步骤 2：K9「待迁移」= 0 且 `Agent.Core.Tests` **399/399** —— **实测均已达成** | 两步各自可撤 |
| **B7/P2** | F-01 · F-02 · F-14 · F-15：抽单一 `IRuntimeSlotPartition` 实现 + `CreateProjector(generation)` + 注册表单例 | `DataCenter.Tests` 不回归；槽语义判据（同键同实例幂等 / 异实例抛 / 处置期逆序释放）集中一处 | 单提交可撤 |
| **B8/P2** | F-09 · F-10 · F-11 · F-12 · F-13：删/接线冗余公开面 | F-10 接线后：装配期 `IsFullyRegistered` 断言（缺失即抛）；F-12 **✅ 已接线（S4-4b/1d）** | 单提交可撤 |
| **B9/P3** | F-22 · F-23 · F-25 · F-26 | F-25：收窄豁免后 K9 重扫仍 EXIT 0（当前无新增违规）；F-26：补一条惰性自举 × 半装配窗口的单测 | 单提交可撤 |
| **B10/P0** | F-33：`InMemoryDataCell.Contains/Remove`（`tier=null`）补齐父链回退，与 `Get/TryGet` 对齐；`Remove` 未命中本地时委派 `Parent.Remove`，事件由**实际删除方**抛出（避免双通知） | 新增：`Contains_FallsBackToParent`、`Remove_FallsBackToParent_RaisesOnOwner`；**两后端同构断言**（`StorageCell` vs `InMemoryDataCell` 在 `tier=null` 下 `Get/Contains/Remove` 三态一致）；先扫全库无 `Contains(`/`Remove(`（`tier=null`）的隐式依赖；`DataCenter.Tests` 不回归 | 单提交可撤 |
| **B11/P0** | F-34：`ShutdownAsync` 三段（槽 / 存储 / 根）各自 try/catch 收集，`_center.Shutdown()` 与 `_root.Dispose()` **必达**，末端聚合外抛；`Boot` catch 复用同一「尽力释放」helper（原始异常优先，释放异常降级为附加信息） | 新增：注入「释放即抛」的槽对象 → 停机后 `RootInstanceGuard` 归零且**可再次 `Boot` 成功**；装配期 `ConfigureSlots` 抛错时对外异常为原始装配异常 | 单提交可撤 |
| **B12/P0** | F-35：分区载体**按后端选择** —— legacy 后端经 `StorageCellExtensions.CreateChildCell`（引擎会话）适配为 `IDataCell` 后交给 `StateStore`，端口后端维持 `InMemoryDataCellFactory`；二选一方案 B（不改行为）：同步改写 `IStateCenter.GetOrCreateScope`/`Cell` 注释与设计文档，显式降级「引擎会话语义」承诺 | 方案 A：legacy 后端下 scope 写值命中 `StorageCell` 子单元（`GetKeys(Session)` 含该键）+ 端口后端行为不变 + `DataCenter.Tests` 全绿；方案 B：文档/注释一致性核对（无代码断言） | 单提交可撤 |
| **B13/P1** | F-36 · F-28②：`TierStringStore.Clear()` 改为**按排除前缀逐键移除**（与 `GetAllKeys` 同语义）；`StateStore.Reset` 更名 `ResetObjectState` 并更新调用点与注释，消除与 `StateCenter.Reset` 的同名两义 | 新增：`Clear()` 后 `_obj:` 键消失且同 cell 的 `ns:scope:key` **仍在**；`ResetObjectState` 只清对象态；`StateOrchestrator.Reset` 行为不回归 | 单提交可撤 |
| **B14/P1** | F-37：`DataCenterHost` 增换代互斥（`SemaphoreSlim(1,1)`，`SwapAsync` 全程持锁）；`Drain` 后复检 `_shutdown`（停机中不绑新根，释放交出物后按停机语义返回） | 新增：并发 8 线程 `SwapAsync` → `RootFactory` 调用次数 = 1；Swap×Shutdown 交错后 `Root.TryGetRoot` 恒 false（无复活根）；`SwapAsync_WithoutFingerprint_PreservesPreviousFingerprint` 等既有断言不回归 | 单提交可撤 |
| **B15/P1** | F-38 · F-31：`StorageCellAdapter` 维护 `key→tier` 旁表（`Set/SetQuiet/SetObject` 登记；事件回调优先查表、未命中再回退探测；删除事件用登记值并清理表项），使 `DataCellChangedEventArgs.Tier` 在删除路径正确 | 新增：Config 层键删除 → 事件 `Tier == Config`；旁表命中时**探测次数为 0**（以计数可注入的 cell 夹具断言） | 单提交可撤 |
| **B16/P2** | F-27（**先决策收敛方向**：Core 仅依赖端口 ⇒ 删 Share 侧副本并迁移 `StateManager`；或反向）· F-30：删除/接线冗余端口面（`IStateDispatch` 删除或接线为 `IPublishDispatcher` 的端口化替身；`RootProvider` 收编入测试夹具；`Decode` 与 `SetObject` 按消费方证据决定去留） | F-30：删除后四套解构建 0 错 0 警 + 7 测试工程通过（零消费方 ⇒ 预期无回归）；F-27：决策记录（写入本台账「状态」列）后按方向执行并复查 `StateManager` 路径 | 单提交可撤 |
| **B17/P3** | F-28① · F-29 · F-31 · F-32 · F-39：聚合清理 —— 双公开面（`GlobalStore`/`Store`、双视图）与死分支/双重重载；metadata 未接线计数（`unparsedKeys` 等接门禁或删）；`IReadOnlyCollection` 改只读视图（`Array.AsReadOnly`）；`LoopStateStore` 快照复用；`StateStore._lock` 死字段删除；`Advance` 类型保真（保留自定义 `IStateGeneration`）；`EmptyRuntimeSlot` 实现 `IDisposable`（no-op）；去重静默丢弃计入 `duplicateKeys` 计数；版本分配改走 3 参 `Publish` 重载；`Recent` 异常面收窄改为捕获必要异常集；`TokenMax` 常量归一（引用 `LoopKeys`） | 每子项一条最小断言；批末 `DataCenter.Tests` 全集 + K9/K10 门禁 EXIT 0 | 按子项拆分可撤 |

| **B18/P1** | F-40 · F-49：`StateCenter.Shutdown` 三段隔离（承诺不抛）+ 订阅面/存储视图统一解绑（`StateStore : IDisposable`、`DataCellStorageView : IDisposable`） | 新增：Composition 抛错 → Shutdown 不抛且 hub 已释放（`Subscribe` 抛 `ObjectDisposedException`）、二次 Shutdown 幂等；`Dispose` 后写 cell 不再回调且数据保留 | 单提交可撤 |
| **B19/P1** | F-41 · F-50：保留键 `__scope` 转义 + `metadata.duplicateKeys` 缺口计数；`BeginTransaction`/`StateCenterEntrySource` 载体纪律注释对齐（**只改文档、不改行为**） | 新增：`__scope` 同名键 → 锚点与值节点并存且 `duplicateKeys=0`；同图坐标 → `duplicateKeys=1`；原 scope 隔离断言（`StateCenterTests.BeginTransaction_WithScope_ShouldWriteToScopeCell_NotGlobal`）不回归 | 单提交可撤 |
| **B20/P2** | F-42 · F-43 · F-44：`DomainAliases` 只读包装；`GetAll` 比较器统一 + `GetOrAdd` 实例级锁（Core/Share **两份同步**）；`Set` 原子判定 + 槽工厂重入以先登为准 | 新增：强转 `Dictionary` 抛 `InvalidCastException`；`Count == GetAll().Count`；并发 `Set` 恰好一次 `Added`；工厂内重入不被覆盖；F-20 既有断言不回归 | 单提交可撤 |
| **B22/P0** | F-55：`StateContextAdapter.ClearTransient` 由「整层 `ClearLayer(Transient)`」改为「按本适配器 round scope 的 `ClearPartition(Transient, _roundId)`」（同源触发点 `WorkflowRunner.cs:169` + `DataContainerBinder.cs:209` 随之收敛） | 新增 `T13_ClearTransient_OnlyClearsOwnRoundScope`（r1 清空后 r2 同层键仍在）；既有 `T11_HarvestClearsTransient`/`T12_CrossRoundPersistence` 不回归 —— **实测**：`Agent.Axioms.Tests` **85/85**、`Agent.Core.Tests` **399/399** | 单提交可撤 |
| **B24/P1** | F-57 · F-58 · F-68：① C 段在构造编排器**之前**解析并固化状态真源（`ctx.StateCenter ??= AgentRuntimeRoot.ResolveStateCenter()`），E 段加「装配上下文 ≠ 编排器真源 ⇒ fail-fast」分叉防线；② E 段槽登记改 `RegisterSlot`（缺键 `Register` / 同实例幂等 / 异实例替换**留痕** `FrameworkSlot_assembly_override`；状态真源异实例覆盖 fail-fast）；③ 取根面 `Bind/Unbind` 持有并释放 `AmbientSlot.Enter` 恢复句柄 | 新增 `RunSegmentERootSlots_OrchestratorHoldingDifferentStateCenter_FailFast`、`RunSegmentERootSlots_SlotHoldsDifferentStateCenter_FailFast`、`RunSegmentERootSlots_SameInstanceTwice_IsIdempotent` —— **实测**：`Agent.Launcher.Tests` **34/34**、`DataCenter.Tests` **95/95**、`PrjBridge`（宿主）构建 0 错 0 警；K9 违规 0 / Check-SingleWriter OK | 单提交可撤 |
| **B25/P1** | F-59 · F-60 · F-69：① `BindStepInputPhase` 基线改**补位不覆盖**（恢复「节点输出端口 > 数据空间」优先级）；② 决策观测契约词表单一权威（新增 `Agent.Contract.Observability.DecisionContractWords`，`DecisionWordOf` → `ToDecisionContractWord`，与骨架词表 `DecisionWords` 显式分工）；③ `ResolveSessionId` 作用域锚改稳定 `task_id`（不再读被执行器回写的 `session_id`） | 新增 `T7_StepInputBaseline_DoesNotOverwritePortResolvedValue`、`AgentDecisionAndSessionScopeAnchorTests`（5 条）—— **实测**：`Agent.Axioms.Tests` **86/86**、`Agent.Core.Tests` **404/404**；回归全绿（Runtime 21 / Persistence 88 / Session 20 / Modules 20 / Operation 47 / Observe 11 / Guardrails 10 / Api 34 / Api.Contract 22） | 单提交可撤 |
| **B26/P2**（⏸ 待实施） | F-62 · F-63 · F-66 | 会话完成**单次 upsert**；`ur_nodes` 读写形态（**先取证**）；确定性核心**边界化** | 执行细节 / 判据 / 回退**唯一权威** = `DataCenter-Agent运行时数据接入-第四轮剩余收敛计划.md` §2.1；其中 **F-63 须先出取证报告**（DEC-3）方可动代码 | 子计划 §5 |
| **B27/P2·P3**（⏸ 待实施） | F-61 · F-64 · F-65 · F-67 · F-70 | 删死实现（`SnapshotSerializer`）；观测事件事实源唯一化；对象槽真源收敛；未落地槽键清理；冗余面/同名族文档化 | 执行细节 / 判据 / 回退**唯一权威** = 子计划 §2.2；**DEC-1 / DEC-2 / DEC-3 / DEC-4 / DEC-5 / DEC-6 未采纳前不得动对应代码** | 子计划 §5 |

**验证纪律（沿用下一步实施计划 §4）**：任一批次提交前须跑 —— 四套解决方案构建（0 错 0 警）+ 7 个测试工程 + K9/K10 门禁；契约变更后必须刷新烘焙副本（`StateHoldDriven.csproj`）。

---

## §6 交叉引用

### 6.1 与公理/策略的对应

| 条目 | 对应公理 / 策略 | 说明 |
|---|---|---|
| F-01 · F-02 · F-04 · F-07 · F-15 | `Strategy.SingleImplementation`、`Strategy.RedundancyElimination` | 同一职责多实现 / 多判定点 |
| F-09 · F-10 · F-11 · F-12 · F-13 | `Strategy.RedundancyElimination` | 无消费方公开面 |
| F-16 · F-17 · F-22 | `AX.StateRootSingleSource`、`RootMissingException` 纪律（"装配错误"语义被降级为运行时异常） | 取根面 fail-fast 边界被误用 |
| F-18 | `AX-CORE.InformationMonotonicity` | 代际指纹信息丢失 |
| F-21 | `A5` 关键决策「重建 + 状态迁移」 | 回建语义与承诺不一致 |
| F-05 · F-24 | `AX.StateRootSingleSource`（SSOT）、DF8 | 兜底造根 / 真源未接通 |
| F-06 | S3（A4）双写收敛 | 单键双写点 |
| F-25 | K9 门禁强度 | 豁免规则过宽 |

### 6.2 与既有待办的边界（不重复展开）

| 既有登记 | 关联条目 | 说明 |
|---|---|---|
| `S3`（A4）双写收敛：`NodeExecutionService.cs:254`（`vars.Set`）与 `:325`（`_store.WriteOutputAsync`）双写 | F-06 | 该双写属 S3 收敛点 1，本清单只登记同族的 `state.Data[SessionId]` 漏收敛 |
| B4-2 迁移期（三条装配入口薄委托） | F-08 | 代码已自注为待收敛，本清单不重复给方案 |
| K9「待迁移 2」+ K10 存量基线 22 | F-05 · F-24 | 归零路径由 S4-4 / S5-2 承担；F-24 指出**归零的前置条件被低估** |

### 6.3 例外说明

- `AX-CORE.AxiomHierarchyStructure`（Kernel 公理）**未**参与本清单：它约束的是规则文件自身的层级，与运行时调用链无关。

---

## §7 诚实边界

| # | 说明 |
|---|---|
| 1 | **F-19 / F-21 / F-11** 为 S4-2（本次）引入或未覆盖的语义，**不是**既有代码问题；F-18 是既有代码 + S4-2 沿用的调用方式共同造成 |
| 2 | **F-22 / F-23 / F-26** 为代码推导（未构造触发用例），已标 `[条件性]`；建议在 B9 先补用例再定修法 |
| 3 | 本清单**未覆盖** LLM 网关链、任务池/调度链、持久化骨干（SQLite/LiteDB）链、UI 函数库链 —— 如需扩展，按链分批追加（编号继续 F-nn） |
| 4 | 行号基于基线提交（Frame `640e1fc`）；代码变动后行号会漂移，**以符号名 + 行号联合定位**，修复后请更新行号或标注"符号已定" |
| 5 | **第三轮（B18–B21，2026-09-12）**：F-50 为**文档对齐**（行为经既有测试确认属有意设计，未改动）；F-51/F-52/F-53/F-54 为**接受现状/决策记录**（理由见各条"状态"列）；F-43 的**跨实例并发残留面**（同一 cell 上的两个 store 并发 `GetOrAdd`）仍以"最后写者胜" —— 端口的 `IDataCell` 无「条件写」原语，须待端口扩展（如 `TryAdd`/`AddOrUpdate` 语义）才能彻底关闭；本轮未覆盖 `unparsedKeys` 门禁化（合法非零，见 F-51）与 F-27 的 Share 侧副本删除（跨仓迁移） |
| 6 | **第四轮（F-55–F-70，2026-09-12）**：本轮对象为 **Agent 侧数据流**（承载者/写点/读点/键空间），非库内聚；F-55/F-56（B22/B23）与 F-57/F-58/F-59/F-60/F-68/F-69（B24/B25）已修复并实测；实施期新增取证：F-69 的实质除「同键双写点」外，还有 **`session_id` 同时承担「执行器 resume 会话 ID」与「StateCenter 会话作用域锚」两义** —— 作用域锚已改稳定 `task_id`（键语义保持 resume，不动 workflow/数据契约）；F-61（死实现）与 F-67（未落地槽键）为**删除或接线二选一**，须先核对无外部消费方（含宿主仓）再动；F-64/F-65 的收敛涉及「观测事实源」「对象槽真源」的方向决策（与 F-27 同型：先决策方向、后执行）；本轮**未覆盖** LLM 网关链、任务池调度链、持久化骨干链、UI 函数库链（沿用第三轮边界） |

---

## §8 维护流程

| 场景 | 动作 |
|---|---|
| 新发现缺陷 | 追加 `F-nn`（不重排、不复用编号）+ 证据等级 + 严重度 + 归口 |
| 修复完成 | 状态列改 `✅`，补提交哈希与判据实测值；**条目不删** |
| 判定为"不修" | 状态列改 `🟦 接受现状`，并在"影响"列写明接受理由与残余风险 |
| 行号漂移 | 更新位置列行号；若符号已重命名，改 `dep` 式记录（旧符号 → 新符号） |
| 与阶段耦合 | 条目归口列须能映射到《下一步实施计划》的 S 步骤或"独立小提交"，否则视为未归口（须当场补） |
