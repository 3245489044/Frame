# Answer↔Cline 契约对齐与消费层循环骨架收敛方案

> 分析日期：2026-08-25
> 模块范围：`Projects/Agent/src/Agent.Core`、`Projects/Agent/src/Agent.Modules/Answer|Cline`、`Projects/Agent/src/Agent.Contract`
> 目标：把 answer 从"二等执行器"提升为与 cline 对称的一等执行器；为后续 cline-answer-cline 回环与骨架收敛给出分阶段路径。
> 本文档为**设计/实施前方案**，审阅通过后再落地。

---

## 1. 目标

1. 让 `answer` 工作流函数消费与 `cline_execute` **同一份** `AgentExecutorRequest` 输入契约（同一组参数键），输入格式与 cline 完全对称。
2. 让 `answer` 返回与 cline **同基类**的富化结果信封，尽可能填充 `SessionId/TokenUsage/StopReason` 等字段（明确能力边界）。
3. 论证并规划：把消费层两条 disjoint 骨架（`agent-round` 主循环 / `collect-analyze-act` 会话模板）收敛为单一数据驱动骨架，使 `answer` 可进入主循环、支撑 cline-answer-cline。

## 2. 现状（有据化验）

### 2.1 统一接缝已存在，answer 只用了 1/10

| 契约层 | cline | answer |
|---|---|---|
| 端口 | `IAgentExecutor.ExecuteAsync(AgentExecutorRequest, ct)` | 同左 |
| 请求契约 | `AgentExecutorRequest(System+UserContents+SessionId+Cwd+DataDir+Provider/Model/BaseUrl/ClientType+Round+ConversationContext)` | 同左（端口签名一致） |
| 结果契约 | 富化子类 `ClineResult`（+FailureStage/EmptyReason） | 裸 `AgentExecutorResult` |
| 工作流函数参数 | 12 键：`system/l1/l2/session_id/executor/provider/model/base_url/client_type`（见 `agent-loop.json` cline_execute 节点） | 2 键：`question/system`（见 `collect-analyze-act.json` analyze 节点） |

**结论**：统一端口（`IAgentExecutor`）+ 统一请求/结果类型（`AgentExecutorRequest/AgentExecutorResult`）+ 中立桥（`NodeExecutionContract` ↔ `IExecutionEndpoint`）**已全部就位**。差的是 `answer` 的函数体只拼了 `question`，把其余 10 个字段全丢了。

### 2.2 关键证据链（代码位置）

- 对称却微弱的一端：[AnswerWorkflowFunction.ExecuteAnswerAsync](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Answer/AnswerWorkflowFunction.cs#L41-L64) 仅读 `question`+`system`，`ResolveExecutor` 固定 `SharedAnswer`。
- 对齐标杆：[ClineExecutorFunction.BuildRequest](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Modules/Cline/ClineExecutorFunction.cs#L87-L127) 读 12 键、拼 `LayeredPromptRequest`、透传 SessionId/Cwd/DataDir/后端覆盖。
- 双向弯折已收敛：`NodeExecutionContract` 负责 `AgentExecutorRequest/Result ↔ NodeExecutionRequest/Result` 路由，answer 的富化字段本可经 `Extras` 往返。
- 双路径骨架：`agent-loop.json`（主循环，含 cline，无 answer）与 `collect-analyze-act.json`（会话模板，collect/analyze/act 三角色）各自独立加载于 [RoundSpecLoader](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Core/Nodes/RoundSpecLoader.cs#L31-L46)。

### 2.3 能力边界（必须诚实到位的部分）

answer 走**纯聊天**（`ILlmClient.ChatWithToolsAsync`），共享核心只返回 `LlmResult(Content, IsSuccess, Error)`（[LlmModels.cs#L8](file:///d:/Project/Program/WorkFlowPrj/Projects/Agent/src/Agent.Contract/Llm/LlmModels.cs#L8)）。因此：

- **结果信封最高也只有 base 字段**：`Ok/Text/Error/SessionId/OutputState/MessageCount/TokenUsage/StopReason`；cliine 专有的 `FailureStage/EmptyReason`（来自 daemon 遥测）answer 无法等价填充，只能 `null`。
- 若要补齐 `TokenUsage/StopReason`，必须**改造共享聊天核心或底层 LLM 客户端**，让它从真实响应提取 token/stopReason —— 这超出"契约对齐"本身，需单独评估。

## 3. 设计决策

### 3.1 "对称"的权威定义

以 `cline_execute` 的**请求构造函数**为唯一模板，把 answer 的函数体改造成同构读取 + 同构构造。两者最终都构造同一个 `AgentExecutorRequest` 类型，差异只在"UserContents 拼装方式"。

### 3.2 输入契约对齐（Phase 1：本轮核心）

**改动 1 — `AnswerWorkflowFunction` 增补请求构造**
新增 `BuildRequest(system, l1, l2, context)`，读取与 `ClineExecutorFunction.BuildRequest` 相同的参数键：

```text
system, l1, l2               → LayeredPromptRequest（UserContents 有序拼装）
question                    → 若 l1/l2 为空，作为 UserContents[0]（向后兼容兜底）
session_id                  → SessionId
{cwd, data_dir}             → 空间边界
provider / model / base_url / client_type → 显式后端覆盖
round                       → Round
conversation_context        → ConversationContext
```

**改动 2 — `collect-analyze-act.json` analyze 节点扩展 ParameterKeys**
从 `["question","system"]` 扩到与 collect/act 一致：

```json
"ParameterKeys": ["system","l1","l2","question","session_id","round","provider","model","base_url","client_type"]
```

> `cwd/data_dir` 由边界框架经 `BoundaryKeys` 透传；若会话模板走 `LoadWorkflow` 种子路径，其种子键已含这些（见 `ConversationPipeline.SeedWorkflow` 注释）。

**改动 3 — 断言镜像**
新增/镜像 cline 侧的 `BuildRequest` 单测，验证 answer 在相同 `system/l1/l2/session_id/round/provider` 输入下，构造出**逐字段相等**的 `AgentExecutorRequest`。

### 3.3 结果信封补齐（Phase 2：可选，需先验证底层）

**方案**：让 `StatelessAnswerExecutor` 的共享聊天核心 `ChatAsync` 返回 `AgentExecutorResult`（而非仅 `(bool,string,error)`），并从真实 LLM 响应提取 `TokenUsage/StopReason` 填入信封。

**前置验证项**（Gate，未过则 Phase 2 降级为"只填 SessionId/OutputState，实测无 token"）：
- 当前实际后端（SK/OpenAI 兼容/Local）的 `ILlmClient` 响应是否携带 token 用量与停止原因；map 到哪一层。
- 若底层未暴露 → Phase 2 只对齐「请求层」，结果信封明确接受为 base 字段（`TokenUsage/StopReason` 保持 null），不做费力不讨好的底层改造。

### 3.4 骨架收敛（Phase 3：结构性，建议单独立 Plan）

**动机**：`agent-round` 与 `collect-analyze-act` 两条路径不能组合，是"cline-answer-cline 只能在会话模板、且 answer 不进主循环"的根因。

**方向**：把骨架收敛为**单一数据驱动模板**——cline 与 answer 都只是"某 executor 步骤"，由模板声明决定顺序与回环（如 `collect→analyze→act` 天然就是 cline-answer-cline）。框架层（HierarchyMethod）不动。

**为何单独 Plan**：涉及 AgentRunner 主循环与 ConversationPipeline 两条驱动链的统一选择、决策词契约、历史/消息归档的归属，属独立子系统，需独立评审后再分解。本文档只确立方向，不展开到逐任务。

## 4. 分阶段任务分解（Phase 1 可执行）

### Task 1：answer 请求构造对齐

**文件**：
- 修改：`Projects/Agent/src/Agent.Modules/Answer/AnswerWorkflowFunction.cs`（新增 `BuildRequest`）
- 修改：`Projects/Agent/src/Agent.Modules/Answer/StatelessAnswerExecutor.cs`（`ExecuteRequestAsync` 消费已富化的 request，不再自拼降级）
- 测试：`CentralTest/AgentTest/Agent.ClineUsage.Tests/AnswerWorkflowFunctionTests.cs`（新增）

**验证**：`dotnet test CentralTest/AgentTest/Agent.ClineUsage.Tests`

### Task 2：模板参数键更新

**文件**：
- 修改：`Projects/Agent/Config/workflows/collect-analyze-act.json` analyze 节点 ParameterKeys 扩容
- 回归：确认 `RoundSpecLoader.LoadWorkflow("collect-analyze-act")` 仍成功加载、steps 数不变

**验证**：现有模板加载测试 + 一次会话工作流冒烟。

### Task 3：请求构造镜像断言（防止回归漂移）

**文件**：
- 测试：`CentralTest/AgentTest/Agent.ClineUsage.Tests/AnswerWorkflowFunctionTests.cs` 增补"同键不同后端→请求逐字段相等"用例

### Phase 1 完成判据

- answer 步骤在相同骨架输入下构造的 `AgentExecutorRequest` 与 cline 对应字段一致（单测锁定）。
- `collect-analyze-act` 模板 analyze 节点参数全量透传，无字段丢弃。
- 现有 StatelessAnswer/Cline/模板加载测试全绿。

## 5. 风险 / 边界

1. **能力不对称是设计使然，非缺陷**：answer 是纯聊天、无 daemon 续跑，富化字段上限即 base。Phase 2 若底层不暴露 token，明示 null，不硬补。
2. **向后兼容**：`question` 保留为 UserContents 首选兜底；若既有调用只传 `question`，行为不变。
3. **双路径收敛不宜一次做完**：Phase 3 明确建议拆分独立计划，避免一次改动引爆 AgentRunner + ConversationPipeline 两套驱动链。

## 6. 结论

**能提取成通用循环框架吗？** 能，且 80% 已就位（统一端口 + 统一契约 + 中立桥）。缺的只是 answer 输入契约对齐（Phase 1，低风险，本轮可做）与骨架双路径收敛（Phase 3，独立推进）。answer"对比 cline 进一步完善"的第一步就是**输入契约完全对称**，其结果信封受纯聊天能力边界约束，需在实施前明示。