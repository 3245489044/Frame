# LoopFoldMethod · 状态折叠框架设计（以折叠替代循环）

> 版本：v1（理念重构版）
> 日期：2026-08-25
> 定位：`CsharpPrj/Operation/LoopFoldMethod`（Operation 层 · Agent 无关 · LLM-free 通用状态折叠框架）
> 阅读对象：关心 Agent 循环架构收敛的开发/架构人员
> 关联文档：[Agent 主循环提取为 LLM 无关通用框架设计](./2026-08-25-Agent主循环提取为LLM无关通用框架设计.md) · [节点执行与上下文组装通用抽取方案](./节点执行与上下文组装通用抽取方案.md)

---

## 摘要

**结论先行：LLM 语境下的所谓"循环"并不是循环，而是状态折叠。** 本文把这一认识落成 `LoopFoldMethod` 的设计：一个以折叠（fold/unfold）为第一抽象的通用框架。它不创造"会干活的循环"，而提供一个"把单个状态点按外部结论逐点铺成有序状态带、并在边界截断"的宿主。

- **范式更名而非从头发明**：现有 `SnapshotDrivenLoopEngine` 机制上已是 fold，本次是语义重命名 + 补齐折叠头（FoldHead）。
- **LLM-free**：框架不强制 LLM；`IFoldOperator` 是唯一外部驱动面，LLM / 规则表 / 人审都只是算子实现。
- **信任反转**：预算、截断、收敛不是可选项，而是框架对"不可信外部算子"的强制执法层。

---

## 前言：为什么"循环"这个词是错的

我们把一路讨论的结论写成文件：**LLM 语境下的所谓"循环"并不是循环，而是一种状态折叠。**

- 传统循环 `while(cond){ body; }`：信息回写到同一个变量，条件在循环内部、可被循环代码自明地判定。它是**自走式（autopilot）**的。
- LLM"循环" `s₀ → s₁ → s₂ → … → sₙ`：每一步吸收外部输入（用户/工具/上一步产物）产出一个**新状态**，状态只增不改回地向后展开，长度由外部决定。它更像**积分把点展开成一条带**，是**外部驱动（externally driven）**的。

本文档基于这一认识，将"循环框架"彻底重建为**以折叠（fold/unfold）为第一抽象**的通用框架 `LoopFoldMethod`。它不创造一个"会干活的循环"，而提供一个"把单个状态点按外部结论逐点铺成有序状态带、并在边界截断"的宿主。

---

## 一、第一原则

### P1 · 状态折叠，而非循环

循环的本质是信息回流；折叠的本质是**展开成带**。`LoopFoldMethod` 只做一件事：

> **读当前状态点 → 交给外部算子展开出下一个状态点 → 把点追加到状态带（Trace）上 → 判断是否截断 → 继续或收敛。**

它刻意不使用"循环变量/迭代/回流"的心智模型，而用"带（只增）+ 坐标（索引）+ 截断（边界）"的模型。

### P2 · 外部驱动

折叠器是**借出控制权的宿主**，不是自走引擎：

- 每个展开步"内容与方向"由外部算子 `FoldOperator` 决定（LLM / 规则表 / 人审都只是算子实现）。
- 折叠器只保留三件事：**记账**（坐标、长度）、**借出**（把控制权交给外部算子）、**执法**（截断、预算、收敛）。

### P3 · 信任反转 → 安全是框架的执法层

外部算子不可信、不会自觉终止。因此**预算、截断、收敛检测不是可选项，而是折叠器对"不可信外部算子"的强制执法层**。这正是传统循环（信任自身 body）与折叠模型（不信任外部算子）最根本的差异。

---

## 二、核心概念

| 概念 | 含义 | 对应"循环"措辞 |
|------|------|----------------|
| `FoldState` | 单个状态点（不可变，只生成新点） | 整体快照 |
| `FoldVertex` | 状态带上的一个点（时间/坐标 + 该点状态 + 该步算子结论） | 单轮产物 |
| `FoldTrace` | 只增状态带（质点展开的轨迹），框架的既成历史 | `Rounds` 列表 |
| `FoldOperator` | 外部算子：`FoldState + FoldContext → (FoldState, FoldVertex)` | `executeStep` + 导演 |
| `FoldHead` | 折叠点：把已展开前缀收敛成"当前等效判定点" | 最终状态 |
| `CutoffPolicy` | 截断策略：预算/轮次/收敛/取消 → `FoldCut` | 终止谓词 |
| `FoldResult` | 折叠结论：`FoldHead + FoldTrace + DataID + FoldCut` | 循环运行结果 |

一句话：**`FoldTrace`** 是演化，**`FoldOperator`** 是演化算子，**`CutoffPolicy`** 是演化边界，**`FoldHead`** 是折叠到此刻的判定点，**`FoldResult`** 是整条带的结论。

---

## 三、折叠语义（unfold）与第一次"点→带"实现

### 3.1 点怎么展开成带

```
s₀ ─ op₁ ─▶ s₁ ─ op₂ ─▶ s₂ ─ … ─▶ sₙ
│            │            │
v₁          v₂          vₙ       ← 每步同时产出一个顶点（含该步算子结论）
└──────────────────────────────▶ FoldTrace = [s₀, s₁, …, sₙ]（只增）
```

折叠器每次迭代：读当前点 → 若 `CutoffPolicy` 判定截断则结束 → 否则 `op(state, ctx)` 得到 `(nextState, vertex)` → 追加 vertex → 以 `nextState` 进入下一轮。**没有任何信息回写覆盖已有点**——轨迹是纯只增带。

### 3.2 与"循环"同一引擎的对应（迁移锚点）

现有 `SnapshotDrivenLoopEngine` 从机制上已经是 fold（`initialState → while → execute → next → 新 state`，`Rounds` 只增），但被"循环/轮次"措辞框住了。`LoopFoldMethod` 保留其**语义正确的部分**，改用折叠命名表达，并补上折叠真正需要的**折叠头（FoldHead）**——这是旧引擎缺失的：旧引擎只回"最终状态"，不把带收敛成"当前判定点"。

---

## 四、框架组件与契约（`TState` 泛型 · LLM-free）

```
Operation/LoopFoldMethod
├─ Fold/        FoldEngine<TState>（unfold 宿主，唯一控制循环推进处）
│               FoldResult<TState>；FoldVertex<TVertex>；FoldTrace<TState>
├─ Operator/    IFoldOperator<TState, TContext, TNextState, TVertex>（外部算子端口，LLM/规则/人审皆实现）
│               FoldContext（坐标 + 预算 + 前置顶点）
├─ FoldHead/    IFoldHeadProjector<TState>（前缀收敛 → 当前判定点）
├─ Cutoff/      ICutoffPolicy<TState>（预算/轮次/取消/收敛 → FoldCut）
├─ Select/      FoldDecision（词表+终决，确定性，LLM 不参与）
└─ Attempt/     IFoldObserver<TState>（观测，非必需）
```

### 契约原型

```text
interface IFoldOperator<in TState, in TContext, out TNextState, out TVertex> {
    (TNextState next, TVertex vertex) Expand(TState state, TContext ctx, CancellationToken ct);
}

interface IFoldHeadProjector<in TState> {
    TState Head(IReadOnlyList<TState> trace);     // 折叠前缀 → 当前判定点
}

interface ICutoffPolicy<in TState> {
    FoldCut Decide(TState head, int coordinate, FoldBudget budget);
    // FoldCut ∈ { Continue, Success, BudgetExhausted, TripTerminated, Cancelled }
}
```

`LoopFoldMethod` 不认识 `LLM`、不认识 `Cline/Answer`、不认识 `AgentTaskItem`。`LLM` 只是 `IFoldOperator` 的一种实现。

---

## 五、生命周期：展开 → 截断 → 折叠 → 收敛

1. **展开（unfold）**：`FoldEngine` 以 `s₀` 起，每轮把控制权交给外部 `IFoldOperator.Expand`，产出 `(next, vertex)`，追加 vertex 到 `FoldTrace`，更新当前点。
2. **截断（cutoff）**：每轮循环头 `ICutoffPolicy.Decide` 判定 `Continue / 终止值`——预算/轮次/取消/收敛任一路由到截断即停。折叠器对不可信外部算子的执法在此。
3. **折叠（fold）**：游程结束（或需判定时）由 `IFoldHeadProjector.Head(trace)` 把前缀收敛成"当前等效判定点"。
4. **收敛（conclusion）**：封装为 `FoldResult{ Head, Trace, Coordinate, TotalDuration, Cut }`。

折叠器本身不含任何 LLM 决策；它只在"展开交给算子、截断交给策略、折叠交给投影器"三个端口出让控制权。

---

## 六、数据流

```
初始 point s₀
   │
   ▼
FoldEngine ──(每轮)→ context(coordinate,budget)
   │
   ▼
IFoldOperator.Expand ──► (next, vertex) ──► trace.Append(vertex) ; 当前点 = next
   │
   ▼
ICutoffPolicy.Decide(head, coord, budget) ──► Continue? ──yes→ 下一轮
   │                                                │no
   ▼                                                ▼
IFoldHeadProjector.Head(trace)                 FoldResult（收敛）
```

---

## 七、错误处理（对不可信外部算子的执法）

| 故障型 | 折叠器的职责 |
|--------|--------------|
| 算子无限膨胀（失控） | `ICutoffPolicy` 预算/轮次截断（框架强制，不信任算子自停） |
| 算子抛异常 | 按坐标折叠（FoldVertex + 错误分类）入带并继续/截断；`FoldResult` 携带 `DataID` 与异常态 |
| 算子返回空/null | 折叠器的空值守卫（外部边界校验） |
| 取消 | `CancellationToken` → `FoldCut.Cancelled` |

信任反转原则下，**这些不是"错误处理加把锁"，而是框架对非可信驱动源的正当执法**。

---

## 八、测试策略（LLM-free 的关键收益）

- 用**确定性假算子**（返回固定展开序列）测 unfold 全分支：截断 / 预算熔断 / 取消 / 收敛。
- 用 `ICutoffPolicy` 假实现测"算子失控时折叠器必截断"（信任反转的执法断言）。
- 用 `FoldHeadProjector` 假实现测"前缀 → 判定点"的折叠正确性。
- 全部测试零 AI 额度、可在纯 CI 跑。

---

## 九、边界与风险（诚实声明）

1. **并非从零发明**：现有 `SnapshotDrivenLoopEngine` 机制已是 fold，本次是**语义重命名 + 补齐折叠头（FoldHead）**，不是从空地上另起炉灶。
2. **LLM-free 的确切含义**：指框架不强制 LLM（规则/人可实现 `IFoldOperator`），非不提供 LLM 能力（LLM 也只是一个算子）。
3. **本框架只解耦"展开调度"**：算子内部若含 LLM 属算子自身行为，不在折叠器范围（节点执行的 executor-agnostic 属另一主题）。
4. **克制**：不为假设的第三个消费者过度抽象；一个折叠骨架 + 一个既有零 LLM 消费者（如 `RuleTaskExecutor` 的折叠化）已足以支撑主张。

---

## 十、LoopFoldMethod 的定位（与既有框架分工）

| 层 / 模块 | 回答的问题 |
|-----------|------------|
| `Operation/LoopFoldMethod` | **状态折叠**：把点展开成带、在边界截断、折叠成头（LLM-free） |
| `Operation/HierarchyMethod` | **容器**：数据/关系/历史存哪、怎么归（零 LLM） |
| `IFoldOperator`（LLM/规则/人） | **唯一外部驱动面**：这一步把当前点展开成下一个点（LLM 只是其一） |
| `Agent.Core` | **装配组合根**：把折叠引擎 + 外部算子 + Agent 节点执行粘在一起 |

清晰的分工本身就是"外部驱动"最好的证明：替换外部算子的实现，`LoopFoldMethod` 与 `Agent.Core` 的非 LLM 部分纹丝不动。

---

## 结语

`LoopFoldMethod` 是一次**范式更名而非从头发明**：确认"LLM 循环"本质是**外部驱动的状态折叠**，把散落在旧"循环"措辞下的机制（unfold / 截断 / 收敛）归拢为一个以折叠为第一抽象的通用框架。它是**借出控制权的宿主**，LLM 是它上面一个可拔可插的算子；它把每个状态点像积分一样铺成一条有序的带，并在边界处折叠收敛——这就是它名字的来历：**循环 · 折叠 · 方法**。
