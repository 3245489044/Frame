# Agent 标准化（模板传递链）设计文档

> **日期**：2026-08-12 | **层级**：数据定义层（Agent 数据标准化语义）| **状态**：已落地（Phase A/B/B+）
> **归属**：独立项目 `Agent.Standardization`；通用 DSL 解析器保持平台层 `Plate.DslMethod`。

---

## 1. 概念：什么是「标准化」

「标准化」= **模板传递链**，一条由 4 个环节构成的确定性数据契约管线：

| # | 环节 | 落点 | 性质 |
|---|------|------|------|
| ① | 单一事实源 DSL | `Agent.Standardization/Dsl/storage-template.dsl.md` | Agent 专属数据契约 |
| ② | 编译 / 校验 / 生成器 | `Agent.Standardization/Compiler/`（`StorageTemplateModel`/`Registry`/`CodeGenerator`） | 语义映射 + 编译 |
| ③ | 编译产物 | `Agent.Standardization/generated/`（`storage-template.generated.json` + `-report.txt`） | 纯编译产物 |
| ④ | 运行时路由 | `Agent.Standardization/Runtime/StorageTemplateStore.cs` | 按 category 命中模板直接落库 |

> **核心语义**：不做「有价值/无价值」分类动作——命中模板即按契约落库；未命中 / 表空 / `mode=none` 即不入库。确定性、框架级、非 AI 判定。

### 1.1 链的本体 = 4 环节，不是 4 个散件
本设计文档固化「标准化」这一命名与 4 环节归属。此前仓库只有「模板 DSL」这类非正式说法，且链的 4 环节横跨 2 个项目 / 2 层：模型/生成器在 `Plate.DslMethod.Schema`、产物与运行时在 `Agent.Persistence`——无归属、靠传递引用才编译通过。本重构将其收进独立项目，且通用解析器留在平台层。

---

## 2. 两条链的分离处理

仓库存在两条「同构」的模板传递链，**消费方不同 → 归属约束不同，不可一刀切**：

| 链 | 消费方 | 归属约束 | 处置 |
|---|---|---|---|
| `ReferenceSchema`（`[Node]`/`[Edge]`/`[Constraint]`） | `Operation.DataStoreDriven`（Agent 无关通用层） | 模型/生成器必须在平台层 `Plate.DslMethod.Schema`；若挪进 Agent 项目会造成 `DataStoreDriven → Agent` 反向依赖 | **保持平台层**；其 DSL/产物与运行时代码同层收拢至 `CsharpPrj/Operation/DataStoreDriven/ReferenceSchema` |
| `StorageTemplate`（`[Template]`） | 仅 `Agent.Persistence` + 测试（Agent 专属） | 可安全收进 Agent 标准化项目 | **收进 `Agent.Standardization`**（本重构） |

### 依赖方向（无环，单向）
```
Agent.Standardization → Plate.DslMethod          （通用解析器，Agent 无关，本层消费不改动）
Agent.Persistence     → Agent.Standardization    （持久化层消费路由）
Operation.DataStoreDriven → Plate.DslMethod      （ReferenceSchema 运行时/模型，Agent 无关闭环）
```

---

## 3. 目标结构

```
Agent.Standardization/            （Agent 数据标准化层，模板传递链唯一归属）
├─ Abstractions/                  IStandardizationCompiler / IStandardizationRegistry / IStandardizationRouter
│                                + StandardizationCompilerOptions / StandardizationGenerationResult
├─ Dsl/                           storage-template.dsl.md（单一事实源）
├─ Compiler/                      StorageTemplateModel / Registry / CodeGenerator（从 Plate.DslMethod.Schema 迁入）
├─ Runtime/                       StorageTemplateStore（从 Agent.Persistence/Core 迁入）
├─ generated/                     storage-template.generated.json / -report.txt（纯编译产物）
└─ 依赖: Plate.DslMethod（通用解析器）｜Agent.Persistence → Agent.Standardization（持久化层消费路由）
```

### 命名空间
- 抽象契约：`Agent.Standardization.Abstractions`
- 编译层：`Agent.Standardization.Compiler`（`StorageMode`/`StorageDecay`/`RetentionSpec`/`StorageTemplate`/`StorageTemplateCollection`/`StorageTemplateRegistry`/`StorageTemplateCodeGenerator`）
- 运行时：`Agent.Standardization.Runtime`（`StorageDataPoint`/`StorageDirective`/`StorageTemplateStore`）

---

## 4. 分阶段落地记录

| 阶段 | 内容 | 风险 |
|---|---|---|
| A | 固化概念：本设计文档定义「标准化」= 4 环节 + 命名 | 无 |
| B | 独立 `Agent.Standardization` 项目：迁 StorageTemplate 链（DSL/模型/生成器/注册表/产物/`StorageTemplateStore`）；`Plate.DslMethod` 仅保留 `[Template]` STYP 前缀注册（`DslConstants`）；`Agent.Persistence` 改为引用它 | 低（新代码、单消费方；测试随迁） |
| B+ | ReferenceSchema 一致性：因 DataStoreDriven 依赖，不迁进 Agent 项目；其 DSL/产物与运行时代码同层收拢至 `Operation.DataStoreDriven/ReferenceSchema`，并修复 README 过时描述与破损测试 | 低 |

### 产物再生成约束（重要）
编译产物内含**绝对路径 `SourceFile`**（指向 DSL 源文件）。迁移后 DSL 位置改变 ⇒ 产物必须**重新生成**（不能直接搬文件），且生成时传给生成器的 DSL 路径**大小写/分隔符须与运行时消费方一致**（本仓库测试经 `RepoRoot()` 取 `D:\...` 大写盘符）。`Agent.Standardization` 的产物当前以 `D:\Project\Program\WorkFlowPrj\Agent\src\Agent.Standardization\Dsl\storage-template.dsl.md` 记录，与 `StorageTemplateDslTests.Regenerate_提交DSL_可重生成且校验通过` 的逐字节断言对齐。

---

## 5. 消费方与验证

- **Agent.Persistence**：经 `ProjectReference` 引用 `Agent.Standardization`，消费 `Runtime.StorageTemplateStore` 路由。
- **测试**：`CentralTest/AgentTest/StorageTemplate.Tests/StorageTemplateDslTests.cs`（11 例，随迁至新项目引用）；`CentralTest/AgentTest/ReferenceSchema.Tests/ReferenceSchemaDslTests.cs`（11 例，改为引用 `DataStoreDriven`）。
- **验证命令**：`dotnet build Agent/Agent.sln`；`dotnet build CsharpPrj/MainProj.sln`；`dotnet test CentralTest/AgentTest/StorageTemplate.Tests`；`dotnet test CentralTest/AgentTest/ReferenceSchema.Tests`。

---

## 6. 关键约束（防回归）

1. `Plate.DslMethod` 的 `DslConstants.cs` 保留 `DslSymbolKind.Template` 与 `[Template]` 前缀注册——通用解析器需继续识别该符号（对齐 clinerules `dsl.md §4.5`「新增 STYP = 增条目，文法不变」）。
2. `ReferenceSchema` 链的模型/生成器（`Plate.DslMethod.Schema.ReferenceSchema*`）保持平台层不动。
3. 产物为【纯编译产物】，禁止手改；变更须作用于 DSL 源后重生成。
