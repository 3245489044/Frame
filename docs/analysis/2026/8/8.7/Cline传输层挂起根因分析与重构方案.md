# Cline 传输层挂起根因分析与重构方案

> 日期：2026-08-07 | 模块：ClineApi（`@cline/sdk`）/ Agent.ClineUsage / FileDriven
> 目标：定位 `cline_browser_test.json`（GUI 模式）中 Cline 真实调用 `click wait` 反复超时（~100s）的**传输层根本原因**，并给出可落地的重构方案。
> 范围：**不**把锅归于框架超时配置；聚焦 `@cline/sdk` 本地后端初始化与真实 LLM 会话启动的挂起点。
> 前置：框架层已做两项修复（外部取消归因修正 + 兜底路径预算），与本方案正交，见 §4。

---

## 1. 背景与问题

`FileDriven/DrivenConfig/Scripts/Cline/cline_browser_test.json`（GUI 模式）包含对 Cline 编码代理的**真实调用**（`click versionButton/configButton/planButton`）。两次运行均出现：

- `click configButton`（timeout=30s）等待 35s 超时 FAIL；
- `click planButton`（timeout=60s）等待 65s 超时 FAIL；
- 三个 ECS 事件函数（`Cline_Config` / `Cline_Plan` / `Cline_Version`）均以 `框架绝对超时` 收尾，且**结束时刻完全一致**（进程收尾时才落定）。

表象是"等待超时"，但真正的问题是：**Cline 的 node 进程（内部 `@cline/sdk` 的 `ClineCore`）挂起不返回、无输出，且框架在预算点杀不死它**，只能等进程收尾 ~100s 统一清理。

---

## 2. 两次运行的日志现象对比

| 现象 | Run 1（17:57） | Run 2（18:20） |
|------|----------------|----------------|
| `versionLabel` | `Cline CLI 版本: 0.0.70` | **空**（读不到） |
| `Cline_Version` 路由 | 命中注册函数 | **`工作流未找到: Cline_Version`**（落入工作流执行器，`ExecuteWorkflow("Cline_Version")`） |
| `Cline_Config` | 100430ms 后框架超时 | 100424ms 后框架超时 |
| `Cline_Plan` | 65421ms 后框架超时 | 65424ms 后框架超时 |
| 收尾时刻 | 三者一致（进程收尾） | 三者一致（进程收尾） |
| 启动后阻塞 | — | ~30s 同步阻塞（18:20:50→18:21:20），`ScopeLifecycleAdapter_init_function_failed` |

**共同点**：凡触发 node/ClineCore 的调用（config/plan）都挂 ~100s。
**Run 2 新增**：`Cline_Version` 在 init 阶段被当作"按名执行工作流"（`UIToolbox.cs:66` 注册的 `ExecuteWorkflow` 函数），找不到工作流定义 → 版本为空。

---

## 3. 根本原因（传输层，非框架层）

### 3.1 唯一的卡死点：`ClineCore.create({ backendMode: "local" })`，且无内部超时

`TypeScriptPrj/TsWorkspace/ClineApi/src/transport/sdk-transport.ts`：

```ts
149  private ensureCore(): Promise<ClineCoreWithHost> {
151    this.corePromise = ClineCore.create({ clientName: "cline-api", backendMode: "local" });
153  }
...
234  const core = await this.ensureCore();        // ← 先等 ensureCore（无超时包裹）
239  const timeoutMs = (options.timeoutSec ?? config.timeoutSec ?? 0) * 1000;  // 定时器在 ensureCore 之后才计算
243  const startPromise = core.start({ ... });
250  if (timeoutMs > 0) { await Promise.race([startPromise, timer]); }  // 超时只包住 core.start()
```

- `timeoutMs` 的竞速定时器在 `await ensureCore()` **之后**才启动 → 一旦 `ClineCore.create(backendMode:"local")` 卡住，**没有任何定时器能打断**。
- node 进程因此**无输出地永久阻塞**，只有 C# 侧 `ClineClient` 的空闲看门狗（120s）或进程收尾（~100s）能终止。

### 3.2 凭据指向"请求永不回"的端点/模型

`credentials/cline.json`：

```json
"provider": "deepseek",
"model": "deepseek-v4-flash",            // ← DeepSeek 官方无此模型（官方：deepseek-chat / deepseek-reasoner）
"baseUrl": "https://inferaichat.com/v1", // ← 非官方第三方端点
"clientType": "openai-responses",        // ← 按 OpenAI Responses API 形状请求
"requiresOpenaiAuth": true
```

- 本地后端启动后首次会话连 `inferaichat.com/v1`，用不存在的模型 + OpenAI Responses 形状 → **请求既不成功、不流式返回、也不报错 → Promise 永不 resolve**。
- `ClineCore.create`/`core.start()` 无健壮超时，故表现为"永久挂起"。

### 3.3 为什么 `version` 偶尔能返回、config/plan 却必挂

- `cli.ts` 的 `version` → `readSdkVersion()`：**同步读 `node_modules/@cline/sdk/package.json`，不创建 ClineCore** → 快且稳（Run 1 返回 0.0.70）。
- `config` 命令逻辑本身也只 `loadConfig()` 读文件，**不创建 ClineCore**；但它之所以也挂，是因为 **node 一启动就在模块顶层 `import "@cline/sdk"`**（`cli.ts:24 → cline-cli.ts → sdk-transport.ts:16`），SDK 模块加载/本地后端初始化副作用把它拖住。
- 真实 LLM 调用（`plan/ask/resume`）必然走 `ensureCore()` → 卡在 SDK 初始化/首次会话。

### 3.4 框架层不是根因（排除证据）

| 现象 | 解释 |
|------|------|
| `click wait` 30/65s 超时 | 是超时机制**按预期触发**（函数没在预算内完成）——属下游症状 |
| `框架绝对超时（100430ms）` | 因 SDK 层无超时、node 不退出，框架只能在进程收尾时兜底归类 |
| `Cline_Plan` 传了 timeout=60 仍跑 65s 无输出 | 60s 定时器包住的是 `core.start()`；卡在它**之前**的 `ensureCore()`，故定时器未启动 |

---

## 4. 已落地的框架层修复（正交，非本次根因）

> 以下两项在先前已完成并验证，不解决挂起本身，但修正了归因与兜底，保留即可。

| 项 | 位置 | 说明 | 状态 |
|----|------|------|------|
| 外部取消归因修正 | `CsharpPrj/Modules/Threading/AsyncExecution/AsyncOperationScheduler.cs` → `HandleCancelException` | 外部 `CallerToken` 取消归 `Cancelled`，不再被误判为 `框架绝对超时` | ✅ 编译 + 测试 5/5 |
| 兜底路径补预算 | `CsharpPrj/Share/EventBindingOrchestrator/Invocation/InvocationKernel.cs` → `RunFallbackAsync` | AEF 未装配时也尊重绝对预算/取消，挂起函数在预算点可被终止 | ✅ 编译通过 |

**重要**：这两项**不是** Run 2 回归（`工作流未找到`）的原因——它们不触碰注册/路由。Run 2 的 `Cline_Version` 落入工作流执行器，属**函数→工作流路由回退**问题（`UIToolbox.cs:66`），应单独处理（见 §5.2）。

---

## 5. 重构方案

### 5.1 A —— SDK 层补硬超时（治"挂起点无逃生通道"）

`TypeScriptPrj/TsWorkspace/ClineApi/src/transport/sdk-transport.ts`：

```ts
private async ensureCore(timeoutMs: number): Promise<ClineCoreWithHost> {
  const init = this.corePromise ?? ClineCore.create({ clientName: "cline-api", backendMode: "local" });
  this.corePromise = init as Promise<ClineCoreWithHost>;
  return await Promise.race([
    init as Promise<ClineCoreWithHost>,
    new Promise<never>((_, rej) =>
      setTimeout(() => rej(new Error(`ClineCore 初始化超时（${timeoutMs}ms）`)), timeoutMs)
    ),
  ]);
}
```

- 在 `ensureCore()`/`runCline` 全链路包一层**硬超时**，使 SDK 初始化卡住时 node 能自行退出（而非永久阻塞）。
- 配套：`ClineClient.RunNodeAsync` 的 `_idleTimeout`（当前 120s）应小于/等于外层预算，确保 C# 侧先兜底杀进程。

### 5.2 B —— 非 LLM 命令走轻量实现，避免 import ClineCore

对 `version / config / history` 这类**不真正调 LLM**的命令，`cli.ts` 顶层 import `@cline/sdk` 是多余且危险的（模块加载副作用即可拖住进程）。建议：

- 将 `cli.ts` 的顶层 import 改为**按需动态 `import()`**，或
- 在 `ClineClient`（C#）层对这些命令直接读文件/免 node，从源头规避 SDK 加载。

**同时**修复 Run 2 的路由回归：`exec`/init 解析 `Cline_*` 时应命中函数注册表；未命中**不应**回退到"按名执行工作流"（`UIToolbox.ExecuteWorkflow`），应明确报"函数未注册"。

### 5.3 C —— 纠正凭据（治"请求永不回"）

- 用**真实可用的 DeepSeek 端点 + 真实模型**（`deepseek-chat` / `deepseek-reasoner`），或
- 确认 `inferaichat.com` 的真实可用模型名、`clientType` 与认证方式（`requiresOpenaiAuth`）后再配置。
- 否则任何真实调用都必然挂起，A 只是让它"快速失败"而非"真能用"。

---

## 6. 决策点

| 决策 | 选项 | 建议 |
|------|------|------|
| SDK 初始化超时时长 | 取外层预算（30/60s）或固定 15s | 取外层预算并加日志；`ensureCore` 超时属框架错，应能归因 |
| `version/config/history` 是否保留 node | A: 动态 import 保 node；B: C# 侧免 node 直读 | 短期 B（最快规避挂起），中期 A（保持单通道） |
| 凭据端点 | 官方 DeepSeek vs 第三方 inferaichat | 以"能真实跑通一次 plan"为验收，二选一并回填到文档 |

---

## 7. 文件落点

| 文件 | 改动 |
|------|------|
| `TypeScriptPrj/TsWorkspace/ClineApi/src/transport/sdk-transport.ts` | `ensureCore()` 加硬超时（§5.1）；`run()` 全程超时 |
| `TypeScriptPrj/TsWorkspace/ClineApi/src/cli.ts` | 顶层 `@cline/sdk` import 改按需动态 import（§5.2） |
| `Agent/src/Agent.ClineUsage/ClineClient.cs` | `_idleTimeout`/`_defaultTimeout` 对齐外层预算；config/version 可免 node |
| `FileDriven/FileDriven.Business/Registrations/Cline/ClineLibrary.cs` | 视需要减少对 node 的依赖（version 本可本地读） |
| `credentials/cline.json` | 修正 model / baseUrl / clientType（§5.3） |
| exec 路由（`UIToolbox.cs:66` 相关回退逻辑） | 未命中函数时**不**回退到按名执行工作流（§5.2） |

---

## 8. 实施步骤

1. **验证卡点**：命令行独立跑 `node <ClineApi>/build/cli.js version` / `config`（带外部 timeout），再 `node ... plan "hi" --timeout 10` 观察卡在 SDK 启动还是 API 请求。
2. **A**：`ensureCore()` 加硬超时；`run()` 全程包超时。
3. **C**：修正 `credentials/cline.json` 的模型/端点；本地跑通一次 `plan` 为验收。
4. **B**：`cli.ts` 顶层 import 改按需；C# 侧 version/config 免 node。
5. **路由修复**：`exec`/init 未命中函数时明确报"函数未注册"，不回退工作流。
6. **回归**：重建 `FileDriven.sln` → BuildOutput，本机运行 `cline_browser_test.json`。

---

## 9. 风险与验证要求

- **必须真实跑通一次 LLM 调用**才能确认"请求永不回"是端点/模型/认证问题，需可用凭据。
- **端到端需 GUI 运行**：本开发环境无法启动 WinForms 界面做完整回归，至少先用 `node cli.js` 单点验证传输层。
- **并发**：init 阶段并发 spawn 多个 node/ClineCore 存在资源竞争，应串行化或收敛 initCommands 中会 spawn node 的项。
- 框架层修复（§4）已编译/测试通过，改动保持正交，不引入回归面。

---

## 10. 验收标准

| # | 验收项 | 判据 |
|---|--------|------|
| 1 | 卡点定位 | 日志/控制台可区分「卡在 `ensureCore()`」vs「卡在 `core.start()`/API」 |
| 2 | SDK 超时生效 | `node cli.js plan --timeout 10` 对错误配置能在 ~10s 内返回 JSON 错误而非永久挂起 |
| 3 | version/config 可用 | `node cli.js version` / `config` 秒回；`versionLabel` 非空 |
| 4 | 真实调用可用 | 修正凭据后 `node cli.js plan "hi"` 返回结果（非超时） |
| 5 | 路由回归修复 | `exec Cline_Version` 命中函数；`工作流未找到: Cline_Version` 不再出现 |
| 6 | GUI 端到端 | `click configButton/planButton` 不再 ~100s 收尾；`GUI 模式脚本执行完成` 在合理时间内给出明确结果 |

---

## 附录：关键代码位置速查

- `ensureCore()` / `run()`：`TypeScriptPrj/TsWorkspace/ClineApi/src/transport/sdk-transport.ts`（`ensureCore` L149、`run` L160、超时竞速 L239-260）
- CLI 命令分发：`TypeScriptPrj/TsWorkspace/ClineApi/src/cli.ts`（`config` L228、`version` L217；顶层 import `@cline/sdk` L24）
- 凭据加载：`TypeScriptPrj/TsWorkspace/ClineApi/src/credentials.ts`（`getProviderCredential` L116）；`credentials/cline.json`
- C# 进程封装：`Agent/src/Agent.ClineUsage/ClineClient.cs`（`RunNodeAsync`，空闲看门狗 120s / 默认 20min）
- C# 注册库：`FileDriven/FileDriven.Business/Registrations/Cline/ClineLibrary.cs`
- 工作流执行器入口：`FileDriven/FileDriven.Business/Registrations/UI/UIToolbox.cs:66`（`ExecuteWorkflow` 函数，Run 2 路由回退点）
- 框架层已修复：`CsharpPrj/Modules/Threading/AsyncExecution/AsyncOperationScheduler.cs`、`CsharpPrj/Share/EventBindingOrchestrator/Invocation/InvocationKernel.cs`

