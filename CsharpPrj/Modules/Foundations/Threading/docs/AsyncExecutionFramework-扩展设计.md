# Threading 模块扩展 — 统一异步执行框架（Async Execution Framework, AEF）

> 版本：1.0（设计草案）｜ 模块：`CsharpPrj/Modules/Threading`（`Modules.Threading`）｜ 依赖：无外部依赖（独立叶子模块）

## 1. 背景与动机

现有代码中，异步长任务（函数执行、node 子进程、UI 等待）的生命周期被**碎片化地散落在五处**，各自造 TCS、各自设固定超时、各自做同步阻塞等待，导致四大根因问题：

| 病根 | 现状 | 后果 |
|---|---|---|
| 生命周期碎片化 | `EcsEventBus`/`ClickSystem`/`AwaitValueSystem`/`FunctionCallFacade`/`ClineClient` 各自实现 | 同一件事五种写法，改一处别处不联动 |
| 无统一取消链 | 超时用 `Task.WhenAny` 竞速、超时后 `ContinueWith` 只打日志 | **超时=放弃等待≠终止** → 孤儿 node/ClineCore 进程级联拖慢后续 |
| 固定时限不一致 | 30s/60s/120s/20min 多套预算叠加 | 真挂起时烧满预算才失败，无法区分"还在跑/已死" |
| 同步阻塞等待 | 命令线程 `Task.Wait()` | 死锁风险、命令队列卡死、日志延迟 60s |

**目标**：把 `Threading` 模块从"重入守卫 + 线程上下文 + AsyncLocal 作用域"扩展为**统一异步执行框架（AEF）**，让所有长任务收敛为单一 **`AsyncOp`**，经**单一调度器**流转；生命周期、取消、空闲看门狗、结果、日志全部由框架接管。

> 本模块定位为**无外部依赖的通用异步原语层**。进程级执行池（`ExecutionCenter.TaskExecutorPool`）作为宿主可承载调度器，但不被本模块强依赖；取消/空闲语义在本层完成，`IProcessHandle` 由外部进程封装（如 `ClineClient`）实现。

---

## 2. 架构总览

```
                      ┌─────────────────────────────────────────────┐
  调用方（EcsEventBus / ClickSystem / AwaitValueSystem / FunctionCallFacade）
        │  Submit(AsyncOp)                                          │
        ▼                                                           │
  ┌────────────────────────  AsyncExecutionFramework  ───────────┐ │
  │  AsyncOperation<T>   (Id/State/CTS/Heartbeat/Completion/...) │ │
  │        │                                                     │ │
  │  AsyncOperationScheduler  (并发上限 + 隔离 + 防重入)          │ │
  │        │                                                     │ │
  │  WatchdogService  (按 Heartbeat 判定空闲挂起 → cancel)        │ │
  │        │                                                     │ │
  │  CancellationToken 贯穿 → IProcessHandle.Kill()  (终止孤儿)  │ │
  │        │                                                     │ │
  │  AsyncOperationResult + op.Completed 事件                    │ │
  └────────┼─────────────────────────────────────────────────────┘ │
           ▼                                                        │
  UI 渲染 / click-wait / await / 日志 / 度量（经 ThreadContext 调度）
```

**设计原则**（对齐本模块既有风格）：

- **单一生命周期**：所有长任务 = `AsyncOp`，不出现模块外的裸 TCS。
- **取消贯穿为强约束**：超时 / 空闲挂起 / 主动取消 三态收敛为同一动作 → `cancel → IProcessHandle.Kill()`。
- **空闲（idle）而非固定期限**：liveness 以心跳为准，调用方不再关心"最长几秒"，只关心"最后 N 秒是否有动静"。
- **非阻塞完成**：命令层用 `await op.WaitAsync()`，模块内部/调用方禁用 `Task.Wait()`。
- **并发与隔离**：调度器提供并发上限与按操作隔离，一个挂起实例不再锁死全局。
- **可观测**：每个 `AsyncOp` 记录 Start/Idle/End/Result，挂起可 dump 诊断。

---

## 3. 新增类型（`Modules.Threading` 命名空间）

### 3.1 操作状态（扩展既有 `ExecutionPhase`）

现有 `ExecutionPhase`（Idle/Running/Completed/Failed/Cancelled/Timeout）已覆盖主状态机，**新增 `IdleHung` 区分"空闲挂起"与"绝对超时"**：

```csharp
public enum AsyncOpPhase
{
    Pending   = 0,   // 已提交未调度
    Running   = 1,   // 执行中（含首次启动初始化）
    Completed = 2,   // 成功完成
    Failed    = 3,   // 失败（返回结果或抛异常）
    Cancelled = 4,   // 主动取消
    TimedOut  = 5,   // 绝对超时（可选启用）
    IdleHung  = 6    // 空闲挂起（心跳超窗）——本框架核心新增态
}
```

### 3.2 进程句柄抽象（供框架终止孤儿进程）

```csharp
public interface IProcessHandle
{
    int Id { get; }
    void Kill(bool entireProcessTree = true);   // 框架取消/空闲超时回调
    bool IsExited { get; }
}

public sealed class ChildProcessHandle : IProcessHandle
{
    // 包装 System.Diagnostics.Process，Dispose 时确保退出
}
```

### 3.3 统一异步操作模型 `AsyncOperation<T>`

```csharp
public sealed class AsyncOperation<T> : IDisposable, IProgress<T>
{
    public Guid Id { get; }                          // 全局唯一
    public string Name { get; }
    public AsyncOpPhase Phase { get; }
    public CancellationTokenSource Cts { get; }      // 取消贯穿源
    public Task<T> Completion { get; }               // 唯一完成信号（取代散落 TCS）
    public T? Result { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? LastHeartbeatAt { get; }  // 看门狗输入
    public IProcessHandle? Process { get; set; }     // 可杀对象（如 node/ClineCore）

    // 心跳：任何"仍在推进"的证据（流式输出/步骤推进）都应调用
    public void ReportProgress(T value) { /* 更新 LastHeartbeatAt；可转发 */ }

    // 完成/失败/取消 终结方法
    public void Complete(T result);
    public void Fail(Exception ex);
    public void Cancel();
}
```

### 3.4 统一结果契约 `AsyncOperationResult`

取代 `FunctionCallFacadeResult`/`ExecutionResult`/`FunctionCallResult` 的碎片：

```csharp
public readonly record struct AsyncOperationResult
{
    public bool IsSuccess { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public long ElapsedMs { get; init; }
    public AsyncOpPhase EndPhase { get; init; }   // Completed/Failed/Cancelled/TimedOut/IdleHung
}
```

### 3.5 空闲看门狗 `WatchdogService`

```csharp
public sealed class WatchdogService : IDisposable
{
    // 启动：以 pollInterval 为周期轮询所有已注册操作
    public void Start(TimeSpan pollInterval, TimeSpan idleWindow);
    // 注册/注销操作
    public void Track(AsyncOperation op);
    public void Untrack(AsyncOperation op);

    // 规则：op.LastHeartbeatAt 距今 > idleWindow → 判定 IdleHung
    //        → op.Phase = IdleHung
    //        → op.Process?.Kill()
    //        → op.Fail(TimeoutException("空闲挂起"))
}
```

> `idleWindow <= 0` → 该操作不启用空闲看门狗（= "完全不设时限"，代价：真挂起无法自愈，仅在显式要求时使用）。

### 3.6 集中调度器 `AsyncOperationScheduler`

```csharp
public sealed class AsyncOperationScheduler : IDisposable
{
    public AsyncOperationScheduler(int maxConcurrency, TimeSpan idleWindow);

    // 提交一个长任务（返回可 await 的操作句柄）
    public Task<AsyncOperationResult> RunAsync(
        string name,
        Func<CancellationToken, IProgress, Task<object?>> work,
        AsyncOperationOptions? options = null);
}
```

调度职责：并发上限（`SemaphoreSlim`）、空闲看门狗接入、防重入（复用 `ReentryGuard`，同一 key 不并发重复执行）、按操作隔离（`options.IsolationKey` → 可映射到 `--data-dir`）。

`AsyncOperationOptions`：

```csharp
public sealed class AsyncOperationOptions
{
    public TimeSpan? IdleTimeout { get; init; }    // null=用调度器默认；<=0=不启用空闲看门狗
    public TimeSpan? AbsoluteTimeout { get; init; } // null=不设绝对上限（默认）
    public object? ReentryKey { get; init; }        // 防重入去重
    public string? IsolationKey { get; init; }      // 隔离域（如每次调用独立 data-dir）
    public IProcessHandle? Process { get; init; }   // 可杀对象
}
```

### 3.7 `ThreadingBridge` 扩展

```csharp
public static class ThreadingBridge
{
    // 既有
    public static void Initialize();          // SetMainThread 等
    public static string GetVersion();

    // 新增：AEF 装配
    public static void InitializeAEF(AsyncOperationScheduler scheduler, WatchdogService watchdog);
    public static string GetStatusSummary();  // 追加：ActiveOps / IdleHung / 并发占用
}
```

---

## 4. 关键语义

### 4.1 超时/空闲/取消 → 统一"取消→杀进程"

```
async Task<AsyncOperationResult> 调度器执行:
  var cts = op.Cts;
  try { result = await work(cts.Token, op); }
  catch (OperationCanceledException) when (!callerCancelled):
        → 判 op.Phase (TimedOut | IdleHung)
        → op.Process?.Kill()   // 孤儿进程在此终止
        → 返回失败结果
```

不再出现"放弃等待但底层继续跑"的路径。

### 4.2 空闲语义 = 心跳

- 心跳源约定：
  - 流式 AI 调用：`cli.ts --stream` 的每行 NDJSON 输出。
  - 步骤型任务：每完成一个 step 调 `op.ReportProgress`。
  - node 子进程：`OutputDataReceived`/`ErrorDataReceived` 每行。
- 调用方只需在"有推进"处发心跳，框架按 `idleWindow` 兜底。

### 4.3 非阻塞等待

```csharp
// 推荐：await 完成信号（非阻塞）
var result = await op.Completion.WaitAsync(CancellationToken.None);

// 若确需带绝对上限（可空）
var result = await op.Completion.WaitAsync(TimeSpan.FromSeconds(60));
```

模块内与调用方均禁用 `Task.Wait()`。

### 4.4 隔离与防重入

- `IsolationKey`：一个挂起的 `ClineCore.create` 只影响其隔离域（如独立 `--data-dir`），不锁死全局。
- `ReentryKey`：同一函数并发重复执行时，后到者直接返回 `Failed(IsReentering)`，避免重复真实调用叠加。

---

## 5. 模块结构（新增文件建议）

```
CsharpPrj/Modules/Threading/
├── AsyncLocalContext.cs          (既有)
├── ExecutionScope.cs             (既有)
├── ExecutionStatus.cs            (既有)
├── ReentryGuard.cs               (既有)
├── ThreadContext.cs              (既有)
├── ThreadingBridge.cs            (既有 + 新增 InitializeAEF/状态)
├── Threading.csproj              (既有)
├── docs/
│   ├── AsyncExecutionFramework-扩展设计.md   (本文档)
│   └── 现有代码应用集成指南.md               (见配套文档)
└── AsyncExecution/
    ├── AsyncOpPhase.cs           (新增：AsyncOpPhase 枚举)
    ├── AsyncOperation.cs         (新增：AsyncOperation<T>)
    ├── AsyncOperationOptions.cs  (新增)
    ├── AsyncOperationResult.cs   (新增)
    ├── IProcessHandle.cs         (新增)
    ├── WatchdogService.cs        (新增：空闲看门狗)
    ├── AsyncOperationScheduler.cs(新增：集中调度器)
    └── OperationExtensions.cs    (新增：WaitAsync 扩展)
```

---

## 6. 与既有原语的关系

| 既有原语 | AEF 中的角色 |
|---|---|
| `ThreadContext.Post/InvokeAsync` | 结果/日志的 UI 线程调度（消除延迟日志） |
| `AsyncLocalContext` / `ExecutionScope` | 操作上下文传播（操作名、隔离域可进 AsyncLocal） |
| `ReentryGuard` | 调度器防重入（`AsyncOperationOptions.ReentryKey`） |
| `ExecutionPhase`/`StepRecord`/`ExecutionResult` | 作为 `AsyncOp` 的细粒度 step 记录承载 |

---

## 7. 实施顺序与验收

| 阶段 | 内容 | 验收 |
|---|---|---|
| P1 | `AsyncOpPhase` + `AsyncOperation<T>` + `AsyncOperationResult` + 取消贯穿 | 可创建/取消/完成一个 AsyncOp，取消可触发 `IProcessHandle.Kill` |
| P2 | `WatchdogService`（心跳+空闲判定） | 无心跳的操作在 `idleWindow` 后被判定 IdleHung 并 kill |
| P3 | `AsyncOperationScheduler`（并发上限+防重入+隔离） | 并发受限；同 key 防重入；隔离域互不影响 |
| P4 | `ThreadingBridge.InitializeAEF` + 状态摘要 | 运行期可见 ActiveOps/IdleHung/并发占用 |

---

## 8. 边界与收益（诚实说明）

- **本框架解决**：挂起可检测、可终止；孤儿进程与级联消失；死锁与延迟日志消失；超时预算收敛为"空闲"单一语义；"不设固定时限"成为一等选项。
- **本框架不解决**：底层某个具体工作为何挂起（如 `ClineCore.create(local)` 初始化失败）——那是传输层问题，需在"现有代码应用指南"中通过传输替换（切回 `cline` CLI / 换通道）处理；但框架保证其在 idle 窗口内被终止且不阻塞其他操作。
