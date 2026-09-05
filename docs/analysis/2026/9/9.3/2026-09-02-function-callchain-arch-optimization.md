# 从「函数调用链」优化整体架构 — 落地文档

> **日期**：2026-09-02
> **触发**：`agent_mcpinvoke_create_task_test.json` 运行卡死（`rule_match` 步骤执行内 `InvokeAsync` 未返回，120s 无输出后由测试脚本判死强杀），已于 19:09 / 19:24 两次独立复现。
> **定位**：承接 9-01 卡死重构（P0 已回退 / P1·P3·P5 保留）与 9.2 写串行化、FunctionLibraryLoader 根因，**以「函数调用链」为唯一主线**给出整体架构优化方案与分阶段落地清单。
> **状态**：Stage 0（取证 + 本档）推进中 · Stage 1–5 待实施
> **原则**：机制强制 > 打补丁；单次取证定方向（吸取 9-01 R1 未取证即改的教训）；小步、可回退、逐 sln 编译。

---

## 〇、一句话结论

> 卡死不是 `rule_match` 这个函数写错（其内核是纯文本规则、零 LLM），而是「单步函数调用 await 永不返回 + 单步无独立超时/watchdog + 资源(规则集/决策设施)隐式首触 + 线程/封送混杂」在**每轮必经的函数调用链**上叠加的必然。治理对象应是**这条调用链本身**——把散在 7 层的横切收拢为正交中间件，施加统一的取消/超时/观测/封送/决策信号能力。

---

## 一、函数调用链现状（源码核实版）

一次「步骤函数调用」穿过 7 层 async 包装（层号 = 缺陷归属用）：

```
L1 SnapshotDrivenLoopEngine<AgentTaskItem,AxiomRound>（Share 唯一 while）
L2 RoundLoopDriver.RunSingleRootAsync
     ├─ CreateLeaseContext（派生租约，共享 Data/Budget/Token/Flag）
     ├─ RoundLifecycle.RunAsync（五步：Execute→Collect→Evaluate→Decide→Adjust）
     │     · 仅当 roundTimeout>0 时经 Task.WhenAny(round, delay) 单轮超时 → 记 TO（见 §九）
     │     └─ executeStep = AgentRunner.ExecutePipelineRoundAsync
     │           └─ _roundPipeline.ExecuteAsync（pre_round→lease_derive→run_round 三阶段）
     │                 └─ WorkflowRunner.RunRoundAsync（骨架 5 步，success=decision）
     │                       └─ NodeGraphOrchestrator.RunRoundAsync（foreach step）
     │                             └─ RoundStepInvoker.ExecuteStepAsync（"步骤开始/完成"，guard echo、return_spec）
     │                                   └─ NodeExecutionService.ExecuteAsync
     │                                         └─ INodeFunctionInvoker.InvokeAsync("rule_match",…)
     │                                               └─ WorkFlowMethodUsage.CallFunctionAsync（FunctionContext）
     │                                                     └─ UnifiedRegistryCenter → handler(ExecuteRuleMatchAsync)
     └─ next = onRoundCompleted → NextHybridRoundAsync（每轮边界；无 roundTimeout 保护，见 §九候选A）
```

**“何时停”存在 3 套来源**：管道内 `rule_match` decision 词 / `RoundLifecycle.Decide`（eval→Token/Budget→TO/TM）/ 调度导演（Llm/Config）提议。

**循环单轮超时现状（本轮取证关键）**：`AgentLoopConfig.DefaultRoundTimeout=90s` 默认开启；`RunMainLoopAsync` 把 `_config.RoundTimeout` 传给 `RoundLoopDriver`（带 `Task.WhenAny(round,delay)` 超时分支）；并已包裹 `StallDetector`（阈值 `max(30, 2×RoundTimeout)`，仅观测不打断）。组合根 `BuildLoopConfig()` 与 `AgentOrchestrator()` 无参构造都保留默认 90s、**全库无把 `RoundTimeout` 置 0 的代码点**。而两次复现均卡 120s 无 `TO` —— 详见 §九判定。
---

## 二、缺陷清单（按调用链归因）

| # | 缺陷 | 代码/日志证据 | 后果 |
|---|------|--------------|------|
| **I1 链路过深、职责重叠** | 7 层 async 包装，每层各自计时/取消/错误归一/决策 | 8.23-F1、langgraph 对比“两层路由决策” | 异常/决策穿透多层，难读难测，改一层伤全链 |
| **I2 横切内联、各自为政** | guard echo、return_spec、LLM 门、错误分类、Outbox 判空、终态信封散在 `RoundStepInvoker`/`NodeExecutionService`/handler | 两次复现四步秒过、唯独 rule_match 卡 | 每新增函数要重读全部横切，同类能力不可复用 |
| **I3 单步无独立超时/watchdog** | 超时在**轮级**(execute 外层)默认 90s；**单步级无**；rule_match 卡无自愈 | 日志 120s 无输出、靠测试脚本判死 | 函数级卡死无法自证定位 |
| **I4 资源隐式首触** | `rule_match` 是首触 `RuleSetLoader.Default` 惰性加载 + 决策设施的步骤；叠加全程 UI 线程警告（CoreWebView2/宿主基准决议失败/ECS 尺寸/回流自检 UI 线程异常） | 日志多处 UI 线程异常 | 一旦经 UI 封送/共享锁 → 永久死锁且不可观测 |
| **I5 装配/发现依赖隐式 Resolving** | `FunctionLibraryLoader` 反射 `Type.GetType` 靠 `AssemblyLoadContext.Resolving` 兜底 | 9.2-FunctionLibraryLoader | 函数库是否可被调用链发现 = 运行时救火，非结构保证 |
| **I6 写面串行化单元错位** | 进程级单写门曾致锁序反转级联；正确=连接/事务级串行 | 9.2-write-serialization | 多连接写同库 + Outbox 并发写者无协调 |
| **I7 决策/导演三源语义重复** | rule_match / Decide / 导演各写各的 | 8.23、langgraph | 终止/继续语义分叉，难收敛 |
| **I8 单轮超时覆盖不全** | roundTimeout 只包 `execute`，**不包 `next`(onRoundCompleted→NextHybridRound→LLM 导演提议+边界持久化)** | `RoundLoopDriver`：WhenAny 仅包 `RoundLifecycle.RunAsync`；`next` 在引擎内无超时 | Hybrid 每轮 next 的 LLM 提议/持久化段无保护空窗，同样可“永久 Running”（§九候选A） |


---

## 三、目标架构：把「一次步骤调用」正交化为可拦截的调用链

> **核心思想**：把散在 7 层的横切，收拢成一条**显式 StepInvocation 中间件栈**，作为所有业务函数的唯一执行走廊。横切在走廊上成为**正交中间件**；handler 退化为「只写内存 + 追加 Outbox」的纯函数。

```
StepExec.InvokeAsync(node, input, ctx, ct)         ← 唯一调用链入口（替代 L3–L6 层层直连）
  0. 前置   节点构建 + ResolveInputs（读，带取消）
  1. 中间件栈（顺序固定、可插拔，默认行为等价 → 零 handler 改动）
     a. CancellationMiddleware   per-step linked-CTS + 单步超时（默认可从轮超时/函数元数据推导）
     b. ResourceInitMiddleware   RuleSetLoader 等确定性设施启动预热（消灭隐式首触 → I4）
     c. LlmGateMiddleware        仅真实 LLM 调用前，合作式暂停门（承接既有 CheckLlmGateAsync）
     d. ObservabilityMiddleware  步骤结构化事件(ur_events) + StallDetector（卡超自动 dump → I3）
     e. ErrorNormalizeMiddleware ErrorClass 归一 + FailureBudget/ProgressToken 单一更新入口
  2. handler 调用（统一 IAgentFunctionInvoker，ct 透传，纯后台线程，无 SynchronizationContext）
  3. 结果 → 决策信号化（decision 词 / ReturnSpec / 错误 → 单一 LoopSignal 面 → I7）
  4. 写    Outbox append（execution 只 append，不落盘）
  5. 轮后  Outbox 后台 writer 经「单库单写连接/单事务」落库（非进程门 → I6）
```

**原则 ↔ 缺陷映射**：中间件栈 = 一次入口职责内聚(I1/I2) · 单步取消/超时/watchdog 下沉 = 函数级自愈自证(I3/I4) · 纯后台线程 + IUiDispatcher = 无 UI 封送死锁(I4) · 决策信号化 = 单 LoopSignal 收敛三源(I7) · 单库单写连接串行 = 连接/事务层串行化(I6) · scoped ALC + 构建清单 = 部署即注册(I5)。

---

## 四、分阶段落地清单（Stage 0–5）

### Stage 0 — 止血与取证（推进中，本档）
| # | 动作 | 交付物 |
|---|------|--------|
| S0.1 | 取证点①：为何默认 `roundTimeout=90s`（组合根未置 0）仍无 TO | 见 §九判定（候选 A/B）与运行态断点确认 |
| S0.2 | 取证点②：卡死栈顶是 UI `SynchronizationContext` 封送（→Stage3）还是 `RuleSetLoader/ConfigFileLoader` 首触（→Stage1-b）还是存储/图锁（→Stage2） | 判定 I4 精确分支 |
| S0.3 | 本档落盘（含两次复现分析 §九） | 本文档 |

### Stage 1 — 横向能力下沉到调用链（收敛、零 handler 改动）
- 新增 `Agent.Core/Nodes/Invocation/` 的 StepInvocation 中间件栈（默认行为等价、可插拔）。
- 把 `RoundStepInvoker.ExecuteStepAsync`/`NodeExecutionService` 内联的单步计时、guard echo、return_spec 解析、错误归一改由中间件承载。
- 引入 CancellationMiddleware(单步超时) 与 ResourceInitMiddleware(RuleSetLoader 启动预热)。
- 改：`RoundStepInvoker.cs`、`NodeExecutionService.cs`、新增 `Agent.Core/Nodes/Invocation/`。

### Stage 2 — 终止真正可作用于阻塞 + 写面收敛
- 可取消异步 I/O 全链路（读/写透传 ct）。
- 单库单写连接/单事务写面（采纳 9.2 §四 A/B/C；Outbox 与主循环共用同一写通道；门退化为有界护栏）。
- 修 I8：把 roundTimeout 从「只包 execute」扩展到「execute+next 同域可取消」（`RoundLoopDriver`）。
- 改：存储 `Sqlite/*`、`NodeExecutionService.PersistAsync`、`OutboxWriter`、`RoundLoopDriver`。

### Stage 3 — 线程 / 封送隔离
- `IUiDispatcher` 收敛所有 UI 封送（显式、有界、失败隔离）；移除运行时 sync-over-async 与 `RunContext.Enter` 的 AsyncLocal 跨线程透传。
- 中间件栈 + handler 全程 `.ConfigureAwait(false)`、纯后台 executor。
- 目标：日志不再出现 `CoreWebView2…UI thread` / ECS 封送超时。

### Stage 4 — 决策信号化 + 编排扁平化
- rule_match decision / ReturnSpec / 错误 / 导演提议 → 单一 LoopSignal 面；外层 post_round 并入单一 `onRoundCompleted`（按 8.23-F1）。

### Stage 5 — 装配/发现确定性 + 治理对齐
- `FunctionLibraryLoader` 升级 scoped ALC + PrjBridge 构建期装配清单（接 9.2-FunctionLibraryLoader §6.1/6.2）。
- 修复 `.clinerules` `enf` 与 `Check-SingleWriter.ps1` 对已回退 `WriteCoordinator` 的不一致引用；判定单元改为「写通道（连接/事务）」。

---

## 五、止血与取证操作说明

### 5.1 止血（配置/装配级，不改驱动语义）
给该任务（或全局 ur_config）补单轮超时，令卡死轮确定性转 `TO` 而非永久 Running：
```sql
-- ur_config（键存在才覆盖，改行即生效；须 < request_timeout_seconds）
INSERT OR REPLACE INTO ur_config(config_key, config_value) VALUES('round_timeout_seconds','45');
```
> 注意：代码默认 90s 已开且组合根未置 0。若加了该行仍无 TO，说明该任务执行路径未走到 `RoundLoopDriver` 的带超时分支或卡在 §九候选 A 的 next 段——按 §九判定处理，不要盲目加更小超时掩盖根因。

### 5.2 抓栈取证（定方向用，一次卡死即可）
卡死（任务 `Running`、`Round` 停滞）时 dump：
1. `tp_mcpinvoke_ctrl` 执行线程栈——确认卡在 `ExecuteRuleMatchAsync`（含其内部 `RuleSetLoader.Default` 惰性首触）还是其上游 `ResolveInputs`（读 ur_nodes）还是 `next`(NextHybridRound→LLM 导演)；判 §九 候选 A/B；
2. `RoundTimeout` 运行时实值 + 是否进入 `RoundLoopDriver` 带超时分支（断点/日志）；
3. UI 线程是否被同步占死（看是否有 `SynchronizationContext.Post` 双向等待的延续）。
依据栈顶归属决定 Stage1-b（规则集加载）/ Stage2（读/写锁）/ Stage3（UI 封送）的先后——不臆测方向。

---

## 六、与既有方案的关系

| 既有文档 | 承接/修正 |
|---------|----------|
| 9.1 `agent-loop-persistence-hang-refactor`（P0–P4） | **P1(Outbox)/P3(可取消)/P5(StallDetector) 保留并入本方案**；P0(R1 进程门)已证伪→改连接级；P2(UI)并入 Stage3 |
| 9.1 `framework-refactor-arch`（D1–D6/R1–R6） | 与本文同目标架构，本文以「调用链」为主线组织；修正 D 系列中“进程门”前提 |
| 9.2 `write-serialization-arch-defects` | §四 A/B/C（单库单写连接/单事务/同写通道）为 Stage2 写面收敛的权威方向 |
| 9.2 `FunctionLibraryLoader-根因分析` | §6.1/6.2（scoped ALC + 构建清单）为 Stage5 权威方向 |

---

## 七、验收与回归基准

统一以 `agent_mcpinvoke_create_task_test.json`、`agent_taskpool_test.json` 回归，应观测：
1. `guard_input → provide_prompt` 立即开始（存储健康无关，保既有 P1 不回退）；
2. 人为注入单步卡死/存储挂起 → 单步超时确定性转 `TO`，不再靠测试脚本 120s 判死（Stage1/2）；
3. 全程无 `CoreWebView2…UI thread` / ECS 封送超时（Stage3）；
4. 若仍卡，单步超阈自动产出线程栈诊断事件，一次运行即定位（Stage1-d/5）；
5. `Check-SingleWriter.ps1`（按写通道判定）全库 0 绕过（Stage5）；
6. `FileDriven.sln` / `Agent.sln` 逐 sln 0 警告 0 错误。

---

## 八、风险与回退
| 风险 | 缓解 |
|------|------|
| 单步超时误杀真实 LLM 长轮 | 默认从轮超时/函数元数据推导；真实 LLM 调用步可单独放宽；可回退关闭 |
| 中间件栈改动面大 | Stage1 默认行为等价、可插拔；`enableOutbox` 既有回退开关保留 |
| UI 隔离(Stage3)牵动 FileDriven | 先 §5.2 取证定 I4 分支再启动；`IUiDispatcher` 未装配走原路径零回归 |
| Outbox 落库时序变化 | 保留单事务 Outbox 选项；读取侧已“节点输出优先” |
| 诊断未取到（无法复现卡死） | 用 §5.1 round_timeout 45s + StallDetector 双保险；必要时注入挂起点做确定性复现 |

---

## 九、二次复现分析（2026-09-02 19:23–19:26，附判定逻辑与修订）

> 同一 `agent_mcpinvoke_create_task_test.json` 复跑，与 19:09 首复现**卡点完全一致**。以下判定均基于日志推演 + 源码核实；凡需运行态确认者明确标注「待取证」。

### 9.1 时间线与卡点（与首复现逐项吻合）
| 时间 | 事件 | elapsed |
|------|------|---------|
| 19:24:19.899 | `guard_input` 步骤开始 | |
| 19:24:19.915 | `guard_input` 完成 | 14ms |
| 19:24:19.917 | `provide_prompt` 开始 | |
| 19:24:19.925 | `provide_prompt` 完成 | 8ms |
| 19:24:19.926 | `cline_execute` 开始 | |
| 19:24:19.929 | `cline_execute` 完成（本地联调 1ms 空返回） | **2ms** |
| **19:24:19.929** | **`rule_match` 步骤开始**（与上同毫秒） | ★卡点 |
| — | `rule_match`“节点函数返回”**永不出现** | ✗ 卡死 |
| 19:24:24.836 | 外部轮询 `Running`（他线程） | 线程存活 |
| … | 120s 无 `Agent.Core` 输出 | 脚本判死 |

> 关键：`cline_execute` **2ms** 空返回（本地联调 `LocalLlmHttp`），其后 `rule_match` 紧贴开始；guard/provide/cline 两次都稳定 <20ms 秒过，**唯独 rule_match 的 `InvokeAsync` 无返回**。

### 9.2 确定性证据与判定链

**证据① — 稳定复现于同一单点（非偶发）**：19:09 / 19:24 两次独立复跑，卡点均 = `rule_match` 步骤 `INodeFunctionInvoker.InvokeAsync("rule_match",…)` 未返回（缺“节点函数返回”日志，`NodeExecutionService.ExecuteAsync` 于 InvokeAsync 返回后才打印）。⇒ 确定性缺陷，非资源竞争偶发。

**证据②（修订）— 默认 90s 单轮超时两次都未触发 TO，但组合根静态未置 0**：
- 源码核实：`AgentLoopConfig.RoundTimeout` 默认 90s；组合根 `AgentConfig.BuildLoopConfig()`（对象初始化器未含 RoundTimeout）与 `AgentOrchestrator()` 无参构造都**保留默认 90s**；全库 `Agent/src` **无任何置 0 点**；`LoopConfigHotReload` 仅对 >0 秒值才覆盖。
- 日志：rule_match 开始 ≈ 19:24:19.929，若 90s 生效，round1 execute 最迟 ≈ 19:25:50 记 `TO`；但 120s（≈19:26:20）判死前**无任何 Agent.Core 收敛日志**。
- ⇒ 静态最简解释（“组合根置 0”）**被证伪**。收敛为两个**互斥的运行态候选**，需断点/线程栈区分：
  - **候选 A**：roundTimeout=90s 确实生效，round1 execute 在 rule_match 卡 90s 后记 `TO`；但引擎随后进入 `next`（`NextHybridRoundAsync` → `Scheduler.AdvanceAsync` LLM 导演提议 + `PersistRuntimeAsync` 边界持久化），**该段无 roundTimeout 保护（实证 I8）** → 卡死于此、无日志、不收敛。
  - **候选 B**：roundTimeout 实际未生效（执行路径未携带，走 `RoundLoopDriver` 直跑分支），rule_match 卡在 execute 内 → 永久 Running、无日志。
- 二者共同指向：**单轮超时机制不足以自愈**，与 Stage 治理方向一致（需单步级正交取消/超时/watchdog，并覆盖 execute+next 全域）。

### 9.3 诊断矩阵（静态可证 / 需运行态取证 二分）
| 结论 | 证据 | 判定状态 |
|------|------|---------|
| 卡点 = rule_match handler(`InvokeAsync`) 内未返回 | 缺“节点函数返回: func=rule_match” | ✅ 确定（日志） |
| 组合根未把 RoundTimeout 置 0（默认 90s） | `BuildLoopConfig()`/无参构造/全库扫描 | ✅ 确定（源码） |
| 90s 单轮超时两次未收敛 | 无 TO、脚本 120s 判死 | ✅ 确定（日志） |
| 候选 A(execute 已 TO、卡在 next/LLM 导演) vs 候选 B(execute 未 TO) | 90s 附近是否有 next/LLM 提议活动、roundTimeout 是否进入带超时分支 | ⏳ 待运行态 |
| 卡死阻塞点：规则集/决策设施首触 vs 读库锁 vs UI 封送 | 线程栈顶归属 | ⏳ 待运行态 |

### 9.4 收窄后的下一步
1. **取证（定 A/B + 阻塞归属）**：卡死时 dump `tp_mcpinvoke_ctrl` 线程栈 + 在 `RoundLoopDriver` 带超时分支入口断点确认 roundTimeout 实值。判 候选 A → **修 I8（Stage2，roundTimeout 扩展覆盖 execute+next）**；判 候选 B → 查该任务执行路径为何不带 roundTimeout。
2. **定阻塞类型（定 Stage 方向）**：栈顶若在 `RuleSetLoader/ConfigFileLoader` 首触 → **Stage1-b**；在 `ResolveInputs`/存储/图锁 → **Stage2**；在 UI `SynchronizationContext` 封送 → **Stage3**。
3. **根治（随调用链正交化）**：按 §三/§四 Stage1–5 收敛，使单步级有独立取消/超时/watchdog，不再依赖“单轮超时是否生效”。

---

*本文档基于 2026-09-02 会话的源码调用链核实（两次复现日志 + AgentLoopConfig/RoundLoopDriver/AgentRunner/NodeExecutionService/BuildLoopConfig 等）、历史 9-01/9-02/9.2 分析整理，作为调用链架构优化的分阶段执行依据。*

---

## 十、调用链诊断日志补点（2026-09-02 落地，替代“运行时抓栈”的第一步）

> 目标：把 §九 候选 A/B 与阻塞归属，改成**下一次复现日志自证**（无需断点/抓栈）。仅加日志，不改行为；`Agent.Core` 编译 0 警告 0 错误。

### 10.1 新增日志（调用点 → msgId → 模板要点）
| 层 | msgId | 何时打 | 判定作用 |
|----|-------|--------|---------|
| L4 步骤调用器 | `RoundStepInvoker_ExecuteStepAsync_inputsResolved` | `ResolveInputsAsync`(读 ur_nodes/存储) 返回后 | 缺此条 = 卡 `ResolveInputs`(读源/锁) → **Stage2** |
| L5 执行服务 | `NodeExecutionService_ExecuteAsync_invokeStart` | `InvokeAsync` 调用前 | 有此条 + 缺“节点函数返回” = 卡 **handler 内部**(`ExecuteRuleMatchAsync`) → 需函数级细日志/**Stage1-b** |
| L2 驱动 | `RoundLoopDriver_round_timeout` | 单轮 `execute` 真正卡超(WhenAny 超时、`roundCts.Cancel()` 前) | 有此条 = **候选 A 前半**（execute 卡满 roundTimeout）；无此条 = **候选 B**（roundTimeout 未生效/直跑） |
| next 钩子 | `AgentRunner_NextHybridRound_start` | `NextHybridRoundAsync`(next) 入口、`Scheduler.AdvanceAsync`(LLM 导演) 前 | 有 start 无 completed = 卡 **next 内**(LLM 导演/边界持久化) → **实证 I8 → Stage2** |
| next 钩子 | `AgentRunner_NextHybridRound_completed` | next 段完成 | 与 start 配对，证 next 是否推进 |

改动的文件：`Agent.Core/Nodes/RoundStepInvoker.cs`、`Agent.Core/Nodes/NodeExecutionService.cs`、`Agent.Core/Loops/RoundLoopDriver.cs`、`Agent.Core/Loops/AgentRunner.cs`、`Agent.Core/Diagnostics/agent_log_messages.json`(+5 msgId)。

### 10.2 下一次复现的判定口径（照此读日志即可定方向）
```
rule_match 步骤开始
 ├─ 缺 inputsResolved                 → 卡 ResolveInputs(读源)            → Stage2
 ├─ 有 inputsResolved
 │    ├─ 无 invokeStart                → 极短段(排除)
 │    └─ 有 invokeStart
 │         ├─ 无"节点函数返回"          → 卡 handler 内部(规则集/return_spec) → Stage1-b(+函数级细日志)
 │         └─ 有"节点函数返回"          → rule_match 已返回(后续另判)
 └─ 全局观测
      ├─ 有 RoundLoopDriver_round_timeout → execute 卡满 roundTimeout(候选A前半)
      │     └─ NextHybridRound_start 有 / completed 无 → 卡 next(LLM 导演/持久化) → 实证 I8 → Stage2
      └─ 无 round_timeout              → 候选 B(roundTimeout 未生效/直跑) → 查该任务执行路径为何不带 roundTimeout
```

### 10.3 验证
- `Agent.Core.csproj` 编译 0 警告 0 错误（已跑通）。
- 复跑 `agent_mcpinvoke_create_task_test.json`，按 §10.2 表读日志即可判定阻塞归属，进而定 Stage 修复方向，无需断点/抓栈。

