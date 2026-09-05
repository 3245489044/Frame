# HTTP 通用框架下沉 Plate 设计

> 分类：平台层通用框架规划 · HTTP 传输基础设施治理
> 依据：Agent HTTP 调用逻辑重构（2026-08-31）识别出的通用 HTTP 基础设施 + 全仓 HTTP 逻辑盘点
> 范围：把跨项目可复用的 HTTP 基础从 Agent 应用层下沉到平台层 `CsharpPrj/Plate`，供 Agent、DataProcess 及未来 C# 项目复用

---

## 1. 背景与目标

2026-08-31 完成的 Agent HTTP 重构（统一受管连接池 `LlmHttpTransport`、统一 OpenAI 兼容客户端 `OpenAiCompatibleLlmClient`、Kestrel 服务端 `LocalLlmHttpHost`、显式递归护栏 `ILlmHopPropagator`）产出了多个**跨项目可复用的通用 HTTP 基础设施**，但当前仍寄居在 Agent 应用层（`Projects/Agent`）。

**目标**：识别并把这些通用 HTTP 基础下沉到平台层 `CsharpPrj/Plate`，使其成为平台能力，消除「每个项目手写 `HttpClient` / 手写端点宿主」的重复，实现连接池、超时、弹性、递归护栏的**单点治理**。

---

## 2. 现状盘点（全仓 HTTP 逻辑）

| 位置 | 类型 | 方向 | 现状 | 通用性 |
|------|------|------|------|--------|
| `Projects/Agent/src/Agent.Contract/Llm/LlmHttpTransport.cs` | 受管连接池传输（`SocketsHttpHandler`+`HttpClient`） | 出站 | 已重构，纯基础 | **高** |
| `Projects/Agent/src/Agent.Contract/Llm/OpenAiCompatibleLlmClient.cs` | OpenAI 兼容 LLM 客户端 | 出站 | 依赖 Agent 契约（`ILlmClient`/`LlmMessage`/`LlmResult`） | 中高 |
| `Projects/Agent/src/Agent.Modules/LocalLlmHttp/LocalLlmHttpHost.cs` | Kestrel + Minimal API 的 OpenAI 兼容服务端 | 入站 | 依赖 `ILlmClient` | 中 |
| `Projects/Agent/src/Agent.Contract/Llm/LlmRecursionGuard.cs` + `ILlmHopPropagator.cs` | 递归跳数护栏（`X-Llm-Hop` + 508） | 双向 | LLM 专用命名 | 中 |
| `Projects/Agent/src/Agent.Contract/Llm/LlmEndpoints.cs` | 端点/端口常量表 | — | Agent 部署配置 | 低 |
| `Projects/Agent/src/Agent.Launcher/Composition/McpServerHosting.cs` | MCP Streamable HTTP server | 入站 | 官方 `ModelContextProtocol.AspNetCore`，已是框架 | — |
| `Projects/Agent/src/Agent.Modules/Cline/*` | daemon stdio IPC | — | 非 HTTP | — |
| `Projects/DataProcess/DataAcquisition/HttpBarSource.cs` | 手写 `new HttpClient` + 30s 硬超时 | 出站 | **重复模式**（与 Agent 重构前同构） | 高（应复用） |
| `CsharpPrj/Modules/Observe/Abstractions/ITransport.cs` | 消息传输抽象（WebView2/InMemory） | — | 非 HTTP | — |
| `PythonPrj/PythonModel/clients/deepseek.py`、`TypeScriptPrj/TsWorkspace/MCPBridge` | OpenAI SDK / Node | 出站 | 跨语言，不适用 C# Plate | — |

---

## 3. 下沉判定标准

一项 HTTP 逻辑**下沉到 Plate** 需同时满足：

1. **跨项目复用**：存在或预期 ≥2 个独立调用方（应用/模块）。
2. **与业务语义解耦**：不依赖应用层契约（如 Agent 的 `ILlmClient`、`LlmResult`）。
3. **纯基础设施**：仅依赖 `System.Net.Http` / `Microsoft.AspNetCore.App`，零领域依赖。
4. **稳定契约**：接口稳定、可独立单元测试。

任一不满足 → 留在应用层或判定不适用。

---

## 4. 候选下沉项

### 4.A 统一受管 HttpClient 连接池传输 —— 【推荐下沉】

- **现状**：`LlmHttpTransport` 已实现为纯静态（`SocketsHttpHandler`：`PooledConnectionLifetime=5min`、`MaxConnectionsPerServer=16`、无硬编码单点超时，超时由调用方显式施加）。
- **通用性**：高。LLM 出站 + `HttpBarSource`（DataProcess）+ 未来任何 HTTP 客户端。
- **依赖**：仅 `System.Net.Http`，**无损下沉**。
- **形态**：`Plate.HttpMethod.Transport` —— `IHttpConnectionPool` / `HttpTransportFactory`（连接池 + 无硬超时 + 显式超时由调用方 `CancelAfter`/Polly）。
- **跨项目改造**：
  - `Agent.Contract` 新增对 `Plate.HttpMethod` 的引用，`LlmHttpTransport` 改为包装/别名通用池。
  - `HttpBarSource`（DataProcess）改用通用池，消除手写 `new HttpClient` + 30s 硬超时。
- **注意**：`HttpBarSource` 需要 GZip/Deflate 自动解压（`AutomaticDecompression`）——通用连接池需支持该配置项，避免丢失此能力。

### 4.B OpenAI 兼容 LLM 传输层 —— 【建议下沉（需拆分）】

- **现状**：`OpenAiCompatibleLlmClient` 是唯一 OpenAI 兼容出站传输（endpoint 归一化、Function Calling 序列化、响应/SSE 解析、工具循环）。
- **通用性**：中高。任意 OpenAI 兼容端点（DeepSeek / Cline / 本地）。
- **依赖约束**：当前绑定 `ILlmClient`/`LlmMessage`/`LlmResult`（Agent 契约）。→ **拆分**：
  - **下沉**：OpenAI 兼容 wire 传输层（请求 DTO、endpoint 归一化、`tools` 序列化、响应解析、工具循环），返回通用模型（如 `OpenAiChatResult`），不依赖 `ILlmClient`。
  - **留 Agent**：`ILlmClient` 契约 + 适配器（wire 结果 → `LlmResult`）。
- **形态**：`Plate.HttpMethod.OpenAI.OpenAiCompatibleTransport`。
- **风险**：与 `ILlmClient` 解耦需定义通用 wire DTO；若 Agent 是唯一消费方，收益有限 → 列为**有条件下沉**（待出现第二个 OpenAI 兼容消费方再落地）。

### 4.C OpenAI 兼容 HTTP 服务端宿主骨架 —— 【建议下沉（抽象化）】

- **现状**：`LocalLlmHttpHost` 是 Kestrel + Minimal API 的 OpenAI 兼容服务端（`chat/completions`、`responses`、`models`、`health`，SSE，508 递归护栏）。
- **通用性**：中。任何"以 OpenAI 兼容协议暴露本地/代理后端"的场景。
- **依赖约束**：当前绑定 `ILlmClient`。→ **抽象为后端委托**（`Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<ChatResponse>>`），宿主只做协议编解码 + 护栏。
- **形态**：`Plate.HttpMethod.OpenAI.OpenAiCompatibleHost`（Kestrel + Minimal API + SSE + 递归护栏）。
- **跨项目改造**：`LocalLlmHttpHost` 改为装配 Plate 宿主 + 后端委托（LocalLlm 规则引擎仍留 Agent）。
- **风险**：需保持现有 curl 冒烟契约（SSE framing / `responses` / `models` / `health`）。

### 4.D 递归跳数护栏 —— 【建议下沉（泛化）】

- **现状**：`LlmRecursionGuard`（`X-Llm-Hop` + `MaxHop=8` 服务端 508）+ `ILlmHopPropagator`（出站显式携带 hop）。
- **通用性**：中。任意「客户端→服务端→客户端」自环场景的纵深防御。
- **依赖约束**：现为 LLM 专用命名/常量。→ **泛化为通用 `HopGuard`**（请求头 + 服务端 508，`MaxHop` 参数化），移除 LLM 命名。
- **形态**：`Plate.HttpMethod.Guard.RecursionHopGuard`。
- **风险**：泛化需移除 LLM 命名；Agent 的 `ILlmHopPropagator` 契约可保留，内部改用通用护栏。

### 4.E 端点/端口常量表 —— 【不适用】

- `LlmEndpoints`（DeepSeek / 本地 8787 / MCP 3200）是 Agent 部署配置，非通用框架。
- 结论：留 Agent（走 `AgentConfig` / 配置源），不下沉 Plate。

---

## 5. 明确不应下沉的项

| 项 | 原因 |
|----|------|
| `ILlmClient` / `LlmMessage` / `LlmResult`（Agent.Contract） | Agent 业务契约，绑定本地规则引擎 / 路由 / 门控语义 |
| `LocalLlmClient` 本地规则引擎 | Agent 特定确定性后端（模板 + 规则 + JSON） |
| MCP 客户端 / `McpServerHosting` | 官方 `ModelContextProtocol` SDK 已是框架，无需再造，`McpServerHosting` 仅是装配 |
| Cline daemon（`ClineClient`/`ClineDaemonHost`） | stdio IPC，非 HTTP |
| `LlmRoutingClient` / `LlmGateway` / `LlmMode` | Agent 状态层统一网关/路由/开关，非 HTTP 基础设施 |
| Python / TS HTTP 客户端 | 跨语言（OpenAI SDK / Node），不适用 C# Plate |

---

## 6. Plate 落位建议

新建 **`CsharpPrj/Plate/HttpMethod/HttpMethod.csproj`**（`AssemblyName=Plate.HttpMethod`、`RootNamespace=Plate.HttpMethod`），ModuleRule 规范对齐现有 `JsonMethod.csproj`（Layer≈6）。

```
CsharpPrj/Plate/HttpMethod/
├── HttpMethod.csproj          # Plate.HttpMethod（ModuleRule 平台层）
├── Transport/                 # 连接池传输、超时策略、重试/弹性（对齐项目 Polly 惯例）
│   ├── IHttpConnectionPool.cs
│   └── HttpTransportFactory.cs
├── OpenAI/                    # OpenAI 兼容 wire 层
│   ├── OpenAiCompatibleTransport.cs   # 出站传输（4.B）
│   └── OpenAiCompatibleHost.cs        # 入站服务端宿主（4.C，Kestrel）
└── Guard/
    └── RecursionHopGuard.cs   # 递归跳数护栏（4.D）
```

- **依赖**：`System.Net.Http`（+ `Microsoft.AspNetCore.App` FrameworkReference，服务端宿主需要），零领域依赖。
- **命名空间**：统一 `Plate.HttpMethod.*`（对齐 `Plate.JsonMethod.*` 惯例）。

---

## 7. 依赖边界与风险

- **方向**：`Plate.HttpMethod`（纯基础） ← `Agent.Contract` / `DataProcess`（上层消费）。**无环**（Plate 不依赖 Agent）。
- **Agent.Contract 新增引用**：`<ProjectReference Include="$(CsharpProjectRoot)\Plate\HttpMethod\HttpMethod.csproj" />`（当前 Agent.Contract 未引 Plate，需新增；`Agent.Launcher` 已引用 Plate/JsonMethod/TConfig，方向合法）。
- **下沉后改造**：
  - `OpenAiCompatibleLlmClient` → 包装 Plate wire 传输 + 适配 `LlmResult`。
  - `LocalLlmHttpHost` → 装配 Plate 宿主 + 后端委托。
  - `HttpBarSource` → 改用通用连接池（需保留 GZip/Deflate 自动解压配置）。
- **回归守护**：现有 `OpenAiCompatibleLlmClientTests` 保留为 Agent 侧适配测试；新增 `Plate.HttpMethod` 单元测试；Kestrel 服务端用 curl 冒烟契约守边界。
- **不引入 Agent 反向依赖**：若下沉出现「Plate 需要引用 Agent 契约」即判定下沉失败，应改为抽象委托/通用 DTO。

---

## 8. 落地顺序

1. **阶段 1**：新建 `Plate.HttpMethod` + 下沉连接池传输（4.A）+ `HttpBarSource` 迁移（跨项目首个消费者）。
2. **阶段 2**：下沉 OpenAI 兼容 wire 传输（4.B）+ Agent 适配（`OpenAiCompatibleLlmClient` 改造）。*待出现第二个 OpenAI 兼容消费方再落地*
3. **阶段 3**：下沉 OpenAI 兼容服务端宿主（4.C）+ `LocalLlmHttpHost` 改造为装配式。
4. **阶段 4**：泛化递归护栏（4.D）。
5. 每阶段以「代码级清单 + 构建验证 + 回归测试」独立交付，避免大爆炸重构。

---

## 9. 收益总结

- **消除重复**：Agent / DataProcess 两处手写 `HttpClient` 收敛为单一受管连接池。
- **端到端能力**：OpenAI 兼容 client+server 成为平台能力，未来新端点/新消费方零重复实现。
- **单点治理**：连接池、超时、重试/弹性、递归护栏在 `Plate.HttpMethod` 统一治理与观测。
- **边界清晰**：业务契约（`ILlmClient`、本地规则引擎、统一网关）留在 Agent，平台层保持纯基础、可独立测试。

