# 三层工作流 MCP 封装设计（collect → analyze → act）

> **版本**：0.1
> **日期**：2026-08-20
> **状态**：设计规划（尚未落地实现）
> **范围**：把「CLine 收集信息 → Answer 分析信息 → Cline 执行任务」三层工作流模板，封装为 **MCP 工具面**（基于 `MCPFramework`），供外部智能体（Cline 主控 / 脚本 / 其它 LLM）按标准 MCP 协议（工具名 + JSON 参数）调用；同时作为《Task即对话·对话驱动主循环外置设计》（v0.4）中"会话工作流模板"的**外部可编程入口**。控制面（五步骨架 / 预算 / 熔断 / 暂停 / 写透对账）**保留在 Agent 框架内**，不外包。
> **依据（代码核实）**：
> - **MCPFramework（TS 服务器框架）**：`TypeScriptPrj/TsWorkspace/MCPFramework/`——`src/core/server.ts`（`createMcpServer`/`startServer`）、`src/core/registry.ts`（`tools.config.json` 定义 + 懒加载 `src/tools/{handler}.ts`（导出 `handle`）+ `registerBusinessService`/`getBusinessService` 注入业务）、`tools-definitions/*.json`（单一数据源）、`templates/project/`（脚手架，`npm run generate` 生成 `tools.config.json` + handlers）
> - **三层工作流模板（唯一权威）**：主文档《Task即对话·对话驱动主循环外置设计》§四（`RoundSpec` collect→analyze→act，复用 `RoundSpecLoader`/`WorkflowRunner`/`RoundStepInvoker`；`ConversationPipeline` 信号驱动推进）
> - **Agent action 面（服务面底座）**：`AgentUi_RunJson`（`AgentUiLibraryRuntime.RunJsonAsync`，`RunRequest` 按 `action` 分发）+ `Agent.StorageBridge.exe`（独立进程写入口，`Invoke-AgentStep` 动作协议）
> - **MCP 桥接现状**：`Agent/Config/mcp-servers.json`（注册 MCP server）+ `Agent.Modules/MCP/McpClientManager.cs`/`McpToolFunction.cs`（`mcp_call_tool`/`mcp_list_tools`）
> - **既有服务化思路**：《MCP服务化接入方案.md》（薄封装 `AgentUi_RunJson` action 面，只透传不复制、控制面留框架、可开关可回退）

---

## 一、背景与目标

### 1.1 为什么三层要封装为 MCP

对话 UI 内嵌调用三层（会话工作流）已由主文档覆盖——用户在主对话框发消息，`ConversationPipeline` 沿三层工作流链推进。但**外部智能体**（Cline 作为主控 / 脚本 / 其它 LLM）还需要一个**标准、语言无关、白名单式**的编程入口，用"工具名 + JSON 参数"驱动"收集→分析→执行"这一通用能力。

三层是**通用可复用资产**（收集信息 → 分析信息 → 执行任务），不应只藏在对话 UI 内部；封装为 MCP 工具面后：
- 工具清单 = 明确的能力白名单（比 skill 提示词的软约束强）；
- 外部智能体无需理解任何 C# / 工作流内部细节；
- 对话 UI 与外部智能体**复用同一三层模板**（单一来源，不双真源）。

### 1.2 目标

1. 基于 `MCPFramework` 新建一个 MCP 服务器工程 `collect-analyze-act-mcp`，暴露 `caa_*` 工具面；
2. 工具 handler 只**透传** Agent action 面（`RunJsonAsync`/`StorageBridge`），不复制分发与落库逻辑（SingleImplementation）；
3. 控制面（五步骨架 / 预算 / 熔断 / 暂停 / 写透对账）留在 Agent 框架内，MCP 层无任何裁决逻辑；
4. 可开关可回退，不删不改现有 API。

---

## 二、与对话主循环的关系（单一来源）

| 维度 | 对话驱动视角（主文档 v0.4） | 工具面视角（本文档） |
|------|------------------------------|----------------------|
| 入口 | 用户在主对话框发消息（外部输入信号） | 外部智能体调 MCP 工具（`caa_*`） |
| 触发 | `ConversationPipeline.RunSignalAsync` | MCP Server → handler → BusinessService → Agent action 面 |
| 三层模板 | 权威：`RoundSpec` collect→analyze→act | **复用同一模板**，不重复定义 |
| 主循环 | 通用编排器（不限制层数） | 单步工具可任意组合；`caa_run` 固化三层整链 |
| 控制面 | 框架内 | 框架内（MCP 只透传） |

> **三层模板唯一权威 = 主文档**（`collect-analyze-act.json`）；本文档只描述**如何把它暴露为 MCP 工具面**，不重新定义三层语义。

---

## 三、工具面设计

暴露 **3 个单步工具**（能力面，供外部自由编排）+ **1 个组合工具**（把三层整链固化为一次调用）。

| 工具名 | handler | 职责 | 关键参数 | 调 Agent action 面 |
|---|---|---|---|---|
| `caa_collect` | `collect` | Cline 收集信息 | `source`（节点/输入）、`question`、`workflowId` | `task op=enqueue(executor=cline)` / `query` |
| `caa_analyze` | `analyze` | Answer 分析信息 | `collected`（上层产物）、`question` | `task op=enqueue(executor=stateless-answer)` |
| `caa_act` | `act` | Cline 执行任务 | `conclusion`（分析结论）、`target` | `task op=enqueue(executor=cline)` / `node op=write` |
| `caa_run` | `run-collect-analyze-act` | 一次跑三层整链 | `source`、`question`、`target`、`sessionName` | `ConversationPipeline` / 依次三步（复用主文档时序） |

**设计原则**：
- 三个单步工具 = 三层各自的能力面，外部智能体可自由编排（先 collect 再 analyze，或反复 analyze）；
- `caa_run` = 把默认模板 `collect-analyze-act.json` 固化为**一次调用返回各层结果**，最贴合"主循环跑三层、由子循环结果触发"；
- 工具 handler 只做参数校验 + 透传，**不复制**三层推进逻辑（推进逻辑在 `ConversationPipeline`/工作流模板）。

---

## 四、工程结构（基于 MCPFramework 模板）

新建 `TypeScriptPrj/TsWorkspace/MCPFramework/collect-analyze-act-mcp/`（从 `templates/project/` 复制脚手架）：

```
collect-analyze-act-mcp/
├─ package.json                     # name: collect-analyze-act-mcp；npm run generate / build / start
├─ tools-definitions/               # ★ 单一数据源（工具定义）
│   ├─ caa-collect.json
│   ├─ caa-analyze.json
│   ├─ caa-act.json
│   └─ caa-run.json
├─ tools.config.json                # 由 npm run generate 生成（勿手改）
├─ src/
│   ├─ index.ts                     # createMcpServer + registerBusinessService(business) + startServer
│   ├─ core/business.ts             # BusinessService：桥接 Agent action 面（RunJsonAsync/StorageBridge）
│   └─ tools/                       # 由 generate 生成（custom 模板手写）或手写 handler
│       ├─ collect.ts               # export async function handle(args) → string
│       ├─ analyze.ts
│       ├─ act.ts
│       └─ run-collect-analyze-act.ts
└─ build/                           # tsc 产物（node build/index.js 启动）
```

**tools-definitions 单一数据源示例**（`caa-collect.json`，`template:"custom"` 保留手写 handler）：

```json
{
  "name": "caa_collect",
  "description": "三层工作流·第1层：Cline 收集信息（读 source 节点/输入，产出 collected_info）",
  "handler": "collect",
  "template": "custom",
  "inputSchema": {
    "type": "object",
    "properties": {
      "source":     { "type": "string", "description": "数据源节点 key 或输入区路径" },
      "question":   { "type": "string", "description": "本次收集意图" },
      "workflowId": { "type": "string", "description": "引用节点所在工作流命名空间" }
    },
    "required": ["source"]
  }
}
```

---

## 五、桥接：TS handler → Agent action 面

MCPFramework 是 TypeScript，handler 无法直接调 C# Agent。桥接方式 = **业务服务（BusinessService）内部调用 Agent action 面**（`AgentUi_RunJson` / `Agent.StorageBridge.exe`），只透传不复制（对齐《MCP服务化接入方案》）。

**`src/index.ts`（入口）**：
```typescript
import { createMcpServer, startServer } from "../core/server";
import { registerBusinessService } from "../core/registry";
import { business } from "./core/business";

registerBusinessService(business);
const server = createMcpServer({ name: "collect-analyze-act-mcp", version: "0.1.0" });
startServer(server, "collect-analyze-act-mcp");
```

**`src/core/business.ts`（业务服务，桥接 Agent action 面）**：
```typescript
export const business = {
  // 经 Agent.StorageBridge / RunJsonAsync 调 Agent action 面（薄封装，不复制分发与落库）
  async collect(source: string, question?: string, workflowId?: string): Promise<string> {
    // ① 取数（读 source 节点/输入）→ ② 入队 Cline 收集任务（executor=cline）→ ③ 返回 collected_info
    return await invokeAgent({ action: "task", op: "enqueue", executorId: "cline", input: `收集 ${source}：${question}` });
  },
  async analyze(collected: string, question?: string): Promise<string> {
    // 输入 collected_info → 入队 Answer 分析任务（executor=stateless-answer）→ 返回 conclusion
    return await invokeAgent({ action: "task", op: "enqueue", executorId: "stateless-answer", input: `${question}\n\n${collected}` });
  },
  async act(conclusion: string, target: string): Promise<string> {
    // 输入 conclusion → 入队 Cline 执行任务（executor=cline）→ 写 target 节点 → 返回 execution_result
    return await invokeAgent({ action: "task", op: "enqueue", executorId: "cline", input: `执行：${conclusion}\n写入 ${target}` });
  },
  async run(source: string, question: string, target: string, sessionName?: string): Promise<string> {
    // 组合整链：复用主文档 ConversationPipeline / 三层工作流模板，返回各层结果
    return await invokeAgent({ action: "task", op: "enqueue", executorId: "conversation",
      input: question, sessionName, workflow: "collect-analyze-act" });
  },
};

async function invokeAgent(req: Record<string, unknown>): Promise<string> {
  // 桥：调用 Agent.StorageBridge.exe（独立进程写入口，Invoke-AgentStep 动作协议）或 HTTP/SSE 到 RunJsonAsync
  // 只透传 req，不复制 Agent 分发/落库/裁决逻辑
  // …（落地实现：child_process exec StorageBridge / fetch RunJson 桥）
  return JSON.stringify({ status: "pending", request: req });
}
```

**`src/tools/collect.ts`（handler，custom 模板手写）**：
```typescript
import { getBusinessService } from "../core/registry";
import { formatError } from "../core/utils";

export async function handle(args: Record<string, unknown>): Promise<string> {
  try {
    const source = args.source as string;
    if (!source) return formatError("缺少必填参数 'source'");
    const business = getBusinessService<typeof import("../core/business").business>();
    return await business.collect(source, args.question as string, args.workflowId as string);
  } catch (err) {
    return formatError(`caa_collect 执行失败: ${err}`);
  }
}
```
`analyze.ts` / `act.ts` / `run-collect-analyze-act.ts` 同构，只调对应 `business` 方法。

---

## 六、注册与调用链

**注册**（二选一，或叠加）：
1. **独立注册**：在 `Agent/Config/mcp-servers.json` 增加 `collect-analyze-act-mcp` 条目（`Command: node`，`Arguments: [该工程 build/index.js]`），C# 侧 `McpClientManager` 连接后经 `mcp_call_tool` 调用；
2. **经 MCPBridge 聚合**：把该工具并入 `MCPBridge` 的聚合工具清单，外部统一入口。

**外部调用链**：
```
外部智能体（Cline 主控 / 脚本）
  → MCP ListTools（暴露 caa_collect / caa_analyze / caa_act / caa_run）
  → CallTool(caa_run, {source, question, target})     ← 组合整链
       → collect-analyze-act-mcp handler（run-collect-analyze-act）
       → BusinessService.run → Agent action 面（RunJsonAsync/StorageBridge）
       → ConversationPipeline 沿 collect→analyze→act 三层推进（复用主文档时序）
       → 返回各层结果（collected_info / conclusion / execution_result）
```
> 对话 UI 内嵌调用与外部 MCP 调用**复用同一三层模板**（SingleImplementation）：对话 UI 直接走 `ConversationSessionService`，MCP 面经 BusinessService 桥到同一 action 面，两者不各自实现三层推进。

---

## 七、安全与边界

| 原则 | 落地 |
|---|---|
| 只暴露能力面 | 工具清单 = `caa_*` 白名单；**不暴露执行链函数**（`guard_input`/`provide_prompt`/`rule_match`/`guard_output`）与确定性核心（Budget/Token/Flag/Round） |
| 控制面不外包 | 预算/熔断/暂停/终止/写透全部留在 Agent 框架内，MCP 层无裁决逻辑 |
| 双真源防护 | handler 只透传 action 面，不复制分发/落库；三层推进权威在主文档工作流模板 |
| 权限与审计 | 工具调用写日志/事件（`ur_events`/`IObservabilitySink`），外部智能体行为可追溯 |
| 失败语义 | 单步失败保留错误行可重放，不阻塞 `caa_run` 后续重试 |

---

## 八、分阶段落地

| Phase | 内容 | 验收 |
|---|---|---|
| **P0（脚手架）** | 从 `MCPFramework/templates/project/` 复制生成 `collect-analyze-act-mcp`，改名 + `package.json`；`npm run generate` 产物正常 | `node build/index.js` 启动，`ListTools` 暴露 4 个 `caa_*` 工具（handler 暂返回占位） |
| **P1（桥接）** | `BusinessService` 接通 `Agent.StorageBridge`/`RunJsonAsync`；三个单步 handler 透传 | `caa_collect` 能读到 source 节点内容并回传 collected_info |
| **P2（组合整链）** | `caa_run` 复用 `ConversationPipeline`/三层工作流模板，一次返回三层结果；注册到 `mcp-servers.json` | `mcp_call_tool(caa_run, …)` 返回 collected/analyzed/acted 三段结果 |
| **P3（收尾）** | 审计事件 + 安全边界复查 + 文档定稿 | 未装配 action 面返回诊断不崩溃；`caa_*` 只暴露能力面不泄控制面 |

---

## 九、权衡

| 决策点 | 方案 | 取舍 |
|---|---|---|
| 形态 | 3 单步工具 + 1 组合工具（`caa_run`） | 单步 = 能力面可自由编排；组合 = 固化三层整链一次调用 |
| 载体 | 基于 `MCPFramework` 新工程（TS，`tools.config.json` + 懒加载 handler） | 复用既有服务器框架与脚手架，零复制；代价是多一个进程 |
| 桥接 | `BusinessService` → `Agent.StorageBridge`/`RunJsonAsync` | 只透传不复制，控制面留框架；代价是需要进程/HTTP 桥 |
| 三层权威 | 主文档工作流模板（`collect-analyze-act.json`） | 单一来源，MCP 面只是入口；代价是需先有主文档 P1-P2 落地 |
| 与对话 UI | 复用同一模板，两条入口 | 不双真源；对话 UI 走 `ConversationSessionService`，MCP 走 BusinessService 桥 |

---

## 十、一句话总结

> **三层工作流 MCP 封装 = 基于 `MCPFramework` 新建 `collect-analyze-act-mcp`，把「CLine 收集 → Answer 分析 → Cline 执行」三层模板暴露为 `caa_*` 工具面（3 单步 + 1 组合 `caa_run`），handler 经 `BusinessService` 只透传 Agent action 面（`RunJsonAsync`/`StorageBridge`）、控制面留在框架**——为外部智能体（Cline 主控）提供标准 MCP 编程入口，与对话 UI 复用同一三层模板（单一来源、只透传不复制、可开关可回退）。

