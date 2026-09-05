# LLM 调用链与状态集中化分析

> 本文档回答两个问题：
> 1. **一次 LLM 调用的完整流程** —— 从外部请求进入，到远程大模型返回，中间经过哪些项目、哪些环节。
> 2. **如何集中化** —— 当前整体调用链横跨多个项目、状态管理分散，如何收敛到单一入口、单一执行通道、单一状态容器。
>
> 关联文档：《Agent架构集中介绍.md》。本文档为架构观测 + 改造分析，不改变既有代码行为。

---

## 1. 一次 LLM 调用的完整流程

### 1.1 整体拓扑

```
外部请求（JSON）
   │  Agent.Launcher / AgentUiLibraryRuntime.RunJsonAsync（单一分发）
   ├──────────────┬─────────────────────┬──────────────────────┐
   │              │                     │                      │
 runLoop       task op            relations / graph        node / resource …
   │          op=answer              │                      │
   │              │                  │                      │
AgentRunner   任务池消费             DataStoreDriven        DataStoreDriven
（混合调度）  （显式循环）           （引用守卫/图库）        （节点/资源）
   │              │
   │          StatelessAnswerExecutor
   └──────────────┴──────────────────────────────┬───────────────┐
                                                 │               │
                              LlmClientFactory（按 provider 取 ILlmClient）
                                                 │
                              SemanticKernelLlmClient（Kernel.InvokeAsync）
                                                 │
                              SK 横切 Filter（取消检查 / LLM 门）
                                                 │
                              IChatCompletionService → 远程 API
```

关键事实：**两条独立调用路径**（执行端、调度端）在 **客户端层汇合**，最终都走 `SemanticKernelLlmClient` 单点。

### 1.2 路径 A：执行端 —— task op=answer（回答 LLM 调用）

| 环节 | 项目 | 类 / 方法 | 职责 |
|------|------|-----------|------|
| 入口分发 | Agent.Launcher | `AgentUiLibraryRuntime.RunJsonAsync` | `action=task`、`op=answer` → 任务池入队并激活消费 |
| 任务池路由 | Agent.Core | `AgentTaskPoolAdapter.Resolve` | 按 `ExecutorId="stateless-answer"` 命中执行器 |
| 任务池执行器 | Agent.Modules | `StatelessAnswerTaskExecutor.ExecuteAsync` | 循环边界过暂停门；装配执行器与请求 |
| 显式循环 | Agent.Modules | `StatelessAnswerLoop.RunLoopAsync` | `RoundLoopDriver.RunSingleRootAsync` 逐轮：execute=执行器、evaluate=规则集（确定性，非 LLM 自主） |
| 单轮执行 | Agent.Modules | `StatelessAnswerExecutor.ExecuteRoundAsync` | 合成 system+user 提示（round 感知）→ 调 `ILlmClient.ChatAsync` |
| 终止判定 | Agent.Modules | `StatelessAnswerLoop.EvaluateRule` | 规则命中 answerAccepted → stop；预算耗尽 → 终止 |

要点：
- 每轮 `ChatAsync` 为**纯聊天、零工具**（无 SessionId、无边界、无工具面）。
- `answerAccepted` 由确定性 `AcceptWhen` / `AcceptancePolicy` 置位，**非 LLM 自主判断**是否继续精化。
- 终止优先级：规则命中 → 预算耗尽 → 末轮无产出 → 取消。

### 1.3 路径 B：调度端 —— 混合调度每轮决策（导演 LLM 调用）

| 环节 | 项目 | 类 / 方法 | 职责 |
|------|------|-----------|------|
| 循环主干 | Agent.Core | `AgentRunner.RunMainLoopAsync` | 快照驱动唯一 while；每轮经 `env.DecisionPoint` 决策 |
| 决策点 | Agent.Core | `DecisionPoint` | 按调度模式选导演（Hybrid 选 AI 导演） |
| 导演 | Agent.Axioms | `LlmStateDirector.DecideAsync` | 编码整体状态（当前策略/可用集/遥测/近期转移）→ 调 LLM 生成 JSON 提议 |
| 提议解析 | Agent.Axioms | `LlmStateDirector.ParseProposal` | `{action,target_strategy,target_executor,target_status,intent,reason,confidence}` → `StateTransitionProposal` |
| 校验/应用 | Agent.Axioms + Agent.Runtime | `TransitionGuard` + `AiSchedulerStateMachine` | 导演仅提议，计算层校验并应用；非法 → null(stay) |

要点：
- 导演走 `GenerateJsonAsync`（强制 `json_object`）或 `GenerateJsonWithToolsAsync`（Function Calling，可选）。
- fail-safe：LLM 不可用 / 解析失败 / 越界策略 → `stay`，不阻断循环。

### 1.4 客户端汇合层（两条路径共用）

| 环节 | 项目 | 类 / 方法 | 职责 |
|------|------|-----------|------|
| 统一入口 | Agent.Runtime | `LlmClientFactory` | 按 provider key（semantic / local / cline）返回 `ILlmClient` |
| 真实客户端 | Agent.Axioms | `SemanticKernelLlmClient` | 持进程内唯一 Kernel 单例；`BuildHistory`（system + 历史 + user）→ `_kernel.InvokeAsync` |
| 横切过滤 | Agent.Axioms | `InvocationFilters.CancellationFilter / LlmGateFilter` | SK 函数调用前：取消检查（`CheckCancellation`）+ LLM 调用门（合作式暂停 Running⇄Paused） |
| 远程调用 | SK | `IChatCompletionService.GetChatMessageContentAsync` | 最终调用远程大模型 API，返回文本/JSON |

要点：
- LLM 调用统一走 `kernel.InvokeAsync`，使 SK 横切管道对每次调用生效（取消/暂停门）。
- 客户端实例存在**双入口**：`SemanticKernelLlmClient.BindInstance` 静态门面 + `LlmClientFactory.Instance`（见 §2.2 问题 ⑤）。

### 1.5 本地 provider 替代

`provider=local` 时注册 `LocalLlmClient`（Agent.Contract/Llm），纯本地生成（模板 + 规则 + JSON 确定性），无触网、无凭据，供联调/回归/离线。装配期经 `LlmHosting.Configure` 分支注册，跳过 SK Kernel 构建。

### 1.6 配置装配

`Agent.Launcher/Composition/LlmHosting.Configure` 为 **LLM 统一配置入口**：
- 单一来源 `AgentConfig.Llm`；`ResolveProvider` 解析当前 provider（StatelessAnswer.Provider 优先，缺省 semantic）。
- `local` → 注册 LocalLlmClient；其余 → MS DI 装配 `SemanticKernelLlmClient`（Kernel + 客户端），解析后 `BindInstance` 绑定为门面 + 注册进 `LlmClientFactory`。
- `ToClineOptions` 把同一份配置翻译为 Cline 运行选项（执行端另一分支，走 ClineDaemonPool）。

---

## 2. 现状诊断：调用链与状态散落

### 2.1 调用链：入口到执行的分层

| 层 | 项目 | 载体 | 问题 |
|----|------|------|------|
| 入口 | Agent.Launcher | `AgentUiLibraryRuntime.RunJsonAsync` | 单入口 ✓，但 relations/graph 分支存在**双层转发**（Runtime → AgentUiLibraryData → Guard） |
| 引擎 | Agent.Core | `AgentRunner` / `AgentTaskPool` / `AgentOrchestrator` | 主循环与任务池双引擎并存 |
| 执行器 | Agent.Modules | `StatelessAnswer*` / `Cline*` | 按名选中，经任务池路由 ✓ |
| 客户端 | Agent.Axioms + Agent.Runtime | `SemanticKernelLlmClient` + `LlmClientFactory` | 双入口（静态门面 + 工厂） |
| 门控 | Agent.Guardrails | `GlobalLlmGate` | 静态 Instance + AsyncLocal |

### 2.2 状态散落清单（5 处 / 4 个项目）

| # | 持有点 | 位置 | 现状 | 问题 |
|---|--------|------|------|------|
| ① | `AgentRunEnvironment` | Agent.Core/Loops | 单次 run 载体（State/DecisionPoint/Scheduler/Cts/Scope/Hooks） | 显式持有，但仅引擎内可见 |
| ② | `AgentRunCancellation` | Agent.Runtime/State/Loops | **静态类** + `Functor.Begin("agent-run")` AsyncLocal 作用域 | Core 经 `Set(...)` **静态副作用**写入；TaskPool/深层函数隐式读取 |
| ③ | `AgentOrchestrator` 注入态 | Agent.Core/Orchestration | 取消检查 / LLM 暂停门 / 全局门 / 任务池持久化端口 | 与 ①② 语义重叠 |
| ④ | `GlobalLlmGate.Instance` | Agent.Guardrails/Gate | 静态 Instance + `AsyncLocal<GlobalGateScope>` | 跨 run 聚合语义合理，但静态分发 |
| ⑤ | `SK BindInstance` + `LlmClientFactory` | Agent.Axioms + Agent.Runtime | 静态门面 + 工厂并存 | 双客户端入口，装配点分裂 |

### 2.3 依赖方向与根因

**已确认的依赖事实**（csproj 实测）：
- `Agent.Core` → `Agent.Runtime`（Core 引用 Runtime 的状态库）。
- `Agent.Runtime` 仅依赖 `Agent.Contract` + 外部库（低层）。
- 因此分层为：`Contract（底）→ Runtime → Axioms → Core → Modules → Launcher（顶）`。

**根因**：同一份 run 状态被拆在 **Core 的显式环境（①）** 与 **Runtime 的静态隐式作用域（②）** 两处，且靠 `Functor` 隐式透传。引擎（Core，上层）通过 `AgentRunCancellation.Set(...)` 向底层状态库做**静态副作用写入**，跨层不可见、难测试、易残留。

---

## 3. 集中化方案

### 3.1 目标分层（单一依赖方向）

```
Launcher（组合根：装配 / 入口）     ← 唯一持有单例注册
   └→ Core（引擎 / 任务池）          ← 消费状态接口
         └→ Runtime（状态实现库）     ← 状态"唯一实现"落地处
               └→ Contract（纯契约）  ← RunContext 接口定义处
```

### 3.2 三处收敛 + 一处去重

**收敛 1：状态容器单一化**（合并 ①②③）
- 在 `Agent.Contract` 定义 `IRunContext` 契约：TaskKey、CancellationToken、Budget、Scheduler、DecisionPoint、Hooks、LlmGate。
- 在 `Agent.Runtime` 实现唯一可变持有者 `RunContext`（`AgentRunEnvironment` 迁入；`AgentRunCancellation` 退化为 `IRunContext` 读取端口，**删除静态 `Set` 副作用**）。
- 传递：`AgentRunner` 构造 `RunContext` 后**显式传入**执行器/任务池/调度器；`Functor` 仅保留为跨异步边界的兜底透传，不再是状态主体。
- 收益：取消检查 / LLM 门 / 调度上下文从"隐式静态"变"可见显式"；修掉 Core 对 Runtime 的静态副作用依赖。

**收敛 2：客户端单一来源**（合并 ⑤）
- `LlmClientFactory`（Runtime）作为**唯一注册表**保留；删除 `SemanticKernelLlmClient.BindInstance` 静态门面，`Instance` 改走工厂。
- 组合根（`LlmHosting`）一次性装配；所有消费方仅构造注入 `ILlmClient`。

**收敛 3：门控状态归属**（④）
- 保留 `AsyncLocal<GlobalGateScope>`（跨 run 聚合语义合理）；删 `static Instance` 分发，改由组合根注册、经 `AgentOrchestrator` 注入。

**去重 4：转发层收敛**
- `RunJsonAsync` 中 relations/graph 分支直接调 Guard 服务，移除 Runtime → Data 双层转发，Guard 变为纯服务。

### 3.3 迁移步骤（增量、每步可验证）

| 步骤 | 内容 | 验证标准 |
|------|------|----------|
| 1 | 建 `IRunContext` 契约（Contract）+ `RunContext` 实现（Runtime），与现状并置 | 编译通过，行为不变 |
| 2 | `AgentRunner` 改用 `RunContext` 显式透传，替换 `AgentRunCancellation.Set` 静态写 | 单次 run 行为不变，测试跑通 |
| 3 | 删 `BindInstance` 门面，`SemanticKernelLlmClient.Instance` 改经 `LlmClientFactory` 解析 | 两条 LLM 路径（回答 + 导演）结果一致 |
| 4 | `AgentUiLibraryData` 转发去重 | relations/graph 模板全过校验 |
| 5 | 清 `AgentRunCancellation` 静态类残留，收敛到 `RunContext` | 全量回归 |

### 3.4 核心原则

- 状态「谁生产谁持有」：run 状态由引擎生产，落 Runtime 持有，跨层只传契约。
- 单例仅组合根注册：其余层只接受构造注入，不再有 `static Instance`。
- 依赖方向单一：`Launcher → Core → Runtime → Contract`，消除逆向副作用。

---

## 4. 附：关键代码位置索引

| 关注点 | 文件 |
|--------|------|
| 入口分发 | `Agent.Launcher/Modules/AgentUiLibraryRuntime.cs` |
| 主循环引擎 | `Agent.Core/Loops/AgentRunner.cs` |
| 单轮纯聊天执行器 | `Agent.Modules/Answer/StatelessAnswerExecutor.cs` |
| 显式循环（任务池载体） | `Agent.Modules/Answer/StatelessAnswerLoop.cs` |
| 任务池执行器 | `Agent.Modules/Answer/StatelessAnswerTaskExecutor.cs` |
| 调度导演（JSON 提议） | `Agent.Axioms/Scheduling/LlmStateDirector.cs` |
| SK 客户端（真实调用点） | `Agent.Axioms/SemanticKernel/SemanticKernelLlmClient.cs` |
| SK 横切过滤 | `Agent.Axioms/SemanticKernel/Filters/InvocationFilters.cs` |
| 客户端装配工厂 | `Agent.Runtime/State/Infrastructure/LlmClientFactory.cs` |
| LLM 统一配置入口 | `Agent.Launcher/Composition/LlmHosting.cs` |
| run 作用域状态（现状散落点） | `Agent.Runtime/State/Loops/AgentRunCancellation.cs` |
| 全局 LLM 门控 | `Agent.Guardrails/Gate/GlobalLlmGate.cs` |
| relations/graph 转发 | `Agent.Launcher/Modules/AgentUiLibraryData.cs` |

---

*文档状态：分析稿。迁移步骤尚未实施，需用户确认后按 §3.3 逐步落地。*
