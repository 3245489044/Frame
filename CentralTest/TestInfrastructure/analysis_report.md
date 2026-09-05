# TestInfrastructure 代码分析报告

> 分析日期：2026-06-08
> 分析范围：TestInfrastructure 目录下所有 11 个源文件

---

## 一、重复逻辑 (Duplicate Logic)

### 1. `RunOnUi` 辅助方法在多个文件中重复定义

**涉及文件：**
- `UI/UiTestHelper.cs` (第 33-44 行)
- `UI/SplitterTestHelper.cs` (第 23-34 行)

**问题描述：**
两个文件都定义了完全相同的 `RunOnUi<T>(Func<T>)` 和 `RunOnUi(Action)` 私有辅助方法，都是对 `MessagePumpEngine.Instance.RunOnUiThread` 的封装。

```csharp
// UiTestHelper.cs
private static T RunOnUi<T>(Func<T> action)
{
    return MessagePumpEngine.Instance.RunOnUiThread(action);
}
private static void RunOnUi(Action action)
{
    MessagePumpEngine.Instance.RunOnUiThread(action);
}

// SplitterTestHelper.cs — 完全相同的代码
private static T RunOnUi<T>(Func<T> action)
{
    return MessagePumpEngine.Instance.RunOnUiThread(action);
}
private static void RunOnUi(Action action)
{
    MessagePumpEngine.Instance.RunOnUiThread(action);
}
```

**建议：** 提取到 `MessagePumpEngine` 中作为公共静态方法，或创建一个 `UiDispatcher` 辅助类。

---

### 2. `TestGroupResult` 与 `TestRunSummary` 功能重叠

**涉及文件：**
- `TestGroupBuilder.cs` — `TestGroupResult` (第 185-197 行)
- `Abstractions.cs` — `TestRunSummary` (第 145-170 行)

**问题描述：**
两个类都用于表示测试结果汇总，具有高度重叠的属性和计算逻辑：

| 功能 | TestGroupResult | TestRunSummary |
|------|----------------|----------------|
| 名称 | GroupName | Name |
| 通过数 | Passed | Passed (计算属性) |
| 失败数 | Failed | Failed (计算属性) |
| 总数 | Total (计算属性) | Total (计算属性) |
| 通过率 | PassRate (计算属性) | PassRate (计算属性) |
| 全部通过 | AllPassed (计算属性) | AllPassed (计算属性) |
| 耗时 | Duration | TotalDuration |

`TestGroupResult` 还额外有 `Skipped`、`Messages` 字段，而 `TestRunSummary` 有 `Results` 列表。`TestReportGenerator.FromTestGroupResult()` 方法（`TestHelper.cs` 第 383-416 行）专门用于将 `TestGroupResult` 转换为 `TestRunSummary`，这进一步证明了两个类的功能重叠。

**建议：** 合并为一个统一的测试结果模型，或让 `TestRunSummary` 直接包装 `TestGroupResult`。

---

### 3. 临时文件/目录创建功能多处重复

**涉及文件：**
- `TestBase.cs` — `IntegrationTestBase.CreateTempFile()` / `CreateTempDirectory()` (第 202-223 行)
- `TestHelper.cs` — `TestHelper.GenerateTempFileName()` / `GenerateTempDirectoryPath()` (第 28-39 行)
- `CleanupHelper.cs` — `TempDirectoryScope` (第 159-231 行)

**问题描述：**
三个不同的地方提供了临时文件/目录的创建功能，但使用方式不同：
- `IntegrationTestBase` 通过继承使用，自动在 `TestCleanup` 中清理
- `TestHelper` 是静态方法，只生成路径名，不负责清理
- `TempDirectoryScope` 通过 `IDisposable` 模式使用，自动清理

虽然各有侧重，但 `IntegrationTestBase.CreateTempFile()` 和 `TempDirectoryScope.CreateFile()` 的功能几乎完全重复（都是创建临时文件并管理清理）。

**建议：** 统一使用 `TempDirectoryScope` 模式，`IntegrationTestBase` 内部可委托给 `TempDirectoryScope`。

---

### 4. `AssertionHelper.AssertAllPassed()` 与 `AssertionHelper.AssertNoFailures()` 功能重复

**涉及文件：** `AssertionHelper.cs`

**问题描述：**
`AssertAllPassed()` (第 31-43 行) 和 `AssertNoFailures()` (第 78-90 行) 的功能几乎完全相同：
- 都检查 `result.Failed > 0`
- 都构建包含失败消息的 `StringBuilder`
- 都调用 `Assert.Fail()`

唯一的区别是错误消息的措辞不同（"有 X/Y 个测试失败" vs "有 X 个失败"）。

**建议：** 删除 `AssertNoFailures()`，或让其中一个委托给另一个。

---

## 二、冗余逻辑 (Redundant Logic)

### 1. `TestBase` 中的日志方法与 `TestLogger` 功能冗余

**涉及文件：**
- `TestBase.cs` — `LogInfo()` / `LogWarning()` / `LogError()` (第 80-105 行)
- `TestHelper.cs` — `TestLogger` 类 (第 225-303 行)

**问题描述：**
`TestBase` 提供了三个日志方法，但它们只是简单的 `Console.WriteLine` 封装，没有日志级别过滤、文件输出等功能。而 `TestLogger` 提供了完整的结构化日志功能（级别、文件保存、线程安全等）。

`TestBase` 的日志方法格式与 `TestLogger` 的格式不同：
- TestBase: `[HH:mm:ss.fff] [INFO] [testName] message`
- TestLogger: `[HH:mm:ss.fff] [Info] [loggerName] message`

**建议：** `TestBase` 内部使用 `TestLogger` 实例，统一日志格式和功能。

---

### 2. `TestPathResolver.GetTestOutputDirectory()` 与 `FileOutputTestBase` 功能冗余

**涉及文件：**
- `TestHelper.cs` — `TestPathResolver.GetTestOutputDirectory()` (第 204-213 行)
- `TestBase.cs` — `FileOutputTestBase` (第 273-330 行)

**问题描述：**
两者都提供测试输出目录的创建和管理功能，但路径结构不同：
- `FileOutputTestBase`: `%TEMP%\TestOutput\{ClassName}\{TestName}`
- `TestPathResolver`: `%TEMP%\TestInfrastructure\{testClassName}\{testName}`

**建议：** 统一路径策略，`FileOutputTestBase` 内部使用 `TestPathResolver`。

---

### 3. `CleanupHelper.SafeDeleteFile()` / `SafeDeleteDirectory()` 与 `TempDirectoryScope` 的清理逻辑冗余

**涉及文件：** `CleanupHelper.cs`

`SafeDeleteFile` 和 `SafeDeleteDirectory` 是底层工具方法，而 `TempDirectoryScope.Dispose()` 和 `TestDataIsolator.Cleanup()` 都调用了它们。这是合理的分层设计，但 `IntegrationTestBase` 的清理逻辑（第 252-259 行）使用了内联的 try-catch 而不是调用 `CleanupHelper` 的方法，导致清理逻辑不一致。

---

### 4. `MockBase.CallCount` / `WasCalled` 与 `CallbackRecorder` / `FuncMock` 的调用计数冗余

**涉及文件：** `MockBase.cs`

`MockBase` 有 `CallCount` 和 `WasCalled` 属性，`CallbackRecorder` 也有完全相同的属性，`FuncMock<TResult>` 和 `FuncMock<T, TResult>` 也有。这些类都独立实现了调用计数逻辑。

**建议：** 提取一个 `ICallable` 接口或 `CallCounter` 基类。

---

## 三、错误逻辑 (Bug / Error Logic)

### 1. `MessagePumpEngine.Shutdown()` 中的 `shutdownSw` 变量未使用

**涉及文件：** `UI/MessagePumpEngine.cs` 第 234 行

```csharp
var shutdownSw = System.Diagnostics.Stopwatch.StartNew();  // 从未使用
```

第 258 行又定义了一个 `spinSw` 用于超时等待。`shutdownSw` 是冗余变量。

---

### 2. `MessagePumpEngine.RunOnUiThreadAsync()` 潜在的线程安全问题

**涉及文件：** `UI/MessagePumpEngine.cs` 第 96-115 行

```csharp
public Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
{
    if (!_initialized) Initialize();  // 非线程安全
    ...
    _uiContext?.Post(...)  // 可能读到 null
}
```

**问题：** `_initialized` 是 `volatile` 字段，但 `Initialize()` 方法外部的检查没有加锁。如果两个线程同时调用，第一个线程还没执行到 `_uiContext = SynchronizationContext.Current`，第二个线程可能读到 `null` 的 `_uiContext`，导致 `_uiContext?.Post(...)` 不执行，`tcs` 永远不完成。

---

### 3. `MessagePumpEngine` 的 `AsyncSyncSynchronizationContext` 设置可能影响全局

**涉及文件：** `UI/MessagePumpEngine.cs` 第 87 行

```csharp
SynchronizationContext.SetSynchronizationContext(new AsyncSyncSynchronizationContext(_uiContext));
```

**问题：** 在 `Initialize()` 中设置全局 `SynchronizationContext` 会影响调用线程（可能是测试线程）的所有 `await` 行为。如果测试框架本身也依赖 `SynchronizationContext`，这可能导致意外的行为。而且这个设置没有在 `Shutdown()` 中恢复原始的 `SynchronizationContext`。

---

### 4. `TestGroupBuilder.Run()` 方法存在死锁风险

**涉及文件：** `TestGroupBuilder.cs` 第 90-97 行

```csharp
public TestGroupResult Run()
{
    return RunAsync().ConfigureAwait(false).GetAwaiter().GetResult();
}
```

**问题：** 如果调用者从 UI 线程调用此方法，`RunAsync()` 内部的 `await ExecuteTestCaseAsync` 虽然使用了 `ConfigureAwait(false)`，但如果 `ExecuteTestCaseAsync` 内部有需要回到原始上下文的操作，仍然可能死锁。

---

### 5. `TestHelper.WaitUntil()` 超时语义问题

**涉及文件：** `TestHelper.cs` 第 44-54 行

```csharp
return condition();  // 超时后再次检查
```

**问题：** 超时后再次调用 `condition()`，如果成功了返回 `true`，但调用者可能误以为条件在超时前就满足了。这可能导致对超时行为的误判。

---

### 6. `TestDataIsolator` 构造函数中调用实例方法

**涉及文件：** `CleanupHelper.cs` 第 259 行

```csharp
CurrentIsolationDirectory = CreateIsolationDirectory();  // 构造函数中调用实例方法
```

**问题：** 虽然 `CreateIsolationDirectory()` 目前不是 `virtual` 方法，但构造函数中调用实例方法是不安全的做法。如果未来有人将其改为 `virtual`，派生类可能访问到未初始化的状态。

---

### 7. `SplitterTestHelper` 与 `UiTestHelper` 中 Splitter 相关方法重复

**涉及文件：**
- `UI/UiTestHelper.cs` — `GetSplitterDistance()` / `SetSplitterDistance()` (第 603-614 行)
- `UI/SplitterTestHelper.cs` — `GetSplitterDistance()` / `SetSplitterDistance()` (第 39-50 行)

**问题描述：**
两个类都提供了完全相同的 `GetSplitterDistance` 和 `SetSplitterDistance` 方法，实现完全一致。

**建议：** 删除 `UiTestHelper` 中的 Splitter 相关方法，统一使用 `SplitterTestHelper`。

---

## 四、总结

| 类别 | 数量 | 关键问题 |
|------|------|---------|
| **重复逻辑** | 4 处 | RunOnUi 重复、TestGroupResult/TestRunSummary 重叠、临时文件管理多处重复、AssertAllPassed/AssertNoFailures 重复 |
| **冗余逻辑** | 4 处 | TestBase 日志与 TestLogger 冗余、输出目录管理冗余、清理逻辑不一致、调用计数多处独立实现 |
| **错误逻辑** | 7 处 | Shutdown 中冗余变量、RunOnUiThreadAsync 线程安全、全局 SynchronizationContext 未恢复、死锁风险、WaitUntil 超时语义问题、构造函数调用实例方法、Splitter 方法重复 |

**建议的修复优先级：**
1. **高优先级（错误逻辑）：** MessagePumpEngine 的线程安全问题、全局 SynchronizationContext 未恢复
2. **中优先级（重复逻辑）：** RunOnUi 提取公共方法、SplitterTestHelper 与 UiTestHelper 合并
3. **低优先级（冗余逻辑）：** 日志统一、测试结果模型合并、临时文件管理统一
