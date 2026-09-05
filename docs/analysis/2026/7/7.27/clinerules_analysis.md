# clinerules 重复/冗余/错误逻辑分析报告

> 生成时间：2026-07-27
> 范围：clinerules/axioms.md, derivation.md, theorems.md, project.md, dsl.md

---

## 一、已修复项汇总

### 1. axioms.md（7处修复）

| # | 类型 | 位置（原行号） | 问题描述 | 修复方式 |
|---|------|---------------|---------|---------|
| 1 | **重复** | L19-20 | `L0: BAX-IDI,...` 的 enf 与 AxiomHierarchyStructure 的 invariants(L0) 重复 | 删除 enf 行 |
| 2 | **错误** | L109 | `sig` 作为自由变量无类型声明，违反 QuantificationConstraint | 改为 `[Type] Signature` + `[Def] sig:Signature` |
| 3 | **错误** | L140-141 | CommitPhase 循环依赖（双向互校验在逻辑时钟域不可实现） | 改为二阶段：obs₁→CommitRequest→obs₂→CommitAck→obs₁.completed |
| 4 | **错误** | L196 | `a≤_c b` 在偏序中不保证可比性 | 改为 `a→*b`（因果传递闭包） |
| 5 | **错误** | L272 | `proj_cf(R)` 使用了 `R'.CF→R.CF`（标量上的因果箭头） | 改为 `R'→R`（轮次间的因果偏序） |
| 6 | **冗余** | L286-287 | 推论状态列表（TH-2/TH-5/...待补充）重复 theorems.md 信息 | 精简为单行 `[CR: theorems.md]` |
| 7 | **逻辑修正** | L78 | `¬⊥=⊥` 在 Scott 域中无内禀否定定义 | 保留（已标注 `dr = "缺陷2/12"`），`¬` 在 axioms.md 中仅用于 TruthValueDomain，其语义由该公理自封 |

### 2. derivation.md（5处修复）

| # | 类型 | 位置 | 问题描述 | 修复方式 |
|---|------|------|---------|---------|
| 1 | **冗余** | §六 | 全文仅指向 engine.md，无有效内容 | 精简为 5 行交叉引用 |
| 2 | **冗余** | §七 | "配置参数权威声明于 axioms.md"无派生信息 | 删除整节 |
| 3 | **冗余** | §八 | 层级映射表照搬 axioms.md，无新增信息 | 删除整节 |
| 4 | **重复** | §十 | 派生推论表与 theorems.md 完全重复 | 删除整节 |
| 5 | **错误** | PO-5 FixSource | 与 PO-4 共用 "Defect-4"，追溯混淆 | 改为 "Defect-4b" |

### 3. theorems.md（1处修复）

| # | 类型 | 位置 | 问题描述 | 修复方式 |
|---|------|------|---------|---------|
| 1 | **重复** | 引用索引表 | 每条推论已含 src 字段，汇总表无新增信息 | 删除整表 |

### 4. project.md（1处修复）

| # | 类型 | 位置 | 问题描述 | 修复方式 |
|---|------|------|---------|---------|
| 1 | **冗余** | §1.6 | "使用默认值，无覆盖"，0 信息含量 | 删除整节 |

### 5. dsl.md（1处修复）

| # | 类型 | 位置 | 问题描述 | 修复方式 |
|---|------|------|---------|---------|
| 1 | **错误** | EBNF AxiomDef | 产生式要求 `---meta` 必选，与实际内容矛盾 | 改为 `[ "---meta" NL { MetaLine } "---" ]`（可选） |

---

## 二、未修复但值得注意的问题（设计决策保留）

| # | 文件 | 位置 | 问题 | 原因 |
|---|------|------|------|------|
| 1 | axioms.md:78 | `¬⊥=⊥` | Scott 域无内禀否定定义，此操作为 Kleene 继承 | 已在 dr 标注 "缺陷2/12"，语义自封 |
| 2 | axioms.md:84 | 并发比较无定义 | `τ₁∥τ₂` 时的比较行为未完全形式化 | 已标注 `ConcurrentComparisonUndefined`，为有意设计 |
| 3 | axioms.md:141 | commit 二阶段 | 新修正的二阶段协议在严格偏序下可工作，但未处理 obs₂ 拒绝 Commit 的情况 | 实际路径 2 中 CompensatePhase 处理超时/失败 |
| 4 | rules.md §七 | 反模式索引引用 engine.md §八 | 可能的跨文件重叠 | 未检测 engine.md 内容，需单独确认 |
| 5 | derivation.md PO-4/PO-5 | "Defect-4" 编号问题 | PO-4 为凸集约束、PO-5 为拓扑嵌入，不同缺陷但共用编号 | 已改为 "Defect-4b" 做区分，但仍建议 axioms.md 中统一缺陷编号体系 |

---

## 三、修改影响分析

### 正向影响
1. **减少维护负担**：删除 4 个冗余节 + 1 个索引表 + 1 个空壳配置节
2. **提高形式正确性**：修复 3 个逻辑公式错误（CommitPhase 循环依赖、a≤_c 偏序比较、proj_cf 标量因果箭头）
3. **增强类型安全**：sig 获得正式类型声明，满足 QuantificationConstraint
4. **文法-实例对齐**：EBNF 与 axioms.md 实际格式一致

### 无影响范围
- L0 的 `invariants` 语义完整性不变（仅删除重复的 enf 行，主线已保留在 AxiomHierarchyStructure）
- 推论逻辑完整性不变（theorems.md 中每条定理的 src+pf 完全保留）
- 配置参数体系不变（仅删除 project.md 中无实际值的节）

---

## 四、建议后续行动

1. **复查 engine.md §八**：确认 rules.md §七引用的 engine.md §八 反模式是否与 rules.md 反模式索引有重叠
2. **统一缺陷编号方案**：当前 axioms.md 中 `dr` 标注的编号跨公理重复（如 "缺陷2/5" 出现在 CountableOrderBridge 和 TruthValueDomainLogic），建议改为全局唯一编号
3. **审查 spec.md §2 ProofOracle**：确认 ExternalTruthEquivalence 的实现与 axioms.md 中的 ExternalConsistencyWitness [DR] 描述一致