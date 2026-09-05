# History 会话编排框架：多 Task 运行态管理与任务装配实施报告

> 范围：`CsharpPrj/Operation/HierarchyMethod`（通用逻辑层）+ `Projects/Agent/src/Agent.Launcher`（Agent 应用层）+ `FileDriven/DrivenConfig/Templates/AgentRun/conversation`（模板层）。
> 日期：2026-08-29 | 验证：HierarchyMethod.csproj / Agent.Launcher.csproj 编译 0 警告 0 错误；HierarchyMethod.Tests 53/53 通过。

---

## 一、背景与问题

当前 Agent 体系存在「从 history 侧启动 agent/task，却要从 task 侧观测和控制」的错位：

1. **状态分裂**：history 只有运行方式声明（`exec.flag`/`exec.lifecycle`）与只读历史；真正运行状态（TaskPool status、`AgentRunStatus`、ProgressToken/Budget/Halt、Round）全在 task 层 `AgentTaskItem` 快照。
2. **控制分裂**：启动在 history（`conversation/send`、`flag/scope`），但控制（pause/cancel/enqueue/status）全在 task 侧（taskpool / `task/` 模板），无 history 级批量控制门面。
3. **装配分裂**：task 提示词 = 三处拼装（工作流 JSON 的 `ParameterKeys` + `ConversationPipeline.SeedWorkflow` 种子键 + `StandardContextBuilder` 上下文粒度），无 history 级默认装配声明。

由此引发两个核心疑问，本报告先给出分析结论，再给出落地框架。

---

## 二、分析结论

### 2.1 三套"标志/状态"体系必须分清

| 域 | 载体 | 枚举 | 语义 |
|----|------|------|------|
| ① 执行标识符（action=flag） | 图元数据 `exec.flag` + `exec.lifecycle` | `ExecutionFlag`：Auto/Llm/Rule/Deterministic/Std；`FlagLifecycleState`：Idle/Flagged/Routed/Running/Completed/Faulted/Cancelled | **"怎么运行"的路由方式声明**，反应式：Set→Signal→解析→路由→运行→回切 Std |
| ② 子智能体执行状态（cline/answer） | 运行态快照 `fw.Runtime.AgentStatuses`（由 `AgentTaskItem.AgentStatus` 投影） | `AgentRunStatus`：Pending/Running/Done/Error/Paused | **"此刻在跑什么"的实时状态** |
| ③ 任务池状态（ur_tasks） | SQLite ur_tasks 载荷 | TaskPool：NotStarted/Running/Paused/Completed/Faulted/Cancelled | **任务记录生命周期（唯一权威状态基座）** |

### 2.2 history 标志位与 cline/answer 执行状态**不一致**（且不可直接比较）

- **词汇域不同**：`Done` vs `Completed` vs `Faulted`/`Error` 是三个独立 enum，无共享 token，`==` 不成立，无统一映射。
- **语义对象不同**：history 标志 = 声明/路由层，且反应式运行完**自动回切 Std(idle)**；cline/answer 状态 = 执行层实时状态。
- **写入路径各自独立**：无单一同步者（违反 `StateRootSingleSource` 理想态）。可同时出现「cline Running」与「history exec.lifecycle=idle」的矛盾。

### 2.3 **不能**仅靠 history 标志位停止 task

- `ExecutionFlag` 无 Stop/Cancel 值（只有路由模式）；对 history 设 `Llm`/`Rule` 是**启动/触发一次运行**而非停止。
- `flag/reset` 只回切元数据（Std/idle），**不取消/暂停真正在跑的任务池执行**（反应式运行是 conductor 后台 drain 的 fire-and-forget）。
- 停止必须走运行时控制层：`task/cancel`（Cancelled）/ `task/pause`（Paused，可恢复）/ `task/activate active=false`（去激活，仅阻止新消费）。

---

## 三、设计方案：History 会话编排框架（History Orchestration）

在 history 容器之上补一层薄编排门面 `IHistoryRuntimeService`，做三件事：**聚合投影、批量控制、装配声明**。

```
┌─────────────────────────────────────────────┐
│ history 会话编排层（IHistoryRuntimeService）  │
│  ① 聚合投影 HistoryRuntimeView（只读派生）    │
│  ② 批量控制 Enqueue/Pause/Resume/Cancel/Step  │
│  ③ 装配声明 HistoryPromptProfile             │
└──────────────┬──────────────────────────────┘
        委托（SingleImplementation，不复制实现）
┌──────────────┴──────────────────────────────┐
│ task 实例层（既有）                           │
│ AgentTaskItem 快照（唯一权威）/ TaskPool      │
│ ConversationPipeline / StandardContext       │
└─────────────────────────────────────────────┘
```

**设计原则（对齐 .clinerules）**：
- `StateRootSingleSource`：运行状态唯一权威仍是 `AgentTaskItem` 快照；history 层运行态**只做只读投影聚合**，不另起独立写。
- `SingleImplementation` / TH-12：history 框架只编排，控制委托 `AgentTaskPoolAdapter`、装配委托 `ConversationPipeline`/`StandardContextBuilder`。
- `ScopeGuard`：history 级批量命令按 `history_owns_task` 限定，不越权其它 history。

---

## 四、实施明细

### 4.1 HierarchyMethod 通用逻辑层（LLM-free，零 Agent 依赖）

新建 `Operation.HierarchyMethod.History.Runtime` 子命名空间（`History/Runtime/`）：

| 文件 | 作用 |
|------|------|
| `HistoryRunStatus.cs` | 单 task 运行状态快照 DTO（词汇中立，承载 TaskPool/AgentRunStatus 两维度文本） |
| `HistoryRuntimeView.cs` | history 运行态聚合视图（TaskCount/Active/Running/Paused/Done/Error + 明细） |
| `HistoryPromptProfile.cs` | history 级装配声明（WorkflowKey/SeedOverrides/ContextScope/Agent/FlagTemplate） |
| `IHistoryRuntimePort.cs` | 单 task 运行注入端口（GetStatus/Start/Pause/Resume/Cancel/Step），由 Agent 层实现 |
| `IHistoryRuntimeService.cs` | history 会话编排统一门面接口（Overview/Start/Pause/Resume/Cancel/Step/Profile） |
| `HistoryRuntimeService.cs` | 纯编排实现：聚合视图 + 按 `history_owns_task` 作用域批量控制 + profile 内存回退 |
| `Abstractions/HierarchyMethodNamespaces.cs` | 追加 Runtime 命名空间 marker |

关键点：**状态只读投影**（聚合经 `IHistoryRuntimePort.GetStatusAsync` 逐 task 读取）、**批量控制 + 作用域守卫**（空 taskKey=全部，`Step` 强制校验归拢归属）、**装配可空**（未装配端口返回空/0）。

### 4.2 Agent 应用层接入

| 文件 | 改动 |
|------|------|
| `Modules/AgentHistoryRuntimePort.cs`（新） | `IHistoryRuntimePort` 适配器：委托 `AgentTaskPoolAdapter`（GetStatus/Pause/Resume/Cancel）+ `ConversationSessionService`（Start/Step 会话单步） |
| `Modules/AgentUiHost.cs` | 新增 `HistoryRuntime` 槽位 + `AttachHistoryRuntime/DetachHistoryRuntime` |
| `Composition/ConversationHosting.cs` | 组合根装配 `HistoryRuntimeService(relations, new AgentHistoryRuntimePort(pool, conversation))` |
| `Modules/AgentUiLibraryConversation.cs` | 新增 `HistoryOverview/HistoryStep/HistoryPause/HistoryResume/HistoryStop` 薄编排 |
| `Modules/AgentUiLibraryRuntime.cs` | `RunJsonAsync` 的 `case "conversation"` 分发新增 `overview/step/pause/resume/stop` |

### 4.3 conversation 模板层

- 新增 `overview.json` / `step.json` / `pause.json` / `resume.json` / `stop.json`
- 更新 `conversation/README.md`（op 字段表、模板清单、示例）

### 4.4 最终能力

从 **history 侧**即可完成多 task 的启动、观测、控制：
- `conversation/overview`：一次总览该 history 全部 task 状态（只读，不消耗额度）。
- `conversation/step`：单 task 单步推进一轮（会话单步）。
- `conversation/pause` / `resume` / `stop`：按 history 作用域批量暂停/恢复/停止（`history_owns_task` 限定，防越权）。

---

## 五、验证结果

| 项目 | 结果 |
|------|------|
| `HierarchyMethod.csproj` | ✅ 编译 0 警告 0 错误 |
| `Agent.Launcher.csproj` | ✅ 编译 0 警告 0 错误 |
| `HierarchyMethod.Tests` | ✅ 53/53 通过（含新增 HistoryRuntimeService 6 用例：聚合计数/批量暂停/单任务暂停/step 作用域守卫/step 委托/profile 内存回退/未装配返回 0） |

---

## 六、边界与后续建议

1. **Step 语义当前=新消息单轮 send**（零新机制、零回归）。若需"对运行中长任务逐轮续跑"，改 `AgentHistoryRuntimePort.StepAsync` 委托 `pool.ResumeAsync` + 快照续跑即可。
2. **`HistoryPromptProfile` 装配声明已建好**，`StartAsync` 已透传 `agent/contextScope`；但 `WorkflowKey`/`FlagTemplate` 落到具体 RoundSpec / SchedulingMode 的完整消费尚未接线（可作下一步）。
3. **一致性根治未完成**：本框架避免了"新增第三套独立状态"，但 `FlagLifecycleState ↔ AgentRunStatus ↔ TaskPool` 三枚举仍无显式映射、无单一对账者。建议后续：
   - 以 `AgentTaskItem` 快照为唯一权威；
   - 补一张 `FlagLifecycleState ↔ AgentRunStatus ↔ TaskPool` 显式映射；
   - 让 history 标志生命周期降为派生/声明层，与执行状态可对账。
4. **并发/作用域**：history 级批量控制建议对 taskKey 列表加锁/判重（复用 `BoundedSessionGate` 思路）。

---

## 七、结论

通过「**history 会话编排框架**」把 **启动、观测、控制、装配四件事收拢到 history 容器层**：状态从 task 聚合上来（只读投影）、控制从 history 批量下去（委托 taskpool）、装配由 history 声明统一（委托会话管线）。多 task 管理由此归一，且未破坏既有 `SchedulingMode`/`TaskPool`/`conversation send` 路径（向后兼容、装配可空、零回归）。

