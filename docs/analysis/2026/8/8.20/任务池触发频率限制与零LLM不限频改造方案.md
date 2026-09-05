# 任务池触发频率限制与「零 LLM 不限频」改造方案

> 日期：2026-08-20 · 状态：方案已定，部分实施（装配点注入未完成，见「四、待完成事项」）
> 触发：FileDriven\DrivenConfig\Scripts\Agent\db_localdb_answer_test.json 中 tp_db_localdb_cline（Hybrid）从未启动即被清理

---

## 一、问题根因

### 1.1 30s 触发频率限制门（TriggerRateGate）
- 限频门是任务池全局、固定、与 LLM 无关的配置：
  - TaskPoolOptions.MinTriggerIntervalMs 默认 30000（30s），定义于 CsharpPrj\Share\TaskStatusPool\TaskPool\TaskPoolOptions.cs
  - 消费循环 DrainOnceAsync 在取任务之前无条件 await _rateGate.AcquireAsync(ct)（TaskPool.cs），即「上次放行后 30s 内任何新任务都必须排队等窗口边界」，不区分任务是否调用 LLM
- TriggerRateGate.AcquireAsync：距上次放行小于窗口则 await Task.Delay(remaining) 到窗口边界；_lastReleaseTicks == 0（进程首事件）立即放行

### 1.2 本次 tp_db_localdb_cline 未启动的时间线

| 时刻 | 事件 | 限频门状态 |
|------|------|-----------|
| 18:36:08.126 | answer 入队（进程内首信号，_lastReleaseTicks==0） | 首次立即放行 → 消费 answer |
| 18:36:13.150 | answer → Completed | 完成触发再次 Drain，但距上次放行仅约 5s → await 到约 18:36:38 |
| 18:36:18.203 | cline 入队（Hybrid） | 仍在限频等待窗口 |
| 18:36:23 / 18:36:28 | 查询 cline → NotStarted | 未放行、未出队 |
| 18:36:28.6 / 29.3 | 脚本 cancel + delete cline | 窗口未到（约 18:36:38）即被清理，从未被消费 |

> NotStarted 持续 = 消费循环根本没把任务出队（非执行器缺失）；若执行器缺失会先 Running 再 Faulted。

### 1.3 「无真实 LLM 调用」判定点
- LlmHosting.ResolveProvider(config)（Agent.Launcher\Composition\LlmHosting.cs）：StatelessAnswer.Provider 优先于 Llm.Provider
  - local = LocalLlmClient（无触网 / 无凭据 / 零真实 API）
  - 其它（semantic / deepseek 等）= 真实外部 LLM
- 任务级：AgentTaskItem.SchedulingMode（Hybrid=LLM 导演 / Config=ur_config·零 LLM / Deterministic=纯规则·零 LLM）、ExecutorId（如 stateless-answer）

---
## 二、方案设计（A + B 结合）

- A（全局）：装配期按 provider=local → 整个任务池 MinTriggerIntervalMs = 0（不限频）。本地联调环境（answer + cline 均走 LocalLlm）全部任务不被 30s 卡住。
- B（任务级）：SchedulingMode != Hybrid（Config / Deterministic = 零 LLM）→ 经 SkipRateLimitFor 委托跳过限频，即使真实环境（provider 非 local）也不限频；真实 LLM 任务（Hybrid）仍限频。
- 二者结合：本地环境全局不限频；真实环境仅零 LLM 任务不限频。

---

## 三、已实施改动（截至 2026-08-20 代码当前状态）

### 3.1 TaskPoolOptions.cs（已改）
新增任务级跳过委托属性：
    public Func<ITaskPoolItem, bool>? SkipRateLimitFor { get; set; }
语义：true = 该任务不限频、立即放行、不更新限频窗口；null = 对所有任务统一限频。

### 3.2 TaskPool.cs（已改，功能可用、格式需整理）
- 新增字段 _skipRateLimitFor（构造时赋值 options?.SkipRateLimitFor）
- DrainOnceAsync：限频门从「取任务前」移到「取到 item 后」，并按任务跳过：
    var item = pending.OrderByDescending(p => p.Priority).First();
    if (_skipRateLimitFor?.Invoke(item) != true)
        await _rateGate.AcquireAsync(ct).ConfigureAwait(false);
> 已知遗留：本次脚本化编辑使 DrainOnceAsync 中 WaitForResumeAsync 与 var pending 挤在同一行（约 311 行），功能正确但格式异常，待整理。

### 3.3 TaskPoolHosting.cs（已改）
零 LLM 纯规则池（TryBootstrapRulePoolAsync）装配时 MinTriggerIntervalMs = 0：
    var adapter = new AgentTaskPoolAdapter(store, executors, new TaskPoolOptions { MinTriggerIntervalMs = 0 }, outputZone: db.Zones);

### 3.4 AgentFrameworkAssembler.cs（未实施）
FrameworkTaskPoolStage.ExecuteCoreAsync（任务池主装配点，已持有 ctx.Config）尚未注入 A/B 策略：
- 未在装配前对 ctx.Config.TaskPool 应用「provider=local → MinTriggerIntervalMs=0」
- 未设置 SkipRateLimitFor = item => ((AgentTaskItem)item).SchedulingMode != SchedulingMode.Hybrid
- 需补充 using Agent.Contract.Scheduling 与 using Share.TaskStatusPool.TaskPool

---
## 四、待完成事项

1. 完成装配点注入（A+B 生效的关键）：在 AgentFrameworkAssembler.FrameworkTaskPoolStage.ExecuteCoreAsync 内构造并传入已应用策略的 TaskPoolOptions：
   - provider=local → MinTriggerIntervalMs = 0
   - SkipRateLimitFor = item => ((AgentTaskItem)item).SchedulingMode != SchedulingMode.Hybrid
2. 整理 TaskPool.cs 311 行格式（WaitForResumeAsync 与 var pending 拆行）。
3. 编译验证：Agent\Agent.sln 及关联工程全仓编译通过（0 警告 0 错误）。
4. 回归：PowerShellScript\TaskPool\Test-AgentTaskPool.ps1 与 agent_taskpool_test.json（[8] 30s 限频验证仍应通过，确认限频门对真实路径行为不变）。

---

## 五、测试脚本配合（不依赖代码即可先跑通 cline）
- 配置前置：确保 agent-config.json 把 StatelessAnswer.Provider=local（最好 Llm.Provider=local）+ LocalLlmHttp.Enabled=true + Agents.cline.BaseUrl=http://127.0.0.1:8787/v1，使 provider 判定为 local（方案 A 触发）。本次日志 WARN 显示 provider=deepseek（未注册），故 A 未生效。
- 观测窗口：cline 入队后的两段 sleep 5000（约 10s）小于 30s 窗口；若需在限频门仍生效时跑通，可临时把等待改为 sleep 31000 跨窗口，或将 answer/cline 拆成两个脚本分两次运行（进程首任务立即放行）。

---

## 六、设计要点与注意
- 不污染 Share 层：SkipRateLimitFor 是委托注入，Share 层 TaskPool 不依赖任何 Agent/LLM 类型，保持「与 Agent 无关」的通用性。
- 窗口语义：跳过限频的任务不更新 _lastReleaseTicks，后续真实 LLM 任务仍按其窗口限频（零 LLM 与真实任务交错时，真实任务之间保持 ≥30s 间隔）。
- 原始目的保留：限频门为「防大量 enqueue 写风暴导致紧密 DB 查询」；零 LLM 任务快速执行本身风险可控，本地联调场景下解除卡顿是期望行为。
