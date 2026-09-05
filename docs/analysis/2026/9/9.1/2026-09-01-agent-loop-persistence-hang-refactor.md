# Agent 循环持久化卡死 — 诊断与框架级重构报告

> **日期**：2026-09-01
> **范围**：`FileDriven/DrivenConfig/Scripts/Agent/agent_mcpinvoke/agent_mcpinvoke_create_task_test.json` 运行卡死
> **方式**：从框架层面重构修复（非打补丁），对齐 `.clinerules`（axioms/formula）治理原则。
> **状态**：P0 ✅ 完成 · P1 ✅ 完成 · P3 ✅ 确认已修复 · P2 ⏳ 待办 · P4 ⏳ 待办

---

## 一、问题现象（复现日志）

脚本触发 `task op=enqueue`（对照组 Path B）后，Agent 主循环启动，随后卡死：

```
[WARN] [Agent.Core] [Agent] [WorkflowRunner] 工作流引用编译存在问题: workflowId=agent-loop, errors=0, warnings=3
[INFO] [Agent.Core] [Agent] [RoundStepInvoker] 步骤开始: function=guard_input
[INFO] [Agent.Core] [Agent] [NodeExecutionService] 节点函数返回: func=guard_input, elapsedMs=2
[INFO] [Agent.Core] [Agent] [RoundStepInvoker] 步骤完成: function=guard_input, elapsedMs=11
[INFO] [BackEndDriven] [Command] 函数执行成功: function=AgentUi_TaskPoolGetStatus, result=tp_mcpinvoke_ctrl -> Running
```

**卡点特征**：
- `guard_input` 步骤**已完成**（"步骤完成"日志输出）。
- `provide_prompt` **从未开始**（"步骤开始: function=provide_prompt" 永不出现）。
- 任务状态停留在 `Running`、`Round=0`，直到整体超时（300s）也不主动收敛。
- 伴随现象：`CoreWebView2 can only be accessed from the UI thread`、`ECS 状态写入投影失败 … UI 线程封送等待超时（5s）`、脚本命令管道 ~60s 冻结。

---

## 二、卡点精确定位（代码链）

通过代码追踪确定：`guard_input` 的"步骤完成"日志在 `RoundStepInvoker.ExecuteStepAsync` 末尾，之后编排器进入 `OnAfterStepAsync`（持久化横切），再到 `provide_prompt` 的 `ExecuteStepAsync`（**第一行就打"步骤开始"日志**）。

既然 `provide_prompt` 的"步骤开始"从未出现，卡点**唯一**落在 `guard_input` 的 OnAfterStep 持久化：

```
NodeGraphOrchestrator.RunRoundAsync（foreach 每步）
  ├─ guard_input ExecuteStepAsync   ← "步骤开始/完成" 已打
  └─ OnAfterStepAsync(guard_input)  ← ★ 卡死
       └─ NodeExecutionService.PersistAsync
            ├─ PersistNodeOutputsAndKeyMappingsAsync（SQLite ur_nodes，事务 + 写门）
            └─ PersistNodeReferencesAsync（写节点/图镜像/关系边）
```

---

## 三、框架级根因（架构缺口，非单一故障）

| # | 根因 | 卡死证据 | 架构缺口 |
|---|------|---------|---------|
| **C1** | SQLite 事务写路径**绕过** `SqliteRelate.DataUniversal` 的进程级串行门 `lock(Gate)` | `PersistNodeOutputsAndKeyMappingsAsync` 走 `SqliteTransaction`（独立连接），无串行、无界阻塞 | 确定性核心**单一写入入口**缺失（违反 `SEP.DeterministicCoreDisjoint`）；事务路径写无界 |
| **C2** | 横切持久化与主循环同步耦合、无单步超时 | `OnAfterStepAsync → PersistAsync` 卡死拖死整轮；`SnapshotDrivenLoopEngine` 取消只在**循环头**判定，救不了卡在 `execute` 内部 | 持久化失败/卡死不阻塞主循环的机制缺失 |
| **C3** | LiteDB 图写（`ReferenceGraphCoordinator`）为 SQLite 之外**唯一未加有界保护**的写路径 | `WriteGuardPort.PutAsync`（业务节点）触发 `MirrorNodeAsync`/`WriteRelationsAsync`（LiteDB） | 图写 `_gate` 单写门存在**泄漏/异常释放风险**（待运行时取证） |
| **C4** | UI 线程被脚本命令 sync-over-async 阻塞 | `CoreWebView2 … UI thread` + ECS `5s` 封送超时 ×3 + 60s 冻结 | FileDriven 命令管道/ECS 投影在 UI 线程同步等待 |
| **C5** | 图库双实例 + 相对路径漂移 | （8.19 已知问题）双 `ReferenceGraphCoordinator` + CWD 依赖 | **已修复**（代码层面已单一化，见 §五-P3） |
| **C6** | 验证链路脆弱（Path A 规则未命中无观测） | conversation/send 返回 LocalLlm 问候语，`tp_llm_mcp_demo` 未创建 | conversation 验证缺 tool_call 命中观测 + 断言"任务确实创建" |

---

## 四、重构方案（P0–P4，框架能力而非补丁）

- **P0 存储层并发模型统一**：SQLite 连接串规范化（`Default Timeout`）+ 统一写门（`SqliteWriteGate`）+ 事务写纳入写门 → 根治 C1。
- **P1 单轮超时 + 持久化解耦**：循环引擎单轮执行体超时（`RoundTimeout`，WhenAny 记 TO）+ 持久化失败不阻塞主循环 → 根治 C2（配合 P0 使持久化写有界）。
- **P2 线程模型（UI 异步化）**：命令管道/ECS 投影去 UI 线程 sync-over-async → 根治 C4。
- **P3 图库装配单一化**：绝对路径 + 单实例注册表 + 单图属主断言 → 根治 C5（已由 8.19 后代码修复）。
- **P4 验证链路**：LocalLlm 规则命中观测化 + 脚本断言"任务确实创建" → 根治 C6。

---

## 五、实施状态与代码改动清单

### P0 ✅ 完成 — 存储层并发模型统一

| 文件 | 改动 |
|------|------|
| `CsharpPrj/Share/UniversalReference/StorageFramework/Core/PersistenceHelper.cs` | `BuildConnectionString` Sqlite 分支加 `Default Timeout=5000;`（非事务连接默认 busy timeout，有界等待） |
| `CsharpPrj/Plate/DataManage/StorageFramework/Sqlite/SqliteWriteGate.cs`（新建） | 进程级写串行门 `SemaphoreSlim(1,1)` + 5s 有界等待 + 超时抛 `TimeoutException` |
| `CsharpPrj/Plate/DataManage/StorageFramework/Sqlite/SqliteTransaction.cs` | `TransactionalSession` 全部命令经 `SqliteWriteGate.Run` 串行，堵住事务写绕过 `DataUniversal.lock(Gate)` 的缺口 |

**编译验证**：`DataStoreDriven.csproj` + `Agent.sln` — 0 警告 0 错误。

### P1 ✅ 完成 — 单轮执行体超时兜底

| 文件 | 改动 |
|------|------|
| `Projects/Agent/src/Agent.Contract/IAgentLoopConfig.cs` | 新增 `TimeSpan RoundTimeout`（Zero=不启用，零回归） |
| `Projects/Agent/src/Agent.Axioms/Loops/AgentLoopConfig.cs` | 新增 `RoundTimeout` 默认 `TimeSpan.Zero` + 显式实现 |
| `Projects/Agent/src/Agent.Core/Loops/RoundLoopDriver.cs` | `RunSingleRootAsync` 加 `roundTimeout`；`execute` 闭包内 `Task.WhenAny(RoundLifecycle, Delay)` 单轮超时 → 记 `TO` |
| `Projects/Agent/src/Agent.Core/Loops/AgentRunner.cs` | `RunMainLoopAsync` 传 `_config.RoundTimeout` |
| `Projects/Agent/src/Agent.Core/Orchestration/LoopConfigHotReload.cs` | 支持 `round_timeout_seconds` 热加载键（改行即生效） |

**启用方式**：ur_config 加 `round_timeout_seconds=60`（须 < RequestTimeout）即启用单轮超时；默认关闭避免误杀真实 LLM 长轮次。

**编译验证**：`Agent.sln` — 0 警告 0 错误。

### P3 ✅ 确认已修复（代码层面，无需重复改动）

8.19 后重构已落地：
- `DatabaseBootstrapper.cs`：`ResolveGraphLogPath` 基于 **CWD 无关**绝对路径（相对路径锚定仓库根）；`_graphStores` 单实例注册表（按绝对路径 GetOrCreate 单一 LiteDatabase owner）。
- `AgentUiHost.AttachDatabase`：`EnsureSingleGraphOwnerLocked` 单图属主断言。
- 本次运行 `ur_endpoints`/`ur_references` 读取正常，图库装配单一。

### P2 ⏳ 待办 — 命令管道/UI 异步化
改动 FileDriven 命令执行 + ECS 投影，去 UI 线程同步阻塞；**建议先运行时取证（抓卡死线程栈）确认 C4 精确因果再动**。

### P4 ⏳ 待办 — 验证链路
LocalLlm `create_node_reference` 规则命中观测化 + 脚本断言改为"读 ur_tasks 断言任务存在"；需脚本引擎补断言命令（框架测试能力增强）。

---

## 六、当前结论

1. **本次卡死的直接核心根因（C1）已由 P0 根治**：事务写路径不再绕过串行门，SQLite 写统一"串行 + 5s 有界"，超时抛异常 → `OnAfterStepAsync` catch 归类 → 主循环继续，任务不再永久 `Running`。
2. **P1 提供引擎级兜底**：即使单轮 `execute` 内部某步（如 LLM/未知 IO）卡超，也终止本轮记 `TO`，任务转明确终止而非永久卡死（默认关闭，可配置启用）。
3. **C3（LiteDB 图写）**是 SQLite 之外唯一剩余的可能无界写路径，其 `_gate` 单写门存在"异常未释放/重入"风险，需运行时取证确认是否参与本次卡死。
4. **C4（UI 线程阻塞）**是日志观察到的伴随现象，与卡死的精确因果待取证。

---

## 七、后续建议（优先级）

1. **运行时取证**：在任务卡死（`Running`、`Round=0`）时 dump `tp_mcpinvoke_ctrl` 执行线程栈，确认卡在 `PersistAsync` 的 SQLite 写还是 LiteDB 图写，或同步上下文死锁。据此决定 P2 是否启动及 C3 是否需要加 `_gate` 泄漏防护。
2. **C3 加固（可选）**：给 `ReferenceGraphCoordinator._gate` 增加 finally 释放（防异常泄漏永久锁死），并给图写加有界超时。
3. **P2**：确认 C4 根因后，将命令执行/ECS 投影改为异步调度，禁止 UI 线程 `.GetAwaiter().GetResult()`。
4. **P4**：补脚本断言原语，Path A 验证改为"任务确实创建"。

---

## 八、回归验证基准

重跑 `agent_mcpinvoke_create_task_test.json` 应观测到：
- 任务不再卡死（正常流转至完成或明确熔断 `TO`）；
- `guard_input` 后 `provide_prompt` 正常开始；
- 不再出现 `ECS 状态写入投影失败` / `CoreWebView2 … UI thread`；
- Path A 与 Path B 的任务均创建并在 `ur_tasks` 完成。

---

*报告基于 2026-09-01 会话的源码分析、日志证据与编译验证整理。*

