# clinerules 逻辑审计报告：重复 / 冗余 / 错误

> 审计范围：`clinerules/` 全部 6 个源文件（axioms.md / dsl.md / theorems.md / engine.md / rules.md / project.md），并交叉核对 `docs/clinerules_overview.md`。
> 审计日期：2026-08-03
> 审计目的：识别重复逻辑、冗余逻辑、错误逻辑，提供逐条证据与修复建议。

---

## 总览

| 类别 | 问题数 | 说明 |
|------|--------|------|
| 重复逻辑 | 8 | 同一语义在多处重复声明，破坏单一权威原则 |
| 冗余逻辑 | 6 | 不新增语义的占位/确认性内容、过时引用 |
| 错误逻辑 | 7 | 公式层级错用、未定义参数引用、权威冲突、示例不一致 |
| **合计** | **21** | 详见下文逐条审计 |

---

## 一、重复逻辑

### R1. `R.cf` 因果累积公式两处重复声明
- **位置**：`axioms.md:213`（CountableOrderBridge）与 `axioms.md:296`（TerminationPriority）
- **证据**：
  - `R→R':R'.cf=max(R'.local,R.cf)`
  - `R.cf=max(R.local, R'.cf)`（已注释"同一规则代入"）
- **严重度**：低（已注消歧，但公式实体重复）
- **修复**：保留 TerminationPriority 为权威，CountableOrderBridge 处改为引用注释。

### R2. `subRoundAttempts=0` 重置规则分散两处
- **位置**：`axioms.md:278`（SubRoundInitialState）与 `engine.md:232`（EvaluateDrivenUpdate pass 分支）
- **严重度**：中（行为定义分散两文件，易改一处漏另一处）
- **修复**：指定 engine.md §5.3 为重置时机权威，axioms 仅保留声明式约束并引用。

### R3. `OnError` 决策逻辑双版本并存
- **位置**：`rules.md:106-111`（OnError_SingleCall + shared-branch）与 `engine.md:272-287`（OnError_Loop 决策树）
- **严重度**：中（共享分支靠 `ref:shared-branch` 约定耦合，`else→shared-branch` 的兜底语义需跨文件追踪）
- **修复**：将各分支完整归类表合并为一张权威表，单次/循环版仅保留差异列。

### R4. `QDC++` 双处声明权威冲突
- **位置**：`axioms.md:294`（qdc_increment，自称"本行保持为唯一权威"）与 `engine.md:230-231`（EvaluateDrivenUpdate）
- **严重度**：中（两处均自称权威；且 axioms 含"decision=TS 重置"，engine 未包含该重置条件）
- **修复**：收敛为单一权威条目，补齐 TS 重置条件到 engine 状态更新表。

### R5. L0 公理元素列表硬编码重复
- **位置**：`axioms.md:22`（`[AX-CORE] L0: BAX-IDI,...,QuantificationConstraint`）与 `axioms.md:25`（L0.5 的 `---meta` 中再次整串列出 L0 全集）
- **严重度**：低（改一处漏另一处即产生漂移）
- **修复**：meta 中用 `L0.elements` 引用替代整串硬编码。

### R6. `cone_forward / cone_backward` 定义重复
- **位置**：`axioms.md:114`（[Def]）与 `axioms.md:173-175`（[BAX-TIME] CausalCone）
- **严重度**：低（[Def] 区与公理区逐字重复）
- **修复**：删除 [Def] 区定义，仅保留 CausalCone 公理内的定义。

### R7. `callCount(k)∈ℕ` 类型约束重复
- **位置**：`axioms.md:111`（[Def]）与 `axioms.md:203`（[BAX-D] Discrete-Countability）
- **严重度**：低
- **修复**：保留 Discrete-Countability 为权威，删除 [Def] 中同名约束。

### R8. `a≺b` 与 `a→b` 等价定义重复
- **位置**：`axioms.md:108-109`
- **证据**：`[Def] a→b = strict_partial_order` 与 `[Def] a≺b ↔ a→b (严格偏序比较)`——同一关系双符号定义
- **严重度**：低（增大符号表；若为兼容旧引用可保留 `≺` 为别名注释）
- **修复**：保留 `→` 为权威，`≺` 降级为兼容别名注释。

---

## 二、冗余逻辑

### S1. TH-1 自述"索引条目，非独立推论"
- **位置**：`theorems.md:10-14`
- **说明**：`EntryCondition`+`InitialState`+`SubRoundInitialState` 三公理合取的编译型速查，明确"不新增语义"。属占用推论编号的占位索引。
- **修复**：降级为 axioms.md 内注释，或改名为非编号索引节。

### S2. TH-2 自述"确认性推论(不新增语义)"
- **位置**：`theorems.md:18-28`
- **说明**：公式与 `axioms.md:116` `[Def] op.needsSync` 逐字相同，纯确认性重复。
- **修复**：合并入 TH 综述或删除，仅保留跨越引用。

### S3. TH-6 证明概要中引入新语义 `ε_g = δ(t)/2`
- **位置**：`theorems.md:91`（pf 中 `ε_g = δ(t)/2`）
- **说明**：`δ(t)/2` 这一具体公式未在任何公理中出现，pf 字段不应承担新定义。
- **修复**：将 `ε_g` 定义提升为 axioms 配置或明确推导步骤，pf 仅引用。

### S4. dsl.md 引用体系外文件（spec.md 缺失）
- **位置**：`dsl.md:3`（"解析器按 `[CR: Instruction/spec.md §1]` 编译"）
- **说明**：clinerules 目录实际无 spec.md；Instruction/spec.md 属另一目录，需确认其存在性与版本一致性。
- **修复**：核对 Instruction/spec.md 存在后保留，否则改为引用文档化锚点。

### S5. docs/clinerules_overview.md 引用不存在的文件
- **位置**：`docs/clinerules_overview.md:17,81-102,157-175,204-209`
- **说明**：所述 `derivation.md`、`spec.md` 两个文件在当前 clinerules 目录不存在；其"体系总结"7 文件清单与实际 6 文件不符。
- **修复**：更新 overview 移除不存在的文件引用（详见错误 E1）。

### S6. `[Def] Intent → types` 无信息量占位
- **位置**：`axioms.md:118`
- **说明**：无具体定义体，无法解析为有意义符号约定。
- **修复**：补全定义或删除。

---

## 三、错误逻辑

### E1. Overview.md 章节编号与文件清单整体过时
- **位置**：`docs/clinerules_overview.md` 全文
- **证据**：
  - 宣称存在 derivation.md / spec.md（实际不存在）
  - `engine.md` 章节描述为"§十 OnError、§十一 反模式"，实际为 **§七 OnError、§八 反模式**
  - 宣称 `rules.md` 含"§四 状态空间定义"，实际 rules.md 无此章节
  - 宣称 `engine.md §二` 为"层级策略(FocusDecision,LayerOverride)"，实际 §二 为配置参数（FocusDecision 在 §1.4）
- **严重度**：高（误导读者）
- **修复**：按当前 6 文件实际结构重写。

### E2. TH-7 公式将 Evaluate 输出错标为 decision 输出
- **位置**：`theorems.md:97-105`
- **证据**：
  - 公式 `decision(T,params)=parse_failure`
  - 但 `parse_failure` 属 EvaluationOutcomeSet（`axioms.md:285`：{pass,partial,fail,parse_error}）
  - DecisionOutputSet（`axioms.md:286`）输出集为 {TS,TO,TM,switch_strategy,continue}，**不含 parse_failure**
- **严重度**：高（形式化系统术语/层级错用，破坏集合成员合法性）
- **修复**：改称"judgment/evaluation 结果"，或改为 `evaluate(T,params)=parse_failure`，并修正推导链。

### E3. CAMPAIGN Profile 引用未定义参数 ROUNDS / GLOBAL_MAX_FAIL
- **位置**：`engine.md:72`（`delta:{ROUNDS=2,GLOBAL_MAX_FAIL=ROUNDS×3,...}`）
- **证据**：axioms 配置区与 engine §2.1 参数表均未声明 `ROUNDS`、`GLOBAL_MAX_FAIL`、`globalCumulativeFailures` 的定义与语义
- **严重度**：高（未定义符号进入配置表达式）
- **修复**：在 engine §2.1 增补参数声明，或改为引用已备案参数。

### E4. dsl.md 示例 DEVELOP Profile 与 engine 权威定义不一致
- **位置**：`dsl.md:42` vs `engine.md:71`
- **证据**：
  - dsl 示例：`delta:{MAX_ITER=10,MAX_FAIL=5}`
  - engine 权威：`delta:{MAX_ITER=10,MAX_FAIL=5,SWITCH_THRESHOLD=3,PARTIAL_RATIO_THRESHOLD=0.8,SUBROUND_MAX_ATTEMPTS=3}`
- **严重度**：中（语法示例与权威值漂移）
- **修复**：dsl 示例前加"示例仅展示语法，非权威值；权威见 engine §2.2"，或删除 delta 值细节。

### E5. `subRoundAttempts` 重置语义两路径未明示互斥
- **位置**：`axioms.md:281-282` 注释（"非 pass 终止不重置"）与 `engine.md:222-223` SubRoundExhaustionGate（`subRoundAttempts=0`）
- **说明**：pass 路径重置（EvaluateDrivenUpdate）与耗尽路径重置（SubRoundExhaustionGate）并存，注释称 engine.md §5.3 为重置时机权威，但 §5.3 的 SubRoundExhaustionGate 也在重置——两条路径优先级/互斥未显式声明。
- **严重度**：中
- **修复**：在 engine §5.3 加一条合并规则，明确"pass 重置优先于耗尽重置，两者不可同时生效"。

### E6. QDC 重置条件 axioms 与 engine 不完全一致
- **位置**：`axioms.md:294`（"重置:decision=TS 或 bestResult 更新时置0"）vs `engine.md:231`（仅 `bestResult 更新时置0`）
- **严重度**：中（TS 决策重置缺失于 engine 状态更新表）
- **修复**：engine EvaluateDrivenUpdate 增补 `decide(TS)→QDC=0` 条件，或 axioms 删除 TS 重置项并统一到 engine。

### E7. dsl.md §3 缩写表表述自相矛盾
- **位置**：`dsl.md:105`（"完整缩写见 [CR: axioms.md]...无独立缩写表"）紧接其后 §3 却给出了独立缩写表
- **严重度**：低
- **修复**：删去"无独立缩写表"表述，或删除表头，保留注为"常用记忆表，完整见 axioms"。

---

## 附：修复优先级建议

| 优先级 | 问题 | 理由 |
|--------|------|------|
| P0（立即） | E1、E2、E3 | 误导文档、形式化系统术语错用、未定义参数 |
| P1（尽快） | E4、E5、E6、R3、R4 | 权威冲突与多路径语义未闭合 |
| P2（常规） | R2、R5~R8、S1~S3、S5、S6、E7 | 重复声明与占位内容 |
| P3（可选） | R1、S4 | 已注消歧/依赖外部目录 |

---

*本报告基于 2026-08-03 时点的 clinerules 6 文件内容得出；文件更新后需重新核验。*