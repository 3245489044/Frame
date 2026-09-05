# Cline 调用挂起：已知问题排查汇总与独立测试框架

> 日期：2026-08-21 ｜ 模块：`Agent.ClineUsage` / `Agent.Modules.Cline` / `Agent.Orchestration.StepExecution` / `@cline/core`（TypeScript）/ `CentralTest/TypeScriptTest/ClineSdk.Tests`
> 状态：**挂起现象已规避（本地模式）+ 根因已收敛到 Cline 调用层；外部真实 LLM 仍可能复现；确切触发条件待差分定位**
> 关联既有分析：`docs/analysis/2026/8/8.8/Cline初始化挂起根因与架构修复方案.md`、`docs/analysis/2026/8/8.21/统一LLM网关收敛重构-实施状态与链路分析.md`

---

## 〇、一句话结论

框架（daemon 池 / 引擎 / 看门狗）的复杂度与"Cline 调用是否卡住"**无因果关系**。问题纯粹出在 **Cline 调用层**：`@cline/core` 的模块加载 / `ClineCore.create` 可能**同步阻塞引擎的事件循环**，导致 JS 超时/心跳/错误帧全部失效，表现为**无限挂起、CPU≈0**。

而**独立调用 Cline 实测不挂**（含并发 3 个，均约 2s 成功）→ 挂起是**框架运行时特定条件**触发的，不是 Cline 调用本身必现。为坐实这一点，建立了独立测试框架 `CentralTest/TypeScriptTest/ClineSdk.Tests`。

---

## 一、已知问题清单

| # | 问题 | 现象 | 根因 | 状态 |
|---|------|------|------|------|
| P1 | Cline SDK 初始化挂起 | 引擎卡在 `ensure-core-start`，无后续阶段/心跳/错误，CPU≈0 | `@cline/core` 模块加载 / `ClineCore.create({backendMode:"local"})` 同步阻塞事件循环，JS 超时救不回 | **已规避（本地模式）**；外部真实 LLM **未根治** |
| P2 | 会话发送 129s AEF 超时 | `AgentUi_ConversationSend` `stages=[Dispatch:0ms\|FunctionStart:0ms]` | 会话 collect/act 步走 `cline_execute` → daemon 池，池 warmup 卡死永不就绪，发送等池就绪至 AEF 超时 | **已修复**（本地模式绕开 daemon） |
| P3 | 终答为空 | 三步都跑完、`CallLog=3`，但"终答:"空白 | `RoundStateMapper.Seed` 无条件注入 `exec_result=null`（键恒存在）→ `WorkflowRunner` 终答链 `ExtractText(null)` 返回空串短路 `??`，到不了 `TraceLeafText` | **已修复** |
| P4 | 外部真实 LLM 仍可能挂 | 非本地模式走 cline 步仍可能复现 P1 | `ClineCore.create` 对本地/外部**写死相同**（`backendMode:"local"`），同步阻塞点相同；本地靠绕开、外部无法绕开 | **未解决**（需单独处理） |
| P5 | 挂起确切触发条件未定位 | 独立调用不挂、框架内挂 | 疑为 C# 继承给子进程的 env / 传给 Cline 的 provider 参数 / 传输时序差异 | **未定位**（待差分验证） |

---

## 二、P1 —— Cline SDK 初始化挂起（根因）

### 现象
- 引擎（engine 子进程，pid 35552）在 `ensure-core-start` 后**再无任何日志**：无 `load-sdk-ok`、无 `engine-running` 心跳、无错误帧。
- Node 主进程 CPU≈0（重活在原生子进程里进行）。

### 根因
`@cline/core` 的 `loadSdk()`（动态 `import("@cline/sdk")`，内部 `export * from "@cline/core"`）与 `ClineCore.create` 在该机器上**同步阻塞事件循环**：
- `@cline/sdk` 只是 `@cline/core` 的别名（`dist/index.js` 仅 `export * from "@cline/core"`）；
- `ClineCore.create({ clientName, backendMode:"local" })` 初始化本地运行时：SQLite 会话后端、telemetry / feature-flags（PostHog，网络）、运行时主机等副作用；
- 同步阻塞 → `setTimeout`（引擎 287s 超时、心跳）**永不触发** → JS 层超时对同步阻塞无能为力；
- 因阻塞发生在**同步代码**里，只有**进程级 kill** 能兜底（daemon 看门狗已做）。

### 证据链
- 日志最后帧 `stage=ensure-core-start`（`sdk-transport.ts:320`），`load-sdk-ok`（:324）未出现 → 卡在 `loadSdk()`/`create`。
- 8.8 文档已确认 `ClineCore.create({backendMode:"local"})` 是"同步阻塞事件循环"的黑盒点。
- 8.21 记录过一次该点**通过**（`phase2-core-start-ok`）→ 说明并非必现，环境/并发相关。

---

## 三、P2 —— 会话发送超时（已修复）

### 链路
```
AgentUi_ConversationSend
 → ConversationPipeline.RunSignalAsync
 → collect/act 步 cline_execute → ClineAgentExecutor → ClineClient(daemon 池)
 → EnsureReadyAsync 等池就绪 ← 池因 P1 永不就绪 → 129s AEF 超时
```

### 修复
`Projects/Agent/src/Agent.Modules/Cline/ClineAgentExecutor.cs`：
`LlmMode.LocalOnly` 时**绕开 Cline daemon**，直接经统一网关唯一咽喉 `LlmGateway.Current` 出确定性本地结果（`ClineResult`）。按调用时点判定、不动模板、不影响真实 LLM。

### 验证
本地模式发送毫秒级完成、`状态=Completed`、三步全跑、`CallLog=3`；`Agent.sln` 构建 0 警告 0 错误。

---

## 四、P3 —— 终答为空（已修复）

### 根因（精确到行）
- `RoundStateMapper.Seed`（`RoundStateMapper.cs:61`）**无条件** `vars[ExecResult] = lease.LastResult`（新会话为 `null`）→ `exec_result` **键恒存在、值为 null**；
- `WorkflowRunner.RunRoundAsync` 终答链（`WorkflowRunner.cs:217-222`）：
  ```
  execResult?.Text
    ?? (vars.TryGetValue("exec_result", out t) ? ExtractText(t) : null)   // t=null → ExtractText(null)=""（空串，非 null！）
    ?? TraceLeafText(vars)                                                 // 永远到不了这里
    ?? ""
  ```
  `ExtractText(null)` 返回**空字符串**，`??` 对空串**不继续短路** → 直接以 `""` 作终答，`TraceLeafText`（专为"不产 exec_result 的会话模板取 act_result"设计）被跳过。

### 修复
终答链只在**非空**时通过（`is { Length: > 0 }`），空串视为"无终答"继续走 `TraceLeafText` → 会话 `act_result`（本地响应文本）成为终答。主循环路径（`execResult.Text` 非空）行为不变。

---

## 五、P4 —— 外部真实 LLM 仍可能挂（未解决）

- `ClineCore.create` 的 `backendMode:"local"` 是**写死**的（`sdk-transport.ts:327`），对本地/外部完全相同 → P1 的同步阻塞点对两条路都成立；
- 本地模式靠 P2 修复**绕开** daemon；外部真实 LLM 需要真实 Cline 编码代理，**无法用网关文本回答替代**，只能直面 SDK 初始化；
- 治本方向（未实施）：进程级看门狗 kill + 重试；或彻底改走标准形态（官方 CLI / 标准 provider 直连），见 `ClineSdk.Tests/README.md`。

---

## 六、P5 —— 确切触发条件未定位（待差分验证）

### 已排除
独立调用（含**并发 3 个**）`ClineCore.create({backendMode:"local"})` 全部 **ok、约 2s** → 排除"Cline 调用本身"与"并发 create"。

### 待查
挂起是**框架运行时特定条件**触发，疑点：
1. C# 进程**继承给子进程的环境变量**（`CLINE_*` / proxy / telemetry 开关等）；
2. 传给 Cline 的**具体 provider/参数**（框架里 `provider=semantic` + openai-compatible 本地端点组合）；
3. daemon / engine 的**传输时序**。

---

## 七、新建独立测试框架：`CentralTest/TypeScriptTest/ClineSdk.Tests`

> 目的：把"Cline 调用本身"从框架剥离，独立复现/验证/定位挂起，回答"挂起是不是 Cline 调用本身的问题"。

### 架构（关键：进程级硬超时）
```
父进程（vitest 用例 或 run-all.mjs）
  └─ spawn node runner.mjs <sdkEntry> <mode> [--start]
       └─ 绝对路径 import @cline/sdk → ClineCore.create → (可选) core.start
          阶段日志 → stderr（同步 flush）；终态 → stdout `RESULT:<ok|error>`
  超时 → kill("SIGKILL") → status=timeout
```
- SDK 经**绝对路径**加载（`src/config.ts` 向上查找 ClineApi 依赖树），不依赖本工程安装 vitest/@cline；
- 因同步阻塞无法被 JS 超时救回，**必须**用子进程 + 进程级 kill。

### 结构
| 文件 | 职责 |
|------|------|
| `package.json` | `test`(vitest) / `test:cli`(纯 node) |
| `vitest.config.ts` | vitest 配置 + `@cline/sdk` alias |
| `src/config.ts` | 向上查找 `@cline/sdk` 绝对路径 |
| `src/runner.mjs` | 子进程执行器：import → create → (start) |
| `src/probe.ts` | 子进程封装 + 进程级硬超时（vitest 用） |
| `src/run-all.mjs` | 纯 node 独立运行器 |
| `src/cline-sdk.test.ts` | vitest 用例 |

### 实测结论（2026-08-21）
| 用例 | 结果 |
|------|------|
| `create(backendMode=local)` | **ok，~2s，不挂** |
| 并发 3×create(local) | **全部 ok，~2.1s，不挂** |
| `create(backendMode=auto)` | ok，~2s |
| vitest 全量 | **3/3 通过** |

> **结论**：Cline 调用本身（含并发）不是问题；挂起是框架运行时条件触发的。

### 运行
```bash
cd CentralTest/TypeScriptTest/ClineSdk.Tests
node src/run-all.mjs        # 纯 node，无需依赖
npm test                     # vitest（已装 vitest）
```

---

## 八、下一次挂起时的对照验证法

1. `node src/run-all.mjs` 独立跑 → **ok** ⇒ 挂起源自框架运行条件（环境/传参/时序），非 Cline 调用；
2. 独立跑若复现 **timeout** ⇒ Cline 调用在特定环境/参数下同步阻塞 → 用 `runner.mjs` stderr 阶段（import/create/start）定位卡点 + `node --cpu-prof` 定到具体模块；
3. 用 `ClineSdk.Tests` 与框架做**差分**（逐一比对 env、provider 参数、并发度），锁定 P5 触发条件。

---

## 九、遗留与建议

| 项 | 建议 |
|----|------|
| P4 外部真实 LLM 挂起 | 进程级看门狗 kill + 重试；或换 Cline 标准调用形态（官方 CLI / 标准 provider 直连） |
| P5 触发条件 | 用 `ClineSdk.Tests` 做 env/参数差分，配合 `--cpu-prof` 剖面定到具体模块 |
| `@cline/core` 同步阻塞根 | 剖面定位到 telemetry(PostHog) / feature-flags / 某原生 `.node` 依赖后，显式禁用相应副作用（传 no-op） |

