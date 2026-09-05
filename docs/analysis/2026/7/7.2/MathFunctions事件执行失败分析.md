# MathFunctions 事件执行失败分析

## 1. 问题现象

```
[ERROR] [FileDriven] UI ThreadException: [EventBinder] 严重错误: 
事件执行服务未注入，无法执行函数 'Add'。
控件 'addButton' 的 'click' 事件已被绑定但无法响应。
```

修复后：UI 无响应（冻结），无报错。

## 2. 架构链路

### 2.1 初始化流程

```
FrameworkInitializer.Initialize()
  │
  ├─ new DefaultEventBindingService()          ← 无 stageCoordinator
  │   └─ new LazyEventExecutionService(null)   ← _coordinator = null
  │
  └─ FileDrivenWdUIBridge.InjectEventBinder()  ← 注入到渲染管线
```

```
FileDrivenGuiInitializerOrchestrator.CreateAndConfigureAsync()
  │
  ├─ CreateStageCoordinatorAsync()             ← 创建 StringStageCoordinator
  ├─ RegisterStagesAsync()                     ← 注册 4 个阶段
  ├─ CreateContextAsync()                      ← 创建 MainForm
  ├─ InjectDependenciesAsync()                 ← 注入 StageCoordinator 到 MainForm
  └─ StartStageCoordinatorAsync()              ← 启动阶段编排
```

```
MainForm 加载流程（UI 线程）：
  │
  ├─ OnRootContainerLoaded()
  │   ├─ StageCoordinator?.CompleteStage("MainLayoutLoaded", true)
  │   └─ LoadConfigSelectorAsync()
  │
  ├─ OnConfigSelectorLoaded()
  │   ├─ StageCoordinator?.CompleteStage("ConfigSelectorLoaded", true)
  │   ├─ StageCoordinator?.CompleteStage("InitializationCompleted", true)
  │   └─ LoadSideContainerAsync("MathFunctions.json")  ← 用户选择后触发
  │
  └─ LoadSideContainerAsync()
      ├─ PreprocessConfig()                    ← 模板处理
      ├─ CreateLayoutPanelSkeleton()           ← 渲染 UI
      ├─ EventBindingPipeline.BindEvents()     ← 绑定事件
      │   └─ EventBinderTransformer
      │       └─ CreateEventHandler()          ← 创建闭包 handler
      │           └─ 捕获 eventExecutionService (LazyEventExecutionService)
      └─ 用户点击按钮 → handler 触发
```

```
FileDrivenGuiInitializerOrchestrator.ExecuteScriptAsync()
  │
  ├─ WaitForPrerequisiteStagesAsync()          ← 等待 3 个前置阶段
  ├─ InjectEventExecutionServiceAsync()        ← 注入 EventExecutionService
  │   ├─ SetStageCoordinator(StageCoordinator) ← 注入到 LazyEventExecutionService
  │   │   └─ _lazyExecutionService.SetCoordinator()
  │   │       ├─ 注册 "EventExecutionServiceReady" 阶段
  │   │       └─ 如果 _isReady 已为 true，补发 CompleteStage
  │   │
  │   └─ base.InjectEventExecutionServiceAsync()
  │       ├─ CreateEventExecutionServiceAsync()
  │       ├─ CreateWinFormsEventBinderAsync()
  │       └─ eventBindingService.SetEventExecutionService()
  │           └─ _lazyExecutionService.SetInner(inner)
  │               ├─ _isReady = true
  │               └─ _coordinator?.CompleteStage("EventExecutionServiceReady", true)
  │
  └─ ExecuteScriptInternalAsync()              ← 执行命令脚本
```

### 2.2 事件触发流程

```
用户点击 "Add" 按钮
  │
  └─ handler(sender, args)                     ← EventBinderTransformer 创建的闭包
      │
      ├─ eventExecutionService?.ExecuteFunctionAsync("Add", ...)
      │   └─ LazyEventExecutionService.ExecuteFunctionAsync()
      │       ├─ EnsureReadyAsync()
      │       │   ├─ _isReady == true → 快速路径返回
      │       │   └─ _isReady == false → await _coordinator.WaitForStageAsync()
      │       │
      │       └─ await _inner!.ExecuteFunctionAsync("Add", ...)
      │
      └─ .GetAwaiter().GetResult()             ← ★ 同步阻塞 UI 线程！
```

## 3. 根因分析

### 3.1 原始错误（已修复）

原始错误 "事件执行服务未注入" 是因为：
1. `FrameworkInitializer` 创建 `DefaultEventBindingService` 时没有传入 `IEventExecutionService`
2. `EventBinderTransformer` 创建 handler 时 `_eventExecutionService` 为 null
3. handler 闭包捕获 null，触发时调用 `null?.ExecuteFunctionAsync(...)` 返回 null

**修复方案：** `LazyEventExecutionService` 包装器 + `SetCoordinator`/`SetInner` 延迟注入机制。

### 3.2 当前问题：UI 死锁

修复后 UI 无响应（冻结），原因是 **UI 线程异步死锁**：

```
UI 线程调用 handler
  → eventExecutionService?.ExecuteFunctionAsync("Add", ...)
  → LazyEventExecutionService.ExecuteFunctionAsync()
  → EnsureReadyAsync()
  → await _coordinator.WaitForStageAsync("EventExecutionServiceReady")
  → await signalTask.WaitAsync(cts.Token)
  → 需要回到 UI 线程继续 await 后续代码
  → 但 UI 线程被 .GetAwaiter().GetResult() 阻塞
  → 死锁！
```

**关键代码行**（`EventBindingTransformers.cs` 第 465 行）：
```csharp
var execResult = eventExecutionService?.ExecuteFunctionAsync(functionName, new object?[] { value }).GetAwaiter().GetResult();
```

`.GetAwaiter().GetResult()` 在 UI 线程上同步阻塞等待异步操作完成，而异步操作需要回到 UI 线程继续执行，造成死锁。

### 3.3 时序竞态问题

即使修复了死锁，还存在时序问题：

1. `LoadSideContainerAsync("MathFunctions.json")` 在 `ConfigSelector` 加载后立即触发
2. 此时 `ExecuteScriptAsync` 尚未执行，`EventExecutionService` 尚未注入
3. handler 闭包捕获的 `LazyEventExecutionService` 中 `_isReady == false`
4. 用户点击按钮时，`EnsureReadyAsync` 需要等待 `"EventExecutionServiceReady"` 阶段完成
5. 但 `CompleteStage("EventExecutionServiceReady", true)` 在 `SetInner` 中调用
6. `SetInner` 在 `InjectEventExecutionServiceAsync` 中调用
7. `InjectEventExecutionServiceAsync` 在 `ExecuteScriptAsync` 中调用
8. 如果用户点击按钮时 `ExecuteScriptAsync` 尚未执行到注入步骤，则等待

## 4. 修复方案

### 方案 A：消除 UI 线程死锁

将 `EventBinderTransformer.CreateEventHandler` 中的 `.GetAwaiter().GetResult()` 改为异步调用。

但 handler 签名是 `EventHandler`（`void` 返回），不能直接改为 `async`。

**方案 A1：使用 `async void`**
```csharp
return async (sender, args) =>
{
    // ...
    var execResult = await eventExecutionService.ExecuteFunctionAsync(functionName, new object?[] { value });
    // ...
};
```

**方案 A2：使用 `Control.BeginInvoke` 在后台线程执行**
```csharp
return (sender, args) =>
{
    // ...
    Task.Run(async () =>
    {
        var execResult = await eventExecutionService.ExecuteFunctionAsync(functionName, new object?[] { value });
        // 如果需要更新 UI，使用 Control.BeginInvoke
    });
    // ...
};
```

### 方案 B：确保注入在事件触发前完成

调整初始化时序，确保 `EventExecutionService` 在 `LoadSideContainerAsync` 之前注入。

### 方案 C：简化方案 — 直接使用 `async void`

由于 WinForms 事件 handler 支持 `async void`，最简单的修复是将 handler 改为 `async void`：

```csharp
return async (sender, args) =>
{
    // ...
    try
    {
        var value = GetControlValueStatic(ctrl, valueAccessor);
        var execResult = await eventExecutionService.ExecuteFunctionAsync(functionName, new object?[] { value });
        var success = execResult != null;
        ctx?.Log($"[EventBinder] 执行结果: {(success ? "成功" : "失败")} ...");
    }
    catch (Exception ex)
    {
        ctx?.Log($"[EventBinder] 执行异常: {ex.Message} ...");
    }
};
```

## 5. 相关文件

| 文件 | 作用 |
|------|------|
| `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json` | 数学函数 UI 配置 |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | EventBinderTransformer，创建 handler |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/EventBindingPipeline.cs` | 事件绑定管线 |
| `CsharpPrj/Operation/EventTriggerFramework/DefaultEventBindingService.cs` | 默认事件绑定服务 |
| `CsharpPrj/Share/EventBindingOrchestrator/Services/LazyEventExecutionService.cs` | 延迟执行服务包装器 |
| `CsharpPrj/Share/EventBindingOrchestrator/Init/StringStageCoordinator.cs` | 阶段协调器 |
| `CsharpPrj/BasicMethod/Common/StepCoordination/AsyncStepCoordinator.cs` | 异步步骤协调器 |
| `FileDriven/FileDriven.Execution/Gui/FileDrivenGuiInitializerOrchestrator.cs` | GUI 初始化编排器 |
| `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` | 框架初始化器 |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | 主窗体 |
