# answer_task_test 任务池运行：问题链、修复记录与当前状态

> **日期**：2026-08-16
> **范围**：`FileDriven/DrivenConfig/Scripts/Agent/answer_task_test.json` 触发的「写 ur_tasks 即激活消费 → stateless-answer 执行器」端到端链路，自 2026-08-16 会话起的问题定位、架构修复与遗留风险。
> **一句话现状**：任务池消费循环已跑通（`mode=Config` 秒级 `Completed`），但**当前配置并未真实调用 LLM/DeepSeek**；`mode=Hybrid` 走 LLM 路径但调度层挂起（未达 API），是该链路「真实回答」目标的唯一遗留阻塞。

---

> **文档定位**：本文件为 **2026-08-16 会话的运行记录（历史快照）**。此后链路已收敛为 `provider=local`（LocalLlm）本地后端——`answer_task_test.json` 的 stateless-answer 执行器经 `ILlmClient.ChatAsync` 命中 **LocalLlm，不调用 DeepSeek**。**权威调用路径见 `Agent/本地联调与上下文传递.md`**；本文件所述「Hybrid 挂起」「真实调 DeepSeek 目标」为当时状态，已被 local 路径取代，勿再作为现状引用。

---

## 1. 背景与调用链

```
外部入口（脚本/即时指令）AgentUi_RunJson action=task op=enqueue
  → TaskPoolEnqueueAsync 写 ur_tasks（写即激活消费循环）
  → 任务池消费循环 TaskPool<TItem,TResult>（Share 层状态机，MaxConcurrency=1）
  → AgentTaskPoolAdapter.Resolve 按 TaskRouter/Disposition 选执行器：
        Hybrid → LlmTaskExecutor（→ AgentOrchestrator → cline_execute → ResolveExecutor("stateless-answer") → StatelessAnswerExecutor → ILlmClient.ChatAsync → DeepSeek）
        Config → RuleTaskExecutor（零 LLM，读 ur_config 规则，确定性 config-schedule 输出）
  → 结果写回 ur_outputs（OutputZoneWriter.Success / Failure）
```

关键事实：
- **`mode=Hybrid`**：调度走 `LlmStateDirector`（真实 DeepSeek 提议），执行走 LLM 执行器 → 真实回答。
- **`mode=Config`**：调度走 `ConfigStateDirector`（零 LLM），执行走 `RuleTaskExecutor`（**完全不依赖 LLM**，`RuleTaskExecutor.cs:15-19`），`strategy=stateless-answer` **在此模式被忽略**。

---

## 2. 原始问题链（会话初观察）

| # | 现象（原日志） | 根因定位 |
|---|---------------|---------|
| 1 | `database is locked`，第二次 `AgentUi_RunJson` 阻塞 67s 后失败 | 任务仍 `Running` 时重复入队 → 两写者并发写同一 SQLite |
| 2 | 消费循环 `Running` 恒态、无轮次推进 | 异常路径不落进度/心跳，外部无法区分「工作中/挂起」 |
| 3 | 失败输出恒为硬编码「handler 异常路径」 | `WriteFailureOutputAsync` 不带真实异常 |
| 4 | 在途任务被 `delete`/`DeleteConfig` 直接清理 | 清理动作无生命周期守卫 |
| 5 | 多条「日志消息定义未找到」「根键未注册」警告 | `agent_log_messages.json` 缺失 msgId、`DataContractRegistrar` 未登记 `transitionHistory` |

---

## 3. 本次修复清单（11 文件，+182/-20）

### 3.1 架构修复（对应问题 1/2/3/4）

| 修复 | 文件 | 内容 |
|------|------|------|
| ① 入队互斥守卫 | `Agent.Launcher/Modules/AgentUiLibrary.cs` | `TaskPoolEnqueueAsync` 入队前校验同 taskKey 在途（Running/Paused）→ 拒绝重复入队 |
| ⑥ 删除生命周期守卫 | `Agent.Launcher/Modules/AgentUiLibrary.cs` | `TaskPoolDeleteAsync` 在途任务禁止直接删除，须先取消 |
| ③ DB busy 退避重试 | `Plate/DataManage/Storage/Sqlite/SqliteRelate.cs` | `ExecuteSQL` 对 `database is locked/busy` 指数退避重试（5 次，200ms→2s）+ `IsBusy` 判定 |
| ④ 在途心跳 | `Share/TaskStatusPool/TaskPool/TaskPool.cs` + `TaskPoolOptions.cs` | 处理体执行期周期性 Running→Running 条件更新刷新 `UpdatedAt`，stuck Running 可被陈旧度观测；`HeartbeatIntervalMs`（默认 15s，0=关闭） |
| ⑤ 结构化失败 | `Agent.Core/TaskPool/AgentTaskPool.cs` | 失败载荷 `error` 携带真实异常信息 |
| ⑦ 可重跑语义 | `Share/TaskStatusPool/TaskPool/TaskPool.cs` | `EnqueueAsync` 对终态/幽灵内存态重置为 NotStarted（否则清理后重跑同 taskKey 不再出队） |
| ⑧ 结果落库接线 | `Agent.Launcher/Composition/DataStoreHosting.cs` + `AgentFrameworkAssembler.cs` | 框架任务池装配补传 `outputZone`（此前 `WriteOutputAsync` 空转，结果不写 ur_outputs） |

### 3.2 JSON / 注册警告补充

| 文件 | 补充内容 |
|------|---------|
| `agent_log_messages.json`（+64） | 补齐 10 个缺失 msgId：`DefaultBoundaryFramework_PreRound/_PostRound_reconstruct/_PostRound_healthy`、`AxiomBinding_report/_drift/_gap`、`BoundaryConfigLoader_Load_loaded/_validation_failed/_not_found/_failed`、`AgentOrchestrator_SetGlobalLlmGate`、`AgentOrchestrator_SetRoundPauseCheck` |
| `Agent.Axioms/Loops/DataContractRegistrar.cs`（+4） | 登记 `transitionHistory` 契约（string，CrossCutting），消除「根键未注册」警告 |

### 3.3 脚本完善（`answer_task_test.json`）

- **[0.5] 预清理**：`AgentUi_TaskPoolCancel` → `op=delete`，清掉上次中断残留的 Running 任务，保证干净起点（幂等）。
- **[4] 末尾清理**：取消（若在途）→ 删除 → DeleteConfig。
- **[2] 入队 `mode` 由 `Hybrid` → `Config`**：绕开 Hybrid 每轮真实 LLM 调度挂起，使任务能秒级完成。

---

## 4. 当前运行状态（2026-08-16 实测）

| 项 | 状态 |
|----|------|
| 消费循环跑通 | ✅ `mode=Config` 入队 id=17 → 首轮轮询即 `Completed`（~5s），不再卡住 |
| 预清理/末尾清理 | ✅ Cancel/Delete/DeleteConfig 均成功，删除守卫放行 |
| `database is locked` | ✅ 已消除（守卫 + busy 重试） |
| msgId / transitionHistory 警告 | ✅ 已补齐 |
| 结果落库 | ⚠️ 已修 outputZone 接线，待重跑确认 ur_outputs 有 `[10]` 新行 |
| **真实 LLM 调用** | ❌ **未发生**（见 §5） |

---

## 5. 关键遗留问题：当前未真正调用 LLM

**结论**：`mode=Config` 的任务通过 **`RuleTaskExecutor`（零 LLM）** 秒级完成，产出确定性 `config-schedule: continue`，**`strategy=stateless-answer` 在 Config 模式被忽略，从未调用 DeepSeek**（代码证据：`AgentTaskPool.cs:101-107` 按 Disposition 选执行器；`RuleTaskExecutor.cs:15-19`「完全不依赖 LLM」）。

### 5.1 两模式矛盾

| 模式 | 执行器 | LLM 回答 | 现状 |
|------|--------|---------|------|
| `Config` | `RuleTaskExecutor`（零 LLM） | ❌ 不调用 | 秒级 `Completed`，但非真实回答 |
| `Hybrid` | `LlmTaskExecutor` → stateless-answer | ✅ 走 DeepSeek | **调度层 `LlmStateDirector` 在到达 API 前挂起**（无 API 记录，任务恒 Running） |

### 5.2 Hybrid 挂起根因（待排查）

`mode=Hybrid` 下，调度先于执行调用 `LlmStateDirector.DecideAsync`（`LlmStateDirector.cs:61-63`）→ `SemanticKernelLlmClient.GenerateJsonAsync` → `_kernel.InvokeAsync` → 真实 DeepSeek。观察到的现象是**该调用在发出 HTTP 请求前就阻塞**（无 API 记录、无失败日志、任务恒 Running）。可疑阻塞点（按优先级）：
1. **全局 LLM 门控** `GlobalLlmGateHost.CreateAsyncCheck()`（经 `AgentOrchestrator.SetGlobalLlmGate` 注入，与暂停门合成单一 LlmGate seam）——若预算/信号量逻辑阻塞则先于 API 挂起。
2. **SK Filter / `kernel.InvokeAsync` 管道**（`InvocationFilters.cs` 对每次函数调用生效）。
3. **`SemanticKernelFactory.CreateDeepSeekKernel` 惰性初始化**（未配凭据时读文件，可能在首次调用阻塞/失败）。

---

## 6. 下一步建议

### 6.1 若目标是「真实调一次 DeepSeek 拿回答」
优先排查 §5.2 的 Hybrid 挂起点，按序：
1. 核查 `GlobalLlmGateHost` 门控检查是否阻塞（是否预算耗尽/信号量未释放）。
2. 核查 `SemanticKernelLlmClient` 的 `kernel.InvokeAsync` 前置链路（SK Filter / Kernel 服务注册）。
3. 确认 DeepSeek 凭据（apiKey/endpoint）是否在框架装配期注入（`SemanticKernelLlmClient.Initialize/BindInstance`）。

### 6.2 若只需任务池链路跑通
`mode=Config` 已达成（确定性规则调度、秒级完成），但需明确**这不是在调 LLM**。

### 6.3 待重跑验证项
- 结果落库：`AgentUi_ListOutputs` 应出现 `[10]` 新行（outputZone 接线后）。
- `SetRoundPauseCheck` / `SetGlobalLlmGate` 警告消失。

---

## 7. 附：测试与构建验证

- `StateManagement.Tests`（TaskPool 相关 13 用例）全通过（含 EnqueueAsync 可重跑改动）。
- `TaskStatusPool` / `Plate` / `Agent.Core` / `Agent.Axioms` / `Agent.Launcher` / `DataStoreDriven` 均 0 错误构建。
- `agent_log_messages.json` / `answer_task_test.json` 均通过 JSON 解析校验。
