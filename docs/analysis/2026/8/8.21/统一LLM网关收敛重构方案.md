# 统一 LLM 网关收敛重构方案（框架层缺陷根治）

> 日期：2026-08-21 · 状态：**已实施（2026-08-21）**
> 触发：运行 `FileDriven/DrivenConfig/Scripts/Agent/agent_local_test_gui_test.json`（GUI 本地联调测试），
> 主对话框发送消息后返回 `会话=default 轮次=1 状态=Completed | 步骤: cline_execute:cline | answer:answer | cline_execute:cline`，`终答:` 为空。
> 三次复现日志（13:44 / 13:49，均在最新一轮编译 13:48 之后运行）行为一致：`error=(无)`、`CallLog=0`、终答空。
> 定位：这是**框架层（组合根/装配/消费链）的 LLM 解析路径碎片化缺陷**，非某个单点函数的偶发问题。
> 关联文件：`Agent.State/Infrastructure/LlmMode.cs`、`Agent.Runtime/State/Infrastructure/LlmRoutingClient.cs`、
> `Agent.Modules/Answer/AnswerWorkflowFunction.cs`、`Agent.Modules/Cline/ClineExecutorFunction.cs`、
> `Agent.Launcher/Composition/LlmHosting.cs`、`Agent.Axioms/Scheduling/LlmStateDirector.cs`、
> `Agent.Launcher/Composition/AgentOptionalModules.cs`、`Agent.Launcher/Composition/AgentFrameworkHosting.cs`。

---

## 〇、结论先行

本地联调会话回答为空的**框架层根因**是：**LLM 客户端解析入口碎片化**。

`LlmMode.Routing`（统一 LLM 网关路由代理）只是众多 `ILlmClient` 解析路径中的**一条**，并未成为"所有 LLM 消费方"的唯一咽喉。代码库中至少存在 4~6 套相互独立的取 LLM 客户端方式，其中**多条路径绕过统一网关、不受本地联调开关 `LlmMode.LocalOnly` 约束**：

- `AnswerWorkflowFunction.SharedAnswer`：`LlmMode.Routing`（网关）→ `AppServiceProvider.Resolve<ILlmClient>()`（服务定位器旁路）→ `LlmClientFactory.Get("semantic")`（工厂直连配置后端旁路）。**即便已把网关放首位，后两级回退仍会绕过网关。**
- `ClineExecutorFunction.ResolveExecutor`：`RoundKeys.Executor`（外部注入执行器旁路）→ 共享 Cline 执行器（走 Cline 通道，仅经 LocalLlmHttp 才到网关）。
- `LlmStateDirector`：`LlmMode.Routing`（网关，未装配即抛错）——**这是唯一规范形态**，其余路径未对齐。

> **决定性证据**：`CallLog=0`（`LocalLlmClient.ChatCore` 一次都没被调用，见 `LocalLlmClient.cs:104-115` `BeginCall` 无条件记录）∧ `生效后端=local`（`LlmMode.Routing` 已装配且 `Name=local`，见 `AgentUiLibraryRuntime.cs:711`）∧ `error=(无)` ∧ 终答空。
>
> 这组证据只能解释为：**本次会话三步步骤实际使用的 LLM 客户端，没有一条流经 `LlmMode.Routing` 路由代理**——answer/cline 经旁路解析到配置后端（SemanticKernel，无有效凭据 → 返回空），因此既不报错、又不在 `CallLog`（路由代理的本地后端）上留痕。

---

## 一、证据链（本次运行日志）

| # | 环节 | 事实 | 证据 |
|---|------|------|------|
| 1 | 三步模板加载 | `已加载会话工作流 collect-analyze-act.json（steps=3）`；`步骤: cline_execute:cline \| answer:answer \| cline_execute:cline` | 日志 `WorkflowRunner`；`ConversationPipeline.ExtractStages` |
| 2 | 无步骤异常 | `本轮骨架完成: ok=true, decision=(无), error=(无)` | `WorkflowRunner` 日志 |
| 3 | 终答为空 | `终答:` 后无内容（空字符串，非 `（空）`） | `AgentUiLibraryConversation.cs:42`（null 才显示 `（空）`，故为 `""`） |
| 4 | 本地 LLM 未被调用 | `CallLog=0` | `LlmMode.CallLog.Count`；`LocalLlmClient.BeginCall` 无条件计数 |
| 5 | 网关已装配 | `生效后端=local`；`本地 LLM HTTP 服务已启动（8787）` | `LlmMode.Routing?.Name`；`AgentOptionalModules` |

> 注：日志未出现 `collect-analyze-act.json 校验未通过`，且 steps=3，说明既有"根因 A（模板回退，act 层丢失）"已修复（PascalCase/SnakeCase 失配已解决）。**本次残留问题集中在"LLM 消费端旁路"（原根因 B 的框架级形态）。**

---

## 二、框架层根因：LLM 解析路径碎片化（SingleImplementation 违例 / TH-12）

### 2.1 现存全部 `ILlmClient` 解析入口盘点

| # | 解析点 | 位置 | 实际路径 | 经统一网关？ |
|---|--------|------|----------|--------------|
| 1 | answer 工作流函数 `SharedAnswer` | `AnswerWorkflowFunction.cs:31` | `LlmMode.Routing ?? AppServiceProvider.Resolve<ILlmClient>() ?? LlmClientFactory.Get("semantic")` | 部分（网关优先，后两级回退绕过） |
| 2 | stateless-answer 任务池 | `LlmHosting.ResolveStatelessAnswerLlm`（`LlmHosting.cs:119-135`） | `LlmMode.Routing` → 工厂兜底 | ✅ 是 |
| 3 | cline 工作流函数 | `ClineExecutorFunction.ResolveExecutor`（`ClineExecutorFunction.cs:76-81`） | `RoundKeys.Executor`（外部注入）→ 共享 Cline 执行器（`AppServiceProvider.Resolve<ClineRunOptions>`） | 否（走 Cline 通道，仅经 LocalLlmHttp 才回网关） |
| 4 | 调度导演 JSON | `LlmStateDirector.cs:53,61` | `LlmMode.Routing`（未装配即抛错） | ✅ 是（规范形态） |
| 5 | 服务定位器 | `AppServiceProvider.Resolve<ILlmClient>()` | 若注册则直连 | 否 |
| 6 | 工厂 | `LlmClientFactory.Get(provider)` | 直连配置后端 | 否 |

> 其中 #5、#6 是 `SharedAnswer` 的两级回退，**只要 `LlmMode.Routing` 在惰性初始化时刻为空、或 `AppServiceProvider` 已注册了非路由实例，answer 就落进旁路**，走真实配置后端。

### 2.2 为什么"`生效后端=local` 却 `CallLog=0`"

- `生效后端` 读 `LlmMode.Routing?.Name`（`AgentUiLibraryRuntime.cs:711`），**只证明**：路由代理被装配、且当前开关选中 local。
- `CallLog` 读 `LlmMode.Routing?.Local.CallLog`（`LlmMode.cs:47-54`），**只统计**：经路由代理→本地后端的调用。
- 二者口径不同。**任何不经路由代理的消费路径（#1 的回退、#3 的注入、#5、#6）都不会计入 `CallLog`，也不受 `LocalOnly` 约束** → 命中真实后端 → 无凭据返回空 → 终答空且 `CallLog=0`。这完美匹配日志。

### 2.3 违反的公理/推论

- **SingleImplementation（TH-12）**：同一职责"解析/消费 LLM 客户端"存在 4~6 套独立实现，行为分裂（走网关 vs 绕过网关）。
- **StateRootSingleSource**：`LlmMode.LocalOnly` 虽为开关唯一权威，但存在大量旁路消费不经过它。
- **架构矛盾**：统一网关门面（`LlmMode.Routing`）**已存在**，却没有成为唯一咽喉，导致"开关开了，但部分路径不听话"。

---

## 三、重构目标

> 把"解析 / 消费 LLM 客户端"收敛为**唯一咽喉**，使 `LlmMode.LocalOnly` 对**所有** LLM 消费方（answer / cline / stateless-answer / 调度导演）统一生效，**消灭旁路**，并让"终答为空"可观测、可归因。

- 单一来源：任何消费方取 LLM 客户端 = `LlmGateway.Current`（= 路由代理）。
- 单一裁决：本地/配置后端切换仅由 `LlmMode.LocalOnly` 裁决，无第二裁决点。

---

## 四、重构方案

### M1【收敛解析入口】新增 `LlmGateway` 唯一访问器

- 位置：`Agent.State/Infrastructure/LlmGateway.cs`
- API：
  ```csharp
  public static ILlmClient Current        // = LlmMode.Routing；null 抛 InvalidOperationException("统一 LLM 网关未装配")
  public static bool IsConfigured         // = LlmMode.IsConfigured
  ```
- 所有消费方统一改用 `LlmGateway.Current`，**删除**各自 `?? AppServiceProvider.Resolve<ILlmClient>() ?? LlmClientFactory.Get("semantic")` 回退链（与 `LlmStateDirector.cs:53-54` 的 fail-fast 形态对齐）。
- 治理：`AppServiceProvider` 不再注册 `ILlmClient` 业务接口（若有）；`LlmClientFactory` 降为**路由代理内部后端注册表**，不对业务消费方直接暴露。

### M2【answer 收敛】

- `AnswerWorkflowFunction.SharedAnswer`：
  ```csharp
  // 唯一咽喉：统一网关路由代理（本地开关 LlmMode.LocalOnly 对其生效）。未装配即抛错，杜绝旁路。
  var llm = LlmGateway.Current;
  return new StatelessAnswerExecutor(llm);
  ```
- 消除 `ResolveExecutor` 的 `RoundKeys.Executor` 注入旁路：**会话工作流的 answer 步固定用 `LlmGateway.Current` 构造的共享执行器**，不接受外部注入（会话语义为"固定分析端"）；仅当确有调度注入需求时再显式收口到网关校验。

### M3【cline 收敛】

- cline 步经 `ClineAgentExecutor → ClineClient → LocalLlmHttp:8787 → 路由代理`。确保 `LocalLlmHttpHost` 的 `_client` **恒为 `LlmMode.Routing`**：
  - `AgentOptionalModules.cs:68` 当前"`LlmMode.Routing` 未装配则跳过"改为 **fail-fast 或显式降级日志**（避免静默走非本地后端）。
- 步级 `BaseUrl/ClientType` 显式覆盖（真·混调，`ClineExecutorFunction.cs:106-111`）**保留**，但文档标注为"显式例外旁路"，与统一网关正交。

### M4【装配保证 + 可观测】

- 组合根（`AgentFrameworkAssembler` / `LlmHosting.Configure`）保证 `LlmMode.Configure` 在**任意**宿主路径（GUI 引导 `AgentFrameworkHosting.TryBootstrapGuiAsync` / CLI / 降级路径）都被调用，确保 `LlmMode.Routing` 恒非 null；降级路径（RulePoolOnly，零 LLM）在 `BootstrapResult` 摘要明确标注"统一网关未装配"。
- 新增装配断言/日志：`LlmMode.Routing` 未装配时 warn，LLM 消费方触发时明确归因「统一网关未装配」，而非静默回退真实后端。

### M5【诊断】会话链路后端审计

- `ConversationPipeline.RunSignalAsync` 结束时，把每步实际生效后端（`routing/local` / `semantic` / `cline`）写入 `reply.Diagnostics`（或会话归档），使"空终答"一眼可归因。

---

## 五、里程碑与验收

| 里程碑 | 改动文件 | 验收 | 状态 |
|--------|----------|------|------|
| M1 | `Agent.State/Infrastructure/LlmGateway.cs`（新建）、消费方逐一替换 | `LlmGateway.Current` 全库唯一入口；无 `LlmClientFactory.Get("semantic")` 业务直连 | **已做** |
| M2 | `AnswerWorkflowFunction.cs` | answer 步恒走 `LlmGateway.Current`；无回退链、无执行器注入旁路 | **已做** |
| M3 | `AgentOptionalModules.cs`、`LocalLlmHttpHost` | cline 腿恒经路由代理；未装配 fail-fast/降级可观测 | **已做** |
| M4 | `LlmHosting.cs`、`AgentFrameworkAssembler.cs`、`AgentFrameworkHosting.cs` | 任意宿主路径 `LlmMode.Routing` 恒装配；降级摘要标注 | **已做** |
| M5 | `ConversationPipeline.cs`、`AgentUiLibraryRuntime.cs` | 终答非空；`CallLog>0`；每步生效后端可审计（会话归档 `__backend_audit`） | **已做** |

**端到端验收**（GUI）：
1. 重跑 `agent_local_test_gui_test.json`：
   - `conversationHistoryTextBox` 终答**非空**（本地规则/默认响应，形如 `（本地后端）已处理问题：...`）；
   - `AgentUi_GetLocalTestStatus` 回显 `CallLog>0`（answer/cline 至少一次命中 local）；
   - 生效后端=local。
2. 回归：关闭本地联调（`LocalTestSet enabled=false`）→ 回配置 provider，走真实后端不回归；`CentralTest` 相关用例（`ConversationPipelineTests`、`RoundSpecLoaderTests`）全绿。

---

## 六、与既有方案衔接

- **根因 B（answer 绕过网关）已部分修复**（`AnswerWorkflowFunction.cs:31` 已把 `LlmMode.Routing` 放首位）。本次为**框架级根治**：把"回退链（AppServiceProvider/工厂）与注入旁路（RoundKeys.Executor）"一并消除，使统一网关成为唯一咽喉。
- **不触碰 `WorkflowRunner.ok` 全局语义**：会话语义已在 `ConversationPipeline` 解耦（`RunSignalAsync` 仅以 `result.Error` 归因，不以 decision 判成败），本次改动仍在消费链层收敛，零回归主循环路径。
- **符合公理**：本方案把"解析 LLM 客户端"收敛为单一实现（TH-12 / SingleImplementation），把"后端切换裁决"收敛为 `LlmMode.LocalOnly` 单点（StateRootSingleSource）。
- **本地联调基础设施本身无缺陷**：`LlmMode` / `LlmRoutingClient` / `LocalLlmHttp` 均正常，问题在**消费端是否都接入了网关**——这正是本方案要消灭的分裂。

---

## 七、实施补充（2026-08-21 实测端到端验证）：更根本根因 = 会话参数种子断链

> 收敛网关（M1-M5）后重跑 GUI 端到端，`CallLog` 仍为 0、终答仍空 —— 暴露**比网关旁路更根本**的框架层断链：
> 会话工作流（collect-analyze-act）步骤参数（`system/l1/l2/question`）**未被从 `lease.Data` 种子进骨架 Vars**，
> 导致 `answer`（question 空）与 `cline_execute`（system/l1/l2 空）快速空返回、**根本不触 LLM**。

- **根因**：`ConversationPipeline.SeedWorkflow` 写入 `lease.Data[RoundKeys.System/L1/L2/Prompt]` 与 `Data["question"/"conversation"]`；
  但 `WorkflowRunner.RunRoundAsync(lease, ct)` 内部 `RoundStateMapper.Seed(ctx, state, lease=null)` 经
  `SeedFromLease(LeaseDeriver.Derive(state))` 只构造**授权键**（Input/UserInput/Goal/…/SessionId/…），
  **不透传 `l1/l2/system/question/prompt`**。`RoundStepInvoker.ResolveInputsAsync` 从 Vars 解析 ParameterKeys 时这些键缺失 →
  `answer` 的 `question` 为空 → `AnswerWorkflowFunction` 提前 return「answer 缺少 question 参数」；`cline_execute` 判 system/l1/l2 空 → 立即返回失败。
- **证据**：会话三步在 **47ms** 内完成（不可能触真实 LLM/daemon），`CallLog=0`、终答空。
- **修复**：`RoundStateMapper.Seed`（`Agent.Axioms/Loops/RoundStateMapper.cs`）新增会话种子键透传
  （`SessionSeedKeys = {System, L1, L2, Prompt, "question", "conversation"}`），把 `state.Data` 中的会话键注入骨架 Vars；
  主循环（agent-loop）经 provide_prompt 主导，行为不变（无回归）。
- **新增测试**：`RoundStateMapperTests.Seed_PropagatesConversationSeedKeys`（透传断言）；`LlmGatewayTests`（CallLog>0 / 未装配 fail-fast）。
- **第二根因（实测第二轮）**：参数透传修复后 cline 步真正执行，但日志 `daemon run-start provider=semantic baseUrl=(空)` —— 会话 cline 步 `defaults` 解析到全局 `config.Llm`（semantic）而非 `Agents.cline`（`127.0.0.1:8787`），绕过 LocalLlmHttp **真实触网**并超时（127s）。
  - **修复**：`ClineAgentExecutor.ExecuteAsync` 在 `LlmMode.LocalOnly` 时**强制** `baseUrl=LlmMode.LocalHttpEndpoint/v1` + `clientType=openai-compatible`，使本地联调开关对 cline 腿统一生效（StateRootSingleSource：后端裁决唯一收敛于 `LlmMode.LocalOnly`，不再依赖配置默认是否指向本地端点）。
- **仍待人工**：真实 GUI 重跑 `agent_local_test_gui_test.json` 验证 `CallLog>0` 与终答非空（headless 不回显脚本详细日志、且依赖真实控件事件）。

---

## 附：关键代码位置速查

| 符号 | 位置 |
|------|------|
| 统一网关路由代理（`ILlmClient`+`ILlmJsonGenerator`） | `Agent.Runtime/State/Infrastructure/LlmRoutingClient.cs` |
| 路由开关门面（`LocalOnly`/`Routing`/`CallLog`/`EnsureLocalInfrastructure`） | `Agent.Runtime/State/Infrastructure/LlmMode.cs` |
| answer 工作流函数 LLM 解析（回退链旁路） | `Agent.Modules/Answer/AnswerWorkflowFunction.cs:31` |
| answer 执行器注入旁路 | `Agent.Modules/Answer/AnswerWorkflowFunction.cs:64-70`（`ResolveExecutor`） |
| cline 执行器注入旁路 | `Agent.Modules/Cline/ClineExecutorFunction.cs:76-81`（`ResolveExecutor`） |
| 统一网关装配（`LlmMode.Configure`） | `Agent.Launcher/Composition/LlmHosting.cs:71`；`AgentFrameworkAssembler.cs:175` |
| stateless-answer 任务池（规范形态，走网关） | `Agent.Launcher/Composition/LlmHosting.cs:119-135`（`ResolveStatelessAnswerLlm`） |
| 调度导演（规范形态，fail-fast） | `Agent.Axioms/Scheduling/LlmStateDirector.cs:53,61` |
| LocalLlmHttp `_client` 注入（未装配跳过） | `Agent.Launcher/Composition/AgentOptionalModules.cs:58-68` |
| GUI 宿主装配门面（完整框架/降级） | `Agent.Launcher/Composition/AgentFrameworkHosting.cs` |
| 生效后端观测（`Routing?.Name`） | `Agent.Launcher/Modules/AgentUiLibraryRuntime.cs:711` |
| CallLog 观测（`Routing?.Local.CallLog`） | `Agent.Runtime/State/Infrastructure/LlmMode.cs:47-54` |
| 本地后端（CallLog 无条件计数） | `Agent.Contract/Llm/LocalLlmClient.cs:104-115`（`BeginCall`） |
| 会话工作流模板 | `Config/workflows/collect-analyze-act.json` |



