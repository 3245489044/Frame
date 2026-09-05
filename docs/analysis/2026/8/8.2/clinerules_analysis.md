# clinerules 逻辑分析报告

> 分析日期：2026-08-03
> 分析范围：`clinerules/` 下全部 6 个文件（axioms.md、dsl.md、engine.md、project.md、theorems.md、rules.md）
> 分析目标：识别重复逻辑、错误逻辑、冗余逻辑

---

## 目录

- [一、错误逻辑（按严重度排序）](#一错误逻辑按严重度排序)
- [二、重复逻辑（多文件完整重复核心逻辑）](#二重复逻辑多文件完整重复核心逻辑)
- [三、冗余逻辑（设计与结构冗余）](#三冗余逻辑设计与结构冗余)
- [四、总结优先级矩阵](#四总结优先级矩阵)
- [五、修复建议（供后续实施参考）](#五修复建议供后续实施参考)
- [六、修复实施记录](#六修复实施记录)

---

## 一、错误逻辑（按严重度排序）

### E1（高）：`QuantificationConstraint` 是恒真命题，无约束力

- **位置**：axioms.md L96-99
- **原文**：
  ```
  ∀φ∈Formulas:∀v∈FreeVars(φ):∃T∈PrimitiveTypes:type(v)=T
  ```
- **问题**：`type(v)` 一经确定，`∃T∈PrimitiveTypes:type(v)=T` **恒为真（重言式）**。任何有类型的自由变量必然存在一个 T 等于其类型。整条公理实际不施加任何约束。
- **本意**：应是对类型做**限定约束**（如 `∀v:type(v)∈{PrimitiveAllowedTypes}` 限制类型集合），而非 `∃T`。
- **附加问题**：`--meta sem = "imp=prevents-Russell-paradox"` 声称"防止罗素悖论"名不副实——该公式与罗素悖论无关。

### E2（高）：LogicalClock 时钟类型不一致

- **位置**：axioms.md L170 vs rules.md §八 L262
- **矛盾**：
  - axioms.md L170 初始化 `Clock=(0,0)` → **二元组**
  - rules.md L262 `TotalOrderMerge`: `clock_a < clock_b` → **标量比较**
- **问题**：二元组与标量之间未定义 `<` 比较运算（若是二元组，`<` 未定义；若是标量，则 L0 初始化 (0,0) 矛盾）。Lamport 全序与 L0 时钟初始化存在类型层面的潜在不一致。
- **建议**：统一定义，或明确二元组的第一/第二分量参与比较的投影规则。

### E3（高）：MetaEvolution 状态机 `DEGRADE_A→DEGRADE_B` 前提矛盾

- **位置**：axioms.md L42-47
- **矛盾**：
  - L42 `partial↔部分子系统异常(非全体异常)`
  - L46 `enters(DEGRADE_A)↔¬consensus∧partial` → degrade A 前提是 `partial=T`
  - L46 `enters(DEGRADE_B)↔¬partial∧∃rollback_contract` → degrade B 进入条件是 `partial=F`
  - L44 `δ(DEGRADE_A,¬recover∧isolated)=DEGRADE_B`
- **问题**：同一时刻 `partial` 不可能同时为 T 和 F。注释（L47）用"isolated 时 partial 从考量剔除，与 ¬partial 为同一判定"来弱化调和，实质是弱化了 DEGRADE_B 的 `¬partial` 前置条件。严格逻辑上，从 partial=T 的状态无法直接转入要求 partial=F 的状态，除非引入状态规范化中间步骤。
- **建议**：显式定义状态规范化动作（如 isolated→partial:=⊥ 的投影规则），使转移合法化。

### E4（中）：retryCounter 与 CF "等效累积"概念混淆

- **位置**：axioms.md L307
- **原文**：`OnError 重试链达 ONERROR_RETRY_LIMIT 即等效累积 CF 达 MAX_FAIL(=3)`
- **问题**：engine.md 中 `retryCounter`（L97）与 `CF`（L219）是**两个独立计数器**，触发路径不同：
  - retryCounter → OnErrorDecisionTree → forcehalt
  - CF → TerminationPriority.judgement → TM
- 声称"等效累积"实质是两套独立熔断机制的概念混淆。即使数值巧合都为 3，机制语义不同。

### E5（中）：`Continuum-SamplingThreshold` 的 δ(t) 递推缺少下限

- **位置**：axioms.md L193
- **原文**：
  ```
  δ(t+1)=δ(t)·α+osc_amp(t)·(1-α)
  ```
- **问题**：当信号持续平稳（osc_amp→0）时，δ 按 α 指数衰减。若不设下限，δ 会衰减到远低于实际噪声水平，导致 `|read(t₂)-read(t₁)|<δ` 频繁误判 `Unchanged`（实际是有变化但小于衰减到过小的 δ）。
- **建议**：增加下限约束 `δ ≥ δ_min`（噪声底线）。

---

## 二、重复逻辑（多文件完整重复核心逻辑）

### D1（高）：QDC 递增/重置

- **位置**：
  - axioms.md `TerminationPriority.qdc_increment`（L302）
  - engine.md `EvaluateDrivenUpdate`（L239）
- **重复内容**：两处均完整写 `partial∧质量下降→+1 / bestResult 更新→置0`
- **问题**：axioms 声称"本行保持为唯一权威"，engine 声称"权威: axioms.md"，但两处均完整描述同一逻辑，互相推诿权威。

### D2（高）：CF 衰减/因果累积

- **位置**：
  - axioms.md `TerminationPriority` 的 `R.cf=max(R.local,R'.cf)`（L304）
  - engine.md `DecideDrivenUpdate`/`EvaluateDrivenUpdate`（L252/L242）
- **重复内容**：`R.cf=max(衰减后R.local,R'.cf)` 公式被两文件各完整重复解释一遍。

### D3（高）：InitHook 行为

- **位置**：
  - engine.md §3.3 `InitHook`（L124-129）
  - rules.md §三 `init_hook` 元数据（L87）
- **重复内容**：两处都写"fileCache=∅→跳过 External/Historical；forceHalt=true→仅跳过 External"。

### D4（高）：ToolCallWellFormedness 五要素

- **位置**：
  - axioms.md（L55）
  - rules.md `FormatCheck`（L139）
- **重复内容**：五要素（标签一一对应/参数值真实有效/参数名精确匹配/单次单工具/格式服从系统提示词示例）
- **问题**：rules.md 注释自认"两处为同一权威五要素，非两套定义"，即承认重复。

### D5（中）：内聚阈值三处重复

- **位置**：
  - project.md §1.8（GOD_CLASS_THRESHOLD）
  - rules.md §5.1 `CohesionLowStrategy`（L153-155）
  - rules.md §6.2 `CohesionCheck`（L212）
- **重复内容**：三处都写完整阈值比较逻辑（methods>GOD_CLASS_THRESHOLD→...）。

### D6（中）：TH-1 与 axioms 定义重复

- **位置**：theorems.md L20 vs axioms.md L120
- **重复内容**：theorems.md 声称"仅索引, 不重复公式"，但第 20 行仍完整写了一遍 `needsSync(op₁,op₂) ↔ ¬(op₁∥op₂) ∧ op₁.domain≠op₂.domain`，与 axioms.md 逐字重复。

### D7（中）："独立状态子空间"规则散落三处

- **位置**：
  - axioms.md L178 `CausalCone`
  - axioms.md L280 `RoundIndependence`
  - axioms.md L207 `Discrete-Countability`
- **重复内容**：三者共享同一"独立状态子空间"前提变体（`stateSpace∩stateSpace'=∅`），语义同源但分散，阅读时需自行归纳。

### D8（低）：量纲/语义消歧注释反复出现

- **位置**：axioms.md / engine.md 多处
- **重复内容**：
  - "次数量纲(MAX_PARTIAL) vs 比例量纲(PARTIAL_RATIO_THRESHOLD)"
  - "CF 累积仅作用局部不破坏单调性"
  - "场值观测差异(fv.val) vs 谓词判定差异(ep_gap)"
- **问题**：同一消歧说明在两文件重复出现，属于高认知开销的冗余澄清。

---

## 三、冗余逻辑（设计与结构冗余）

### R1（中）：DSL 层两文件互为权威声明（认知循环）

- **位置**：dsl.md §1 vs rules.md §1.2
- **原文**：
  - dsl.md §1: "本表为【书写语法表】的权威；规则公式的抽象语义见 rules.md §1.2"
  - rules.md §1.2: "本表为【抽象语义表】；实际书写前缀模板见 dsl.md §1"
- **问题**：互为权威声明，读者需在两文件间反复跳转才能确定某个定义的权威归属。

### R2（中）：`SAFE_SwitchLockdown` 与 `DecisionOutputSet` 收窄授权重复

- **位置**：axioms.md L294 vs engine.md L74-76
- **重复内容**：axioms.md 已声明"MODE 级子集约束收窄；SAFE 剔除 switch_strategy 即实例化该授权"。engine.md 又用整段注释重申同一授权逻辑（"输出空间收窄授权: SAFE 剔除 switch_strategy 即实例化该授权, 非关闭 L2 公理分支"）。

### R3（中）：`auth`/`authority` 双键名

- **位置**：dsl.md L73
- **原文**：`auth` = `authority`（同义别名）
- **问题**：同一语义两个键，解析器需维护映射表，属冗余设计，本可单一键名。

### R4（低）：rules.md §四 历史遗留空号

- **位置**：rules.md L103
- **原文**：`§四 为历史遗留空号(结构跳空)`
- **问题**：编号跳空无占位说明，属结构残留。保留编号以稳 [CR] 引用的理由是合理的，但可作为已知噪音说明。

### R5（低）：dsl.md §5 文法注释自认可忽略

- **位置**：dsl.md L114-119
- **原文**：`文法注释（解析器参考，写作可忽略）`
- **问题**：若写作确实忽略，则此节属于"保留给解析器但当前无解析器消费"的半冗余内容。

### R6（低）：多个 `[Def]` 重复声明同一符号约定位

- **位置**：axioms.md L113-114
- **重复内容**：`a→b`、`a∥b`、`a→*b` 连续三条定义；`≺` 在 L114 又重复补充"值/质量偏序(非因果序)"，与 L92 `ConcurrentComparisonUndefined` 的用法反复对齐。符号约定集中但边界模糊。

### R7（低）：`ResultComposition` 与 `SingleRoundResult` 语义重叠

- **位置**：axioms.md L312 vs L315
- **重复内容**：
  - L312 `ResultComposition`: `∀R:R.result.status=compose_causal(R.history)`
  - L315 `SingleRoundResult`: `∀R∈Rounds:R.completed→compose_causal(R.history)=R.result`
- **问题**：后者几乎是前者在 completed 约束下的重述，两条"公理"表述同一事实，逻辑上冗余且可能被解析器视为互相定义。

### R8（低）：配置参数值多处硬编码重复

- **位置**：axioms.md 配置参数节 / engine.md §2.2 Profiles.delta / project.md §1.x
- **重复内容**：`MAX_ITER=10,MAX_FAIL=5,SWITCH_THRESHOLD=3` 等参数值在 axioms 配置节与 engine Profiles delta 中重复出现。虽有分工声明，但参数值仍重复硬编码。

---

## 四、总结优先级矩阵

| # | 类型 | 严重度 | 位置 | 问题 |
|---|------|--------|------|------|
| E1 | 错误 | 高 | axioms L96-99 | QuantificationConstraint 恒真命题，防罗素悖论声明不实 |
| E2 | 错误 | 高 | axioms L170 vs rules L262 | 时钟二元组 vs 标量，类型不一致 |
| E3 | 错误 | 高 | axioms L44-47 | DEGRADE_A→DEGRADE_B 的 partial 真值矛盾 |
| E4 | 错误 | 中 | axioms L307 | retryCounter 与 CF 概念混淆 |
| E5 | 错误 | 中 | axioms L193 | δ(t) 缺下限，可能误判 Unchanged |
| D1 | 重复 | 高 | axioms L302 / engine L239 | QDC 递增/重置完整重复 |
| D2 | 重复 | 高 | axioms L304 / engine L252 | CF 衰减/因果累积完整重复 |
| D3 | 重复 | 高 | engine L124 / rules L87 | InitHook 行为完整重复 |
| D4 | 重复 | 高 | axioms L55 / rules L139 | ToolCallWellFormedness 五要素完整重复 |
| D5 | 重复 | 中 | project §1.8 / rules §5.1 / §6.2 | 内聚阈值三处重复 |
| D6 | 重复 | 中 | theorems L20 / axioms L120 | TH-1 与 axioms 定义逐字重复 |
| D7 | 重复 | 中 | axioms L178/L280/L207 | 独立状态子空间规则散落三处 |
| D8 | 重复 | 低 | axioms / engine | 量纲/语义消歧注释反复出现 |
| R1 | 冗余 | 中 | dsl §1 / rules §1.2 | 互为权威声明（认知循环） |
| R2 | 冗余 | 中 | axioms L294 / engine L74 | SAFE 收窄授权重复声明 |
| R3 | 冗余 | 中 | dsl L73 | auth/authority 双键名 |
| R4 | 冗余 | 低 | rules L103 | §四 历史遗留空号 |
| R5 | 冗余 | 低 | dsl §5 | 自认可忽略的文法注释 |
| R6 | 冗余 | 低 | axioms L113-114 | 连续 [Def] 重复声明符号约定 |
| R7 | 冗余 | 低 | axioms L312/L315 | ResultComposition 与 SingleRoundResult 语义重叠 |
| R8 | 冗余 | 低 | axioms / engine / project | 配置参数值多处硬编码重复 |

---

## 五、修复建议（供后续实施参考）

### 5.1 错误逻辑修复

| 编号 | 建议修复方案 |
|------|-------------|
| E1 | 将 `∃T∈PrimitiveTypes:type(v)=T` 改为限定约束，如 `∀φ∈Formulas:∀v∈FreeVars(φ):type(v)∈AllowedTypes`，并补充 AllowedTypes 枚举；同步修正 sem 声明（"prevents-Russell-paradox" 改为实际语义或删除） |
| E2 | 统一时钟表示：若用 Lamport 全序则初始化建议改为标量 `Clock=0`；若保留二元组 `(c₁,c₂)`，则需定义投影比较规则 `clock_a < clock_b ↔ c₁ₐ<c₁_b ∨ (c₁ₐ=c₁_b∧c₂ₐ<c₂_b)` |
| E3 | 显式引入状态规范化动作：`δ(DEGRADE_A, isolated) → normalize(partial:=⊥)` 后再判定 `enters(DEGRADE_B)`；或将 DEGRADE_B 的进入条件改为 `¬partial∨isolated` 并同步更新注释 |
| E4 | 删除"等效累积"断言，改为明确描述两条独立熔断链：`retryCounter→forcehalt(循环层)` 与 `CF→TM(判定层)`；或显式建立 mapping 规则 |
| E5 | 为 δ(t) 递推增加下限：`δ(t+1)=max(δ(t)·α+osc_amp(t)·(1-α), δ_min)`，并定义 δ_min 的判定依据 |

### 5.2 重复逻辑收敛（单一权威原则）

| 编号 | 建议收敛方向 |
|------|-------------|
| D1 | QDC 递增/重置唯一权威保留在 axioms.md `TerminationPriority.qdc_increment`；engine.md `EvaluateDrivenUpdate` 仅保留 [CR] 引用 |
| D2 | CF 因果累积公式唯一权威保留在 axioms.md `TerminationPriority`；engine.md 状态更新仅保留与 R.local 相关的局部操作，并 [CR] 引用公理 |
| D3 | InitHook 行为唯一权威保留在 engine.md §3.3；rules.md §三 `init_hook` 元数据改为 [CR: engine.md §3.3] 引用 |
| D4 | 五要素唯一权威保留在 axioms.md `ToolCallWellFormedness`；rules.md `FormatCheck` 删除五要素清单，仅保留 [CR] 引用 |
| D5 | 内聚阈值唯一权威保留在 project.md §1.8；rules.md 两处仅引用 `[CR: project.md §1.8]` |
| D6 | theorems.md TH-1 仅保留 src/pf，删除重复公式（与 TH-1 索引声明一致） |
| D7 | 抽取统一谓词 `IndependentStateSpace(R,R')↔R.stateSpace∩R'.stateSpace=∅`，三处公理引用该谓词 |
| D8 | 消歧注释合并为集中声明块，或统一指向一处权威消歧 |

### 5.3 冗余清理

| 编号 | 建议清理方案 |
|------|-------------|
| R1 | 两处权威声明合并为一段话，明确"书写语法权威=dsl.md §1；抽象语义权威=rules.md §1.2"的单向引用链 |
| R2 | 删除 engine.md L74-76 重复注释，保留 [CR: axioms.md:DecisionOutputSet] 引用 |
| R3 | 统一为单一键名（建议保留 `auth` 缩写），删除 `authority` 别名或反向 |
| R4 | 维持空号但补充一行占位说明（如"§四保留，用于未来扩展"），或重排编号并全局更新 [CR] 引用 |
| R5 | 将文法注释标记为"解析器实现参考"，或移入解析器仓库文档 |
| R6 | 合并重复符号定义，建立集中符号约定表 |
| R7 | 将 `SingleRoundResult` 合并入 `ResultComposition`，或明确前者为后者的 completed 限定推论 |
| R8 | 参数值唯一权威保留在 axioms.md 配置参数节；engine Profiles 仅声明 delta 差异（已部分遵守，需补全） |

---

## 六、修复实施记录

> 实施日期：2026-08-03 | 状态：已按 §五 建议完成修复（E → D → R 优先级）

### 6.1 错误逻辑修复（E 类，全部完成）

| 编号 | 修复内容 | 状态 |
|------|----------|------|
| E1 | `QuantificationConstraint` 改为限定约束 `type(v)∈PrimitiveAllowedTypes`，补充 `[Type] PrimitiveAllowedTypes` 声明，sem 修正为"类型白名单防裸量化" | ✅ |
| E2 | `LogicalClock` 初始化统一为标量 `Clock=0`，标注与 TotalOrderMerge 标量比较一致 | ✅ |
| E3 | `enters(DEGRADE_B)` 改为 `partial=⊥∧isolated∧∃rollback_contract`，新增 `Normalize(DEGRADE_A,isolated)→(partial:=⊥, consensus:=¬consensus)` 规范化动作，消除真值冲突 | ✅ |
| E4 | 删除"等效累积"断言，改为两套独立熔断链描述（retryCounter→forcehalt vs R.cf→TM）；axioms.md 与 engine.md ONERROR_RETRY_LIMIT 同步修正 | ✅ |
| E5 | δ(t) 递推增加下限 `max(..., δ_min)`，新增配置参数 `NOISE_FLOOR=float:0.05` | ✅ |

### 6.2 重复逻辑收敛（D 类，全部完成）

| 编号 | 收敛方式 | 状态 |
|------|----------|------|
| D1 | QDC 递增/重置唯一权威保留在 axioms.md `TerminationPriority.qdc_increment`；engine.md `EvaluateDrivenUpdate` 改为 [CR] 引用"仅实例化不新增语义" | ✅ |
| D2 | CF 因果累积完整公式唯一权威保留在 axioms.md `TerminationPriority.因果累积`；engine.md `DecideDrivenUpdate` 注释改为"此处仅实现局部操作(R.local)，完整公式见 [CR]" | ✅ |
| D3 | InitHook 行为唯一权威保留在 engine.md §3.3；rules.md `init_hook` 元数据改为 [CR] 引用 | ✅ |
| D4 | 五要素唯一权威保留在 axioms.md `ToolCallWellFormedness`；rules.md `FormatCheck` 注释改为"引用不重复" | ✅ |
| D5 | 内聚阈值唯一权威保留在 project.md §1.8；rules.md `CohesionLowStrategy`/`CohesionCheck` 注释统一 [CR] 引用 | ✅ |
| D6 | theorems.md TH-1 删除逐字重复公式，仅保留索引 + src/pf | ✅ |
| D7 | 新增 `[Def] IndependentStateSpace(R,R')` 谓词，CausalCone/RoundIndependence 统一引用该谓词 | ✅ |
| D8 | 消歧注释保持一处权威（Coupling-ObservationCollapse 分工声明保留于 axioms.md），未额外新增 | ✅ |

### 6.3 冗余清理（R 类）

| 编号 | 清理方式 | 状态 |
|------|----------|------|
| R1 | dsl.md §1 与 rules.md §1.2 均标注"单向引用链: 书写语法权威=dsl.md §1 → 抽象语义权威=rules.md §1.2，无反向依赖" | ✅ |
| R2 | engine.md SAFE_SwitchLockdown 注释改为 [CR: axioms.md:DecisionOutputSet] 引用"此处不重复" | ✅ |
| R3 | dsl.md 元数据键表统一为 `auth` 单一键名，`authority` 标记"已废弃 replaced-by(auth)" | ✅ |
| R4 | rules.md §四 空号注释保留（维持 [CR] 引用稳定，予以保留） | 保留 |
| R5 | dsl.md §5 标题改为"文法注释（解析器实现参考）"，移除"写作可忽略"表述 | ✅ |
| R6 | axioms.md 连续 [Def] 符号定义合并简化（a→b/a∥b/a→*b/≺） | ✅ |
| R7 | `SingleRoundResult` 合并入 `ResultComposition`（注释声明为全称公理平凡特例） | ✅ |
| R8 | 参数值权威保留于 axioms.md 配置参数节；新增 `NOISE_FLOOR` 配置项统一 δ_min | ✅ |

### 6.4 修复验证

- [x] `Clock=(0,0)` 旧二元组表述无残留（确认已清扫）
- [x] 旧 `R.stateSpace∩R'.stateSpace=∅→R∥R'` 无残留（已替换为谓词引用）
- [x] `等效累积` 断言仅出现在修正后的"非等效累积"注释中（预期状态）
- [x] `SingleRoundResult`/`authority:` 实际使用无残留（仅合并注释/废弃标注中提及）
- [x] 新增符号均有定义：`PrimitiveAllowedTypes`（类型基底）、`δ_min`/`NOISE_FLOOR`（配置参数）、`IndependentStateSpace`（[Def]）

### 6.5 输出格式公理化增强（2026-08-03 第二轮）

> 目标：增强对大模型输出格式的规范——将"输出内容格式"从配置/结构声明提升为公理级权威锚点（沿用 E/D/R 收敛经验：单一权威原则，不制造新重复）。

#### 6.5.1 新增公理（axioms.md）

| 公理 | 层级 | 定位 |
|------|------|------|
| `OutputWellFormedness [AX-META]` | L0.5 | 输出侧格式权威（六要素：结构闭合/内容真实/命名精确/载体对齐/模板服从/自检对齐）。与 `ToolCallWellFormedness`（输入侧五要素）对象域互斥分工 |
| `FormatFidelity [AX-CORE]` | L0.5 | 结构保真：Fidelity↔StructAlign∧FieldComplete∧TypeMatch，确定性提取/评估准入前提 |
| `StructuralGrounding [AX]` | L2 | 输出结构性引用必须可外部验证（禁占位符/无依据路径/虚构历史） |

- `AxiomHierarchyStructure` L0.5 成员表与 L0.5 运行时不变式（MetaRound 目标集合）已同步更新。
- 分工消歧注释：输入侧（ToolCallWellFormedness）vs 输出侧（OutputWellFormedness）；FormatFidelity（Schema 代数层）vs Coupling-ObservationCollapse（值域层）。

#### 6.5.2 新增推论（theorems.md）

| 推论 | 结论 | 价值 |
|------|------|------|
| TH-7 | `Fidelity(O,S)→extract_deterministic(O,S)` | 输出侧确定性提取（与 TH-3 输入侧呼应） |
| TH-8 | `¬WellFormed_Out(O,S)→eval=parse_error→FormatSelfCorrection` | 输出侧格式漂移闭环（与 TH-6 并列） |
| TH-9 | `Fidelity(O₁,S)∧Fidelity(O₂,S)→comparable(O₁,O₂)` | 跨迭代/跨轮可比（支撑 bestResult、CAMPAIGN） |

#### 6.5.3 规则/流程/反模式落地（rules.md / engine.md）

| 位置 | 变更 |
|------|------|
| rules.md FormatCheck | 标注【输入侧】，新增输出侧分工声明（引用 OutputWellFormedness/FormatFidelity/TH-8，不重复六要素） |
| rules.md FormatSelfCorrection | 触发覆盖 TH-6+TH-8 双路径；步骤1 增"结构失配"定位；步骤3 增 StructuralGrounding 验证 |
| rules.md §七 | 新增 `@ ANTI-STRUCTURE-DRIFT`（高，可自动修复）、`@ ANTI-FIDELITY-LOSS`（中，Collect 保留原始输出） |
| engine.md Collect | 补提取前置引用（FormatFidelity+TH-7）；输出侧格式检查引用（OutputWellFormedness+TH-8）；COLLECT_PATTERNS 明确为 FormatFidelity 参数化实例 |
| engine.md §八 | `@ ANTI-RAW-OUTPUT-LOSS` 与 `@ ANTI-FIDELITY-LOSS` 分工注释（保留层 vs 对齐层） |

#### 6.5.4 公理化设计准则（约束新增公理的五条方法论）

1. **真值域化**——格式要求落到 T/F/⊥，衔接 TruthValueDomainLogic（违规→F，信息不足→⊥探索）
2. **可验证化**——附带可判定的自检动作（不依赖 AI 内省）
3. **推导性**——公理→定理→规则单向引用，杜绝重复（D 类经验）
4. **反模式化**——每条公理绑定至少一个可命名反模式
5. **分层强制**——元约束(L0.5)不可改 / 领域(L2)可版本化 / 配置(L3)可运行时调

---

*本报告含分析结果与修复实施记录。修复已于 2026-08-03 落地至 clinerules 全部 6 个文件（axioms.md/engine.md/theorems.md/rules.md/dsl.md/project.md 仅分析未改动）。第二轮已新增输出格式公理族（OutputWellFormedness/FormatFidelity/StructuralGrounding + TH-7/8/9）。*
