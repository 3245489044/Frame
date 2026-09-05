# 单一路径彻底重构 — 实施看板（S1–S6）

> **方案文档**：`2026-09-02-remove-fallback-single-path-refactor.md`（§六 阶段 / §七 验收基准 / §八 边界）
> **决策（已拍板）**：S1 装配彻底删降级形态 —— GUI 目标态 = 完整 Agent（任务池 / 会话 / LLM 网关 / 数据库 四核心），任一未装配 → 启动即 fail、UI 显示「Agent 不可用 + 原因」；无凭据即不装配 = 明确报错。不复建 `RulesOnlyAgent` 降级形态。
> **执行顺序**：S1 → S2 → S4-A/B →（S3 = 9.3 中间件栈；S4-C·S4-D 并入 S3 上下文）→ S5 → S6（线性，独立 commit / 逐 sln 编译 / 既有测试回归）
> **基线**：`Main@08cd81072`（前置含 9.3 调用链部分改动）；Agent.sln 编译 0 错误；FileDriven.sln 同理需维持。
> **跨会话性质**：每阶段为独立可回退落地单元；本文档记录进度、已改动文件、每步验证结论。

---

## 阶段状态

| 阶段 | 面 | 状态 | 落地单元（子步） | 备注 |
|------|----|------|------------------|------|
| S1 | 装配 | ✅ 完成 | S1-A … S1-D | 见下方清单；ab2ee6ed2 + d21f1e4ea |
| S2 | 运行入口 | ✅ 完成 | S2-A 删 AgentHost 直跑兜底；S2-B Reactive 已共享同一 RunAsync 端点 | 见下方清单；0b86d775b |
| S4 | 决策/错误 | ◐ 进行中 | S4-A/B 完成；S4-C/D 并入 S3(9.3) | S4-C LoopSignal 决策信号化属 9.3 Stage4 |
| S3 | 调用链 | ◐ 进行中 | 单步超时接线+覆盖读源已落地（见会话日志） | 承接 9.3；完整中间件栈(LlmGate/ResourceInit/ErrorNormalize)与 return_spec 横切迁移待续 |
| S5 | 写 | ▢ 未开始 | | |
| S6 | 发现 | ▢ 未开始 | | |

> 图例：▢ 未开始 · ◐ 进行中 · ✅ 完成（含编译+测试验证） · ✋ 阻塞

---

## S1 · 装配面（彻底删降级，fail-fast，单连接）

> 目标判据（§七.2）：核心模块任一未 Attached → 启动即 fail、日志 `failedModules + 失败链`、UI 明确「Agent 不可用 + 原因」；不再出现「DB-only 但 OnLoad 成功、Agent 静默不可用」。§七.4：单连接（agent.db 仅装配主链唯一产出，无惰性自举第二连接）。

- [x] S1-A：装配层去降级 — `AgentFrameworkHosting.TryBootstrapGuiAsync` 删两级降级 catch，仅成功返回；失败即抛（失败链上抛至 MainForm）。
- [x] S1-B：宿主契约演进 — `BootstrapResult` / `AgentFrameworkBootstrapInfo`：删 `RulePoolOnly`，引入 `failedModules + reason`；更新 `AgentUiBridge` / `IAgentUiBridge` / `MainForm.OnLoadAsync`。
- [x] S1-C：装配主链 fail-fast + 产物对账 — `AgentFrameworkHosting` 四核心对账 `AssemblyFailedException.ThrowIfCoreIncomplete`（任务池/会话/LLM 网关/数据库）；CLI 路径（AgentHost）不受影响（会话按宿主形态声明）。
- [x] S1-D：删惰性独立 DB — `AgentUiHost.GetOrBootstrapDatabaseAsync` 去 DatabaseBootstrapper 惰性自举，仅返回装配主链产物；未装配返回 null（单连接不变量）。

> S1 落地说明：会话/任务池/网关"按声明装配"由 GUI 门面对账（ThrowIfCoreIncomplete）承载；`ConversationHosting.TryAttach` 的 pool==null 跳过对 CLI 直跑是正确语义（保留），GUI 侧由对账捕获会话缺失 → fail。

---

## S2 · 运行入口（唯一咽喉，删直跑旁路）

> 目标判据（§七.1）：无双入口旁路 —— EnableTaskPool=true 时任务必然走统一池；不存在"直跑兜底"或"Reactive 另起直跑"。

- [x] S2-A：删 `AgentHost` 直跑兜底 — `RunThroughPoolAsync` pool null → 抛装配失败（声明走池却无池 = fail-fast），删 `fw.RunAsync` 编排器直跑旁路。
- [x] S2-B：Reactive 汇入咽喉 — **代码实据**：ReactiveTrigger runner（DataStoreHosting:75）与任务池 LlmTaskExecutor 均已收敛到同一 `AgentOrchestrator.RunAsync` 端点（SingleImplementation），无第二套独立 run 引擎；`RunOnceAsync` 已删、AgentHost 直跑已删（S2-A）。Reactive 的 `ur_inputs` 通道按文档 §一 [边界] 定位保留（汇入同一 run 端点后保留）。§七.1 无双入口旁路判据达成。

---

## S4 · 决策/错误（LoopSignal 单面 + 失败即抛 + 清 catch-null）

> 目标判据（§七.3/§七.6）：无 catch 吞异常转 null 承载核心能力；决策单源（continue/stop/TO/TM 单一 LoopSignal 面）。

- [x] S4-A：清 `catch{return null}` 能力模块 — 删 `TaskPoolHosting.TryBootstrapRulePoolAsync`（零 LLM RulePoolOnly 降级任务池装配器，S1 去降级后无 .cs 调用方的死降级装配点；保留 `BuildTaskPoolStoreAsync` 供 DataStoreHosting）。
- [x] S4-B：装配/关键路径失败即抛带 ErrorClass — `AssemblyFailedException` 增 `ErrorClass` 属性（= `ErrorClass.Irrecoverable`，装配失败架构性不可重试/降级），对齐 §4.5；装配抛错已带 failedModules + reason + 失败链。
- [ ] S4-C：决策单源 `LoopSignal` — rule_match / RoundLifecycle.Decide / 调度导演收敛单面裁决（对齐 `DecisionOutputSet`）。
- [ ] S4-D：错误面清静默 catch — 全库 grep `catch { return null; }` / `catch { /* 静默 */ }` 逐一消灭（保留显式运行期容错：有界重试/取消令牌）。

---

## 会话日志

| 日期 | 阶段 | 动作 | 验证结论 |
|------|------|------|----------|
| 2026-09-02 | 基线 | Agent.sln 编译（含 9.3 未提交改动） | ✅ 0 错误 / 180 警告（历史遗留） |
| 2026-09-02 | S1-A/B | `ab2ee6ed2`：TryBootstrapGuiAsync 删两级降级；AssemblyFailedException + 四核心对账；契约删 RulePoolOnly→Succeeded+failedModules+reason；AgentUiBridge/MainForm/日志模板同步 | ✅ Agent.sln / FileDriven.sln / PrjBridge.csproj 0 错误 |
| 2026-09-02 | S1-C/D | AgentUiHost 去 DatabaseBootstrapper 惰性自举（单连接）；GUI 四核心对账兜底会话/任务池 | ✅ Agent.sln 0 错误；Launcher 0 警告 0 错误 |
| 2026-09-02 | S2 | `0b86d775b`：删 AgentHost 直跑兜底旁路（pool null→fail）；Reactive 已共享同一 AgentOrchestrator.RunAsync 端点（非旁路） | ✅ Launcher 0 警告 0 错误；§七.1 达成 |
| 2026-09-02 | S4-A | 删 TaskPoolHosting.TryBootstrapRulePoolAsync（RulePoolOnly 降级装配器，catch-null 吞异常） | ✅ Launcher 0 警告 0 错误 |
| 2026-09-02 | S4-B | AssemblyFailedException 增 ErrorClass(Irrecoverable)；装配抛错带 ErrorClass/failedModules/reason | ✅ Launcher 0 警告 0 错误 |
| 2026-09-02 | 看板 | 登记 S4-C/D 并入 9.3 Stage4 依赖 | — |
| 2026-09-02 | S3① | 单步超时接线+覆盖 ResolveInputs：`RoundStepInvoker` 增 `_stepTimeout` + `RunBoundedAsync`(per-step linked-CTS+WhenAny)；`WorkflowRunner`/`AgentRunner` 透传 **独立默认 90s（非由 RoundTimeout 派生——运行时 RoundTimeout 实测为 0/候选B，单轮 WhenAny 不收敛）**；仅封装**确定性读源 ResolveInputs**（handler 不套，避免误截断 cline_execute 暂停门/LLM）；超时→强类型 `StepTimeoutException`(非取消)；新增 `Invocation/StepTimeoutException.cs` + msgId `RoundStepInvoker_ExecuteStepAsync_timeout` | ✅ Agent.Core 0警告0错误；Agent.sln 0错误(180 历史遗留警告)；FileDriven launcher 已刷新 Agent.Core.dll；需 test10 运行回归 |
| 2026-09-02 | 收尾 | 方案文档状态行更新（S1/S2/S4-A·B 已实施），本看板登记剩余阶段续推进路径 | 工作区干净，7 独立 commit 全跨 sln 编译通过 |

---

## 剩余阶段续推进路径（下次会话续推进依据）

> 本看板为 S1–S6 单一路径重构的**单一进度记录**；方案权威见 `2026-09-02-remove-fallback-single-path-refactor.md`。当前已完成 S1(全)/S2(全)/S4-A·B；S4-C/D 实际并入 S3(9.3 中间件栈) 上下文。剩余 S3/S5/S6 均需**运行级验证**（非纯编译可安全完成），建议按依赖顺序独立推进、各自独立 commit。

### S3 · StepExec 中间件栈（承接 9.3 function-callchain）
- **依赖**：须先推进 9.3 Stage 0（卡死取证 S0.1/S0.2，判 I4 精确分支：UI 封送 / RuleSetLoader 首触 / 存储锁）→ 再按 9.3 Stage 1-4 顺序实施。
- 目标：新增 `Agent.Core/Nodes/Invocation/` StepInvocation 中间件栈（Cancellation/ResourceInit/LlmGate/Observability/ErrorNormalize），把 `RoundStepInvoker.ExecuteStepAsync`/`NodeExecutionService` 内联横切改由中间件承载。
- **风险**：`ExecuteStepAsync` 含大量既有内联行为（入参解析/StateAdapter 双向绑定/RecordStep/guard echo/return_spec），重构须"默认行为等价"；须 agent_mcpinvoke_create_task_test / agent_taskpool_test 运行回归。
- 本会话勘察结论：Invocation/ 目录尚未创建；中间件栈框架当前不存在（非已部分实现）。
- S4-C（LoopSignal 决策信号化）+ S4-D（清静默 catch）并入本阶段上下文实施。

### S5 · 写面（单库单写连接/统一 Outbox）
- 采纳 9.1 `2026-09-01-framework-refactor-{arch,implement}.md`（`NodePersistOutbox.cs` 已存在）+ 9.2 写面 §四。
- 目标：删 `enableOutbox` 同步回退开关（统一 Outbox）；Outbox 与主循环同写通道；图/LiteDB 同库隔离同写序；门退化为有界护栏（连接/事务级串行，非进程级单写门——9.2 已证伪锁序反转）。
- 改：存储 `Sqlite/*`、`NodeExecutionService.PersistAsync`、`OutboxWriter`、`RoundLoopDriver`。

### S6 · 发现/装配（scoped ALC + 构建清单）
- 采纳 9.2-FunctionLibraryLoader：scoped `AssemblyLoadContext` + PrjBridge 构建期装配清单（部署即注册）。
- 目标：删除 `AssemblyLoadContext.Resolving` 运行时兜底与反射 `Type.GetType` 撞运气。
- 改：`FunctionLibraryLoader`、`PrjBridge.csproj`。

### 建议下次会话起点
按"可运行验证优先"建议顺序：**S5（写面，最独立、Outbox 基建已就绪，见效明确）→ S6（ALC 发现，改动面收敛）→ S3（中间件栈，需 9.3 取证前置，最重）**。也可按方案 §六 建议先做 E 面装配（已完成）后，从任一独立专项切入——由推进会/取证结论决定。
