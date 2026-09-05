# 统一 LLM 网关收敛重构 · 实施状态与链路分析

> 日期：2026-08-21 · 状态：**分析快照（已实施两轮修复，端到端仍卡在 Cline daemon↔LocalLlmHttp 传输层）**
> 用途：汇总 8.21 三次真实 GUI 实测的证据链、已完成修复、剩余卡点，供后续彻底优化重构，**避免重复 debug**。
> 关联方案：`docs/analysis/2026/8/8.21/统一LLM网关收敛重构方案.md`。

---

## 〇、结论摘要

| 层 | 状态 | 证据 |
|---|---|---|
| 统一网关收敛（M1-M5） | ✅ 已实施，单测通过 | `Agent.Axioms.Tests` 42/42；`LlmGateway.Current` 全库唯一咽喉 14 消费点 |
| 会话参数种子断链 | ✅ 已修复 | `RoundStateMapper.Seed` 透传会话键；`RoundStateMapperTests` 断言通过 |
| cline 走本地 | ⚠️ baseUrl 已强制指向本地，但 **daemon 挂起** | 三次实测日志对比 |
| 端到端（终答非空 + CallLog>0） | ❌ **未达成**，卡在 Cline daemon 传输 | 19:04 实测 `phase2-core-start-ok` 后无日志、125s 超时、`CallLog=0` |

**一句话**：网关收敛、参数种子、cline 本地端点三处均正确修复并生效；当前唯一残留卡点是 **Cline（TypeScript daemon）连 LocalLlmHttp 时挂起** —— 这是独立于「网关收敛」的重链兼容问题，需单独诊断。

---

## 一、目标链路（会话 collect-analyze-act 三步）

```
用户发消息
 → ConversationPipeline.RunSignalAsync
   → RoundStateMapper.Seed（把 SeedWorkflow 的 system/l1/l2/question 注入骨架 Vars）✅ 已修
   → WorkflowRunner 沿 collect-analyze-act 三步执行
       collect:  cline_execute → ClineAgentExecutor → ClineClient(daemon池) → baseUrl(本地) → LocalLlmHttp → LlmMode.Routing → local   ⚠️ daemon 挂起
       analyze:  answer      → StatelessAnswerExecutor → LlmGateway.Current → LlmMode.Routing → local                                ✅ 路径已就绪（尚未被执行到）
       act:      cline_execute → 同 collect                                                                                              ⚠️ 依赖 collect 完成
```

> **卡点**：链第一步 collect（cline）不返回 → analyze/act 永不执行 → 终答空、CallLog=0。
> answer 链路本身已收敛正确（LlmGatewayTests 已验证 `LlmGateway.Current → local → CallLog+1 → 返回非空`），只是**因 cline 卡住从未被触达**。

---

## 二、已实施修复（全部通过编译 + 单测）

### 2.1 统一网关收敛（方案 M1-M5）

| 里程碑 | 改动 | 验收 |
|---|---|---|
| **M1** | 新增 `Agent.Runtime/State/Infrastructure/LlmGateway.cs`（`Current` 抛「未装配」+ `IsConfigured`）；全部消费方改 `LlmGateway.Current`：`AnswerWorkflowFunction`/`LlmStateDirector`/`LlmHosting.ResolveStatelessAnswerLlm`/`AgentFrameworkAssembler.WireLocalJsonGenerator`/`LocalLlmHttpModule`/`StatelessAnswerModule`/`AnswerFactory` | `LlmClientFactory.Get("semantic")` 业务直连清零（仅剩 `LlmHosting` 装配内部 + `AnswerFactory` 显式 provider 例外分支） |
| **M2** | `AnswerWorkflowFunction.SharedAnswer` 固定 `new StatelessAnswerExecutor(LlmGateway.Current)`；`ResolveExecutor` 消除 `RoundKeys.Executor` 注入旁路 | answer 恒走网关，无回退链/注入旁路 |
| **M3** | `AgentOptionalModules.ensureHttp` 由静默跳过改显式降级日志；`LocalLlmHttpModule` 默认 client 用 `LlmGateway.Current` | 未装配可观测 |
| **M4** | `LlmHosting.Configure` 加装配日志 `LlmHosting_gateway_configured`；`AgentFrameworkHosting` 降级摘要标注「统一网关未装配」 | 日志已见 `统一 LLM 网关已装配（生效后端=semantic）` ✅ |
| **M5** | `ConversationPipeline.RunSignalAsync` 写会话归档 `__backend_audit` + `GetBackendAuditAsync`；`reply.Diagnostics` 契约保持不变 | `ConversationPipelineTests` 归档审计测试通过 |

### 2.2 会话参数种子断链（第一根因，比网关更根本）

- **根因**：`ConversationPipeline.SeedWorkflow` 写入 `lease.Data[system/l1/l2/prompt/question/conversation]`，但 `RoundStateMapper.Seed(ctx,state,lease=null)` 走 `SeedFromLease(LeaseDeriver.Derive(state))` **只构造授权键**，不透传这些会话键 → answer 的 `question` 空、cline 的 `system/l1/l2` 空 → 三步快速空返回、不触 LLM。
- **证据**：18:34 实测三步 47ms 完成、`CallLog=0`、终答空。
- **修复**：`RoundStateMapper.Seed` 新增 `SessionSeedKeys={System,L1,L2,Prompt,"question","conversation"}` 透传；主循环走 provide_prompt 主导，无回归。

### 2.3 cline 本地强制（第二根因）

- **根因**：会话 cline 步 `ClineAgentExecutor` 的 `defaults` 解析到全局 `config.Llm`（`provider=semantic`、`baseUrl` 空）而非 `Agents.cline`（`127.0.0.1:8787`）→ 绕过 LocalLlmHttp 真实触网。
- **证据**：18:47 实测 `daemon run-start provider=semantic baseUrl=(空)` → 真实触网超时。
- **修复**：`ClineAgentExecutor.ExecuteAsync` 在 `LlmMode.LocalOnly` 时**强制** `baseUrl=LlmMode.LocalHttpEndpoint/v1` + `clientType=openai-compatible`。

---

## 三、三次真实 GUI 实测证据链

| 次 | 时间 | 关键日志 | 结论 | 触发的修复 |
|---|---|---|---|---|
| 1 | 18:34 | `本轮骨架完成 ok=true steps=3`，三步 47ms，`CallLog=0`，终答空 | 步骤参数空 → 快速空返回 | 参数种子断链修复（§2.2） |
| 2 | 18:47 | `daemon run-start provider=semantic baseUrl=(空)`，127s 超时 | cline 真实触网 | cline 本地强制（§2.3） |
| 3 | 19:04 | `daemon run-start baseUrl="http...7/v1" clientType=openai-compatible`；`phase2-core-start-ok` 后无日志；125s 超时 | cline 已指向本地，但 **daemon→LocalLlmHttp 挂起** | 待诊断（§四） |

> **三次演进已验证**：①参数种子修复生效（cline 真正执行）→ ②cline 本地端点生效（baseUrl=127.0.0.1:8787/v1）→ ③卡点从「参数/路由」转移到「daemon 传输」。

---

## 四、当前残留卡点分析：Cline daemon ↔ LocalLlmHttp

### 4.1 现象
- daemon（TypeScript node，ClineApi/build/daemon.js）已成功初始化（`phase1/phase2-core-start-ok`，约 0.5s）。
- 之后进入 LLM 调用阶段，**再无任何日志**，直到 `AgentUi_ConversationSend` 125s/126s 框架超时。
- `CallLog=0`（LocalLlmClient 一次未命中）→ **请求未到达 local，或 LocalLlmHttp 未收到 daemon 请求**。

### 4.2 关键参数（19:04 日志）
```
mode=act, provider=semantic, model=deepseek-chat,
baseUrl="http://127.0.0.1:8787/v1", clientType="openai-compatible",
apiKey=sk-c...3b56
```
> **疑点：`provider=semantic` 与 `baseUrl=本地 openai 兼容 + clientType=openai-compatible` 组合不匹配。**
> 我（§2.3）只强制了 baseUrl/clientType，**provider 仍是 defaults 的 `semantic`**（来自 config.Llm）。
> Cline daemon 的 provider 决定它走哪套 SDK/凭据逻辑；`semantic` 不是 Cline 标准 provider（标准：deepseek/anthropic/openai…），可能使 daemon 在 SDK 加载/调用阶段挂起。

### 4.3 根因溯源（defaults 为何是 semantic）
- 会话 cline 步 `ClineExecutorFunction.SharedExecutor` 用 `AppServiceProvider.Resolve<ClineRunOptions>()` 作为 `defaults`。
- `AgentOptionalModules.RegisterAgents` 注册的是 `ToClineOptions(config.Agents[config.DefaultAgent])`；若 `config.Agents` **为空**（`ApplyExternalConfig` 未填充 Agents 节），则 `defaultLlm` 回退 `config.Llm`（semantic）→ `defaults` 为 semantic。
- **次级根因**：`config.Agents`（agent-config.json 的 `Agents.cline` 含 BaseUrl=127.0.0.1:8787）在会话 GUI 装配路径未正确解析。

### 4.4 可能的解决方向（后续试验，勿再盲改）
1. **LocalOnly 时同步强制 provider**：与 baseUrl 匹配，如复用 `Agents.cline.Provider`（deepseek）或 Cline 支持的 openai provider；避免 `provider=semantic` + openai-compatible 组合。
2. **修复 `config.Agents` 解析**：确认 `AgentConfig.ApplyExternalConfig` 是否填充 `Agents`；若缺失，`SharedExecutor` 的 defaults 应显式取 `Agents.cline`（含本地端点），而非 `config.Llm` 兜底。
3. **LocalLlmHttp 加请求日志**：在 `HandleChatCompletionsAsync` 入口记录一次请求（当前无日志，无法判断 daemon 请求是否到达），补可观测。
4. **验证 daemon 请求路径**：确认 daemon 以 `baseUrl=.../v1` 发的是 `/v1/chat/completions` 而非 `/v1/v1/chat/completions`（LocalLlmHttp 仅匹配精确 `/v1/chat/completions`，见 `LocalLlmHttpHost.cs:135`）。
5. **临时降级验证**：会话模板改用**纯 answer（无 cline）**，直接验证「answer 走网关 → CallLog>0 → 终答非空」，把 cline daemon 传输从端到端验收中剥离，先锁定网关收敛正确性。

---

## 五、架构问题汇总（供彻底优化重构）

| # | 问题 | 位置 | 性质 |
|---|---|---|---|
| A | 会话工作流参数种子断链（已修，但模式脆弱：Seed 需显式白名单透传会话键） | `RoundStateMapper.cs` | 设计：SeedFromLease 白名单 vs 会话自定义键 |
| B | `ClineExecutorFunction.SharedExecutor` 的 `defaults` 经 `AppServiceProvider` 兜底，语义不确定（Agents vs Llm 全局） | `ClineExecutorFunction.cs` | 设计：执行器默认配置解析应显式绑定 Agent，而非服务定位器兜底 |
| C | `config.Agents`（agent-config 的 Agents 节）是否被 `ApplyExternalConfig` 解析，未验证 | `AgentConfig` | 配置解析缺口 |
| D | cline 腿的本地联调依赖「baseUrl 指向本地 + LocalLlmHttp 兼容」，与 `LlmMode.LocalOnly` 是两条独立机制（LocalOnly 只影响网关，不影响 cline 的 baseUrl） | `ClineAgentExecutor.cs` | 架构：cline 腿的后端路由未统一收敛到 LlmMode（已用 LocalOnly 强制 baseUrl 缓解，但 provider 未同步） |
| E | LocalLlmHttp 无可观测的请求日志，无法判断 daemon 请求是否到达 | `LocalLlmHttpHost.cs` | 可观测缺口 |
| F | Cline daemon 对 `provider=semantic` + openai-compatible 本地端点的兼容性未验证 | ClineApi daemon | 外部集成（TypeScript） |
| G | M5 后端审计写入会话归档，但 UI 主回显（`ConversationSendAsync`）不展示；空终答主回显仍无审计 | `ConversationPipeline`/`AgentUiLibraryConversation` | 可观测：可考虑在 Diagnostics 追加（需评估既有测试契约） |

---

## 六、当前验证状态（已固化，勿重做）

| 项 | 结果 |
|---|---|
| `Agent.sln` / `FileDriven.sln` 构建 | 0 警告 0 错误 |
| `Agent.Axioms.Tests` | 42/42（含 LlmGatewayTests×2、RoundStateMapperTests×1） |
| `Agent.Core.Tests` | 130/131（1 失败 = 基线既有 `AgentConfigExternalConfigTests`：期望 provider=local、实际 agent-config 为 semantic 的环境漂移，非本次引入） |
| `Agent.Guardrails.Tests` | 10/10 |
| `Agent.ClineUsage.Tests` | 45/45 |
| 收敛断言 | `AppServiceProvider.Resolve<ILlmClient>` 残留 0；`LlmClientFactory.Get` 仅剩 `LlmHosting` 装配内部 + `AnswerFactory` 显式 provider 例外 |

---

## 七、后续建议步骤（按优先级）

1. **先验证网关收敛正确性（剥离 cline）**：用纯 answer 会话（或直接调 `StatelessAnswerExecutor` 经 `LlmGateway.Current`）确认 `CallLog>0` + 终答非空。若通过 → 网关收敛已达标，残留仅 cline daemon 兼容。
2. **诊断 Cline daemon 传输**：加 `LocalLlmHttp` 请求日志 + 确认 daemon 请求 URL/body/`provider` 语义；必要时同步强制 `provider` 与本地端点匹配。
3. **修复 `config.Agents` 解析** 或 **让 `SharedExecutor.defaults` 显式绑定 `Agents.cline`**，使 cline 的 provider/baseUrl 与配置一致，不再依赖 LocalOnly 硬覆盖。
4. 按 §4.4 的 5 个方向逐一实验，避免盲改。


