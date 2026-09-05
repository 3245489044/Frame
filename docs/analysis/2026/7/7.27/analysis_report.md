# clinerules 逻辑分析报告

> **分析范围**：clinerules/ 目录下全部 8 个文件（axioms.md, derivation.md, dsl.md, engine.md, project.md, rules.md, spec.md, theorems.md）
> **总行数**：1,956 行
> **分析日期**：2026-07-27 | **最后更新**：2026-07-27（新增 E8-E12）
> **严重性分级**：🔴 高（影响正确性）、🟡 中（影响可维护性）、🟢 低（影响一致性）

---

## 目录

- [一、重复逻辑](#一重复逻辑)
- [二、冗余逻辑](#二冗余逻辑)
- [三、错误逻辑](#三错误逻辑)
- [四、汇总与优先级建议](#四汇总与优先级建议)

---

## 一、重复逻辑

### D1 🔴 derivation.md 与 engine.md 状态变量派生列完全重合

**位置**：`derivation.md §二`（17 行） + `engine.md §3.1`「派生」列

**表现**：
- derivation.md §二 定义每个状态变量的"源公理"和"派生路径"
- engine.md §3.1 状态变量表的末列"派生"标注 `§二:operationLog`、`§二:retryCounter` 等
- **实际上 engine.md 的"派生"列仅是一个引用标签**，指向 derivation.md——这不构成重复

**严重性重新评估**：🟢 低
- 这是**引用（引用目标）** 模式，职责分离清晰
- 但 engine.md 若直接删除"派生"列并将引用改为 `[CR: derivation.md §二]` 会更简洁

**建议**：删除 engine.md §3.1 的"派生"列，表头添加一条 `@see [CR: derivation.md §二]` 引用

---

### D2 🟢 engine.md §4.1 与 axioms.md 的决策输出集

**位置**：`engine.md §4.1` vs `axioms.md`:[DecisionOutputSet [AX] + TerminationPriority [AX]]

**表现**：
- engine.md 第 162 行明确标注 "权威来源：axioms.md"、"此处仅补充动作映射摘要"
- 但实际内容仍完整重述了 `{TS,TO,TM,switch_strategy,continue}` 五个决策值
- 动作映射摘要只有 5 条简短说明（第 165-167 行）

**建议**：将 engine.md §4.1 精简为：
```markdown
### 4.1 决策输出 → 动作映射
决策输出枚举 `{TS,TO,TM,switch_strategy,continue}` 以 axioms.md 为权威源。
- TS → 正常完成 / TO → halt / TM → forcehalt
- switch_strategy → 切换策略 / continue → 进入 Adjust
详细定义见 `[CR: axioms.md:DecisionOutputSet [AX]]` 和 `[CR: axioms.md:TerminationPriority [AX]]`。
```

---

### D3 🟢 层级定义的三个视角重复

**位置**：`axioms.md:[AxiomHierarchyStructure]` + `engine.md §1.1` + `spec.md §1.1`

**表现**：
- axiomas.md 是权威源，定义 L0-L3 的不可变性约束
- engine.md §1.1 从"运行时焦点"视角定义每层的策略
- spec.md §1.1 从"代码生成"视角定义每层的实现机制
- spec.md 第 19 行明确标注"三层视角正交互补"

**结论**：这是**刻意的视角分离**，不算重复。但需确认是否所有 LAYER_ORDER 变更都能同步更新三处。
**建议**：在三处文件头部统一添加标注，例如 `@synced-from: axioms.md:AxiomHierarchyStructure`，变更时用 grep 定位。

---

## 二、冗余逻辑

### R1 🔴 derivation.md 整体存在必要性疑问

**位置**：`derivation.md` 全文（152 行）

**分析**：
- §一（领域规则派生）：13 行映射，均为"公理 A → 规则 R"的直线映射
- §二（状态变量派生）：17 行映射，同样为直线映射
- §三-§五（策略/规范/流程派生）：同样为直线映射
- §七（证明义务表）：14 行，但自身声明"以 axioms.md 行内 dr 为准；不替代。。。"——自我否定

**关键问题**：
1. 每条映射都是 `源公理 → 目标规则` 的单一关系，无复合推导链
2. 目标规则（在 rules.md/engine.md 中）的 meta 块已标注 `[CR: derivation.md]`——反过来引用 derivation.md
3. 证明义务表（§七）的信息在 axioms.md 的 `---meta dr` 字段中已有完整记录
4. 所有派生信息在源公理的 `---meta src` 和目标规则的 `---meta ref` 中已双向覆盖

**建议**：
- 方案 A（推荐）：**删除 derivation.md**，将每条派生路径的追溯信息压缩到：
  - 源公理的 `---meta` 块中添加 `derives:{目标规则列表}`
  - 目标规则的 `---meta` 块中添加 `derived-from:{源公理列表}`
- 方案 B（保守）：将 derivation.md 降级为 `derivation-archive.md`，仅保留 §七 证明义务表

---

### R2 🟡 derivation.md §七 证明义务表冗余

**位置**：`derivation.md §七`（14 行表格）

**表现**：自身声明"权威源声明：每条公理的最新缺陷状态以 axioms.md 对应公理的 `---meta dr` 字段为准"——既然以 axioms.md 为准，此表即为冗余副本。

**建议**：删除 §七 表格，在 §七 标题下仅保留一行：
```markdown
> 证明义务的权威记录见各公理的 `---meta dr` 字段。`[CR: axioms.md]`
```

---

### R3 🟢 theorems.md 编号不连续

**位置**：`theorems.md`（99 行，6 条定理）

**表现**：仅有 TH-2、TH-5、TH-7、TH-9、TH-10、TH-11。缺失 TH-1、TH-3、TH-4、TH-6、TH-8。

**建议**：
- 选项 A：搜索 axioms.md 和 engine.md 中是否有未被 theorems.md 收录的定理，补齐缺失的 TH
- 选项 B：如确实不存在这些定理，将现有定理重新编号为 TH-1~TH-6

---

### R4 🟢 derivation.md 跳章（缺少 §六）

**位置**：`derivation.md` 章节编号

**表现**：§一→§二→§三→§四→§五→**跳至 §七**→§八。§六 缺失。

**原因追溯**：在 §五 末尾有句"引擎派生已在 engine.md 中内联定义并标注公理引用"，但未说明这就是跳过的 §六。

**建议**：在 §五 和 §七 之间插入：
```markdown
> **§六（已省略）**：引擎派生已在 engine.md §四-§八 中内联定义并标注公理引用。
```

---

### R5 🟢 rules.md §四 状态空间定义仅为骨架

**位置**：`rules.md §四`（6 行）

**表现**：自身标注"此处仅保留循环要素类型签名作为骨架"。实际上只有 3 个类型签名声明，无增量信息。

**建议**：删除 §四，将类型签名移到 engine.md §3.1 作为状态变量表的说明，或直接删除。

---

### R6 🟢 axioms.md [DR] 中冗余的历史演进注解

**位置**：`axioms.md`：
- `[DR] ExternalConsistencyWitness`（第 64-71 行）——meta 含历史演进说明（"原本为[AX-META]，但...→降级"）
- 其他多处 `---meta dr` 字段包含架构决策历史

**表现**：公理文件应记录逻辑必然性，而非项目演进历史。演进历史应放在项目 changelog。

**建议**：
- 将架构决策的演进历史（如"原本为 X→降级为 DR"）从 `---meta dr` 移出
- `dr` 字段仅保留**设计理由**（为什么这样设计），不保留**历史演变**（之前是什么）

---

### R7 🟡 axioms.md TerminationPriority meta 绑定引擎实现

**位置**：`axioms.md` 第 277 行
```
---meta dr = "R.CF实质是engine.md §5.2中consecutiveFailures(counter类型ℕ)在轮次R判定时刻的快照值;proj_cf(R)从因果前驱投影CF值" ---
```

**表现**：公理的 meta 注解将 R.CF 绑定到 engine.md 的具体实现（consecutiveFailures）。公理应独立于引擎实现。

**建议**：改写为公理内部自足的描述：
```
---meta dr = "R.CF为轮次R判定时刻的累计失败计数器标量值；proj_cf(R)从因果前驱投影CF值" ---
```

---

## 三、错误逻辑

### E1 🔴 Coupling-ObservationCollapse 类型错误

**位置**：`axioms.md` 第 218-221 行
```
[BAX-X] Coupling-ObservationCollapse:
  ∀fv∈IDI,∀obs:fv.continuousVariance≠0 | observed(fv,obs)→fv.continuousVariance|_obs=0
```

**错误**：`∀fv∈IDI`——IDI 在 axioms.md 第 117 行定义为 `IDI=observer-framework(logic-semantic-base)`，是**框架名/视角名**，不是实体类型。`[Type] Entity = 可指称实体` 是实体类型。`∀fv∈IDI` 在类型系统上不合法。

**影响**：高一——使用未定义的量化域，解析器（按 dsl.md EBNF）无法验证此公式的类型约束。

**修复**：
```diff
-   ∀fv∈IDI,∀obs:fv.continuousVariance≠0 | observed(fv,obs)→fv.continuousVariance|_obs=0
+   ∀fv:FieldValue,∀(obs:OBSERVER):fv.continuousVariance≠0 | observed(fv,obs)→fv.continuousVariance|_obs=0
```
（如 `FieldValue` 类型未定义，需在 axioms.md 「类型基底」节新增 `[Type] FieldValue = C域场变量`）

---

### E2 🟡 engine.md §5.3 EvaluateDrivenUpdate CF 指代不明确

**位置**：`engine.md` 第 293 行
```
pass:              consecutiveFailures = max(0, CF-1); CP=0; subRoundAttempts=0
```

**错误**：`CF-1` 中的 `CF` 指代不明确：
- 是 `preEvaluateCF`（第 290 行定义的快照值）？
- 还是 `consecutiveFailures`（当前值，可能已被 EvaluateDrivenUpdate 之前的逻辑修改）？

**影响**：中——实现者无法确定该用哪个值，可能导致 CF 衰减逻辑不一致。

**修复**：
```diff
-   pass:              consecutiveFailures = max(0, CF-1); CP=0; subRoundAttempts=0
+   pass:              consecutiveFailures = max(0, preEvaluateCF - 1); CP=0; subRoundAttempts=0
```
（第 295 行的 `fail/parse_error: CF++` 同样需明确化为 `consecutiveFailures++ 或 preEvaluateCF+1`）

---

### E3 🟡 spec.md Rust/Go 语言混用未说明理由

**位置**：`spec.md`：
- §2：Rust trait `ProofOracle`、`GenesisConfig`、`MetaGatekeeper`
- §3：Go struct `Sampler`
- §4：Rust enum `DegradationState`、struct `ContractRegistry`

**错误**：跨语言的接口规范混用，但未说明：
1. 是否真的要用两种语言实现不同组件？
2. 还是某一种是目标语言，另一种是伪代码？
3. 使用者应参考哪种语言的签名？

**影响**：中——实现者无法判断应使用 Rust 还是 Go 来实现 Sampler。

**修复**：
- 在所有代码块前添加语言标注和选择理由：
```markdown
> 以下接口以 Rust trait 形式给出（目标实现语言）。[或：以下接口以 Go 伪代码形式给出，用于说明概念]
```
- 或统一为一种语言（推荐 Rust，因为 §2 和 §4 都是 Rust）

---

### E4 🟢 derivation.md PO-7 废弃仍保留在表格中

**位置**：`derivation.md` 第 136 行
```
| PO-7（已废弃） | — | — | ExternalConsistencyWitness 已从 [AX-META] 降级为 [DR]... |
```

**错误**：标注 "已废弃" 但仍占表格一行，且 `affected`、`修复来源`、`落实位置` 三列均为 `—`。

**建议**：删除此行，并在表头加注：
```
> 本表不包含已废弃的证明义务。历史记录见 git log。
```

---

### E5 🟢 axioms.md InitialStateNullity 的 `L.I.State` 未定义

**位置**：`axioms.md` 第 226 行
```
[AX] InitialStateNullity: ∀x:L.I.State→x.intent∈{NO_DATA,NO_RESPONSE} | NO_DATA=∅ | NO_RESPONSE=null
```

**错误**：`L.I.State` 中的 `L.I` 未在类型系统或命名空间中定义。从上下文推测可能指 "Loop.Iteration.State"，但无定义。

**建议**：
```diff
-   ∀x:L.I.State→x.intent∈{NO_DATA,NO_RESPONSE}
+   ∀x∈State→x.intent∈{NO_DATA,NO_RESPONSE}
```
或在「类型基底」节添加：
```
[Type] L.I.State = 轮次迭代状态（L1 循环中的状态快照）
```

---

### E6 🟢 axioms.md CountableOrderBridge 中 `causal_chain_depth` 未定义

**位置**：`axioms.md` 第 197 行
```
causal_weight(r)=causal_chain_depth(r) | same_chain(R)={R'|R→*R'∨R'→*R}
```

**错误**：`causal_chain_depth(r)` 函数在 axioms.md 中无定义。虽然后文第 204 行 `[DR] CausalFallbackHeuristic` 中引用了 `causal_chain_depth`，但作为 DR（设计理由）引用不构成正式定义。

**建议**：在「定义(Def)与符号约定」节添加：
```
[Def] causal_chain_depth(r) = |{r'|r'→*r}| (因果前驱链长度)
```

---

### E7 🟢 axioms.md 类型标注格式不一致

**位置**：`axioms.md` 全局

**表现**：量化变量的类型标注有两种格式：
- `∀(x:Entity),(y:Entity)`（带括号，第 130 行）
- `∀fv∈IDI`（用 ∈，第 218 行）
- `∀k:callCount(k)∈ℕ`（用 : 无括号，第 185 行）

**影响**：低——dsl.md 的 EBNF（第 218 行 `Formula = LogicalExpression`）未严格定义类型标注语法，多种格式都算合法但不利于一致性。

**建议**：统一为 `∀(name:Type)` 格式（dsl.md §4.2 推荐格式），消除 `∈` 在类型标注中的使用。

---

### E8 🔴 axioms.md SubRoundInitialState 公式与注释矛盾

**位置**：`axioms.md` 第 258-263 行
```
[AX] SubRoundInitialState:
  ∀R∈Iteration:R.start.subRoundAttempts=0
  | //! 宽松约束：subRoundAttempts=0 仅在 R 为全新轮次时强制
  | //! 若 R 由 Adjust 重入进入，subRoundAttempts 可继承前值
```

**错误**：公理（`[AX]`）的公式体声明 `∀R∈Iteration:R.start.subRoundAttempts=0` 是全称量化、无例外的必然命题。但注释明确说明"宽松约束""仅在全新轮次时强制""重入时可继承前值"——注释**完全否定了公式体的强制力**。公理不允许例外；如果存在例外，则不应标记为 `[AX]`。

**影响**：🔴 高——逻辑公式与注释冲突，解析器按公式体理解会得到错误结论（全量归零），实现者按注释理解才是正确行为。

**修复**：
```diff
- [AX] SubRoundInitialState:
-   ∀R∈Iteration:R.start.subRoundAttempts=0
+ [AX] SubRoundInitialState:
+   ∀R∈Iteration:isFresh(R)→R.start.subRoundAttempts=0
+   | isFresh(R)↔R.preRound=null（全新轮次）
```
或在类型基底增加 `[Type] FreshRound` 后改为 `∀R∈FreshRound:R.start.subRoundAttempts=0`。

---

### E9 🟡 engine.md OnError 决策树 parse_failure 分支缺失

**位置**：`engine.md` 第 386-389 行
```
① forceHalt=true? → forcehalt | false → ①.5
①.5 result=∅ ∧ callCount(T, params)=0 → retry | 否则 → ②
```

**错误**：`①.5` 仅检查 `result=∅`，未区分 `result=parse_failure`。当 `result=parse_failure`（如引擎输出格式错误导致 extract 失败）且 `callCount=0` 时，会错误进入 `retry` 路径——但 `parse_failure` 的语义是**解析规则不匹配**而非空结果，应优先走 `Adjust_Parser`（修复提取规则）而非重试。

**影响**：🟡 中——首次调用即 parse_failure 时，引擎可能无限重试相同工具（每次都返回相同 parse_failure），直到触发 retryCounter 熔断，浪费调用。

**修复**：在 `①.5` 中增加 parse_failure 分支：
```diff
 ①.5 result=∅ ∧ callCount(T, params)=0 → retry | 否则 → ②
+   | result=parse_failure → Adjust_Parser | 否则 → ②
```

---

### E10 🟡 axioms.md 公理 `[AX]` 层级归属不明确

**位置**：`axioms.md` vs `dsl.md` vs `engine.md`

**表现**：
- `dsl.md` §2.1 定义 `[AX]` = L2 领域公理
- `axioms.md` 中大量 `[AX]` 公理（如 `FirstCallTransient`、`RepeatedCallStable`、`TransientRetryNoCost`）被 engine.md 当作 L1 运行时规则使用
- `TerminationPriority` 标记为 `[AX]`（按 dsl.md 为 L2），但其 meta 中明确引用 engine.md §5.2 的实现细节

**影响**：🟡 中——同一标记 `[AX]` 在不同文件中可能被理解为不同层级。引擎开发者可能误以为标记为 `[AX]` 的公理都应编译为 L2 领域约束（可版本化、旧版存档），但实际上 `FirstCallTransient` 等是运行时 L1 规则。

**修复**：
- 选项 A：为运行时 L1 公理添加明确标记，例如在 `[AX]` 后增加 `([L1])` 标注
- 选项 B：在 `axioms.md` 的 §二-§八 中按层级分组，每组前标注层级归属

---

### E11 🟢 axioms.md SyncProtocolTriple 与 engine.md commit_invariant 约束强度不一致

**位置**：`axioms.md` 第 141-143 行 vs `engine.md` 第 240 行

**axioms.md 定义**：
```
commit(obs₁,obs₂): obs₁发出CommitRequest → obs₂收到后确认CommitAck → obs₁收到Ack后completed
```
**engine.md 额外约束**：
```
commit_invariant: SyncProtocolTriple.CommitPhase L0 invariant: ¬∃obs:commit(obs,obs);
实现要求：任何 CommitPhase 调用前 assert(obs₁ ≠ obs₂);
```

**错误**：axioms.md 的公理定义中 `commit(obs₁,obs₂)` 的语义是一个二阶段协议过程——`obs₁→obs₂→obs₁`。这个定义**自然排除了** `obs₁=obs₂` 的情况（自己发给自己 Ack 无意义），但实际上 axioms.md 并未**显式禁止**自提交。engine.md 的 assert 是额外施加的 L0 不变式。

**影响**：🟢 低——engine.md 的 assert 在语义上是正确的，但 axioms.md 的公理定义不够严谨。

**修复**：在 axioms.md 的 SyncProtocolTriple 公理中显式添加自提交禁令：
```diff
-  | ! CommitPhase: ○(AtomicLock) | ¬∃obs:commit(obs,obs) | 逻辑时钟域原子,非物理原子
+  | ! CommitPhase: ○(AtomicLock) | ¬∃obs₁,obs₂:commit(obs₁,obs₂)∧obs₁=obs₂ | 逻辑时钟域原子,非物理原子
```

---

### E12 🟢 theorems.md TH-5 公理引用名不匹配

**位置**：`theorems.md` 第 79 行
```
| src | `{Coupling-Embedding [AX], Continuum-SamplingThreshold [AX]}`
```

**错误**：`Coupling-Embedding` 在 `axioms.md` 中的规范全名是 `[BAX-X] Coupling-Embedding(TopologicalContinuityEmbedding)`（第 209 行）。`Coupling-Embedding` 是不精确的缩略名，在自动交叉引用验证时可能匹配失败。

**影响**：🟢 低——对人类读者无影响，但若未来引入 [CR] 引用自动验证工具，缩略名会导致假阴性失败。

**修复**：对齐为 axioms.md 中的规范全名：
```diff
- | src | `{Coupling-Embedding [AX], Continuum-SamplingThreshold [AX]}`
+ | src | `{Coupling-Embedding(TopologicalContinuityEmbedding) [BAX-X], Continuum-SamplingThreshold [BAX-C]}`
```

---

## 四、汇总与优先级建议

### 按文件统计问题数

| 文件 | 问题数 |
|------|--------|
| axioms.md | 9（E1, E5, E6, E7, E8, E10, E11, R6, R7） |
| engine.md | 5（D1, D2, E2, E9, E11交叉） |
| rules.md | 1（R5） |
| spec.md | 1（E3） |
| theorems.md | 2（R3, E12） |
| project.md | 0 |
| dsl.md | 0 |
| derivation.md | 4（R1, R2, R4, E4） |

### 按严重性排序的修复优先级

| 优先级 | 问题编号 | 内容 | 建议操作 |
|--------|---------|------|---------|
| P0 🔴 | E1 | Coupling-ObservationCollapse 类型 `IDI` 未定义 | 更正为合法类型 |
| P0 🔴 | E8 | SubRoundInitialState 公式与注释矛盾 | 增加 isFresh 限定 |
| P0 🔴 | R1 | derivation.md 整体冗余 | 删除或归档 |
| P1 🟡 | E2 | EvaluateDrivenUpdate CF 指代不明确 | 明确化为 preEvaluateCF |
| P1 🟡 | E3 | spec.md 语言混用 | 统一语言或加说明 |
| P1 🟡 | E9 | OnError 决策树 parse_failure 分支缺失 | 增加 Adjust_Parser 分支 |
| P1 🟡 | E10 | 公理 `[AX]` 层级归属不明确 | 增加层级标注 |
| P1 🟡 | R2 | derivation.md §七 证明义务表冗余 | 删除 |
| P1 🟡 | R7 | TerminationPriority meta 绑定引擎实现 | 改写为自足描述 |
| P2 🟢 | E4 | PO-7 废弃仍保留 | 删除 |
| P2 🟢 | E5 | L.I.State 未定义 | 加类型定义 |
| P2 🟢 | E6 | causal_chain_depth 未定义 | 加定义 |
| P2 🟢 | E7 | 类型标注格式不一致 | 统一格式 |
| P2 🟢 | E11 | SyncProtocolTriple 与 engine.md 约束强度不一致 | 显式添加自提交禁令 |
| P2 🟢 | E12 | theorems.md TH-5 公理引用名不匹配 | 对齐规范全名 |
| P2 🟢 | R3 | theorems.md 编号不连续 | 重编号或补齐 |
| P2 🟢 | R4 | derivation.md 跳章 | 加注释 |
| P2 🟢 | R5 | rules.md §四 骨架 | 删除或引用 |
| P2 🟢 | R6 | axioms.md 历史演进注解 | 移入 changelog |
| P3 🟢 | D1 | engine.md 派生列引用 | 可优化为引用标注 |
| P3 🟢 | D2 | engine.md 决策输出重述 | 精简 |
| P3 🟢 | D3 | 层级定义三视角 | 加同步标注 |

---

> **本报告仅识别问题并提供修复方向。实际修改需逐项评估对上下游引用（如 spec.md→axioms.md 的 [CR] 引用链）的影响，建议使用 `search_files` 验证引用完整性后执行。**