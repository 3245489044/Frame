# 非限定策略状态机（AI 驱动）构建文档

> 日期：2026-08-09 | 范围：`Agent/src/Agent.Contract`、`Agent/src/Agent.Axioms`
> 关联：clinerules（`AX-CORE.EssentiallyUndecidable` / `AX.DecisionOutputSet`）、`WorkFlowMethod.LoopFramework`（`IStateMachine` 端口 / `Axioms` 引擎）、`LoopFramework_综合设计文档.md`（v1.2 预留 `LLMStateMachine`/`HybridStateMachine`）
> 方案来源：循环状态逻辑分析（Agent 用 `AxiomLoopEngine` 过程式五步轮次、未用状态机）+ LoopFramework 冗余分析

---

## 1. 重构目标

| # | 目标 | 对应问题 |
|---|------|---------|
| 1 | 构建 **AI 驱动**的策略转换状态机，管理"当前生效策略"状态 | Agent 循环只有确定性决策，`switch_strategy` 死语义未落地 |
| 2 | **不限制死循环**的状态（无 max-iterations / halt / 终止） | 循环终止责任外置（`EssentiallyUndecidable`），策略状态机只管理状态 |
| 3 | 复用 `WorkFlowMethod` 内置状态机抽象（`IStateMachine<TState>` 端口） | 不重复造状态机抽象；该端口目前"活抽象、死实现" |
| 4 | 落地 `LoopFramework_综合设计文档.md` 预留的 `LLMStateMachine` 概念 | 设计蓝图已规划 LLM 决策，代码从未实现 |

---

## 2. 基线

- 构建命令：`dotnet build Agent\Agent.sln -c Debug`
- 基线状态：**0 个警告 / 0 个错误**（2026-08-09 记录）
- 目标框架：`net10.0-windows`
- 测试项目：Agent 侧暂无正式单测项目（`LoopFrameworkUsageTests` 属 WorkFlowMethod 侧）

---

## 3. 需求与设计约束

| 约束 | 说明 | 依据 |
|------|------|------|
| AI 驱动 | 转移决策由 LLM（`IStrategyDirector`）基于遥测 + 历史提出 | 设计文档 v1.2 决策层三态机（Rule/LLM/Hybrid） |
| **不限制死循环** | 状态机自身**无任何 max-iterations / halt / IsCompleted 终止逻辑** | `AX-CORE.EssentiallyUndecidable`：引擎不承担停机判定 |
| 只管理策略状态 | 状态=策略标识；转移=更换当前策略；记录历史 / 发事件 | `AX.DecisionOutputSet.switch_strategy` |
| 失败 fail-safe | AI 导演失败 / 解析失败 → 保持当前策略（stay），不中断调用方 | 对齐零 LLM 兜底哲学（`FirstCallTransient`） |

---

## 4. 现状与依据

### 4.1 LoopFramework 使用现状（关联文档二）

| 子目录 | 生产使用 | 说明 |
|--------|---------|------|
| `Axioms/`（AxiomContext/AxiomLoopEngine/RoundLifecycle/...） | ✅ 是 | Agent 主循环运行其上（24 处引用） |
| `Abstractions/ILoopObserver` | ✅ 是 | Agent/AxiomLoopEngine 消费 |
| `Abstractions/IStateMachine` | ❌ 否 | **活抽象、死实现**（待本方案复活） |
| `Core/LoopEngine`、`StateMachine/RuleBasedStateMachine` 等 | ❌ 否 | 生产死代码（详见文档二清理） |

### 4.2 设计蓝图 vs 代码

`LoopFramework_综合设计文档.md`（v1.2，2026-05-18）决策层规划：
```
IStateMachine<TState>
RuleBasedStateMachine │ LLMStateMachine │ HybridStateMachine
```
- 现状：仅 `RuleBasedStateMachine` 存在（且生产未用）；**`LLMStateMachine`/`HybridStateMachine` 从未实现**。
- 本方案 = 落地 `LLMStateMachine`（AI 驱动）概念，但以"策略状态机"形态、**不接 `LoopEngine` 终止层**。

---

## 5. 设计：复用 `IStateMachine<StrategyState>` 端口，自驱动决策

### 5.1 为什么不接整套 `LoopEngine`

`LoopEngine.ExecuteAsync` 的循环用 `ILoopController.ShouldContinue`（`DefaultLoopController.MaxIterations`）+ `decision.IsCompleted` 短路强制终止——这与"不限制死循环"直接冲突，且强制要求 `IActionExecutor`/`IContextManager`/`ILoopMiddleware`，对"仅管理策略状态"是过度设计。

**正确复用 = 只取决策端口 `IStateMachine<TState>`，舍弃循环层 `LoopEngine`。**

### 5.2 架构

```
外层策略管理钩子（每轮后，可选接入 Agent 循环）
   ↓ 传入 StrategyTelemetry
IStateMachine<StrategyState>.DecideAsync(telemetry, ct)
   ↓ 内部：IStrategyDirector（默认 LlmStrategyDirector，LLM 提议）
StateTransition<StrategyState>{ NextState=目标策略, Reasoning, IsLLMDecision=true }
   ↓ 外部应用：更新 CurrentState / 记录历史 / 发事件（无终止）
```

- 无 `MaxIterations`、无 `IsCompleted` 短路 → 天然不限制死循环，只返回"切到哪个策略"，继续/终止由外部裁决。
### 5.3 契约（`Agent.Contract/Strategies/`，namespace `Agent.Contract.Strategies`）

| 类型 | 职责 |
|------|------|
| `StrategyState`（record） | 策略状态（Id/Name/Purpose） |
| `StrategyTelemetry`（record） | 决策输入遥测快照（轮次/预算/token/eval/errorClass/压力/连续失败/stopReason） |
| `StrategyDirectorContext`（record） | 导演输入：当前策略 + 可用集 + 遥测 + 近期历史 |
| `StrategyTransitionProposal`（record） | 导演输出提议（Target/Reason/Confidence）；null=stay |
| `StrategyTransitionRecord`（record） | 已应用转移历史（From/To/Reason/Confidence/Round/Timestamp） |
| `StrategyDecision`（record） | 一次 `AdvanceAsync` 结果（Switched/From/To/Reason/Confidence） |
| `StrategyStateChangedEventArgs`（class） | 策略变更事件参数 |
| `IStrategyDirector`（interface） | AI 决策端口：`DecideAsync(context, ct) → proposal?` |
| `IStrategyStateMachine`（interface） | 状态机门面：`Current` / `History` / `Available` / `AdvanceAsync` / `Reset` / `StrategyChanged` |

> 注：`IStrategyStateMachine` 为自驱动门面（含历史/事件/目标校验）；其**决策核心可直接实现框架 `IStateMachine<StrategyState>`**，以复用 `StateTransition`/`IsLLMDecision`。两者可选（门面内置状态机实现，或以适配方式暴露 `IStateMachine<StrategyState>`）。

### 5.4 实现（`Agent.Axioms/Strategies/`，namespace `Agent.Core.Strategies`）

| 文件 | 职责 |
|------|------|
| `AiDrivenStrategyStateMachine.cs` | 状态机主体：持 `IStrategyDirector` + 可用策略集；`AdvanceAsync` 调用导演 → 校验目标在可用集（防幻觉）→ 应用转移/记录/发事件；**无任何终止逻辑** |
| `LlmStrategyDirector.cs` | AI 导演默认实现：编码遥测/历史进提示 → `SemanticKernelLlmClient.GenerateJsonAsync` → 解析 `{action,target,reason,confidence}`；失败 → null(stay) |

依赖方向：`Agent.Core → Agent.Axioms → Agent.Contract → WorkFlowMethod`，无环。

---

## 6. 交互流程

```text
Round N 结束后（循环仍继续）
  → 构造 StrategyTelemetry（预算/评估/边界压力/错误分类）
  → strategyMachine.AdvanceAsync(telemetry)
       ├─ LlmStrategyDirector.DecideAsync → proposal
       ├─ proposal==null / 目标==当前 → 保持（Switched=false）
       └─ proposal 目标∈可用集 → 切换（更新 Current / 记历史 / 发 StrategyChanged）
  → 外部按新 Current 重配策略；循环继续（由外部裁决是否终止）
```

---

## 7. 与 `AxiomLoopEngine` 的关系 / 未来接线

- `AxiomLoopEngine`（过程式五步轮次）**保持不变**，作为主循环；本状态机是**可选策略管理层**，不侵入框架。
- 落地方式：在 `AgentRunner` 轮次后、或经 `ILoopObserver` 旁路，调用 `strategyMachine.AdvanceAsync(telemetry)`，将 `StrategyDecision.To` 关联到 `LoopDecision.SwitchStrategy` 语义（可选，后续）。
- 明确边界：本状态机**不裁决 continue/终止**（那是 `AxiomLoopEngine`/`TerminationDecider` 的职责）；它只管理"切到哪个策略"。

---

## 8. 阶段清单

| 阶段 | 内容 | 状态 |
|------|------|------|
| 0 | 基线锁定 + 本文档 | ✅ 本文档 |
| 1 | 契约：`Agent.Contract/Strategies/`（DTO + 两接口） | ☐ 待实施 |
| 2 | 实现：`AiDrivenStrategyStateMachine`（`IStateMachine<StrategyState>` 决策核心 + 门面） | ☐ 待实施 |
| 3 | 实现：`LlmStrategyDirector`（接 `SemanticKernelLlmClient.GenerateJsonAsync`，JSON 提议，fail-safe） | ☐ 待实施 |
| 4 | 验证：`dotnet build Agent\Agent.sln -c Debug` 0 警告 0 错误 | ☐ 待实施 |
| 5 | （可选）接入 `AgentRunner` 轮次后策略管理钩子 | ☐ 待实施 |
| 6 | （可选）确定性规则 + AI 混合 `HybridStrategyStateMachine`（预算≥2/连续失败→switch 短路） | ☐ 待实施 |

---

## 9. 风险与验证

| 风险 | 缓解 |
|------|------|
| 导演失败 / LLM 不可用 | `DecideAsync` 捕获异常 → 返回 null(stay)，不抛出 |
| LLM 提议未注册策略（幻觉） | 目标必须在 `Available` 集内，否则忽略并 Warn |
| 与 `LoopEngine` 终止语义混淆 | 本方案**不接 `LoopEngine`**，只用 `IStateMachine` 端口（文档二保留该端口） |
| 变更面大 | 契约/实现为新增文件，不触碰既有 `Axioms` 引擎；分阶段提交 |

验证方式：每阶段结束 `dotnet build Agent\Agent.sln -c Debug` 保持 0 警告 0 错误；核心单测（可后续补）：`AiDrivenStrategyStateMachine` 用 mock director 验证 切换/去重/未知目标忽略/stay。

---

## 10. 待确认

- `IStrategyStateMachine` 门面 vs 直接以 `IStateMachine<StrategyState>` 对外（是否保留门面的历史/事件便捷能力）。
- 是否接入 `AgentRunner`（阶段 5）或仅作为独立组件交付。
- 是否落地 `HybridStrategyStateMachine`（阶段 6）。
