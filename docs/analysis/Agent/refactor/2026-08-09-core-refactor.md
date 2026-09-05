# Agent.Core 彻底重构跟踪文档

> 日期：2026-08-09 | 范围：`Agent/src` 全部模块 | 关联：clinerules、`WorkFlowMethod.LoopFramework.Axioms`
> 方案来源：`docs/refactor/` 规划（缺陷分析与重构方案见对话记录，精简要点见下）。

## 1. 重构目标

| 目标 | 对应缺陷 |
|---|---|
| 1. 错误分类单一权威、引擎与决策端口用同一分类 | 错误分类被 `out _` 丢弃 + 双处重复分类 |
| 2. 决策词强类型化，消灭"三处解释成功语义" | 决策/终止语义多轨并存 |
| 3. 跨轮状态从 `Dictionary<string,object>` 收敛为强类型 DTO | 魔法字符串键状态契约 |
| 4. `WorkflowRunner` 上帝类拆分为单一职责组件 | 上帝类违反单一职责 |
| 5. 静态全局单例（注册表/IoC/缓存）实例化、可并行可测 | 进程级全局单例 |
| 6. 模块装配由"反射+字符串+吞异常"改为显式、核心执行端不可缺省 | 反射+字符串+吞异常 |
| 7. 核心决策数据（L0/规则集/骨架）改 fail-fast 或显式开关 | 静默 fail-open |
| 8. 命名空间 `Agent.Planner` 残留收敛为 `Agent.Core` | 命名空间与物理位置错位 |
| 9. `Action.Retry/Switch`、`LoopDecision.SwitchStrategy` 死语义落地或删除 | 死语义 |

## 2. 基线

- 构建命令：`dotnet build Agent\Agent.sln -c Debug`
- 基线状态：**0 个警告 / 0 个错误**（2026-08-09 记录）
- 目标框架：`net10.0-windows`
- 测试项目：目前无正式单测项目（仅 `BuildOutput/obj/SmokeTest` 残留，需核实既有测试框架）

## 3. 阶段清单

| 阶段 | 内容 | 状态 |
|------|------|------|
| 0 | 基线锁定 + 本文档 | ✅ 进行中 |
| 1 | P0 错误分类单一权威（AgentRunner 显式填 ErrorClass，MapEval 保留防御兜底） | ✅ |
| 2 | 决策词强类型化（DecisionWordMapper 单一词汇表 / WorkflowRoundResult.Decision 强类型） | ✅ |
| 3 | 跨轮状态强类型化（RoundState DTO + RoundKeys 键名收敛） | ✅ |
| 4 | WorkflowRunner 拆分（RoundSpecLoader/RoundStepInvoker/RoundStateMapper） | ✅ |
| 5 | 实例化 DI 去静态单例（LlmClientFactory 已实例化；IWorkflowFunctionRegistry/PromptFrameworkService 建议后续） | 🔄 部分 |
| 6 | 显式模块装配（去反射 + Required/Optional + fail-fast） | ✅（组合根反转至 Launcher：AgentOptionalModules 显式装配，去 Core 反射） |
| 7 | 核心配置 fail-fast 开关 + 文档/契约对齐（IAgentLoopConfig.RetryLimit 已补；fail-fast 开关建议后续） | ✅（AgentConfig.StrictConfig + AgentCoreRuntime，组合根决策） |
| 8 | 命名空间收敛（Agent.Planner→Agent.Core）+ 死语义收敛 | ☐ 建议独立后续 |

## 4. 关键设计决策

- **错误分类统一（最小侵入）**：不动框架 `RoundLifecycle`，在 `AgentRunner.ExecuteRoundAsync` 包装时显式填充 `AxiomActionResult.ErrorClass`；引擎天然优先取显式值，`LoopDecisionMapper` 收到非空 `explicitError` 后不再二次文本分类。
- **强类型决策**：以 `LoopDecision` 为唯一成功/终止真值，字符串词只在 `rule_match → DecisionWordMapper → LoopDecision` 单一入口出现一次。
- **状态强类型 + 键白名单校验**：既消除魔法键，又保留声明式骨架灵活性，把"静默吞错"改为"可观测错误"。
- **显式装配边界**：组合根（Launcher）显式装配，`Agent.Core` 不反向引用适配模块，通过 `IAgentModule[]` 注入。

## 5. 每阶段验证方式

- 每阶段结束：`dotnet build Agent\Agent.sln -c Debug` 保持 0 警告 0 错误。
- 关键行为 Diff：`BuildRoundSpec` 主路径不变（`guard_input → provide_prompt → cline_execute → rule_match`）；决策词行为一致。
- 关键单元（`DecisionWordMapper`/`LoopDecisionMapper`/错误分类/`RoundSpecLoader`）新增单测。

## 7. 建议独立后续任务（未在本轮完成）

| 任务 | 说明 | 风险 |
|------|------|------|
| `WorkFlowMethodUsage` 实例化适配层 | Agent 内部调用收敛到 `IWorkflowFunctionRegistry`；需协调跨模块（ClineUsage/Guardrails/McpTools）注册路径 | 框架级改动，跨模块回归 |
| `PromptFrameworkFunction` 缓存/生成委托实例化 | `_l1Cache`/`TaskPromptGenerator` 移入实例服务，消除进程级静态缓存 | 中等 |
| 显式模块装配（去反射） | `AgentModuleRegistrar` 改编译期显式引用 + `Required/Optional` + fail-fast；需在 Launcher 组合根装配 | 改变启动行为，需谨慎 |
| 核心配置 fail-fast 开关 | `RuleSet`/`PromptCatalog`/骨架加载加 `strictConfig`，损坏即 throw | 需明确宿主默认值 |
| 命名空间收敛 | `Agent.Planner`/`Agent.Planner.Prompts` 残留收敛为 `Agent.Core.*`，并同步文档六边形图 | 大范围机械 churn |
| 死语义落地/收敛 | `Action.Retry/Switch`、`LoopDecision.SwitchStrategy` 落地最小重试或删除未用分支（框架侧） | 框架语义，需独立评审 |
| 单测基础设施 | 为 `DecisionWordMapper`/`LoopDecisionMapper`/错误分类/`RoundSpecLoader` 建立测试项目 | — |

> 以上任务建议在建立测试基础设施后，作为独立提交逐个推进，避免未验证的大范围改动。

## 8. 已完成改动清单（本轮）
- 阶段 1：错误分类单一权威（`AgentRunner` 显式填 `ErrorClass`，消除双处重复分类）。
- 阶段 2：`DecisionWordMapper` 单一决策词汇表 + `WorkflowRoundResult.Decision` 强类型。
- 阶段 3：`RoundKeys` 键名收敛 + `RoundState` 强类型 DTO + `RoundStateMapper`（Seed/Harvest 迁移）。
- 阶段 4：`WorkflowRunner` 拆分为 `RoundSpecLoader`/`RoundStepInvoker`/`RoundStateMapper` + 精简编排器。
- 阶段 5(部分)：`LlmClientFactory` 实例化（保留默认静态门面），`ClineExecutorFunction` 改用实例。
- 阶段 7(部分)：`IAgentLoopConfig`/`AgentLoopConfig` 补 `RetryLimit`，消除文档/契约漂移。

每阶段均验证 `dotnet build Agent\Agent.sln -c Debug` 保持 0 警告 0 错误。

### 本轮补充（2026-08-09 配置反转到组合根 Launcher）
- **编排器构造注入**：`AgentOrchestrator(IAgentLoopConfig)` 新增；无参构造保留（`new AgentLoopConfig()`）向后兼容。
- **去反射装配**：`AgentModuleRegistrar` 删除 `TryRegisterMcpToolsModule`/`TryRegisterGuardrailsModule`（反射+字符串）；`SetupAgentAsync(loopConfig, moduleRegistrar)` 只注册 CoreModule + 组合根回调。
- **Launcher 显式装配**：新增 `AgentConfig`（配置目录/fail-fast/模块开关/循环预算）与 `AgentOptionalModules`（按开关显式装配 Cline/McpTools/Guardrails），`InitializeStage`/`AxiomAgentLauncher` 统一组合根。
- **运行时配置反转**：新增 `AgentCoreRuntime`（ConfigDirectory + StrictConfig），由 Launcher 在装配期注入；`RoundSpecLoader`/`RuleSet`/`PromptCatalog` 读取它决定目录解析与 fail-fast，未设置时保留内置向上查找/默认兜底。
- **fail-fast**：`StrictConfig=true` 时骨架/规则集/提示词缺失或损坏即抛，不回退内置默认。

