# Agent 架构设计分析报告

> 分析范围：`D:\Project\Program\WorkFlowPrj\Agent`（10 个内部项目 + Axioms 生成库 + 2 个入口）
> 分析方法：csproj 依赖图提取 + 组合根/宿主/状态层逐文件审读 + 跨层引用追踪
> 与《Agent代码逻辑分析报告》互补：本篇聚焦**结构、边界、职责、状态管理**等设计层问题，不复述代码层缺陷。

**总体评价**：这是一套"契约分层 + 公理驱动 + 装配管线化"的成熟设计（组合根 11 阶段装配、D1-D4 收敛痕迹明显、依赖方向大体单向）。核心架构问题集中在三处：**① 物理分层与文档分层不一致（状态层寄居、存储桥挂错宿主）；② 静态全局（Service Locator / AsyncLocal / 静态单例）与组合根注入双通道并存；③ 模块接入用"字符串函数注册表"取代了类型化插件契约**。

---

## 一、当前分层与依赖图

```
                        ┌─────────────────────────────────────────┐
                        │  Agent.Launcher  (组合根 + WinForms UI)   │  ← 引用全部内部项目
                        └───────────────┬─────────────────────────┘
                                        │
        ┌───────────────┬───────────────┼──────────────┬──────────────────┐
        ▼               ▼               ▼              ▼                  ▼
  Agent.Modules   Agent.Guardrails  Agent.Core ──► Agent.Axioms ──► Agent.Runtime
  (Cline/MCP/Answer)  (门控)         (循环引擎)   (公理绑定 L5)   (状态/持久化)
                                        │               │                │
                                        └──► Agent.Operation ──► Agent.Contract（纯契约）
                                             Agent.Calculate         │
                                                │                     ▼
                                                └──► Axioms(Plate.Axioms, 生成库) ──► DslMethod

  Agent.StorageBridge（独立 EXE）──► Agent.Launcher（⚠ 本应只依赖存储库）
```

**发现的架构问题（按严重度排序）：**

### 1.【高】Agent.Axioms → Agent.Runtime：自述分层规则与物理依赖不符
- `src/Agent.Axioms/*.csproj` ModuleRule 明确写：「本模块不得反向引用 Agent.Core，依赖方向固定为 **Agent.Core → Agent.Axioms → Axioms(Plate.Axioms)**」——但实际 **8 个文件 `using Agent.State`**（ConfigFileLoader / PromptCatalog / LlmStateDirector / TransitionGuard / InvocationFilters / SchedulerAssistantTools / SemanticKernelLlmClient / SkillCatalog），而 `namespace Agent.State` 物理上**定义在 Agent.Runtime 项目里**。
- 后果：真正的依赖是 `Core → Axioms → Runtime → Contract`，Runtime 反而成了最底层；「公理绑定实现层」依赖「运行时状态层」，两者在文档中是平级甚至相反的。
- 根因：**`Agent.State` 状态层没有独立成项目**，而是寄居在 Agent.Runtime 里。Axioms 要消费状态对象（BoundaryRuntimeState / ReactiveConsumptionState / AgentCoreRuntime / JsonShim）就只能引用整个 Runtime。
- 建议：把 `Agent.State` 命名空间独立为 `Agent.State` 项目（最底层，仅依赖 BCL + Share），Agent.Runtime 降为它的消费方；或修订分层文档使 Runtime 成为公认最底层。

### 2.【高】Agent.StorageBridge（独立 EXE）引用 Agent.Launcher（WinForms 库）
- `src/Agent.StorageBridge/*.csproj` → `Agent.Launcher`，而 Launcher 是 `UseWindowsForms=true` 的库。存储桥进程只需要 `DatabaseModule`/`DatabaseBootstrapper`，却把整个 UI 宿主装配层拖进依赖。
- 后果：① 存储桥进程连带 WinForms 程序集；② Launcher 同时承担「组合根 + UI 宿主 + 存储基础设施」三重职责，归属混乱；③ StorageBridgeCommands 与 DatabaseModule 的存储语义耦合在装配层。
- 根因：**存储基础设施（DatabaseModule/DatabaseBootstrapper）没有下沉到独立库**（如 Agent.Storage 或 Agent.Runtime），而是挂在 Launcher。
- 建议：把 DatabaseModule 及 DatabaseBootstrapper 迁出 Launcher 至独立 Storage 项目，Launcher 与 StorageBridge 都引用它。

### 3.【中】Agent.Core 巨型依赖中心：程序集级无边界强制
- Agent.Core 引用 18 个项目，内部按子目录（Loops / Orchestration / TaskPool / DataStream / Boundaries / Prompts）分六个子系统，但边界只存在于 namespace 级，**程序集内子模块可随意互相引用，无编译期拦截**（如 TaskPool 直接引用 Orchestration 类型没有任何检查）。
- 后果：子系统间耦合靠自律而非结构；AgentRunner（565 行）+ AgentOrchestrator（451 行）+ AgentTaskPool 都是横跨多子系统的"厚类"。
- 建议：若六子系统继续膨胀，按「循环引擎 / 任务池 / 数据流 / 编排」拆分为独立程序集并显式声明引用方向。

### 4.【中】Agent.Guardrails → Agent.Core 反向依赖，且 using 实际未用
- `GuardFunction.cs:7 using Agent.Core` 但全文未使用任何 Core 类型（门控函数只操作 WfContext 参数）。安全门控本应只依赖契约层（IAgentExecutor / 函数注册契约），却直接引用被门控的核心。
- 建议：Guardrails 项目移除对 Agent.Core 的引用（若确实未用），门控端口下沉到 Agent.Contract。

---

## 二、组合根与装配架构

### 5.【中】装配上下文 = 服务定位器式共享可变上下文
- `AgentFrameworkAssemblyContext` 持 12 个装配产物属性（Orchestrator/Persistence/DataStore/Resources/NodeRelations/ReferenceGrounding/NodeGraphReconciler/KeyMappings/Reactive/TaskPool/Boundary），阶段间依赖靠 `CanExecute` 检查 + 注释顺序维系，**无显式依赖图**（阶段 6.5 依赖阶段 6 和 4 的产物，仅靠 CanExecute 守卫）。
- `FrameworkResourceGraphStage`（阶段 6.5）是"上帝阶段"：单阶段完成建图库、建引用守卫、写透包装、KeyMapping 创建、对账器、回注编排器 6 类装配，任何一环失败都归因到"FrameworkResourceGraph"。

### 6.【中】静态宿主与组合根注入双通道并存（Service Locator）
- `AgentUiHost` 提供 7 个**静态槽位**（Current / Database / ReferenceGrounding / TaskPool + Has* 判断），组合根装配完 `Attach(framework)`；UI 工作流函数经 `AgentUiHost.Require()` 读取。
- 后果：① **进程内单实例**——`Current` 只有一个槽位，多租户 / 并行测试 / 双框架并存被架构性禁止，测试需反复 Attach/Detach；② 装配产物存在「组合根字段」与「静态槽位」两条事实源；③ `GetOrBootstrapDatabaseAsync` 允许 UI 函数**惰性自装配数据库**——UI 层职责越界，数据库装配无唯一所有者。
- 说明：对"工作流 DSL 函数库"场景这是有意识的权衡（注释已声明"组合根反转，UI 层只读快照"），但对可测试性与多实例是硬伤。

### 7.【中】模块接入 = 全局函数注册表 + 字符串路由
- Cline / MCP / Guardrails 经 `WorkFlowMethodUsage.RegisterAsyncFunction("mcp_call_tool", ...)` 注册到**全局函数表**，工作流 DSL 按字符串名调用；执行器按 `Name` 字符串解析（`IAgentExecutor` 注册表）。`McpToolFunction.Register()` 依赖 `FunctionRegistrar.RegisterOnce` 防双路径重复注册（说明注册路径本就存在分叉）。
- 后果：① 插件契约无类型化接口——`"mcp_call_tool"` 拼错运行时才暴露；② Core 与模块的耦合经"全局表 + 名字"隐式发生，编译期不可见；③ 与代码层报告的规则引擎双实现（ConfigStateDirector / RuleTaskExecutor 各自解析同一规则集）同源——配置驱动都靠字符串键。

### 8.【低】装配哲学不一致：fail-fast 与 fail-silent 并存
- 核心配置 `ValidateConfig` 坚持 fail-fast（"缺失/非法即抛，禁止静默回退"）；可选模块 `TryRegisterAsync` 却 catch-all 吞异常只打日志（MCP 注册失败静默降级）。同为装配期错误，策略却不统一，用户无从感知模块降级。

---

## 三、状态管理架构

### 9.【高】AsyncLocal（Functor）隐式上下文 7 通道
- `AgentRunCancellation` 把 取消令牌 / 整体预算 / 任务键 / 调度模式 / 调度上下文 / 取消检查 / LLM 门 全部经 **Functor(AsyncLocal) 隐式透传**，深层代码无参读取（`GetToken()` / `GetBudget()`）。
- 优点：免参数穿透污染（几千处调用链不用传参）。代价：
  - **隐式依赖编译期不可见**（Hidden Dependencies）：方法签名不体现它读取了 run 上下文；
  - **作用域生命周期敏感**：`AgentFramework.RunAsync` 不得不用 `finally { RestoreFrameworkCancellation() }` 手动恢复框架级取消——代码注释自认"若不恢复，下一次常驻 RunAsync 将沿用上一次调用的 token"（踩过坑的证据）；
  - **并发 run 串扰**：任务池暂停门需 `ClearTaskKey`（置 null）+ `SetTaskKey` 回填防嵌套 LLM 调用二次进闸（又一个坑的证据）；MaxConcurrency>1 时多 run 并发，AsyncLocal 流式上下文会互相污染；
  - `AgentRunCancellation` 标记 `[BindAxiom(..., Status = BindStatus.Drift)]`——架构自认与公理契约漂移。

### 10.【中】静态单例与实例注入并存（"先到者赢"）
- `SemanticKernelLlmClient.Instance` 静态单例 vs `SetLlmClient()` 实例注入并存；`LlmClientFactory.Instance`、`AgentCoreRuntime.Apply` 静态配置、`LlmHosting.Configure` 静态装配。LlmHosting 注释自述要解决"先到者赢"问题——静态与注入两条路径的时序竞争是架构级双源。
- 建议：运行时配置（配置目录/LLM 客户端/取消检查）全部收口到显式传入，静态通道仅作测试兜底。

### 11.【中】状态所有权分散
- `Agent.State` 命名空间的对象分布在 Agent.Runtime 多个子目录（Boundaries / DataStream / Infrastructure / Loops），Agent.Core 又各自持有编排器状态（AgentOrchestratorState）、边界状态（BoundaryFramework）、运行时状态——「状态收敛到 Agent.State」的意图与物理分布不一致，状态修改点散落 Core/Runtime 两项目。

---

## 四、执行路径与模块边界

### 12.【中】双执行路径并存，取消/持久化语义分叉
- **直跑路径**：`AgentOrchestrator.RunAsync`（合成瞬态 item，不落库）；
- **任务池路径**：`EnqueueAndWaitAsync` → TaskRouter 路由 → 执行器（每轮经 TaskPoolPersistence 落库）。
- 同一循环引擎（RoundLoopDriver）驱动，但装配、取消、持久化三处语义不同（直跑不持久化、无任务键；任务池每轮持久化、暂停门生效）。`AgentFramework` 把两条路径都暴露给调用方，选择责任落在调用方，缺省语义靠注释约定。

### 13.【中】存储桥双入口
- 存储语义（StorageEnvelope / StorageRouteTable）是收敛的单一命令引擎（StorageBridgeCommands），但入口有两个：独立 EXE（Program.cs 参数解析）与进程内 API（DatabaseModule / AgentFramework.Database）。CLI 参数归一逻辑（--id/--key/--resourceType 别名）只在 Program.cs，进程内调用不走同一归一。

### 14.【低】AgentFramework 胖门面
- 60+ 透传成员（Runtime/Database 便捷转发），已从"集中 API"膨胀为"全功能门面"，API 面与实现细节几乎 1:1，后续演进成本高。

---

## 五、安全与门控（架构职责分裂）

### 15.【高】Guardrails 是"伪安全"：子串黑名单 + 与真门控职责分裂
- `GuardFunction` 的输入/输出过滤是**大小写不敏感子串匹配**（`DROP TABLE` / `rm -rf` / `eval(` / `sk-` / `-----BEGIN` / `password` / `secret`）：
  - **高误报**：用户输出含 "password" / "secret" 字样即整段拦截；贴 RSA 公钥（`-----BEGIN`）被拦；
  - **易绕过**：`SELECT/**/FROM`、`rm\ -rf`、Unicode/编码变体全部穿透——这不是安全机制，是"关键词过滤"；
  - **职责分裂**：真正的门控（`GlobalLlmGate`：预算/kill switch/计数）与这个玩具过滤同处 Guardrails 模块，但两者零协同；输入/输出安全本应是架构级横切关注点（经 LlmGate seam 统一挂点），却被做成了工作流里可选的 guard_input/guard_output 函数。
- 建议：要么按威胁模型重做成结构化检测（SQL 注入/提示注入需要解析器或 LLM 判级），要么明确降级为"演示性过滤"并在文档标注非安全边界；输入输出过滤接入 LlmGate 统一咽喉而非独立函数。

---

## 六、架构债务清单（按优先级）

| 优先级 | 问题 | 建议动作 |
|---|---|---|
| P0 | Agent.State 未独立成项目，Axioms 被迫依赖 Runtime | 拆分 Agent.State 项目（最底层），修订分层文档 |
| P0 | StorageBridge 依赖 WinForms Launcher | DatabaseModule/DatabaseBootstrapper 下沉独立 Storage 库 |
| P0 | Guardrails 伪安全过滤 + 职责分裂 | 重构为结构化检测或降级标注；过滤接入 LlmGate |
| P1 | AsyncLocal 隐式上下文 7 通道，作用域/并发脆弱 | 收敛通道数；提供作用域封装（Scope 对象替代裸 Set）；并发 run 显式隔离 |
| P1 | 静态宿主单实例（AgentUiHost.Current） | 以实例参数传递替换静态槽位（或至少支持多槽位 Key 化） |
| P1 | 模块接入全局字符串函数表 | 定义类型化插件接口（IFunctionModule），注册表只存接口实例 |
| P2 | 装配上下文 12 产物无显式依赖图 / 上帝阶段 | 阶段声明 DependsOn 元数据；拆分 ResourceGraph 阶段 |
| P2 | Guardrails → Core 未用 using；csproj 重复引用 | 清理依赖 |
| P2 | 双执行路径取消/持久化语义分叉 | 文档化路径矩阵或收敛单一入口 |

---

*报告生成时间：2026-08-19。行号/文件名基于分析时点源码。*
