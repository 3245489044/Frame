# Cline 初始化挂起根因与架构修复方案

> 日期：2026-08-08 ｜ 模块：`Agent.ClineUsage` / `ClineApi` / `Modules.Threading`（AEF）/ `FileDriven`
> 状态：**分析定稿（修复未实施）**
> 关联：`docs/analysis/2026/8/8.7/Cline复用与并行重构方案.md`、`Cline传输层挂起根因分析与重构方案.md`

---

## 1. 症状（实测日志，GUI 模式）

```
[10:19:10.500] [INFO] [BackEndDriven] [Command] 同步执行函数: function=Cline_PoolReady
[10:22:10.628] [WARNING] [BackEndDriven] [ClinePool] 就绪预算耗尽/取消，清理未就绪 daemon — warm=0
[10:22:10.629] [ERROR] [BackEndDriven] [ClinePool] daemon 池就绪失败（等待预热 worker 超时/失败）:
                Cline daemon 池未在预算 180s 内就绪（0 warm，实际耗时 180s）。
```

- 池启动正常（daemon 能 spawn、`ping` 探活通过），但 `warmup` 后 180s 内 **0 个 worker 变 warm**；
- daemon 在 180s 内**连一个错误响应都没返回**（没有任何 `ok:false` / `error` 帧）；
- 已确认与并发无关：**单 worker、顺序预热、180s 预算**仍挂死。

---

## 2. 根因（含证据链）

### 2.1 关键线索

`sdk-transport.ensureCore` 带 JS 侧硬超时（warmup payload 传 `warmupTimeoutSec`）。若 Node 事件循环存活，到点 `setTimeout` 必然 reject → daemon `respondError` → C# 收到 `Ok=false` 的具体错误。但实际**什么都没回**。

### 2.2 结论

`ClineCore.create({ backendMode: "local" })`（或 `@cline/sdk` 的模块加载 / 本地后端初始化）**同步阻塞了 Node 事件循环**：

- JS 超时定时器不触发（事件循环跑不动）；
- daemon 的 stdin 读取循环停摆（不再处理后续请求 / 心跳）；
- C# 侧 180s 只见静默，最终按自身预算耗尽报错；
- 重活在**原生子进程**里进行，所以 node 主进程 CPU≈0（曾据此误判为“锁竞争死锁”，已排除）。

> 这正是“连卡在哪都不知道”的机制：卡点在一个**把宿主事件循环阻塞掉的黑盒 SDK 初始化**里，且位于 C# 与 node 的双重观测盲区。

---

## 3. 函数调用链与文件路径

### 3.1 GUI 回归触发链（本次失败路径）

```
FileDriven/FileDriven.Launcher/Program.cs : Main(args)
  └─ FileDrivenExecutor.Run(args)                          Mode=GUI
       └─ 启动脚本加载  FileDriven/DrivenConfig/Scripts/Cline/cline_browser_test.json
            └─ ScriptConverter / JsonPipelineProcessor     FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs
                                                            FileDriven/FileDriven.Utilities/ScriptPipeline/JsonPipelineProcessor.cs
            └─ 步骤 [2] exec Cline_PoolReady
                 └─ FunctionExecutor → ClineLibrary（Cline_PoolReady 注册）
FileDriven/FileDriven.Business/Registrations/Cline/ClineLibrary.cs
  └─ Client.EnsurePoolReadyAsync(ct)                        (ClineLibrary.cs: Cline_PoolReady 委托)
Agent/src/Agent.ClineUsage/ClineClient.cs :83
  └─ _pool.Value.EnsureReadyAsync(WarmupTimeout, ct)        ClineClient.cs :87
Agent/src/Agent.ClineUsage/Pool/ClineDaemonPool.cs :71
  ├─ TriggerWarmup() :112
  ├─ WarmupCoreAsync :123
  │    ├─ _state.StartAsync(token)
  │    │    └─ PoolInitializer.StartAsync                   ClinePoolState.cs:69 / Pooling/Core/PoolInitializer.cs:38
  │    │         ├─ spawn N × ClineDaemonProcess           (ClineDaemonProcess.cs: node build/daemon.js)
  │    │         └─ ping 探活 ProbeWorkerAsync              ClinePoolState.cs:105
  │    └─ fan-out WarmWorkerAsync(id)（SemaphoreSlim）      ClineDaemonPool.cs:130
  │         └─ daemon.Ipc.SendAsync("warmup", 180s)         ClineDaemonPool.cs:173 / ClineDaemonIpc.cs:47
  │              └─ stdin 写 JSON-Line ──► node build/daemon.js
  └─ 循环等 WarmWorkerCount>0 / 预算 180s :82-90
  └─ 预算耗尽 → _state.KillAllDaemons() + 抛错             ClinePoolState.cs:90 / ClineDaemonPool.cs:94
```

### 3.2 Node 侧 warmup 处理链（★ 阻塞点）

```
TypeScriptPrj/TsWorkspace/ClineApi/build/daemon.js         (源: src/daemon.ts)
  main() → readline(stdin) → pending.then(handle)          src/daemon.ts:43 / :114-125
  handle: cmd="warmup"                                     src/daemon.ts:88
    └─ transport.warmup(warmupTimeoutSec*1000=180000, trace)   src/transport/sdk-transport.ts:413
         └─ ensureCore(180000, trace)                          sdk-transport.ts:179
              ├─ loadSdk()  ← dynamic import("@cline/sdk")     sdk-transport.ts:51 / :182
              └─ sdk.ClineCore.create({backendMode:"local"})   sdk-transport.ts:185
                   ←【★ 事件循环阻塞点】本地后端初始化（子进程 / 原生加载），
                     同步阻塞事件循环 → JS 超时定时器不触发、stdin 循环停摆、心跳停发
```

### 3.3 真实请求链（正常路径，与 warmup 形成对比）

```
ClineClient.RunTaskAsync → _pool.Value.SendAsync("ask/plan",…)   ClineClient.cs:211
  └─ ClineDaemonPool.SendAsync :186
       ├─ EnsureReadyAsync(readyBudget) :193
       ├─ PickIdleWorkerCoreAsync :258
       └─ _scheduler.RunAsync(…, IdleTimeout + Process.Kill + ExternalToken) :213
            └─ AsyncOperationScheduler + WatchdogService
               Modules/Threading/AsyncExecution/AsyncOperationScheduler.cs
               Modules/Threading/AsyncExecution/WatchdogService.cs
                 └─ daemon.Ipc.SendAsync(cmd, remaining, opCt, heartbeat)  ClineDaemonIpc.cs:47
```

---

## 4. 架构缺陷清单（含文件:行号）

| # | 缺陷 | 位置 | 后果 |
|---|------|------|------|
| 1 | **优先级颠倒**：卡死概率最高的 warmup 走裸 `Ipc.SendAsync` 硬超时，**无看门狗 / 不杀进程 / 不 dump 输出**；正常请求反而走 AEF（看门狗+进程树 kill）。 | `ClineDaemonPool.cs:173`（warmup）vs `ClineDaemonPool.cs:213`（请求） | warmup 挂死时不可观测、不可终止 |
| 2 | **“池就绪”双语义打架**：`PoolInitializer` 用 `ping` 探活（进程起 = running），真正“热”是另一处 `WarmupCoreAsync`；两者混成一个误导性的“池未就绪”。 | `PoolInitializer.cs:38/70` vs `ClineDaemonPool.cs:123` | 错误归因被吞，无法区分“进程没起”还是“ClineCore 没热” |
| 3 | **`EnsureReadyAsync` 自旋吞错**：失败后 `warmup.IsCompleted → TriggerWarmup()` 反复重触发，直到预算耗尽；每 worker 真错只进 Diagnostics（模板此前未注册）。 | `ClineDaemonPool.cs:87-88` | 最终报错零诊断价值 |
| 4 | **框架硬套**：`WorkerStateStore` 把每个 worker 状态**同步写 StorageEngine（SQLite cell）**，并带“并发变更即 `throw InvalidOperationException`”的 CAS——与“spawn 几个 node 进程”不匹配，纯开销 + 新失败面。 | `Pooling/Core/WorkerStateStore.cs:176` | 不必要的外部存储依赖与异常面 |
| 5 | **三层包装**：`PoolInitializer` 再套 `InitializationOrchestrator`，异常 `GetDeepestMessage` 解包。 | `Pooling/Core/PoolInitializer.cs:43/135` | 只为“起进程 + ping”，却叠多层编排 |
| 6 | **观测默认关闭**：trace 走 stderr 且 `--trace` 才开；`ClineDaemonProcess` 默认丢弃 stderr。 | `sdk-transport.ts:37`；`ClineDaemonProcess.cs:52-55`（已改转发） | 最需可见性处默认不可见 |
| 7 | **SDK init 阻塞事件循环**：`ClineCore.create` 的黑盒初始化与 daemon 控制面同进程，事件循环被堵 → 连超时/心跳/错误响应都发不出。 | `sdk-transport.ts:185` | “卡在哪”完全不可知（根因） |

---

## 5. 修复方案（保留全部既有功能）

> 原则：**不砍功能**。池 / 并行 / 复用 / 预算收敛 / 错误分类 / 补位重启 / 熔断全保留；核心是把 daemon 的**控制面与执行面解耦**，让事件循环永不被 SDK 阻塞，同时让每阶段可观测、每等待有界。

### A. 执行面隔离（治根）
- `daemon.ts`：把 `loadSdk` + `ClineCore.create` 放到 **`worker_threads`（或子进程）**里跑，daemon 主循环保持响应：
  - 持续发心跳 / 回 ping → C# 看门狗不再被静默骗过；
  - worker 每推进一阶段 `postMessage` → daemon 转成 `{"type":"trace","stage":…}` stdout 帧；
  - 预算到点，daemon 能真正杀掉后端子进程并回带最后 stage 的错误。
- 效果：事件循环不再被阻塞 → “连错误都不回”变成“明确报：卡在 `ClineCore.create` 本地后端（超时 Ns）”。

### B. 统一“就绪”语义 + warmup 走 AEF
- warmup 与请求同机：AEF 看门狗 + 进程树 kill + 捕获输出 dump；
- `EnsureReadyAsync` 报错带每 worker **最后 stage**，不再自旋吞错；
- “ping 就绪”与“warm 就绪”拆成两个明确状态，不再混成一个误导文案。

### C. 观测默认全开
- trace 阶段无条件发 stdout 帧；C# 逐行透传日志；不再依赖 `--trace` 开关。

### D. WorkerPool 存储瘦身（不减功能）
- 池的并行 / 补位 / 请求关联 / 健康统计全保留；
- 去掉“每 worker 状态持久化到 SQLite + CAS 抛异常”这条与进程池无关的路径，改用进程内轻量状态（running/warm/busy/pid/lastActivity），行为不变、失败面变小。

---

## 6. 验收判据

1. warmup 超时 → 日志精确显示 `stage=X（子进程=Y）`，而非笼统“池未就绪”；
2. 池 `running / warm` 语义清晰，成功 / 失败可归因；
3. 功能（池 / 并行 / 复用 / 预算 / 补位 / 熔断）全部保留；
4. 退出无孤儿 node 进程；
5. 单测通过（`Agent.ClineUsage.Tests`）。

---

## 7. 参考文件索引

| 角色 | 文件 |
|------|------|
| GUI 入口 / 脚本 | `FileDriven/FileDriven.Launcher/Program.cs`、`FileDriven/DrivenConfig/Scripts/Cline/cline_browser_test.json` |
| 函数注册 | `FileDriven/FileDriven.Business/Registrations/Cline/ClineLibrary.cs` |
| 客户端 | `Agent/src/Agent.ClineUsage/ClineClient.cs` |
| 池 / 状态 / 进程 / IPC | `Agent/src/Agent.ClineUsage/Pool/ClineDaemonPool.cs`、`ClinePoolState.cs`、`ClineDaemonProcess.cs`、`ClineDaemonIpc.cs`、`DaemonProtocol.cs`、`ClineDaemonOptions.cs` |
| 池框架（问题来源） | `CsharpPrj/Operation/StateHoldDriven/Pooling/Core/PoolInitializer.cs`、`WorkerStateStore.cs`、`RequestTracker.cs`、`PoolHealthRegistry.cs`、`WorkerStateStoreExtensions.cs` |
| AEF（请求侧看门狗） | `CsharpPrj/Modules/Threading/AsyncExecution/AsyncOperationScheduler.cs`、`WatchdogService.cs`；`Time/IdleWatchdog.cs`、`TimeBudget.cs`、`TimingDefaults.cs` |
| Node daemon / SDK | `TypeScriptPrj/TsWorkspace/ClineApi/src/daemon.ts`、`src/transport/sdk-transport.ts`、`src/config.ts`、`src/credentials.ts`、`build/daemon.js` |
| 日志模板 | `CsharpPrj/Operation/BackEndDriven/backend_log_messages.json`、`FileDriven/FileDriven.Execution/Diagnostics/execution_log_messages.json` |
