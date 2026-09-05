# FileDriven.Execution 代码整理与日志完善方案

> 文档日期：2026-06-30
> 分析范围：`CsharpPrj/FileDriven/FileDriven.Execution/` 目录下所有源文件 + 相关外部依赖
> 文档目的：记录所有已发现问题的修复方案，以及事件触发日志的完善设计
> **核心原则：以 JSON 配置驱动的事件 UI 为主，所有修复围绕确保 JSON 驱动链路正确工作**

---

## 目录

1. [概述](#一概述)
2. [P0 级修复方案（必须立即修复）](#二p0-级修复方案必须立即修复)
3. [P1 级修复方案（建议近期修复）](#三p1-级修复方案建议近期修复)
4. [P2 级修复方案（迭代中修复）](#四p2-级修复方案迭代中修复)
5. [P3 级修复方案（低优先级）](#五p3-级修复方案低优先级)
6. [日志框架完善方案](#六日志框架完善方案)
7. [修复实施指南](#七修复实施指南)

---

## 一、概述

### 1.1 问题总览

| 类别 | 数量 | 严重程度 |
|------|------|---------|
| 逻辑错误 (Bug) | 10 项 | P0×2, P1×4, P2×3, P3×1 |
| 冗余逻辑 (Redundant) | 8 项 | P2×7, P3×1 |
| 重复逻辑 (Duplicate) | 5 项 | P2×5 |
| GUI 按钮无日志根因 | 5 个潜在原因 | P0×1 |

### 1.2 事件绑定链路全景（JSON 驱动）

```
FrameworkInitializer
  └─ 创建 DefaultEventBindingService (WinFormsEventBinder=null)
      └─ 注入到 FileDrivenWdUIBridge
          └─ 渲染管线 WinFormsRenderPipeline
              └─ ExecuteBindEventsStage()
                  └─ _eventBinder.BindEventsAsync(form, config)
                      └─ DefaultEventBindingService.BindEvents()
                          └─ BindControlsRecursive()
                              └─ if (_winFormsEventBinder != null)  ← 此处可能为 null
                                  └─ _winFormsEventBinder.BindControlEvents()
                                      └─ 绑定 WinForms 原生事件
```

### 1.3 按钮点击无日志的 5 个潜在原因

| # | 原因 | 触发条件 | 影响 |
|---|------|---------|------|
| ① | **WinFormsEventBinder 注入时序问题** | 延迟注入发生在 BindEvents 之后 | 所有 JSON 事件绑定被跳过 |
| ② | **EventBindingResolver._loadedConfigs 为空** | sideload 配置未注册到 PipelineEntry | 事件绑定信息找不到 |
| ③ | **EventTriggerService 依赖 Form.ActiveForm** | 无激活窗口/焦点在对话框 | FindControl 返回 null |
| ④ | **IsScriptExecutionMode 误设为 true** | 管线执行 set 命令时用户点击 | 运行时事件被跳过 |
| ⑤ | **executeButton 代码驱动绑定被注释** | MainForm.cs:367-369 被注释 | 无安全网回退（但不应恢复） |

> **修复方向**：以 JSON 驱动为主，修复 ①②③④ 确保 JSON 链路正确，⑤ 仅添加诊断日志不恢复代码绑定。

---

## 二、P0 级修复方案（必须立即修复）

### B1. EventBindingResolver 线程安全

**问题**：`GetSnapshot()` 在 lock 内枚举 `_loadedConfigs`，但 `_loadedConfigs` 是外部传入的引用，外部可能在 lock 外修改。

**修复方案**：改用 `ConcurrentDictionary` 替代手动 lock。

```csharp
// EventBindingResolver.cs
private readonly ConcurrentDictionary<string, object> _loadedConfigs;

public EventBindingResolver(IDictionary<string, object> loadedConfigs, object? lockObj = null)
{
    // ★ 修复：创建副本，外部修改不影响内部枚举
    _loadedConfigs = new ConcurrentDictionary<string, object>(
        loadedConfigs ?? new Dictionary<string, object>(),
        StringComparer.OrdinalIgnoreCase);
}

// GetSnapshot 不再需要 lock
private List<KeyValuePair<string, LoadedConfigInfo>> GetSnapshot()
{
    var result = new List<KeyValuePair<string, LoadedConfigInfo>>();
    foreach (var kvp in _loadedConfigs)
    {
        if (kvp.Value is LoadedConfigInfo info)
            result.Add(new KeyValuePair<string, LoadedConfigInfo>(kvp.Key, info));
    }
    return result;
}
```

**涉及文件**：`EventPipeline/EventBindingResolver.cs`

---

### B5. ExecutionCenter handler 异常处理缺陷

**问题**：catch 块中 `((FileDrivenPipelineMessage)msg)` 在 msg 类型不匹配时会抛出 `InvalidCastException`，覆盖原始异常信息。

**修复方案**：使用 `as` 转换 + null 检查。

```csharp
// FrameworkInitializer.cs - RegisterExecutionCenterHandler
catch (Exception ex)
{
    var pipelineMsg = msg as FileDrivenPipelineMessage;
    return Task.FromResult<ITaskResponse>(
        new ExecutionCenter.Messages.TaskResponse(
            requestMessageId: pipelineMsg?.MessageId ?? Guid.Empty,
            success: false,
            error: $"FileDrivenPipeline handler error: {ex.Message}",
            result: null));
}
```

**涉及文件**：`Lifecycle/FrameworkInitializer.cs`

---

### 按钮无日志修复（JSON 驱动链路修复 + 诊断日志）

**问题**：executeButton 的 Click 事件在 JSON 配置驱动绑定链路中因 WinFormsEventBinder 注入时序问题而失败。

**修复方案**：**不恢复代码驱动绑定**，而是修复 JSON 驱动链路并添加诊断日志。

#### 修复 ①：确保 WinFormsEventBinder 在 BindEvents 前完成注入

```csharp
// FrameworkInitializer.cs - 调整注入时序
public async Task InitializeAsync()
{
    // 1. 创建 DefaultEventBindingService（此时 WinFormsEventBinder 为 null）
    var eventBindingService = new DefaultEventBindingService();
    FileDrivenWdUIBridge.InjectEventBinder(eventBindingService);

    // 2. ★ 修复：在 BindEvents 之前确保 WinFormsEventBinder 已注入
    //    如果 GuiModeRunner 尚未创建 WinFormsEventBinder，则在此处创建并注入
    if (eventBindingService.WinFormsEventBinder == null)
    {
        var winFormsBinder = new WinFormsEventBinder(/* 依赖注入 */);
        eventBindingService.InjectWinFormsEventBinder(winFormsBinder);
        
        FileDrivenLogPlaceholderHandler.Log("FrameworkInitializer_InjectWinFormsBinder",
            new { method = "early_injection", timestamp = DateTime.UtcNow });
    }

    // 3. 继续其他初始化...
}
```

#### 修复 ②：在 DefaultEventBindingService 中添加跳过诊断日志

```csharp
// DefaultEventBindingService.cs - BindControlsRecursive()
if (controlConfig.Events != null)
{
    if (_winFormsEventBinder != null)
    {
        FileDrivenLogPlaceholderHandler.Log("EventBinding_BindControl",
            new
            {
                controlName = controlConfig.Name,
                eventTypes = string.Join(",", controlConfig.Events.Keys),
                eventCount = controlConfig.Events.Count
            });
        _winFormsEventBinder.BindControlEvents(control, controlConfig.Events);
        _boundCount++;
    }
    else
    {
        // ★ 新增诊断：记录因 WinFormsEventBinder 为 null 跳过的事件绑定
        FileDrivenLogPlaceholderHandler.Log("EventBinding_SkipControl",
            new
            {
                controlName = controlConfig.Name,
                reason = "WinFormsEventBinder is null",
                eventCount = controlConfig.Events.Count,
                eventTypes = string.Join(",", controlConfig.Events.Keys),
                suggestion = "Check FrameworkInitializer injection timing"
            });
        _skippedCount++;
    }
}
```

#### 修复 ③：在 MainForm 中添加 JSON 驱动绑定验证日志

```csharp
// MainForm.cs - BindCommandEvents()
private void BindCommandEvents()
{
    WinFormsEventRegistrar.RegisterKeyPressDirect(_commandTextBox, CommandTextBox_KeyPress);

    // ★ 注意：executeButton 的 Click 事件由 MainLayout.json 的 events 配置驱动
    //   DefaultEventBindingService 在渲染管线的 BindEvents 阶段负责绑定
    //   此处不再重复注册代码驱动绑定，以保持 JSON 配置驱动的单一职责

    // ★ 新增诊断：验证 JSON 驱动的事件绑定是否成功
    var executeBtn = WinFormsEventRegistrar.FindControl(_rootContainer, "executeButton");
    if (executeBtn != null)
    {
        var clickDelegate = executeBtn.Click;
        FileDrivenLogPlaceholderHandler.Log("MainForm_VerifyEventBinding",
            new
            {
                buttonName = "executeButton",
                hasClickHandler = clickDelegate != null,
                bindingSource = "json_config_driven",
                note = clickDelegate == null
                    ? "Click handler not bound - check WinFormsEventBinder injection timing"
                    : "Click handler bound successfully via JSON config"
            });
    }
}
```

**涉及文件**：
- `Lifecycle/FrameworkInitializer.cs`
- `Operation/EventTriggerFramework/DefaultEventBindingService.cs`
- `FileDriven.UI/Core/MainForm.cs`

---

## 三、P1 级修复方案（建议近期修复）

### B2. Application.OpenForms[0] 窗口假设

**问题**：`EventPipelineExecutor` 假设第一个打开的窗口就是目标窗口。

**修复方案**：通过构造函数或方法参数传递明确的 `TargetForm` 引用。

```csharp
// EventPipelineExecutor.cs
public class EventPipelineExecutor
{
    private readonly Form? _targetForm;

    /// <summary>
    /// ★ 修复：通过构造函数注入明确的 TargetForm 引用
    /// </summary>
    public EventPipelineExecutor(Form? targetForm = null)
    {
        _targetForm = targetForm;
    }

    private Form? ResolveTargetForm()
    {
        // ★ 修复：优先使用注入的 TargetForm
        if (_targetForm != null && !_targetForm.IsDisposed)
            return _targetForm;

        // 回退到 Application.OpenForms（兼容旧调用方）
        if (Application.OpenForms.Count > 0)
            return Application.OpenForms[0];

        return null;
    }
}
```

**涉及文件**：`EventPipeline/EventPipelineExecutor.cs`

---

### B3. IsScriptExecutionMode 多实例问题

**问题**：`WinFormsEventBinder.IsScriptExecutionMode` 是实例属性，多个实例间状态不同步。

**修复方案**：使用静态属性或全局状态管理。

```csharp
// WinFormsEventBinder.cs
public class WinFormsEventBinder : IWinFormsEventBinder
{
    // ★ 修复：改为静态属性，确保所有实例共享同一状态
    private static bool _isScriptExecutionMode;

    /// <summary>
    /// 脚本执行模式标记（全局静态）
    /// <para>当管线正在执行 set 命令时，设置为 true，跳过运行时事件触发</para>
    /// </summary>
    public bool IsScriptExecutionMode
    {
        get => _isScriptExecutionMode;
        set => _isScriptExecutionMode = value;
    }
}
```

**涉及文件**：`EventPipeline/WinFormsEventBinder.cs`

---

### B4. FileDrivenRuntimeContext 数据不一致

**问题**：`GetService` 先查 `_contextFrame` 再查 `_items`，但 `_items` 中的数据可能是过期的。

**修复方案**：统一使用 `_contextFrame` 作为唯一数据源，移除 `_items` 字典。

```csharp
// FileDrivenRuntimeContext.cs
public class FileDrivenRuntimeContext
{
    private readonly IContext _contextFrame;

    public FileDrivenRuntimeContext(IContext contextFrame)
    {
        _contextFrame = contextFrame ?? throw new ArgumentNullException(nameof(contextFrame));
        // ★ 修复：不再从 _contextFrame 复制到 _items
        //   所有数据统一通过 _contextFrame 获取
    }

    public object? GetService(Type serviceType)
    {
        // ★ 修复：统一使用 _contextFrame 作为唯一数据源
        return _contextFrame.GetData(serviceType);
    }

    public void SetService(Type serviceType, object? value)
    {
        // ★ 修复：统一写入 _contextFrame
        _contextFrame.SetData(serviceType, value);
    }
}
```

**涉及文件**：`Context/FileDrivenRuntimeContext.cs`

---

### B6. EventTriggerService 依赖 Form.ActiveForm

**问题**：使用 `Form.ActiveForm` 查找控件，CLI 模式下为 null。

**修复方案**：通过构造函数注入 `Form` 引用，或使用 `Application.OpenForms` 并允许外部指定目标窗口。

```csharp
// EventTriggerService.cs
public class EventTriggerService : IEventTriggerService
{
    private readonly Form? _targetForm;

    /// <summary>
    /// ★ 修复：通过构造函数注入 Form 引用
    /// </summary>
    public EventTriggerService(Form? targetForm = null)
    {
        _targetForm = targetForm;
    }

    private Control? FindControl(string controlName)
    {
        // ★ 修复：优先使用注入的 Form 引用
        var form = _targetForm ?? Form.ActiveForm;
        if (form == null)
        {
            // ★ 新增诊断：记录查找失败原因
            FileDrivenLogPlaceholderHandler.Log("EventTriggerService_FindControl_noForm",
                new { controlName, reason = "No target form available" });
            return null;
        }

        var found = form.Controls.Find(controlName, true);
        return found.Length > 0 ? found[0] : null;
    }
}
```

**涉及文件**：`EventPipeline/EventTriggerService.cs`

---

### B8. RunPipelineAsync 线程模型死锁风险

**问题**：内部使用 `Task.Run + GetAwaiter().GetResult()` 同步阻塞等待异步操作。

**修复方案**：确保所有异步路径都使用 `await` 而非 `.GetAwaiter().GetResult()`。

```csharp
// ExecutionPipelineEntry.cs
public async Task<FileDrivenPipelineContext> RunPipelineAsync(FileDrivenPipelineContext context)
{
    try
    {
        // ★ 修复：使用 await 替代 GetAwaiter().GetResult()
        var result = await Task.Run(() => ExecutePipelineInternal(context))
            .ConfigureAwait(false);
        return result;
    }
    catch (Exception ex)
    {
        context.HasError = true;
        context.ErrorMessage = ex.Message;
        FileDrivenLogPlaceholderHandler.Log("ExecutionPipelineEntry_RunPipelineAsync_error",
            new { error = ex.Message });
        return context;
    }
}
```

**涉及文件**：`Pipeline/ExecutionPipelineEntry.cs`

---

## 四、P2 级修复方案（迭代中修复）

### R1/R5. 统一 ExecutionResult 构建路径

**问题**：ExecutionCenter handler 手动构造 `ExecutionResult`，没有复用 `ToBackEndExecutionResult()`。

**修复方案**：ExecutionCenter handler 复用 `ToBackEndExecutionResult()`。

```csharp
// FrameworkInitializer.cs - RegisterExecutionCenterHandler
// ★ 修复：复用 ToBackEndExecutionResult 而非手动构造
var pipelineEntry = serviceProvider.GetRequiredService<ExecutionPipelineEntry>();
var backEndResult = pipelineEntry.ToBackEndExecutionResult(pipelineContext);
return Task.FromResult<ITaskResponse>(
    new ExecutionCenter.Messages.TaskResponse(
        requestMessageId: msg.MessageId,
        success: backEndResult.IsSuccess,
        error: backEndResult.ErrorMessage,
        result: backEndResult.Result));
```

**涉及文件**：
- `Lifecycle/FrameworkInitializer.cs`
- `Pipeline/ExecutionPipelineEntry.cs`

---

### R2. 统一事件类型映射

**问题**：事件类型映射在 4 个地方重复实现。

**修复方案**：统一使用 `EventTriggerFramework.EventTypeMapper`，删除 `EventTypeMapper.cs`。

```csharp
// ★ 删除 EventPipeline/EventTypeMapper.cs
// 所有调用方改为直接使用 EventTriggerFramework.EventTypeMapper.Default

// EventBindingResolver.cs - ResolveEventType()
// ★ 修复：委托给 EventTriggerFramework.EventTypeMapper
public string ResolveEventType(string controlName, string operationType)
{
    return EventTriggerFramework.EventTypeMapper.Default.ToCommandAction(operationType)
           ?? operationType;
}

// WinFormsEventBinder.cs - GetEventFunctionName()
// ★ 修复：委托给 EventTriggerFramework.EventTypeMapper
private string GetEventFunctionName(string eventType)
{
    return EventTriggerFramework.EventTypeMapper.Default.ToCommandAction(eventType)
           ?? eventType;
}
```

**涉及文件**：
- `EventPipeline/EventTypeMapper.cs`（删除）
- `EventPipeline/EventBindingResolver.cs`
- `EventPipeline/WinFormsEventBinder.cs`

---

### R3. 统一命令执行路径

**问题**：`ExecuteCommandStage` 没有复用 `FileDrivenExecutionEngine.ExecuteFileDrivenCommandAsync()`。

**修复方案**：`ExecuteCommandStage` 复用 `FileDrivenExecutionEngine`。

```csharp
// ExecuteCommandStage.cs
public class ExecuteCommandStage : IPipelineStage
{
    private readonly FileDrivenExecutionEngine _engine;

    public ExecuteCommandStage(FileDrivenExecutionEngine engine)
    {
        _engine = engine;
    }

    public async Task ProcessAsync(FileDrivenPipelineContext context)
    {
        foreach (var command in context.Commands)
        {
            // ★ 修复：复用 FileDrivenExecutionEngine 的统一入口
            var result = await _engine.ExecuteFileDrivenCommandAsync(command, context);
            context.Results.Add(result);
        }
    }
}
```

**涉及文件**：`Pipeline/ExecuteCommandStage.cs`（如存在）

---

### R4. 统一上下文创建

**问题**：`CreateContext()` 和 `CreateMinimalContext()` 部分字段重复设置。

**修复方案**：提取公共初始化方法。

```csharp
// FileDrivenPipelineRunner.cs
private FileDrivenPipelineContext CreateContext(/* params */)
{
    var context = new FileDrivenPipelineContext();
    InitializeCommonContext(context);
    // ★ 提取：CreateContext 特有的初始化
    context.IsFullMode = true;
    return context;
}

private FileDrivenPipelineContext CreateMinimalContext(/* params */)
{
    var context = new FileDrivenPipelineContext();
    InitializeCommonContext(context);
    // ★ 提取：CreateMinimalContext 特有的初始化
    context.IsFullMode = false;
    return context;
}

private void InitializeCommonContext(FileDrivenPipelineContext context)
{
    // ★ 统一：所有公共字段在此初始化
    context.CreatedAt = DateTime.UtcNow;
    context.HasError = false;
    // ... 其他公共字段
}
```

**涉及文件**：`Pipeline/FileDrivenPipelineRunner.cs`

---

### Y1. 删除 EventTypeMapper.cs

**问题**：纯包装类，没有添加任何额外逻辑。

**修复方案**：删除文件，所有调用方直接使用 `EventTriggerFramework.EventTypeMapper`。

**涉及文件**：`EventPipeline/EventTypeMapper.cs`

---

### Y2. 删除 IEventPipelineExecutor 接口重复定义

**问题**：注释说"从 `Modules.Pipeline.CentralPipeline.IEventPipelineExecutor` 复制"。

**修复方案**：删除此文件，直接引用原接口。

**涉及文件**：`EventPipeline/IEventPipelineExecutor.cs`

---

### Y3. 确认并删除未使用的 WinFormsEventRegistrar

**问题**：提供了 6 个事件注册方法，但在当前读取的文件中没有任何地方引用。

**修复方案**：确认是否被外部引用，如无引用则删除。

> **注意**：`WinFormsEventRegistrar` 实际上被 `UIEventBinder` 和 `MainForm` 使用，不能删除。但 `EventPipeline/WinFormsEventRegistrar.cs`（如果存在）可能是死代码。

**涉及文件**：`EventPipeline/WinFormsEventRegistrar.cs`（如存在且未被引用）

---

### Y4. 删除 ClickBindingInfo.cs

**问题**：简单的 DTO 类，但 `EventBindingInfo.cs` 已经提供了类似功能。

**修复方案**：确认是否被使用，如无使用则删除。

**涉及文件**：`EventPipeline/Models/ClickBindingInfo.cs`

---

### Y5. 统一 FileDrivenRuntimeContext 数据源

**问题**：既通过 `_contextFrame` 获取数据，又维护自己的 `_items` 字典。

**修复方案**：统一使用 `_contextFrame` 作为唯一数据源，移除 `_items` 字典。（同 B4 修复）

**涉及文件**：`Context/FileDrivenRuntimeContext.cs`

---

### Y6. 简化 HandlerReentrancyGuard

**问题**：`_exited` 标志与 `GuardScope.Dispose()` 的 `_disposed` 标志功能重叠。

**修复方案**：如果外部不会手动调用 `Exit()`，可以移除 `_exited` 标志。

```csharp
// HandlerReentrancyGuard.cs
public void Exit()
{
    // ★ 修复：移除 _exited 标志，由 GuardScope.Dispose 的 _disposed 提供保护
    _isExecuting.Value = false;
}
```

**涉及文件**：`Context/HandlerReentrancyGuard.cs`

---

### Y7. ScriptLoadSkeleton 复用路径解析服务

**问题**：手动实现文件路径解析，但 `ExecutionHelper.ResolveCommandFilePath()` 已提供。

**修复方案**：复用 `ExecutionHelper.ResolveCommandFilePath()`。

```csharp
// ScriptLoadSkeleton.cs
private string ResolveScriptPath(string relativePath)
{
    // ★ 修复：复用 ExecutionHelper 的路径解析服务
    return ExecutionHelper.ResolveCommandFilePath(relativePath)
           ?? Path.Combine(ProjectRoot, relativePath);
}
```

**涉及文件**：`Pipeline/ScriptLoadSkeleton.cs`

---

### Y8. 精简日志记录

**问题**：几乎所有方法的入口和出口都有日志调用，有些方法有 5-6 处。

**修复方案**：保留关键路径的入口/出口日志，移除中间步骤的冗余日志。

| 方法 | 当前日志数 | 建议保留 |
|------|-----------|---------|
| `ExecuteCommandStage.Process()` | 3 处 | 1 处（入口） |
| `ExecutionPipelineEntry.RunPipelineAsync()` | 5 处 | 2 处（入口+出口） |
| `FileDrivenGuiInitializer.ExecuteScriptAsync()` | 10+ 处 | 3 处（入口+关键步骤+出口） |

---

## 五、P3 级修复方案（低优先级）

### B10. FindControlByName 性能优化

**问题**：每次调用都创建完整的快照，然后遍历所有配置。

**修复方案**：考虑缓存控件名称到配置的映射。

```csharp
// EventBindingResolver.cs
private readonly Dictionary<string, string> _controlNameToConfigKey = new(StringComparer.OrdinalIgnoreCase);

// 在首次遍历时建立索引
private void BuildControlIndex()
{
    foreach (var kvp in _loadedConfigs)
    {
        if (kvp.Value is LoadedConfigInfo info)
        {
            IndexControlsInConfig(info.Config, kvp.Key);
        }
    }
}

private object? FindControlByName(string controlName)
{
    // ★ 修复：使用索引直接定位，避免遍历所有配置
    if (_controlNameToConfigKey.TryGetValue(controlName, out var configKey))
    {
        if (_loadedConfigs.TryGetValue(configKey, out var configObj) && configObj is LoadedConfigInfo info)
        {
            return FindControlInConfig(info.Config, controlName);
        }
    }
    return null;
}
```

**涉及文件**：`EventPipeline/EventBindingResolver.cs`

---

## 六、日志框架完善方案

### 6.1 现有日志框架分析

当前日志通过 `FileDrivenLogPlaceholderHandler.Log` 实现，这是一个基于 Placeholder 框架的日志处理器。日志调用遍布整个代码库，但**事件触发链路的日志严重不足**，导致无法定位按钮点击无日志的根因。

### 6.2 事件触发全链路日志设计

日志完善的核心目标是：**让 JSON 驱动的事件绑定链路完全可见**，从绑定到触发的每一步都有日志可查。

#### 6.2.1 事件绑定阶段日志（DefaultEventBindingService）

| 日志点 | 位置 | 日志内容 | 级别 |
|--------|------|---------|------|
| 开始绑定 | `BindEvents()` 入口 | `{formName}, {viewCount} 个视图` | Info |
| 绑定控件 | `BindControlsRecursive()` 循环 | `{controlName}, {eventCount} 个事件` | Info |
| 跳过绑定 | `_winFormsEventBinder == null` | `{controlName}, 原因: WinFormsEventBinder 为 null` | **Warning** |
| 绑定完成 | `BindEvents()` 出口 | `{totalBound} 个控件绑定成功, {totalSkipped} 个跳过` | Info |

```csharp
// DefaultEventBindingService.cs - 新增日志
public void BindEvents(Form form, UIConfiguration config)
{
    FileDrivenLogPlaceholderHandler.Log("EventBinding_Start",
        new { formName = form.Name, viewCount = config.Views?.Count ?? 0 });

    _boundCount = 0;
    _skippedCount = 0;
    BindControlsRecursive(form, config.Views?.SelectMany(v => v.Controls)?.ToList() ?? new());

    FileDrivenLogPlaceholderHandler.Log("EventBinding_Complete",
        new { totalBound = _boundCount, totalSkipped = _skippedCount });
}

private void BindControlsRecursive(Form form, List<ControlConfig> controls)
{
    foreach (var controlConfig in controls)
    {
        if (controlConfig.Events != null && _winFormsEventBinder != null)
        {
            FileDrivenLogPlaceholderHandler.Log("EventBinding_BindControl",
                new
                {
                    controlName = controlConfig.Name,
                    eventTypes = string.Join(",", controlConfig.Events.Keys),
                    eventCount = controlConfig.Events.Count
                });
            _winFormsEventBinder.BindControlEvents(control, controlConfig.Events);
            _boundCount++;
        }
        else if (controlConfig.Events != null)
        {
            FileDrivenLogPlaceholderHandler.Log("EventBinding_SkipControl",
                new
                {
                    controlName = controlConfig.Name,
                    reason = "WinFormsEventBinder is null",
                    eventCount = controlConfig.Events.Count,
                    eventTypes = string.Join(",", controlConfig.Events.Keys)
                });
            _skippedCount++;
        }
        // ... 递归处理子控件 ...
    }
}
```

#### 6.2.2 WinFormsEventBinder 绑定日志

| 日志点 | 位置 | 日志内容 | 级别 |
|--------|------|---------|------|
| 开始绑定控件 | 方法入口 | `{controlName}, {controlType}` | Info |
| 绑定单个事件 | 事件循环 | `{controlName}, {eventType}, {functionName}` | Info |
| 跳过事件 | 事件不支持 | `{controlName}, {eventType}, 原因: 不支持的事件类型` | Warning |
| 绑定完成 | 方法出口 | `{controlName}, {boundCount} 个事件绑定成功` | Info |

```csharp
// WinFormsEventBinder.cs
public void BindControlEvents(Control control, EventConfig eventsConfig)
{
    FileDrivenLogPlaceholderHandler.Log("WinFormsBinder_BindStart",
        new { controlName = control.Name, controlType = control.GetType().Name });

    int boundCount = 0;
    foreach (var eventEntry in eventsConfig)
    {
        var eventType = eventEntry.Key;
        var eventValue = eventEntry.Value?.ToString() ?? "";

        if (IsEventSupported(control, eventType))
        {
            BindSingleEvent(control, eventType, eventValue);
            boundCount++;

            FileDrivenLogPlaceholderHandler.Log("WinFormsBinder_BindEvent",
                new { controlName = control.Name, eventType, functionName = eventValue });
        }
        else
        {
            FileDrivenLogPlaceholderHandler.Log("WinFormsBinder_SkipEvent",
                new { controlName = control.Name, eventType, reason = "Unsupported event type" });
        }
    }

    FileDrivenLogPlaceholderHandler.Log("WinFormsBinder_BindComplete",
        new { controlName = control.Name, boundCount });
}
```

#### 6.2.3 事件触发阶段日志（EventTriggerService）

| 日志点 | 位置 | 日志内容 | 级别 |
|--------|------|---------|------|
| 开始触发 | 方法入口 | `{controlName}, {eventType}, {value}` | Info |
| 查找控件 | FindControl | `{controlName}, 找到/未找到` | Info/Warning |
| 查找事件绑定 | FindEventBinding | `{controlName}, {eventType}, 找到/未找到` | Info/Warning |
| 执行函数 | 函数执行前 | `{functionName}, {inputCount} 个输入` | Info |
| 函数执行完成 | 函数执行后 | `{functionName}, 成功/失败, {executionTime}ms` | Info/Error |
| 写入结果 | WriteResult | `{outputTarget}, {value}` | Info |
| 触发完成 | 方法出口 | `{controlName}, {eventType}, 成功/失败` | Info/Error |

```csharp
// EventTriggerService.cs
public async Task<IEventTriggerResult> TriggerEventAsync(string controlName, string eventType, string value)
{
    var sw = Stopwatch.StartNew();

    FileDrivenLogPlaceholderHandler.Log("EventTrigger_Start",
        new { controlName, eventType, value });

    // 查找控件
    var control = FindControl(controlName);
    if (control == null)
    {
        FileDrivenLogPlaceholderHandler.Log("EventTrigger_ControlNotFound",
            new { controlName, eventType });
        return CreateFailedResult(controlName, eventType, $"Control '{controlName}' not found");
    }

    // 查找事件绑定
    var binding = _resolver?.FindEventBinding(controlName, eventType);
    if (binding == null)
    {
        FileDrivenLogPlaceholderHandler.Log("EventTrigger_BindingNotFound",
            new { controlName, eventType });
        return CreateFailedResult(controlName, eventType, $"No binding found for '{controlName}.{eventType}'");
    }

    FileDrivenLogPlaceholderHandler.Log("EventTrigger_ExecuteFunction",
        new
        {
            controlName,
            eventType,
            functionName = binding.FunctionName,
            inputCount = binding.Inputs?.Count ?? 0
        });

    // ... 执行函数逻辑 ...

    sw.Stop();
    FileDrivenLogPlaceholderHandler.Log("EventTrigger_Complete",
        new
        {
            controlName,
            eventType,
            functionName = binding.FunctionName,
            success = result.IsSuccess,
            executionTimeMs = sw.ElapsedMilliseconds
        });

    return result;
}
```

#### 6.2.4 脚本执行模式日志（ScriptExecutionModeGuard）

| 日志点 | 位置 | 日志内容 | 级别 |
|--------|------|---------|------|
| 进入脚本模式 | 构造函数 | `{previousValue} → true` | Info |
| 退出脚本模式 | Dispose | `true → {previousValue}` | Info |

```csharp
// ScriptExecutionModeGuard.cs
public ScriptExecutionModeGuard(IWinFormsEventBinder? binder)
{
    _binder = binder;
    _previousValue = binder?.IsScriptExecutionMode ?? false;

    if (binder != null)
    {
        binder.IsScriptExecutionMode = true;

        FileDrivenLogPlaceholderHandler.Log("ScriptMode_Enter",
            new { previousValue = _previousValue, newValue = true });
    }
}

public void Dispose()
{
    if (_binder != null)
    {
        FileDrivenLogPlaceholderHandler.Log("ScriptMode_Exit",
            new { previousValue = true, restoredValue = _previousValue });

        _binder.IsScriptExecutionMode = _previousValue;
    }
}
```

#### 6.2.5 用户操作日志（WinFormsEventBinder 事件处理器）

| 日志点 | 位置 | 日志内容 | 级别 |
|--------|------|---------|------|
| 用户点击按钮 | Click 处理器 | `{controlName}, 用户点击` | Info |
| 用户输入命令 | KeyPress 处理器 | `{text}, 用户按下回车` | Info |
| 事件被跳过 | IsScriptExecutionMode 检查 | `{controlName}, {eventType}, 原因: 脚本执行模式` | Warning |

```csharp
// WinFormsEventBinder.cs - 事件处理器包装
private void OnControlClick(object? sender, EventArgs e, string functionName)
{
    if (IsScriptExecutionMode)
    {
        FileDrivenLogPlaceholderHandler.Log("UserEvent_Skipped",
            new
            {
                controlName = (sender as Control)?.Name ?? "unknown",
                eventType = "click",
                reason = "ScriptExecutionMode",
                suggestion = "Wait for script execution to complete"
            });
        return;
    }

    FileDrivenLogPlaceholderHandler.Log("UserEvent_Triggered",
        new
        {
            controlName = (sender as Control)?.Name ?? "unknown",
            eventType = "click",
            functionName
        });

    // ... 执行函数 ...
}
```

### 6.3 日志分类与级别

建议使用统一的日志分类前缀，便于过滤和检索：

| 分类前缀 | 用途 | 示例 |
|---------|------|------|
| `EventBinding_` | 事件绑定阶段 | `EventBinding_Start`, `EventBinding_BindControl` |
| `EventTrigger_` | 事件触发阶段 | `EventTrigger_Start`, `EventTrigger_ExecuteFunction` |
| `UserEvent_` | 用户操作事件 | `UserEvent_Triggered`, `UserEvent_Skipped` |
| `ScriptMode_` | 脚本执行模式 | `ScriptMode_Enter`, `ScriptMode_Exit` |
| `WinFormsBinder_` | WinForms 绑定器 | `WinFormsBinder_BindStart`, `WinFormsBinder_BindEvent` |

### 6.4 日志级别建议

| 级别 | 适用场景 |
|------|---------|
| `Info` | 正常的事件绑定和触发流程 |
| `Warning` | 事件被跳过（如 WinFormsEventBinder 为 null、IsScriptExecutionMode=true） |
| `Error` | 事件绑定或触发失败 |
| `Debug` | 详细的绑定过程（仅在调试时启用） |

### 6.5 日志排查指南

当按钮点击无日志时，按以下顺序排查：

```
1. 检查 EventBinding_Start 是否存在
   ├─ 不存在 → 渲染管线的 BindEvents 阶段未执行
   └─ 存在 → 继续

2. 检查 EventBinding_SkipControl 是否存在
   ├─ 存在 → WinFormsEventBinder 注入时序问题（原因①）
   │   └─ 修复：确保 FrameworkInitializer 在 BindEvents 前注入 WinFormsEventBinder
   └─ 不存在 → 继续

3. 检查 WinFormsBinder_BindEvent(executeButton, click) 是否存在
   ├─ 不存在 → JSON 配置中 executeButton 的 events 未定义或未加载（原因②）
   │   └─ 修复：检查 MainLayout.json 和 EventBindingResolver._loadedConfigs
   └─ 存在 → 继续

4. 检查 UserEvent_Triggered(executeButton, click) 是否存在
   ├─ 不存在 → 继续
   └─ 存在 → 事件已触发，检查后续处理逻辑

5. 检查 UserEvent_Skipped(executeButton, click) 是否存在
   ├─ 存在 → IsScriptExecutionMode=true（原因④）
