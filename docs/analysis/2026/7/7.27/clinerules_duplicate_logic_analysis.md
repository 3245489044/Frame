# clinerules 文件间重复逻辑分析报告

> **分析日期**：2026-07-27  
> **分析范围**：clinerules/ 下全部 7 个文件（axioms.md、derivation.md、engine.md、rules.md、spec.md、dsl.md、project.md）

---

## 一、层级定义体系的三重视角冗余

### 涉及文件

- `axioms.md` — `[AX-CORE] AxiomHierarchyStructure`（权威源，L0-L3 不可变性约束）
- `engine.md §一` — 引擎运行时视角（L0-ENG 架构层 / L1-ENG 逻辑层 / L2-ENG 代码层 / L3-ENG 语法层）
- `spec.md §1.1` — 代码生成视角（L0 硬核不变式 / L0.5 TLA+ / L1 智能合约 / L2 Versioned Spec / L3 ConfigMap）
- `rules.md §1.1` — 公式类型层级（基底公理 / 元推理 / 领域公理 / 策略 / 规范 / 反模式 / 流程）
- `derivation.md §八` — 层级映射派生表

### 重复模式

4 个不同视角各自用一套表重复描述 L0-L3，尽管每个文件开头都声明"视角不同"，但：

| 视角 | 所在文件 | 核心内容特点 |
|------|---------|-------------|
| 公理权威源 | axioms.md | 不可变性约束（绝对不可变/骨架不可变/需签名/可版本化/运行时可变） |
| 引擎运行时 | engine.md | 工程关注点（架构/逻辑/代码/语法）+ 层级优先级 |
| 代码生成 | spec.md | 编译目标（静态断言/TLA+/合约/版本化Spec/ConfigMap） |
| 公式类型 | rules.md | 逻辑性质（基底/推理/领域/策略/规范/反模式/流程） |

**三个视角表都在说 L0-L3，但名词解释完全不同**。engine.md 甚至创建了一套 `L0-ENG`/`L1-ENG` 新术语与 axioms.md 的 L0-L3 并行。

### 建议

保持 `axioms.md` 为唯一权威源，`engine.md §一` 和 `spec.md §1.1` 的视角表简化为"差异摘要"（只列出与 axioms.md 不同的部分），取消 `L0-ENG` 等别名术语。

---

## 二、OnError 策略的双文件定义

### 涉及文件

- `rules.md §5.1` — `OnError(error) [CD] [layer: L2-L3, scope: single_call|global]`
- `engine.md §十` — `OnError(error, stepIndex, toolchain) [CD] [layer: L1, scope: loop_body]`

### 重复模式

两版本共享相同的"公共分支"错误分类逻辑：

```
error∈{partial_output, warning} → continue
error∈{irrecoverable} → halt
error∈{resource_exhausted} → report + halt
```

rules.md 显式标出了"公共分支"段并附了差异表，但这三行代码还是在两个文件中各写了一份。

### 差异表

| 维度 | rules.md 通用版 | engine.md 循环版 |
|------|----------------|-----------------|
| compilation_error | halt | 由 ExecutionFailureGate 重试至限制后 TM |
| execution_failure/timeout | report + retry_optional | halt+persist |
| 计数器/熔断 | 不维护 | 维护 retryCounter/emptyCount/switchCounter |

### 建议

公共分支合并到 `rules.md` 的"共享语义"段作为唯一定义，`engine.md §十` 通过 `[CR: rules.md:OnError.shared-branch]` 引用，不再重复写分支代码。

---

## 三、公理派生信息的三元冗余

### 涉及文件

- `axioms.md` — 每个公理的 `---meta` 块中的 `src` 键声明来源
- `derivation.md` — 全文独立的追溯矩阵（领域规则/状态变量/策略/流程→公理映射）
- `engine.md §五` — 循环公理实例化表 + `§4.1` 状态变量表的"派生公理"列
- `rules.md` — 各策略/规范的"源公理"引用文本

### 重复模式

每条公理的"从哪来"被记录在三个不同层级：

| 层级 | 表现形式 | 示例 |
|------|---------|------|
| 公理自身 | `---meta src = "..."` | axioms.md: `Discrete-Countability` 的 meta |
| 追溯矩阵 | 完整的行表 | derivation.md §二: `retryCounter → Discrete-Countability + TerminationPriority` |
| 实例化表 | 嵌入式复制 | engine.md §4.1: retryCounter 的派生公理列也写了同一行 |
| 策略/规范引用 | 文本引用 | rules.md 各策略的"源公理"注释 |

具体来说：
- `derivation.md §二` 状态变量派生表 ≠ `engine.md §4.1` 状态变量表的"派生公理"列 → 信息重复
- `derivation.md §六` 五步模型步骤转移表 ≠ `engine.md §六` 的步骤间转移合法性描述 → 信息重复
- `derivation.md §三-§五` 策略/规范/流程派生表 ≠ `rules.md` 各策略/规范的元数据 → 信息重复
- `derivation.md` 的各项内容已经涵盖甚至超过了 axioms.md 中 `src` 元数据的覆盖范围

### 建议

- 将 `derivation.md` 作为**唯一的公理派生追溯权威源**
- `axioms.md` 的 `src` meta 键仅用于快速浏览引用（可选保留），不承担完整追溯责任
- `engine.md §4.1` 的"派生公理"列改为 `[CR: derivation.md §二]`
- `engine.md §五` 的循环公理实例化表改为 `[CR: derivation.md §六]`

---

## 四、配置参数定义的一致性边界

### 涉及文件

- `axioms.md` — 配置参数段（MAX_FAIL / QUALITY_DECLINE_THRESHOLD / META_THRESHOLD 等），权威源
- `engine.md §3.1` — 引擎专用参数（PARTIAL_THRESHOLD / EXEC_TIMEOUT 等），权威源
- `project.md` — 项目级配置（THRESHOLD_SMALL / ExecMode 等），权威源
- `rules.md §5.1` — CohesionLowStrategy（GOD_CLASS_THRESHOLD=40, AGGREGATE_THRESHOLD=30）
- `rules.md §6.2` — CohesionCheck（COHESION_METHOD_THRESHOLD=30, COHESION_LINE_THRESHOLD=1000）

### 重复模式

跨文件参数归属清晰（axioms.md / engine.md / project.md 的职责边界明确），但 **rules.md 内部存在阈值重复**：

```
CohesionLowStrategy: GOD_CLASS_THRESHOLD(40), AGGREGATE_THRESHOLD(30)
CohesionCheck:      COHESION_METHOD_THRESHOLD(30)  // 值相同，名不同
```

两处硬编码了大约 30 行的阈值（方法数），但用了不同的参数名。逻辑上 `AGGREGATE_THRESHOLD(30)` 和 `COHESION_METHOD_THRESHOLD(30)` 应是同一阈值。

### 建议

将 `rules.md` 中的 CohesionCheck 和 CohesionLowStrategy 共享的方法阈值统一为 `COHESION_METHOD_THRESHOLD = int:30` 一起在阈值表中声明，CohesionLowStrategy 直接引用 CohesionCheck 的输出，而非独立声明一套独立阈值。

---

## 五、执行流程定义的部分重叠

### 涉及文件

- `engine.md §六` — 五步模型（Execute→Collect→Evaluate→Decide→Adjust）
- `rules.md §八` — 操作流程（SyncGuard / IdempotencyGuard / ErrorHandler / CompactProcedure 等）
- `derivation.md §六` — 流程衍生追溯 + 步骤间转移合法性表

### 重复模式

```
engine.md §六 的五步模型
  ├── 步骤间转移合法性条件（→ engine.md §六 正文）
  ├── 步骤间转移合法性条件（→ derivation.md §六 表格，重复）
  └── 每个步骤的公理约束（→ engine.md §六 正文 + derivation.md §六 表格，重复）

rules.md §八 的操作流程
  ├── ErrorHandler（引用 engine.md §十 OnError）
  ├── SyncGuard（独立流程定义）
  └── CompactProcedure（引用 engine.md §八 循环模式的阶段动作）
```

`derivation.md §六` 的五步模型表格与 `engine.md §六` 的正文存在完整的信息重叠：每个步骤的名称、公理来源、转移条件在三处都写了。

### 建议

- 取消 `derivation.md §六` 的五步模型表格（engine.md §六 是唯一的五步模型权威源）
- `derivation.md §六` 改为仅包含 `[CR: engine.md §六]` + 补充派生路径分析

---

## 六、错误分类体系的多处重复

### 涉及文件

| 位置 | 内容 |
|------|------|
| `axioms.md` FailureSubtyping | 错误类型枚举（exec_failure/eval_failure/parse_failure） |
| `engine.md §六` ExecutionFailureGate | compilation_error 重试至限制→TM |
| `engine.md §十` OnError | 循环版错误处理（含 compilation_error 引用 ExecutionFailureGate） |
| `rules.md §5.1` OnError 通用版 | compilation_error → halt |
| `rules.md §6.1` FailMode | errorType 到 FailFast/FailSafe/FailGraceful 映射 |
| `rules.md §6.1` FixPriority | compilation_error → P0 |
| `project.md §1.6` FixPriority | compilation_error → P0 |

### 重复模式

`compilation_error` 这一个错误类型就出现在 6 个不同的位置：

1. axioms.md FailureSubtyping — 定义其为 exec_failure 的子类型
2. engine.md ExecutionFailureGate — 处理路径（重试→TM）
3. engine.md OnError — 引用 ExecutionFailureGate 的处理
4. rules.md OnError 通用版 — halt
5. rules.md FixPriority — P0
6. project.md FixPriority — P0（与 rules.md 重复）

`project.md §1.6` 的 `FixPriority.compilation_error = P0` 在 `rules.md §6.1 FixPriority` 中也声明了同样的映射。project.md 作为项目配置应该只放语言特定的异常类型映射，不应重复 rules.md 中已定义的修复优先级。

### 建议

- 在 `axioms.md` 的 `[AX] FailureSubtyping` 中建立错误类型的**唯一权威枚举**，其他所有文件的错误分类通过引用方式使用
- 移除 `project.md §1.6` 的 FixPriority 表，将其改为 `[CR: rules.md §6.1 FixPriority]`

---

## 七、计数器规则的三处描述

### 涉及文件

- `axioms.md` — `[AX-FRAME] CountableOrderBridge`（完整因果加权 Join 公式）
- `engine.md §七` — 状态更新规则（CF/CP/QDC 的递增/重置/衰减逻辑）
- `engine.md §六 Decide` — "工程简化说明"段落（解释 CF 比较的简化方式）

### 重复模式

同一套因果计数规则在三处以不同粒度描述：

| 位置 | 粒度 | 内容特点 |
|------|------|---------|
| axioms.md CountableOrderBridge | 形式化 | 完整因果加权 Join（a⊗b、causal_weight、same_chain） |
| engine.md §六 Decide | 阐述性 | 工程简化说明：解释公理中的复杂概念被简化成了什么 |
| engine.md §七 更新规则 | 实现级 | 完整的 if-else 分支：Evaluate 驱动和 Decide 驱动两条路径 |

**engine.md §六 Decide 的"工程简化说明"本质上是对 axioms.md 的注释性重复**——如果有改公理的需求，需要同步更新 axioms.md 和这条注释。

### 建议

- 将 `engine.md §六 Decide` 的"工程简化说明"压缩为一行免责声明，详细信息指向 `axioms.md CountableOrderBridge`
- 或者将其翻译为正式的 `[DR]` 设计理由块

---

## 八、汇总评估

| 序号 | 重复类型 | 严重程度 | 修复成本 | 牵涉文件数 |
|------|---------|---------|---------|-----------|
| 1 | 层级定义三视角冗余 | ★★★★ 高 | 中 | 4 |
| 2 | 公理派生信息三元冗余 | ★★★★ 高 | 高 | 4 |
| 3 | 错误分类枚举跨文件重复 | ★★★ 中高 | 低 | 6 |
| 4 | 计数器规则三处描述 | ★★★ 中 | 低 | 2 |
| 5 | OnError 公共分支重复 | ★★★ 中 | 低 | 2 |
| 6 | 执行流程定义重叠 | ★★★ 中 | 中 | 3 |
| 7 | rules.md 内部阈值重复 | ★★ 低 | 低 | 1 |
| 8 | 配置参数跨文件重复 | ★ 轻微 | 低 | 4 |

### 优先修复建议

1. **P0** — 层级定义统一化：消除三重视角冗余，仅保留 axioms.md 权威源
2. **P1** — 错误分类枚举统一：在 axioms.md FailureSubtyping 建立唯一枚举
3. **P1** — 公理派生去重：derivation.md 作为唯一追溯源
4. **P2** — 其他中低优先级项

---

## 附：重复模式根源分析

这些重复的设计根源是 **"每个文件可独立阅读"优于 DRY 原则**的设计取舍。具体表现为：

1. **冗余声明代替交叉引用**：宁愿把同一概念在多个文件中各写一遍，也不愿意让读者跳转引用
2. **多视角平行描述**：每引入一个新视角（引擎视角、代码生成视角、公式类型视角）就重建一套 L0-L3 表
3. **实现层叠注释**：engine.md 中的"工程简化说明"是对 axioms.md 中复杂公式的二次注释

如果要在保持可读性的前提下消重，建议采用 **`[CR]` 交叉引用 + 差异摘要** 策略：权威内容只写一次，其他文件用 `[CR]` 引用，并只写"与本文件视角相关的差异点"。