# Agent 侧 MCP 调用路径与装配

> **版本**：1.0　**日期**：2026-09-01
> **范围**：Agent（`Projects/Agent`）进程作为 **MCP 客户端**调用外部工具、与作为 **MCP server** 被外部 LLM 调用的全部路径；含主对话循环（`conversation/send`）触达 MCP 的两条路径。
> **原则**：收拢聚拢点（SingleImplementation / TH-12）＋模块化分离（客户端方向 / 服务端方向各自收敛，装配归宿主组合根）。
> **关联**：`doc/注册表驱动通用MCP框架方案.md`（registry_* 设计）、`doc/MCP服务化接入方案.md`、`doc/2026-08-18-csharp-kernel-mcp-design.md`。

---

## 一、总览：MCP 双方向

MCP 在本仓库存在**方向相反**的两条链路，机制互不冲突：

```
┌─ 方向 A（Agent 作为 MCP 客户端）───────────────────────────────┐
│  Agent 进程（C#）                                              │
│    McpClientManager (ModelContextProtocol.Client 官方 SDK)      │
│      │ stdio JSON-RPC                                          │
│      ▼                                                         │
│    MCPBridge (Node 宿主机进程, TypeScriptPrj/.../MCPBridge)     │
│      │ HTTP/SSE (localhost:3100-3107)                          │
│      ▼                                                         │
│    外部 MCP servers（Docker workflow-mcp-env 内）                │
│      sequential-thinking / context7 / memory / lexis-mcp …     │
└───────────────────────────────────────────────────────────────┘

┌─ 方向 B（Agent 作为 MCP server，被外部调用）────────────────────┐
│  外部 LLM（Cline / Claude 作主控）                              │
│      │ stdio 或 HTTP (localhost:3200/mcp)                      │
│      ▼                                                         │
│  Agent MCP server                                              │
│    瘦宿主 McpServerHosting → answer / cline / local_llm / read_nodes
│    完整装配宿主 RegistryMcpHosting → registry_list_tools / registry_call_tool
└───────────────────────────────────────────────────────────────┘

> 一句话：**方向 A** = Agent 去调外部的 MCP 工具（进站），**方向 B** = 外部经 MCP 来调 Agent 的能力（出站）。

---

## 二、方向 A：Agent 作为 MCP 客户端（调用外部 MCP 工具）

### 2.1 组件

| 组件 | 位置 | 职责 |
|------|------|------|
| `McpClientManager` | `Agent.McpRegistryHost/McpClientManager.cs` | MCP 客户端门面。用官方 `McpClient`（ModelContextProtocol SDK）承载 stdio/JSON-RPC，连接 MCPBridge；`ListToolsAsync` / `CallToolAsync` / `ConnectAsync`。单桥懒连接。 |
| `McpToolFunction` | `Agent.McpRegistryHost/McpToolFunction.cs` | 注册**工作流函数** `mcp_call_tool` / `mcp_list_tools`（`WorkflowFunctionRegistrar.RegisterMany`），供会话工作流步骤按函数名调用。 |
| `McpFoldToolProvider` | `Agent.McpRegistryHost/McpFoldToolProvider.cs` | 把 `McpClientManager` 桥接为 `LlmTool` 工具集，供 SK 折叠算子（`SkConversationFoldOperator`）经 Function Calling 调用。 |
| `LocalDbMcpTools` | `Agent.Modules/MCP/LocalDbMcpTools.cs` | 本地数据库读写工具面（`db_*`），复用 mcp_call_tool 注册模式。 |

### 2.2 调用链（两条使用面）

```
McpClientManager.ConnectAsync → stdio → MCPBridge/build/index.js → HTTP/SSE → 外部 servers
    ▲
    ├── McpToolFunction.CallToolAsync / ListToolsAsync   （工作流函数面：mcp_call_tool / mcp_list_tools）
    └── McpFoldToolProvider.GetToolsAsync / CallAsync    （LLM 工具面：LlmTool → SK 折叠算子 Function Calling）
```

### 2.3 装配（组合根）

`AgentOptionalModules.Register`（`Agent.Launcher/Composition/AgentOptionalModules.cs`）在 `config.RegisterMcpTools`（默认 `true`）时：

```
new McpClientManager()
  → orchestrator.RegisterDisposable(AsyncDisposeAdapter(manager))   // 生命周期
  → McpToolFunction.Initialize(manager)                            // 注入 manager
  → McpToolFunction.Register()                                     // 注册 mcp_call_tool/mcp_list_tools
  → LocalDbMcpTools.Register()                                     // 注册 db_*
```
- **懒连接**：`McpToolFunction.GetClientAsync` 首次调用才 `ConnectAsync`（信号量串行），不阻塞宿主启动。
- 主对话循环的 answer 子智能体侧：`ConversationHosting.TryBuildFoldOperator` 用 `new McpFoldToolProvider()` 注入 `StatelessAnswerExecutor(llm, extraToolsProvider: mcp.GetToolsAsync)`。

### 2.4 配置

| 配置文件 | 关键节 | 说明 |
|----------|--------|------|
| `Projects/Agent/Config/agent-config.json` | `McpBridge` | `NodePath=node`、`BridgeScriptPath=TypeScriptPrj/TsWorkspace/MCPBridge/build/index.js`、`RequestTimeoutSeconds=60`、`ProcessStartTimeoutMs=30000`、`DisconnectTimeoutMs=5000`、`ToolCacheTtlSeconds=60` |
| `Projects/Agent/Config/mcp-servers.json` | `McpServers` | 声明 mcp-bridge（`node …/MCPBridge/build/index.js`） |
| `TypeScriptPrj/TsWorkspace/MCPBridge/mcp-bridge.config.json` | `servers` | 聚合的**外部** MCP 端点：3100-3107（sequential-thinking/context7/context-mode/memory/lexis-mcp/token-savior-recall/csproj-context）＋ `csharp-agent`（3200，`enabled:false`）。`docker.enabled=false` 默认不启 Docker，仅聚合已运行端点。 |

│  外部 LLM（Cline / Claude 作主控）                              │
│      │ stdio 或 HTTP (localhost:3200/mcp)                      │
│      ▼                                                         │
│  Agent MCP server                                              │
│    瘦宿主 McpServerHosting → answer / cline / local_llm / read_nodes
│    完整装配宿主 RegistryMcpHosting → registry_list_tools / registry_call_tool
└───────────────────────────────────────────────────────────────┘
```

> 一句话：**方向 A** = Agent 去调外部的 MCP 工具（进站），**方向 B** = 外部经 MCP 来调 Agent 的能力（出站）。

---

## 三、方向 B：Agent 作为 MCP server（被外部调用）

### 3.1 瘦宿主 `McpServerHosting`

`Agent.McpRegistryHost/McpServerHosting.cs`：
- `PrepareConfig(config)`：装配统一 LLM 网关（`LlmHosting.Configure`）+ 配置绑定（`agent-config.json` → `Llm/StatelessAnswer/Agents`）。
- `RegisterToolServices(services, cfg, nodeOutputs?)`：注入执行端 DI——`ILlmClient`（local_llm）、`StatelessAnswerExecutor`（answer）、`ClineAgentExecutor`（cline）、可选 `INodeOutputStore`（read_nodes）。
- `RunStdioAsync` / `RunHttpAsync`（默认 `http://localhost:3200/mcp`，`LlmEndpoints.DefaultMcpPort=3200`，`--port` 可覆盖）：`WithTools(McpServerTools, RegistryMcpTools)`。

**暴露工具面**（`McpServerTools`）：`answer` / `cline` / `local_llm` / `read_nodes`，入参对齐会话折叠算子 `{question/system/conversationContext/stepKey/stateKey}`。

### 3.2 完整装配宿主 `RegistryMcpHosting`（注册表驱动通用 MCP 框架）

`Projects/PrjBridge/Agent.McpRegistryHost.App`（`Program.cs` / `RegistryMcpHosting.cs`；`WorkflowRegistryBootstrap` 在 `Agent.McpRegistryHost` 库）：

```
WorkflowRegistryBootstrap.Initialize()
  → UnifiedRegistryCenter.Instance 装配（WorkflowFactory/ExecutorFactory/PipelineFactory）
  → FunctionLibraryLoader.Load(function-libraries.json)   // 全部已注册函数库入库
McpServerHosting.RegisterToolServices(...)                 // answer/cline/local_llm 执行端 DI
AddMcpServer().WithTools(McpServerTools, RegistryMcpTools) // 暴露 registry_* 服务端
```

**暴露工具面**（`RegistryMcpTools`）：
- `registry_list_tools`：自动枚举全部已注册（非控制面）工作流函数，含 skill 动态组装的描述/schema。
- `registry_call_tool(functionName, arguments)`：按名调用注册表工作流函数。
- **控制面黑名单**（不暴露）：`guard_*` / `rule_match` / `provide_prompt` / `cline_execute` / `agent_loop` / `sk_plan` / `sk_chat` / `mcp_call_tool` / `mcp_list_tools` / `db_summary`。
- 用途：外部 LLM 经 `registry_call_tool("AgentUi_ConversationSend", …)` 可远程驱动主对话循环。

### 3.3 stdio / HTTP 传输

| 传输 | 入口 | 端点/边界 |
|------|------|-----------|
| stdio | `RunStdioAsync` | stdout 仅供 JSON-RPC，禁 LogRepository 业务日志 |
| HTTP | `RunHttpAsync` | `localhost:3200/mcp`（仅回环，无鉴权；仅本机受信客户端） |
| 冒烟 | `--smoke [--call X --args {}]` | 初始化注册表 + 枚举函数 + 可选调用真实函数，不进 MCP 循环 |
| 枚举 | `--list-tools [--prefix X]` | 输出白名单内函数清单（schemaOnly） |


---

## 四、主对话循环调用 MCP 的路径

主对话循环链路：`AgentRuntimeUI.json ⑦ 主对话框 → conversation/send → AgentUi_ConversationSend → ConversationPipeline → 会话工作流（RoundSpec）→ 子智能体`。它有**两条**触达外部 MCP 的路径：

### 4.1 路径①：answer 子智能体经 SK 折叠算子 Function Calling

```
conversation/send (agent=answer)
  → ConversationPipeline → SkConversationFoldOperator.ExpandAsync
  → AnswerFoldExecutor(new StatelessAnswerExecutor(llm, extraToolsProvider: mcp.GetToolsAsync))
  → ChatWithToolsAsync（Function Calling）→ LlmTool
  → McpFoldToolProvider.GetToolsAsync / CallAsync
  → McpClientManager.CallToolAsync → MCPBridge → 外部 MCP server
```
- 装配点：`ConversationHosting.TryBuildFoldOperator`（`Agent.Launcher/Composition/ConversationHosting.cs`）——answer 与 cline 共用同一外部 MCP 工具面。
- `cline` 子智能体：经 `ClineAgentExecutor` 走 Cline 自身执行端；其外部 MCP 工具面取决于 Cline daemon 配置，不经本仓库 `McpFoldToolProvider`。

### 4.2 路径②：会话工作流显式 `mcp_call_tool` 步骤

```
会话 RoundSpec 某步 funcName = "mcp_call_tool"
  → RoundStepInvoker 按 FuncName 命中注册函数
  → McpToolFunction.CallToolAsync（工作流函数面）
  → McpClientManager.CallToolAsync → MCPBridge → 外部 MCP server
```
- 现成范式：`FileDriven/DrivenConfig/WorkFlow/McpToolWorkflow.json`（`mcp_list_tools` → `mcp_call_tool`）。
- 把该步放进会话 RoundSpec 即可让主循环在链上调用外部 MCP 工具。
- 前置：`AgentOptionalModules.RegisterMcpTools` 已装配 MCP 客户端（默认 true，懒连接）。

> **默认不触发**：默认会话模板（`conversation/send` → cline/answer 单步）不含 MCP 步骤；路径①仅当 answer 的 SK 折叠算子实际发起工具调用、且 MCPBridge 就绪时才发生。

---

## 五、关键文件索引

| 文件 | 职责 |
|------|------|
| `Agent.McpRegistryHost/McpClientManager.cs` | MCP 客户端门面（官方 SDK + stdio + MCPBridge） |
| `Agent.McpRegistryHost/McpToolFunction.cs` | 工作流函数 `mcp_call_tool` / `mcp_list_tools` |
| `Agent.McpRegistryHost/McpFoldToolProvider.cs` | MCP → `LlmTool` 桥（SK 折叠算子用） |
| `Agent.McpRegistryHost/McpServerTools.cs` | 服务端工具 `answer/cline/local_llm/read_nodes` |
| `Agent.McpRegistryHost/RegistryMcpTools.cs` | 服务端 `registry_list_tools` / `registry_call_tool` |
| `Agent.McpRegistryHost/McpServerHosting.cs` | 瘦宿主（stdio/HTTP + 执行端 DI） |
| `Agent.McpRegistryHost/RegistrySkillProvider.cs` | skill 动态组装描述/schema |
| `Agent.Launcher/Composition/AgentOptionalModules.cs` | 客户端装配（RegisterMcpTools） |
| `Agent.Launcher/Composition/ConversationHosting.cs` | 主循环折叠算子 + McpFoldToolProvider 注入 |
| `Projects/PrjBridge/Agent.McpRegistryHost.App/{Program.cs,RegistryMcpHosting.cs}` | 完整装配宿主（registry_*，Exe 组合根；与 FileDriven 解耦，随 PrjBridge 侧独立构建） |
| `Agent.McpRegistryHost/WorkflowRegistryBootstrap.cs` | 工作流注册表初始化引导（FileDriven 零改动；按仓库路径加载 function-libraries.json） |
| `FileDriven/DrivenConfig/Configs/function-libraries.json` | 函数库清单（含 `McpToolFunction`） |
| `TypeScriptPrj/TsWorkspace/MCPBridge/` | Node 桥（宿主机，聚合外部 servers） |
| `TypeScriptPrj/TsWorkspace/MCPFramework/` | MCP 框架（TS 侧基础设施） |
| `DevEnvironment/Docker/mcp-gateway/` | Docker 侧 MCP 网关（TS） |
| `Agent/Config/agent-config.json` `mcp-servers.json` | 客户端/桥配置 |
| `MCPBridge/mcp-bridge.config.json` | 聚合的外部 MCP 端点表 |

---

## 六、启动与验证命令

```bash
# 完整装配宿主（registry_* 服务端），默认 stdio
dotnet run --project Projects/PrjBridge/Agent.McpRegistryHost.App

# HTTP 服务端（localhost:3200/mcp）
dotnet run --project Projects/PrjBridge/Agent.McpRegistryHost.App -- --http

# 冒烟：初始化注册表 + 枚举全部已注册工作流函数（可选 --call 调真实函数）
dotnet run --project Projects/PrjBridge/Agent.McpRegistryHost.App -- --smoke --call Add --args "{\"a\":1,\"b\":2}"

# 枚举白名单内函数（schemaOnly）
dotnet run --project Projects/PrjBridge/Agent.McpRegistryHost.App -- --list-tools --prefix db_

# 客户端侧：确认 MCPBridge 可连接（外部 servers 须已就绪）
node TypeScriptPrj/TsWorkspace/MCPBridge/build/index.js
```

---

## 七、安全边界

- **HTTP server 仅绑 `localhost`（回环）**，无鉴权——仅限本机受信客户端（P3-2 约束；跨进程/主机须先补认证）。
- **控制面不外包**：`registry_*` 黑名单过滤确定性核心/执行链函数（`guard_*`/`cline_execute`/`agent_loop` 等），防外部经 MCP 越权改预算/触达控制面。
- **审计**：工具调用写日志/事件（`IObservabilitySink` / `ur_events`），外部调用可追溯。
- **失败语义**：`mcp_call_tool` / `registry_call_tool` 未注册/白名单外/参数非法均返回明确错误 JSON，不抛、不阻断。
- **MCPBridge 默认不启 Docker**：仅聚合已运行端点；`docker.enabled=false` 需显式开启才管理容器。

---

## 八、原则对齐

| 原则 | 落地 |
|------|------|
| 收拢聚拢点 | 客户端/服务端各收敛到单一门面（`McpClientManager` / `RegistryMcpTools`），`McpFoldToolProvider` 与 `McpToolFunction` 均复用 `McpClientManager`（SingleImplementation / TH-12） |
| 模块化分离 | 客户端方向（进站）与服务端方向（出站）正交；装配归宿主组合根（`AgentOptionalModules` / `McpServerHosting` / `RegistryMcpHosting`）；`McpServerTools` 纯库无循环依赖，宿主只装配 |

