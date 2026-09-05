# 高风险项重构规划（A1 / B1 / B5）

> 范围：Agent 项目高风险重构项的可行性分析、分步方案与验证策略。
> 生成日期：2026-08-09 | 状态：A1 已实施；B1/B5 待排期

---

## A1 — 跨程序集命名空间冲突与项目边界漂移

### 现状事实（已核验）
- 依赖方向：`Agent.Core.csproj → Agent.Axioms.csproj`（单向，Agent.Core 引用 Agent.Axioms；Agent.Axioms 不引用 Agent.Core）。
- **两个程序集同时声明 `Agent.Core` / `Agent.Planner` / `Agent.Llm` 命名空间**：
  - Agent.Axioms 程序集内：`Agent.Core`（WorkflowRunner/RoundStateMapper/LoopDecisionMapper/LeaseDeriver/DecisionWordMapper/RoundKeys/RoundSpec… 17 文件）、`Agent.Core.Boundaries`（4）、`Agent.Core.Diagnostics`（1）、`Agent.Core.Infrastructure`（1）、`Agent.Core.Strategies`（3）、`Agent.Llm.*`（5）、`Agent.Planner*`（16）。
  - Agent.Core 程序集内：`Agent.Core`（AgentRunner/AgentOrchestrator…）、`Agent.Core.Boundaries`（7）、`Agent.Llm`、`Agent.Planner`。
- 运行时无歧义（无同类型名冲突），属**维护性/概念漂移**，非运行时缺陷。

### 风险探测结论（关键）
| 探测项 | 结果 |
|---|---|
| 嵌入资源 `LogicalName="Agent.Core.Prompts.xxx"`（csproj） | 与 C# 命名空间**无耦合**；`PromptCatalog.GetManifestResourceStream("Agent.Core.Prompts."+...)` 为字符串常量，重命名命名空间不受影响 |
| 反射按命名空间字符串查找（`GetType("Agent.Core...")`） | 无 |
| `InternalsVisibleTo`（AssemblyInfo.cs） | 程序集级，与命名空间无关，不受影响 |
| XML 文档 `<see cref="Agent.Core.AgentOrchestrator"/>` | 该类型在 Agent.Core 程序集，不随本次改动，仍可解析 |

**结论：命名空间重命名 = 纯编译期、行为保持；`dotnet build` 的编译错误即全部待修引用清单（唯一兜底）。**

### 分步方案
目标：Agent.Axioms 项目全部文件命名空间对齐项目 `RootNamespace=Agent.Axioms`，消除跨程序集命名空间冲突。

| 阶段 | 范围 | 映射 |
|---|---|---|
| 1（本轮实施） | `Agent.Core` / `Agent.Core.*` 族（26 文件） | `Agent.Core→Agent.Axioms`、`.Boundaries/.Diagnostics/.Infrastructure/.Strategies→` 对应 `Agent.Axioms.*` |
| 2（待排期） | `Agent.Llm.*`（5 文件） | `Agent.Axioms.Llm.*` |
| 3（待排期） | `Agent.Planner*`（16 文件） | `Agent.Axioms.Planner*` |

执行步骤：
1. 脚本按「长名优先」替换 26 文件的 `namespace Agent.Core*` 声明。
2. 修复 Agent.Axioms 内 `Agent.Planner*` 文件对改名类型的 `using`（如 PromptLlmGenerator→AgentLoopFunction）。
3. 修复 Agent.Core 程序集内引用改名类型的文件（AgentRunner/AxiomAgentLoop/DefaultBoundaryFramework 等）补 `using Agent.Axioms;`。
4. `dotnet build` 迭代至 0 错误 0 警告。
5. 遗留清理：不再被引用的命名空间别名校验；XML 注释同步。

### 验证
- 行为保持：仅命名空间变更是编译期符号，运行语义不变。
- 构建 0 错误 0 警告。
- 全量 `git diff` 复核无资源名/反射串被误改。

---

## B1 — `Route`/`SwitchAgent` 只算不用（多 Agent 切换能力缺失）

### 现状事实
- `AgentRunner.Route(signal, budgetLevel)` 计算 `SchedulerAction.{Terminate,SwitchAgent,Retry,Continue}`，但在 `RunInternalAsync` 的 evaluate 回调中**仅用于写日志**，从不据以切换执行器。
- 循环实际终止由 `AxiomLoopEngine` + `TerminationDecider` 依据 eval 裁决；`AgentExecutors` 注册表存在但循环不消费。
- 仅注册一个 `ClineAgentExecutor`（且 cline_execute 硬编码 `SharedExecutor`），无真实第二执行器可切。

### 方案（功能新增，需行为测试，非本轮实施）
方向 A：**真接线多 Agent 切换**（对齐 `docs/langgraph-integration-analysis.md` Supervisor 蓝图）
1. `ILoopDecisionMapper`/`AxiomActionResult` 增「下一轮执行器 Id」承载字段。
2. `AgentRunner` 维护 `CurrentExecutorId`，evaluate 回调据 `Route` 输出选定下一跳执行器。
3. `cline_execute` 从上下文读当前执行器 Id → 经执行器注册表解析目标（替换硬编码 `SharedExecutor`）。
4. 组合根注册多个执行器（coder/researcher/reviewer…）以让 `SwitchAgent` 有真实目标。

方向 B：**裁剪未消费语义**
- 若短期内不引入多执行器：删除 `Route` 的 `SwitchAgent` 分支，`Terminate/Continue` 语义交由引擎 `TerminationDecider` 单一裁决，消除「文档宣称支持熔断切换子代理、实际固定同一 worker」的误导。

建议：先做方向 B（低风险、立即消除误导），多 Agent 能力按方向 A 单独立项。

### 验证
- 方向 B：删除未消费分支后构建 + 回归主循环（done/error/continue 路由不变）。
- 方向 A：需单元/集成测试验证执行器切换与跨轮状态。

---

## B5 — 三重取消/超时机制并存

### 现状事实
- (a) 显式 `CancellationToken` 参数沿调用链传递；(b) `AgentRunCancellation`（Functor/AsyncLocal）在 `AgentRunner.RunInternalAsync` 每轮注入；`ClineAgentExecutor` 在 ct 不可取消时回退 `GetToken()`；(c) 各工作流函数内 `AgentLoopFunction.CheckCancellation()` 读 AsyncLocal。
- (b) 与 (c) 同源（AsyncLocal）；(a) 为显式令牌，与 (b) 存的是同一 `cts.Token`，当前语义一致、工作正常。

### 方案（行为变更风险，需谨慎，非本轮实施）
1. 收敛为「显式 ct 单一传播」：`CheckCancellation()` 改为接收 ct 参数，删除 AsyncLocal 回退。
2. 保留 `AgentRunCancellation.GetBudget()`（超时预算量纲，非取消令牌），与取消解耦。
3. 深层执行端（ClineClient/池层）统一用显式 ct。

### 验证
- 取消/超时回归：正常完成、外部取消、内部超时三条路径分别验证。

---

## 优先级与依赖
| 项 | 依赖 | 本轮 |
|---|---|---|
| A1 阶段1 | 无（纯机械，构建可验证） | ✅ 实施 |
| B1 方向B | 无 | 待排期 |
| A1 阶段2/3、B5 | A1 阶段1 稳固后 | 待排期 |
