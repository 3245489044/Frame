# 跨语言数据中心（DataCenter）— 架构设计

> **层级**：Frame 平台库（Layer 3） | **建立**：2026-09-12 | **对标**：`Frame\CallCenter` | **协议**：CSG（`Frame\CsharpPrj\Protocol\CSG`）
> **一句话**：把**运行时数据**统一收敛到一个**语言无关**的中枢——数据面用 CSG CanonicalForm，控制面管作用域/分层/事务/订阅/代数/迁移；插件（Agent/Engine/…) 不持有运行数据，只经 DataCenter 读写，于是「热更新」天然「带着运行数据」。
> **通用化重构**：框架层不得含领域词汇（loop/decision…）——设计与实施见 [`DataCenter-通用化重构与实施计划.md`](./DataCenter-通用化重构与实施计划.md)。

---

## 1. 立项动因

| 问题 | 事实 |
|---|---|
| 运行数据散在插件内 | Agent 侧 `AgentUiHost`/`LlmRuntime`/`SkillRuntimeState`/`AgentCoreRuntime`/MCP 回环等数十个 static 槽；`StateCenter` 由插件自建（`Agent.Core\Composition\AgentOrchestrator.cs:93`、`Agent.Core\Nodes\WorkflowRunner.cs:89` 含兜底第二根） |
| 数据与逻辑同命 | 插件卸载 = 数据丢失；且 `ExtensionLoadContext` 当前 `isCollectible: false`，插件根本无法卸载 |
| 通用状态逻辑无归属 | `StateCenter` 家族位于 `Operation\StateHoldDriven`（应用层，Layer 4），契约混实现于 `Share\Foundations\StateManagement` |
| 跨语言无共基 | C#/TS 各有状态设施；但 CSG（C# `CSG.Unified` + TS `TsWorkspace\Protocol`）已是双端就绪的语言无关中间表示，且 TS daemon 侧已用 CanonicalForm 传状态帧（`StateBlockLog.DaemonCsg`） |

**结论**：DataCenter = 把「唯一的状态中心实现」+「CSG 数据面」+「控制器（作用域/分层/事务/订阅/代数/迁移）」合成一个平台层框架，置于所有产品之下。

---

## 2. 工程形态（对标 CallCenter）

```
Frame\DataCenter\
├── Directory.Build.props            平台库约定：CsharpProjectRoot=..\CsharpPrj；输出到 DataCenter\BuildOutput\$(Configuration)
├── GlobalUsings\                    跨程序集公共命名空间（Usings.System / Usings.External）
├── DataCenter.csproj                根聚合工程（ModuleRule 元数据，EnableDefaultCompileItems=false）
├── DataCenter.slnx                  解决方案（根聚合 + 全部子工程）
├── DataCenter.Core\                 ★ 数据面 + 控制面（语言无关核心）
│   ├── Core\StateCenter.cs          IStateCenter 唯一实现（scope 分区/事务/订阅/Actor/生命周期 Provider）
│   ├── Core\StateStore.cs           通用状态存储（IStorageCell 后端；字符串 KV + 对象 KV + StateChanged）
│   ├── Layering\                    StateLayerScheme / LayeredStateKey / LayeredStoreClient（分层键，键翻译收进框架）
│   ├── Decision\                    LoopStateStore / DecisionCenter / LoopKeys / DecisionRecord（决策一等化）
│   ├── Blocks\StateCenterBlock.cs   状态中心初始化块
│   └── Extensions\
├── schemas\csg-runtime-data.schema.json   CSG 扩展域 csg://runtime-data（只扩不改主 schema）
└── Doc\DataCenter-架构设计.md             本文档
```

**依赖方向（单向）**：`产品/应用（Agent、Engine、FrontEndDriven…） → DataCenter.Core → CSG.Unified（协议） + Share.StateManagement（纯契约） + Share.TaskStatusPool（裁决权威） + TConfig / InternalStorage / BasicMethod.Block`。

**产物落点**：`Frame\DataCenter\BuildOutput\$(Configuration)\`（无 TFM 层）；因 `Operation.StateBlock` 已 ProjectReference 本工程，`DataCenter.Core.dll` 同时进入 `CsharpPrj\BuildOutput\$(Configuration)\`（共享权威烘焙目录），Agent 等消费方经其烘焙清单同目录解析。

---

## 3. 数据面：CSG 三投影 ↔ 运行时数据

| 运行时数据语义 | CSG 投影 | 承载 |
|---|---|---|
| 状态值（Config 持久 / Session 跨轮 / Transient 轮内） | **Configuration** | `CanonicalNode{Type=State\|Property, Properties.value}` |
| 状态迁移（轮次五步、熔断升降级、终止裁决） | **StateMachine** | `CanonicalNode{Type=State}` + `CanonicalEdge{Type=Transition}` |
| 数据引用与依赖（`ur_nodes`/`ur_references`） | **Graph** | `CanonicalNode{Type=Record}` + `CanonicalEdge{Type=Reference}` |
| 作用域 / 分层 / 键 | **URI** | `csg://{domain}/{layer}/{scope}/{key}`（现 `{ns}:{scope}:{key}` 的 URI 化） |
| 代数 / 指纹 / SchemaVersion | **Metadata** | `generation` / `fingerprint` / `schemaVersion` |
| 换代事务与提交 | **ExecutionPlan** | `TransformEngine` + `CsgErrorCode` + 版本协商（`CsgProtocol`） |

**契约纪律**：CSG 主 schema 与 `CsgProtocol v1.0.0` **语义冻结**；本框架只在 `metadata.schema = "csg://runtime-data"` 域内扩展（R-DC1）。


---

## 4. 控制面能力（规划与现状）

| 能力 | 现状 | 说明 |
|---|---|---|
| 作用域分区 scope | ✅ 已归位（`StateCenter.GetOrCreateScope`） | 根 cell 下的子 cell，父链回退 |
| 分层 Config/Session/Transient | ✅ 已归位（`StateLayerScheme`/`LayeredStoreClient`） | 广播分级：Transient 静默写（`SetQuiet`），Session/Config 走两级广播 |
| 事务 | ✅ 契约在 Share（`IStateTransaction`），实现待上收 | 见 §5.2 |
| 订阅中枢 | ✅ 契约在 Share（`IStateSubscriptionHub`），实现待上收 | 同上 |
| 决策状态化 | ⏳ 待外移（G1） | 现暂置 `Core\Decision\*`；依通用化重构 **必须移出**至 `DataCenter.Extensions.LoopDecision`（框架零领域词汇），Core 只留通用状态能力。裁决语义权威仍在 `Share.TaskStatusPool.Loops`（R7） |
| 代数与在途 | ⏳ 规划 | 上移 CallCenter 的 `HotReloadVersionStore`/`TrackHotReloader`/原子换快照 |
| 迁移 | ⏳ 规划 | `SchemaVersion` + CanonicalForm diff/apply + 失败保留旧代数 |
| 终端面 Adapter（C#/TS/PS/MCP） | ⏳ 规划 | TS 复用 `TsWorkspace\Protocol`；MCP 面暴露数据面 |

---

## 5. 迁移台账（StateHoldDriven → DataCenter）

### 5.1 已迁（2026-09-12，编译 + 73/73 单测全绿）

| 原位置（`Frame\CsharpPrj\Operation\StateHoldDriven`） | 新位置（`Frame\DataCenter\DataCenter.Core`） | 分类 |
|---|---|---|
| `Core\StateCenter.cs` | `Core\StateCenter.cs` | 通用 |
| `Core\StateStore.cs` | `Core\StateStore.cs`（**剥离产品端口**，只留状态 KV 能力） | 通用 |
| `Layering\{StateLayerScheme,LayeredStateKey,LayeredStoreClient}.cs` | `Layering\` | 纯通用 |
| `Decision\{LoopStateStore,DecisionCenter,LoopKeys,DecisionRecord}.cs` | `Decision\` | 通用 |
| `Blocks\StateCenterBlock.cs` | `Blocks\StateCenterBlock.cs`（改吃通用 `StateStore`） | 通用 |
| `Extensions\StateBlockExtensions.cs` | `Extensions\` | 通用 |
| — | 新增 `StateHoldDriven\Core\OperationStateStore.cs` | 产品侧派生：`StateStore` + 服务定位器端口（Registry/WorkflowRegistry/Executor/Pipeline） |

**兼容策略**：命名空间保持 `Operation.StateBlock.*` 不变 → 消费方（Agent/FrontEndDriven/Engine/测试）**零改动**；后续如需 `DataCenter.Core.*` 命名空间，以 `[assembly: TypeForwardedTo]` 平滑切换。

### 5.2 待迁（剩余步骤）

| 项 | 处置 |
|---|---|
| `Share\Foundations\StateManagement` 的**实现**部分（`Core\{StateOrchestrator,StateManager,StateActor,ScopeLifecycleStateMachine,StateLifecycleProviders,StateBlock}.cs`、`StateStorage\Core\*`） | 上收 DataCenter.Core；`Share.StateManagement` 降为**纯契约**（Abstractions + Models 值类型）；**R1 红线随之修订** |
| `Share.StateManagement` 中与本主题无关的子域（`Pooling\**` 工作池、`Stages\**` 执行阶段、`Core\{NodeExecutionCoordinator,ExecutionStateMachine,TaskExecutionState,ExecutionStateStore,ExecutionParameterStore,TtlCache,GenericStore}\`） | **不并入 DataCenter**；另行择址（执行/池域），避免把无关域绑进数据中心 |
| 产品装配件（`Blocks\{Registry,FunctionStorage,FileDriven,Executor,AppService}StateBlock`、`Pipeline\StatePipelineBuilder`） | 留 `StateHoldDriven`（或另立 `Operation.StateHoldDriven.App`） |
| `StateHoldDriven\Daemon\**`、`FunctionStorage\**` | 与状态管理无关（误置）；**单独立项校正**，不随本迁移 |
| 代数 / 迁移 / HotSwap | 从 CallCenter 上移为共享件（CallCenter 反向引用，消除两份实现） |
| Agent 静态槽与插件内 StateCenter | 迁入 DataCenter 分区（`RuntimeSlot`），删除 `new StateCenter(...)` 与兜底第二根 |

---

## 6. 红线

| # | 红线 | 状态 |
|---|---|---|
| **R1'** | **修订**：`Share.StateManagement` 降为纯契约；状态中心实现（含 StateOrchestrator/订阅/事务/Actor）唯一归属 `DataCenter.Core` | 待执行（§5.2） |
| **R2** | DataCenter 不引 FrontEndDriven（UI/ECS 无关） | 保持 |
| **R7** | 裁决语义唯一权威仍在 `Share.TaskStatusPool.Loops`；DataCenter 只做状态落地，不复制裁决 | 保持 |
| **R8** | 确定性核心（Budget/Token/Flag/Round/LastEval）单一写口 | 保持（`LoopStateStore.Save`） |
| **R-DC1** | CSG 主 schema 只扩不改；`CsgProtocol v1.0.0` 语义冻结 | 保持 |
| **R-DC2** | `DataCenter.Core` 不引语言栈（FileDriven/FunctionStorage/WinForms/Agent/Engine） | 保持（端口已剥离至 `OperationStateStore`） |
| **R-DC3** | 产品 → DataCenter 单向；DataCenter 不得反向引用任何产品 | 保持 |

---

## 7. 验收判据

- **编译**：`DataCenter.slnx`、`Operation.csproj`、`Agent.sln` 全绿（**已实测**）。
- **行为等价**：`StateHoldDriven.Tests` 全绿（**已实测 73/73**，测试代码零改动即证明等价）。
- **架构**：`grep` `DataCenter.Core` 内无 `FileDriven|FunctionStorage|Agent|GameEditor|WinForms` 类型；`new StateCenter(` 在 `Projects\Plug` 内归零（目标态）。
- **跨语言**：同一 CanonicalForm 在 C# 与 TS 两侧往返等价（同一 `csgVersion`）。
- **热更新**：换代前后 `budget/token/flag/round/lastEval` 连续；旧代数 ALC `WeakReference.IsAlive == false`。

---

## 8. 风险与诚实边界

| 项 | 说明 | 缓解 |
|---|---|---|
| 第三套状态实现 | DataCenter 若自建裁决/存储将与既有双份 | 红线 R7 + 只做状态落地 |
| 共享栈不可卸载 | `WorkFlowMethod`/`Share.*`/`Plate.*` 与契约域不可卸载；UI 控件/native 不可热换 | 三域清单化（契约/共享栈/实现域） |
| `Share.StateManagement` 降契约的波及 | 被 7 个工程引用（DataStoreDriven/FrontEndDriven/StateHoldDriven/WorkFlowMethod/CommandFramework/AiScheduling/Share） | 分批：先建 DataCenter 同型实现 → 类型转发 → 置 `dep` 退役；每步编译 + 测试 |
| 输出落点双份 | `DataCenter.Core.dll` 同时存在于 DataCenter 与 CsharpPrj 烘焙目录（后者为依赖复制） | 权威目录 = DataCenter；CsharpPrj 侧仅为消费解析副本 |
| 换代非零成本 | 需交换点 + 排空；"秒级零停机"仅在交换点语义成立时成立 | 显式 `ISwapPoint`，不做无条件承诺 |

---

## 9. 与既有资产的关系

| 资产 | 关系 |
|---|---|
| `Frame\CsharpPrj\Protocol\CSG` | 数据面协议权威；**只扩不改** |
| `Frame\CallCenter` | 平级平台；`HotReloadVersionStore`/`TrackHotReloader`/原子换快照 **上移共享**，CallCenter 反向引用 |
| `Frame\CsharpPrj\Share\Foundations\StateManagement` | 现：契约 + 实现；目标：**纯契约**（R1'） |
| `Frame\CsharpPrj\Operation\StateHoldDriven` | 现：通用 + 产品混合；目标：**仅产品装配件** |
| `Projects\Plug\Agent` | 终端面消费者（运行数据归位 → 插件可换代） |
| `Projects\Plug\Engine`、`Frame\CsharpPrj\Operation\FrontEndDriven` | 第二/第三消费方（`EngineStateCenter`、`StateCenterFactory`） |
| `Projects\PrjBridge`、`Projects\FileDriven` | 宿主：持有 DataCenter 单实例 + 可收集 ALC 代数 |

