# FileDriven 集成 Agent 方案

> 本文档记录如何在 FileDriven 框架中集成 Agent 模块，使 JSON 工作流能够调用 Agent 函数。

## 1. 背景

### 1.1 两个注册体系的关系

FileDriven 和 Agent 使用**同一个底层注册表**：

```
FunctionStorageRegistry.Registry (Modules.Registry.FunctionRegistry)
    ├── FileDriven 层注册：MathLibrary、BuiltInUnitsLibrary、UIToolbox、WorkflowLibrary 等
    │   （通过 FileDrivenLifecycle.Initialize() 注册）
    │
    ├── FunctionStorage 层注册：MathFunctionRegistrar 等
    │   （通过 FunctionStorageLifecycle.Initialize() 注册）
    │
    └── Agent 层注册：agent_loop_entry、sk_plan、mcp_call_tool 等
        （通过 WorkFlowMethodUsage.RegisterAsyncFunction() 注册）
```

关键连接点：

- `FunctionStorageLifecycle.Initialize()` 设置 `WorkFlowMethodUsage.DefaultBasicRegistryProvider = () => _registry`
- `WorkFlowMethodInitializer.Initialize()` 调用 `WorkFlowMethodUsage.Initialize(basicRegistry: FunctionStorageRegistry.Registry)`
- `WorkFlowMethodUsage.RegisterAsyncFunction()` 内部调用 `_workflowRegistry!.Register(name, delegate)`，而 `_workflowRegistry` 底层就是 `FunctionStorageRegistry.Registry`

**结论：Agent 通过 `RegisterAsyncFunction` 注册的函数，已经自动对 `FunctionStorageRegistry.ExecuteAsync()` 可见，无需额外桥接。**

### 1.2 工作流执行链路

```
JSON 工作流定义
    ↓ (WorkflowExecutionLayer 解析)
步骤: { funcName: "agent_loop_entry", ... }
    ↓ (WorkflowExecutionLayer.ExecuteStepsCore)
FunctionStorageRegistry.ExecuteAsync("agent_loop_entry", args)
    ↓ (FunctionExecutor)
WorkFlowMethodUsage 注册的 Func<WfContext, Task<object>>
    ↓
AgentLoopFunction.ExecuteEntryAsync(WfContext)
```

## 2. 集成步骤

### 2.1 在 FileDrivenLifecycle 中注册 Agent 函数

**文件：** `FileDriven/FileDriven.Business/FileDrivenLifecycle.cs`

在 `Initialize()` 方法末尾添加 Agent 模块注册：

```csharp
public void Initialize()
{
    if (_initialized) return;

    // ... 现有注册代码 ...

    // ★ 注册 Agent 函数到共享的 FunctionRegistry
    //    Agent 函数通过 WorkFlowMethodUsage.RegisterAsyncFunction() 注册，
    //    底层使用同一个 FunctionStorageRegistry.Registry 实例。
    //    因此 FileDriven 工作流可以直接通过 funcName 引用 Agent 函数。
    Agent.Core.AgentLoopFunction.Register();      // agent_loop_entry, condition_check, dynamic_workflow_executor
    Agent.Planner.SKPlannerFunction.Register();   // sk_plan, sk_chat
    Agent.McpTools.McpToolFunction.Register();    // mcp_call_tool, mcp_list_tools
    Agent.Guardrails.GuardFunction.Register();    // guard_input, guard_output

    _initialized = true;
}
```

> **注意：** 需要添加对 Agent 项目的引用。如果 `FileDriven.Business.csproj` 不能直接引用 Agent，可以通过以下方式解耦：
> - 方式 A：在 `FileDriven.Business.csproj` 中添加 `<ProjectReference Include="..\Agent\src\Agent.Core\Agent.Core.csproj" />`
> - 方式 B：通过反射在运行时动态注册（适用于 Agent 可选加载的场景）

### 2.2 创建 Agent 工作流 JSON

**文件：** `FileDriven/DrivenConfig/WorkFlow/AgentWorkflow.json`

#### 示例 1：简单调用 — 直接调用 Agent 入口

```json
{
  "description": "Agent 智能体工作流 — 调用 Agent 执行复杂任务",
  "workflowName": "AgentWorkflow",
  "displayName": "Agent 智能体工作流",
  "version": "1.0",
  "defaultInput": {
    "userInput": "{\"message\": \"帮我分析这段代码...\", \"max_iterations\": 10}"
  },
  "steps": [
    {
      "stepName": "Agent 执行",
      "funcName": "agent_loop_entry",
      "inputKeys": ["userInput"],
      "outputKey": "agentResult",
      "parameters": ["${userInput}"]
    }
  ],
  "outputMapping": {
    "result": "agentResult"
  }
}
```

#### 示例 2：多步骤 — 安全过滤 + LLM 规划 + 条件判断

```json
{
  "description": "安全增强的 Agent 工作流 — 输入过滤 → LLM 规划 → 条件判断 → 动态执行",
  "workflowName": "SafeAgentWorkflow",
  "displayName": "安全 Agent 工作流",
  "version": "1.0",
  "defaultInput": {
    "userInput": "{\"message\": \"帮我重构这段代码...\"}"
  },
  "steps": [
    {
      "stepName": "输入安全过滤",
      "funcName": "guard_input",
      "inputKeys": ["userInput"],
      "outputKey": "safeInput",
      "parameters": ["${userInput}"]
    },
    {
      "stepName": "LLM 规划",
      "funcName": "sk_plan",
      "inputKeys": ["safeInput"],
      "outputKey": "planResult",
      "parameters": ["${safeInput}"]
    },
    {
      "stepName": "条件判断",
      "funcName": "condition_check",
      "inputKeys": ["planResult"],
      "outputKey": "decision",
      "parameters": ["${planResult}"]
    },
    {
      "stepName": "输出安全过滤",
      "funcName": "guard_output",
      "inputKeys": ["planResult"],
      "outputKey": "finalResult",
      "parameters": ["${planResult}"]
    }
  ],
  "outputMapping": {
    "safe_result": "finalResult",
    "plan": "planResult"
  }
}
```

#### 示例 3：MCP 工具调用 — 直接使用 MCP 工具

```json
{
  "description": "MCP 工具调用工作流 — 列出并调用 MCP 工具",
  "workflowName": "McpToolWorkflow",
  "displayName": "MCP 工具调用工作流",
  "version": "1.0",
  "defaultInput": {
    "toolName": "sequential-thinking",
    "toolArgs": "{\"thought\": \"分析这个问题的步骤...\"}"
  },
  "steps": [
    {
      "stepName": "列出 MCP 工具",
      "funcName": "mcp_list_tools",
      "inputKeys": [],
      "outputKey": "toolsList",
      "parameters": []
    },
    {
      "stepName": "调用 MCP 工具",
      "funcName": "mcp_call_tool",
      "inputKeys": ["toolName", "toolArgs"],
      "outputKey": "toolResult",
      "parameters": ["${toolName}", "${toolArgs}"]
    }
  ],
  "outputMapping": {
    "tools": "toolsList",
    "result": "toolResult"
  }
}
```

### 2.3 更新 UI 配置

**文件：** `FileDriven/DrivenConfig/JsonConfig/WorkflowRunner.json`

在 `workflowComboBox.items` 数组中添加新的工作流名称：

```json
"items": [
    "DeepSeekChatWorkflow",
    "BatchRefactorWorkflow",
    "CodeReviewWorkflow",
    "NLToCodeWorkflow",
    "MathOperationsWorkflow",
    "AgentWorkflow",          // ← 新增
    "SafeAgentWorkflow",      // ← 新增
    "McpToolWorkflow"         // ← 新增
]
```

## 3. Agent 函数清单

| 函数名 | 所属模块 | 签名 | 说明 |
|--------|---------|------|------|
| `agent_loop_entry` | Agent.Core | `Func<WfContext, Task<object>>` | Agent 主循环入口，初始化对话历史 |
| `condition_check` | Agent.Core | `Func<WfContext, Task<object>>` | 检查 LLM 输出是否表示任务完成 |
| `dynamic_workflow_executor` | Agent.Core | `Func<WfContext, Task<object>>` | 执行 LLM 生成的动态工作流片段 |
| `sk_plan` | Agent.Planner | `Func<WfContext, Task<object>>` | LLM 规划（Auto Function Calling） |
| `sk_chat` | Agent.Planner | `Func<WfContext, Task<object>>` | 纯文本聊天，无工具调用 |
| `mcp_call_tool` | Agent.McpTools | `Func<WfContext, Task<object>>` | 调用 MCP 工具 |
| `mcp_list_tools` | Agent.McpTools | `Func<WfContext, Task<object>>` | 列出所有可用 MCP 工具 |
| `guard_input` | Agent.Guardrails | `Func<WfContext, Task<object>>` | 输入安全过滤 |
| `guard_output` | Agent.Guardrails | `Func<WfContext, Task<object>>` | 输出安全过滤 |

## 4. 注意事项

### 4.1 函数签名兼容性

Agent 函数通过 `WorkFlowMethodUsage.RegisterAsyncFunction()` 注册，签名统一为 `Func<WfContext, Task<object>>`。

`WorkflowExecutionLayer` 执行步骤时调用 `FunctionStorageRegistry.ExecuteAsync(name, args)`，其中 `args` 是 `object[]` 数组。

`FunctionExecutor` 的参数匹配机制需要能将 `object[]` 参数正确映射到 `WfContext` 类型。如果参数匹配失败，需要在工作流步骤的 `parameters` 中显式构造 `WfContext` 所需的参数结构。

### 4.2 MCPBridge 生命周期

Agent 的 MCP 工具依赖 MCPBridge 子进程。MCPBridge 的启动方式有两种：

| 方式 | 说明 | 适用场景 |
|------|------|---------|
| **懒加载** | 首次调用 `mcp_call_tool` 或 `mcp_list_tools` 时自动启动 MCPBridge | FileDriven 启动快，Agent 功能按需加载 |
| **预加载** | FileDriven 初始化时一并启动 MCPBridge | 需要 Agent 功能立即可用 |

推荐使用**懒加载**方式，在 `McpToolFunction` 首次被调用时通过 `McpClientManager.ConnectAsync()` 启动 MCPBridge。

### 4.3 项目引用

如果 `FileDriven.Business.csproj` 需要直接引用 Agent 项目，添加：

```xml
<ItemGroup>
    <ProjectReference Include="..\Agent\src\Agent.Core\Agent.Core.csproj" />
    <ProjectReference Include="..\Agent\src\Agent.Planner\Agent.Planner.csproj" />
    <ProjectReference Include="..\Agent\src\Agent.McpTools\Agent.McpTools.csproj" />
    <ProjectReference Include="..\Agent\src\Agent.Guardrails\Agent.Guardrails.csproj" />
</ItemGroup>
```

如果希望解耦（Agent 可选加载），可以通过反射在运行时动态注册：

```csharp
private void TryRegisterAgentFunctions()
{
    try
    {
        var agentLoopType = Type.GetType("Agent.Core.AgentLoopFunction, Agent.Core");
        var registerMethod = agentLoopType?.GetMethod("Register", Type.EmptyTypes);
        registerMethod?.Invoke(null, null);
    }
    catch
    {
        // Agent 模块不可用，静默跳过
    }
}
```

## 5. 验证方法

### 5.1 验证函数注册

在 FileDriven 初始化完成后，检查 Agent 函数是否已注册：

```csharp
var registered = FunctionStorageRegistry.GetRegisteredNames();
var agentFunctions = registered.Where(n => n.StartsWith("agent_") || n.StartsWith("sk_") || n.StartsWith("mcp_") || n.StartsWith("guard_"));
Console.WriteLine($"Agent 函数: {string.Join(", ", agentFunctions)}");
```

### 5.2 验证工作流执行

通过 WorkflowRunner UI 选择 `AgentWorkflow`，输入参数后点击执行，验证：
1. 工作流步骤能否正确找到 `agent_loop_entry` 函数
2. 函数执行结果是否正确返回
3. 输出映射是否正确

### 5.3 验证 MCP 工具

通过 `McpToolWorkflow` 验证：
1. `mcp_list_tools` 能否返回 MCP 工具列表
2. `mcp_call_tool` 能否正确调用指定工具

---

> **文档版本：** v1.0
> **创建日期：** 2026-07-06
> **适用范围：** FileDriven + Agent 集成
