# SK Function Calling 与工作流函数边界（架构不变式）

> 目的：把"Function Calling 与 WorkFlowMethod 工作流函数不冲突"从口头约定固化为显式、强制的边界。
> 对齐公理：`SEP.DeterministicCoreDisjoint` / `AX.DecisionOutputSet` / `AX-CORE.AxiomHierarchyStructure(invariants L0)`；
> 反模式：`@ ANTI-SYNTAX-OVERRIDE` / `@ ANTI-LAYER-LEAK`。

## 一、两套注册表（互不桥接）

| 注册表 | 载体 | 承载对象 | 驱动方 |
|--------|------|----------|--------|
| **工作流函数** `FunctionStorageRegistry` | `WorkFlowMethodUsage.RegisterAsyncFunction` | `guard_input` / `provide_prompt` / `cline_execute` / `rule_match` / `guard_output` | 引擎（`WorkflowRunner` 按 `RoundSpec` 顺序） |
| **SK 工具集** `Kernel` plugins | `KernelFunctionFactory` / `SkFunctionBinder` | LLM 辅助工具（只读） | LLM（`FunctionChoiceBehavior`） |

两套注册表**各自独立、禁止互相引用**。引擎执行链函数绝不注册进 Kernel plugins；LLM 辅助工具绝不经工作流骨架调用。

## 二、两类函数

### 引擎执行链（确定性核心，禁止暴露给 LLM 自治）
- 名单：`EngineSkeletonFunctionCatalog.Names`（`guard_input` / `provide_prompt` / `cline_execute` / `rule_match` / `guard_output`）。
- 承载 `RoundLifecycle` 五步顺序、`rule_match`→`DecisionPoint` 决策权威、`guard_*` 守卫。
- 若暴露为 SK Function Calling 工具，LLM 可在生成阶段绕过确定性顺序与裁决 → 违反确定性核心纪律。
- 新增引擎骨架步骤时必须同步追加该名单。

### LLM 辅助工具（自由探索源场景，允许 Function Calling）
- 条件：只读、非裁决、仅服务"SK 作为 LLM 客户端的辅助调用点"（如 `LlmStateDirector` 提议生成）。
- 产物仍须经 `TransitionGuard`（目标∈可用集）校验 + `HybridSchedulerStateMachine` 施加才生效——确定性兜底不可省。
- 经 `SkFunctionBinder.BindLlmTool` 唯一入口注册（名字命中引擎名单即拒绝）。

### 执行端工具（answer 作为调用方，S3 落点）
- answer（`StatelessAnswerExecutor`）经 `ILlmClient.ChatWithToolsAsync` **动态传入**本地数据库读写工具
  （`LocalDbMcpTools.GetTools()`：`db_read_node` / `db_list_nodes` / `db_write_node` / `db_delete_node` / `db_summary`），
  LLM 自主读写本地节点库。
- 工具经 `FunctionChoiceBehavior.Auto(functions)` **动态传入**，**不经** `Kernel.Plugins` 常驻、**不经** `BindLlmTool`；
  工具名不撞引擎名单（`EngineSkeletonFunctionCatalog.Names`）。
- **允许含写工具**：answer 写入的是**业务数据节点**（ur_nodes 数据区），不触碰确定性核心
  （Budget / Token / Flag / Round / 任务池裁决），对齐 `SEP.DeterministicCoreDisjoint`——确定性核心仍只归框架，
  answer 不可改预算/终止/状态机。
- 仍禁止把引擎执行链函数（`guard_input` / `provide_prompt` / `cline_execute` / `rule_match` / `guard_output`）暴露给 answer。

## 三、强制机制

`SkFunctionBinder.BindLlmTool(name, description, method)` 是**唯一允许**把函数转成 `KernelFunction` 的入口：
- 函数名命中 `EngineSkeletonFunctionCatalog.Names` → 抛 `NotSupportedException`（不依赖调用方诚实声明角色，按名单强制拒绝）。
- 仅此入口可用于装配 SK 工具集；任何旁路 `KernelFunctionFactory.CreateFromMethod` 直接注册引擎函数均属违规（code review 检查点）。

## 四、安全启用场景

1. **`LlmStateDirector`**（`[BindAxiom(FirstCallTransient)]`，自由探索源，S2）：经 `Kernel.Plugins` 常驻只读 LLM 辅助工具（`BindLlmTool` 注册），`FunctionChoiceBehavior.Auto()` 无参使用 Plugins。
2. **`StatelessAnswerExecutor`**（answer 作为调用方，S3）：经 `ChatWithToolsAsync` 动态 `FunctionChoiceBehavior.Auto(functions)`，工具集 = `LocalDbMcpTools.GetTools()`（db_* 本地数据库读写，可含写）。
3. **`cline_execute`**：**不**启用 Function Calling（走 Cline 编码代理，非 SK 调用点）。

## 五、检查清单（变更时）

1. 新增函数是否属于引擎执行链？是 → 只加 `EngineSkeletonFunctionCatalog` 名单，不注册 SK。
2. 新增 LLM 辅助工具？是 → 命名不得撞引擎名单，经 `BindLlmTool` 注册，保持只读。
3. 是否出现同一工具同时存在于两套注册表？是 → 违反 `SingleImplementation`，必须消除。
4. `FunctionChoiceBehavior` 是否被加到了非 `LlmStateDirector` / `StatelessAnswerExecutor` 的 SK 调用点？是 → 复核是否触碰确定性核心。
5. answer（`ChatWithToolsAsync`）传入的工具名是否撞引擎名单（`EngineSkeletonFunctionCatalog.Names`）？是 → 拒绝（确定性核心不可暴露给 LLM 自治）。
6. 引擎执行链函数是否出现在 `LocalDbMcpTools` 工具集或 `BindLlmTool` 注册？是 → 违规，须消除。
