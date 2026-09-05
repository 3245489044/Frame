# LoopSignal 单面 — 决策三源收敛分析（S4-C）

> **日期**：2026-09-02
> **定位**：承接单一路径重构 `2026-09-02-remove-fallback-single-path-refactor.md` §4.4（决策面）+ 9.3 function-callchain §四 Stage 4（决策信号化）。本文档为 S4-C **前置梳理**——按已核实的源码给出「何时停」决策三源的真实归属与当前收敛状态，判定"单一 LoopSignal 面"是否已达成 / 残余何在。
> **方法**：源码核实（非臆测）。三源 → 各自的写入点 → 是否多头写循环判定。

---

## 一、三源实况（源码核实）

### 源①：管道内 `rule_match` decision 词
- 写入：`rule_match` handler 把决策词写进 `outputs[RoundKeys.Decision]`（`continue`/`stop`/`complete`/`error`）。
- 消费（唯一解码点）：`LoopDecisionMapper.MapEval` 经 `DecisionWordClassifier.IsSuccess(word)` → `EvalOutcome`（`Agent.Core/Nodes/LoopDecisionMapper.cs:34-35`）。
- **关键事实：决策词不独立写 LoopDecision**——它只被 `LoopDecisionMapper` 解码为 eval，不产生 TS/TO/TM。`DecisionWordClassifier`（`Agent.Operation/Loops/DecisionWordClassifier.cs`）是"决策词→语义"的**单一词汇表**（含 `ToFoldDecision` 信号投影单一权威）。
- ⇒ 源①不是循环判定的第二个写入者。

### 源②：`RoundLifecycle.Decide`（评估→终止）
- `Share/TaskStatusPool/Loops/RoundLifecycle.cs`：`evaluate` → `ProgressUpdater`（token）/`BudgetEscalator`（budget）→ ④Decide `TerminationDecider.ShouldTerminate` 产出 **LoopDecision{TS,TO,TM,Continue}**（`round.Decision`）。
- 引擎头 `budgetTerminate` 再调 `TerminationDecider.ShouldTerminate`（`RoundLoopDriver`）+ `lastDecision` 闭包。
- `TerminationDecider`（`LoopDeciders.cs`）终止判定仅两条：`ControlFlag.halt=1→TM`；`ProgressToken.IsExhausted→TO`（对齐 `AX.TerminationPriority`）。
- ⇒ TS/TO/TM 的**唯一裁决 = 引擎 `TerminationDecider`/`RoundLifecycle`**，无第二写入者。

### 源③：调度导演（Llm/Config）提议
- `AgentRunner.NextHybridRoundAsync` → `env.Scheduler.AdvanceAsync`（`AiSchedulerStateMachine`）→ 产出 **`SchedulerAction`**（非限定动作域）。
- `Share/AiScheduling/SchedulerAction.cs:6` **明示："本枚举不携带任何终止语义（继续/终止由外部裁决）"**。
- 仅改 `state.Data`（ActiveStrategy/ExplicitExecutor）等策略/执行状态，**不触碰确定性核心**（Budget/Token/Flag）。
- ⇒ 源③属于 `AX.DecisionOutputSet` 的**非限定动作子域**，本就不含 TS/TO/TM。

---

## 二、判定：循环判定（stop/go/TO/TM）是否多头写？

**否。已单源收敛。** 证据链：

```
rule_match 决策词 ──(仅被)──> LoopDecisionMapper.MapEval ──> EvalOutcome ──> RoundLifecycle: ProgressUpdater/BudgetEscalator
                                                                                └─> TerminationDecider.ShouldTerminate ──> LoopDecision{TS,TO,TM,Continue}  [唯一裁决]
调度导演 ──> SchedulerAction（非限定动作域，明示无终止语义） ──> state.Data 策略/执行状态
```

- `TS/TO/TM` 唯一写入者 = 引擎 `TerminationDecider`/`RoundLifecycle`（`RoundLoopDriver.lastDecision` + 引擎头 `budgetTerminate`）。
- `continue/switch_strategy` 为循环内非终止决策；rule_match 词仅解码为 eval，不直写循环判定。
- `DecisionPoint`（`Agent.Core/Loops/DecisionPoint.cs:21-23` 注释）已声明"只裁决循环内评估，不裁决路由/终止"——与代码一致。

> **结论**：9.3 I7「决策/导演三源语义重复」在"谁写循环判定"这个层面**已被既有收敛消除**（S1/S2 前的历史上确有多头写，现收敛于 TerminationDecider 单裁决 + rule_match 词单解码）。S4-C 的"单一 LoopSignal 面"**在循环判定层已达成**，无第二套写 stop/go/TO/TM 的代码。

---

## 三、真正的残余：多套"观测信号"枚举（非循环判定，勿误合并）

代码存在多套**平行的观测/信号投影**，它们**不写循环判定**，但语义相近易被误判为重复：

| 信号域 | 来源 | 语义 |
|--------|------|------|
| `LoopDecision` | `RoundLoopDriver.lastDecision` / 引擎 | 循环判定 + 终止（TS/TO/TM）|
| `FoldDecision`(Continue/Stop/Error) | `DecisionWordClassifier.ToFoldDecision`（决策信号化单一权威） | 折叠域动作投影 |
| `AgentSignal`(DONE/ERROR/RETRY) | `DecisionPoint.ToAgentSignal`：ReturnSpec 优先 / `AgentSignalClassifier.FromEval` 兜底 | HFSM 租约信号（仅观测）|
| `SchedulerAction` | `AiSchedulerStateMachine` | 非限定动作域 |

- `DecisionPoint.cs:60-62` 注释已明确：ReturnSpec→AgentSignal 与 FromEval 是**两个互补枚举映射**，强行合并会丢结构信息，故保留双分支。
- **不应**为"形式上单面"而合并这些观测信号域——它们分属不同投影，合并违反 `DecisionOutputSet` 的终止/非终止二分与既有"勿叠第三套"注记。

---

## 四、S4-C 落地判定（诚实的收敛范围）

1. **不需要为"单源写循环判定"新增 LoopSignal**——该项已由 `TerminationDecider`/引擎单裁决 + 词单解码满足（§二）。
2. **残余可动点（可选，低风险）**：观测信号域无**统一入口抽象**，各消费方分别依赖 `LoopDecision`/`FoldDecision`/`AgentSignal`。若确要"信号化单面"，建议**新增一个只读的 `LoopSignal` 视图（非写通道）**：在每轮边界把 `round.Decision`（LoopDecision）投影成统一只读信号供观测/UI 消费，**不替换** FoldDecision/AgentSignal（保留各自的消费者与结构信息），仅作**统一读取面**。此为加性、默认不改变现网写路径。

   - 改：`Agent.Core/Loops/`（新增 `LoopSignal.cs` 只读投影）+ 每轮边界调用点（`RoundLoopDriver` 收尾或 `AgentRunner` next）。
   - 风险：低（加性、不改现有写语义）；需运行级确认观测无回归。
3. **明确不做**：不合并 FoldDecision/AgentSignal；不让 rule_match 词直写循环判定；不让调度导演触碰确定性核心。

---

## 五、验收口径

- 全库 `grep`：`TS/TO/TM` 循环判定**仅**由 `TerminationDecider`/`RoundLifecycle`（引擎）+ `AgentLoopTermination.ConvergeFinal` 收敛，无第二写入者。
- 若新增只读 `LoopSignal`：其为纯投影，不改 `lastDecision`/`budgetTerminate`/`SchedulerAction` 任何写路径。
- `Agent.sln` 编译 0 错误；既有决策单测（`LoopDecisionMapperTests`/`SnapshotLoopEngineTests`）全绿。

---

*本文档基于 2026-09-02 会话对决策机器（RoundLoopDriver / RoundLifecycle / TerminationDecider / LoopDecisionMapper / DecisionPoint / AiSchedulerStateMachine / DecisionWordClassifier / SchedulerAction）的源码核实整理，作为 S4-C 的收敛判定与（可选）加性 LoopSignal 视图的实施依据。*
