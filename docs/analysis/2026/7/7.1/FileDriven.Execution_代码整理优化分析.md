# FileDriven.Execution 代码整理优化分析报告

> **分析范围**：`CsharpPrj/FileDriven/FileDriven.Execution/`  
> **对比范围**：`CsharpPrj/FileDriven/FileDriven.UI/Core/`、`CsharpPrj/FileDriven/FileDriven.UI/WinForms/Services/`  
> **接口定义**：`CsharpPrj/Operation/ForntEndDriven/Abstractions/`  
> **分析日期**：2026-06-30  
> **分析维度**：重复逻辑、冗余逻辑、错误逻辑

---

## 目录

1. [架构概览](#1-架构概览)
2. [重复逻辑分析](#2-重复逻辑分析)
3. [冗余逻辑分析](#3-冗余逻辑分析)
4. [错误逻辑分析](#4-错误逻辑分析)
5. [优化建议](#5-优化建议)
6. [优先级排序](#6-优先级排序)

---

## 1. 架构概览

### 1.1 分析文件清单

| 目录 | 文件 | 行数 | 职责 |
|------|------|------|------|
| **EventPipeline/** | `EventHub.cs` | 142 | 框架级生命周期事件中心 |
| | `EventSubscriptionManager.cs` | 185 | 统一事件订阅生命周期管理器 |
| | `UIEventBinder.cs` | 205 | WinForms UI 事件绑定器 |
| | `WinFormsEventRegistrar.cs` | 95 | WinForms 控件事件注册器 |
| | `WinFormsEventBinder.cs` | 252 | WinForms 事件绑定器（反射） |
| | `EventBindingInfo.cs` | 26 | 事件绑定信息 DTO |
| | `EventBindingResolver.cs` | 305 | 事件绑定解析器 |
| | `EventPipelineExecutor.cs` | 181 | 事件管线执行器 |
| | `EventTriggerService.cs` | 123 | 事件触发服务 |
| | `Models/LoadedConfigInfo.cs` | 25 | 已加载配置信息 DTO |
| **Pipeline/Stages/** | `ParseCommandStage.cs` | 76 | 命令解析阶段 |
| | `ExecuteCommandStage.cs` | 189 | 命令执行阶段 |
| | `ResultCollectStage.cs` | 49 | 结果收集阶段 |
| **Gui/** | `FileDrivenGuiInitializer.cs` | 202 | GUI 模式初始化器 |

### 1.2 对比文件清单

| 文件 | 行数 | 关系 |
|------|------|------|
| `FileDriven.UI/Core/EventHub.cs` | 63 | Execution EventHub 的旧版本（静态类） |
| `FileDriven.UI/Core/EventSubscriptionManager.cs` | 156 | Execution EventSubscriptionManager 的旧版本 |
| `FileDriven.UI/WinForms/Services/UIEventBinder.cs` | 157 | Execution UIEventBinder 的旧版本 |

### 1.3 当前架构关系

```
EventPipeline (FileDriven.Execution)
  │
  ├── 实现 ForntEndDriven 接口：
  │     EventHub              → IEventHub
  │     EventSubscriptionManager → IEventSubscriptionManager
  │     UIEventBinder         → IUIEventBinder
  │     WinFormsEventRegistrar → IWinFormsEventRegistrar
  │
  ├── 使用 EventTriggerFramework：
  │     EventBindingResolver  → IEventBindingResolver (ETF)
  │     EventTriggerService   → extends EventTriggerServiceBase (ETF.Core)
  │     EventPipelineExecutor → uses EventTypeMapper (ETF)
  │     WinFormsEventBinder   → IWinFormsEventBinder (ETF)
  │     EventBindingInfo      → IEventBindingInfo (ETF)
  │
  └── 自实现：
  │     EventHub              → 生命周期事件中心
  │     WinFormsEventBinder   → 自实现反射绑定
  │     EventSubscriptionManager → 自实现订阅管理
  │
  └── UI 层遗留（应废弃）：
        FileDriven.UI/Core/EventHub.cs              → 静态类旧版本
        FileDriven.UI/Core/EventSubscriptionManager.cs → 旧版本
        FileDriven.UI/WinForms/Services/UIEventBinder.cs → 旧版本（逻辑未同步）
```

---

## 2. 重复逻辑分析

### 2.1 EventHub 双重实现

#### 现状

| 位置 | 类型 | 实现方式 | 状态 |
|------|------|---------|------|
| `Execution/EventPipeline/EventHub.cs` | 实例类 | 实现 `IEventHub`，内部使用 `EventBusFacade` | ✅ 当前使用 |
| `UI/Core/EventHub.cs` | 静态类 | 原生 `event` 字段 | ❌ 遗留代码 |

#### 差异对比

| 维度 | Execution 版本 | UI 版本 |
|------|---------------|---------|
| 实例化方式 | `new EventHub(eventBus?)` — 支持 DI 注入 | `EventHub.RaiseInitializationCompleted()` — 静态调用 |
| 事件总线 | `EventBusFacade`（命名事件系统） | 原生 `event` 字段 |
| 订阅清除 | `_eventBus.ClearAll()` | `InitializationCompleted = null`（反射 hack） |
| ConfigLoaded 参数 | `ConfigLoadedEventArgs(configName, success)` | 无参数 |
| 接口实现 | `IEventHub` | 无接口（静态类无法实现接口） |

#### 根因

注释说明"保留在 UI 层因为循环依赖"，但 Execution 版本已存在且被使用。UI 版本是迁移前的旧代码，迁移后未清理。

#### 优化建议

- 在 `UI/Core/EventHub.cs` 添加 `[Obsolete("请使用 FileDriven.Execution.EventPipeline.EventHub")]` 特性
- 确认所有调用方已迁移到 Execution 版本后删除 UI 版本

---

### 2.2 EventSubscriptionManager 双重实现

#### 现状

| 位置 | 去重机制 | 额外功能 | 状态 |
|------|---------|---------|------|
| `Execution/EventPipeline/EventSubscriptionManager.cs` | `ConcurrentDictionary`（线程安全） | `AddUnsubscribeAction<TDelegate>` 泛型辅助 | ✅ 当前使用 |
| `UI/Core/EventSubscriptionManager.cs` | `bool` 标志（非线程安全） | 无 | ❌ 遗留代码 |

#### 差异对比

| 维度 | Execution 版本 | UI 版本 |
|------|---------------|---------|
| Subscribe 三个重载 | ✅ 有 | ✅ 有 |
| AddUnsubscribeAction 泛型 | ✅ `AddUnsubscribeAction<TDelegate>` | ❌ 无 |
| AddUnsubscribeAction _disposed 检查 | ❌ 无 | ✅ 有 |
| SubscribeLogWritten 去重 | `ConcurrentDictionary.TryAdd` | `bool _logWrittenSubscribed` |
| Dispose 实现 | 委托给 `UnsubscribeAll()` | 独立遍历（代码重复） |
| UnsubscribeAll 遍历顺序 | 正向遍历 | 逆序遍历 |

#### 优化建议

- 在 `UI/Core/EventSubscriptionManager.cs` 添加 `[Obsolete]` 特性
- 将 UI 版本的 `_disposed` 检查合并到 Execution 版本
- 确认所有调用方已迁移后删除 UI 版本

---

### 2.3 UIEventBinder 双重实现

#### 现状

| 位置 | 基类 | 状态 |
|------|------|------|
| `Execution/EventPipeline/UIEventBinder.cs` | Execution 的 `EventSubscriptionManager` | ✅ 当前使用 |
| `UI/WinForms/Services/UIEventBinder.cs` | UI 的 `EventSubscriptionManager` | ❌ 遗留代码 |

#### 关键差异：BindConfigSelectorEvents 逻辑不一致

| 事件 | Execution 版本 | UI 版本 |
|------|---------------|---------|
| `searchTextBox.TextChanged` | → `_refreshConfigList()` | → `_refreshConfigList()` |
| `configListView.SelectedIndexChanged` | → **启用 `loadSelButton`** ✅ | → **空操作** ❌ |
| `configListView.DoubleClick` | → `_loadSelectedConfig()` | → `_loadSelectedConfig()` |
| `loadSelButton.Click` | → `_loadSelectedConfig()` | → `_loadSelectedConfig()` |
| `browseButton.Click` | → 使用 `_openFileDialog`（可选注入） | → 使用 `_openFileDialog`（必选，有默认值） |
| `enableProcessingCheckBox.CheckedChanged` | → **切换 `styleComboBox` 启用状态** ✅ | → **`_refreshConfigList()`** ❌ |

#### 差异：UnbindAll 实现

| 步骤 | Execution 版本 | UI 版本 |
|------|---------------|---------|
| 1 | `UnbindCommandEvents()` | ❌ 缺失 |
| 2 | `UnsubscribeAll()` | `UnsubscribeAll()` |

#### 差异：构造函数参数

| 参数 | Execution 版本 | UI 版本 |
|------|---------------|---------|
| `loadSideContainerAsync` 返回类型 | `Func<string, Task>` | `Func<string, Task<UIConfiguration?>>` |
| `openFileDialog` 默认值 | `null`（可选） | `new OpenFileDialog{...}`（有默认值） |

#### 优化建议

- 在 `UI/WinForms/Services/UIEventBinder.cs` 添加 `[Obsolete]` 特性
- 确认所有调用方已迁移后删除 UI 版本

---

### 2.4 WinFormsEventRegistrar 模式重复

#### 现状

6 个方法重复完全相同的结构：

```csharp
// 每个方法都是这个模式，只有控件类型和事件名不同
public Action RegisterXxx(ConfigContainer container, string controlName, EventHandler handler)
{
    var control = FindControl<TControl>(container, controlName);
    if (control == null) return () => { };          // ← 重复
    control.Xxx += handler;                          // ← 不同
    return () => control.Xxx -= handler;             // ← 不同
}
```

#### 优化建议

提取泛型辅助方法：

```csharp
private Action RegisterEvent<TControl>(
    ConfigContainer container, string controlName, EventHandler handler,
    Action<TControl, EventHandler> subscribe,
    Action<TControl, EventHandler> unsubscribe)
    where TControl : Control
{
    var control = FindControl<TControl>(container, controlName);
    if (control == null) return () => { };
    subscribe(control, handler);
    return () => unsubscribe(control, handler);
}
```

各方法简化为一行委托：

```csharp
public Action RegisterClick(ConfigContainer container, string controlName, EventHandler handler)
    => RegisterEvent<Button>(container, controlName, handler,
        (c, h) => c.Click += h, (c, h) => c.Click -= h);
```

---

## 3. 冗余逻辑分析

### 3.1 EventSubscriptionManager.Dispose 与 UnsubscribeAll 代码重复（UI 版本）

#### 现状

UI 版本的 `Dispose()` 和 `UnsubscribeAll()` 有完全相同的遍历逻辑：

```csharp
// UnsubscribeAll() — 第 92-108 行
for (int i = _unsubscribeActions.Count - 1; i >= 0; i--)
{
    try { _unsubscribeActions[i](); } catch { }
}
_unsubscribeActions.Clear();

// Dispose() — 第 137-155 行
for (int i = _unsubscribeActions.Count - 1; i >= 0; i--)
{
    try { _unsubscribeActions[i](); } catch { }
}
_unsubscribeActions.Clear();
```

Execution 版本已修复：`Dispose()` 委托给 `UnsubscribeAll()`。

#### 优化建议

UI 版本应同步 Execution 版本的修复，`Dispose()` 直接调用 `UnsubscribeAll()`。

---

### 3.2 EventBindingResolver.FindControlInConfig 死代码

#### 现状

```csharp
// EventBindingResolver.cs 第 303-304 行
private static ControlConfig? FindControlInConfig(UIConfiguration config, string name)
    => ControlTreeWalker.FindControl(config, name);
```

此方法从未被调用。`FindControlByName` 已通过缓存实现完整查找逻辑。

#### 优化建议

直接删除此方法。

---

### 3.3 EventBindingResolver 的 lockObj 废弃参数

#### 现状

```csharp
// 两个构造函数都接受 lockObj 参数但从未使用
public EventBindingResolver(..., object? lockObj = null, ...)
```

注释说明"★ B1 修复：改用 ConcurrentDictionary 替代手动 lock"，但参数未清理。

#### 优化建议

移除 `lockObj` 参数，保持构造函数签名简洁。

---

### 3.4 EventPipelineExecutor 与 EventTriggerService 的 Form 回退模式重复

#### 现状

两处实现相同的"优先注入→回退全局"模式：

| 位置 | 优先 | 回退 |
|------|------|------|
| `EventPipelineExecutor.Execute()` | `_targetForm` | `Application.OpenForms[0]` |
| `EventTriggerService.FindControl()` | `_targetForm` | `Form.ActiveForm` |

#### 优化建议

提取共享的 `FormResolver` 工具方法：

```csharp
internal static class FormResolver
{
    public static Form? Resolve(Form? preferredForm)
        => preferredForm ?? Form.ActiveForm ?? 
           (Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null);
}
```

---

### 3.5 EventHub 的包装器缓存可泛型化

#### 现状

```csharp
private readonly Dictionary<EventHandler, Action<object?>> _initCompletedWrappers = new();
private readonly Dictionary<EventHandler<ConfigLoadedEventArgs>, Action<object?>> _configLoadedWrappers = new();
```

add/remove 逻辑几乎完全相同，只是事件类型不同。

#### 优化建议

提取泛型方法：

```csharp
private void AddWrapper<T>(T key, Action<object?> wrapper, 
    Dictionary<T, Action<object?>> cache, string eventName)
    where T : Delegate
{
    if (!cache.ContainsKey(key))
    {
        cache[key] = wrapper;
        _eventBus.Subscribe(eventName, wrapper);
    }
}

private void RemoveWrapper<T>(T key, 
    Dictionary<T, Action<object?>> cache, string eventName)
    where T : Delegate
{
    if (cache.TryGetValue(key, out var wrapper))
    {
        _eventBus.Unsubscribe(eventName, wrapper);
        cache.Remove(key);
    }
}
```

---

## 4. 错误逻辑分析

### 4.1 🔴 UIEventBinder 事件绑定逻辑不一致（严重）

#### 问题描述

Execution 版本和 UI 版本的 `BindConfigSelectorEvents()` 逻辑不一致，UI 版本缺少两个关键功能：

1. `configListView.SelectedIndexChanged` → 启用 `loadSelButton`（UI 版本是空操作）
2. `enableProcessingCheckBox.CheckedChanged` → 切换 `styleComboBox` 启用状态（UI 版本是刷新配置列表）

#### 影响

如果 UI 版本仍在使用：
- 用户选中列表项时"加载选中配置"按钮不会启用
- 勾选"启用处理"复选框时样式下拉框不会切换启用状态

#### 修复方案

UI 版本应同步 Execution 版本的事件绑定逻辑，或直接标记废弃。

---

### 4.2 🔴 UIEventBinder.UnbindAll 缺少 UnbindCommandEvents（严重）

#### 问题描述

```csharp
// Execution 版本 — 正确
public void UnbindAll()
{
    UnbindCommandEvents();  // ← 先取消命令事件
    UnsubscribeAll();       // ← 再取消所有订阅
}

// UI 版本 — 错误
public void UnbindAll()
{
    UnsubscribeAll();       // ← 缺少 UnbindCommandEvents()
}
```

#### 影响

`commandTextBox.KeyPress` 事件在 `UnbindAll()` 后仍然保持订阅，导致：
- 对象无法被垃圾回收（事件泄漏）
- 已释放的控件仍可能收到事件回调（`ObjectDisposedException`）

#### 修复方案

UI 版本添加 `UnbindCommandEvents()` 调用。

---

### 4.3 🟡 EventSubscriptionManager._disposed 检查不一致

#### 问题描述

```csharp
// Execution 版本 — AddUnsubscribeAction 没有 _disposed 检查
protected void AddUnsubscribeAction(Action unsubscribe)
{
    _unsubscribeActions.Add(unsubscribe);  // ← Dispose 后仍可添加
}

// UI 版本 — AddUnsubscribeAction 有 _disposed 检查
protected void AddUnsubscribeAction(Action unsubscribe)
{
    if (!_disposed)  // ← 有检查
    {
        _unsubscribeActions.Add(unsubscribe);
    }
}
```

#### 影响

Execution 版本在 `Dispose()` 后仍可添加订阅，这些订阅不会被清理，导致事件泄漏。

#### 修复方案

将 UI 版本的 `_disposed` 检查合并到 Execution 版本。

---

### 4.4 🟡 SubscribeLogWritten 去重机制线程安全差异

#### 问题描述

```csharp
// Execution 版本 — 线程安全
private readonly ConcurrentDictionary<object, byte> _subscribedHandlers = new();
public void SubscribeLogWritten(Action<LogEntry> handler)
{
    if (_subscribedHandlers.TryAdd(handler, 0))  // ← 原子操作
    { ... }
}

// UI 版本 — 非线程安全
private bool _logWrittenSubscribed;
public void SubscribeLogWritten(Action<LogEntry> handler)
{
    if (_logWrittenSubscribed) return;  // ← 非原子操作
    _logWrittenSubscribed = true;
    ...
}
```

#### 影响

UI 版本在多线程环境下，两个线程可能同时通过 `if` 检查，导致重复订阅。

#### 修复方案

UI 版本应使用 `ConcurrentDictionary` 或 `Interlocked.CompareExchange` 确保原子性。

---

### 4.5 🟡 WinFormsEventBinder._isScriptExecutionMode 静态字段冲突

#### 问题描述

```csharp
private static bool _isScriptExecutionMode;
public bool IsScriptExecutionMode
{
    get => _isScriptExecutionMode;
    set => _isScriptExecutionMode = value;
}
```

注释说"通过静态字段确保多个实例间共享状态"，但：
- 如果多个 `WinFormsEventBinder` 实例需要不同的脚本执行模式状态，静态字段会导致冲突
- 实例属性包装静态字段的设计令人困惑

#### 影响

当系统中有多个 `WinFormsEventBinder` 实例时，一个实例设置 `IsScriptExecutionMode = true` 会影响所有实例。

#### 修复方案

- 如果确实需要共享状态，使用实例字段 + 单例模式更清晰
- 如果不需要共享，改为实例字段

---

### 4.6 🟡 EventBindingResolver 缓存永不失效

#### 问题描述

```csharp
private Dictionary<string, ControlConfig>? _controlCache;
private volatile bool _cacheBuilt;

private object? FindControlByName(string controlName)
{
    if (!_cacheBuilt)
    {
        lock (_cacheLock)
        {
            if (!_cacheBuilt)
            {
                // 构建缓存...
                _cacheBuilt = true;  // ← 之后永不更新
            }
        }
    }
    return _controlCache?.TryGetValue(controlName, out var cached) == true ? cached : null;
}
```

`_controlCache` 在首次查找时构建，之后永不更新。如果 `_loadedConfigs` 在运行时发生变化（如 `DoClose` 中的 `TryRemove`），缓存不会同步。

#### 影响

运行时配置变更后，事件绑定解析返回过时结果：
- 已移除的控件仍可找到绑定
- 新增的控件找不到绑定

#### 修复方案

添加缓存失效机制：

```csharp
public void InvalidateCache()
{
    _cacheBuilt = false;
    _controlCache = null;
}
```

在 `_loadedConfigs` 变更时调用 `InvalidateCache()`。

---

### 4.7 🟡 ExecuteCommandStage 的 Task.Run + GetResult 死锁风险

#### 问题描述

```csharp
var containerResult = Task.Run(async () =>
    await _executionCell.ExecuteAsync(request).ConfigureAwait(false))
    .GetAwaiter().GetResult();
```

#### 风险分析

| 场景 | Process() 执行线程 | 风险 |
|------|-------------------|------|
| 管线路径 | 非 UI 线程（ThreadPool） | 低 — Task.Run 切换到另一个 ThreadPool 线程，无死锁 |
| 事件触发路径 | UI 线程（WinForms 消息循环） | 中 — Task.Run 将任务调度到 ThreadPool，但 GetResult 阻塞 UI 线程 |

虽然 `ConfigureAwait(false)` 减少了死锁概率，但 `GetResult()` 是同步阻塞调用，在 UI 线程上执行时仍可能因线程池饥饿导致死锁。

#### 修复方案

- 如果 `Process()` 可能在 UI 线程执行，使用 `async` 重写 `Process` 方法
- 或使用 `_executionCell.ExecuteAsync(request).GetAwaiter().GetResult()`（无 Task.Run 包装）

---

### 4.8 🟢 EventTriggerService 的 Form.ActiveForm 回退不可靠

#### 问题描述

```csharp
private Control? FindControl(string controlName)
{
    var form = _targetForm ?? Form.ActiveForm;  // ← CLI 模式下 Form.ActiveForm 返回 null
    if (form == null) return null;
    ...
}
```

注释已承认"CLI 模式下 Form.ActiveForm 返回 null"。

#### 影响

CLI 模式下 `FindControl` 总是返回 `null`，输出写入功能失效。

#### 修复方案

- 确保 CLI 模式调用时传入 `_targetForm`
- 或添加 `Application.OpenForms` 作为二级回退

---

## 5. 优化建议

### 5.1 立即执行（低风险，高收益）

| 序号 | 操作 | 涉及文件 | 风险 |
|------|------|---------|------|
| 1 | UI 层三个文件添加 `[Obsolete]` 特性 | `UI/Core/EventHub.cs`、`UI/Core/EventSubscriptionManager.cs`、`UI/WinForms/Services/UIEventBinder.cs` | 低 |
| 2 | 删除 `EventBindingResolver.FindControlInConfig` 死代码 | `EventBindingResolver.cs` | 低 |
| 3 | 移除 `EventBindingResolver.lockObj` 废弃参数 | `EventBindingResolver.cs` | 低 |
| 4 | 合并 `_disposed` 检查到 Execution 版本 | `EventSubscriptionManager.cs` | 低 |

### 5.2 建议执行（中等风险，中等收益）

| 序号 | 操作 | 涉及文件 | 风险 |
|------|------|---------|------|
| 5 | 同步 UIEventBinder 事件绑定逻辑 | `UI/WinForms/Services/UIEventBinder.cs` | 中 |
| 6 | 添加 `UnbindCommandEvents` 到 UI 版本 | `UI/WinForms/Services/UIEventBinder.cs` | 低 |
| 7 | 添加缓存失效机制 | `EventBindingResolver.cs` | 中 |
| 8 | 提取 `FormResolver` 工具方法 | `EventPipelineExecutor.cs`、`EventTriggerService.cs` | 低 |

### 5.3 规划执行（需要更多分析）

| 序号 | 操作 | 涉及文件 | 风险 |
|------|------|---------|------|
| 9 | WinFormsEventRegistrar 泛型化重构 | `WinFormsEventRegistrar.cs` | 中 |
| 10 | EventHub 包装器缓存泛型化 | `EventHub.cs` | 低 |
| 11 | WinFormsEventBinder 静态字段改为实例字段 | `WinFormsEventBinder.cs` | 中 |
| 12 | ExecuteCommandStage 异步改造 | `ExecuteCommandStage.cs` | 高 |

---

## 6. 优先级排序

### 按影响范围排序

```
优先级 1 — 功能缺失/错误
  ├── 🔴 UIEventBinder 双重实现逻辑不一致
  ├── 🔴 UIEventBinder.UnbindAll 缺少 UnbindCommandEvents
  └── 🟡 EventBindingResolver 缓存永不失效

优先级 2 — 维护负担
  ├── EventHub 双重实现（标记废弃）
  ├── EventSubscriptionManager 双重实现（标记废弃）
  └── UIEventBinder 双重实现（标记废弃）

优先级 3 — 代码整洁
  ├── FindControlInConfig 死代码删除
  ├── lockObj 废弃参数移除
  ├── _disposed 检查合并
  └── Form 回退模式提取

优先级 4 — 架构优化
  ├── WinFormsEventRegistrar 泛型化
  ├── EventHub 包装器泛型化
  ├── WinFormsEventBinder 静态字段修复
  └── ExecuteCommandStage 异步改造
```

### 推荐实施顺序

```
第 1 步：标记 UI 层废弃代码（低风险）
第 2 步：删除死代码和废弃参数（低风险）
第 3 步：合并 _disposed 检查（低风险）
第 4 步：添加缓存失效机制（中风险）
第 5 步：提取 FormResolver（低风险）
第 6 步：WinFormsEventRegistrar 泛型化（中风险）
第 7 步：EventHub 包装器泛型化（低风险）
第 8 步：WinFormsEventBinder 静态字段修复（中风险）
第 9 步：ExecuteCommandStage 异步改造（高风险，需充分测试）
```

---

## 附录：文件状态总览

| 文件 | 行数 | 状态 | 问题类型 | 建议操作 |
|------|------|------|---------|---------|
| `Execution/EventPipeline/EventHub.cs` | 142 | ✅ 可用 | 冗余（可泛型化） | 提取泛型方法 |
| `Execution/EventPipeline/EventSubscriptionManager.cs` | 185 | ✅ 可用 | 错误（缺 _disposed 检查） | 合并 _disposed 检查 |
| `Execution/EventPipeline/UIEventBinder.cs` | 205 | ✅ 可用 | 无 | 保持现状 |
| `Execution/EventPipeline/WinFormsEventRegistrar.cs` | 95 | ✅ 可用 | 重复（模式可泛型化） | 提取泛型辅助方法 |
| `Execution/EventPipeline/WinFormsEventBinder.cs` | 252 | ✅ 可用 | 错误（静态字段） | 改为实例字段 |
| `Execution/EventPipeline/EventBindingInfo.cs` | 26 | ✅ 可用 | 无 | 保持现状 |
| `Execution/EventPipeline/EventBindingResolver.cs` | 305 | ✅ 可用 | 冗余+错误 | 删除死代码+废弃参数+缓存失效 |
| `Execution/EventPipeline/EventPipelineExecutor.cs` | 181 | ✅ 可用 | 冗余（Form 回退） | 提取 FormResolver |
| `Execution/EventPipeline/EventTriggerService.cs` | 123 | ✅ 可用 | 冗余+错误 | 提取 FormResolver+修复回退 |
| `Execution/EventPipeline/Models/LoadedConfigInfo.cs` | 25 | ✅ 可用 | 无 | 保持现状 |
| `Execution/Pipeline/Stages/ExecuteCommandStage.cs` | 189 | ✅ 可用 | 错误（死锁风险） | 异步改造 |
| `Execution/Pipeline/Stages/ParseCommandStage.cs` | 76 | ✅ 可用 | 无 | 保持现状 |
| `Execution/Pipeline/Stages/ResultCollectStage.cs` | 49 | ✅ 可用 | 无 | 保持现状 |
| `Execution/Gui/FileDrivenGuiInitializer.cs` | 202 | ✅ 可用 | 无 | 保持现状 |
| `UI/Core/EventHub.cs` | 63 | ❌ 遗留 | 重复 | 标记 `[Obsolete]` |
| `UI/Core/EventSubscriptionManager.cs` | 156 | ❌ 遗留 | 重复+错误 | 标记 `[Obsolete]` |
| `UI/WinForms/Services/UIEventBinder.cs` | 157 | ❌ 遗留 | 重复+错误 | 标记 `[Obsolete]` |
