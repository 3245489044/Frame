# StateBus 并发冲突治理与线程通用框架提取设计

> 版本：1.6（P1–P4 + 并发原语优化 + P6 上下文边界已实施；P5 部分记档）｜ 模块：`CsharpPrj/Modules/Threading`（`Modules.Threading`）｜ 依赖：无外部依赖（独立叶子模块；Channels inbox + AsyncLock 内建）
> 定位：与《AsyncExecutionFramework-扩展设计.md》（异步长任务执行，AEF）**互补**的**共享状态并发（StateBus）治理层**。
> AEF 管『长任务怎么跑、如何被打断』；本文管『共享可变状态被多线程同写同读 + 同步广播，怎么隔离、如何把 Agent 与 StateManagement 的线程逻辑提取成通用框架』。

> **实施状态（2026-09）**：✅ P1（通用原语）✅ P2（Center 门面化）✅ 并发原语优化（AsyncLock + Channels）✅ P3（StateCenter 装配口 + PublishDispatcherAdapter）✅ P4（StateStore.StateChanged 转发可经统一派发器，默认 Inline）。✅ **P6(a/b/c)**：`RunContext` 迁 `Modules.Threading.AmbientSlot`；`RunContext.Enter` 作用域句柄 Dispose 时清 `SchedulerContext`（E1 陈旧/跨 run 泄漏修复）；`NodePersistOutbox` 后台持久化 worker 以 `ExecutionContext.SuppressFlow` 启动（切 run 上下文泄漏）。Agent.Runtime/Core/Axioms 构建 0 错误 0 警告。✅ **P6 评估（TaskPool/UI）**：TaskPool 消费 worker 为组合根长驻宿主（无外层 run 作用域可切）→ 流切断 no-op；UI 封送已单点（WinFormsUIDispatcher 唯一 IUIDispatcher）→ 无需收敛。⏳ **P5（Vars 读快照/轮提交）**：涉及活体 Agent 步路径数据语义重设计（StateCenter.Transient 每键广播→同轮合并提交），改动风险高且 P3/P4 已门控广播；记档为**后续运行时重设计项**，不在编译层盲改。P3/P4/P6 其余运行时切异步仍按『先 Inline 零回归、后逐消费方切』纪律待真机验证后激活。


---

## 1. 问题定位 —— 谁与谁咬合

### 1.1 一句话根因
线程会卡，是因为 **Agent 侧 ④(Vars 适配：`StateBackedDataSpace`/`StateContextAdapter`) 把每步读源/编排的 Vars 落到 ①(共享 `StateCenter`/`StorageCell`/`StateSubscriptionHub`，带两级同步广播) 之上**；而这两者又被 ②(UI/ECS 投影：`EcsStateAccessor`/`WinFormsUIDispatcher`) 与 ③(`RunContext` AsyncLocal + Outbox 后台持久化线程) 从**多线程并发进出**。
同一批共享可变状态被后台引擎、UI、后台持久化同时读写 + 同步广播，又无隔离 —— 任一线程撞上（锁持卡死 / 广播内封送等待 / 读-写交错）即卡，写线程被订阅者焊死在 `Write` 内污染全局。

### 1.2 两级同步广播（① 的最要害）
`StateCenter.Write`（StateCenter.cs:116-122）一次写触发 **两条各自独立的同步扇出**：
1. `_cell.Set(...)` → `StorageCell.Set`（StorageCell.cs:25-39）末尾 `DataChanged?.Invoke` → `StateStore.OnCellDataChanged`（StateStore.cs:163-166）→ 各 `StateChanged` handler；
2. `_hub.Publish(...)` → `StateSubscriptionHub.Publish→Dispatch`（StateSubscriptionHub.cs:88-117）→ **同步逐个**调订阅 handler。

两条都在**写者线程**上执行；任一条链出现阻塞/封送/反向等待，写线程即被焊死。

> 现状仅 (2) 已开解耦口（`IPublishDispatcher` 注入点，Dispatch 102-115：dispatcher 非空则转异步派发）；(1) 仍为内联同步事件。治理需两线都走同一派发原语（见 §4.2）。
> 另注：`StateCenter._hub` 为私有字段、`ConfigureDispatch` 只挂在具体类 `StateSubscriptionHub` 上而不在 `IStateSubscriptionHub` 接口 —— 组合根目前无法注入派发器，属**装配口缺口**（§4.2 修复）。

### 1.3 四道冲突面 + 一个命名冲突
| 面 | 冲突 | 源文件 | 目标治理 |
|---|---|---|---|
| 写者多方 | 引擎后台/UI/Outbox 后台同写共享状态、多写门并存 | `WriteCoordinator` / `SingleWriteGuard`（HierarchyMethod/Center）｜ `StateCenter.Write` | 单一确定性写路径 + 串行写门（上收通用层） |
| 写即同步广播 | 写线程被订阅链 / UI 封送反向阻塞 | `StateSubscriptionHub.Dispatch`｜ `StorageCell.DataChanged` | 派发解耦，写线程只提交不执行回调 |
| 隐式上下文跨线程 | AsyncLocal 沿逻辑链隐式透传、跨后台/UI 边界泄漏 | `RunContext`（RunContext.cs:18,29）｜ `AgentRunCancellation` | 显式 Ambient 作用域（只读快照）+ 工作边界切断 |
| UI/ECS 直读共享态 | 投影/封送直读共享存储、多套 dispatcher | `EcsStateAccessor`｜ `WinFormsUIDispatcher`（IUIDispatcher）｜ WorkFlowMethod/WinFormsThreadDispatcher | 单一封送抽象 + 读快照 |
| 命名冲突★ | 两处同名 `ExecutionScope` 异义 | `Modules.Threading.ExecutionScope`（ambient 当前作用域） vs `Operation.HierarchyMethod.Center.ExecutionScope`（可取消有界执行） | 有界执行体上收为通用 `BoundedExecution`；`ExecutionScope` 名保留给 ambient |

### 1.4 AsyncLocal 的隐式共享本质（③ 的坑）
`RunContext.Current` 是 `AsyncLocal<RunContext?>`（RunContext.cs:18），`Enter`/`ScopeRestore`（32-43）压栈；`AgentRunCancellation` 全部只读 `RunContext.Current`。AsyncLocal 的价值（同 await 链内透传）也是其风险：
上下文沿**逻辑执行链**隐式流动；当某步的真实线程跳到 TaskPool 后台 / Outbox 持久化线程 / UI 投影时，链是否延续取决于 ExecutionContext 是否被捕获——既可能把本不该共享的 `SchedulerContext`/预算/令牌带过边界（写放并行双写），也可能在断链处读到 null（行为依时序漂移）。
治理 = 让显式边界显式化（进 worker 按意图传入或切断），而非指望隐式链正确。

---

## 2. 治理原则
| 原则 | 含义 | 对应冲突面 |
|---|---|---|
| P1 单一确定性写路径 | 每个确定性写目标只允许唯一注册写者经串行写门进入；平行实现 fail-fast | 写者多方 |
| P2 广播与写解耦 | 写线程只提交发布/落值；订阅回调经派发器在目标线程模型执行，绝不反向阻塞写者 | 写即同步广播 |
| P3 上下文显式 + 边界切断 | ambient 仅承载只读/不可变快照；进入 TaskPool/Outbox 等后台 worker 时按意图显式传入或切断（不隐式泄漏） | 隐式上下文跨线程 |
| P4 封送单一收敛 + 读快照 | 封送只走唯一 dispatcher 抽象；UI/投影读写共享态经快照，不压到写者热路径 | UI/ECS 直读共享态 |
| P5 通用层零依赖 | 所有新原语落入 Modules.Threading（叶子、无外部依赖），源侧只留适配/门面 | 全部 |

---

## 3. 通用框架目标形态（Modules.Threading 增量）

### 3.1 目录与命名（先消冲突）
保留既有根原语不变：`AsyncLocalContext` / `ThreadContext` / `ThreadingBridge` / `ReentryGuard` / `ExecutionStatus` / `ExecutionScope`（＝ambient 当前作用域）。

新增子目录（全部**无外部依赖**，仅 System / Collections.Concurrent / Threading.Tasks）：

```
CsharpPrj/Modules/Threading/
├── AsyncLocalContext.cs / ExecutionScope.cs / ThreadContext.cs / ThreadingBridge.cs / ReentryGuard.cs   (既有，保留)
├── Scope/
│   ├── AmbientSlot.cs        # 显式命名、可嵌套、只读快照纪律的作用域槽（统一 AsyncLocal 用法）
│   └── FlowBoundary.cs       # 后台 worker 入口『不继承外层 ambient』的执行边界（切断隐式泄漏）
├── Bounded/
│   └── BoundedExecution.cs   # 有界执行载体：预算耗尽→强类型超时（吸收 Center.ExecutionScope 语义）
├── Write/
│   ├── SingleWriterRegistry.cs   # 进程级单一确定性写者注册/断言（= SingleWriteGuard 泛化）
│   └── SerialWriteGate.cs        # 串行写门 + 有界等待（= WriteCoordinator 泛化，依赖 Bounded）
├── Dispatch/
│   ├── IThreadDispatcher.cs      # 唯一泛化派发形态：void Post(Action)
│   ├── InlineDispatcher.cs       # 同步直跑（现状零回归）
│   ├── SerialDispatcher.cs       # 单后台消费者保序
│   ├── MainThreadDispatcher.cs   # 包装 SynchronizationContext / 自定义（ThreadContext 委托目标）
│   └── RouteDispatcher.cs        # 按订阅/订阅者上下文路由
└── docs/
    └── StateBus并发冲突治理与线程通用框架提取设计.md   (本文)
```

命名冲突消解表：

| 旧（异义同名/业务属主） | 消解后归属 |
|---|---|
| `Operation.HierarchyMethod.Center.ExecutionScope`（可取消有界执行，152 行） | 逻辑上收为 `Modules.Threading.BoundedExecution`（RunStepAsync/RunRoundAsync/RunStoreAsync/Derive/Cancel 语义保留，仅换归属名）；Center 改薄门面或删除 |
| `Modules.Threading.ExecutionScope`（ambient 当前作用域） | 保留，语义不变 |
| `Operation.HierarchyMethod.Center.WriteCoordinator` | `Modules.Threading.SerialWriteGate`（内部有界等待依赖 BoundedExecution） |
| `Operation.HierarchyMethod.Center.SingleWriteGuard` | `Modules.Threading.SingleWriterRegistry` |
| `Share.IPublishDispatcher`（新，仅 23 行） | 保留为**薄门面**，实现委托 Modules.Threading dispatch 内置实现；`StateSubscriptionHub.ConfigureDispatch` 组装它 |

> 理由：上述四类 Center 原语都只依赖 System，天然满足『无外部依赖叶子模块』约束，可直接上收；业务侧消费方仅需换 using/类型名。

### 3.2 派发抽象（P2 的家）
- `IThreadDispatcher { void Post(Action delivery); }` 为唯一泛化形态（把一次投递放到目标线程模型）。
- 内置实现：`InlineDispatcher`（同步直跑＝现状零回归）；`SerialDispatcher`（单后台消费者保序，供写者解耦广播）；`MainThreadDispatcher`（包装 SynchronizationContext/自定义，即 ThreadContext 现逻辑的委托目标）；`RouteDispatcher`（按订阅上下文选目标，供 UI/ECS 投影回投）。
- 家 = `Modules.Threading.Dispatch`；业务层只留门面/适配，不再各自实现派发。

### 3.3 Ambient 与边界（P3 的家）
- `AmbientSlot<T>`：显式命名、可嵌套、Enter/Exit 返回句柄；纪律为 **T 应是只读/不可变快照**（禁放可写句柄，如可变的 `SchedulerContext`）。`ExecutionScope.Current`、`RunContext.Current`、`DiagnosticContext` 统一迁到它。
- `FlowBoundary`：把一段工作包进『不继承外层 ambient』的执行；用于进入 TaskPool（`LlmTaskExecutor`/`RuleTaskExecutor`）与 Outbox（`NodePersistOutbox`）后台 worker 的首帧，或在边界按意图显式传入所需最小句柄。

### 3.4 只读视图（P4 读侧，演进项）
`NodeReadView`（HierarchyMethod/Center，90 行）已表达『单一读源 + 快照/过滤』思想；其线程面提升为框架只读视图原语（可选新增 `ReadOnlyView`），供 UI/投影与 Vars 读侧统一消费，读不回写者热路径。

### 3.5 并发原语优化（Channels + AsyncLock）
架构级优化：引入 `System.Threading.Channels` 与 `AsyncLock`，替换手动队列/裸 SemaphoreSlim，收敛为类型化并发原语。
- **`AsyncLock`（新增，模块根，内建原语）**：await 式互斥 + AsyncLocal **重入死锁自证**（同逻辑链重入 → fail-fast）；可配超时（TimeoutException），可与 `BoundedExecution` 组合得「有界获取 → Store 超时自证」。`SerialWriteGate` 内部已改用它。
- **`SerialDispatcher`（改，基于 Channel）**：用 `Channel<T>` 取代 BlockingCollection——可选**有界容量 + 背压**（`BoundedChannelFullMode.Wait`：`PostAsync` 限流写者 / `Drop` 丢投递策略由装配决定）；`Post` 无界投递不阻塞写线程、满/已释放退化为内联（不丢）；原生 `await foreach` 消费支持优雅排空。
- **依赖策略（诚实说明）**：`System.Threading.Channels` 为 **.NET inbox**（仓库多处直接 `using`，含本模块 `Streaming/`，无需 PackageReference）；`AsyncLock` 无官方类型，作为 **Modules.Threading 内建原语**——两者均不引入外部 NuGet，保持模块「无外部依赖（叶子）」不变式。

---

## 4. 具体提取映射（源 → Modules.Threading）

### 4.1 Agent 侧 → Modules.Threading
| 源（Agent） | 现状 | 目标原语 / 改动面 |
|---|---|---|
| `RunContext.Current`（AsyncLocal + Enter/ScopeRestore，RunContext.cs:18,29,32-43） | 隐式跨线程、可含可写 SchedulerContext | 改走 `AmbientSlot<RunView>`（只读快照：TaskKey / RunToken / Budget）；Enter/Exit 句柄语义保留；可写 `SchedulerContext` 移出（避免隐式双写，对应 E1 类） |
| `AgentRunCancellation`（静态只读 Current，AgentRunCancellation.cs:19-40） | 读隐式、断链读 null 依时序 | 门面保留，读取经 `AmbientSlot<RunView>.Current`；断链 null 语义显式化（无作用域→None/null 写清楚） |
| `AgentLoopFunction.CheckCancellation/CheckLlmGateAsync`（AgentLoopFunction.cs:22-34） | 从 `AgentRunCancellation` 间接读 | 消费方改为显式取注入句柄，不再依赖 AsyncLocal 兜底 |
| `TaskPool`（LlmTaskExecutor / RuleTaskExecutor）／ `NodePersistOutbox` worker 入口 | 继承外层 ExecutionContext | 首帧 `FlowBoundary` 切断；任务池所需 run 句柄**显式**传入 → 后台不读到错误 run |
| `StateBackedDataSpace` / `StateContextAdapter`（④ 缝合处） | Vars 直接落 `StateCenter.Transient`、每键广播 | 写经 `SerialWriteGate` 单写 + hub 异步派发；读走只读视图/快照；可选同轮合并提交（§5 P5） |

### 4.2 StateManagement / UI 侧 → Modules.Threading
| 源 | 现状 | 目标 |
|---|---|---|
| `StateSubscriptionHub.Dispatch`（②，StateSubscriptionHub.cs:88-117） | 已开 `IPublishDispatcher` 口，无装配、缺组合根注入点 | 组合根注入 Modules.Threading dispatcher（默认 Inline 零回归 → 逐消费者切 Serial/Route）；**修复装配口**：把 hub 预装 dispatcher（StateCenter 构造入参）或给 `IStateSubscriptionHub` 增 ConfigureDispatch |
| `StorageCell.DataChanged` / `StateStore.StateChanged`（① 第一线，StorageCell.cs:38 / StateStore.cs:163-166） | 内联同步事件 | 收敛到同一派发原语：写侧仅落值，事件转发统一经 dispatcher 出队；StateChanged 消费方逐步异步化 |
| `StateCenter.Write`（② 两级广播，StateCenter.cs:116-122） | 写线程跑两级同步 | 拆为：写 Cell + 提交发布（进 dispatcher 队列），写线程即返回；写路径本身经 `SerialWriteGate` 收敛（P1） |
| `EcsStateAccessor` / 投影读（FrontEndDriven） | UI 投影直读共享态 | 读经只读快照视图（读不回写者热路径）；对共享态的回写统一封送 + 单写门 |
| `IUIDispatcher`（WinFormsUIDispatcher，FrontEndDriven/Lifecycle）+ `ThreadContext` | 两套 UI 封送并存 | `IUIDispatcher` 为唯一契约；`WinFormsUIDispatcher` 经 `Modules.Threading.MainThreadDispatcher` 承载；WorkFlowMethod/WinFormsThreadDispatcher（反射实现）弃用收敛 |

### 4.3 引用面 / 分层前置
Modules.Threading 无外部依赖（叶子）。本次需给 `Operation/HierarchyMethod`、`Share/StateManagement` 与 Agent 引用项目新增对 `Threading.csproj` 的引用（FrontEnd/BackEnd/WorkFlowMethod/EventBindingOrchestrator **已引用**）。
依赖方向单向：源侧 → Threading；Threading 不反向引用任何业务项目，不构成环。

---

## 5. 实施顺序与验收（每步独立可编译）
| 步 | 改动 | 验收 |
|---|---|---|
| P1 | Threading 增目录与通用原语：Scope/AmbientSlot、Scope/FlowBoundary、Bounded/BoundedExecution、Write/SingleWriterRegistry、Write/SerialWriteGate、Dispatch/IThreadDispatcher + Inline/Serial/Main/Route | `Threading.csproj` 独立构建 0 错误 0 警告；引用面打通（HierarchyMethod / StateManagement / Agent 可引用） |
| P2 | 命名消解：Center.ExecutionScope 逻辑迁 BoundedExecution；Center 三件套换用上收原语（WriteCoordinator→SerialWriteGate、SingleWriteGuard→SingleWriterRegistry），Center 留薄门面或删除 | Operation 构建通过；Center 行为等价（写门有界等待/单写断言语义保留） |
| P3 | StateSubscriptionHub 装配 Modules.Threading dispatcher（默认 Inline 零回归）→ Serial/Route 逐消费方切 | 写线程不再被订阅 handler 阻塞；同一发布 delivery 保序 |
| P4 | StorageCell/StateStore/StateCenter 写路径解耦：写只更新 + 提交发布，事件转发统一出队 | 两级广播都脱离写线程热路径 |
| P5 | Vars（StateContextAdapter/StateBackedDataSpace）读快照化；可选同轮多次写合并为一次 delivery | 读侧不回写者热路径；读快照一致（对齐 StateSnapshot） |
| P6 | RunContext/AgentRunCancellation 走 AmbientSlot；TaskPool/Outbox 入口 FlowBoundary；UI 封送收敛 IUIDispatcher（MainThreadDispatcher 承载）；SchedulerContext 移出 RunContext | 后台 worker 不再隐式继承 run 上下文；隐式双写消除；UI 封送单点 |

> 验收门（每步）：`Threading.csproj` / StateManagement 项目 / Operation(HierarchyMethod) 项目 / `FrontEndDriven.csproj` / Agent 相关 csproj 0 错误 0 警告。
> 顺序依赖：P1（原语就位）→ P2（Center 收敛，先跑起来）→ P3/P4（广播解耦，从 P2 稳定的消费方开始逐个切）→ P5（读侧）→ P6（上下文边界，改动面最广、放后）。
> 关键纪律：**先 Inline 零回归、后逐消费方切**——把内联广播改异步会改变观察时序，禁止一次性全量切换。

---

## 6. 边界与诚实说明

- **本框架解决**：写者多方/双写（单写门 fail-fast，对齐 `DeterministicCoreDisjoint`）；广播阻塞写者（派发解耦，写线程只提交）；AsyncLocal 隐式跨线程（显式 Ambient + FlowBoundary 切断）；UI 封送多套并存（单 dispatcher 收敛 + 只读快照读）。
- **本框架不解决**：具体某一步业务 await 为何慢/阻塞（那是节点执行超时/AEF 域）；UI 消息泵本身卡死（那是宿主渲染域，需 WebView2/WinForms 泵侧处理）。
- **时序语义**：异步派发引入观察延迟；若某消费者依赖『写后立即可见』，须先在 P3 以保序 Serial 验证，再切异步，避免时序回归。
- **性能取舍**：广播出队有额外开销；建议先按 (namespace, scope) 快照版本、同轮多写合并后投递（保序前提下），避免每键独立出队放大负载。
- **语义对齐**：写门 / 派发 / 只读快照分别落实 axioms 的 `DeterministicCoreDisjoint` / `StateSnapshot` / `StateMerge`；本层为**工程实现**，不新增公理符号（DSL 侧不产生新 `[AX]`，也不需重编 references.md）。
- **验收证据**：并发冲突以『写线程不被订阅链/封送阻塞 + 后台 worker 不隐式继承 run 上下文 + 单写门无越权』三性质衡量，不以单点耗时计。

---

> 关联文档：《AsyncExecutionFramework-扩展设计.md》（AEF，异步长任务）｜《现有代码应用集成指南.md》（AEF 集成）——本文与其互补，不重叠。



