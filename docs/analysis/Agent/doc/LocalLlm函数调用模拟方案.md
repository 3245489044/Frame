# LocalLlm「返回结果可选择可配置」—— 支持真实模拟基于 LLM 的 MCP/工具调用

> **版本**：1.0　**日期**：2026-09-01
> **范围**：改进 `Agent.Contract/Llm/LocalLlmClient`，使本地后端返回结果**可选择可配置**（text / json / **tool_call**），
> 从而能在本地（provider=local，零真实 API）**真实模拟基于 LLM 的 Function Calling / MCP 调用**，为「历史主循环经 skills/mcp 创建 task」
> 提供可离线复现的 LLM 决策层。
> **状态**：Phase 1（返回能力）✅ 已实施 + 单测通过；Phase 1.5（进程内工具执行）✅ 已实施 + 单测通过（`agent_mcpinvoke` 已改为走 LLM 决策）；
> Phase 2（wire 端到端）✅ 已实施 + 集成测试通过（`LocalLlmHttpIntegrationTests.WireTools_*`，见 §四/§五）。

---

## 一、现状限制（为什么"无法真实模拟"）

| 层 | 现状 | 结论 |
|----|------|------|
| **返回载体** | `LocalLlmClient` 三入口都返回**纯字符串** `LlmResult.Content`（`ResolveResponse` 只产 `string`），无「返回一次工具调用」的形态 | 只能"说"不能"调" |
| **`LlmResult` 模型** | 只有 `Content / IsSuccess / Error`，无 `ToolCalls` | 无法承载 LLM 自主的工具决策 |
| **规则** | `LocalLlmRule.Mode ∈ {text, json}`，无 `tool_call` | 不可选择"这次返回一个 MCP 调用" |
| **tools 传递** | `ChatWithToolsAsync` 走接口默认实现 → 回退 `ChatAsync`，**tools 被忽略** | Function Calling 在本地不可演练（`本地联调与上下文传递.md` 局限表第 6 条） |
| **工具循环位置** | `OpenAiCompatibleTransport.ChatWithToolsAsync`（Plate）已实现完整工具循环（发→解析 tool_calls→执行→回填→再发） | 循环唯一实现，应**复用**而非复制 |
| **HTTP 边界** | `OpenAiCompatibleHost` 后端委托 `(messages, hop, ct)`，**不接 tools**；响应只序列化 content 文本 | 即使后端想返回 tool_calls 也无法透传 |

> 一句话：本地 LLM 只能返回字符串、且工具集在 HTTP 边界被丢弃 → 无法复现「LLM 决定调用某个 MCP/工具」的链路。

---

## 二、已落地（Phase 1）：返回结果可选择可配置

改动集中在 `Agent.Contract`（自包含，构建 0 警告 0 错误，单测 5/5 通过）。

### 2.1 规则模型扩展（`LocalLlmRule`）

```csharp
// Mode 三态：text（默认）| json | tool_call
public string? Mode { get; set; } = "text";
public string? ToolName { get; set; }     // Mode=tool_call：要调用的工具名
public string? ToolArguments { get; set; } // Mode=tool_call：传给工具的参数 JSON（缺省 "{}"）
```

### 2.2 结果模型扩展（`LlmResult` / 新增 `LlmToolCall`）

```csharp
public sealed record LlmResult(
    string? Content, bool IsSuccess = true, string? Error = null,
    IReadOnlyList<LlmToolCall>? ToolCalls = null);   // 位置参数追加，既有调用点零破坏

public sealed record LlmToolCall(string Name, string Arguments, string? Id = null);
```

### 2.3 客户端能力（`LocalLlmClient.ChatWithToolsAsync` 覆写）

- 命中 `Mode=tool_call` 规则 **且调用方传入的同名工具存在** → 返回 `LlmResult(ToolCalls=[{Name, Arguments}], Content=null)`；
- 否则回退后续规则 / 默认文本（**不产生悬空工具调用**，`ChatAsync` 纯聊天永远不触发，零回归）；
- `CallLog`（`LocalLlmCallRecord.ToolCalls`）记录每次工具调用，审计可观测。

### 2.4 配置示例（`agent-config.json` → `AgentConfig:LocalLlm.Rules`）

```jsonc
"LocalLlm": {
  "DefaultResponse": "（本地后端）已处理问题：{user}",
  "Rules": [
    {
      "Match": "regex:创建任务|create task",
      "Mode": "tool_call",
      "ToolName": "create_node_reference",
      "ToolArguments": "{\"TaskKey\":\"tp_local_mcp\",\"UserInput\":\"由 LocalLlm 决策经 MCP 技能创建的任务\"}"
    }
  ]
}
```

> 语义：当回答/会话主循环问 LocalLlm「创建任务」时，LocalLlm **返回一次 `create_node_reference` 工具调用**（即"LLM 决定调用 MCP 技能"），下游执行该调用即可创建 task。

---

## 三、Phase 1 验证

`CentralTest/AgentTest/Agent.Core.Tests/LocalLlmToolCallTests.cs`（5 用例全绿）：

| 用例 | 断言 |
|------|------|
| 规则命中 + 工具已提供 | `ToolCalls[0].Name/Arguments` 正确，`Content=null` |
| 未传 tools | 回退默认文本，无悬空工具调用 |
| 工具名不匹配 | 回退默认文本 |
| `ChatAsync`（无工具通道） | 永不触发工具调用（零回归） |
| `CallLog` | 记录本次工具调用（审计） |

---

## 三.5、Phase 1.5（已实施）：进程内工具执行 —— LLM 决策 → 真实模拟调用

> 为使本地 LLM 决策返回的工具调用被**真正执行并回喂**（不改动 Plate wire 层），已新增**共享工具执行器** `LlmToolExecutor`
> 并把它接入折叠算子与 answer 执行器；`create_node_reference` 同时以 `LlmTool`（Function Calling 工具面）暴露。本地路径真实执行；
> 真实后端（wire 层 transport 已内部执行工具）返回 `ToolCalls=null` → 原样返回最终文本，零回归。

| 改动 | 位置 | 说明 |
|------|------|------|
| 新增 `LlmToolExecutor.ExecuteToolCallsAsync` | `Agent.Contract/Llm/LlmToolExecutor.cs` | 执行 `LlmResult.ToolCalls`（按传入 `LlmTool` 集，缺失/失败→诊断不抛）；无 ToolCalls→原样返回 Content |
| `SkConversationFoldOperator.ChatStepAsync` 接入 | `Agent.Core/Signal/` | 工具调用结果 → `LlmToolExecutor` 执行并回填 |
| `StatelessAnswerExecutor.ChatAsync` 接入 | `Agent.Modules/Answer/` | 同上（answer 执行端） |
| `create_node_reference` 以 `LlmTool` 暴露 | `Agent.Launcher/Composition/ConversationHosting.cs` | `CreateNodeReferenceTool(taskGraph)`：入参 CreateTaskGraphRequest → TaskGraphService 直调；注入复合工具面 + answer 额外工具 |
| `CompositeFoldToolProvider` 支持额外工具源 | `Agent.McpRegistryHost/` | 并入 create_node_reference LlmTool |
| `LocalLlm` 预置 tool_call 规则 | `Projects/Agent/Config/agent-config.json` | `Mode=tool_call, ToolName=create_node_reference, ToolArguments=CreateTaskGraphRequest` |

> 由此 `agent_mcpinvoke` 已改为**走 LLM 决策**：自然语言 → LocalLlm 命中 tool_call 规则返回预置调用参数 → `LlmToolExecutor` 真实模拟调用 → 创建 task。

---

## 四、Phase 2（已实施）：wire 端到端 —— 复用 Plate 工具循环（SingleImplementation）

> Phase 1.5 已提供**进程内**「LLM 决策 → 执行」闭环；若需跨进程 / 对任意 OpenAI 兼容客户端同样生效，再走 **wire 路径**
> **复用 `OpenAiCompatibleTransport` 已实现的工具循环**（对齐 SingleImplementation / TH-12），避免再复制循环。

```
调用方 OpenAiCompatibleLlmClient(BaseUrl=LocalLlmHttp)
  → 把 LlmTool 映射 OpenAiTool（含 ExecuteAsync）
  → OpenAiCompatibleTransport 工具循环：带 tools 请求 LocalLlmHttp
  → 【改动①】OpenAiCompatibleHost 解析 tools → 透传工具名给后端委托
  → 【改动②】LocalLlmHttpHost.BackendAsync 转发工具名 → LocalLlmClient.ChatWithToolsAsync
       （LocalLlm 命中 tool_call 规则 → 返回 LlmResult.ToolCalls）
  → 【改动③】LocalLlmHttpHost 把 ToolCalls 映射回 OpenAiChatResult.Ok(null, toolCalls)
  → 【改动④】OpenAiCompatibleHost 把 tool_calls 序列化进 chat.completion.message
  → 传输层解析 tool_calls → 执行 OpenAiTool.ExecuteAsync → 回填 tool 结果 → 再发
  → LocalLlm 命中最终文本规则 → 返回终答
```

### 改动点清单

| 位置 | 改动 | 说明 |
|------|------|------|
| `Plate/HttpMethod/Host/OpenAiCompatibleHost.cs` | 解析 `tools[].function.name`；后端委托签名加 `IReadOnlyList<string> toolNames`；响应 `message` 加 `tool_calls` 序列化 | wire 层只透传名字+回写调用，**不执行**（执行在客户端 transport） |
| `Agent.Modules/LocalLlmHttp/LocalLlmHttpHost.cs` | `BackendAsync` 收 toolNames → `ChatWithToolsAsync`（本地已支持）；`LlmResult.ToolCalls → OpenAiChatResult.ToolCalls` | 桥接 |
| `Agent.Contract/Llm/OpenAiCompatibleLlmClient.cs` | 已复用 `OpenAiCompatibleTransport`（工具循环现成），无需改 | — |

> 改动点清单为已实施状态（§四 改动①-④ + 额外⑤），全部落位：
> - 【①/④】`OpenAiCompatibleHost` 解析 `tools[].function.name` → 后端委托签名加 `IReadOnlyList<string> toolNames`；响应 `message` 按 OpenAI 标准回写 `tool_calls`（客户端 transport 解析执行；stream + tool_calls 显式报错，工具循环固定 stream=false）。
> - 【②/③】`LocalLlmHttpHost.BackendAsync` 收 toolNames → 构造占位 LlmTool → 走 `ChatWithToolsAsync` 子通道；`AdaptAsync` 把 `LlmResult.ToolCalls` 映射回 `OpenAiChatResult.Ok(null, toolCalls)`。
> - 【⑤】`LocalLlmClient.ResolveResponse` 工具调用去重：messages 已含 `role=tool` 结果 → 不再重复返回该调用（对齐真实模型见到工具结果后停止的语义，使 wire 多轮循环闭环；进程内 stateless-answer 只调一次无 `role=tool`，零回归）。
> 因改动触及 Plate 共享 wire 层（真实后端同样经它），已同步更新 `OpenAiCompatibleHostSmokeTests` 构造签名并补 tools/tool_calls 冒烟；真实后端路径（无 tools → 原 `ChatMessagesAsync`/hop 传播）行为保持不变。

---

## 五、验收标准

| 阶段 | 验收 |
|------|------|
| Phase 1 | `LocalLlmToolCallTests` 5/5 绿；`Agent.Contract` 构建 0 警告 0 错误 |
| Phase 1.5 | `LocalLlmToolCallTests` 8/8 绿（含 `LlmToolExecutor` 3 用例）；`Agent.Launcher` 构建 0 警告 0 错误 |
| 端到端 | 会话主循环（conversation/send，answer）在 LocalLlm 下**走 LLM 决策**：自然语言 → LocalLlm 命中 tool_call 规则 → `LlmToolExecutor` 真实模拟调用 `create_node_reference` → 创建 task（`tp_llm_mcp_demo`），读 ur_tasks 验证（`agent_mcpinvoke` 测试） |
| Phase 2（已实施） | 本地 OpenAI 兼容客户端（BaseUrl=LocalLlmHttp）带 tools 调 `create_node_reference`：返回 `tool_calls` → transport 执行 → 回喂 tool 结果 → LocalLlm 去重落终答（`LocalLlmHttpIntegrationTests.WireTools_*` 绿，含 `OpenAiCompatibleHostSmokeTests` tools/tool_calls 冒烟） |

---

## 六、一句话总结

> **LocalLlm 改进 = 把返回结果从「纯字符串」扩展为「可选择可配置（text / json / tool_call）」**：Phase 1 已给 `LlmResult` 加 `ToolCalls`、给 `LocalLlmRule` 加 `Mode=tool_call` + `ToolName/ToolArguments`、并覆写 `ChatWithToolsAsync`（命中规则且工具存在才返回工具调用，否则零回归回退）——让本地 LLM 能像真实模型一样**自主决策返回一次 MCP/工具调用**；Phase 2 再经 LocalLlmHttp 桥接复用 `OpenAiCompatibleTransport` 既有工具循环，即可端到端真实模拟「LLM 决定调用 → 执行 → 回喂 → 终答」的基于 LLM 的 MCP 调用。
