# 注册表驱动通用 MCP 框架方案（方案 B）

> **版本**：1.0
> **日期**：2026-09-01
> **状态**：设计定稿（组件已实现并编译通过，宿主装配待落地）
> **范围**：让 MCP **默认调用所有已注册的工作流函数**（基于 `Modules.Registry`），免为每个函数重复构建注册逻辑；调用提示词按 skill 动态组装。采用**方案 B（模块化分离）**：保留现有瘦宿主 `McpServerHosting` 不变，新增「完整装配 MCP 宿主」，复用 FileDriven 的 **IoC 工作流注册**方式使注册表就绪，`registry_*` 工具自动枚举/调用全部已注册函数。
> **核心依据（代码核实）**：
> - 已实现组件：`Agent/src/Agent.Modules/MCP/RegistryMcpTools.cs`、`RegistrySkillProvider.cs`（编译通过）；
> - 现有瘦宿主：`Agent/src/Agent.Launcher/Composition/McpServerHosting.cs`（`RunStdioAsync`/`RunHttpAsync`，answer/cline/local_llm/read_nodes）；
> - 工作流注册 IoC 范例：`FileDriven/FileDriven.Execution/Initial/FrameworkInitializer.cs`（"Registry"/"AppService" 阶段）+ `FileDriven/FileDriven.Business/Registrations/**`（函数库 `RegisterAll(ITrConfigRegistry)`）+ `function-libraries.json`。

---

## 一、背景与目标

### 1.1 目标
把「通过 MCP 调用 Agent 全部已注册工作流函数」收敛为**注册表驱动的通用能力**：

| 需求 | 实现 |
|------|------|
| MCP 默认可调用所有已注册函数 | `registry_list_tools`（自动枚举）+ `registry_call_tool`（按名调用），不重复写注册逻辑 |
| 提示词按 skill 动态组装 | `RegistrySkillProvider`：函数命中 `SKILL.md` 即加载其描述/正文/契约（用到哪些加载哪些） |
| 模块化分离 | 方案 B：新增完整装配宿主，不动现有瘦宿主 |

### 1.2 与既有方案的关系
- 复用本方案第一阶段已实现的 `RegistryMcpTools`（`[McpServerToolType]`，`registry_list_tools`/`registry_call_tool`）与 `RegistrySkillProvider`；
- 本方案补齐其**运行时前置**：宿主进程必须先初始化工作流注册表（`UnifiedRegistryCenter`），否则 `registry_*` 返回「注册表未初始化」。

---

## 二、现状核实

### 2.1 已实现组件
| 文件 | 职责 | 状态 |
|------|------|------|
| `Agent.Modules/MCP/RegistryMcpTools.cs` | `registry_list_tools`（枚举 + skill 组装描述/schema）、`registry_call_tool`（按名执行）；白名单过滤控制面函数 | ✅ 编译通过 |
| `Agent.Modules/MCP/RegistrySkillProvider.cs` | 扫描 `Agent/skills/**/SKILL.md`，按函数名匹配，动态组装 description/契约 | ✅ 编译通过 |
| `Agent.Launcher/Composition/McpServerHosting.cs` | 瘦宿主，已把 `RegistryMcpTools` 加入 `WithTools(new[]{typeof(McpServerTools), typeof(RegistryMcpTools)})` | ✅ 编译通过 |

### 2.2 关键发现：Agent 进程未初始化工作流注册表
`WorkFlowMethodUsage`（`BasicRegistry`/`Executor`/`GetAllFunctionNames()`）经由 `UnifiedRegistryCenter`（`Modules.Registry`）提供，其 `IsInitialized` 默认 `false`，需显式 `Initialize()`（且需先设置 `WorkflowFactory`/`ExecutorFactory`/`PipelineFactory`）。核实结论：

- **Agent 源码无任何 `UnifiedRegistryCenter.Instance.Initialize()` 或 factory 赋值**；
- 因此 Agent 现有 `WorkflowFunctionRegistrar.RegisterMany`（`McpToolFunction.Register`/`LocalDbMcpTools.Register`/`CoreModule`）注册的工作流函数**因注册表未初始化而抛异常，被 `AgentOptionalModules` 的 `FailSilentRegistry` 静默降级吞掉**，从未真正入库；
- 结论：`registry_*` 要枚举到函数，宿主必须先走「工作流注册初始化」。

### 2.3 工作流注册的 IoC 范例：FileDriven `FrameworkInitializer`
工作流注册的 IoC 初始化**已在 FileDriven 落地**（用户确认），是方案 B 复用的依据：

```csharp
// FileDriven/FileDriven.Execution/Initial/FrameworkInitializer.cs —— orchestrator.Register("Registry", ...)
var unifiedRegistry = Modules.Registry.UnifiedRegistryCenter.Instance;
unifiedRegistry.WorkflowFactory   = (funcReg) => new DefaultWorkflowRegistry(funcReg);
unifiedRegistry.ExecutorFactory   = (funcReg) => new ExecutionFramework.Engine.FunctionExecutor(
    funcReg, new ExecutionFramework.Abstractions.NullParameterProcessor(), typeConverter: new TypeConverterAdapter());
unifiedRegistry.PipelineFactory   = (workflows) => new PipelineExecutor(workflows);
unifiedRegistry.Initialize();   // 先初始化 Workflows/Executor/Pipeline，函数库注册才能访问

// 函数库注册：从 function-libraries.json 显式登记加载（含 Agent 侧 AgentUiLibrary 等）
var configPath = Path.Combine(projectRoot, "FileDriven", "DrivenConfig", "Configs", "function-libraries.json");
var results = FunctionLibraryLoader.Load(unifiedRegistry.Functions, configPath, autoDiscover: false);
// ...失败即抛 InitializationException（fail-fast）
```

- 函数库：`FileDriven/FileDriven.Business/Registrations/{BuiltIn,Cline,Math,UI,Workflow}/*.cs`，每个 `RegisterAll(ITrConfigRegistry)` 注册一批工作流函数（`Cline_Ask`/`Cline_Plan`/`Add`/`SetValue` 等）；
- IoC 装配：`AppService` 阶段 `InitializeAppServiceProvider()` → `UnifiedRegistryCenter.Instance.BuildServiceProvider(services)` + `AppServiceProvider.Initialize(sp)`；`PostInitHooks`（如 `FunctionExecutorInjectionHook` 把 `IFunctionExecutor` 注入 `PipelineEntryPool`）。

## 三、方案 B 设计（模块化分离）

### 3.1 架构分层
```
[外部 LLM / Cline 主控]  →  MCP stdio / HTTP
        │  ListTools / CallTool
┌───────────────────────────────────────────────────────┐
│ 完整装配 MCP 宿主 RegistryMcpHosting（新增）            │  ← 方案 B 落地
│  ① 复用 FileDriven IoC 工作流注册初始化                 │
│     - UnifiedRegistryCenter: factory + Initialize()     │
│     - FunctionLibraryLoader.Load(function-libraries.json)│
│     - AppServiceProvider 装配                           │
│  ② AddMcpServer().WithTools<McpServerTools, RegistryMcpTools>() │
└───────────────────────────────────────────────────────┘
        │  依赖：FileDriven.Business 函数库 / WorkFlowMethod / Modules.Registry
[ Modules.Registry（UnifiedRegistryCenter.Functions）]   ← 唯一数据源
        │  枚举 GetAllFunctionNames() / TryGetDescriptor
        │  执行 Executor.ExecuteAsync(name, namedParams)
[ 已注册工作流函数库：Cline_* / Math_* / UI_* / db_* / AgentUi_* ... ]
```

> **模块化分离**：现有瘦宿主 `McpServerHosting`（answer/cline/local_llm/read_nodes，纯 C# 能力）**保持不动**；新增完整装配宿主承载工作流函数面。`registry_*` 工具只在完整装配宿主下可用。

### 3.2 新增完整装配 MCP 宿主（`RegistryMcpHosting`）
新增 `Agent.Launcher/Composition/RegistryMcpHosting.cs`，启动序列：

```csharp
public static class RegistryMcpHosting
{
    public static async Task<int> RunStdioAsync(AgentConfig? config = null, string[]? args = null)
    {
        LogRepository.Enabled = false;                 // MCP stdio：stdout 仅协议帧
        await InitializeWorkflowRegistryAsync();       // ① 复用 FileDriven IoC 工作流注册（见 §3.3）

        var cfg = McpServerHosting.PrepareConfig(config); // ② LLM 网关等
        var builder = Host.CreateApplicationBuilder(args ?? Array.Empty<string>());
        McpServerHosting.RegisterToolServices(builder.Services, cfg);
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(new[] { typeof(McpServerTools), typeof(RegistryMcpTools) });
        await builder.Build().RunAsync();
        return 0;
    }
    // RunHttpAsync 同构（WithHttpTransport + MapMcp）
}
```

### 3.3 复用 FileDriven 工作流注册初始化
为满足「模块化分离 + 通用性」，把 FileDriven `FrameworkInitializer` 的「Registry + AppService」两阶段收敛为可复用初始化器（SingleImplementation / TH-12），供 MCP 宿主与既有 FileDriven 启动共用：

```csharp
// WorkflowRegistryBootstrap（新增，或直接调 FileDriven 现成入口）
public static void InitializeWorkflowRegistry(string? projectRoot)
{
    var ur = Modules.Registry.UnifiedRegistryCenter.Instance;
    ur.WorkflowFactory = f => new DefaultWorkflowRegistry(f);
    ur.ExecutorFactory = f => new ExecutionFramework.Engine.FunctionExecutor(
        f, new NullParameterProcessor(), typeConverter: new TypeConverterAdapter());
    ur.PipelineFactory = w => new PipelineExecutor(w);
    ur.Initialize();

    var cfgPath = Path.Combine(projectRoot ?? ".", "FileDriven", "DrivenConfig", "Configs", "function-libraries.json");
    FunctionLibraryLoader.Load(ur.Functions, cfgPath, autoDiscover: false);  // 注册全部函数库
}
```

> 两种形态任选：**(a)** 直接调用 FileDriven 现成 `FrameworkInitializer`（完整，含 UI/CommandFramework 等非必需项）；**(b)** 抽公共 `WorkflowRegistryBootstrap`（仅注册表 + 函数库，最贴合 MCP 宿主，推荐）。

### 3.4 与瘦宿主并存
| 宿主 | 承载能力 | registry_* 可用 |
|------|----------|----------------|
| `McpServerHosting`（保留） | answer/cline/local_llm/read_nodes（纯 C# 能力） | 否（注册表未初始化，返回「未初始化」诊断） |
| `RegistryMcpHosting`（新增） | 工作流函数面（全部已注册库） | ✅ 是 |

---

## 四、调用链
```
外部 LLM → list_tools
  → RegistryMcpTools.RegistryListTools
    → WorkFlowMethodUsage.GetAllFunctionNames()      // Modules.Registry 已初始化，含全部函数库
    → 白名单过滤（控制面）→ RegistrySkillProvider.BuildDescription（skill 动态组装）
    → 返回 [{name, description, schema}]
外部 LLM → call_tool(registry_call_tool, {functionName, arguments})
  → RegistryMcpTools.RegistryCallTool
    → WorkFlowMethodUsage.HasFunction(name) → Executor.ExecuteAsync(name, namedParameters)
    → FunctionExecutor → 注册函数执行 → 返回 {ok, value, error}
```

## 五、通用性（新增函数零改造）
- 新增工作流函数 = 在 `function-libraries.json` 登记库（或 IoC 注册）→ `registry_list_tools` **自动暴露**，`registry_call_tool` 可调，**零 MCP 代码**；
- 提示词随 `SKILL.md` 更新自动变化（`RegistrySkillProvider` 惰性加载 + 缓存）；
- 通用框架：任何基于 `Modules.Registry` 注册的函数库（Cline/Math/UI/Agent 侧）自动成为 MCP 工具。

## 六、安全与控制面边界
| 边界 | 做法 |
|------|------|
| 控制面不外包 | `RegistryMcpTools.InternalFunctionPrefixes` 黑名单过滤（`guard_*`/`rule_match`/`provide_prompt`/`cline_execute`/`agent_loop` 等），确定性核心不暴露 |
| 失败语义 | `registry_call_tool` 未注册/白名单外/参数非法均返回明确错误 JSON，不抛、不阻断 |
| 审计 | 工具调用写日志/事件（`IObservabilitySink`/`ur_events`），外部调用可追溯 |
| 权限 | MCP 宿主仅本机受信访问（HTTP 只绑 localhost，对齐 `McpServerHosting` 既有约束） |

## 七、skill 动态组装
- `RegistrySkillProvider.Match(functionName)`：① `SKILL.md` 的 `trigger:` 精确命中 → ② skill.Name 与函数名互相包含 → ③ 标签命中；
- `BuildDescription`：函数自身描述 + 命中技能的描述/请求契约（`schema:`）/正文摘要（限长），**用到哪些加载哪些**；
- 目录缺失/无命中 → 空（回退函数描述 + 宽松 schema，不抛、不阻断）。

## 八、落地路径
| Phase | 内容 | 验收 |
|------|------|------|
| **P0** | 抽 `WorkflowRegistryBootstrap`（或复用 FileDriven 现成入口），在宿主初始化 `UnifiedRegistryCenter` + `FunctionLibraryLoader.Load` | 完整装配下 `WorkFlowMethodUsage.GetAllFunctionNames()` 返回非空（含 Cline_*/Math_*/db_*/AgentUi_*） |
| **P1** | 新增 `RegistryMcpHosting.RunStdioAsync/RunHttpAsync`（§3.2），`WithTools<McpServerTools, RegistryMcpTools>` | `tools/list` 返回 `registry_list_tools`/`registry_call_tool`，且 `registry_list_tools` 能列出全部已注册函数 |
| **P2** | 白名单校验 + 审计 + skill schema 契约（可选：把 `schema:` 映射成结构化 JSON Schema） | 控制面不可达；skill 描述随匹配函数动态变化 |
| **P3** | 冒烟：`registry_call_tool` 调用 `Cline_Version`/`db_list_nodes`/`Add` 等真实函数 | 返回结构化结果，失败明确诊断 |

## 九、权衡
| 决策点 | 取舍 |
|--------|------|
| 完整装配宿主依赖 | 需可加载 `FileDriven.Business` 函数库 + `function-libraries.json` 登记的各 DLL（含 `Agent.Launcher.dll` 的 `AgentUiLibrary`）；缺失即 fail-fast（对齐 FileDriven 现有策略） |
| 复用 vs 抽公共初始化 | 抽 `WorkflowRegistryBootstrap` 最贴合 MCP 宿主（仅注册表+函数库）；直接调 `FrameworkInitializer` 简单但带入 UI/CommandFramework 等非必需项 |
| 注册表数据源 | `Modules.Registry`（工作流引擎注册表）是唯一真源；Agent 既有 `RegisterAsyncFunction` 需在注册表初始化后才真正生效 |
| 瘦/完整双宿主 | 职责清晰、零影响既有；代价是两套 MCP server 进程/端点 |

---

## 十、一句话总结
> **注册表驱动通用 MCP 框架（方案 B）= 保留瘦宿主，新增「完整装配 MCP 宿主」`RegistryMcpHosting`，复用 FileDriven 的 IoC 工作流注册（初始化 `UnifiedRegistryCenter` + `FunctionLibraryLoader.Load(function-libraries.json)`）使 `Modules.Registry` 就绪，配合已实现的 `RegistryMcpTools`（`registry_list_tools`/`registry_call_tool`，白名单过滤）与 `RegistrySkillProvider`（skill 动态组装提示词）——任何注册进工作流注册表的函数自动成为 MCP 工具，新增函数零改造，框架通用性极强。**



