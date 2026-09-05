# LLM 调用与脚本等待的框架化重构方案（消除同步阻塞，收敛为统一长任务执行协议）

> 日期：2026-08-22 · 状态：**方案（待实施）**
> 触发：真实 LLM 测试脚本 `agent_real_llm_test.json` 在工作流**已成功完成**（`本轮骨架完成: ok=true, steps=3`）后，
> 仍**卡在 `click sendMessageButton wait:true` 的完成信号上**；而本地模式 `agent_local_test_gui_test.json` 全流程跑通。
> 核心结论：**不是 LLM 本身失败，而是「慢异步调用」暴露了框架「同步阻塞等待 + 完成信号链路过长 + LLM 执行端未统一抽象」的跨层架构缺陷**——本地快调用掩盖了它，真实慢调用放大了它。
> 关联文件：`BackEndDriven/CommandExecution/ClickSetGetSystems.cs`、`AwaitValueSystem.cs`、`FrontEndDriven/ControlInteraction/EventBinding/EcsEventBus.cs`、
> `Share/EventBindingOrchestrator/Invocation/InvocationKernel.cs`、`Modules/Threading`（AEF）、`Agent.Modules/Cline/ClineAgentExecutor.cs`、
> `Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs`、`Agent.Launcher/Modules/AgentUiLibraryConversation.cs`。

---

## 〇、结论先行

「卡住」的根因是**框架性**的，不是单点 bug：

1. **命令层（脚本 click/await）用「同步阻塞等待异步长任务」**：`ClickSystem` 的 `wait=true` 调 `ack.Completion.Wait(timeout)`、`AwaitValueSystem` 调 `tcs.Task.Wait()`，在命令执行线程上**同步阻塞**直到函数完成信号落定。
2. **完成信号链路过长、任一环慢/断则命令无限等待**：`completion` 依赖整条 `DispatchFunctionAsync → InvocationKernel.ExecuteAsync → AgentUi_ConversationSend → SendAsync → RunSignalAsync → WorkflowRunner → cline_execute → ClineClient daemon → GetHistory → 回写 OutputTarget → completion.TrySetResult`。本次工作流 `ok=true` 后 `InvocationKernel.ExecuteAsync` 未返回（无 `EcsEventBus_Dispatch_output_decision` 日志）→ `completion` 从未落定 → `Wait` 一直阻塞到超时。
3. **LLM 执行端未统一抽象**：`ClineAgentExecutor.ExecuteAsync` 内 `if (LlmMode.LocalOnly)` 硬分叉「本地绕开」与「真实 daemon」；`answer` 走 `StatelessAnswerExecutor`。三条路径各自实现异步/取消/超时，语义分叉，无法统一治理。
4. **计时/取消/预算多时钟**：`ClineClient` 预算 295s（`ClineCallCap`）、会话 120s、click `wait` 120s+5s、内核默认 30s 各成一体，慢调用下无法单一权威终止/归因。

> **项目已有治本蓝图**：`Modules/Threading/docs/现有代码应用集成指南.md`（AEF 迁移）已明确把 `EcsEventBus` 手建 TCS、`ClickSystem` 的 `Task.Wait()`、`AwaitValueSystem` 的 `tcs.Task.Wait()` 迁移到 `AsyncOperation.WaitAsync()`（非阻塞）+ 单一权威计时 + 取消贯穿。**该蓝图已识别根因并规划，但命令层尚未落地**（现状仍同步阻塞）。本方案在其基础上补上「LLM 执行端统一抽象」一环，形成闭环。

一句话：**把「脚本/UI 等待」与「LLM 执行」都收敛到统一的异步执行框架（AEF），用统一的长任务执行协议（异步 + 取消 + 心跳进度 + 单一时钟 + 结构化结果）替代「同步阻塞 + 散落 TCS + 多时钟」，而不是给某条慢路径打补丁。**

---

## 一、现象与证据链（本次真实 LLM 运行日志）

脚本 `agent_real_llm_test.json` 关键段：`click sendMessageButton wait=true output=conversationHistoryTextBox timeout=120000`。

| 时间 | 日志 | 含义 |
|------|------|------|
| 19:57:56.724 | `[RoundStepInvoker] 步骤开始: function=cline_execute` | collect 步开始（真实 daemon） |
| 19:58:00.951 | `节点函数返回: func=cline_execute, elapsedMs=4219` | collect 真实 4.2s 完成 |
| 19:58:01.964 | `节点函数返回: func=answer, elapsedMs=1008` | analyze 完成 |
| 19:58:03.999 | `节点函数返回: func=cline_execute, elapsedMs=2032` | act 真实 2.0s 完成 |
| 19:58:04.001 | `[WorkflowRunner] 步骤完成: function=cline_execute` | 骨架步骤完成 |
| 19:58:04.008 | `本轮骨架完成: ok=true, decision=(无), error=(无), errorStep=(无), errorClass=(无), steps=3` | **工作流成功完成** |
| （此后） | **无 `EcsEventBus_Dispatch_output_decision` / `EcsEventBus_Dispatch_completed` / `ECS 事件输出决策`** | **`InvocationKernel.ExecuteAsync` 未返回 → `completion` 未落定 → click wait 卡死** |

**关键对比**：`newSessionButton` 点击后有 `ECS 事件输出决策 — opId=... Completed` + `ECS 状态写入物理投影 conversationStateTextBox`；而 `sendMessageButton`（真实模式）**没有**对应的输出决策/完成日志。这直接证明：函数体（Agent 工作流）完成了，但**「函数调用」这一层（InvocationKernel → 完成信号）没有走完**。

**为什么本地模式跑通**：本地 `cline_execute` 走 `LlmGateway`（确定性本地 chat，~1s），整条 `AgentUi_ConversationSend` 快，完成信号在命令线程的等待窗口内落定；真实模式 `cline_execute` 走 daemon（累计 ~30s + 预热），慢调用放大链路上任一慢/断点，使完成信号未落定被暴露。

---

## 二、调用链与线程模型（现状）

```
脚本 click sendMessageButton (wait=true, timeout=120000)
  └─ ClickSystem.Execute  [命令执行线程]  ── ack.Completion.Wait(waitTimeoutMs)  ← 同步阻塞
       └─ EcsEventBus.Enqueue  →  Dispatch(e)   ── _ = DispatchFunctionAsync(...)   ← fire-and-forget（async）
            └─ InvocationKernel.ExecuteAsync   [AEF worker]  ── await ...ConfigureAwait(false)
                 └─ AgentUi_ConversationSend → ConversationSendAsync
                      └─ svc.SendAsync → ConversationPipeline.RunSignalAsync
                           └─ WorkflowRunner.RunRoundAsync → cline_execute → ClineAgentExecutor
                                ├─ LocalOnly=true  → ExecuteLocalBypassAsync → LlmGateway.ChatAsync   （本地快）
                                └─ LocalOnly=false → ClineClient.AskAsync → daemon 池 → 真实 LLM     （秒-分钟级慢）
                           → GetHistoryAsync → RenderBody（回显）
                 ← 返回 HTML
            ← 回写 OutputTarget（_stateAccessor.SetValue）+ completion.TrySetResult   ← 完成信号
```

**缺陷点（跨层）：**

| # | 层 | 缺陷 | 证据位置 |
|---|----|------|----------|
| A1 | 命令层 | `wait=true` 用 `Task.Wait()` **同步阻塞**等待异步长任务（真实 LLM 秒-分钟级），长时间占用命令执行线程并阻塞脚本后续命令 | `ClickSetGetSystems.cs`（`ack.Completion.Wait`） |
| A2 | 命令层 | `await`/`waitfor` 同样手建 TCS + `tcs.Task.Wait()` 同步阻塞 | `AwaitValueSystem.cs` |
| A3 | 事件层 | 完成信号依赖**整条** `DispatchFunctionAsync → InvocationKernel → 函数 → 回写` 链；任一环慢/断则 `completion` 不落定，等待方无限阻塞 | `EcsEventBus.cs`（fire-and-forget + 全链后才 `TrySetResult`） |
| A4 | 执行层 | `InvocationKernel.ExecuteAsync` 用 `ConfigureAwait(false)`，本身非阻塞；但函数执行**没有统一的心跳/进度**，无法区分「慢但正常」与「卡死」 | `InvocationKernel.cs` |
| A5 | LLM 端 | `ClineAgentExecutor` 内 `if (LlmMode.LocalOnly)` 硬分叉本地/真实；`answer` 走 `StatelessAnswerExecutor`——**执行端未统一抽象**，慢调用无法统一治理 | `ClineAgentExecutor.cs` |
| A6 | 时钟 | ClineClient 295s / 会话 120s / click wait 120s+5s / 内核 30s 多时钟，无单一权威 | `ClineClient.cs`、`ConversationPipeline`、`ClickSetGetSystems.cs` |

---

## 三、结构性根因（非单点）

### D1 同步阻塞 × 异步长任务（反模式）
命令层用 `Task.Wait()`/`GetAwaiter().GetResult()` 同步等待本就异步的长任务（真实 LLM）。本地快调用让阻塞"看起来无害"，真实慢调用使任何上游慢/断点都表现为「命令卡住」。这是**同步等待异步**反模式在脚本-事件-LLM 三层贯穿。

### D2 完成信号链路过长且无隔离
`completion` 落定依赖从命令入队到函数回写的整条链。链路中 `InvocationKernel.ExecuteAsync` 未返回（本次），或回写/GetHistory 慢，都会让等待方永久阻塞。完成信号与业务执行**未解耦、无超时上抛的分类兜底**（虽有 `InvocationOutcome`，但等待方只看到"未完成"）。

### D3 LLM 执行端未统一抽象
本地/真实/answer 三条路径各自实现异步/取消/超时/进度，语义分叉（`if (LlmMode.LocalOnly)` 即分叉点）。「慢调用治理」（统一预算、统一心跳、统一取消、统一归因）无处安放，只能在各路径分别打补丁——这正是"打补丁 vs 聚拢"的对立所在。

### D4 计时/取消/预算多时钟
ClineClient 295s / 会话 120s / click 120s+5s / 内核 30s。慢调用下，任一时钟未对齐即无法在正确边界终止或归因；取消令牌也非全程贯穿。

### D5 脚本命令系统非 async
`CommandSystemBase.Execute` 是同步方法，命令无法 `await` 长任务；要么阻塞（现状），要么需要命令系统 async 化才能接入 AEF 非阻塞等待。

---

## 四、重构目标与通用框架设计（集中聚拢）

### 4.1 目标
把「LLM 执行」与「脚本/UI 等待」**都收敛到统一的异步执行框架（AEF）**，用一套**长任务执行协议**替代分散实现，做到：
- **无同步阻塞**：GUI/命令线程永不 `Task.Wait()`/`GetAwaiter().GetResult()` 等待异步长任务。
- **单一权威计时/取消**：所有慢调用共享同一预算/取消时钟，可单一边界终止 + 归因。
- **统一执行端口**：本地/真实/answer/cline 收敛为一个 `IExecutionEndpoint`，统一 `ExecuteAsync(request, ct, progress)` 契约。
- **完成信号可靠落定**：等待方订阅 AEF `AsyncOperation`，而非依赖跨多层散落 TCS 的链式 `TrySetResult`。

### 4.2 通用框架：统一「长任务执行协议」

```
┌────────────────────────────────────────────────────────────────────────┐
│  AEF · 统一异步执行框架（Modules.Threading）——单一权威计时/取消/进度       │
│  AsyncOperation<T>  (Id / CancellationToken / Heartbeat / Completion)   │
└──────────────┬───────────────────────────────┬────────────────────────┘
               │ 提交 AsyncOp                   │ 订阅 AsyncOp
       ┌───────▼────────┐              ┌───────▼─────────────┐
       │ 统一执行端       │              │ 等待方（非阻塞）        │
       │ IExecutionEndpoint             │ ClickSystem / Await    │
       │  (本地/真实/answer/cline)        │  → op.Completion.WaitAsync │
       └───────┬────────┘              └──────────────────────┘
               │ 统一：异步 + 取消 + 心跳进度 + 单一时钟 + 结构化结果
```

**等待方统一范式**（`ClickSystem`/`AwaitValueSystem`/会话发送均收敛到此）：
```csharp
// 非阻塞：命令系统 async，订阅 AsyncOp 完成信号，而非同步 Wait()
var ok = await op.Completion.WaitAsync(
    timeoutMs > 0 ? TimeSpan.FromMilliseconds(timeoutMs) : Timeout.InfiniteTimeSpan);
if (!ok) { /* 归因分类：FunctionFault / FrameworkTimedOut / IdleHung / SignalLost */ }
```

**执行端统一端口**（消除 `if (LlmMode.LocalOnly)` 分叉）：
```csharp
public interface IExecutionEndpoint
{
    string Name { get; }                       // cline / answer / semantic / local
    Task<EndpointResult> ExecuteAsync(
        AgentExecutorRequest request,
        CancellationToken ct,
        IProgress<EndpointProgress>? progress = null);   // 心跳进度
}
```
- 本地/真实/answer/cline 各自是实现，但**共用同一 AsyncOp（统一预算/取消/心跳）**。
- `cline_execute` 不再内嵌 `if (LlmMode.LocalOnly)`——由执行端解析器（配置/策略）选择实现，单一裁决点。

---

## 五、分层重构方案（M1–M6，聚拢而非补丁）

### M1【命令层 async 化 + 接入 AEF 非阻塞等待】
- 文件：`ClickSetGetSystems.cs`、`AwaitValueSystem.cs`、`CommandSystemBase`（BackEndDriven）
- 改动：命令系统支持 `async Task ExecuteAsync`；`ClickSystem` 的 `wait=true` 从 `ack.Completion.Wait(timeout)` 改 `await op.Completion.WaitAsync(timeout)`；`AwaitValueSystem` 从 `tcs.Task.Wait()` 改订阅 `AsyncOp.WaitAsync`。
- 收益：命令线程/GUI 永不阻塞；慢 LLM 调用期间脚本让出线程、消息泵/回写可继续。
- 依据：`现有代码应用集成指南.md` §6/§7（已规划未落地）。

### M2【完成信号与业务执行解耦（AEF AsyncOp 替代散落 TCS）】
- 文件：`EcsEventBus.cs`
- 改动：`DispatchFunctionAsync` 手建 `TaskCompletionSource<EventFunctionOutcome>` → 改为提交 `AsyncOperation<EventFunctionOutcome>`（`op.Complete`/`op.Fail`），`EventDispatchResult.Completion` 指向 `op.Completion`；回写 `OutputTarget` 与完成信号落定解耦，信号经 `op.Complete` 单一终结。
- 收益：等待方订阅的是框架统一终结的 AsyncOp，不再依赖"回写成功才 set completion"的隐式耦合；`SignalLost` 可被框架识别而非"永远等待"。

### M3【LLM 执行端统一抽象 IExecutionEndpoint】
- 文件：`ClineAgentExecutor.cs`、`StatelessAnswerExecutor.cs`、新增 `EndpointResolver`
- 改动：抽取 `IExecutionEndpoint`（§4.2）；`ClineAgentExecutor` 的 `if (LlmMode.LocalOnly)` 分支移入执行端解析器（单一裁决点，配置/策略驱动选择本地或真实）；`answer` 复用 `StatelessAnswerExecutor` 作为 endpoint；`cline_execute`/`answer` 均提交统一 AsyncOp。
- 收益：本地/真实/answer 统一异步/取消/进度/结果契约，慢调用治理（统一预算/心跳/取消）有单一安放点；符合 `SingleImplementation`（收敛重复执行路径）。
- 关联：clinerules `[Strategy] SingleImplementation`（[CR: axioms:Strategy.SingleImplementation@consume]）。

### M4【单一权威计时 + 取消贯穿 + 心跳进度】
- 文件：`ClineClient.cs`（实现 `IProcessHandle` + 心跳）、`ConversationPipeline`/`ConversationSessionService`（会话预算单一来源）、`FunctionCallFacade`（变薄适配器）
- 改动：会话发送预算 = 单一来源（会话级，向内核下发）；ClineClient 实现 `IProcessHandle`，`OutputDataReceived` 作心跳重置空闲窗口，取消→`process.Kill(entireProcessTree)`；消除 295s/120s/30s 多时钟。
- 收益：慢调用在正确边界终止/归因，无孤儿进程、无跨层竞速。
- 依据：`现有代码应用集成指南.md` §3/§4（已规划未落地）+ 8.21 卡点文档「预算对齐」。

### M5【统一结果与日志调度】
- 文件：`InvocationKernel.cs`、`EcsEventBus.cs`、`FunctionCallFacade`
- 改动：三套结果类型（`FunctionCallFacadeResult`/`ExecutionResult`/`FunctionCallResult`）收敛为 `AsyncOperationResult`；`FrontEndDrivenLog`/`BackEndLog` UI 侧统一经 `ThreadContext.Post` 单一调度。
- 收益：结果契约唯一；日志即时出现（消除慢调用期间 UI 侧延迟日志缺失导致"看不到完成"的误导）。
- 依据：`现有代码应用集成指南.md` §8。

### M6【脚本/测试契约与可观测性】
- 文件：`agent_real_llm_test.json`、`backend_log_messages.json`（BackEndDriven）
- 改动：脚本 `click wait=true` 的 `timeout` 语义保持"函数执行预算"，等待走 `op.WaitAsync`；`EcsEventBus` 增加 `EcsEventBus_Dispatch_signal_lost`（完成信号丢失归因）日志，使"工作流完成但函数调用未落定"一眼可辨。
- 收益：脚本排障可归因到「函数执行 vs 完成信号」，不再"卡住无日志"。

---

## 六、落地清单与验收

| 里程碑 | 改动文件 | 验收 | 状态 |
|--------|----------|------|------|
| M1 | `ClickSetGetSystems.cs`、`AwaitValueSystem.cs` | 命令线程无 `Task.Wait()`/`GetAwaiter().GetResult()`；慢 LLM 期间脚本让出线程 | 待实施 |
| M2 | `EcsEventBus.cs` | 完成信号经 `AsyncOp` 单一终结；无散落 TCS | 待实施 |
| M3 | `ClineAgentExecutor.cs`、`StatelessAnswerExecutor.cs`、`EndpointResolver` | `cline_execute`/`answer` 走统一 `IExecutionEndpoint`；无 `if (LlmMode.LocalOnly)` 执行分叉 | 待实施 |
| M4 | `ClineClient.cs`、会话预算、`FunctionCallFacade` | 单一预算时钟；取消→杀 node 进程树；心跳可见 | 待实施 |
| M5 | `InvocationKernel.cs`、`EcsEventBus.cs` | 三套结果收敛为 `AsyncOperationResult`；日志即时 | 待实施 |
| M6 | 脚本 + 日志消息 | 真实 LLM 测试**不再卡死**，`wait` 非阻塞完成；失败可归因到完成信号 | 待实施 |

**端到端验收**（GUI）：`agent_real_llm_test.json` 真实模式全流程走完，`click sendMessageButton wait=true` 在 LLM 完成（约 30s）后非阻塞解除，脚本继续到 `[5] 校验状态` 并以 `success=true` 结束；期间 GUI 线程不冻结，可并行操作。

**回归**：`agent_local_test_gui_test.json`（本地）仍跑通；`StateHoldDriven.Tests` / `FrontEndDriven.Tests` / `Agent.Core.Tests`（ConversationPipelineTests）不回归。

---

## 七、与既有方案衔接

- **AEF 蓝图**（`Modules/Threading/docs/现有代码应用集成指南.md`）：M1/M2/M4/M5 是其已规划未落地部分的本方案承接；本方案**补上 M3（LLM 执行端统一抽象）**，使「慢调用治理」有执行端落点，形成完整闭环。
- **8.21 卡点文档**（`LLM 测试卡点已精确定位.md`）：已确认「collect 卡在 daemon 传输层 / 预算错位」。本方案从架构上消除「同步阻塞 + 多时钟 + 完成信号丢失」三类根因，而非继续在传输层逐点打补丁。
- **错误归因（本轮已落地）**：`WorkflowRunner` 的 `errorStep`/`errorClass`/`WorkflowRunner_step_failed` 已把「工作流步骤失败」可归因；本方案补「函数调用层完成信号」的可归因（M6 `signal_lost` 日志），两者互补。
- **公理对齐**：本方案把「同步等待异步长任务」「LLM 执行端未统一」视为违反 `[Strategy] SingleImplementation`（重复执行路径）与 `[AX] SyncProtocolTriple`（Commit→Compensate→Observe 三段提交，完成信号必须可观察）的结构性缺陷，重构使其落地。

---

## 附：关键代码位置速查（当前状态）

| 符号 | 位置 |
|------|------|
| `ClickSystem.wait=true` 同步阻塞 | `BackEndDriven/CommandExecution/ClickSetGetSystems.cs`（`ack.Completion.Wait`） |
| `AwaitValueSystem` 手建 TCS + `Wait()` | `BackEndDriven/CommandExecution/AwaitValueSystem.cs` |
| `EcsEventBus.DispatchFunctionAsync` fire-and-forget + completion | `FrontEndDriven/ControlInteraction/EventBinding/EcsEventBus.cs` |
| `InvocationKernel.ExecuteAsync`（ConfigureAwait(false)，AEF） | `Share/EventBindingOrchestrator/Invocation/InvocationKernel.cs` |
| `ClineAgentExecutor` 本地/真实分叉 | `Agent.Modules/Cline/ClineAgentExecutor.cs`（`if (LlmMode.LocalOnly)`） |
| `ClineClient.AskAsync → daemon` | `Agent.Modules/Cline/ClineClient.cs` |
| 会话工作流运行 | `Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs` |
| UI 会话库 | `Agent.Launcher/Modules/AgentUiLibraryConversation.cs` |
| AEF 迁移蓝图（已规划未落地） | `Modules/Threading/docs/现有代码应用集成指南.md` |



