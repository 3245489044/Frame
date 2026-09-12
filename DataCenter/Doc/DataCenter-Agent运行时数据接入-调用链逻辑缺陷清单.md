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
| **P1** | F-24 | 编排器**无状态中心注入面** → 通道 A（状态 KV 接入）实际未接通 | S4-4 前置（拆两步） |
| **P2** | F-01 · F-02 · F-03 · F-14 | 槽分区双实现 / 投影器装配重复 / 取根面双通道 / `Default` 每次新建注册表 | 收敛 |
| **P2** | F-09 ~ F-13 · F-15 | 无消费方公开面（`LiveCount` / 域校验 / `GenerationPayload` / `Swap` / 转发方法） | 收敛 |
| **P3** | F-05 · F-06 · F-07 · F-08 · F-22 · F-23 · F-25 · F-26 | 双造根（K9 待迁移）/ 双写键 / 重复判定 / 迁移期冗余 / 异常覆盖 / 门禁豁免过宽 / 惰性自举冲突 | 分散 |

---

## §1 分析范围（四条调用链）

| 链 | 名称 | 入口 → 出口 |
|---|---|---|
| **链 1** | 根装配与取根 | `DataCenterHost.Boot` → `IDataCenterRoot`/`IRootProvider` → `AgentRuntimeRoot`（`Bind`/`Partition`/`Resolve`）→ 运行时槽分区 → Agent 侧静态门面转发 |
| **链 2** | 换代 | `DataCenterHost.SwapAsync`（Drain/Export/装配/Import/Resume）· `ExtensionGenerationFactory.Swap` · `ExtensionLoadContext.Unload` |
| **链 3** | Agent 组合根装配与槽发布 | `AgentFrameworkHosting.TryBootstrapGuiAsync` → `AgentComposition.StartAsync` → `AgentFramework.StartAsync` → `AgentFrameworkAssembler`（A–E 段）→ `AgentComposition.FromFramework` / `AgentRuntime.EnsureComplete` |
| **链 4** | 轮次状态写路径 | `WorkflowRunner` → `RoundStateMapper`（`Seed`/`Harvest`）→ `vars`(Transient) / `state.Data` → `AgentRunnerCore.StateCenter` |

> **未覆盖**：LLM 网关链、任务池/调度链、持久化骨干（SQLite/LiteDB）链、UI 函数库链（不在本工作线变更面内）。

---

## §2 重复逻辑（Repeat）

| # | 类别 | 位置（证据） | 现象 | 影响 / 严重度 | 状态 |
|---|---|---|---|---|---|
| **F-01** | 重复 | `HostRuntimeSlot.SlotPartition`（`Frame\DataCenter\DataCenter.Hosting\Hosting\HostRuntimeSlot.cs:66-187`）vs `AgentRuntimeRoot.LocalPartition`（`Projects\Plug\Agent\src\Agent.Contract\Runtime\AgentRuntimeRoot.cs:111-162`）`[已核]` | 两套 `IRuntimeSlotPartition` 实现逐行同构（`Register`/`Replace`/`TryGet`/`Get`），**且已语义漂移**：Host 版维护 `_order` 注册序 + 处置期逆序释放，Local 版无注册序、不释放 | 违背 `Strategy.SingleImplementation`；两套语义各自演进必然分叉（谁是真行为不可判） | ⏸ 待收敛 |
| **F-02** | 重复 | `DataCenterHost.cs:115`（`Boot`）与 `:254`（`BuildRoot`）`[已收敛 T3]` | 投影器装配表达式重复两次：`new TierStateProjector(new StateCenterEntrySource(_center), _registry, _codec, generation)` | 首建/换代两条路径的投影器口径可能不一致（改一处漏一处）；建议抽 `CreateProjector(generation)` | ✅ 已收敛（T3：单一 `CreateProjector` 工厂 + 公开 `CreateCoreRoot(generation)` 供宿主 `RootFactory` 复用；判据 `DataCenter.Tests` 59/59，新增「首建/换代同投影器实例」断言，宿主侧重复表达式已消除） |
| **F-03** | 重复 | `AgentRuntimeRoot.cs:25`（`ProviderSlot`）· `:32`（`Binding.Value.Provider`）· `:58`（`CurrentProvider => ProviderSlot.Current ?? Volatile.Read(...)`）`[已核]` | 取根面**双通道承载同一 provider**：AmbientSlot（逻辑链级） + 进程级字段；`:44` `Enter` 返回的句柄被丢弃（"设置语义"） | 两个"真源"叠加；AmbientSlot 值沿逻辑链泄漏且无人 `Restore`；语义边界仅靠注释 | ⏸ 待收敛 |
| **F-04** | 重复 | `AgentComposition.cs:111`（`partition.Replace`）vs `AgentRuntimeRoot.cs:77`（`partition.Register`）`[已核]` | 同一分区**两套写语义并用**：装配期 E 段用"覆盖式替换"，取根解析用"同键异实例即抛" | 同一动作在不同路径一覆盖一抛错；与 F-20 叠加即"并发首访炸"。建议统一为 `GetOrAdd` + 显式 `Replace` 仅限处置期 | ⏸ 待收敛 |
| **F-05** | 重复 | `AgentOrchestrator.cs:93` 与 `WorkflowRunner.cs:89` `[已核]` | 同一职责（"轮次状态真源"）两处构造 `StateCenter`（即 K9 待迁移 2 项） | 生产路径实际用 orchestrator 的（`AgentRunner.cs:133` 显式 `center: core.StateCenter`）→ `WorkflowRunner` 的 `?? new StateCenter(...)` 属**无生产调用者的兜底构造**；但仍是"静默第二真源"的入口面 | ⏸ S4-4 |
| **F-06** | 重复 | `RoundStateMapper.cs:135`（`Harvest` 白名单含 `RoundKeys.SessionId`）与 `WorkflowRunner.cs:252`（`state.Data[RoundKeys.SessionId] = sid`）`[已核]` | `state.Data[SessionId]` **两个写点**（`ExecResult` 已在 `RoundStateMapper.cs:131-132` 收敛为单点，`SessionId` 漏收敛） | 双写漂移面（写入时序不同即出现"最后写者赢"）；`S3`（A4）收敛点 2 的一部分 | ⏸ S3 |
| **F-07** | 重复 | `ExtensionGenerationHost.cs:99-107` 与 `ExtensionLoadContext.cs:329-333 / :342` `[已核]` | "默认 ALC 已有同名程序集则复用"判定做了**两遍**：外层 `Load` 先经 `LoadFromAssemblyName` 触发 ALC，而 ALC 内部（`ExtensionLoadContext.Load`）自己又遍历 `Default.Assemblies` + `Default.LoadFromAssemblyName` | 冗余遍历（每次装载多扫一遍默认 ALC 程序集表）；两处判定未来可能给出不同答案 | ⏸ 待收敛 |
| **F-08** | 重复 `[已登记]` | `AgentFramework.cs:178-180`（`Attach` + `AttachTaskPool`）· `AgentComposition.cs:64`（`AttachDatabase`）· `AgentFrameworkHosting.cs:43-67`（再 `EnsureComplete`）`[已核]` | 三条装配入口分层"薄委托"，各自附带一次槽发布/对账 | 代码自注为 **B4-2 迁移期**（B4-3..B4-6 待收敛）；当前无功能错误，属结构性重复 | ⏸ 迁移期 |

---

## §3 冗余逻辑（Redundant）

| # | 类别 | 位置（证据） | 现象 | 影响 / 严重度 | 状态 |
|---|---|---|---|---|---|
| **F-09** | 冗余 | `Frame\DataCenter\DataCenter.Core\Roots\RootInstanceGuard.cs:14`（`internal static int LiveCount`）`[已核]` | 公开诊断面**零消费方**（注释称"诊断/判据用"，全库无引用） | `Strategy.RedundancyElimination` 判据（¬∃caller→redundant）成立；建议删除或补一条装配期断言使其有实义 | ⏸ |
| **F-10** | 冗余 | `AgentStateDomain.cs:60`（`MissingRegistrations`）· `:88`（`IsFullyRegistered`）`[已核]` | 域登记校验函数**零消费方** → **K5（Kind 注册完备）在装配期并未真正执行**，仅靠 `DataCenter.Tests\AgentStateDomainTests` 事后断言 | 冗余 + 判据虚化：装配期自检缺失（域分裂只能在测试里发现） | ✅ 已接线（S4-3：`AgentRuntimeRootBootstrap` 引导前 `IsFullyRegistered` 自检 + `Agent.Launcher.Tests` 断言） |
| **F-11** | 冗余 `(S4-2)` | `GenerationPayload`（`Frame\DataCenter\DataCenter.Hosting\Hosting\GenerationPayload.cs`，消费点仅 `DataCenterHost.cs:178`）`[已核]` | 跨代载荷**无真实生产/消费方**（`Export`/`Import` 钩子目前仅测试使用）；`Empty` 与 `new GenerationPayload()` 等价 | 预置空壳抽象：若 S4-3 仍无消费方，应显式收窄（明确唯一用途）或降级为内部类型，避免"看起来有机制"的误导 | ⏸ S4-3 判定 |
| **F-12** | 冗余 | `ExtensionGenerationFactory.Swap`（`Frame\CsharpPrj\Modules\Registry\ExtensionGenerationHost.cs:267`）`[已核]` | **零生产调用方**（仅 `Integration.Tests\ExtensionGenerationTests` 使用） | S4-1 交付未接线；S4-3 已消费 `Generation`（根代际 ↔ 扩展代数对齐），`Swap` 仍未接线（留待换代闭环批次） | 🟡 部分接线（Generation ✅ / Swap ⏸） |
| **F-13** | 冗余 | `AgentRuntimeRoot.cs:89`（`ResetForTesting() => Unbind()`）`[已核]` | 纯转发方法（与 `Unbind` 完全等价） | 低；可并入测试夹具 | ⏸ 低 |
| **F-14** | 冗余 | `AgentStateDomain.cs:51`（`Default => Apply(GraphRegistry.CreateDefault())`）`[已核]` | 每次属性访问都**新建并重建注册表**；`Resolve`（`:57`）在未绑定时亦每次新建 | 破坏"注册表同源/同实例"前提（`ReferenceEquals` 判据不可用）+ 重复建表开销 | ⏸ 建议惰性单例 |
| **F-15** | 冗余 | `IRuntimeSlot` 三实现（`HostRuntimeSlot` / `LocalRuntimeSlot`（`AgentRuntimeRoot.cs:102`）/ `EmptyRuntimeSlot`）+ 两个 cell 工厂（`DataCenterHostOptions.RootCellFactory` / `PartitionCellFactory`，`DataCenterHost.cs:113-114`）`[已核]` | 承载面重叠，职责边界仅靠注释区分（"哪个是真路径"不可从类型判断） | 与 F-01 同源；增加新宿主时的选择歧义 | ⏸ 待收敛 |

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
| **F-24** | `AgentOrchestrator.cs:59/93`（`private readonly StateCenter _stateCenter` 仅 ctor 内构造，**无 `SetStateCenter`/构造参数注入面**；消费点仅 `:117 new AgentRunnerCore(_loopConfig, _stateCenter)`）`[已核]` | 即便宿主已持根，编排器仍用私有 `StorageCell("agent-orchestrator")` ⇒ **通道 A（状态 KV 接入）在编排器路径上实际未接通**（A1/A2 只把对象槽挂根，状态真源没挂） | **高** | ⏸ P1（S4-4 需拆两步：先补注入面） |
| **F-25** | `Check-DataCenterGenericity.ps1:200`（`if ($name -like 'static*' -and ($trimmed -match '=>' -or $trimmed -match '\(')) { continue }`）`[已核]` | K9 豁免规则过宽：命中行只要含 `(` 或 `=>` 即跳过 ⇒ `static IRootProvider p = Make();` 之类**可绕过门禁** | 中低（门禁） | ⏸ P3 |
| **F-26** | `AgentUiHost.cs:102-110`（`AttachDatabase` → `:107 EnsureSingleGraphOwnerLocked`）+ `:164-170`（比对 `_referenceGrounding.Graph` 与 `dbResources`）+ `:350-360`（惰性 DB-only 自举）`[条件性]` | **惰性 DB 自举与全框架装配的图 owner 冲突**：若"引用守卫已挂（`AgentComposition.cs:217`）但 `State.Database` 仍为空"的窗口内发生惰性自举，新模块自带**另一个** coordinator ⇒ 断言抛"图库分裂"，而真实原因是装配半途 | 中 | ⏸ P3（需构造用例确认） |

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
| **B6/P1** | F-24：拆两步（先补 `StateCenter` 注入面 + 槽键，再删兜底造根清白名单） | 步骤 1：注入后 orchestrator 使用注入实例（`ReferenceEquals` 断言）；步骤 2：K9「待迁移」= 0 且 `Agent.Core.Tests` 396/396 | 两步各自可撤 |
| **B7/P2** | F-01 · F-02 · F-14 · F-15：抽单一 `IRuntimeSlotPartition` 实现 + `CreateProjector(generation)` + 注册表单例 | `DataCenter.Tests` 不回归；槽语义判据（同键同实例幂等 / 异实例抛 / 处置期逆序释放）集中一处 | 单提交可撤 |
| **B8/P2** | F-09 · F-10 · F-11 · F-12 · F-13：删/接线冗余公开面 | F-10 接线后：装配期 `IsFullyRegistered` 断言（缺失即抛）；F-12 随 S4-3 接线而消解 | 单提交可撤 |
| **B9/P3** | F-22 · F-23 · F-25 · F-26 | F-25：收窄豁免后 K9 重扫仍 EXIT 0（当前无新增违规）；F-26：补一条惰性自举 × 半装配窗口的单测 | 单提交可撤 |

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

---

## §8 维护流程

| 场景 | 动作 |
|---|---|
| 新发现缺陷 | 追加 `F-nn`（不重排、不复用编号）+ 证据等级 + 严重度 + 归口 |
| 修复完成 | 状态列改 `✅`，补提交哈希与判据实测值；**条目不删** |
| 判定为"不修" | 状态列改 `🟦 接受现状`，并在"影响"列写明接受理由与残余风险 |
| 行号漂移 | 更新位置列行号；若符号已重命名，改 `dep` 式记录（旧符号 → 新符号） |
| 与阶段耦合 | 条目归口列须能映射到《下一步实施计划》的 S 步骤或"独立小提交"，否则视为未归口（须当场补） |
