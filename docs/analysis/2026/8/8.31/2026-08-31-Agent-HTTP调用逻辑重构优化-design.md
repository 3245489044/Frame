# Agent HTTP 调用逻辑重构优化设计

> 适用范围：`D:\Project\Program\WorkFlowPrj\Projects\Agent\src`
> 分类：代码级优化与重构 · HTTP 传输层治理
> 依据：对 `src` 全量扫描的 HTTP 调用逻辑分析

***

## 1. 现状盘点

### 1.1 出站 HTTP 客户端（发起请求）

| 位置                                                     | 方向                                   | 说明                                                                                                           |
| ------------------------------------------------------ | ------------------------------------ | ------------------------------------------------------------------------------------------------------------ |
| `Agent.Contract/Llm/OpenAiCompatibleLlmClient.cs`      | `POST {baseUrl}/v1/chat/completions` | 统一 OpenAI 兼容 LLM 传输。本地腿(→`127.0.0.1:8787`)与真实腿(→DeepSeek)共用同一实现，仅 BaseUrl 不同。静态共享 `HttpClient{Timeout=60s}`。 |
| `Agent.Axioms/SemanticKernel/SemanticKernelFactory.cs` | SK `AddOpenAIChatCompletion`         | 通过 `SemanticKernelLlmClient.Instance` 与 `PromptLlmGenerator` 也向 DeepSeek 发起真实 HTTPS。**与上者构成两套并存的出站 LLM 传输。** |

### 1.2 入站 HTTP 服务器（监听）

| 位置                                                                   | 监听                   | 说明                                                                                                                            |
| -------------------------------------------------------------------- | -------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `Agent.Modules/LocalLlmHttp/LocalLlmHttpHost.cs`（+ Module / Options） | `127.0.0.1:8787`     | **HttpListener 手写** OpenAI 兼容端点：`/v1/chat/completions`、`/v1/responses`、`/v1/models`、`/health(/z)`。手工路由、手工 SSE 分块、手工 JSON 编解码。 |
| `Agent.Launcher/Composition/McpServerHosting.cs`                     | `localhost:3200/mcp` | 官方 `ModelContextProtocol.AspNetCore` + Kestrel 的 Streamable HTTP（较规范）；另含 stdio 分支。                                            |

### 1.3 非 HTTP（排除项，勿误改）

* Cline 执行端走**子进程 Node MCPBridge 的 stdio IPC**（`ClineDaemonHost` 重定向 stdin/stdout/stderr）；`BaseUrl` 仅是下发给 TS 桥的配置项，非 C# 直接 HTTP。

* `McpClientManager` 走 `StdioClientTransport`（子进程 stdio），非 HTTP。

***

## 2. 问题清单（按优先级）

### P1-1 出站 LLM 传输两套并存

尽管 `LlmHosting` 注释宣称「取代 SK Kernel 装配」，但 `SemanticKernelFactory` + `SemanticKernelLlmClient.Instance` 仍是活跃路径（经 `PromptLlmGenerator`、调度 skill 等）。两套实现都向同一真实端点发出站请求，双传输分岔：

* 行为不一致（SK 路径带 Filter/门控，统一 client 路径走 `LlmRecursionGuard`）。

* 维护面翻倍，`ILlmClient` 语义存在两处归属。

### P1-2 出站 HttpClient 无统一生命周期

三处各自 `private static readonly HttpClient { Timeout=60 }`（`OpenAiCompatibleLlmClient.SharedHttp`、`SemanticKernelFactory._http`）：

* 硬编码 60s 超时；无命名策略、无连接池寿命(`PooledConnectionLifetime`)、无弹性重试（项目已有 Polly 惯例却未用于传输层）。

* 未接入 `IHttpClientFactory`，编排/观测（诊断事件、门控计数)难以统一挂接。

### P2-1 手写 HttpListener 与 ASP.NET Core 双轨

`LocalLlmHttpHost` 手工路由/SSE/JSON 编解码，且 HttpListener 有 ACL `urlacl` 缺陷；同仓 `McpServerHosting` 已迁 Kestrel，两轨标准不统一。

### P2-2 端点/端口/URL 硬编码散落

* `8787`：`LlmMode.LocalHttpEndpoint`、`LocalLlmHttpOptions.ListenPrefix`、`AgentOptionalModules` 日志。

* `3200`：`McpServerHosting`。

* `https://api.deepseek.com`：`OpenAiCompatibleLlmClient`、`SemanticKernelFactory` 各有一份默认值。
  三处漂移易导致本地/真实端点配置不一致。

### P3-1 递归跳数依赖可变静态

`LlmRecursionGuard.CurrentHop` 为异步上下文静态值：客户端写、`LocalLlmHttpHost.RunBackendAsync` 写、服务端读。依赖 AsyncLocal 流转，跨请求并发有串扰隐患。

### P3-2 MCP HTTP 无显式安全约束

`localhost:3200/mcp` 为主机回环，当前风险可控；但未显式施加「仅回环 + 无鉴权提示」，需固化约束避免随代理配置暴露。

***

## 3. 目标架构

```
                     ┌────────────────────────────────────────────┐
  外部OpenAI客户端 ──▶│  Agent 入站 HTTP（Kestrel）                 │
  (Cline / Claude)   │  · LocalLlm 服务端（/v1/chat/completions…）│
                     │  · MCP Streamable HTTP（/mcp）             │
                     └───────────────┬────────────────────────────┘
                                     │ 统一 ILlmClient 网关
                                     ▼
                     ┌────────────────────────────────────────────┐
                     │  LlmRoutingClient / LlmMode（唯一咽喉）      │
                     └───────────────┬────────────────────────────┘
                                     │ LlmClientFactory
              ┌──────────────────────┼──────────────────────┐
              ▼                      ▼                      ▼
     OpenAiCompatibleLlmClient  LocalLlmClient(进程内)     [废除]
     (唯一出站传输, 走统一Http)  (规则引擎, 不入网)       SemanticKernelLlmClient
                                                          并入网关或收敛
```

**核心目标**

1. **单一出站传输**：真实必网路径只保留 `OpenAiCompatibleLlmClient`；`SemanticKernelLlmClient` 收敛为该客户端的适配层或废弃，消灭双实现。
2. **统一传输层**：出站收敛为一个受管的 `SocketsHttpHandler` 连接池（`IHttpClientFactory` 或等价单例），统一超时/重试/观测。
3. **入站双轨归一**：`LocalLlmHttpHost` 迁至 Kestrel + Minimal API，删除手工传输样板。
4. **配置/常量单一来源**：端口、默认端点集中为一处配置或常量表。

***

## 4. 分阶段重构方案（代码级）

### 阶段 A · P1-2 出站 HttpClient 收敛（小，先行，独立可交付）

1. 抽取共享连接池：新建 `Agent.Contract/Llm/LlmHttpTransport.cs`（或复用现有 `GlobalLlmGate` 位置），单例持有
   `SocketsHttpHandler`（`PooledConnectionLifetime=5min`、`MaxConnectionsPerServer=16`）+ `HttpClient`(无硬编码单点超时，改由调用方 `CancellationTokenSource.CancelAfter` 或 Polly)。
2. `OpenAiCompatibleLlmClient` 与 `SemanticKernelFactory` 改为引用该共享传输实例；

   * `SemanticKernelFactory` 的 `new HttpClient` 删除，注入共享 handler。
3. 超时治理对齐：将既有 60s 硬编码替换为统一配置源 `AgentLlmConfig.TimeoutSec`（默认 60）。
4. **验证**：真实端点往返一次、本地腿往返一次；`ss`/诊断确认仅 1 个连接池、无每请求新建 socket。

### 阶段 B · P1-1 出站 LLM 传输单向收敛（中，需梳理调用方）

目标：让 `SemanticKernelLlmClient` 不再是第二条真实出站腿。

方案（二选一，建议 B2）：

* **B1 并入网关**：`SemanticKernelLlmClient` 改由 `OpenAiCompatibleLlmClient` 包装（Kernel 仅保留工具/过滤管线，模型补全走统一 client，`.Instance` 静态解耦为网关查询）。

* **B2 语义统一**：凡经 `ILlmClient` 的调用一律走 `LlmClientFactory`/`LlmRoutingClient`；`SemanticKernelLlmClient.Instance` 改读 `LlmGateway.Current`（对齐 `ResolveStatelessAnswerLlm` 已有模式）。

> 注：若 SK 管线（`InvocationFilters`、工具插件）确需保留，则 B1 保留 Kernel 的横切能力、仅替换补全通道，禁用 `builder.AddOpenAIChatCompletion` 自带出站，改为在 SK 内注入统一 client 的补全。需在 `SemanticKernelServiceCollectionExtensions` / `PromptLlmGenerator` 同步修改。

**改动面**（代码级清单）

* `Agent.Axioms/SemanticKernel/SemanticKernelFactory.cs`：移除自带出站 HttpClient/`AddOpenAIChatCompletion` 出厂，改为经注入的补全通道。

* `Agent.Axioms/SemanticKernel/SemanticKernelLlmClient.cs`：`.Instance`→网关解析。

* `Agent.Axioms/Prompts/PromptLlmGenerator.cs`：改用 `LlmGateway.Current`。

* `Agent.Axioms/Scheduling/LlmStateDirector.cs`：解除对 `SemanticKernelLlmClient.Instance` 隐式依赖（注释已点出）。

* 更新 `Agent.Contract/Llm/ILlmClient.cs` 的实现归属注释。

**验证**：provider 不变前提下，answer/规划端/SK skill 三条路径返回一致且仅经统一网关出网；本地联调模式零真实触网。

### 阶段 C · P2-1 LocalLlmHttp 迁 Kestrel（中，较大，独立立项）

将 `LocalLlmHttpHost` 由 `HttpListener` 改写为 Kestrel + Minimal API：

1. 新建 `LocalLlmHttpHost.Kestrel`（或 in-place 重写）：`WebApplication.CreateBuilder` + `UseUrls(prefix)`，`MapPost("/v1/chat/completions")`、`MapPost("/v1/responses")`、`MapGet("/v1/models")`、`MapGet("/health")`。
2. `WriteSseAsync` 手工分块改写为 `IAsyncEnumerable` + `Results.Stream`（Kestrel 原生流式），删除手工 chunk 拼装。
3. `HttpListenerException/ACL` 分支删除；`LocalLlmHttpOptions` 保留，但 `ListenPrefix` 语义对齐 `UseUrls`（若保留 `http://127.0.0.1:8787/` 兼容则归一化逻辑延后到配置阶段）。
4. `P3-1` 一并处理：`hop` 改为**显式请求参数/头传递**（服务端从请求头读 `X-Llm-Hop`，出站时在 `HttpRequestMessage` 上显式设置，不再读写 `LlmRecursionGuard.CurrentHop` 静态）。

**验证**：`curl` 对 `chat/completions`(SSE/非流式)、`responses`、`models`、`health` 与迁移前行为一致；递归自环在超限 hop 处被 508 阻断；无 ACL `urlacl` 前置要求（Windows 非管理员可起）。

### 阶段 D · P2-2 端点/端口单一来源 + P3-2 安全约束（小）

1. 新增 `AgentConfig`/`AgentLlmConfig` 统一端口与默认端点字段；`LlmMode.LocalHttpEndpoint` 改读配置，删除散落字面量 `"http://127.0.0.1:8787/"`、`"https://api.deepseek.com"`、`3200`。
2. `McpServerHosting.RunHttpAsync` 的 `localhost` 绑定保留并显式注释「仅回环，不随 Kestrel 代理配置暴露」；若未来需跨进程访问，另行补充认证。
3. 出站/入站超时统一到配置源 + Polly `RetryPolicy`/`TimeoutPolicy`（对齐项目工程惯例），面向 `GlobalLlmGate` 统一门控计数。

***

## 5. 风险与护栏

| 风险                       | 缓解                                                        |
| ------------------------ | --------------------------------------------------------- |
| 双传输收敛破坏 SK 横切（Filter/工具） | 阶段 B 优先选 B1（保留 Kernel、仅换补全通道），用回归测试守边界                    |
| 迁 Kestrel 改变端口/SSE 语义    | 阶段 C 交付前先固化行为契约（curl 脚本），逐端点对照                            |
| 统一连接池引发 DNS/超时行为变化       | 保留 `PooledConnectionLifetime` 与显式 `CancelAfter`，不做隐性长连接承诺 |
| 静态 `CurrentHop` 串扰       | 阶段 C 内改为显式头传递，独立验证高并发自环用例                                 |

## 6. 落地顺序建议

1. **阶段 A**（HttpClient 收敛）—— 并行无争议，先行落地。
2. **阶段 B**（出站传输单向收敛）—— 依赖 A 的共享传输，其次。
3. **阶段 C**（LocalLlmHttp 迁 Kestrel，含 P3-1）。
4. **阶段 D**（配置集中 + 安全约束）—— 可与 C 并行收尾。

> 各阶段均以「代码级清单 + 可验证标准」独立交付，避免大爆炸式重构；不新增模块，优先在既有文件/循环内增强，符合本仓工程约定。

