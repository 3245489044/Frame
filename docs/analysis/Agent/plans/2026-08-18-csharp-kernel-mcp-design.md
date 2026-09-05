# CSharpKernelMCP — C# 瞬态编译执行 MCP 服务器设计

> **日期**：2026-08-18
> **目标**：把「让 LLM 直接输出**可编译的 C# 代码**，由 Roslyn 在宿主内瞬态编译 / 加载 / 执行并返回强类型结果」这一范式，实现为一个 MCP 服务器（`CSharpKernelMCP`），使 **Cline / Answer / Claude / Trae** 等任意智能体经 MCP 统一调用，并接入 Agent 主循环的**编译验证与自愈闭环**。
> **一句话方案**：新增一个 .NET MCP 服务器（原生 Roslyn 内核，`ModelContextProtocol` SDK），暴露 `compile_csharp` / `compile_and_run_csharp` / `list_api` 三个工具；Agent 经既有 `McpClientManager` / `McpToolFunction` 暴露为 `mcp_call_tool`；`agent-loop.json` 增 `compile_verify` 步骤消费 CodeShape 产物，编译失败 → `ErrorClass.compilation_error` → 预算 / 熔断 → 下一轮 prompt 携带精确 CS 错误（编译器 = 语法约束器）。
> **设计原则**：适配层只做协议转换（MCP 极薄）；编译校验是**共享守卫**（在循环校验阶段），不埋在任一 provider 内；跨轮只传**文本快照**，不传内存引用。

---

## 1. 背景与边界（为何构建为 MCP）

### 1.1 范式来源

`docs/KnowLedge/当 AI 学会编译：重构 C# 与 LLM 的编程新范式.md` 提出「编译时 AI」：LLM 输出 C# 代码 → 宿主内 Roslyn 瞬态编译/执行 → 结果以强类型对象交还调用方。核心抽象为 `T Execute<T>(string codeSnippet, params object[] args)`。

文档 §三 曾**反对**把内核封装成 MCP（性能损耗 + 场景窄化），但那是针对「追求进程内零往返、被多宿主以原生速度复用」的通用内核。本项目真实诉求是**「任意智能体（Cline/Answer/Claude/Trae）都能调用 C# 编译执行能力」**——这恰恰需要**标准互通边界**，而 MCP 就是 Cline / Claude / Trae 都原生支持的这个边界。

### 1.2 三条必须守住的诚实边界（贯穿全文）

| # | 边界 | 在本设计的落实 |
|---|------|----------------|
| B1 | **AI 输入输出永远是文本（Token）** | `compile_and_run_csharp` 返回**序列化结果文本**（stdout + JSON），跨轮拼接进 Prompt；不存在跨轮对象引用 |
| B2 | **编译器只证明「可编译」，不证明「做得对」** | `compile_verify` 只判语法/类型；要逼近「逻辑正确」必须另加**测试断言**，不得用「没抛异常」充当正确 |
| B3 | **信任边界分层** | 语法白名单 = 纵深防御，非沙箱；不可信代码走**进程隔离**；高危操作（反射/unsafe/文件/网络）默认禁止、显式授权 + 全程审计 |

### 1.3 关键取舍（文档 §三 的 MCP 反对 vs 本设计）

| 维度 | 文档反对 MCP 的场景 | 本场景 |
|------|--------------------|--------|
| 复用面 | 需要**多宿主原生库复用** | 需要**任意智能体互通**（MCP 主场地） |
| 性能 | 进程内零往返 | 每次往返毫秒级，相对「编译+运行一段代码」可接受 |
| 场景窄化 | 包装成**单一工具** | 暴露**一组工具 + 资源**，即「编译器服务端」，非单一封装函数 |
| 安全 | 进程内沙箱弱 | MCP 天然进程隔离（信任边界更清晰） |

> 结论：**构建为 MCP 最贴合「任意智能体都能用」的目标，且复用仓库已有的 MCP 客户端/桥接基建。** 若日后有「低延迟原生多宿主复用」硬需求，再按文档 §三 把同一内核下沉为本机库，二者可并存。

---

## 2. 总体架构

```
┌─ 任意智能体（Cline / Claude Desktop / Trae / Agent 自身循环）─────────────┐
│  各自经其 MCP 客户端（stdio JSON-RPC）调用同一工具集                        │
└──────────────┬────────────────────────────────────────────────────────────┘
               │ stdio / JSON-RPC（MCP 协议）
┌──────────────▼────────────────────────────────────────────────────────────┐
│  CSharpKernelMCP（.NET 进程 = MCP 协议 + Roslyn 内核，进程内零跳转）        │
│  ├─ compile_csharp          仅编译校验（语法/类型门）→ 结构化诊断           │
│  ├─ compile_and_run_csharp  编译 → 隔离 AssemblyLoadContext 执行 → 结果文本 │
│  └─ list_api / 资源         暴露入口约定 / 可用程序集 / 白名单（供 LLM 对齐）│
└──────────────┬────────────────────────────────────────────────────────────┘
               │ 接入方式二选一 / 可叠加
┌──────────────▼────────────────────────────────────────────────────────────┐
│  Agent 接入                                                            │
│  (a) 聚合进 MCPBridge（现有单桥连接不变，mcp_call_tool 即达）              │
│  (b) McpClientManager 扩展为多服务器（读 mcp-servers.json）                 │
└──────────────┬────────────────────────────────────────────────────────────┘
               ▼
   Agent 主循环 agent-loop.json
   guard_input → provide_prompt → cline_execute → [compile_verify] → rule_match
      编译失败 ─► ErrorClass.compilation_error ─► 预算/熔断 ─► 下一轮带 CS 错误
```

### 2.1 组件落位

| 组件 | 位置 | 类型 | 说明 |
|------|------|------|------|
| CSharpKernelMCP 服务器 | `TypeScriptPrj/TsWorkspace/CSharpKernelMCP/`（与 MCPBridge 平级） | .NET 可执行项目 | `ModelContextProtocol` SDK + `Microsoft.CodeAnalysis.CSharp`；内核可选复用 `RoslynMethod.Core` 的 `DynamicCompilation`/`CompilationSkeleton` |
| MCPBridge 聚合（可选） | `TypeScriptPrj/TsWorkspace/MCPBridge/` | TS（已有） | 把 csharp-kernel 加入聚合，使单桥连接同时暴露 C# 工具 |
| Agent MCP 客户端 | `Agent/src/Agent.Modules/MCP/McpClientManager.cs`、`McpToolFunction.cs` | 已有 | `mcp_call_tool`/`mcp_list_tools` 已就绪；视接入方式决定是否扩为多服务器 |
| 循环校验步骤 | `Agent/Config/workflows/agent-loop.json` | 已有 | 新增 `compile_verify` 节点（`compile_verify` 函数） |
| 产物契约 | `Agent/src/Agent.Contract/ClineResult.cs`（`AgentExecutorResult`） | 扩展 | 增 `OutputKind` + `CodePayload` |

---
## 3. 服务器工具清单（MCP 工具集）

> 目标：**一组工具 + 资源**，避免「单一封装函数」窄化（文档 §三）。所有工具返回**结构化 JSON**（可被 LLM 与 `rule_match` 稳定解析）。

### 3.1 `compile_csharp`（仅编译校验，无副作用）

- **用途**：语法 / 类型门。LLM 每轮出码后先编译校验，失败即把精确诊断喂回下一轮。
- **入参**：
  | 参数 | 类型 | 必填 | 说明 |
  |------|------|------|------|
  | `source` | string | 是 | C# 源码（可含 using 与多个类型） |
  | `entry` | string | 否 | 期望入口 `Class.Method`（缺省按约定 `Test.Run`/`Program.Run`/`Main`） |
  | `targetFramework` | string | 否 | 缺省 `net10.0-windows`（对齐 Agent） |
- **出参**：
  ```json
  {
    "ok": true/false,
    "compileErrors": [ { "id": "CS0103", "message": "...", "line": 12, "column": 9 } ],
    "entryResolved": "Test.Run",
    "elapsedMs": 42
  }
  ```
- **错误分类映射**：`compileErrors` 非空 → 上游判 `ErrorClass.compilation_error`。

### 3.2 `compile_and_run_csharp`（编译 + 隔离执行）

- **用途**：编译通过后，在隔离 `AssemblyLoadContext` 内执行，返回**序列化结果文本**（边界 B1）。
- **入参**：在 `compile_csharp` 基础上增：
  | 参数 | 类型 | 必填 | 说明 |
  |------|------|------|------|
  | `args` | object[] | 否 | 透传给入口方法（可选） |
  | `timeoutMs` | int | 否 | 执行超时（缺省 30000） |
  | `serialize` | bool | 否 | 是否把返回对象序列化为文本快照（缺省 true） |
- **出参**：
  ```json
  {
    "ok": true,
    "stdout": "24133\n",
    "result": { "value": 24133, "type": "System.Int32" },
    "resultText": "24133",
    "runtimeError": null,
    "timedOut": false,
    "usage": { "compileMs": 38, "runMs": 4 },
    "runId": "5f9c...-guid"       // 审计追踪
  }
  ```
- **语义**：`resultText` = 序列化结果文本（跨轮快照来源）；`stdout` 单独返回便于观测；执行超时置 `timedOut`（上游可归 `ErrorClass.timeout`）。

### 3.3 `list_api` / 资源（供 LLM 对齐契约）

- **用途**：让 LLM 按**服务器暴露的程序集 / 入口约定 / 白名单 / 允许的 using** 出码（对齐 `OutputWellFormedness` / 格式指令的上下文高注意区原则）。
- **出参**：
  ```json
  {
    "entryConventions": ["Test.Run", "Program.Run", "Main"],
    "allowedUsings": ["System", "System.Linq", "System.Collections.Generic"],
    "forbiddenApis": ["System.IO.File", "System.Reflection", "System.Net.Sockets"],
    "sandboxMode": "whitelist"
  }
  ```

### 3.4 资源（Resources）

- `csharp://api`：等价 `list_api` 的结构化资源（可被 MCP 客户端读取）。

### 3.5 明确不放内核的工具

- **`self_heal`**：不放进内核——让 LLM 留在循环内负责「读诊断→改写」，内核保持**纯执行、无 LLM**（职责单一、可审计）。
- **测试断言**：不放内核默认执行，而是由 `compile_verify` 守卫在循环侧**显式传入断言**（边界 B2）。

---

## 4. CodePayload 契约（Agent 侧产物形态）

> 目标：让统一结果 `AgentExecutorResult` 能表达「这是一段可编译的 C#」，供循环 `compile_verify` 识别与消费。**不改动 `IAgentExecutor` 端口本身**，只泛化其结果。

### 4.1 `OutputKind` 判别

```csharp
public enum OutputKind
{
    None,      // 缺省（现有行为）
    Text,      // 纯文本（Answer / 普通聊天）
    Artifact,  // 文件产物 / Diff（Cline 类）
    Code,      // 可编译 C# 片段（新范式）
}
```

### 4.2 `CodePayload`

```csharp
public sealed record CodePayload(
    string Source,                        // C# 源码
    string? Entry,                        // Class.Method；null = 按约定解析
    IReadOnlyList<string>? Usings = null, // 显式 using（可选）
    IReadOnlyList<string>? Assertions = null); // 测试断言源（边界 B2：可编译 ≠ 正确）
```

### 4.3 `AgentExecutorResult` 扩展（向后兼容）

```csharp
record AgentExecutorResult(
    bool Ok, string? Text, string? Error,
    string? SessionId = null, string? OutputState = null,
    int MessageCount = 0, TokenUsage? TokenUsage = null,
    string? StopReason = null,
    OutputKind Kind = OutputKind.None,   // 新增（缺省 None）
    CodePayload? Code = null);           // 新增（仅 Kind==Code 时非空）
```

- **兼容性**：`Kind`/`Code` 均带默认值；现有 Cline/Answer 执行器不填即 `None`，`compile_verify` 对非 `Code` 产物**直通跳过**，零回归。
- **闭环**：CodeShape provider（如 `CsCoderExecutor`）返回 `Kind=Code, Code=payload` → 循环 `compile_verify` 步骤消费。

---

## 5. agent-loop 接入（compile_verify 步骤 + 反馈闭环）

### 5.1 工作流变化（`Agent/Config/workflows/agent-loop.json`）

在 `cline_execute` 与 `rule_match` 之间插入 `compile_verify` 节点：

```json
{
  "Symbol": "process",
  "NodeId": "compile_verify",
  "FuncName": "compile_verify",
  "Label": "编译验证（仅对 OutputKind=Code 产物；文本/产物直通）",
  "ParameterKeys": ["exec_result"],
  "ResultKeys": ["compile_report", "next_prompt_hint"],
  "NextNodeConfig": { "Configs": { "on_success": "rule_match" } }
}
```

### 5.2 `compile_verify` 函数语义

```
compile_verify(exec_result):
  若 exec_result.Kind != Code  → 直通（compile_report=null，不影响 rule_match）
  否则:
    mcp_call_tool(tool=compile_csharp, {source, entry})
    ├─ 编译失败 → 取 compileErrors
    │     → ErrorClass.compilation_error
    │     → 升级 FailureBudget + next_prompt_hint 携带精确 CS 错误（编译器=语法约束器）
    └─ 编译通过 → (可选) 以断言调 compile_and_run_csharp
          → resultText 序列化为文本快照 → next_prompt_hint 携带结果文本（自回归）
```

### 5.3 错误分类与熔断复用（零新增机制）

复用既有链路 `Agent/src/Agent.Contract/ClineResult.cs`（`ClineResultClassifier.ToErrorClass`）：

| 编译/运行结果 | 归因 | ErrorClass | 预算动作 |
|---------------|------|------------|----------|
| 编译失败 | `compileErrors` 非空 | `compilation_error` | 升级（对齐 axioms：编译错误**不豁免**、统一计入） |
| 执行超时 | `timedOut=true` | `timeout` | 升级 |
| 执行异常 | `runtimeError` 非空 | `execution_failure` | 升级 |
| 成功非空 | 结果文本 | `null` | 回退（pass） |
| 成功空 | `resultText` 为空 | `empty_result` | 升级（不静默判成功） |

### 5.4 自愈闭环（文档 §4 / §5.1）

```
第 N 轮: CsCoderExecutor 产出 Code 片段（含可能错误）
  → compile_verify: 编译失败，抓 CS0103...
  → next_prompt_hint: "上一轮代码存在编译错误: CS0103(第12行): 名称'x'不存在…"
  → 第 N+1 轮: LLM 不再是"猜测能否编译"，而是"修正编译器指出的确定性错误"
```

> 边界 B2 注记：仅凭「编译通过」不视为「逻辑正确」。若要验证行为，须显式提供 `Assertions` 并执行测试断言；未提供断言时 `compile_and_run_csharp` 只返回结果文本，不声明正确性。

---

## 6. 配置 Schema

### 6.1 服务器注册（两种接入方式）

**方式 A — 聚合进 MCPBridge（推荐首版，单桥连接不变）**：在 `TypeScriptPrj/TsWorkspace/MCPBridge/mcp-bridge.config.json` 的 `servers` 增加指向 csharp-kernel 的条目（若内核以子进程方式被桥接）。此方式下 Cline / Agent 都经既有单桥拿到工具。

**方式 B — Agent 多服务器客户端（通用化）**：把 `Agent/Config/mcp-servers.json` 扩展为可被 `McpClientManager` 消费的多服务器清单：

```json
{
  "McpServers": [
    { "Name": "mcp-bridge", "Command": "node", "Arguments": [".../MCPBridge/build/index.js"] },
    {
      "Name": "csharp-kernel",
      "Command": "dotnet",
      "Arguments": ["D:/Project/Program/WorkFlowPrj/TypeScriptPrj/TsWorkspace/CSharpKernelMCP/build/CSharpKernelMCP.dll"],
      "Description": "CSharpKernelMCP — C# 瞬态编译执行内核"
    }
  ]
}
```

> 现状核对：`McpClientManager` 当前为**单桥接**（硬编码默认 `MCPBridge/build/index.js`），`mcp-servers.json` 暂未被其读取。方式 B 需对 `McpClientManager` 做「多服务器 + 读清单」扩展（新增 `InitializeFrom(McpServersConfig)`、按 server 建连接/路由 `mcp_call_tool`）。首版建议先走方式 A 验证闭环，方式 B 作为通用化演进。

### 6.2 循环预算相关（`agent-config.json`，可选）

```jsonc
"CompileVerify": {
  "Enabled": true,             // 是否启用 compile_verify 步骤
  "CompileTimeoutMs": 5000,
  "RunTimeoutMs": 30000,
  "ErrorLimit": 20             // 单次喂回下一轮的诊断条数上限（防上下文膨胀）
}
```

### 6.3 CodeShape provider（`agent-config.json` 的 `Agents`，后续）

```jsonc
"Agents": {
  "cs-coder": { "Kind": "code", "Provider": "deepseek", "Model": "deepseek-chat", "ClientType": "openai-responses" }
}
```

> `Kind=code` 由通用 provider 装配把请求路由到 `CsCoderExecutor`（产出 `OutputKind.Code`）。

---

## 7. 安全边界（对齐文档 §六）

| 层 | 措施 |
|----|------|
| 默认禁止 | 反射（`System.Reflection`）、`unsafe`、`System.IO.File` 写删、网络套接字、进程启动 |
| 语法白名单（纵深防御） | 仅允许白名单 using / API；`list_api` 把白名单暴露给 LLM 对齐；**明确不承诺为沙箱** |
| 进程隔离（兜底） | 不可信代码在独立 `AssemblyLoadContext` + 独立进程运行；`--sandbox` 模式收紧；内核进程本身即信任边界 |
| 高危授权 | 高危操作经显式授权门面暴露（如受限 IO 通道），非默认开启 |
| 审计 | 每次编译/运行记 `runId` + source 摘要 + 调用方 + 结果归因，落日志/事件区 |
| 跨轮隔离 | 只返回 `resultText` 文本快照；`CodePayload` 执行产物不跨轮共享内存对象（边界 B1） |
| 上下文防膨胀 | `CompileVerify.ErrorLimit` 截断诊断；`list_api` 收敛 using/API 面 |

### 7.1 热插拔/状态迁移（文档 §五.3 诚实边界）

- `compile_and_run_csharp` 默认运行**无状态纯函数**（静态入口），结果文本回传。
- 持有 DbContext / Service 注入 / 复杂状态机的逻辑，需另行设计状态迁移协议，**不承诺**「零停机秒级替换」。

---

## 8. 落地路线图（实施顺序）

> 每步含可验证产出；按「先闭环、再通用化、再接入外部智能体」推进。

| 步骤 | 内容 | 产出 / 验证 |
|------|------|-------------|
| S1 | 新建 `CSharpKernelMCP`（.NET 可执行）：`ModelContextProtocol` SDK + Roslyn，实现 `compile_csharp` / `compile_and_run_csharp` / `list_api`，隔离 `AssemblyLoadContext` + 白名单 + 审计 | `dotnet run`；`mcp_list_tools` 见三工具；冒烟编译/运行通过 |
| S2 | 接入 Agent 客户端：方式 A 聚合进 MCPBridge（或方式 B 扩展 `McpClientManager` 多服务器 + 读 `mcp-servers.json`） | `mcp_list_tools` 列出 csharp 工具；`mcp_call_tool(tool=compile_and_run_csharp)` 返回结果 |
| S3 | 契约泛化：`AgentExecutorResult` 增 `OutputKind` + `CodePayload`（`Agent/src/Agent.Contract/ClineResult.cs`） | 现有 Cline/Answer 回归 0 改动；非 Code 产物直通 |
| S4 | `compile_verify` 步骤：`agent-loop.json` 插节点 + 实现 `compile_verify` 函数，接 `ErrorClass.compilation_error` → 预算/熔断 → `next_prompt_hint` 带 CS 错误 | 编译失败使 budget 升级、下一轮提示含错误码；成功回退 |
| S5 | CodeShape provider 试点：`CsCoderExecutor`（`IAgentExecutor`，返回 `Kind=Code`）+ `Agents` 配置 `Kind=code` | 端到端：LLM 出码 → 编译 → 结果文本 → 自回归修正 |
| S6 | 接入 Cline / Claude / Trae：各自 MCP 配置注册同一 server（或经 MCPBridge 聚合） | 外部智能体直接调用 `compile_and_run_csharp`，零适配代码 |
| S7 | 安全强化：`--sandbox` 模式、进程隔离选项、审计回流（日志/事件区）、`ErrorLimit` 防膨胀 | 安全清单逐项核对 |

## 9. 明确不做（范围外 / 边界）

| 项 | 说明 |
|----|------|
| 不在内核放 LLM | `self_heal` 由循环内 LLM 承担，内核保持纯执行 |
| 不以「编译通过」断言正确 | 正确性须经显式测试断言（边界 B2） |
| 不承诺进程内沙箱 | 语法白名单是纵深防御；不可信代码走进程隔离（边界 B3） |
| 不承诺有状态热插拔 | 仅无状态纯函数可热替换；有状态逻辑需状态迁移协议 |
| 不改造 `IAgentExecutor` 端口签名 | 只泛化其结果类型，保持现有执行器零改动 |
| 不做通用 provider 注册表（本阶段） | 首版聚焦「编译执行 + 循环闭环」；任意 SDK 可插拔的注册表在 S6 后再评估 |

## 10. 参考

- 范式文档：`docs/KnowLedge/当 AI 学会编译：重构 C# 与 LLM 的编程新范式.md`
- 公理：`ErrorClass`/`ClassifyError`/`FailureSubtyping`（`.clinerules/axioms.md §4`；`compilation_error` 统一计入 FailureBudget、不豁免）
- 错误分类实现：`CsharpPrj/Operation/WorkFlowMethod/LoopFramework/Axioms/ErrorClassifier.cs`
- Agent 主循环：`Agent/Config/workflows/agent-loop.json`
- Agent MCP 客户端：`Agent/src/Agent.Modules/MCP/McpClientManager.cs`、`McpToolFunction.cs`；`Agent/Config/mcp-servers.json`
- MCPBridge 服务器（模式参考）：`TypeScriptPrj/TsWorkspace/MCPBridge/`
- 可选复用内核：`CsharpPrj/Plate/RoslynMethod/RoslynMethod.Core/Compilation/DynamicCompilation.cs`

