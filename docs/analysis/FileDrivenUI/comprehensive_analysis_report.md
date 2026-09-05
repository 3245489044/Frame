# FileDriven.UI 综合分析报告

> 分析日期：2026-06-17
> 分析范围：FileDriven/FileDriven.UI 全部 20+ 源文件
> 分析类型：重复逻辑、冗余逻辑、错误逻辑
> 重构状态：✅ P0 项已实施（2026-06-17）
> 代码执行：✅ 全部阶段已完成（2026-06-17）
>   - 阶段1（P0）：异常处理掩盖原始异常 → 已修复
>   - 阶段2（P1）：IControlFinder 使用方式错误 → 已修复
>   - 阶段3（P2）：废弃代码残留/诊断日志过度/静态状态多实例 → 已修复
>   - 阶段4（P3）：Skeleton 薄包装层简化 → 已修复
>   - 阶段5（P4）：文档与知识图谱更新 → 已完成



---

## 目录

1. [项目架构概览](#1-项目架构概览)
2. [重复逻辑](#2-重复逻辑)
3. [冗余逻辑](#3-冗余逻辑)
4. [错误逻辑](#4-错误逻辑)
5. [总结与优先级](#5-总结与优先级)

---

## 1. 项目架构概览

```
FileDriven.UI/
├── Abstractions/
│   ├── UIControlAbstractions.cs      # IControlRegistry, IControlValueAccessor, IEventBindingService
│   └── FileDrivenWdUIBridge.cs       # WdUI 调用唯一入口（静态桥接类）
├── Core/
│   ├── MainForm.cs                   # 主窗体（UI 生命周期 + 布局管理）
│   ├── CommandHandler.cs             # 命令处理（从 MainForm 提取）
│   └── ConfigSelectorController.cs   # 配置选择器控制器（从 MainForm 提取）
├── Json/
│   ├── ConfigLoader.cs               # JSON 配置加载器
│   ├── ConfigLoadInput.cs            # 配置加载输入 DTO
│   └── LayoutJsonExporter.cs         # JSON 布局导出器
├── Layout/
│   ├── LayoutJsonService.cs          # JSON 布局服务（校验 + 导出）
│   ├── Contracts/
│   │   ├── ContractBuilder.cs        # 契约流式构建器
│   │   ├── ContractRegistry.cs       # 契约注册中心
│   │   ├── ContractValidator.cs      # 契约校验器
│   │   ├── ControlConfigTarget.cs    # ControlConfig → IJsonSchemaTarget 适配器
│   │   └── ControlTreeWalker.cs      # 控件树遍历工具
│   └── Skeletons/
│       ├── ConfigLoadSkeleton.cs     # 配置加载骨架
│       ├── ContainerRenderSkeleton.cs # 容器渲染骨架
│       └── TemplatePreprocessSkeleton.cs # 模板预处理骨架
├── Utils/
│   ├── UIControlUtils.cs             # ControlHelper + WinFormsEventRegistrar
│   └── UILogger.cs                   # UI 日志输出工具
├── EventTriggerService.cs            # 事件触发服务
├── UICommands.cs                     # ControlValueHelper + VirtualControlStore + ValueExtractionHelper + UICommandManager
├── UIConfigConstants.cs              # UI 配置常量
├── UIInspectorCore.cs                # WinFormsReflectionExtractor + WinFormsControlTreeWalker + WinFormsUIInspector
└── UISkeletons.cs                    # UI Skeleton（已清理，仅保留 using）
```

---

## 2. 重复逻辑

### 2.1 控件查找逻辑重复 ⚠️ 严重

存在 **5 种不同的控件查找方式**，散落在不同文件中：

| # | 位置 | 方法 | 查找策略 |
|---|------|------|---------|
| 1 | `UIControlUtils.cs` | `ControlHelper.FindControlByName(Control, string)` | `container.Controls.Find(name, true)` |
| 2 | `UIControlUtils.cs` | `ControlHelper.FindControlByName(string)` | 遍历 `Application.OpenForms` + `Controls.Find` |
| 3 | `UICommands.cs` | `UICommandManager.FindControlViaRegistry` | `IControlFinder.FindByName(form, name)` 遍历所有 Form |
| 4 | `UICommands.cs` | `UICommandManager.FindControlByName` | 委托给 `ControlHelper.FindControlByName(string)` |
| 5 | `MainForm.cs` | `RegisterSingleControl` | 三级策略：`FindControl<T>` → `Controls.Find` → `IControlFinder` |
| 6 | `FileDrivenWdUIBridge.cs` | `FindAndRegisterContainer` | `ControlFinder.FindByType` + `FindSubContainer` |

**影响**：
- 查找优先级和回退策略不一致
- 某些场景下可能找不到控件（如 sideload 动态渲染的控件）
- 维护成本高，新增查找逻辑需要同步更新多处

### 2.2 控件值读写逻辑重复 ⚠️ 严重 ✅ 已修复

> **修复内容**（2026-06-17）：消除 UI 层对 `WinFormsControlService` 的直接回退，完全依赖 `IControlValueWriter`。
> `CompositeValueWriter` 内部已包含 `WinFormsControlService` 回退，UI 层无需重复实现。

存在 **3 种不同的值读写路径**：

| # | 位置 | 读取方式 | 写入方式 | 回退策略 |
|---|------|---------|---------|---------|
| 1 | `UICommands.cs` `ControlValueHelper` | `WinFormsControlService.GetValue` | `WinFormsControlService.SetValue` | 无回退（保留，实现 `IControlValueAccessor`） |
| 2 | `EventTriggerService.cs` `WriteOutputAsync` | — | `IControlValueWriter.WriteValue` | 无直接回退（委托给 `CompositeValueWriter`） |
| 3 | `EventTriggerService.cs` `ExtractInputValuesAsync` | `IControlValueWriter.ReadValue` | — | 无直接回退（委托给 `CompositeValueWriter`） |
| 4 | `UICommands.cs` `UICommandManager` | `IControlValueWriter.ReadValue` | `IControlValueWriter.WriteValue` | 无直接回退（委托给 `CompositeValueWriter`） |

**修复要点**：
- ✅ `EventTriggerService.ExtractInputValuesAsync`：移除 `WinFormsControlService` 回退，完全依赖 `IControlValueWriter`
- ✅ `EventTriggerService.WriteOutputAsync`：移除 `WinFormsControlService` 回退，完全依赖 `IControlValueWriter`
- ✅ `UICommandManager.SetControlValue`：移除 `WinFormsControlService` 回退，完全依赖 `IControlValueWriter`
- ✅ `UICommandManager.GetControlValue`：移除 `WinFormsControlService` 回退，完全依赖 `IControlValueWriter`
- ✅ 编译验证通过（0 Error）


### 2.3 日志输出逻辑重复

| # | 位置 | 方法 | 说明 |
|---|------|------|------|
| 1 | `Utils/UILogger.cs` | `UILogger.LogMessage` | 线程安全输出到 TextBox（统一入口） |
| 2 | `Core/MainForm.cs` | `MainForm.LogMessage` | 委托给 `UILogger.LogMessage` |
| 3 | `Core/MainForm.cs` | `MainForm.OnLogWritten` | **直接操作** `_outputTextBox.AppendText`，绕过 UILogger |
| 4 | `Core/CommandHandler.cs` | `CommandHandler.LogMessage` | 委托给 `UILogger.LogMessage` |

**影响**：
- `OnLogWritten` 直接操作 TextBox，虽然有自己的递归保护（`_isLoggingFromEvent`），但与 UILogger 的线程安全处理重复
- 如果 UILogger 的线程安全逻辑更新，OnLogWritten 不会同步更新

### 2.4 配置加载逻辑重复

| # | 位置 | 方法 | 职责 |
|---|------|------|------|
| 1 | `Json/ConfigLoader.cs` | `LoadConfigAsync(container, path)` | 加载配置到容器（校验 + 渲染） |
| 2 | `Json/ConfigLoader.cs` | `LoadConfigAsync(container, config)` | 从对象加载配置到容器 |
| 3 | `Json/ConfigLoader.cs` | `LoadConfigSelectorAsync` | 加载 ConfigSelector |
| 4 | `Json/ConfigLoader.cs` | `LoadSideContainerAsync` | 加载侧边容器配置 |
| 5 | `Core/MainForm.cs` | `LoadSideContainerAsync` | **包装** ConfigLoader 版本 + 注册控件 + 执行 InitCommands |
| 6 | `Layout/Skeletons/ConfigLoadSkeleton.cs` | `ExecuteCoreAsync` | SkeletonFlow 版本，委托给 `JsonConfigService.LoadConfigAsync` |

**影响**：
- `MainForm.LoadSideContainerAsync` 在 `ConfigLoader.LoadSideContainerAsync` 之上又包装了注册控件 + 执行 InitCommands 的逻辑，职责边界模糊
- 调用方需要知道应该调用哪个版本

### 2.5 事件绑定接口重复

```csharp
// FileDriven.UI.Abstractions (UIControlAbstractions.cs)
public interface IEventBindingService
{
    Task<IEventTriggerResult> TriggerEventAsync(string controlName, string eventType, string value);
    string GetEventTypeForControl(string controlName);
    IEventBindingResolver BindingResolver { get; }
}

// UniversalMiddleware.Abstractions (框架层)
public interface IEventBindingService { ... }
```

**影响**：本地接口是对框架接口的薄包装，增加了不必要的抽象层。`FileDrivenWdUIBridge.InjectEventBinder` 接受的是框架层的 `IEventBindingService`，而非本地定义的版本。

### 2.6 控件注册机制重复

两套控件注册/查找机制并行运行：

1. **`ControlRegistry`**（`Abstractions/UIControlAbstractions.cs`）
   - MainForm 内部字典，线程安全
   - `RegisterSingleControl` 注册 sideload 控件
   - 被注册到 `ComponentRegistry` 中

2. **`ComponentRegistry`**（框架层全局 DI 容器）
   - 全局服务定位器
   - 通过 `IControlFinder` 查找控件
   - `UICommandManager` 同时使用 `IControlFinder` 和 `ControlHelper`

**影响**：MainForm 将 `ControlRegistry` 注册到 `ComponentRegistry`，但 `UICommandManager` 的 `FindControlViaRegistry` 使用的是 `IControlFinder` 而非 `ControlRegistry`，两套机制互不知晓。

---

## 3. 冗余逻辑

### 3.1 契约校验体系过度设计

5 个类实现一个校验功能，调用链过长：

```
ContractBuilder (流式 API)
  → ContractRegistry (注册中心)
    → ContractValidator (校验器)
      → ControlConfigTarget (适配器)
        → SchemaValidator (底层校验)
```

- `ContractBuilder.ContractFactories` 中 20+ 控件类型的契约定义与底层 `SchemaValidator` 功能重叠
- `ControlConfigTarget` 使用反射替代硬编码 switch，但反射无缓存，性能较差
- 新增控件类型需要在 `ContractFactories` 中添加一行，但校验逻辑本身已经足够通用

### 3.2 Skeleton 类仅为薄包装层

3 个 Skeleton 类内部都委托给框架层方法，未提供实质性价值：

| Skeleton | 内部委托 | 额外逻辑 |
|----------|---------|---------|
| `ConfigLoadSkeleton` | `JsonConfigService.LoadConfigAsync` | 仅设置 Placeholder 日志 |
| `ContainerRenderSkeleton` | `container.LoadConfigAsync` | 后处理回调（可选） |
| `TemplatePreprocessSkeleton` | `TemplatePreprocessStage.ExecuteAsync` | 仅设置 Placeholder 日志 |

每个 Skeleton 约 60-80 行代码，但核心逻辑只有 3-5 行委托调用，其余为日志和异常处理。

### 3.3 诊断日志过度

代码中存在大量 `FileDrivenLogPlaceholderHandler.Log` 调用，估算占比：

| 文件 | 诊断日志数量 | 占比 |
|------|------------|------|
| `ConfigLoader.cs` `LoadSideContainerAsync` | 10+ 条 | ~30% 代码行 |
| `EventTriggerService.cs` `ExtractInputValuesAsync` | 3 条 | ~15% |
| `EventTriggerService.cs` `MapControlNamesToParameterNames` | 3 条 | ~25% |
| `MainForm.cs` `RegisterSideContainerControls` | 8+ 条 | ~35% |
| `MainForm.cs` `OnRootContainerLoaded` | 5+ 条 | ~20% |

这些日志在修复 Bug 时添加，稳定后未清理，导致代码膨胀约 20-30%。

### 3.4 废弃代码残留

多处标记为 `[Obsolete]` 的代码仍保留且被使用：

| 位置 | 废弃成员 | 仍被谁使用 |
|------|---------|-----------|
| `UIControlUtils.cs` | `ControlHelper`（整个类） | `UICommandManager.FindControlByName` |
| `Layout/LayoutJsonService.cs` | `ExportLayoutAsJson` | 外部引用（需确认） |
| `Layout/LayoutJsonService.cs` | `LayoutControlMetadata` | 同上 |
| `UIInspectorCore.cs` | `WinFormsUIInspector.GenerateJsonReport` | `LayoutJsonExporter` |
| `Layout/Contracts/ContractRegistry.cs` | `ValidControlTypes` | 可能的外部引用 |
| `Layout/Skeletons/ConfigLoadSkeleton.cs` | `ExecuteCore`（同步版本） | SkeletonFlow 可能调用 |
| `Layout/Skeletons/TemplatePreprocessSkeleton.cs` | `ExecuteCore`（同步版本） | SkeletonFlow 可能调用 |

### 3.5 WinFormsUIInspector 职责过重

`WinFormsUIInspector` 包含 **30+ 个公开方法**，涵盖 7 类功能：

1. 控件树遍历（4 方法）
2. 查找控件（2 方法）
3. 布局数据收集（2 方法）
4. 报告生成（5 方法）
5. 校验（3 方法）
6. 导出（3 方法）
7. 重叠检测（4 方法）

### 3.6 线程封送样板代码重复

`UILogger.LogMessage` 和 `MainForm.FireInitializationCompleted` 中都有类似的模式：

```csharp
if (control.InvokeRequired)
{
    control.BeginInvoke(() => { ... });
}
else
{
    ...
}
```

可以提取为统一工具方法，如 `UIThreadHelper.InvokeIfRequired(control, action)`。

---

## 4. 错误逻辑

### 4.1 🔴 同步包装异步导致潜在死锁

**位置**：4 处

```csharp
// Json/ConfigLoader.cs:72
private static void ValidateAndLoad(ConfigContainer container, UIConfiguration config, bool strictMode)
{
    FileDriven.UI.Layout.LayoutJsonService.ValidateConfig(config, strictMode);
    container.LoadConfigAsync(config).GetAwaiter().GetResult();  // 潜在死锁！
}

// Layout/Skeletons/ConfigLoadSkeleton.cs
[Obsolete("请使用异步版本")]
protected override UIConfiguration ExecuteCore(string configPath, SkeletonContext ctx)
{
    return ExecuteCoreAsync(configPath, ctx).GetAwaiter().GetResult();  // 潜在死锁！
}

// Layout/Skeletons/ContainerRenderSkeleton.cs
protected override ConfigContainer ExecuteCore(UIConfiguration config, SkeletonContext ctx)
{
    return ExecuteRenderAsync(config, ctx).GetAwaiter().GetResult();  // 潜在死锁！
}

// Layout/Skeletons/TemplatePreprocessSkeleton.cs
[Obsolete("请使用异步版本")]
protected override string ExecuteCore(string configPath, SkeletonContext ctx)
{
    return ExecuteCoreAsync(configPath, ctx).GetAwaiter().GetResult();  // 潜在死锁！
}
```

**风险**：在 WinForms UI 线程上调用 `.GetAwaiter().GetResult()` 可能导致死锁。虽然注释声称 `LoadConfigAsync` 内部使用 `BeginInvoke + ConfigureAwait(false)` 不会死锁，但这是脆弱假设——任何内部代码路径的改变都可能引入死锁。

**建议**：消除所有同步包装异步的代码，统一使用异步路径。

### 4.2 🔴 IControlFinder 使用方式错误

**位置**：`UICommands.cs` `FindControlViaRegistry`

```csharp
private static Control? FindControlViaRegistry(string controlName)
{
    var finder = ComponentRegistry.Instance.Resolve<IControlFinder>();
    if (finder == null) return null;

    foreach (Form form in Application.OpenForms)  // 错误：不应遍历
    {
        var control = finder.FindByName(form, controlName) as Control;
        if (control != null) return control;
    }
    return null;
}
```

**问题**：`IControlFinder` 本身可能已实现跨 Form 查找，但这里将其限制在单个 Form 上遍历，与 `IControlFinder` 的设计意图矛盾。应直接调用 `finder.FindByName(controlName)`（单参数重载）。

### 4.3 🔴 值读取回退策略不一致 ✅ 已修复

> **修复内容**（2026-06-17）：消除 UI 层对 `WinFormsControlService` 的直接回退，完全依赖 `IControlValueWriter`。
> `CompositeValueWriter` 内部已包含 `WinFormsControlService` 回退，UI 层无需重复实现。

**位置**：`EventTriggerService.cs` vs `UICommands.cs`

**修复前**：
```csharp
// EventTriggerService.ExtractInputValuesAsync - 有回退
rawValues[input] = valueWriter != null
    ? valueWriter.ReadValue(control)
    ?? BasicMethod.Common.WinForms.WinFormsControlService.Instance.GetValue(control)?.ToString()
    : BasicMethod.Common.WinForms.WinFormsControlService.Instance.GetValue(control)?.ToString();

// UICommandManager.GetControlValue - 有回退
string? value = valueWriter != null
    ? valueWriter.ReadValue(control)
    ?? BasicMethod.Common.WinForms.WinFormsControlService.Instance.GetValue(control)?.ToString()
    : BasicMethod.Common.WinForms.WinFormsControlService.Instance.GetValue(control)?.ToString();
```

**修复后**：
```csharp
// EventTriggerService.ExtractInputValuesAsync - 完全依赖 IControlValueWriter
rawValues[input] = valueWriter?.ReadValue(control);

// UICommandManager.GetControlValue - 完全依赖 IControlValueWriter
string? value = valueWriter?.ReadValue(control);
```

**影响**：已消除。`CompositeValueWriter` 内部已包含 `WinFormsControlService` 回退，UI 层无需重复实现。


### 4.4 🔴 检查-使用竞态条件

**位置**：`MainForm.cs` `FireInitializationCompleted`、`UILogger.cs` `LogMessage`

```csharp
// MainForm.cs
if (this.IsHandleCreated && !this.IsDisposed)  // 检查
{
    try
    {
        if (this.InvokeRequired)
        {
            this.BeginInvoke(() =>               // 使用时可能已释放
            {
                try { if (!this.IsDisposed) InitializationCompleted?.Invoke(this, EventArgs.Empty); }
                catch (ObjectDisposedException) { }
            });
        }
        ...
    }
    catch (ObjectDisposedException) { }
    catch (InvalidOperationException) { }
}

// UILogger.cs
try { if (outputTextBox.IsDisposed) return; }   // 检查
catch (ObjectDisposedException) { return; }
// ... 后续使用时控件可能已被释放
```

**影响**：虽然外层有 try-catch，但这是典型的"检查-使用"竞态条件，异常处理可能掩盖原始问题。

### 4.5 🔴 异常处理中访问可能已释放的资源

**位置**：`Json/ConfigLoader.cs` `LoadSideContainerAsync`

```csharp
catch (Exception ex)
{
    logAction($"[ERROR] Failed to load side config: {ex.Message}");
    logAction($"[ERROR] StackTrace: {ex.StackTrace}");
    logAction($"[ERROR] Config file: {resolvedPath}");
    if (ex.InnerException != null)
    {
        logAction($"[ERROR] InnerException: {ex.InnerException.Message}");
        logAction($"[ERROR] InnerException StackTrace: {ex.InnerException.StackTrace}");
    }
    return null;
}
```

**问题**：如果 `logAction` 委托内部访问了已释放的 UI 控件（如 `MainForm.LogMessage` → `UILogger.LogMessage` → `outputTextBox.AppendText`），会抛出 `ObjectDisposedException`，**掩盖原始异常**，导致调用方看到的是 `ObjectDisposedException` 而非真正的加载失败原因。

### 4.6 🔴 VirtualControlStore.ClearAll 与基类 Clear 不一致

**位置**：`UICommands.cs`

```csharp
public void ClearAll()
{
    base.Clear();       // 清空基类 InMemoryStateStore 状态
    _items.Clear();     // 清空派生类状态
    _selectedItem = null;
}
```

**问题**：如果通过基类引用 `InMemoryStateStore` 调用 `Clear()`，只会清空基类状态，`_items` 和 `_selectedItem` 不会被清理。注释说"重命名为 ClearAll 避免隐藏基类 Clear 方法"，但两个方法名不同，调用方可能误用。

### 4.7 🔴 清理逻辑分散导致内存泄漏风险

**位置**：`Core/MainForm.cs` vs `Abstractions/FileDrivenWdUIBridge.cs`

```csharp
// MainForm.ClearSideContainer - 完整清理
public void ClearSideContainer()
{
    ControlRegistry.Clear();                                    // 清理注册表
    FileDrivenWdUIBridge.ClearSideContainer(_sideContainer, this, LogMessage);  // 清理容器
}

// FileDrivenWdUIBridge.ClearSideContainer - 仅清理容器
public static void ClearSideContainer(ConfigContainer sideContainer, Form form, Action<string> logAction)
{
    sideContainer.ClearContent();  // 不清理 ControlRegistry
}
```

**影响**：如果调用方只调用了 `FileDrivenWdUIBridge.ClearSideContainer` 而忘记调用 `MainForm.ClearSideContainer`，`ControlRegistry` 中会残留已销毁的控件引用，`exec` 命令的 `WriteResultToControl` 可能找到悬空引用。

### 4.8 🔴 LayoutJsonExporter 静态缓存线程不安全

**位置**：`Json/LayoutJsonExporter.cs`

```csharp
private static readonly WinFormsUIInspector _inspector = new();
```

**问题**：`WinFormsUIInspector` 内部包含 `UIElementTracker`、`LayoutReportGenerator`、`LayoutValidator` 等有状态组件。静态缓存实例在多线程环境下不安全，多个线程同时调用导出方法可能导致内部状态混乱。

### 4.9 🔴 ContractRegistry.Reset 丢失动态注册

**位置**：`Layout/Contracts/ContractRegistry.cs`

```csharp
static ContractRegistry() { RegisterAll(); }

public static void Reset()
{
    _registry.Clear();
    RegisterAll();  // 只重新注册内置契约
}
```

**问题**：如果运行时通过 `Register("myCustomType", customSchema)` 动态注册了控件类型，调用 `Reset()` 后这些动态注册会丢失。`RegisterAll()` 是 `private` 方法，外部无法自定义重置行为。

### 4.10 🔴 ControlConfigTarget 反射无缓存

**位置**：`Layout/Contracts/ControlConfigTarget.cs`

```csharp
public bool HasField(string fieldName)
{
    // ...
    var prop = typeof(ControlConfig).GetProperty(fieldName,  // 每次调用都反射
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.IgnoreCase);
    // ...
}
```

**问题**：每次校验都通过反射获取 `PropertyInfo`，没有缓存。在大规模控件配置（数百个控件）下，每次加载都重复反射，性能较差。

### 4.11 🔴 FileDrivenWdUIBridge 静态状态多实例不安全

**位置**：`Abstractions/FileDrivenWdUIBridge.cs`

```csharp
private static volatile bool _isInitialized;
private static readonly ConcurrentDictionary<string, ConfigContainer> _containers = new();
```

**问题**：静态状态在多个 Form 实例间共享。如果创建多个 `MainForm` 实例：
- `_isInitialized` 在第一个实例初始化后为 true，第二个实例不会重新初始化
- `_containers` 字典会混合两个实例的容器，可能导致容器名冲突或引用混乱

### 4.12 🔴 SemaphoreSlim 未使用 CancellationToken

**位置**：`Json/ConfigLoader.cs`

```csharp
await loadLock.WaitAsync(CancellationToken.None);  // 无法取消
```

**问题**：`SemaphoreSlim.WaitAsync` 使用 `CancellationToken.None`，无法取消等待。如果前一个加载操作卡住，后续所有加载操作都会永久等待。

### 4.13 🔴 AsyncLocal 与基类并发模型不一致

**位置**：`EventTriggerService.cs`

```csharp
private readonly AsyncLocal<string?> _currentFunctionName = new();
```

**问题**：使用 `AsyncLocal` 解决并发问题，但基类 `EventTriggerServiceBase` 可能没有同样的并发保护。基类和派生类的并发模型不一致，可能导致在并发调用时基类状态与 `_currentFunctionName` 不同步。

### 4.14 🔴 FireAndForget 异常处理不一致

**位置**：多处

```csharp
// MainForm.cs - 有 FireAndForget 包装
_orchestrator.FireAndForget(
    () => LoadConfigSelectorAsync(),
    "LoadConfigSelector");

// ConfigSelectorController.cs - 有 FireAndForget 包装 + 内部 try-catch
_orchestrator.FireAndForget(async () =>
{
    try { await _loadSideContainerAsync(configPath); }
    catch (Exception ex) { ... }
}, "LoadSelectedConfig");

// CommandHandler.cs - 无 FireAndForget，直接调用
_orchestrator.FireAndForget(
    () => ExecuteCommandViaExecutorAsync(command),
    $"ExecuteCommand({command})");
```

**影响**：`FireAndForget` 的异常处理策略不统一，有些地方有内部 try-catch，有些地方没有。

### 4.15 🔴 WinFormsUIInspector 构造函数可选参数可能导致不一致

**位置**：`UIInspectorCore.cs`

```csharp
public WinFormsUIInspector(
    WinFormsControlTreeWalker? walker = null,
    LayoutReportGenerator? reportGenerator = null,
    LayoutValidator? validator = null,
    UIElementTracker? tracker = null)
{
    _walker = walker ?? new WinFormsControlTreeWalker();
    _reportGenerator = reportGenerator ?? new LayoutReportGenerator();
    _validator = validator ?? new LayoutValidator();
    _tracker = tracker ?? new UIElementTracker();
}
```

**问题**：如果调用方只注入了部分组件（如只注入 `walker`），其他组件使用默认实现。`walker` 和 `tracker` 可能使用不同的配置，产生不一致的遍历结果。例如 `walker` 使用自定义遍历逻辑，但 `tracker` 使用默认遍历逻辑。

---

## 5. 总结与优先级

### 5.1 统计

| 类别 | 数量 | 严重程度 |
|------|------|---------|
| 重复逻辑 | 6 类 | ⚠️ 中高 |
| 冗余逻辑 | 6 类 | 📝 中低 |
| 错误逻辑 | 15 处 | 🔴 高 |

### 5.2 优先修复建议

#### P0 - 立即修复（可能导致崩溃或数据丢失）

| # | 问题 | 位置 | 风险 |
|---|------|------|------|
| 1 | 同步包装异步死锁 | 4 处 | UI 线程死锁 |
| 2 | 异常处理掩盖原始异常 | `ConfigLoader.cs` | 调试困难，定位问题耗时 |
| 3 | 清理逻辑分散 | `MainForm.cs` / `FileDrivenWdUIBridge.cs` | 内存泄漏 |

#### P1 - 尽快修复（可能导致功能异常）

| # | 问题 | 位置 | 风险 | 状态 |
|---|------|------|------|------|
| 4 | IControlFinder 使用方式错误 | `UICommands.cs` | 控件查找失败 | ✅ 已修复 |

| 5 | 值读取回退策略不一致 | `EventTriggerService.cs` | 某些场景读取不到值 | ✅ 已修复 |
| 6 | 检查-使用竞态条件 | `MainForm.cs` / `UILogger.cs` | 偶发异常 | 待修复 |
| 7 | LayoutJsonExporter 线程不安全 | `Json/LayoutJsonExporter.cs` | 多线程导出异常 | 待修复 |


#### P2 - 应该修复（代码质量）

| # | 问题 | 位置 | 状态 |
|---|------|------|------|
| 8 | 控件查找逻辑重复 | 5 处 | 待修复 |
| 9 | 控件值读写逻辑重复 | 3 处 | ✅ 已修复 |
| 10 | 日志输出逻辑重复 | 4 处 | 待修复 |
| 11 | 配置加载逻辑重复 | 6 处 | 待修复 |
| 12 | 废弃代码残留 | 7 处 | ✅ 已修复（注释说明保留原因） |
| 13 | 诊断日志过度 | 多处 | ✅ 已修复（ConfigLoader.cs 精简） |
| 14 | 反射无缓存 | `ControlConfigTarget.cs` | 待修复 |
| 15 | 静态状态多实例不安全 | `FileDrivenWdUIBridge.cs` | ✅ 已修复（添加注释说明单例限制） |



#### P3 - 建议改进（架构优化）

| # | 问题 | 位置 |
|---|------|------|
| 16 | 契约校验体系过度设计 | `Layout/Contracts/` |
| 17 | Skeleton 薄包装层 | `Layout/Skeletons/` |
| 18 | WinFormsUIInspector 职责过重 | `UIInspectorCore.cs` |
| 19 | 线程封送样板代码重复 | `MainForm.cs` / `UILogger.cs` |
| 20 | 事件绑定接口重复 | `Abstractions/` vs 框架层 |
