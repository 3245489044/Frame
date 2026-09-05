# WdUIContainerManager 缺失修复方案

## 问题描述

`WdUIContainerManager` 在重构计划中被列为待创建文件，但从未被实际创建。以下两个文件引用了不存在的 `WdUIContainerManager`：

1. **`FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs`** — 10 处引用
2. **`FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs`** — 2 处引用

## 根因分析

`WdUIContainerManager` 是 2026-07-21 重构中计划创建的类，用于集中管理 ECS 容器的生命周期（初始化/关闭/创建/查找/清空/移除）。但该文件从未被创建，导致编译错误。

## 修复策略

### 方案：在 `FileDrivenWdUIBridge` 中直接实现容器管理逻辑

由于 `FileDrivenWdUIBridge` 本身就是静态桥接类，且 `WdUIContainerManager` 的设计意图就是静态管理容器，最简洁的修复方式是将 `WdUIContainerManager` 的功能直接内联到 `FileDrivenWdUIBridge` 中。

### 需要修改的文件

#### 文件 1：`FileDrivenWdUIBridge.cs`

**修改内容：** 将 `WdUIContainerManager.Xxx()` 调用替换为直接实现。

**需要添加的字段：**
```csharp
private static readonly ConcurrentDictionary<string, EcsConfigContainer> _containers = new();
private static volatile bool _isInitialized;
private static readonly object _lock = new();
```

**方法替换对照表：**

| 原调用 | 替换为 |
|--------|--------|
| `WdUIContainerManager.Initialize(registry)` | 直接实现：创建 `EcsPipelineOrchestrator`，注册到 `AppServiceProvider`，设置 `_isInitialized = true` |
| `WdUIContainerManager.Shutdown()` | 直接实现：释放 `EcsPipelineOrchestrator`，清空字典，设置 `_isInitialized = false` |
| `WdUIContainerManager.IsInitialized` | `_isInitialized` |
| `WdUIContainerManager.CreateContainer(name)` | `new EcsConfigContainer() { Name = name }` + 加入字典 |
| `WdUIContainerManager.CreateSubContainer(parent, name)` | `new EcsConfigContainer() { Name = name }` + 加入字典 + `parent.RegisterSubContainer(sub)` |
| `WdUIContainerManager.GetContainer(name)` | 从字典查找 |
| `WdUIContainerManager.RemoveContainer(name)` | 从字典移除 |
| `WdUIContainerManager.DisposeContainer(name)` | 释放 + 从字典移除 |
| `WdUIContainerManager.ClearContainerContent(container, form, logAction)` | 调用 `container.ClearContainerContent()` + UI 线程封送 |

**`ConfigContainer` 类型问题：**
- `FileDrivenWdUIBridge.cs` 中 `ConfigContainer` 来自 `using WdUI.UI;`
- 实际类型是 `EcsConfigContainer`（位于 `FrontEndDriven.Container` 命名空间）
- 需要添加 `using FrontEndDriven.Container;` 并将返回类型改为 `EcsConfigContainer`
- 或者添加类型别名：`using ConfigContainer = FrontEndDriven.Container.EcsConfigContainer;`

#### 文件 2：`FrameworkInitializer.cs`

**修改内容：** 将 `WdUIContainerManager.IsInitialized` 和 `WdUIContainerManager.Initialize(registry)` 替换为直接调用 `FileDrivenWdUIBridge` 的对应方法。

```csharp
// 原代码：
if (!WdUIContainerManager.IsInitialized)
{
    var registry = AppServiceProvider.Resolve<IStorageRegistry>()
        ?? throw new InvalidOperationException(...);
    WdUIContainerManager.Initialize(registry);
}

// 替换为：
if (!FileDrivenWdUIBridge.IsInitialized)
{
    var registry = AppServiceProvider.Resolve<IStorageRegistry>()
        ?? throw new InvalidOperationException(...);
    FileDrivenWdUIBridge.Initialize();
}
```

### 详细实现

#### FileDrivenWdUIBridge.cs 修改

**需要添加的 using：**
```csharp
using System.Collections.Concurrent;
using FrontEndDriven.Container;
using FrontEndDriven.ECS;
using UniversalMiddleware.Abstractions;
```

**需要添加的字段（在类中）：**
```csharp
private static readonly ConcurrentDictionary<string, EcsConfigContainer> _containers = new();
private static volatile bool _isInitialized;
private static readonly object _lock = new();
private static EcsPipelineOrchestrator? _orchestrator;
```

**Initialize 方法替换：**
```csharp
public static void Initialize()
{
    if (_isInitialized) return;
    lock (_lock)
    {
        if (_isInitialized) return;
        
        var registry = FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Resolve<IStorageRegistry>();
        if (registry == null)
        {
            registry = new Modules.InternalStorage.Core.StorageRegistry();
        }
        
        _orchestrator = new EcsPipelineOrchestrator(registry);
        FrameworkAbstractions.ComponentRegistry.AppServiceProvider.Register(_orchestrator);
        _isInitialized = true;
    }
}
```

**Shutdown 方法替换：**
```csharp
public static void Shutdown()
{
    if (!_isInitialized) return;
    lock (_lock)
    {
        if (!_isInitialized) return;
        
        foreach (var kvp in _containers)
        {
            kvp.Value.Dispose();
        }
        _containers.Clear();
        _orchestrator?.Dispose();
        _orchestrator = null;
        _isInitialized = false;
    }
}
```

**IsInitialized 属性替换：**
```csharp
public static bool IsInitialized => _isInitialized;
```

**CreateContainer 方法替换：**
```csharp
public static EcsConfigContainer CreateContainer(string name)
{
    var container = new EcsConfigContainer { Name = name };
    _containers[name] = container;
    return container;
}
```

**CreateSubContainer 方法替换：**
```csharp
public static EcsConfigContainer CreateSubContainer(EcsConfigContainer parent, string name)
{
    var subContainer = new EcsConfigContainer { Name = name };
    _containers[name] = subContainer;
    parent.RegisterSubContainer(subContainer);
    return subContainer;
}
```

**GetContainer 方法替换：**
```csharp
public static EcsConfigContainer? GetContainer(string name)
{
    _containers.TryGetValue(name, out var container);
    return container;
}
```

**RemoveContainer 方法替换：**
```csharp
public static void RemoveContainer(string name)
{
    _containers.TryRemove(name, out _);
}
```

**DisposeContainer 方法替换：**
```csharp
public static void DisposeContainer(string name)
{
    if (_containers.TryRemove(name, out var container))
    {
        container.Dispose();
    }
}
```

**ClearSideContainer 方法替换：**
```csharp
public static void ClearContainerContent(EcsConfigContainer container, Form form, Action<string> logAction)
{
    if (container == null) return;
    
    if (form.InvokeRequired)
    {
        form.BeginInvoke(() => ClearContainerContent(container, form, logAction));
        return;
    }
    
    container.ClearContainerContent();
    logAction?.Invoke($"侧边容器 '{container.Name}' 内容已清理");
}
```

**注意：** `ClearSideContainer` 方法签名需要改为使用 `EcsConfigContainer` 而非 `EcsConfigContainer`（当前已经是 `EcsConfigContainer`）。

### 影响范围

| 文件 | 修改类型 | 影响 |
|------|---------|------|
| `FileDrivenWdUIBridge.cs` | 重写内部实现 | 对外 API 不变，调用方无需修改 |
| `FrameworkInitializer.cs` | 替换引用 | 从 `WdUIContainerManager` 改为 `FileDrivenWdUIBridge` |

### 验证步骤

1. 修改完成后，执行 `dotnet build` 验证编译通过
2. 确认 `FileDrivenWdUIBridge` 的所有调用方（`MainForm.cs`、`CommandLayer.cs`）不受影响
3. 确认 `FrameworkInitializer.cs` 中的 WdUI 初始化流程正常
