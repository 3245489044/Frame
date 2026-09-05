# `clinerules/axioms.md` 逻辑分析报告

> 生成时间：2026-07-26
>
> 分析方法：逐条检查公理的形式化表述（量词、类型、推导关系、自洽性、冗余度）
>
> 分析范围：全部 708 行，包含基底元公理 [BAX] × 13、涌现元公理 [EAX] × 13、领域公理 [AX] × 18、推论 [TH] × 7、设计理由 [DR] × 18、交叉引用表

---

## 目录

1. [重复逻辑（Semantic Duplication）](#1-重复逻辑semantic-duplication)
2. [冗余逻辑（Verbose / Unnecessary Content）](#2-冗余逻辑verbose--unnecessary-content)
3. [错误逻辑（Logical Errors）](#3-错误逻辑logical-errors)
4. [严重等级汇总](#4-严重等级汇总)

---

## 1. 重复逻辑（Semantic Duplication）

共 **3 处**。指同一语义在不同位置以不同形式重复表述。

### [DUP-1] 公理本体标注 × 节说明 × 交叉引用表——三重冗余

**影响范围**：几乎所有领域公理（[AX]），以 StateSnapshot 为例

**冗余结构**：
```
1. 节说明（L356-358）：§一公理依赖 S×V（确定性涌现）
2. 公理本体标注（L363）：基底溯源：TimeSnapshot [BAX] + DeterminismEmergence [EAX]
3. 交叉引用表（L696）：T → StateSnapshot
4. 交叉引用表（L704）：S×V → StateSnapshot
```

**问题**：同一信息（StateSnapshot 的基底来源）出现在 4 个位置。维护时需要同步更新 4 处，极易产生不一致。

**建议**：保留公理本体标注作为单一真相源，删除节说明中的重复描述和交叉引用表中的单行映射。

---

### [DUP-2] ResultComposition [AX] 与 [TH-3] 内容完全重复

- **ResultComposition [AX]（L569-578）**：定义 `meltdown > overflow > success` 优先级和判定规则
- **[TH-3]（L607-615）**：用 if-else 分支重写完全相同的逻辑

**问题**：推论应*推导出公理未显式声明的新知识*，但 [TH-3] 只是把公理换了一种控制流写法。`meltdown > overflow > success` 已经在公理中明确声明，推论没有添加任何新信息。

**建议**：删除 [TH-3]，或将 [TH-3] 改为真正的推论（例如推导出"熔断决策传递到父轮次"等非平凡结果）。

---

### [DUP-3] TerminationPriority 条件与 TerminalSuccessCondition 重叠

- **TerminationPriority（L531-535）**："前置条件短路"块包含 `forceHalt=false ∧ Evaluate=pass → terminate_success`
- **TerminalSuccessCondition（L550-553）**：再次声明 `R.terminal = terminate_success ↔ (¬forceHalt ∧ Evaluate=pass)`

**问题**：两处完全等价的语义。

**建议**：删除 TerminalSuccessCondition，合并到 TerminationPriority 作为一个声明。

---

## 2. 冗余逻辑（Verbose / Unnecessary Content）

共 **5 处**。指不违反逻辑正确性但增加无谓复杂度。

### [VER-1] 符号约定表大量符号从未使用或仅用一次

**L54-86** 定义了 33 个符号，其中以下符号在正文中从未出现或仅出现一次：

| 符号 | 出现次数 | 说明 |
|------|----------|------|
| `pattern(s,t₁,t₂)` | 0 | 完全未使用 |
| `invariant(op)` | 0 | 完全未使用 |
| `precedes(D₁,D₂)` | 0 | 完全未使用 |
| `agent.selects(O)` | 0 | 完全未使用（该语义在 DecisionAutonomy 中直接用文字表述） |
| `@{t}` | 0 | 约定为 `@{t}` 但全文使用 `@t` |
| `a∥b` | 0 | 约定为 `a∥b` 但全文使用 `op₁∥op₂` |
| `extract(r)` | 1 | 仅在 ParseErrorStatus 中使用 |
| `errorCost(c)` | 1 | 仅在 TransientRetryNoCost 中使用 |

**建议**：删除未使用符号，内联仅用一次的符号。

---

### [VER-2] 基底溯源的三级冗余

**结构**：
```
级别 A（节开头）: "§一公理主要依赖 S×V（确定性涌现）"          →  L356-358
级别 B（每条公理）: "基底溯源：TimeSnapshot [BAX] + ..."       →  L363
级别 C（交叉引用表）: "T → StateSnapshot" + "S×V → StateSnapshot"  →  L696 & L704
```

**问题**：三个层级覆盖的都是"公理→基底"的映射关系。交叉引用表（L687-708）的每一行都可以从公理本体的 `基底溯源` 标注自动生成。

**建议**：仅保留级别 B（公理本体的基底溯源标注），删除级别 A 和级别 C，或将交叉引用表改为自动生成的元数据文件。

---

### [VER-3] ResultStructure 声明了四个字段但只使用一个

**L563-567**：
```
result = {terminal, quality, metadata, output}
```

ResultComposition（L569-578）使用的字段只有 `status`，而 `terminal`、`quality`、`metadata`、`output` 四个字段从未被任何公理引用。

**建议**：移除未使用的字段声明，或补充它们在其他公理中的使用。

---

### [VER-4] [DR:SP-1] 与 [DR:SP-2] 设计理由高度重叠

- **[DR:SP-1] SpacePartition**（L664）："域划分是系统可分析性的前提——并发安全、作用域、层级结构的基础"
- **[DR:SP-2] SpaceIsolation**（L666-667）："域→并发的推理路径为所有并发优化提供公理基础"

**问题**：核心信息（域划分是并发安全的基础）在两个理由中重复。

**建议**：合并为一个设计理由。

---

### [VER-5] 时间索引符号不一致

- **符号约定（L43）**：`@{t}` — 时间索引
- **正文实际使用**：全文使用 `@t`，如 `state@t₁`、`observed(entity.state@t)`

**问题**：约定的符号与使用的符号不一致，约定形同虚设。

**建议**：将符号约定改为 `@t` 或统一为 `@{t}`。

---

## 3. 错误逻辑（Logical Errors）

共 **10 处**。指违反逻辑规则（量词、类型、推导有效性、自洽性）的公理表述。

---

### [ERR-1] TimeSequence [BAX] — 循环定义 / 恒真式 ★★★★★

**位置**：L184-186

**原文**：
```
∀s₁,s₂∈SystemState, ∀t₁<t₂ : (state@t₁→state@t₂)∈ProcessSpace ↔ ProcessSpace.valid((s₁,s₂),t₁,t₂)
```

**问题**：

`ProcessSpace` 同时出现在等号两侧公理欲定义的正是 ProcessSpace，但检查合法性的谓词 `ProcessSpace.valid` 又依赖于 ProcessSpace 自身。这是一个循环定义。

从逻辑结果看，这个式子对**任意** (s₁,s₂,t₁,t₂) 组合都自动成立：等号左边断言"转移属于 ProcessSpace"，右边断言"ProcessSpace 判定它合法"——两者在任一一致的语义下恒等，不能施加任何约束。

**严重性**：严重。TimeSequence 是五个时间基底公理之一，循环定义使时间基底公理集丧失约束力。

**修复方案**：将 ProcessSpace 定义为所有可能的合法状态转移的集合，而非通过自指来定义：
```
ProcessSpace = {(state₁@t₁, state₂@t₂) | t₁<t₂ ∧ ∃operation∈Operations : state₁→state₂ via operation}
```
然后用 TimeSequence 约束 ProcessSpace 与系统状态转移的关系（不是循环定义）。

---

### [ERR-2] SpaceBoundary [BAX] 域归属规则 — 量词范围错误 ★★★★★

**位置**：L137-138

**原文**：
```
∀op∈Operations, ∀d₁,d₂∈Domains : op.crosses(d₁) ∧ op.originDomain=d₁
  → op.targetDomain=d₂ ∧ d₁≠d₂
```

**问题**：`∀d₁,d₂∈Domains` 是全称量词。这意味着：

- "对**所有** d₂∈Domains，op 的目标域都是 d₂"
- "且对**所有** d₂，d₂ ≠ d₁"

这在集合大小 > 1 时必然为假——一个操作不可能同时拥有所有域作为目标域。

正确逻辑：存在唯一的目标域，`∃!d₂∈Domains : op.targetDomain=d₂ ∧ d₁≠d₂`

**严重性**：严重。量词错误导致该约束式恒假，SpaceBoundary 的公理效力彻底丧失。

**修复方案**：
```
∀op∈Operations, ∀d₁∈Domains : op.crosses(d₁) ∧ op.originDomain=d₁
  → ∃!d₂∈Domains : op.targetDomain=d₂ ∧ d₁≠d₂
```

---

### [ERR-3] RandomnessPrimordial [BAX] — 单向蕴含 vs 双向语义 ★★★★★

**位置**：L195-197

**原文**：
```
∀entity∈Entities, ∀t∈Time : observed(entity.state@t) → locked(entity.state@t)
```

**注释**（L198）：
> 实体在时刻 t 的状态当且仅当被观测时才被锁定。未观测时状态是自由的。

**问题**：公理是单向蕴含（`observed → locked`），但注释是双向（`observed ↔ locked`）。公理只表达了"若观测则锁定"，未表达"若未观测则自由"——后者是 FirstCallTransient 和 ToolDeterminism 的真正基底。

**严重性**：严重。缺失的方向是"变化自由度"的核心语义——没有 `¬observed → ¬locked`，就无法推导出 FirstCallTransient。

**修复方案**：改为双向蕴含：
```
∀entity∈Entities, ∀t∈Time : observed(entity.state@t) ↔ locked(entity.state@t)
```
或明确拆分：
```
∀entity∈Entities, ∀t∈Time : observed(entity.state@t) → locked(entity.state@t)    (观测锁定)
∀entity∈Entities, ∀t∈Time : ¬observed(entity.state@t) → ¬locked(entity.state@t)  (未观测自由)
```

---

### [ERR-4] [TH-6] 认知锁定定律 — 推导不成立 ★★★★

**位置**：L637-641

**原文**：
```
∀p∈Perception, ∀model∈LLM : p ∉ model.knowledge → model.canLock(p) ∧ model.canObserve(p)
```

**源公理**：ObservationLocking [EAX] + EpistemicLimit [EAX]

**分析**：从 EpistemicLimit [EAX] 可知：
```
model.knowledge ⊆ (model.trainingData ∩ model.contextWindow)
```
如果 `p ∉ model.knowledge`，可能有三种情况：
1. p ∈ trainingData ∩ contextWindow 但未被模型正确编码（模型内部表示问题）
2. p ∉ trainingData（超出训练数据）
3. p ∉ contextWindow（超出上下文窗口）

在情况 2 和 3 中，模型**不能**认知锁定 p（因为 p 在认知范围之外）。因此结论 `model.canLock(p)` 不能无条件成立。

**严重性**：严重。推论依赖的源公理不足以支持结论。

**修复方案**：添加前提条件 `p ∈ model.trainingData ∩ model.contextWindow`：
```
∀p∈Perception, ∀model∈LLM : p ∉ model.knowledge ∧ p∈(trainingData∩contextWindow) → canLock(p)
```

---

### [ERR-5] ObservationLocking [EAX] — 类型错误 ★★★

**位置**：L287-289

**原文**：
```
∀domain∈Domains, ∀entity∈domain : observed(entity) → entity.state @ {t | entity ∈ domain} = locked(entity.state)
```

**问题**：`∀entity∈domain` 中，entity 是实体类型（Entities），domain 是域类型（Domains = 状态空间的子集）。实体不能"属于"一个状态域。应使用 `entity.active(domain)`（已在 SpaceScope [BAX] 中定义）或类似谓词。

此外，`entity ∈ domain` 的表达混淆了"实体属于域"和"状态在域中"两个不同概念。

**严重性**：中等。语义意图清晰，但形式化表述的类型不匹配会阻碍形式验证。

**修复方案**：
```
∀domain∈Domains, ∀entity∈Entities : entity.active(domain) → observed(entity.state, domain)
  → entity.state.locked(domain)
```

---

### [ERR-6] StabilityThreshold [EAX] — 自由变量 ★★★

**位置**：L298-301

**原文**：
```
∀k∈CallKey : callCount(k) ≥ threshold → stable(k)
```

**问题**：`threshold` 是一个自由变量——它未被量化，也未声明为全局常量。在经典一阶逻辑中，自由变量在公理中是隐式全称量化的（等价于 `∀threshold ∈ ?`），但 `threshold` 的类型和取值范围未定义。

**严重性**：中等。语义上可理解为"存在某个阈值"，但形式化不完整。

**修复方案**：
```
∃threshold∈ℕ, ∀k∈CallKey : callCount(k) ≥ threshold → stable(k)
```

---

### [ERR-7] 逻辑基础 — 自指循环 ★★★

**位置**：L30-31、L46

**原文**：
> "当 T×S 变换组合作用于命题域时，就涌现为逻辑"（L30）
> "其合法性由 TransformationComposition [EAX] 保证"（L46）

**问题**：逻辑是公理体系的元语言（L18-19）。如果逻辑的合法性需要由体系内部的 [EAX] 来"保证"，就构成自指循环——用体系内的公理来证明体系自身的推理工具是合法的。

**严重性**：中等。作为一个实践中的设计文档，这种循环可以容忍（类似于哥德尔不完备定理所揭示的任何形式系统都无法自证一致性）。但严格来说，应将逻辑表述为"元层次约定"而非"由体系内公理保证"。

**修复方案**：删除 L46 的"其合法性由 TransformationComposition [EAX] 保证"，改为：
> "逻辑是公理体系的元约定，其有效性由使用者认可而非体系内公理保证。"

---

### [ERR-8] ResultStructure [AX] — 符号歧义 ★★

**位置**：L563-567

**原文**：
```
result = {terminal, quality, metadata, output}
```

**问题**：`{...}` 在本文中是集合表示法（见 SpacePartition 的 `{domain₁,...,domainₙ}`）。但这里 `=` 右侧是记录结构而非集合，且 ResultComposition 使用 `R.result.status` 暗示 `result` 是记录/对象而非集合。集合与记录的符号混用。

**严重性**：低。语义可推断但不精确。

**修复方案**：
```
result = ⟨terminal:..., quality:..., metadata:..., output:...⟩
```

---

### [ERR-9] SpaceHierarchy [BAX] — 蕴含冗余 ★★

**位置**：L118-121

**原文**：
```
∀space∈Spaces, ∀layer∈LAYERS, ∀sub∈subspaces(space) : space.layer = layer
  → sub.layer = layer ∧ sub.space ⊑ space.space
```

**问题**：`sub ∈ subspaces(space)` 已隐含 `sub.space ⊑ space.space`，后半部分结论是子空间关系的重述。这不算严格错误，但降低了公理的简洁性。

**严重性**：低。

**修复方案**：
```
space.layer = layer → sub.layer = layer
```

---

### [ERR-10] TimeContinuity [BAX] — 未中断条件不足以保证延续 ★★

**位置**：L175-177

**原文**：
```
∀R∈Rounds : ¬R.interrupted → R.result ∈ Continuation(R.next)
```

**问题**：`Continuation(R.next)` 定义为"轮次 R 完成后可能进入的下一个状态集合"。但 "未被中断" 不一定保证结果在下一轮次的延续空间中——如果轮次结果无效（例如不满足 EntryCondition），即使未中断也无法进入下一轮。

**严重性**：低。语义基本正确，缺少对轮次结果有效性的前提条件。

**修复方案**：
```
∀R∈Rounds : ¬R.interrupted ∧ R.result.valid → R.result ∈ Continuation(R.next)
```

---

## 4. 严重等级汇总

| 等级 | 数量 | 条目 |
|------|------|------|
| ★★★★★ 严重 | 3 | [ERR-1] TimeSequence 循环定义, [ERR-2] SpaceBoundary 量词错误, [ERR-3] RandomnessPrimordial 语义不匹配 |
| ★★★★ 高 | 1 | [ERR-4] [TH-6] 推导不成立 |
| ★★★ 中 | 3 | [ERR-5] ObservationLocking 类型错误, [ERR-6] StabilityThreshold 自由变量, [ERR-7] 逻辑基础自指循环 |
| ★★ 低 | 3 | [ERR-8] ResultStructure 符号歧义, [ERR-9] SpaceHierarchy 蕴含冗余, [ERR-10] TimeContinuity 条件不足 |
| — 重复 | 3 | [DUP-1] 三重冗余标注, [DUP-2] [TH-3] 与公理重复, [DUP-3] 终止条件重复 |
| — 冗余 | 5 | [VER-1] 未使用符号, [VER-2] 三级溯源冗余, [VER-3] 未用字段, [VER-4] DR重叠, [VER-5] 符号不一致 |

**总计：严重逻辑错误 4 处（P0）、中等错误 3 处（P1）、低错误 3 处（P2）、重复 3 处、冗余 5 处**。