# Agent 自由调用注册函数 —— skill 实时装配方案（非传统 MCP 注册）

> **版本**：1.0　**日期**：2026-09-01
> **状态**：已实施（P0-P3 落地：编译通过 + 冒烟通过）
> **范围**：让 **Agent 框架自身主循环（SK 会话折叠算子）自由调用 FileDriven 侧注册的工作流函数**，且因函数量庞大**不以传统 MCP 逐个静态注册**，改为**以 skill 实时装配运行**（用到哪些加载哪些，工具面 O(1) 恒定）。
> **关联**：`doc/注册表驱动通用MCP框架方案.md`（registry_* 设计）、`doc/MCP调用路径与装配.md`（双向 MCP 链路）、`clinerules`（SingleImplementation/TH-12/TH-13 冗余消除）。

---

## 一、背景与目标

### 1.1 目标

把「Agent 框架自由调用 FileDriven 侧注册函数」收敛为 **skill 驱动的实时装配能力**，不随函数量增长而膨胀 LLM 工具面：

| 需求 | 实现 |
|------|------|
| Agent 主循环 LLM 可自由调用任意白名单注册函数 | 通用调度工具 `registry_call`（进程内直调 `WorkFlowMethodUsage.Executor`） |
| 函数量庞大，**不逐个静态注册 MCP 工具** | 工具面恒定 3 个通用工具，不逐函数注册、不静态枚举 |
| **以 skill 实时装配**（用到哪些加载哪些） | `registry_search` 命中才用 `RegistrySkillProvider` 实时组装函数描述/请求契约；可选首用物化缓存 |
| 复用既有能力，不新增第三套副本 | 抽公共 `RegistryToolService`，服务端 `RegistryMcpTools` 与进程内 `RegistryFoldToolProvider` 共用（SingleImplementation / TH-12） |

### 1.2 与既有方案的关系

- 复用 `注册表驱动通用MCP框架方案.md` 已实现的 `RegistryMcpTools`（`registry_list_tools`/`registry_call_tool`）、`RegistrySkillProvider`（skill 动态组装）、`WorkflowRegistryBootstrap`（注册表初始化引导）；
- 本方案**不新增服务端**，而是把「注册表驱动通用调用 + skill 组装」**下沉/复用到 Agent 自身 LLM 工具面**（方向 A 内部），做成**进程内直调**（不经 MCP 协议）。

---

## 二、现状核实

### 2.1 已具备的组件

| 文件 | 职责 | 状态 |
|------|------|------|
| `Agent.McpRegistryHost/RegistryMcpTools.cs` | `registry_list_tools`（枚举 + skill 组装描述/schema）、`registry_call_tool`（按名执行）；`InternalFunctionPrefixes` 黑名单过滤控制面 | ✅ 已实现（方向 B 服务端工具面） |
| `Agent.McpRegistryHost/RegistrySkillProvider.cs` | `Match`（按函数名匹配技能）+ `BuildDescription`（函数描述 + 技能契约/正文组装），惰性缓存 | ✅ 已实现（当前只服务 `registry_list_tools` 的 description） |
| `WorkflowRegistryBootstrap.cs`（`Agent.McpRegistryHost/`，已从 `FileDriven/Agent.McpRegistryHost.App/` 下沉） | 初始化 `UnifiedRegistryCenter` + `FunctionLibraryLoader.Load(function-libraries.json)` | ✅ 已实现（完整装配宿主用） |
| `Agent.McpRegistryHost/McpFoldToolProvider.cs` | 桥 `McpClientManager`（外部 MCPBridge/Node servers）→ `LlmTool`，供 SK 折叠算子 Function Calling | ✅ 已实现（桥的是**外部 Node/Docker MCP 工具**，非 C# 注册函数） |

### 2.2 关键差距：Agent 自身主循环调不到 FileDriven 注册函数

- `ConversationHosting.TryBuildFoldOperator`（`Agent.Launcher/Composition/ConversationHosting.cs:199-216`）只装配了 `McpFoldToolProvider`（桥外部 MCPBridge）注入 `SkConversationFoldOperator.toolProvider`；
- FileDriven 侧注册函数（`FileDriven.Business/Registrations/**`，经 `function-libraries.json` 16 个库登记）**只经方向 B 服务端 `registry_call_tool` 暴露**，外部 LLM 可调，**Agent 自身主循环调不到**；
- **前置缺陷**（`注册表驱动通用MCP框架方案.md §2.2` 已核实）：纯 Agent 进程未初始化工作流注册表 → `WorkflowFunctionRegistrar.RegisterMany` 注册的函数因注册表未初始化抛异常、被 `FailSilentRegistry` 静默吞掉、从未真正入库 → 必须先 `WorkflowRegistryBootstrap.Initialize()`。

### 2.3 函数量级佐证

`FileDriven/DrivenConfig/Configs/function-libraries.json` 登记 16 个函数库（Math/Workflow/BuiltIn/MathBrowser/FunctionTester/UI/WorkflowLibrary/Cline/AgentUi/RuleMatch/Prompt/Guard/Mcp/UiCatalog/Engine 等），单库函数数十计——**不适合逐个静态注册为 MCP 工具**（工具列表爆炸 + schema 污染 + 上下文膨胀）。

---

## 三、核心思路

> **把「注册表驱动通用调用 + skill 实时组装」从方向 B 服务端下沉/复用到 Agent 自身 LLM 工具面（方向 A 内部），做成进程内直调，工具面 O(1) 恒定，不随函数量增长。**

```
┌─ 方向 A 内部（Agent 自身主循环 / SK 折叠算子）──────────────────────┐
│  SkConversationFoldOperator                                          │
│    └─ toolProvider: RegistryFoldToolProvider（新增）                 │
│         ├─ registry_search   // 按 query/prefix/module/skill 命中 → skill 实时组装描述/契约
│         └─ registry_call     // 白名单校验 → WorkFlowMethodUsage.Executor 进程内直调
│                  │
│                  ▼
│  Modules.Registry（UnifiedRegistryCenter，WorkflowRegistryBootstrap 已初始化）
│    └─ FileDriven.Business/Registrations 注册函数（全量入库，免逐个 MCP 注册）
└──────────────────────────────────────────────────────────────────────┘
```

---

## 四、方案

### 4.1 新增组件：`RegistryFoldToolProvider : IMcpFoldToolProvider`

放置于 `Agent.McpRegistryHost`（对齐 `McpFoldToolProvider` 形态），暴露给 SK 折叠算子 LLM **恒定 3 个通用工具**（非几百个静态函数工具）：

| 工具 | 入参 | 行为 |
|------|------|------|
| `registry_search` | `query`/`prefix`/`module`/`skill` | `WorkFlowMethodUsage.GetAllFunctionNames()` → `RegistryToolService.IsInternal` 白名单过滤 → 按关键词/前缀/模块/技能匹配 → 对命中函数用 `RegistrySkillProvider.BuildDescription` **实时组装描述/请求契约** → 只返回命中项（不返回全部） |
| `registry_call` | `functionName` + `arguments`(JSON 对象) | 白名单校验 → `WorkFlowMethodUsage.Executor.ExecuteAsync(name, args)` **进程内直调**（不经 MCP 协议） |
| （可选）skill `executor` 路由 | — | 复用 `SkConversationFoldOperator` 既有 `IFoldInProcessExecutor` 进程内直调路径，无需新工具 |

### 4.2 抽公共调用核：`RegistryToolService`

抽公共 `RegistryToolService`（Search/Call/IsInternal/Describe），供：
- 服务端 `RegistryMcpTools`（`registry_list_tools`/`registry_call_tool`）；
- 进程内 `RegistryFoldToolProvider`（`registry_search`/`registry_call`）；

共用同一白名单与描述组装逻辑（SingleImplementation / TH-12，消除 `RegistryCallTool` 逻辑双份）。`RegistryMcpTools` 收敛为只保留 MCP 工具形态外壳，核心逻辑下沉到 `RegistryToolService`。

### 4.3 skill 实时装配语义（落地"以 skill 装配运行"）

- skill（`SKILL.md`，`SkillCatalog` 解析）承载"**如何调用某类注册函数**"的操作指南 + 请求契约（`schema:` front-matter）；
- LLM 面对任务 → `registry_search` 按 task 关键词搜 → 命中函数描述已含 skill 契约（`RegistrySkillProvider.BuildDescription`）→ 按契约 `registry_call`；
- **可选增强（首用物化）**：首次命中某 skill/函数时**动态物化**一个带完整 schema 的 `LlmTool` 加入当前步工具集并缓存复用；默认不静态枚举 → 既"实时装配"又不爆炸。

### 4.4 装配接缝（改动点）

1. **抽 `RegistryToolService`**（§4.2）；
2. **`ConversationHosting.TryBuildFoldOperator`**：把 `RegistryFoldToolProvider` 与现有 `McpFoldToolProvider` **并列组合**注入 `SkConversationFoldOperator.toolProvider`（skill 决定用哪个执行端）；
3. **`WorkflowRegistryBootstrap` 下沉**：已从 `FileDriven/Agent.McpRegistryHost.App` 下沉到 `Agent.McpRegistryHost` 库（消除 Launcher→App 依赖反转）；`RegistryToolService.EnsureRegistryInitialized()` **惰性初始化注册表**（首次 `registry_search` 才加载，失败降级不抛，不阻塞纯瘦宿主）。

---

## 五、调用链

```
Agent 主循环（SK 折叠算子）LLM
  → registry_search {query:"Add"}   // 注册表已初始化；命中 → RegistrySkillProvider 组装描述/契约
  → registry_call {functionName:"Add", arguments:"{\"a\":1,\"b\":2}"}
  → RegistryToolService.Call → 白名单校验 → WorkFlowMethodUsage.Executor.ExecuteAsync("Add", params)
  → FileDriven.Business MathLibrary.Add 执行 → 返回 {ok, value, error}
```

---

## 六、通用性（新增函数零改造）

- 新增工作流函数 = 在 `function-libraries.json` 登记库（或 IoC 注册）→ `registry_search` **自动可搜**，`registry_call` 可调，**零 MCP 代码**；
- 提示词随 `SKILL.md` 更新自动变化（`RegistrySkillProvider` 惰性加载 + 缓存）；
- 通用框架：任何基于 `Modules.Registry` 注册的函数库（Cline/Math/UI/Workflow/Agent 侧）自动成为 Agent 主循环可自由调用的能力。

---

## 七、安全与控制面边界

| 边界 | 做法 |
|------|------|
| 控制面不外包 | 复用 `RegistryToolService.IsInternal` 黑名单（`guard_*`/`rule_match`/`provide_prompt`/`cline_execute`/`agent_loop` 等），确定性核心不进入 Agent 自身工具面 |
| 进程内直调白名单 | 与方向 B 服务端共用同一白名单（SingleImplementation / TH-12），无旁路 |
| 失败语义 | `registry_call` 未注册/白名单外/参数非法均返回明确错误 JSON，不抛、不阻断 |
| 审计 | 工具调用写日志/事件（`IObservabilitySink`/`ur_events`），进程内调用可追溯 |
| 注册表未初始化 | `registry_search`/`registry_call` 返回「注册表未初始化」诊断，不抛、不阻断 |

---

## 八、落地路径

| Phase | 内容 | 验收 |
|------|------|------|
| **P0** | 抽 `RegistryToolService`（Search/Call/IsInternal/Describe），`RegistryMcpTools` 收敛为工具形态外壳 | ✅ 已实施：服务端 `registry_*` 行为不变（App 编译通过 + smoke 通过） |
| **P1** | 实现 `RegistryFoldToolProvider : IMcpFoldToolProvider`（`registry_search`/`registry_call` + skill 组装 + 白名单） | ✅ 已实施：编译通过；工具面恒定 3 工具 |
| **P2** | `WorkflowRegistryBootstrap` 下沉 Agent 库 + `ConversationHosting.TryBuildFoldOperator` 并列装配（惰性初始化注册表） | ✅ 已实施：Launcher 编译通过；`toolProvider` 注入 `CompositeFoldToolProvider` |
| **P3** | 冒烟：主循环 answer 步骤 LLM 经 Function Calling 走 `registry_search`→`registry_call` 执行真实函数 | ✅ 已实施：`--smoke --call Add` 返回 `ok=True value=3`；工具列表不随函数量增长 |

---

## 九、权衡

| 决策点 | 取舍 |
|--------|------|
| 通用工具 vs 专用工具 | 通用工具让 LLM 多一步搜索（`registry_search`→`registry_call`），换取 O(1) 工具面与免静态注册；高频函数可走"首用物化缓存"降低搜索开销 |
| 惰性初始化注册表 | 首次 `registry_search` 有一次性的注册表加载（毫秒级），换取不阻塞纯瘦宿主启动 |
| 进程内直调 vs 套 MCP | 进程内直调零 JSON-RPC 绕行、零跨进程开销，对齐 `McpServerHosting`"进程内直调不经 MCP 协议"原则；代价是不经统一 MCP 协议边界，但同一进程内本无协议必要 |
| 下沉 `WorkflowRegistryBootstrap` | 下沉到 Agent 库消除依赖反转，代价是 Agent 侧引入 FileDriven 函数库 DLL 依赖（对齐方案文档 §九完整装配宿主依赖） |

---

## 十、一句话总结

> **Agent 自由调用注册函数（skill 实时装配方案）= 复用已实现的 `RegistryMcpTools`/`RegistrySkillProvider`/`WorkflowRegistryBootstrap`，抽公共 `RegistryToolService`，新增进程内 `RegistryFoldToolProvider : IMcpFoldToolProvider`，把「注册表驱动通用调用 + skill 实时组装」下沉到 Agent 自身主循环 LLM 工具面（方向 A 内部）；工具面恒定 3 个通用工具，不逐函数静态注册 MCP，`registry_search` 命中才用 `RegistrySkillProvider` 实时组装描述/契约、`registry_call` 进程内直调——任何注册进工作流注册表的函数自动成为 Agent 可自由调用的能力，新增函数零改造，工具面不随函数量增长。**

