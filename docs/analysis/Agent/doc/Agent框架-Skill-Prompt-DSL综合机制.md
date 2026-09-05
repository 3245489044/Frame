# Agent 框架 Skill / Prompt / DSL 综合机制

> **版本**：1.2　**日期**：2026-08-29
> **范围**：`Projects/Agent`（`Agent.Skill` / `Agent.Axioms` / `Agent.Core` / `Agent.Operation` / `Agent.Runtime` / `Agent.Launcher`）
> **性质**：现状综合梳理（基于源码取证，非实施变更）
> **v1.1 修订**：提示词 DSL 收敛为 **Skill 唯一真相** —— 删除 `Config/prompts/*.system.md`；L0/L1/L2/Format 一律经 `PromptDslSource` 从 DSL 承载技能（`dsl: format/l0-system/l0-loop/l0-logic/protocol`）装配；拆除 `prompt-l0` 双 L0 覆盖；`boundaries` 迁入 `skills/rare/boundaries`；`AxiomCodeGenerator` 改读 skill 正文（构建期契约源）
> **v1.2 修订**：**统一 DSL 解析器 + 多投影器** —— `PromptDslParser`/`FormatSpecParser` 已清理移除；L0/L1/L2/Format 一律由 `Plate.DslMethod.DslParser` 唯一解析，再经 `L0Projector`/`L1L2Projector`/`FormatProjector` 投影为各消费模型；`Agent.Operation` 新增对 `DslMethod` 的依赖，`PromptCatalog` 三入口统一走 DslParser + 投影器（详见 `docs/analysis/Agent/refactor/2026-08-29-统一DSL解析器与双模型投影方案.md`）
> **关联**：`docs/analysis/2026/8/8.29/骨架五步信号驱动收敛-重构方案.md`、`信号驱动SK会话主循环整合方案.md`、`AI交互标准上下文三粒度重构方案.md`

---

## 一、总览与分层

Agent 框架的提示词体系由**三块耦合的子系统**构成：

| 子系统 | 职责 | 核心载体 |
|--------|------|----------|
| **Skill（技能）** | 外部可挂载的操作指南 / 契约说明书 / 工具面 / DSL 承载，统一装配入口 | `Agent.Skill` + `SkillCatalog` + `SkillInjector` |
| **Prompt（提示词）** | L0/L1/L2 三层提示词的分层装配、生成、校验、填槽 | `PromptCatalog` + `PromptFrameworkFunction` + `PromptDslSource` |
| **DSL（声明语法）** | 提示词文本的两种声明文法（clinerules 声明 DSL / 提示词协议 DSL），供模型读与引擎解析（统一解析器 + 多投影器） | `skills/**/SKILL.md`（dsl: 承载）+ `Plate.DslMethod.DslParser` + `L0Projector`/`L1L2Projector`/`FormatProjector` |

**单向依赖（无环）**：

```
Agent.Core ──┐
Agent.Axioms ┼──▶ Agent.Skill（零外部依赖叶库）
Agent.Modules┘
Agent.Operation ─▶ Plate.DslMethod（统一解析 DslParser）；Agent.Axioms ─▶ Agent.Operation（投影器投影，PromptCatalog 供源）
Agent.Runtime ─▶ Agent.Skill（SkillRuntimeState 承载 SkillDefinition）
Agent.Core ─▶ Agent.Runtime（SkillRuntimeState.Hot 供 SkillInjector / PromptDslSource 消费）
Agent.Launcher ─▶ Agent.Axioms + Agent.Runtime（SkillCatalogLoader 装配）
```

---

## 二、Skill 体系

### 2.1 定位与程序集

`Agent.Skill`（`src/Agent.Modules/Skill/Agent.Skill.csproj`，命名空间 `Agent.Skill`）是技能协作**通用逻辑底座**（LLM-free、Agent 无关、零外部依赖）。原则：契约单一 · 执行端无关 · 单向无环（SingleImplementation / TH-12）。只承载「信封契约 / 执行端分发端口 / 技能选择装配」纯逻辑，不承载任何执行。

### 2.2 核心类型

| 类型 | 角色 |
|------|------|
| `SkillModule` | 技能归属模块枚举：`Internal / Mcp / Rare / L0`（分层裁决键） |
| `SkillDefinition` | 技能契约（Name/Description/Kind/Tags/Trigger/Executor/Schema/Body/Module/OnDemand/AlwaysOn/Backend/**Dsl**） |
| `SkillSelector` | 零 LLM 表驱动选择（`Select` / `SelectWithRare`） |
| `SkillPromptAssembler` | 技能块装配（注入 system 槽位） |
| `SkillInjector` | **技能统一装配入口**（输入守卫 + 选 skill + SkillBlock，单一口径（L0 由 PromptCatalog 单源装配）） |
| `InputSafetyGuard` | 输入安全纯函数（SQL/Shell 危险模式扫描） |
| `SkillsOptions` | 运行期配置（`Enabled=true` / `SkillsRelDir="skills"` / `MaxInjected=3`） |
| `IFoldInProcessExecutor` | 执行端分发端口（answer/cline 进程内直调） |
| `IDeterministicSkillExecutor` | 确定性模块执行端口（module: mcp + backend:） |
| `ConversationFoldVertex` | 信号折叠信封契约（stepKey/executor/output/chainEnded/error） |

### 2.3 模块分层

| 模块 | 语义 | 加载 | 选择 | 分发后端 | 代表 |
|------|------|------|---------|------|
| `Internal` | 契约说明书：正文与 C# 模型 1:1，零工具往返 | 热集 | 关键词自动匹配 | SK 聊天（正文即指导） | `signal-result` |
| `Mcp` | 包装外部 MCP / 本地确定性模块 | 热集 | 关键词 + 上限 | ①进程内确定性执行端→②`IFoldInProcessExecutor`/MCP→③SK 兜底 | `conversation-agent` |
| `Rare` | 指定场景才用（生命周期/生成器） | **懒集** | **仅显式 requestedNames** | SK 聊天指导 | `axiom-generator` |
| `L0` | L0 必写协议层（DSL 承载载体：l0-format/l0-system/l0-loop/l0-logic） | 热集 | 恒在（不进操作块，经 `PromptDslSource` 装配 L0） | `PromptCatalog` 单源拼接 | `l0-system` |

**缺省推断**（未声明 `module:`）：`kind==lifecycle → Rare`；`kind==l0 → L0`；`executor≠sk → Mcp`；否则 `Internal`。

### 2.4 目录结构与 front-matter

```
skills/
  internal/<name>/SKILL.md
  mcp/<name>/SKILL.md
  rare/<name>/SKILL.md
  l0/<name>/SKILL.md        ← L0 载体（如 l0-format / l0-system / l0-loop / l0-logic）
```

`SkillCatalog.LoadFromDirectory` 递归扫描所有含 `SKILL.md` 的子目录（根目录自身 `SKILL.md` 为参考母文件，不加载）。front-matter 键（YAML）：

| 键 | 取值 | 说明 |
|----|------|------|
| `name` | string（必填） | 唯一技能 id（一经注册不可变） |
| `description` | string | 供意图匹配 / 选择打分 |
| `kind` | operation \| lifecycle \| knowledge \| l0 | 类别 |
| `module` | internal \| mcp \| rare \| l0 | 归属模块（缺省按 kind/executor 推断） |
| `always` | true \| false | 恒在注入（缺省随 module: l0 置位） |
| `dsl` | none \| format \| l0-system \| l0-loop \| l0-logic \| protocol | DSL 承载类型（缺省 none） |
| `tags` / `trigger` | list / string | 选择匹配 |
| `schema` | string | 确定性请求契约 |
| `executor` | sk \| answer \| cline | 执行端（缺省 sk） |
| `backend` | string | 确定性模块后端锚点 |
| `rare` / `on_demand` | bool | 按需注入（缺省随 rare 模块置位） |

正文 = 操作指南或 DSL 原文（`dsl != none` 时）。

### 2.5 加载链

```
SkillCatalogLoader.Load（组合根装配）
  └─ SkillsOptions.Enabled=false → SkillRuntimeState.Set(空,空)
  └─ SkillCatalog.LoadFromDirectory(skillsDir)   ← Agent.Axioms 纯加载器（目录缺失→空）
       └─ ParseFrontMatter 每条 SKILL.md → SkillDefinition
  └─ SkillRuntimeState.Set(hot=Module≠Rare, rare=Module==Rare)  ← Agent.Runtime 状态快照
```

消费方（`SkillInjector` / `PromptDslSource` / 折叠算子）只读 `SkillRuntimeState.Hot/Rare`，不触碰加载器。

### 2.6 选择与注入

`SkillInjector.Inject(userInput, goal, taskKey, hot, rare, requestedNames?)`：

```
① 输入守卫 InputSafetyGuard.Check → 命中返回 BlockedReason（短路）
② 操作块选配源 blockSource = hot 排除 {AlwaysOn || module==L0 || dsl!=none}（DSL 承载 / L0 载体不进操作块）
③ SkillSelector.SelectWithRare(blockSource, rare, ...)
     - 显式 requestedNames 优先（可命中 rare/on-demand）
     - 关键词自动匹配：score name=3 / trigger=2 / tag=1，OnDemand 恒排除
     - MaxInjected 截断（默认 3）
④ SkillPromptAssembler.Build(selected) → SkillBlock
⑤ 返回 InjectResult{ BlockedReason, SelectedSkills, SkillBlock }（L0 由 PromptCatalog 经 PromptDslSource 单源装配，不在 Inject 内）
```

### 2.7 DSL 承载（`dsl:`）

`dsl != none` 的技能，其**正文即提示词 DSL 原文**（format 段 Schema / L0 各段 / L1-L2 协议），由 `PromptDslSource.ReadSkillBody(dsl)` 供给 `PromptCatalog` 装配；不进"技能操作指南"块、不参与关键词打分。缺省 `none` = 普通操作技能（现有 SKILL.md 零迁移）。

---

## 三、Prompt 体系

### 3.1 L0 / L1 / L2 分层

| 层 | 内容 | 来源 | 形态 | 引擎解析 |
|----|------|------|----------|
| **L0** | 系统提示词（协议/身份/操作/安全/循环/逻辑公理） | `PromptCatalog` 经 `PromptDslSource` 按序拼接 4 段 skill 正文 | 进程级常量，模型读文本 + 结构化 `L0Protocol` | DslParser + L0Projector（全段结构化：公理/门/状态机/类型/Schema） |
| **L1** | 任务级提示（目标/约束/计划/原始输入） | `PromptCatalog.BuildL1`（模板填槽）或 SK 生成 | `{slot}` 模板 / 生成 JSON | DslParser + L1L2Projector → PromptProtocol |
| **L2** | 步骤级提示（上步结果/下一步） | `PromptCatalog.BuildL2Continue` 或 replan 生成 | `{slot}` 模板 / 生成 JSON | 同上（L1L2Projector） |

`RepositoryPaths.L0PromptFileNames`（顺序即上下文高注意区，format 置前缀对齐 `FormatInstructionLocality`）：
`format → l0-system → l0-loop → l0-logic`（DSL 承载段标识）。

### 3.2 装配管线（`PromptFrameworkFunction`）

`BuildAssemblyPipeline`（StaticAsyncPipeline 四阶段）：

```
l0      ctx.L0 = PromptCatalog.L0SystemPrompt（skill 正文唯一来源，由 PromptCatalog 经 PromptDslSource 装配）
skills  SkillInjector.Inject → ctx.SelectedSkills / ctx.SkillBlock（L0 由 PromptCatalog 单源装配，不在此覆盖）
l1      首步 → GetOrGenerateL1Async（模板填槽 / SK 生成 + PromptValidator 校验 + taskKey 缓存）
        继续步 → 缓存或模板填槽
l2      首步留空；继续步按 ReplanConditions 判定 → 生成 / BuildL2Continue 填槽
assemble PromptText.Assemble(L0, SkillBlock, L1, L2)
        产出 { system=L0+SkillBlock, l0, skills, skill_block, l1, l2, replanned, boundary_action, task_key, is_first_step }
```

`PromptAssemblyContext` 承载 L0/L1/L2/SkillBlock/SelectedSkills/Replanned/BoundaryAction 等中间产物；`AssembleLayersAsync` 为 `IContextProjector` 适配器复用同一条管线（SingleImplementation/TH-12）。

### 3.3 PromptCatalog 单源加载（skill 唯一来源）

| 加载 | 来源（skill 唯一，无文件/嵌入兜底） |
|------|------|
| `L0SystemPrompt` / `L0Protocol` | `PromptDslSource` 各段 skill 正文（`L0FileToDsl` 映射）→ `DslParser` + `L0Projector`，skill 唯一来源 |
| `Protocol`（L1/L2） | `dsl: protocol` skill 正文 → `DslParser` + `L1L2Projector`，skill 唯一来源 |
| `FormatSpec` | `dsl: format` skill 正文 → `DslParser` + `FormatProjector`，skill 唯一来源 |


### 3.4 生成与校验

- **生成**：`PromptLlmGenerator.GenerateTaskPromptJsonAsync`（L1）/ `GenerateStepPromptJsonAsync`（L2）；`Generation.Enabled`（`% L1_GENERATION_ENABLED`）与 `needsReplan`（`L2ReplanConditions`）触发。
- **校验**：`PromptValidator.TryAcceptL1/TryAcceptL2` 按 `FormatSpec` 派生必填（L1→goal / L2→instruction），失败回退模板填槽（`GenerationFallback`）。
- **填槽**：`PromptText.Fill(template, slots)` 复用 `Plate.JsonMethod.TemplateFormatter`（`{slot}` 语法，缺失替换空串）；`PromptText.Assemble` 按序以空行拼接。

---

## 四、DSL 体系

### 4.1 两套 DSL

| DSL | 用途 | 解析器 | 载体 |
|-----|------|--------|------|
| **clinerules 声明 DSL** | L0 系统提示（`[AX]/[Gate]/[SM]/[Type]/[Def]/[Strategy]/[Schema]/[CR]`） | 模型读（引擎不解析），format 段除外 | `l0-system` / `l0-loop` / `l0-logic` / `format`（DSL 承载技能） |
| **提示词协议 DSL** | L1/L2 模板 / 生成配置 / replan 判定 | `PromptDslParser` | `l1l2`（dsl: protocol） |

### 4.2 L0：clinerules 声明 DSL（半 DSL）

L0 各段用 clinerules 声明语法（源于 `.clinerules/axioms.md / formula.md / project.md / logic.md`），语义为给模型读的强制指令文本。**引擎不解析** DSL 标记（`l0-system`/`l0-loop`/`l0-logic` 标注"纯模型声明"）。唯一例外：**`dsl: format` 技能可解析**——`FormatSpecParser` 把 `[Schema]` 解析为 `PromptFormatSpec`（Schema/必填/枚举），供 L1/L2 生成校验与 DTO 一致性。

### 4.3 L1/L2：提示词协议 DSL（`PromptDslParser` 文法）

`dsl: protocol` 技能正文（`skills/internal/l1l2/SKILL.md`）为唯一运行期数据源，解析为 `PromptProtocol`。文法：

| 结构 | 语法 | 落到模型 |
|------|------|----------|
| 模板 | `[SM] 名字:` + ` ```text ` fenced 块 | `L1.Template` / `L2.ContinueTemplate` |
| 槽位 | fenced 块后 `\| slots: a, b, c` | 填槽 slot 集 |
| 生成 system | `[AX] 名字: 文本 \| 对齐: ...`（值取首个 `\|` 前） | `L1/L2.Generator.SystemTemplate` |
| 生成 user 模板 | `[SM] 名字:` + fenced 块 + `slots:` | `Generator.UserTemplate` |
| 生成配置 | `% NAME=type:value` | `L1GenerationConfig` |
| replan 判定 | `[Gate] 名字:` + 缩进行 `field=…, op=…, value=[…]` | `L2.ReplanConditions` |

`[SM]/[Gate]/%` 为解析器消费的结构；`[AX]` 正文是给 LLM 生成器的 system 提示；`[Type]`（§四 契约公理）解析器**忽略**（仅人工审查）。

### 4.4 l0-format 双角色

`skills/l0/l0-format`（`dsl: format`）正文同时是：① L0 拼接之一（模型读文本）；② `DslParser + FormatProjector` 的 `[Schema]` 投影源（引擎用，L1/L2 生成 Schema 单一真相）。同一技能正文同时喂给两个消费者，消除"文件路径 vs 校验 Schema"漂移。

---

## 五、三者集成（数据流）

```
技能装配（启动）：SkillCatalogLoader.Load → SkillRuntimeState{ Hot, Rare }
                      │  Hot 含 Internal+Mcp+L0（含 DSL 承载技能 l0-*/l1l2）
提示词装配（每轮）：
  PromptFrameworkFunction 管线
     l0 阶段 → PromptCatalog.L0SystemPrompt
               └─ PromptDslSource.ReadSkillBody(dsl) 唯一来源（L0FileToDsl：format/l0-system/l0-loop/l0-logic）
     skills 阶段 → SkillInjector.Inject(Hot, Rare)
                    （L0 由 PromptCatalog 经 PromptDslSource 单源装配，不再由技能覆盖）
                    ├─ SkillBlock（操作技能，排除 dsl!=none / L0 / AlwaysOn）
     l1/l2 阶段 → PromptCatalog.Protocol（dsl:protocol 唯一来源）→ 填槽/生成/校验
  assemble → prompt = L0 + SkillBlock + L1 + L2；system = L0 + SkillBlock
```

**关键**：DSL 承载技能（`dsl!=none`）是"装配期 DSL 源"，经 `PromptDslSource` 供 `PromptCatalog`；普通操作技能（`dsl=none`）经 `SkillInjector` 注入 SkillBlock 给 LLM。两者职责分离、互不混入。


---

## 六、关键设计决策与红线

| 约束 | 落实 |
|------|------|
| **零回归** | `PromptDslSource.ReadSkillBody` 未挂载/正文留空 → null → 该段为空降级（skill 唯一来源，无文件/嵌入兜底） |
| **保持 DSL** | 两套 DSL 文法（clinerules 声明 DSL / 提示词协议 DSL）一字未改；解析器签名不变，仅文本来源由文件改为"skill 正文唯一来源" |
| **单一真相** | DSL 原文仅经 `dsl:` 承载技能承载（skill 唯一真相）；`Config/prompts` 目录已删除，未挂载/正文留空 → 空段降级 |
| **SingleImplementation / TH-12** | 装配收敛 `SkillInjector` 单一入口；填槽复用 `TemplateFormatter`；`skills` 目录与仓库路径由 `RepositoryPaths` 单点持有 |
| **SEP.DeterministicCoreDisjoint** | L0/L1/L2 装配与技能选择属引擎执行链，不进 LLM Function Choice 工具集 |
| **FormatInstructionLocality** | L0 四段按序拼接、format 置前缀保持上下文高注意区 |
| **Zero-migration for skills** | 新字段 `AlwaysOn`/`Dsl` 均有缺省；旧 SKILL.md 解析自动归 `none`/按 kind/executor 推断模块 |

---

## 七、文件清单

| 环节 | 文件 |
|------|------|
| Skill 通用逻辑库 | `src/Agent.Skill/`（`SkillDefinition.cs`/`SkillSelector.cs`/`SkillPromptAssembler.cs`/`SkillInjector.cs`/`SkillsOptions.cs`/`IFoldInProcessExecutor.cs`/`IDeterministicSkillExecutor.cs`） |
| Skill 加载 | `src/Agent.Axioms/Skills/SkillCatalog.cs`、`src/Agent.Launcher/Loading/SkillCatalogLoader.cs`、`src/Agent.Runtime/State/Skills/SkillRuntimeState.cs` |
| Skill 目录 | `skills/{internal,mcp,rare,l0}/*/SKILL.md` |
| Prompt 装配 | `src/Agent.Core/Prompts/PromptFrameworkFunction.cs`、`PromptAssemblyContext.cs` |
| Prompt 目录/加载 | `src/Agent.Axioms/Prompts/PromptCatalog.cs`、`PromptDslSource.cs` |
| DSL 解析/模型 | `src/Agent.Operation/Prompts/L1L2Projector.cs`、`L0Projector.cs`、`L0Protocol.cs`、`FormatProjector.cs`、`PromptProtocol.cs`、`PromptFormatSpec.cs`、`PromptText.cs` |
| 生成/校验/格式 | `src/Agent.Axioms/Prompts/PromptBuilder.cs`、`PromptLlmGenerator.cs`、`PromptValidator.cs`、`PromptDefaults.cs`、`FormatConsistencyValidator.cs` |
| DSL 文本 | `skills/{l0/l0-format,l0/l0-system,l0/l0-loop,l0/l0-logic,internal/l1l2,rare/boundaries}/SKILL.md`（唯一真相；`Config/prompts` 已删除） |
| 路径常量 | `src/Agent.Axioms/Infrastructure/RepositoryPaths.cs` |

---

## 八、附录：DSL 承载技能示例（`skills/l0/l0-format/SKILL.md` 头部）

```yaml
---
name: l0-format
kind: l0
module: l0
dsl: format
tags: [prompt, dsl, l0, protocol]
---
# 格式契约（L0 · 进程级常驻）— 可解析 + 模型读
...
[Schema] L1TaskPromptSchema:
  { goal:string, constraints?:string, plan_summary?:string }
  | required:{goal}
---
```

`dsl: protocol` 技能（`skills/internal/l1l2/SKILL.md`）正文即 L1/L2 生成协议 DSL，由 `DslParser` + `L1L2Projector` 投影为 `PromptProtocol`；`dsl: format` 技能正文由 `DslParser` + `FormatProjector` 投影为 `PromptFormatSpec`，同时并入 L0 文本。








