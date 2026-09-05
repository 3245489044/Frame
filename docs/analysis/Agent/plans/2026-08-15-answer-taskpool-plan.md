# Answer 模块经任务池消费 task.md — 实施计划与执行记录

> **日期**：2026-08-15
> **目标**：按 `FileDriven\DrivenConfig\Templates\AgentRun\task.md` 的 `op=enqueue` 示例，入队一个真实任务；**消费者不用 Cline**，由调度选中 `stateless-answer` 执行器（`Agent\src\Agent.Modules\Answer`）经 DeepSeek/SemanticKernel 纯聊天通道消费并回读结果。
> **一句话方案**：即时指令/脚本 `AgentUi_RunJson action=task op=enqueue` 写 `ur_tasks` 激活消费 → 任务 `CurrentStrategyId="stateless-answer"` 使 `AgentRunner` 主循环 `cline_execute` 经 `ResolveExecutor` 按名命中 `StatelessAnswerExecutor`（Answer 模块）→ `ILlmClient.ChatAsync`（DeepSeek，零工具）→ 结果写回 `ur_outputs`。

---

> **历史规划（已过时）**：本文为 2026-08-15 的**目标方案**（拟「真实调 DeepSeek」）。2026-08-16 落地路径改为 `provider=local`（LocalLlm）本地后端——`answer_task_test.json` 命中 **LocalLlm 而非 DeepSeek**；§6 验证标准中「结果来自 DeepSeek（semantic）」判据已过时。**权威现状见 `Agent/本地联调与上下文传递.md`**，本文件仅作历史参考。

---

## 1. 术语与调用链

**"消费者 = Answer 模块"** = 任务池出队 → `AgentOrchestrator.RunAsync` → `AgentRunner` 主循环 → `cline_execute` 步骤 → `AgentRuntimeManager.PlanRound(activeStrategy)` → `ResolveExecutor` 按 `activeStrategy`(=`CurrentStrategyId`) 匹配执行器 `Name` → 命中 `StatelessAnswerExecutor`（`IAgentExecutor`，Name="stateless-answer"，单轮纯聊天零工具）。

```
交互启动.bat（管道 JSON 或脚本）
  └─ 运行测试GUI模式.ps1（识别即时指令/脚本 → 合成 exec AgentUi_RunJson）
      └─ AgentUi_RunJson action=task op=enqueue（task.md 模板字段 + strategy=stateless-answer）
          └─ TaskPoolEnqueueAsync → 写 ur_tasks（写即激活消费）+ 任务 CurrentStrategyId="stateless-answer"
              └─ TaskPool 消费循环 → AgentOrchestrator.RunAsync → AgentRunner 主循环
                  └─ cline_execute → AgentRuntimeManager.ResolveExecutor("stateless-answer")
                      └─ StatelessAnswerExecutor（Answer 模块，IAgentExecutor.ExecuteAsync 单轮）
                          └─ ILlmClient.ChatAsync（SemanticKernel / DeepSeek，零工具）
   结果 ── BackEndDriven 日志"函数执行成功: result=…"──▶ 命名管道 ──▶ ps1 stdout
```

## 2. 现状核查（已确认）

| 项 | 状态 |
|----|------|
| Launcher 接线 | ✅ 已就绪：`AgentOptionalModules.Register` L36 `if(config.RegisterStatelessAnswer)` → `RegisterStatelessAnswer` → `StatelessAnswerModule.Register(orchestrator, name:"stateless-answer")`（`Agent\src\Agent.Launcher\Composition\AgentOptionalModules.cs`） |
| 配置开关 | ✅ `AgentConfig.RegisterStatelessAnswer=true`（默认）；`agent-config.json` 未覆盖（默认 true）；`DefaultAgent="cline"` |
| Answer 模块 | ✅ `StatelessAnswerExecutor`（IAgentExecutor，Name="stateless-answer"，单轮纯聊天零工具，经 `ILlmClient.ChatAsync`，`SemanticKernelFactory.CreateDeepSeekKernel` 读 `credentials/cline.json`） |
| 构建 | ✅ `dotnet build Agent/Agent.sln` 0 错误 0 警告 |
| 执行器选择机制 | `AgentRuntimeManager.ResolveExecutor(activeStrategy)` 按策略名匹配执行器 Name，否则回退首个注册执行器（注册序=cline） |
| **集成缺口（实施前发现）** | 任务 `AgentTaskItem.CurrentStrategyId` 默认空、`RunRequest` 无执行器字段 → 入队任务会回退首个执行器（cline），**无法选中 stateless-answer** |

## 3. 实施改动（本次会话）

**文件**：`Agent\src\Agent.Launcher\Modules\AgentUiLibrary.cs`

1. **`RunRequest` 增可选 `Strategy` 字段**：`task op=enqueue` 请求体可显式指定初始生效策略/执行器（如 `strategy:"stateless-answer"`）。
2. **`task op=enqueue` 分发透传**：`TaskPoolEnqueueAsync(..., req.SchedulerRulesKey, req.Strategy)`。
3. **`TaskPoolEnqueueAsync` 设初始策略**：`strategy` 非空时 `item.CurrentStrategyId = strategy`，并加入 `AvailableStrategyIds`（使 SwitchStrategy 守卫可接受）。缺省 null = 既有行为（回退 cline），**不破坏既有 cline 任务测试**。

> 语义：`CurrentStrategyId="stateless-answer"` → `PlanRound` 按名命中 `StatelessAnswerExecutor`，使消费者为 Answer 模块。

## 4. 新建测试脚本

**文件**：`FileDriven\DrivenConfig\Scripts\Agent\answer_task_test.json`

流程：① `AgentUi_GetStatus`/`AgentUi_GetAgentStatuses`（确认 stateless-answer 已注册）→ ② `AgentUi_SetConfig` 写 `scheduler.rules.answer_test`（`continue`，保持初始策略）→ ③ `AgentUi_RunJson action=task op=enqueue`（`strategy=stateless-answer`, `mode=Config`, `schedulerRulesKey=scheduler.rules.answer_test`, `taskKey=tp_answer_test`）→ ④ 状态回读 → ⑤ `AgentUi_ListOutputs` 结果回读 → ⑥ 清理（op=delete + DeleteConfig）。

## 5. 执行方式

```powershell
& "D:\Project\Program\WorkFlowPrj\PowerShellScript\FileDriven\FileDriven\运行测试GUI模式.ps1" "Agent\answer_task_test.json"
```

> 前置：GUI 宿主须装配 Agent 框架 + 任务池（`AgentFramework` → `DataStoreHosting.RegisterTaskPool`）+ 数据库（`AgentUiHost.AttachTaskPool`/`AttachDatabase`）。未装配时各动作返回「未装配」诊断、不消耗额度。

## 6. 验证标准

| # | 判据 |
|---|------|
| 1 | 入队返回含 `策略=stateless-answer`；任务状态到达 `Completed`/`Faulted`（可判定，非滞留 Running/Paused） |
| 2 | 结果文本非空，来自 DeepSeek 纯聊天（semantic），**无** Cline/工具调用痕迹 |
| 3 | 日志/`AgentUi_GetRuntimeState` 可见执行器为 `stateless-answer`（非 cline） |
| 4 | 诊断日志：`%TEMP%\fileDriven_launcher.log` |

## 7. 已知约束与风险

| 项 | 说明 |
|----|------|
| 必须 GUI 宿主 | headless CLI 未装配 `IEventPlatform`/Agent 运行时；Agent 动作走 GUI 宿主（bat 默认） |
| 真实 DeepSeek 调用 | 消耗 API 额度（SemanticKernel 通道读 `credentials/cline.json` defaultProvider=deepseek） |
| 单轮语义 | 任务池路径走 `StatelessAnswerExecutor.IAgentExecutor.ExecuteAsync`（单轮）；多轮精化（`StatelessAnswerLoop`）仅 `AnswerAsync` 门面使用，不在本路径 |
| 任务池装配 | 依赖 GUI 宿主开启任务池 + 数据库；否则返回「未装配」诊断 |

## 8. 会话待办（执行+验收）

- [ ] 运行 `运行测试GUI模式.ps1 "Agent\answer_task_test.json"`，按 §6 验收。
- [ ] 若 GUI 宿主未装配任务池/数据库 → 返回诊断，需核对宿主装配配置后再跑。
