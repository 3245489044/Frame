# Agent.Core 与公理对应关系重构文档（DSL 框架 + 公理代码库）

> 日期：2026-08-09 | 范围：`CsharpPrj/Plate/DslMethod`、`Agent/src/Agent.Core`、`Agent/Config/prompts`
> 关联：clinerules（axioms.md / formula.md / logic.md / dsl.md）、`WorkFlowMethod.LoopFramework.Axioms`
> 方案来源：实现↔公理对应散落问题分析与 `Plate.DslMethod` DSL 框架（见下）

---

## 1. 重构目标

| # | 目标 | 对应问题 |
|---|---|---|
| 1 | 实现↔公理对应收敛到单一可机检的绑定层 | 对应散落在代码注释 / `loop-axioms §七` 表格 / 各文件头注，互不互指 |
| 2 | 公理成为编译期一等代码（一条公理一个 `.cs`、一个层级一个文件夹） | 公理仅存在于 markdown，无类型约束、无索引 |
| 3 | 公理代码库**反转**从 `Agent/Config/prompts` 契约生成 | 手写公理易与运行期契约漂移 |
| 4 | `Agent.Core` 各模块与公理建立显式、可校验绑定 | 边界框架等行为"只实现不声明"，C 类公理缺口 |
| 5 | 缺口/漂移/悬空可机检、可治理 | 散落对应无法 grep 汇总 |

---

## 2. 基线

- 构建命令：`dotnet build CsharpPrj\Plate\Plate.csproj -c Debug`
- 基线状态：**0 个警告 / 0 个错误**（`Plate.DslMethod` 已构建通过，2026-08-09 记录）
- 目标框架：`net10.0-windows`
- 测试项目：无正式单测项目；用临时冒烟工程验证后已清理

---

## 3. 阶段清单

| 阶段 | 内容 | 状态 |
|------|------|------|
| 0 | 基线锁定 + 本文档 | ✅ 本文档 |
| 1 | 构建 `Plate.DslMethod` DSL 框架（解析/索引/引用/矩阵） | ✅ 已完成 |
| 2 | 框架扩展代码公理层（`[Axiom]`/`IAxiom`/`AxiomScanner`/`[BindAxiom]`） | ☐ 待实施 |
| 3 | 公理代码库 `Plate/Axioms`（反转生成，一公理一文件、一层级一文件夹） | ☐ 待实施 |
| 4 | `Agent.Core` 实现类 `[BindAxiom]` 绑定 | ☐ 待实施 |
| 5 | 校验/缺口/漂移治理（复用 `ContractRegistry`） | ☐ 待实施 |

---

## 4. 现状问题：对应散落

| 载体 | 内容 | 缺陷 |
|---|---|---|
| 代码注释 | `AgentLoopConfig` 引 MAX_PROGRESS、`BoundaryTriggerArbiter` 引 GlobalDecisionKey | 不可机检、易漂移 |
| `loop-axioms.system.md §七` | 引擎组件↔公理表 | 仅引擎层、非符号 |
| `logic-axioms`/`system.prompt.dsl` | clinerules 抽取的 L0 | 只声明"抽取自"未声明"实现于" |
| `l1l2-generator.system.md` | L1/L2 协议 | `boundary_action` 枚举与代码 `expand_root` 漂移 |

---

## 5. 目标架构（两层：DSL 框架 + 公理代码库）

```
[权威来源] Agent/Config/prompts/*.md   （运行期契约）
      │  反转生成（DslParser → 层级映射 → 发射）
      ▼
[公理代码库] CsharpPrj/Plate/Axioms/   （一条公理一个 .cs，一个层级一个文件夹）
      │  AxiomScanner 反射索引
      ▼
[绑定层] Agent.Core 实现类 [BindAxiom(typeof(公理类), Kind)]
      │  ContractRegistry.Validate / FindGaps / BuildMatrix / Index.Reverse
      ▼
[治理] 悬空 / 缺口 / 漂移 清单
```

### 5.1 已实现：`Plate.DslMethod`（阶段1）

| 层 | 文件 | 职责 |
|---|---|---|
| 定义 | `DslConstants.cs` | 前缀注册表 + 符号类型 + 契约维度（module/axis/kind/status/layer） |
| 定义 | `Model/DslModel.cs` | `DslReference`/`DslDeclaration`/`DslDocument`/`BindingDeclaration` |
| 解析 | `Parsing/CrReferenceParser.cs` | `[CR]` 交叉引用解析（含旧形态兼容） |
| 解析 | `Parsing/FieldParser.cs` | `k=v` 结构化字段提取 |
| 解析 | `Parsing/DslParser.cs` | 逐行 DSL 声明/章节/fenced 块/续行解析 |
| 逻辑 | `Logic/DslIndex.cs` | 符号索引（`lib::STYP::name`）+ `[CR]` 解析 + 反向引用 |
| 逻辑 | `Logic/ContractRegistry.cs` | `[Bind]` 提取 + 校验 + 分组 + 缺口 + 矩阵 |
| 逻辑 | `Logic/ModuleAxisMatrix.cs` | 模块×轴矩阵 + 空单元格 |
| 逻辑 | `Logic/ValidationIssue.cs` | 校验问题载体 |
| 门面 | `DslFramework.cs` | `ParseText`/`ParseFile`/`BuildRegistry` |

**已验证**（冒烟工程，后清理）：解析 3 文档 / 139 声明 / 5 绑定 / 悬空 target 正确判出 / 36 矩阵缺口 / 反向引用回链。

### 5.2 待实现：代码公理层（阶段2-3）

| 新增 | 位置 | 职责 |
|---|---|---|
| `AxiomLevel` 枚举 / `IAxiom` / `[Axiom]` / `[BindAxiom]` | `Plate.DslMethod/Abstractions` | 公理元数据与绑定特性 |
| `AxiomScanner` | `Plate.DslMethod/Logic` | 反射扫描程序集 → 公理索引 + 绑定 |
| `AxiomCodeGenerator` | `Plate.DslMethod/CodeGen` | `prompts/*.md` 反转生成 `Axioms/{Level}/{Name}.cs` |
| `Plate/Axioms` 项目 | `CsharpPrj/Plate` | 公理代码库（一公理一文件、一层级一文件夹） |

---

## 6. 文件分类对应（Agent.Core 文件 → 模块簇 → 公理）

| 模块簇 | 目录 | 轴 | 代表公理绑定 |
|---|---|---|---|
| `agent.core.loops` | `Loops/` | termination/error/round | `RoundLifecycle`/`TerminationPriority`/`EvaluationOutcomeSet`/`DecisionOutputSet`/`ClassifyError` |
| `agent.core.boundaries` | `Boundaries/` | boundary | `BoundaryPressureModel`/`BoundaryTriggerArbiter`/`SpatialBoundary`（**需新增公理**） |
| `agent.core.prompts` | `Prompts/` | output | `OutputWellFormed`/`FormatFidelity`/`FirstCallTransient`（guard/implement） |
| `agent.core.infrastructure` | `Diagnostics/` `Infrastructure/` `SemanticKernel/` | infra/error | `InformationMonotonicity`/`SingleImplementation`/`StratifiedReflectionSchema` |

**典型绑定示例**
```
[BindAxiom(typeof(Plate.Axioms.L1.RoundLifecycle), Kind=Implement)] WorkflowRunner
[BindAxiom(typeof(Plate.Axioms.L1.TerminationPriority), Kind=Implement)] AgentLoopConfig
[BindAxiom(typeof(Plate.Axioms.L1.EvaluationOutcomeSet), Kind=Implement)] LoopDecisionMapper
[BindAxiom(typeof(Plate.Axioms.L2.SpatialBoundary), Kind=Implement, Status=Gap)] SpatialBoundaryEvaluator
```

---

## 7. 反转生成：层级映射规则

| prompts 文件 | 归入层级 | 依据 |
|---|---|---|
| `system.prompt.dsl.md` | L0 | 全文件进程级常量 |
| `loop-axioms.system.md` | L1 | 引擎循环层 |
| `logic-axioms.system.md` `## B1 L0 CORE` / `## B2 L0 BAX` | L0 | 节标题 |
| `logic-axioms.system.md` `## A0–A3`（LO 算子） | L1 | 引擎运算 |
| `logic-axioms.system.md` `## B5 领域公理` | L2 | 节标题 |
| `logic-axioms.system.md` `## B6 元级指导`（Strategy） | L2 策略 | 节标题 |
| `logic-axioms.system.md` `## B7 配置参数（L3）`（`%`） | L3 | 节标题 |
| `l1l2-generator.system.md` | L0.5（输出）+ L1/L2（提示词） | 输出层 |
| 任意 `%` 行 | L3 | 配置参数 |

映射以**节标题为主、文件缺省为辅**逐声明确定，并产出映射表供审查。

---

## 8. 绑定机制

- **载体**：`Agent.Core` 实现类 `[BindAxiom]`（编译期、推荐）；可保留 markdown `[Bind]` 契约文件双通道。
- **种类**：`implement`（代码实现）/ `derive`（prompts 契约渲染）/ `guard`（校验器守卫）。
- **状态**：`aligned` / `gap`（缺公理）/ `drift`（漂移）。

---

## 9. 校验与治理

| 关注点 | 框架能力 |
|---|---|
| 悬空对应 | `ContractRegistry.Validate()`（target 不可解析） |
| 缺口 | `FindGaps()`（module×axis 空单元格） |
| 漂移 | `FindByStatus("drift")`（如 `RoundStepInvoker`、`AgentRunCancellation`） |
| 反向追溯 | `Index.Reverse`（改公理→谁实现） |
| 单一权威 | 对应只登记在 `[BindAxiom]`/契约文件，删除散注 |

---

## 10. 风险与注意

- 反转生成的公理代码库与 prompts 契约需**同一提交原子变更**，避免漂移。
- `boundary_action` 枚举（L2 提示词）与代码 `expand_root` 存在漂移，需在契约层声明归属空间边界。
- `Plate/Axioms` 生成文件建议**提交入库**（静态产物，可审查），而非构建期动态生成，避免构建副作用。

---

## 11. 后续待确认

- 公理库独立项目 `Plate/Axioms` vs `DslMethod/Axioms` 子命名空间。
- 边界公理归 `L2` 还是独立 `Boundary/` 文件夹。
- 首批生成范围（与 Agent.Core 直接相关 vs 全量四文件）。
- 绑定载体（`[BindAxiom]` 特性 vs markdown `[Bind]`，或双通道）。
