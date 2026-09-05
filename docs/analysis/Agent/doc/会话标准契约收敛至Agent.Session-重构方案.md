# 会话标准契约收敛至 Agent.Session 重构方案

> **版本**：1.0
> **日期**：2026-08-31
> **状态**：方案分析稿（交付；实施按 P0→P1→P2 分阶段）
> **模块范围**：`Projects/Agent/src/Agent.Orchestration/SessionWorkflow`、`Projects/Agent/src/Agent.Session`、`Projects/Agent/src/Agent.Launcher/Modules/Libraries/AgentUiLibraryConversation.cs`
> **目标**：把 Agent.Orchestration 中残留的**会话标准契约 / 通用会话件**（标准格式反向投影、会话 DTO、会话并发门闸）收敛下沉至 Agent.Session，使 Agent.Session 成为**闭合的「会话标准格式 + 会话标准 DTO + 通用会话件」共享契约层**；编排壳（生命周期/号线）保持原位。
> **前置**：StandardContext 读写框架已落地（StandardContext 模型 + 三粒度构建器 + 渲染 + wire 注册表已迁入 Agent.Session）；Agent.Session 已被 5 个项目引用（Assembly / Core / Modules / Launcher / Orchestration）。

***

## 〇、一句话总览

> Agent.Session 当前只承载「会话标准格式」的**读侧**（StandardContext 模型 + Builder + Provider + Renderer + wire 注册表）。Agent.Orchestration 里有一批**标准契约 / 通用件**被遗落：标准格式的反向投影（`ConversationNodeSync.ToStages`）、会话交互三件 DTO（信号 / 分层产出 / 运行回复）、通用有界会话锁（`BoundedSessionGate`）。三者依赖均落在 Agent.Session 现有边界内（Share.Conversation / Share.TaskStatusPool / BCL），应下沉补齐，使 Agent.Session 成为闭合的标准契约层，编排壳只留纯粹的流程控制。

***

## 一、目标与设计原则

### 1.1 目标

1. **写↔读双向投影内聚**：`StandardContextBuilder`（读侧合成）与 `ConversationNodeSync.ToStages`（写侧/重建反向）同域收拢到 Agent.Session，消除「标准格式的正反向各居一层」的割裂。
2. **标准会话交互契约单一曝光面**：把「会话信号（输入）/ 分层步骤产出 / 运行回复」三个标准 DTO 收敛到 Agent.Session，与 `StandardContext` 并列，供 Assembly / Core / Modules / Orchestration 共用同一套契约。
3. **通用会话件外提**：`BoundedSessionGate` 是「仅淘汰未持有锁的有界 key 互斥」，零领域依赖，提炼为通用件，供多会话 / 多任务并发场景复用。
4. **编排壳保持纯粹**：ConversationPipeline / ConversationCentralPipeline（生命周期五阶段）、NodeWrite 块、折叠/信号接线等**编排与域写逻辑原位不动**，Agent.Orchestration 维持「形态 A：纯编排壳」语义。

### 1.2 设计原则

| #  | 原则             | 语义                                                                                                                                                             |
| -- | -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P1 | **依赖单向性（硬护栏）** | Agent.Session 被 Core / Assembly / Modules 反引用，**绝不能反向引入 Agent.Contract / Agent.Core / WorkFlowMethod**。下沉件只能触碰 Share.Conversation / Share.TaskStatusPool / BCL |
| P2 | **零行为变更**      | 下沉为纯搬移 + using 替换，不改语义、不改依赖注入，回归面为零                                                                                                                            |
| P3 | **标准契约与编排分离**  | DTO/投影/门闸归 Session（标准契约）；生命周期/号线回归 Orchestration（编排壳）                                                                                                          |
| P4 | **可分阶段落地**     | 按 P0（零依赖直接搬）→ P1（补一个引用收敛 DTO）→ P2（概括化）推进，每阶段可独立验证                                                                                                              |
| P5 | **为中性契约预留**    | 域强类型（IConversationEntry / WorkflowRoundResult）不进入 Session；需概括化时先抽中性契约（对齐执行器模型中性约定）                                                                             |

***

## 二、现状盘点（边界与文件归属）

### 2.1 Agent.Session 现有能力（读侧，已闭合）

| 组件                                                     | 职责                                                           |
| ------------------------------------------------------ | ------------------------------------------------------------ |
| `StandardScope` / `StandardContext`                    | 三粒度标准上下文模型（纯数据、零 LLM）                                        |
| `IStandardContextBuilder` / `StandardContextBuilder`   | node / task / history 三粒度构建器（读侧合成，ur\_nodes + ur\_histories） |
| `IStandardContextProvider` / `StandardContextProvider` | 角色→默认粒度映射（MainLoop→History / SubAgent→Task）                  |
| `IStandardContextRenderer` / `StandardContextRenderer` | 渲染为 cline/claude/openai wire JSON                            |
| `StandardContextRole`                                  | 主循环 / 子代理角色                                                  |
| `Wires/ConversationAgentWires` + Registry              | OpenAI / Claude / Cline wire + 注册表                           |

### 2.2 Agent.Orchestration 需审查的 18 个文件

```
SessionWorkflow/
  ConversationPipeline / ConversationCentralPipeline     // 编排壳本体
  ConversationSessionService                             // 门面（依赖 Pipeline）
  ConversationNodeSync                                   // ★ 反向投影（迁 Session）
  ConversationSignal.cs                                  // ★ 会话信号 DTO（迁 Session）
  ConversationReply.cs                                   // ★ 运行回复 DTO（迁 Session）
  ConversationStageInfo（在 ConversationReply.cs 内）      // ★ 分层产出 DTO（迁 Session）
  BoundedSessionGate.cs                                  // ★ 通用有界会话锁（迁 Session）
  SessionContext                                        // ▲ 概括化后迁（P2）
  NodeWrite/ (Pipeline + 5 块)                           // 域写逻辑（留）
  FoldConversationRunner / FoldStepPipeline             // 执行接线（留）
  SignalWorkflowRunner / ConversationSignalFoldAdapter   // 执行接线（留）
```

### 2.3 依据（为何这些该下沉）

Agent.Session 当前是**最低共享层**（无 ModuleRule，被 5 个项目引用），其使命是「会话标准格式 + 标准契约」。Orchestration 中残留件的依赖可行性：

| 下沉候选                                                               | 依赖                                        | 是否在 Session 边界内         |
| ------------------------------------------------------------------ | ----------------------------------------- | ----------------------- |
| `ConversationNodeSync.ToStages`                                    | Share.Conversation.Adapters               | ✅                       |
| `ConversationSignal` / `SubAgentResult` / `ConversationSignalKind` | 无（纯 record/enum）                          | ✅                       |
| `ConversationStageInfo`                                            | 无（纯 record）                               | ✅                       |
| `ConversationReply`                                                | Share.TaskStatusPool + Share.Conversation | ✅（需补 TaskStatusPool 引用） |
| `BoundedSessionGate`                                               | BCL                                       | ✅                       |

***

## 三、分类清单（归属裁决）

| 文件                                                                                                       | 归属                | 理由                                                            |
| -------------------------------------------------------------------------------------------------------- | ----------------- | ------------------------------------------------------------- |
| `ConversationNodeSync`                                                                                   | **迁 Session（P0）** | 标准格式反向投影，与 `StandardContextBuilder` 同域应守一处；依赖仅在 Session 边界内   |
| `ConversationStageInfo`                                                                                  | **迁 Session（P1）** | 标准分层产出 DTO，纯 record                                           |
| `ConversationSignal` / `SubAgentResult` / `ConversationSignalKind`                                       | **迁 Session（P1）** | 标准会话信号模型，零依赖                                                  |
| `ConversationReply`                                                                                      | **迁 Session（P1）** | 标准运行回复 DTO；需给 Session 补 Share.TaskStatusPool                  |
| `BoundedSessionGate`                                                                                     | **迁 Session（P0）** | 通用有界 key 互斥，零领域依赖                                             |
| `SessionContext`                                                                                         | **概括化后迁（P2）**     | 现依赖 AxiomContext / RoundSpec / IConversationEntry（下游）；须先抽中性契约 |
| `ConversationPipeline`                                                                                   | 留 Orchestration   | 编排壳本体，依赖 RoundSpec/WorkflowRunner                             |
| `ConversationCentralPipeline`                                                                            | 留 Orchestration   | 生命周期五阶段（Initialize/Load/Process/Save/Complete）                |
| `NodeWritePipeline` + 5 块                                                                                | 留 Orchestration   | 域写逻辑，依赖 IConversationStore/IConversationEntry                 |
| `FoldConversationRunner` / `FoldStepPipeline` / `SignalWorkflowRunner` / `ConversationSignalFoldAdapter` | 留 Orchestration   | 执行后端（LoopFoldMethod/SkConversationFoldOperator）接线             |
| `ConversationSessionService`                                                                             | 留 Orchestration   | 依赖 ConversationPipeline；其会话串行核心已在 BoundedSessionGate          |

***

## 四、实施计划

### 4.1 P0：零依赖直接搬（ConversationNodeSync + BoundedSessionGate）

* 迁移文件：

  * `Agent.Orchestration/SessionWorkflow/ConversationNodeSync.cs` → `Agent.Session/StandardContextSync.cs`（或 `_sync/`）

  * `Agent.Orchestration/SessionWorkflow/BoundedSessionGate.cs` → `Agent.Session/Concurrency/BoundedSessionGate.cs`（或直接根目录）

* 改动内容：namespace 更改为 `Agent.Session[.xxx]`；引用方补 `using Agent.Session;`。

* 受影响消费方：`ConversationSessionService`、`AgentUiLibraryConversation`（收 `ConversationNodeSync` / `BoundedSessionGate`）。

* 验证：Agent.Orchestration / Agent.Launcher 编译通过；编排 + UI 会话串行与节点回显行为不变。

### 4.2 P1：会话 DTO 收敛（Signal / StageInfo / Reply）

* 迁移文件：

  * `ConversationSignal.cs`（`ConversationSignalKind` + `SubAgentResult` + `ConversationSignal`）→ Agent.Session/Contracts/

  * `ConversationReply.cs`（`ConversationStageInfo` + `ConversationReply`）→ Agent.Session/Contracts/

* csproj 变更：`Agent.Session.csproj` 增补 `Share.TaskStatusPool`（供 `ConversationReply.Status` 的 `TaskExecutionStatus`）。

* 受影响消费方：ConversationPipeline / ConversationCentralPipeline / ConversationSessionService / ConversationSignalFoldAdapter / AgentUiLibraryConversation（改 using）。

* 验证：编排编译通过；Send/GetHistory/GetNodes 对外行为与序列化格式不变（DTO 仅挪命名空间）。

### 4.3 P2（可选，工作量大）：SessionContext 概括化

* 在 Agent.Session 引入中性 `SessionRunContext`：强类型 key 集合 + 容错状态袋（乐观失败容忍），不依赖任何域强类型。

* Orchestration 的 `SessionContext` 收敛为 `SessionRunContext` 的会话侧投影（仅承载 item/log/lease/result 等编排态）。

* 对齐既有「执行器/结点经中性契约、模型无关」工程约定。

* 前置条件：确认不会有 Agent.Contract / Agent.Core 类型泄漏进 Session；否则维持现状（不迁），并不强行推进。

> 建议：P2 如需做，单独一个会话推进，不与 P0/P1 混排。

***

## 五、依赖护栏（硬约束）

迁移前必须满足依赖单向性 **Agent.Session 不反向引用 Agent.Contract / Agent.Core / WorkFlowMethod**：

* ✅ P0（ConversationNodeSync 依赖 Share.Conversation；BoundedSessionGate 依赖 BCL）

* ✅ P1（仅新增 Share.TaskStatusPool，属原语状态枚举库，非 Agent 依赖）

* ⚠️ P2（SessionContext 含下游字段，需中性化后才可迁；否则放弃）

> 任一候选若引入 Agent.Contract / Agent.Core 等下游引用，即判**不可迁**，退回留在 Orchestration。

***

## 六、回归验证标准

| # | 检查项                                                                 | 判定    |
| - | ------------------------------------------------------------------- | ----- |
| 1 | Agent.Orchestration / Agent.Session / Agent.Launcher 编译通过           | ✅ / ❌ |
| 2 | `SendAsync` 同会话串行（BoundedSessionGate）行为不变                           | ✅ / ❌ |
| 3 | `GetNodesAsync` 节点回显结构与 UI 渲染不变                                     | ✅ / ❌ |
| 4 | ConversationSignal → FoldSignal 桥接（ConversationSignalFoldAdapter）不变 | ✅ / ❌ |
| 5 | 无 Agent.Contract / Agent.Core 反向引用进入 Agent.Session                  | ✅ / ❌ |

***

## 七、结论

Agent.Orchestration 应下沉到 Agent.Session 的是**标准契约/通用件**（反向投影、三件 DTO、有界锁），而非**编排/域写**（生命周期、NodeWrite 块、折叠接线）。下沉后：

* Agent.Session 收口为「会话标准格式 + 会话标准 DTO + 通用会话件」的**闭合共享契约层**；

* Agent.Orchestration 回归「形态 A：纯编排壳」（生命周期 + 端口注入 + 取消/追踪），更贴近其 ModuleRule 定位；

* 依赖单向性（Session 为最低共享层）得到维护，可独立测试、被 5 个项目复用。

