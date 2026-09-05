# `core.md` 通用逻辑提取分析报告

> 分析日期：2026-07-24
> 分析范围：`clinerules/core.md`（401行）、`clinerules/project.md`（117行）、`clinerules/guides.md`（75行）
> 分析目标：识别 `core.md` 中可提取为通用逻辑的内容，降低项目特定耦合，提升规则库的可复用性

---

## 目录

1. [已实现的分离模式（现状评估）](#1-已实现的分离模式现状评估)
2. [P0 - 必须提取的元级通用逻辑](#2-p0---必须提取的元级通用逻辑)
3. [P1 - 应提取的通用逻辑](#3-p1---应提取的通用逻辑)
4. [P2 - 可提取的通用逻辑](#4-p2---可提取的通用逻辑)
5. [P3 - 建议提取的通用逻辑](#5-p3---建议提取的通用逻辑)
6. [提取方案总览](#6-提取方案总览)
7. [附录：当前架构的亮点](#7-附录当前架构的亮点)

---

## 1. 已实现的分离模式（现状评估）

在分析"可提取"内容之前，先肯定已做好的分离设计：

### 1.1 规则逻辑 vs 项目配置 已分离

`project.md` 已从 `core.md` 拆出，`core.md` 中的通用规则通过 `→ project.md §一 [配置名]` 引用项目配置值，实现了：

| 配置项 | 引用规则 | 当前状态 |
|--------|----------|----------|
| 文件处理阈值（SMALL/LARGE） | ReadMode / DataProcessMode | ✅ 已分离 |
| 执行环境配置（timeout/rate_limit/retry） | ExecConfig | ✅ 已分离 |
| 废弃策略（firstTimeAction/retryAction） | ConflictStrategy | ✅ 已分离 |
| 路径格式配置 | PathStrategy | ✅ 已分离 |
| 异常类型映射 | ThrowRule | ✅ 已分离 |
| 修复优先级映射 | FixPriority | ✅ 已分离 |
| WellFormed 评估阈值 | WellFormed | ✅ 已分离 |

### 1.2 工具映射已分离

`guides.md` 承载了工具名映射、场景→工具链映射表，`core.md` 通过引用指向 `guides.md`，避免了规则本体与具体工具名的耦合。

### 1.3 反模式已集中管理

`core.md` §四 将所有反模式集中管理，各章节通过引用链接指向此处，避免了重复定义。

---

## 2. P0 - 必须提取的元级通用逻辑

### 2.1 规则分类体系 → 提取为 `RuleMetaModel` 元规则

**当前位置**：`core.md` 头部注释（第1-4行）

**问题**：规则分类体系（公理→策略→规范→反模式→流程）和公式类型（`[TD]`/`[CM]`/`[CD]`/`[SM]`/`[QA]`/`[CC]`）目前只在文件头部用注释说明，未作为可引用的规则定义。这意味着：

- 分类体系无法被其他规则文件引用
- 公式类型的语义没有形式化定义
- 层级间的引用规则（如"策略可引用公理，不可修改公理"）是隐式约定

**提取方案**：

```markdown
### RuleMetaModel [元规则]
> 规则引擎的元模型，定义规则的分类体系、公式类型和层级间引用规则。

#### 规则分类层级
| 层级 | 性质 | 可变性 | 职责 | 可引用 |
|------|------|--------|------|--------|
| **公理** (Axiom) | 原子原则 | 不可变 | 声明"是什么" | 不依赖其他规则 |
| **策略** (Strategy) | 操作决策 | 可变 | 定义"怎么做" | 可引用公理 |
| **规范** (Standard) | 约束条件 | 不可变 | 定义"必须/禁止" | 可引用公理、策略 |
| **反模式** (Anti-pattern) | 负面模式 | 可变 | 集中管理已知错误 | 可引用公理、策略、规范 |
| **流程** (Procedure) | 步骤序列 | 可变 | 定义"按什么顺序" | 可引用策略、规范 |

#### 公式类型定义
| 标识 | 名称 | 语义 | 输出类型 |
|------|------|------|----------|
| `[TD]` | 阈值决策 (Threshold Decision) | 基于阈值比较输出决策 | 枚举值 |
| `[CM]` | 分类映射 (Classification Mapping) | 输入→类别的映射 | 类别标签 |
| `[CD]` | 条件决策 (Conditional Decision) | 基于条件分支输出决策 | 枚举值 |
| `[SM]` | 序列映射 (Sequence Mapping) | 输入→有序步骤的映射 | 步骤列表 |
| `[QA]` | 质量评估 (Quality Assessment) | 基于标准输出质量等级 | pass/warn/fail |
| `[CC]` | 成本计算 (Cost Calculation) | 基于参数输出量化成本 | 数值 |
```

### 2.2 配置注入模式 → 提取为 `ConfigInjection` 机制

**当前位置**：`core.md` 中隐式约定（多处使用 `→ project.md §一 [配置名]`）

**问题**：规则参数化的机制是隐式约定，没有形式化定义：

- 参数来源只有 `project.md`，没有考虑环境变量、运行时参数等来源
- 参数解析优先级未定义
- 参数类型校验规范缺失
- 参数缺失时的兜底行为未定义

**提取方案**：

```markdown
### ConfigInjection [元机制]
> 规则参数化注入机制，实现"规则逻辑与配置值分离"。

#### 参数来源优先级
1. **运行时参数**（调用时显式传入）→ 最高优先级
2. **项目配置**（project.md 中声明）→ 次高优先级
3. **环境变量**（系统环境变量）→ 第三优先级
4. **默认值**（规则中硬编码的兜底值）→ 最低优先级

#### 参数引用语法
- `→ project.md §一 [配置名]`：引用项目配置文件中的配置值
- `→ env:ENV_VAR_NAME`：引用环境变量
- `→ default:value`：声明默认值

#### 参数缺失处理
- 必需参数缺失 → 规则降级为 warn 模式，记录配置缺失警告
- 可选参数缺失 → 使用默认值，静默处理
- 类型不匹配 → 记录配置错误，使用默认值兜底
```

---

## 3. P1 - 应提取的通用逻辑

### 3.1 后处理推导链 → 提取为 `ExecutionPipeline` 通用执行模型

**当前位置**：`core.md` §二 `ToolStrategy` 内嵌（第121-125行）

**问题**：后处理推导链 `execute(toolchain) → Δ → HistoricalIntegrity → persist_reasoning → StateValidity → valid` 目前只写在 `ToolStrategy` 中，但这是**所有工具链执行的通用后处理模式**，不应只属于某一个策略。

**提取方案**：

```markdown
### ExecutionPipeline [SM]
> 通用执行管道，所有工具链共享同一后处理流程。

#### 管道定义
`Input(task, toolchain) → [前置检查] → [执行] → [后处理] → Output(result, Δ)`

#### 前置检查（Pre-flight）
1. `StateValidity(cognitiveState, worldState)` → 检查认知状态有效性
2. `IdempotencyCheck(current, last, fileHash)` → 检查操作幂等性
3. `BudgetAlloc(phase)` → 分配上下文预算

#### 执行（Execution）
- 按 `Toolchain(scene)` 定义的步骤顺序/并行执行
- 遇到错误按 `OnError` 策略处理

#### 后处理（Post-processing）
1. `Δ = computeDelta(cognitiveState, worldState)` — 计算变更
2. `HistoricalIntegrity(Δ)` — 验证历史完整性
3. `persist_reasoning(Δ)` — 持久化推理轨迹（Why）
4. `record_knowledge(Δ)` — 记录知识（create_entities/note）
5. `StateValidity(postState)` — 验证变更后状态
6. `→ valid` — 管道完成
```

### 3.2 路径规范通用部分 → 合并到 `PathStrategy`

**当前位置**：`project.md` §二（第76-81行）

**问题**：`project.md` 中的路径规范包含通用原则（绝对路径、禁止 cd、兜底策略），这些原则不限于 WorkFlowPrj 项目，应属于通用规则。

**当前 `project.md` §二 内容**：
```
- ⚠️ filesystem MCP 必须使用绝对路径
- ⚠️ 禁止使用 cd 切换目录
- 路径解析失败时：尝试使用项目根目录作为前缀
- ctx_execute 中的文件路径：使用双反斜杠或正斜杠
```

**提取方案**：

将通用原则提取到 `core.md` 的 `PathStrategy` 中：

```markdown
#### PathStrategy(execEnv, projectConfig) [CM]
路径格式。`execEnv → projectConfig.path.format[execEnv]`

##### 通用路径原则（跨项目适用）
1. **绝对路径原则**：所有文件操作必须使用绝对路径，禁止依赖相对路径或工作目录
2. **禁止 cd**：禁止使用 `cd` 切换目录，所有 CLI 命令必须使用绝对路径
3. **兜底策略**：路径解析失败时，尝试使用 `projectConfig.path.root` 作为前缀
4. **沙箱转义**：sandbox 环境中的路径使用双反斜杠 `\\` 或正斜杠 `/`
```

`project.md` §二 简化为：
```markdown
## 二、路径规范
> 通用路径原则参见 `core.md` §二 [PathStrategy]。
> 以下为项目特定配置。

- **项目根目录**：`d:\Project\Program\WorkFlowPrj\`
- **路径格式配置值**：参见 §一 [1.4 路径格式配置]
```

---

## 4. P2 - 可提取的通用逻辑

### 4.1 通用流程模板实例 → 提取到 `core.md` §五

**当前位置**：`project.md` §六（第104-117行）

**问题**：`project.md` §六 的"日志添加流程"是 `core.md` §五 `ProcedureTemplate` 的项目实例，但 `core.md` §五 目前只有 `ProcedureTemplate` 的骨架定义，没有提供任何通用流程模板实例。

**提取方案**：

在 `core.md` §五 中预定义一组通用流程模板：

```markdown
### 通用流程模板库

#### LogAddFlow [SM]
> 日志添加通用流程。项目通过参数化配置实例化。
- 参数：`{layerOptions, apiOptions, prohibitedContexts, idFormat, registryPath}`
- 步骤：
  1. `identify_layer`：确定日志所属层（→ layerOptions）
  2. `select_api`：选择日志 API（→ apiOptions）
  3. `check_prohibited`：检查是否在禁止日志的上下文中（→ prohibitedContexts）
  4. `name_event`：命名事件
  5. `name_message_id`：分配消息 ID（→ idFormat）
  6. `register_log`：在日志注册表中登记（→ registryPath）
  7. `add_initialization`：在模块初始化代码中注入日志依赖

#### FileDeprecateFlow [SM]
> 文件废弃通用流程。项目通过参数化配置实例化。
- 参数：`{deprecatedDir, excludePattern, cleanCondition}`
- 步骤：
  1. `check_references`：检查文件是否有引用
  2. `move_to_deprecated`：移动到废弃目录（→ deprecatedDir）
  3. `exclude_from_build`：从构建中排除（→ excludePattern）
  4. `verify_no_impact`：验证无影响
  5. `schedule_cleanup`：安排清理（→ cleanCondition）
```

`project.md` §六 简化为参数化配置：

```markdown
## 六、项目级流程实例化

### 6.1 日志添加流程（LogAddFlow 实例）
> 通用模板参见 `core.md` §五 [LogAddFlow]。

| 参数 | 值 |
|------|-----|
| `layerOptions` | Business / Execution / Utility |
| `apiOptions` | ILogger / LogHelper / DiagnosticSource |
| `prohibitedContexts` | 热路径、循环体 |
| `idFormat` | `{模块缩写}_{3位数字}` |
| `registryPath` | `docs/LogRegistry/` |
```

### 4.2 废弃文件规范 → 提取为 `DeprecationTemplate`

**当前位置**：`project.md` §三（第83-88行）

**问题**：废弃文件规范（`_deprecated/` 目录、编译排除、清理时机）是 C# 项目的通用模式，其他项目类型（TypeScript、Python）也有类似需求，但实现方式不同。

**提取方案**：

在 `core.md` 中定义语言无关的废弃模板：

```markdown
### DeprecationTemplate [SM]
> 文件废弃通用模板。项目通过参数化配置实例化。

#### 参数
| 参数 | 类型 | 说明 |
|------|------|------|
| `deprecatedDir` | string | 废弃文件存放目录（默认 `_deprecated/`） |
| `excludeMethod` | enum | 排除方式：`compile_exclude` / `runtime_ignore` / `module_exclude` |
| `excludeConfig` | object | 排除配置（语言特定） |
| `cleanTrigger` | enum | 清理触发条件：`no_references` / `manual_review` / `time_based` |

#### 通用步骤
1. `move_to_deprecated`：将文件移动到 `{moduleDir}/{deprecatedDir}/`
2. `apply_exclusion`：根据 `excludeMethod` 应用排除配置
3. `verify_isolation`：验证废弃文件不影响正常构建/运行
4. `register_cleanup`：注册清理条件（→ cleanTrigger）
```

`project.md` §三 简化为：

```markdown
## 三、废弃文件规范
> 通用废弃模板参见 `core.md` §五 [DeprecationTemplate]。

| 参数 | 值 |
|------|-----|
| `deprecatedDir` | `_deprecated/` |
| `excludeMethod` | `compile_exclude` |
| `excludeConfig` | `<Compile Remove="_deprecated\**\*" />` + `<None Include="_deprecated\**\*" />` |
| `cleanTrigger` | `no_references` |
```

### 4.3 `AmbiguityAxiom` 去 C# 化

**当前位置**：`core.md` §一（第28-30行）

**问题**：规则名直接包含 C# 编译器错误号 `CS0104`，`extract_to_TConfig` 也是 C# 特定模式（TConfig = TypeScript/Type Configuration？）。

**当前内容**：
```
#### AmbiguityAxiom(semantics, phase) [CD]
CS0104 歧义引用处理。`same → extract_to_TConfig` · `different ∧ transition → using_alias` · `different ∧ migration_done → remove_alias`
```

**提取方案**：

```markdown
#### AmbiguityAxiom(semantics, phase) [CD]
同名歧义引用处理（如 C# CS0104、TypeScript 同名导入冲突等）。
`same → extract_to_shared_config` · `different ∧ transition → use_alias` · `different ∧ migration_done → remove_alias`
- semantics∈{same, different}（冲突语义是否相同）
- phase∈{transition, migration_done}（迁移阶段）
- 示例：C# 中两个命名空间导出同名类型 → 提取到共享配置；过渡期使用 using 别名 → 迁移完成移除别名
```

---

## 5. P3 - 建议提取的通用逻辑

### 5.1 `ConflictStrategy` 动作抽象化

**当前位置**：`core.md` §二（第108-113行）

**问题**：`deprecationConfig.firstTimeAction` 的值 `move_deprecated_and_exclude` 隐含了 C# 的编译排除机制，其他语言可能使用不同的排除方式。

**提取方案**：

将动作值定义为抽象动作，语言特定实现在 `project.md` 中配置：

```markdown
#### ConflictStrategy(hasReference, isFirstTime, deprecationConfig) [CD]
文件冲突处理策略（遵循 DeprecateAxiom 的原子原则）。
`¬hasReference → delete`
`hasReference ∧ isFirstTime → deprecationConfig.firstTimeAction`
`hasReference ∧ ¬isFirstTime → deprecationConfig.retryAction`

##### 抽象动作定义
| 抽象动作 | 语义 | C# 实现 | TypeScript 实现 | Python 实现 |
|----------|------|---------|-----------------|-------------|
| `move_and_exclude` | 移动+排除 | `_deprecated/` + `<Compile Remove>` | `_deprecated/` + tsconfig exclude | `_deprecated/` + `__init__.py` 排除 |
| `deprecate_in_place` | 原地废弃 | `[Obsolete]` 特性 | `@deprecated` JSDoc | `@deprecated` docstring |
| `delete` | 直接删除 | 删除文件 | 删除文件 | 删除文件 |
```

---

## 6. 提取方案总览

### 6.1 优先级与工作量

| 优先级 | 提取内容 | 当前所在 | 目标位置 | 工作量 | 影响范围 |
|--------|----------|----------|----------|--------|----------|
| **P0** | RuleMetaModel 元规则 | `core.md` 头部注释 | `core.md` §〇 元规则 | 小 | 新增，无破坏性 |
| **P0** | ConfigInjection 机制 | 隐式约定 | `core.md` §〇 元规则 | 小 | 新增，无破坏性 |
| **P1** | ExecutionPipeline | `ToolStrategy` 内嵌 | `core.md` §二 策略 | 中 | 重构 ToolStrategy |
| **P1** | 路径规范通用部分 | `project.md` §二 | `core.md` §二 PathStrategy | 小 | 修改 project.md |
| **P2** | 通用流程模板库 | `project.md` §六 | `core.md` §五 流程 | 中 | 新增 + 修改 project.md |
| **P2** | DeprecationTemplate | `project.md` §三 | `core.md` §五 流程 | 中 | 新增 + 修改 project.md |
| **P2** | AmbiguityAxiom 去 C# 化 | `core.md` §一 | `core.md` §一 | 小 | 修改规则名和逻辑 |
| **P3** | ConflictStrategy 抽象化 | `core.md` §二 | `core.md` §二 | 中 | 修改策略 + project.md |

### 6.2 建议的实施顺序

```
Phase 1（无破坏性新增）：
  ├── RuleMetaModel 元规则 → core.md §〇
  └── ConfigInjection 机制 → core.md §〇

Phase 2（重构 + 提取）：
  ├── ExecutionPipeline 从 ToolStrategy 提取
  ├── 路径规范通用部分从 project.md 合并
  └── AmbiguityAxiom 去 C# 化

Phase 3（新增模板）：
  ├── 通用流程模板库 → core.md §五
  └── DeprecationTemplate → core.md §五

Phase 4（抽象化改造）：
  └── ConflictStrategy 动作抽象化 + project.md 配置更新
```

---

## 7. 附录：当前架构的亮点

在分析过程中，也发现 `core.md` 当前设计的一些值得肯定的亮点：

### 7.1 元阈值豁免声明（MetaThresholdExemption）

第57-59行的元阈值豁免声明是一个精巧的**自指悖论防护**机制，明确区分了"规则引擎的运行参数"和"业务代码中的魔法数字"，避免了规则引擎陷入"规则本身违反规则"的困境。

### 7.2 死锁防护（StateRecovery）

第189行的死锁防护（`full_reset` 分支）预见了"恢复失败→状态无效→尝试恢复"的死循环，通过清空一切并请求用户确认来打破死锁。这是状态管理中的经典问题，设计考虑周全。

### 7.3 三定律联合评估模式

`WellFormed`（语义自足 + 结构正交 + 表达克制）和 `StateValidity`（内部一致 + 外部一致 + 历史完整）都采用了"三定律联合评估"模式，这种模式具有很好的通用性，可提取为 `TripleLawAssessment` 通用评估框架。

---

> **本文档为分析报告，如需实施上述提取方案，请参考各章节的"提取方案"部分进行具体修改。**
