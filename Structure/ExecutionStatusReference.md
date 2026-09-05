# 执行状态标志位参考文档

> 记录 MainLayout.json 渲染调用链中所有执行状态标志位的定义、位置和读取方式。
>
> 通用基础类型定义在 `CsharpPrj/Modules/Threading/ExecutionStatus.cs`（命名空间 `Modules.Threading`），
> 业务层通过 `Data` 字典传入自有数据。

---

## 一、标志位总览

| 层级 | 标志位 | 文件 | 类型 |
|------|--------|------|------|
| **通用基础** | `ExecutionPhase` | `CsharpPrj/Modules/Threading/ExecutionStatus.cs` | `enum` |
| ^^ | `StepRecord` | ^^ | `class` |
| ^^ | `ExecutionResult` | ^^ | `class` |
| **UI 初始化** | `MainForm.InitializationTask` | `FileDriven/FileDriven.Execution/Initial/MainForm.cs` | `Task<ExecutionResult>` |
| **UI 初始化** | `IStageCoordinator`（三阶段） | TConfig 接口 + MainForm 使用 | `interface` |
| **脚本执行** | `StartGuiModeContext.ScriptExecutionResult` | `FileDriven/FileDriven.Execution/Gui/Blocks/StartGuiModeContext.cs` | `ExecutionResult?` |
| **Block 层** | `ApplicationRunBlock` 返回 `BlockResult` | `FileDriven/FileDriven.Execution/Gui/Blocks/ApplicationRunBlock.cs` | `Ok / Fail` |
| **ECS 管线** | `EcsRenderPipeline._hasExecuted` | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/EcsRenderPipeline.cs` | `private bool` |
| ^^ | `EcsRenderPipeline._pipelineDirty` | ^^ | `private bool` |
| **CSG 输出** | `WinFormsOutputPort._isFirstApply` | `CsharpPrj/Operation/FrontEndDriven/Ports.cs` | `private bool` |
| ^^ | `WinFormsOutputPort.LastAppliedNodeCount` | ^^ | `public int` |

---

## 二、通用基础类型（Modules.Threading）

### ExecutionPhase 枚举

```csharp
public enum ExecutionPhase
{
    Idle = 0,     // 未开始/空闲
    Running,      // 执行中
    Completed,    // 已完成（成功）
    Failed,       // 失败
    Cancelled,    // 已取消
    Timeout       // 超时
}
```

### StepRecord 类

```csharp
public sealed class StepRecord
{
    public string StepName { get; set; }              // 步骤名称（如 "MainLayoutLoaded"）
    public string? Description { get; set; }          // 步骤描述（可选）
    public ExecutionPhase Phase { get; set; }          // 执行阶段
    public bool IsSuccess { get; set; }                // 是否成功
    public string? ErrorMessage { get; set; }          // 错误消息（失败时）
    public string? ExceptionType { get; set; }         // 异常类型名称（失败时）
    public long DurationMs { get; set; }               // 步骤耗时（毫秒）
    public DateTime Timestamp { get; set; }             // 步骤开始时间戳（UTC）
    public Dictionary<string, object?>? Tags { get; set; }  // 调用方自定义标签
}
```

### ExecutionResult 类

```csharp
public sealed class ExecutionResult
{
    public bool IsSuccess { get; set; }                // 是否成功
    public string? ErrorMessage { get; set; }          // 错误消息（失败时）
    public string? ExceptionType { get; set; }         // 异常类型名称（失败时）
    public long TotalDurationMs { get; set; }           // 总耗时（毫秒）
    public DateTime StartTime { get; set; }             // 执行开始时间（UTC）
    public DateTime EndTime { get; set; }               // 执行结束时间（UTC）
    public List<StepRecord> Steps { get; set; }         // 步骤记录列表
    public Dictionary<string, object?>? Data { get; set; }  // 调用方自定义数据

    public void AddStep(StepRecord step);               // 添加步骤记录
    public StepRecord? FindStep(string stepName);       // 按名称查找步骤
}
```

#### Data 字典业务键一览

调用方通过 `Data` 字典传入业务特定数据，当前定义的键：

| 键名 | 值类型 | 来源 | 说明 |
|------|--------|------|------|
| `"hasExecuted"` | `bool` | FileDriven | 管线是否至少执行过一次（对应 `_hasExecuted`） |
| `"isDirty"` | `bool` | FileDriven | 管线是否有未应用的变化（对应 `_pipelineDirty`） |
| `"isFirstApply"` | `bool` | FileDriven | 是否为首次 Apply（对应 `_isFirstApply`） |
| `"lastAppliedNodeCount"` | `int` | FileDriven | 最后一次 Apply 应用的节点数 |
| `"totalCommands"` | `int` | FileDriven | 脚本总命令数 |
| `"succeededCount"` | `int` | FileDriven | 脚本成功命令数 |
| `"failedCount"` | `int` | FileDriven | 脚本失败命令数 |

---

## 三、UI 初始化标志位

### 3.1 MainForm.InitializationTask

**位置**：`FileDriven/FileDriven.Execution/Initial/MainForm.cs`

```csharp
public Task<Modules.Threading.ExecutionResult> InitializationTask => _initTcs.Task;
```

**类型**：`Task<Modules.Threading.ExecutionResult>`

**设置时机**：`MainForm.OnLoadAsync()` 中三阶段全部完成后设置。

| 条件 | `IsSuccess` | `ErrorMessage` / `ExceptionType` | `TotalDurationMs` |
|------|-------------|----------------------------------|-------------------|
| 三阶段全部成功 | `true` | `null` / `null` | 实际耗时 |
| 任一阶段抛异常 | `false` | ex.Message / ex.GetType().FullName | `sw.ElapsedMilliseconds` |

**消费方**：`ApplicationRunBlock` 在 `Form.Shown` 事件中通过 `await initTask` 消费。

### 3.2 IStageCoordinator 阶段协调器

**位置**：`CsharpPrj/Plate/TConfig/TrConfig/Services/IStageCoordinator.cs`

**注册的三个阶段**（在 `FileDrivenGuiInitializerOrchestrator.CreateContextAsync()` 中注册）：

| 阶段名 | 描述 | 完成位置 | 成功条件 |
|--------|------|----------|----------|
| `"MainLayoutLoaded"` | 主布局加载完成 | `MainForm.cs:118` | `LoadUIConfigAsync(MainLayout)` 无异常 |
| `"ConfigSelectorLoaded"` | 配置选择器加载完成 | `MainForm.cs:129` | `LoadUIConfigAsync(Content)` 无异常 |
| `"InitializationCompleted"` | 整体初始化完成 | `MainForm.cs:137` | `RenderAsync()` 无异常 |

**API**：
```csharp
void RegisterStage(string stageId, string description, int timeoutMs = 30000);
void CompleteStage(string stageId, bool success = true, string? errorMessage = null);
Task<bool> WaitForStageAsync(string stageId);
ExecutionStatus GetState(string stageId);
```

---

## 四、脚本执行标志位

### 4.1 StartGuiModeContext.ScriptExecutionResult

**位置**：`FileDriven/FileDriven.Execution/Gui/Blocks/StartGuiModeContext.cs`

```csharp
public Modules.Threading.ExecutionResult? ScriptExecutionResult { get; set; }
```

**设置时机**：`ApplicationRunBlock` 的 `Form.Shown` 事件处理中，脚本执行完毕时设置。

**Data 字典示例**：
```csharp
guiContext.ScriptExecutionResult = new ExecutionResult
{
    IsSuccess = scriptSuccess,
    TotalDurationMs = elapsedMs,
    Data = new Dictionary<string, object>
    {
        ["totalCommands"] = 5,
        ["succeededCount"] = 4,
        ["failedCount"] = 1
    }
};
```

---

## 五、Block 层标志位

### 5.1 ApplicationRunBlock

**位置**：`FileDriven/FileDriven.Execution/Gui/Blocks/ApplicationRunBlock.cs`

**返回值**：`Task<BlockResult>`

| 场景 | 返回值 |
|------|--------|
| 消息循环启动成功 | `BlockResult.Ok("消息循环已启动，前台线程已返回")` |
| MainForm 为 null/已释放 | `BlockResult.Fail(...)` |
| Load 同步阶段超时 | `BlockResult.Fail("MainForm.Load 同步阶段超时（30 秒）")` |
| 后台线程异常 | `BlockResult.Fail(...)` |

---

## 六、ECS 管线内部标志位（FrontEndDriven）

### 6.1 EcsRenderPipeline

**位置**：`CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/EcsRenderPipeline.cs`

| 标志位 | 类型 | 初始值 | 设 `true` | 设 `false` | 作用 |
|--------|------|--------|-----------|------------|------|
| `_hasExecuted` | `bool` | `false` | CSG Apply 成功后 | `Clear()` | 标记管线至少执行过一次 |
| `_pipelineDirty` | `bool` | `false` | `LoadConfiguration()` / `RecalculateLayout()` | CSG Apply 成功后 | 标记管线有未应用的变化 |

**CSG 执行判断**（`ExecuteRenderStage()` line 384）：
```csharp
bool shouldExecuteCsg = !_hasExecuted || _pipelineDirty;
```

### 6.2 WinFormsOutputPort

**位置**：`CsharpPrj/Operation/FrontEndDriven/Ports.cs`

| 标志位 | 类型 | 初始值 | 设 `false` | 作用 |
|--------|------|--------|------------|------|
| `_isFirstApply` | `bool` | `true` | Apply 成功后 set | 标记首次应用（首次全量，后续差异） |
| `LastAppliedNodeCount` | `int` | `0` | Apply 成功后设值 | 最后一次 Apply 应用的节点数 |

---

## 七、调用链标志位传递图

```
ApplicationRunBlock
│
├─ [同步] Form.Load 触发 → loadStarted.Set()         ← 仅同步部分完成
│
├─ [异步] MainForm.OnLoadAsync()                     ← async 初始化
│   │
│   ├─ CompleteStage("MainLayoutLoaded", true)       ← ← Ⅰ
│   ├─ CompleteStage("ConfigSelectorLoaded", true)   ← ← Ⅱ
│   ├─ ExecuteRenderStage()
│   │   ├─ LayoutSystem.Update()                     ← 布局计算
│   │   ├─ SplitContainerSystem.Update()              ← 分割容器
│   │   ├─ StyleSystem.Update()                       ← 样式合并
│   │   ├─ EventBindingSystem.Update()                ← 事件绑定
│   │   ├─ CsgEntryPoint.Collect()                   ← ECS→CanonicalForm
│   │   └─ WinFormsOutputPort.Apply()                ← →WinForms控件
│   │        └─ _isFirstApply = false                 ← 首次 Apply 完成
│   │
│   ├─ CompleteStage("InitializationCompleted", true) ← ← Ⅲ
│   │
│   └─ _initTcs.TrySetResult(ExecutionResult)         ← 聚合结果
│        ├─ IsSuccess = true/false
│        ├─ TotalDurationMs
│        └─ Data["hasExecuted"] = true
│           ["isFirstApply"] = false
│
├─ [Shown] await mainForm.InitializationTask           ← 等待初始化完成
│   │
│   ├─ !IsSuccess → 跳过脚本
│   ├─ IsSuccess → 执行脚本
│   │
│   └─ guiContext.ScriptExecutionResult               ← 脚本执行结果
│        ├─ IsSuccess
│        └─ Data["totalCommands"] / ["succeededCount"] / ["failedCount"]
│
└─ 返回 BlockResult.Ok/Fail                           ← Block 层结果
```

---

## 八、运行成功判定路径

```
用户操作 → CLI
  └─ ApplicationRunBlock
       ├─ [同步] Load 事件触发 → loadStarted.Set()                     ← 消息循环启动成功
       ├─ [异步] OnLoadAsync 执行
       │    ├─ CompleteStage("MainLayoutLoaded", true)                ← 第①步成功
       │    ├─ CompleteStage("ConfigSelectorLoaded", true)            ← 第②步成功
       │    ├─ ExecuteRenderStage()                                   ← 完整管线
       │    │    ├─ CsgEntryPoint.Collect()                           ← ECS→CanonicalForm
       │    │    └─ WinFormsOutputPort.Apply()                        ← →WinForms控件
       │    │         └─ appliedNodeCount > 0 → 实际有控件被创建
       │    ├─ CompleteStage("InitializationCompleted", true)         ← 第③步成功
       │    └─ initTcs.TrySetResult(Success=true)                     ← 初始化聚合结果
       ├─ [Shown] await initTask → initResult.IsSuccess               ← 等待初始化
       ├─ ExecuteScriptAsync(mainForm)                                ← 执行脚本
       └─ guiContext.ScriptExecutionResult.IsSuccess                  ← 脚本执行结果

最直接的运行成功标志：MainForm.InitializationTask → IsSuccess = true
```

---

## 九、相关文件引用

| 文件 | 路径 |
|------|------|
| 通用执行状态类 | `CsharpPrj/Modules/Threading/ExecutionStatus.cs` |
| MainForm 初始化 | `FileDriven/FileDriven.Execution/Initial/MainForm.cs` |
| 启动上下文 | `FileDriven/FileDriven.Execution/Gui/Blocks/StartGuiModeContext.cs` |
| 消息循环 Block | `FileDriven/FileDriven.Execution/Gui/Blocks/ApplicationRunBlock.cs` |
| GUI 编排器 | `FileDriven/FileDriven.Execution/Gui/FileDrivenGuiInitializerOrchestrator.cs` |
| ECS 渲染管线 | `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/EcsRenderPipeline.cs` |
| WinForms 输出端口 | `CsharpPrj/Operation/FrontEndDriven/Ports.cs` |
| 阶段协调器接口 | `CsharpPrj/Plate/TConfig/TrConfig/Services/IStageCoordinator.cs` |
| 主布局配置 | `FileDriven/DrivenConfig/JsonConfig/MainLayout.json` |