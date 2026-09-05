# clinerules 引用化改造重构方案

> **基于**：docs/analysis/clinerules_duplicate_logic_analysis.md  
> **目标**：消除 8 类重复逻辑，建立 `[CR]` 引用体系，保持文件独立可读性  
> **状态**：方案规划阶段（待实施）

---

## 一、`[CR]` 引用格式规范

### 语法定义

在 `rules.md §1.2` 原有的 `[CR]` 类型定义基础上补充引用语法：

```
[CR: 文件名:标识符]          — 引用整节/整文件
[CR: 文件名:公理名 [AX]]     — 引用特定公理
[CR: 文件名:§X.策略名]       — 引用特定策略/规范/流程
[CR: 文件名:§X]              — 引用特定章节
```

### 示例

```
[CR: axioms.md:AxiomHierarchyStructure [AX]]
[CR: derivation.md §二:retryCounter]
[CR: engine.md §六]
[CR: rules.md §5.1:OnError.shared-branch]
[CR: rules.md §6.1:FixPriority]
```

### 阅读约定

以 `[CR]` 开头的行表示"此处内容与权威源一致，详情请跳转引用"，阅读者可选择：
- 跳转阅读：打开权威源文件获取完整语义
- 原位理解：本文件提供摘要/差异摘要，足以完成本文件视角下的阅读

---

## 二、步骤一：层级定义去重（P0）

### 当前问题

4 个文件各有不同的 L0-L3 表，内容大量重叠：
- `axioms.md` — 权威源（不可变性约束）
- `engine.md §一` — 引擎运行时视角（L0-ENG/L1-ENG 别名）
- `spec.md §1.1` — 代码生成视角
- `rules.md §1.1` — 公式类型层级

### 改造方案

#### engine.md §一

**当前**：独立的 L0-ENG 层级表 + 层级优先级 + 上下文类型 + 反模式  
**改造后**：

````markdown
## §一、抽象层级定义（Layer Taxonomy）

> **权威源**：`[CR: axioms.md:AxiomHierarchyStructure [AX]]`
> 层级不可变性约束（L0 绝对不可变 / L0.5 逻辑骨架不可变 / L1 修改需 ≥2 验证者 / L2 可版本化 / L3 运行时可变）以 axioms.md 为准。
> 
> 多视角差异对照见：`[CR: docs/analysis/layer_perspectives.md]`（如有此文件）。

**引擎运行时视角的层级关注点**（仅列出引擎特有内容，通用语义见 axioms.md）：

| 层级 | 引擎关注点 | 典型策略/规范 |
|------|-----------|--------------|
| L0 | 项目结构、模块边界、通信模式 | PathStrategy, CommunicationMode |
| L1 | 控制流、迭代循环、错误熔断、状态恢复 | OnError(loop), CompactProcedure, StateRecovery |
| L2 | 实现细节、代码质量、设计模式、内聚性 | CodeStandard, CohesionCheck |
| L3 | 工具调用、文件操作、语言特性、参数格式 | ReadStrategy, ToolSelect |

### 1.1 层级优先级

取消 `L0-ENG > L1-ENG > L2-ENG > L3-ENG` 别名命名，改为：
```
层级优先级（引擎运行时视角）：L0 > L1 > L2 > L3
```
与 rules.md §1.3 的跨层优先级一致。

### 1.2 上下文类型（Scope Taxonomy）— 保持不变

### 1.3 层级反模式 — 保持不变
````

**变化**：取消 L0-ENG/L1-ENG 别名术语（消除与 axioms.md L0-L3 的命名混淆），全表简化为差异摘要。

---

#### spec.md §1.1

**当前**：独立的 L0-L3 代码生成映射表（7 行）  
**改造后**：

````markdown
### 1.1 层级映射表

> **权威源**：`[CR: axioms.md:AxiomHierarchyStructure [AX]]`
> 
> **代码生成视角差异摘要**（仅列出编译映射特有内容）：

| 层级 | 代码生成角色 | 实现机制 |
|------|------------|---------|
| L0 | 硬核不变式 | 编译期静态断言 + 过程宏 |
| L0.5 | 元推理规则 | 状态机 DSL（Stateright/TLA+） |
| L1 | 社会共识 | 智能合约接口（≥2 签名校验） |
| L2 | 领域公理 | Versioned Spec + 数据库迁移 |
| L3 | 配置参数 | etcd / Kubernetes ConfigMap |
````

**变化**：取消"代码生成目标"列（与 axioms.md 重复），仅保留"代码生成角色"+"实现机制"这两列 specs.md 特有的内容。

---

#### rules.md §1.1

**当前**：公式类型层级表（7 行，L0/L0.5/L2/策略/规范/反模式/流程）  
**改造后**：

````markdown
### 1.1 规则分类层级

> **权威源**：`[CR: axioms.md:AxiomHierarchyStructure [AX]]`
> 
> **公式类型视角**：基底公理(L0) → 元推理(L0.5) → 领域公理(L2) → 策略(L2-L3) → 规范(L2) → 反模式(L2-L3) → 流程(L3)
> 
> 完整公式类型定义见 §1.2，各层级的可变性/职责见 axioms.md。
````

**变化**：从 7 行全量表简化为 1 行汇总 + 引用。

---

#### derivation.md §八

**当前**：层级映射派生表（L0-L3+FocusDecision+LayerOverride）  
**改造后**：

在表头增加：
```
> 层级定义权威源：`[CR: axioms.md:AxiomHierarchyStructure [AX]]`
```
表内容保持不变（派生路径信息是 derivation.md 独有的价值）。

---

## 三、步骤二：公理派生信息去重（P1）

### 当前问题

- `axioms.md` — `src` meta 键记录来源（每条公理 1-3 行）
- `derivation.md` — 完整的追溯矩阵（全篇）
- `engine.md §4.1` — 状态变量表的"派生公理"列（15 行）
- `engine.md §五` — 循环公理实例化表（12 行）
- `rules.md` — 各策略/规范的"源公理"注释（约 20 处）

### 改造方案

#### engine.md §4.1 — 状态变量表

**当前**的"派生公理"列内容（如 `IDI-Interaction + IDI-HistoryEmergence`）改为 `[CR]` 引用 derivation.md：

| 变量名 | 类型 | 初始值 | 说明 | 读权限 | 写权限 | 所属层级 | 派生公理（→ derivation.md） |
|--------|------|--------|------|--------|--------|----------|---------------------------|
| `operationLog` | ... | ... | ... | ... | ... | L1 | `[CR: derivation.md §二:operationLog]` |
| `fileCache` | ... | ... | ... | ... | ... | L2 | `[CR: derivation.md §二:fileCache]` |
| ... | ... | ... | ... | ... | ... | ... | ... |

**变化**：15 行"派生公理"单元格内容从 10-30 字符的公式名改为统一的 `[CR]` 引用格式，消除了与 derivation.md 的信息重复。

---

#### engine.md §五 — 循环公理实例化表

**当前**的 12 行表包含三列：公理名、实例化含义、使用位置。  
**改造后**：

| 公理 | 实例化含义（引擎特有） | 派生路径 |
|------|---------------------|---------|
| `[CR: axioms.md:RoundCompletion [AX]]` | `R.completed↔R.executed∧R.verified∧R.logged` | `[CR: derivation.md §六:RoundCompletion]` |
| `[CR: axioms.md:RoundTransition [AX]]` | `enters(Rk+1)↔completed(Rk)∧reEvaluated(Rk)` | `[CR: derivation.md §六:RoundTransition]` |
| ... | ... | ... |

**变化**：公名列改为 `[CR: axioms.md]`，删除"使用位置"列（在 engine.md 正文中有），派生路径列改为 `[CR: derivation.md]`。保留"实例化含义"列（这是 engine.md 独有的工程阐释）。

---

#### rules.md — 各策略/规范的源公理引用

**当前**的每个策略/规范已有"源公理"注释（如 `源公理：IDI-Identity [AX]`），这些注释改为统一格式：

```markdown
### DeprecateStrategy(D, hasReference) [CD] Provides: ...
`[CR: derivation.md §三:DeprecateStrategy]`
```

策略的函数体和决策逻辑保持不变，仅在注释位置增加 `[CR]` 引用。

---

#### axioms.md — `src` meta 键

**保持现状**：`src` 键作为快速跳转标签保留（不需要修改），但在注释中说明其性质：

```markdown
> 完整派生追溯见：`[CR: derivation.md]`
```

---

## 四、步骤三：错误分类枚举统一（P1）

### 当前问题

`compilation_error` 在 6 个位置出现：
1. axioms.md FailureSubtyping — 类型定义
2. engine.md §六 ExecutionFailureGate — 重试至限制→TM
3. engine.md §十 OnError — 引用 ExecutionFailureGate
4. rules.md §5.1 OnError 通用版 — halt
5. rules.md §6.1 FixPriority — P0
6. project.md §1.6 FixPriority — P0（与 5 重复）

### 改造方案

#### axioms.md FailureSubtyping

在 `[AX] FailureSubtyping` 的 `---meta` 块中增加 `enf` 标注：

```
---meta
view = "IDI, D"
src = "IDI-Difference, Discrete-Enumeration"
sem = "defect-resolved=parse-error-classification-gap"
enf = "specification: 本公理是错误类型的唯一权威枚举，其他文件通过 [CR] 引用"
---
```

#### project.md §1.6

**当前**：FixPriority 表（compilation_error→P0 / runtime_exception→P1 / code_quality_warning→P2）
**改造后**：

```markdown
### 1.6 修复优先级映射

> 修复优先级权威定义：`[CR: rules.md §6.1:FixPriority]`
> 
> **语言相关的异常类型映射**（以下为 C# 特有，仅定义语言绑定，不含优先级）：
> 
> | 错误类型 | C# 异常类型 |
> |---------|------------|
> | creation_failure | `InvalidOperationException` |
> | config_missing | `InvalidOperationException` |
> | state_inconsistency | `InvalidOperationException` |
> | lookup_failure | `KeyNotFoundException` |
> | null_parameter | `ArgumentNullException` |
> | index_out_of_range | `ArgumentOutOfRangeException` |
```

**变化**：移除 compilation_error→P0 和 runtime_exception→P1 行（与 rules.md 重复），将表名从"修复优先级映射"改为"语言特定的异常类型映射"，仅保留 C# 类型绑定。

#### engine.md §十 OnError

**当前**：公共分支部分包含：
```
error∈{partial_output, warning} → continue
error∈{irrecoverable} → halt
error∈{resource_exhausted} → report + halt
```
**改造后**：

```markdown
> **公共分支**（与通用版共享的错误分类语义，`[CR: rules.md §5.1:OnError.shared-branch]`）：
> ```
> error∈{partial_output, warning} → continue
> error∈{irrecoverable} → halt
> error∈{resource_exhausted} → report + halt
> ```
```

**变化**：在公共分支前增加 `[CR]` 引用行，但保留分支代码本身（防止跳转阅读中断）。

---

## 五、步骤四：OnError 公共分支去重（P2）

### 改造方案

在 engine.md §十 的开头增加：

```markdown
> ### 与通用版 OnError 的关系
> 
> 本循环版与通用版（`[CR: rules.md §5.1:OnError]`）共享以下公共分支：
> - `partial_output, warning → continue`
> - `irrecoverable → halt`
> - `resource_exhausted → report + halt`
> 
> 差异点：循环版增加熔断逻辑（retryCounter/emptyCount/switchCounter/forceHalt），处理 compilation_error 的方式不同。
```

这样，公共分支从"隐性重复"变为"显式引用+差异声明"。

---

## 六、步骤五：执行流程去重（P2）

### derivation.md §六

**当前**：步骤间转移合法性表（5 行：Execute→Collect → Coupling-ObservationCollapse 等）
**改造后**：

```markdown
## §六、迭代引擎派生

> 五步模型定义与步骤间转移合法性：`[CR: engine.md §六]`
> 
> **本表仅补充引擎.md 中未覆盖的派生路径分析**：

| 步骤 | 派生公理 | 路径说明 |
|------|---------|---------|
| ① Execute | StateSnapshot + Discrete-AtomicStep | 原子状态跃迁 |
| ② Collect | Coupling-ObservationCollapse | 观测锁定后的快照捕获 |
| ③ Evaluate | TruthValueDomainLogic + EvaluationOutcomeSet | 三值真值判定 |
| ④ Decide | DecisionOutputSet + TerminationPriority | 决策空间枚举+优先级链 |
| ⑤ Adjust | SyncProtocolTriple | Try→Commit/Compensate |
```

**变化**：删除"步骤间转移合法性"列（在 engine.md §六 中已有），保留"派生公理"+"路径说明"列（derivation.md 独有的价值）。在表头增加 `[CR]` 引用。

---

## 七、步骤六：计数器规则去重（P2）

### engine.md §六 Decide

**当前**的"⚠ 工程简化说明"段落（8 行，解释 axioms.md CountableOrderBridge 的工程简化方式）  
**改造后**：

```markdown
> **⚠ 工程简化说明** | `[DR]` 本裁决顺序是 `[CR: axioms.md:CountableOrderBridge [AX]]` 的工程实现子集。
> - CF 比较假设已由因果加权 Join 归约为可比较标量
> - projection(proj_cf(R)) 逻辑由 §七 的因果加权 Join 隐式处理
> - parameter_versioning 的 τ_decision 绑定见 axioms.md `TerminationPriority` 的 parameter_versioning 子句
> - 当 CF 不可归约时，决策跳过 CF 判定
```

**变化**：8 行压缩为 5 行，第一行声明为 `[DR]`（设计理由）并指向 axioms.md。

---

## 八、步骤七：rules.md 内部阈值去重（P3）

### rules.md §5.1 CohesionLowStrategy

**当前**：
```
methods > GOD_CLASS_THRESHOLD(40) → suggest_refactor + request_confirmation
AGGREGATE_THRESHOLD(30) < methods ≤ GOD_CLASS_THRESHOLD(40) → record_as_intent + keep_structure
methods ≤ AGGREGATE_THRESHOLD(30) → no_action
```
**改造后**：

```markdown
> 方法数阈值由 CohesionCheck（`[CR: rules.md §6.2:CohesionCheck]`）定义：
> - High → no_action
> - Low（methods > COHESION_METHOD_THRESHOLD）→ 进入本策略评估

```
methods > GOD_CLASS_THRESHOLD(40) → suggest_refactor + request_confirmation
methods ≤ GOD_CLASS_THRESHOLD(40) → record_as_intent + keep_structure
GOD_CLASS_THRESHOLD = int:40
```

**变化**：取消 `AGGREGATE_THRESHOLD(30)` 这个独立阈值（与 COHESION_METHOD_THRESHOLD(30) 值相同名不同），将方法数比较改为引用 CohesionCheck 的输出等级。`GOD_CLASS_THRESHOLD(40)` 是 CohesionLowStrategy 独有的阈值（40 ≠ 30），保留。

---

## 九、影响分析与风险控制

### 文件改动量汇总

| 步骤 | 改动文件 | 操作类型 | 行数变化 |
|------|---------|---------|---------|
| ① CR 规范 | rules.md §1.2 | 补充语法说明 | +5 行 |
| ② 层级去重 | engine.md §一 | 替换全表 | -15 行 |
| ② 层级去重 | spec.md §1.1 | 替换全表 | -5 行 |
| ② 层级去重 | rules.md §1.1 | 替换全表 | -8 行 |
| ② 层级去重 | derivation.md §八 | 表头加引用 | 0 行 |
| ③ 派生去重 | engine.md §4.1 | 替换派生列 | 0 行（内容替换） |
| ③ 派生去重 | engine.md §五 | 替换公理列 | -6 行 |
| ③ 派生去重 | rules.md 各策略 | 添加引用行 | +20 行 |
| ④ 错误枚举 | project.md §1.6 | 替换表 | -4 行 |
| ④ 错误枚举 | engine.md §十 | 增加引用 | +2 行 |
| ⑤ OnError | engine.md §十 | 增加引用 | +4 行 |
| ⑥ 执行流程 | derivation.md §六 | 替换表 | -5 行 |
| ⑦ 计数器 | engine.md §六 Decide | 压缩注释 | -3 行 |
| ⑧ 阈值 | rules.md §5.1 | 修改引用 | -2 行 |

**总计**：减少约 45-50 行重复内容，增加约 30 行 `[CR]` 引用标记。

### 风险控制

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| `[CR]` 引用断裂（文件改名/章节移动） | 低 | `git grep` 可在合并前检查 |
| 独立可读性下降 | 中 | 每个文件保留摘要+差异，`[CR]` 是补充不是替代 |
| 实施中断导致部分文件已改部分未改 | 中 | 按步骤逐个提交，每个步骤独立可回滚 |
| 引擎视角的 L0-ENG 概念已被广泛应用 | 低 | 全局搜索 L0-ENG/L1-ENG/L2-ENG/L3-ENG，确认无外部引用 |

---

## 十、实施顺序

```
步骤零：[CR] 格式规范（规则定义先行）
  │
  ├── 步骤一：层级定义去重（P0，影响面最大）
  │   ├── engine.md §一
  │   ├── spec.md §1.1
  │   ├── rules.md §1.1
  │   └── derivation.md §八
  │
  ├── 步骤二：公理派生去重（P1，改动点最多）
  │   ├── engine.md §4.1 + §五
  │   └── rules.md 各策略引用
  │
  ├── 步骤三：错误枚举统一（P1）
  │   ├── project.md §1.6
  │   └── engine.md §十
  │
  ├── 步骤四：OnError 去重（P2）
  │   └── engine.md §十 公共分支引用
  │
  ├── 步骤五：执行流程去重（P2）
  │   └── derivation.md §六
  │
  ├── 步骤六：计数器去重（P2）
  │   └── engine.md §六 Decide
  │
  └── 步骤七：阈值去重（P3）
      └── rules.md §5.1
```

---

## 附录：`[DR]` 设计理由标记的规范化

在层级去重中将 engine.md 的"工程简化说明"标记为 `[DR]`，因此需要在 `rules.md §1.2` 中补充 `[DR]` 的使用规范：

```markdown
| `[DR]` | 设计理由 | 记录设计决策的原理依据 | 原理说明 | 无（元数据） |

> 使用约定：`[DR]` 前缀的段落实质是"设计理由声明"，标记在段落开头表示"这段内容是对设计决策的解释，不是公理或规范"。
> 在 axioms.md 中设计理由存储于 `---meta` 块的 `dr` 键；在 engine.md/rules.md 等工程文件中，设计理由用 `[DR]` 前缀内联标注。