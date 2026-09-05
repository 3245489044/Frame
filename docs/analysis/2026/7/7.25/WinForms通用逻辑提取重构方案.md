# WinForms 通用逻辑提取重构方案

> 目标：将 `CsharpPrj/Operation/FrontEndDriven/WinForms/` 中与 WinForms 无关的通用逻辑提取到 `Shared/` 和相关基础设施层，
> 使通用代码可被 WPF/MAUI/Avalonia 等其他 UI 框架复用，同时缩小 WinForms 目录的维护面。

---

## 一、现有基础设施现状

### 1.1 已存在的泛型树算法（`Framework.Tree` 命名空间）

| 位置 | 组件 | 用途 |
|------|------|------|
| `BasicMethod/Map/GraphFoundation/FrameworkTree/` | `TreeWalker<T>` | 泛型树遍历（DFS/BFS） |
| 同上 | `TreePathFinder<T>` | 泛型祖先路径查找 |
| 同上 | `TreeIndex<T, TKey>` | 泛型 O(1) 树索引 |
| 同上 | `TreeNode<T>` | 泛型树节点 |

`ControlIndex`（WinForms/Finder/ControlIndex.cs）和 `ControlFinder`（WinForms/Finder/ControlFinder.cs）本质上是这些泛型算法的 **WinForms 特化包装**。

### 1.2 已存在的抽象层（`Shared/FrameworkAbstractions/Controls/`）

| 文件 | 用途 |
|------|------|
| `IControlFinder.cs` | 统一控件查找器接口（已抽象） |
| `IControlValue.cs` | 控件值读写接口 |
| `ControlTreeHelper.cs` | 控件树遍历工具（含 WinForms 依赖） |
| `WinFormsControlFinder.cs` | WinForms 实现 |

### 1.3 目标目录依赖图谱

```
WinForms/ (FrontEndDriven)
  ├── Finder/ControlIndex.cs          ──依赖→ Framework.Tree.TreeIndex
  ├── Finder/ControlFinder.cs         ──依赖→ Framework.Tree.TreeWalker + TreePathFinder
  ├── Finder/ControlRegistryAwareFinder.cs ──依赖→ IControlFinder（已在 Shared）
  ├── Abstractions/IListSelectionAdapter.cs ──→ 纯接口
  ├── Abstractions/IWinFormsControlAdapter.cs ──→ 含 WinForms 依赖的接口
  ├── Utils/UiThreadHelper.cs          ──→ 部分通用
  ├── ValueSync/WinFormsControlValueWriter.cs ──→ 适配器注册表通用
  └── Services/FileDrivenWdUIBridge.cs ──→ ECS 耦合
```

---

## 二、第一梯队：可直接提取（零 / 低 WinForms 依赖）

### 2.1 `ControlIndex` → 泛型索引包装器提取

**当前状态**：
`ControlIndex` 是 `TreeIndex<Control, string>` 的薄包装，所有方法（`Rebuild`、`Register`、`FindByName`、`Contains`、`GetKeys`、`GetAll`、`Clear`、`Count`）直接委托给 `_index`。

**现状代码**（WinForms/Finder/ControlIndex.cs）：
```csharp
public class ControlIndex
{
    private readonly TreeIndex<Control, string> _index;
    // 所有方法委托给 _index
}
```

**提取方案**：
创建泛型包装器替代 ControlIndex 的包装逻辑，ControlIndex 继承/组合泛型版本。

```
  WinForms/Finder/             Shared/Tree/
    ControlIndex.cs  ──重构──→    TreeIndexWrapper.cs (泛型)
        │                           ↑ 继承或组合
        └── 保留为 WinForms 特化
```

**具体步骤**：

1. 在 `Shared/Tree/`（或 `FrameworkAbstractions/Utilities/`）创建 `TreeIndexWrapper<T, TKey>`：

```csharp
// Shared/Tree/TreeIndexWrapper.cs
namespace FrameworkAbstractions.Tree;

/// <summary>
/// 通用树索引包装器 — 提供类型安全的 O(1) 节点查找
/// <para>包装 Framework.Tree.TreeIndex 的通用功能。</para>
/// </summary>
/// <typeparam name="TNode">节点类型</typeparam>
/// <typeparam name="TKey">键类型</typeparam>
public class TreeIndexWrapper<TNode, TKey>
    where TNode : class
{
    private readonly TreeIndex<TNode, TKey> _index;

    public TreeIndexWrapper(IEqualityComparer<TKey>? keyComparer = null)
    {
        _index = new TreeIndex<TNode, TKey>(keyComparer);
    }

    public void Rebuild(TNode root, Func<TNode, IEnumerable<TNode>> getChildren, 
        Func<TNode, TKey?> keyExtractor) => _index.Rebuild(root, getChildren, keyExtractor);
    public void RegisterNode(TNode node, TKey key) => _index.RegisterNode(node, key);
    public void UnregisterNode(TKey key) => _index.UnregisterNode(key);
    public TNode? Find(TKey key) => _index.TryGet(key, out var node) ? node : null;
    public bool Contains(TKey key) => _index.Contains(key);
    public IEnumerable<TKey> GetKeys() => _index.GetKeys();
    public IEnumerable<TNode> GetAll() => _index.GetAll().Select(kvp => kvp.Value);
    public void Clear() => _index.Clear();
    public int Count => _index.Count;
}
```

2. `ControlIndex` 改为继承：

```csharp
public class ControlIndex : TreeIndexWrapper<Control, string>
{
    public ControlIndex() : base(StringComparer.OrdinalIgnoreCase) { }

    // 保留 WinForms 特化的 Rebuild/Register/Unregister 重载
    public void Rebuild(Control root) =>
        base.Rebuild(root, c => c.Controls.Cast<Control>(), c => c.Name);
    public void Register(Control control) { if (!string.IsNullOrEmpty(control.Name)) RegisterNode(control, control.Name); }
    public void Unregister(Control control) { if (!string.IsNullOrEmpty(control.Name)) UnregisterNode(control.Name); }
    public void UnregisterByName(string name) { if (!string.IsNullOrEmpty(name)) UnregisterNode(name); }
    public Control? FindByName(string name) => Find(name);
    public T? FindByName<T>(string name) where T : Control => Find(name) as T;
}
```

**影响范围**：
- 新增文件：`Shared/Tree/TreeIndexWrapper.cs`（或 `FrameworkAbstractions/Utilities/`）
- 修改文件：`WinForms/Finder/ControlIndex.cs`（继承基类，删除重复委托代码）
- 预计删除重复代码：~80 行

---

### 2.2 `ControlFinder` 中 `ForEachOpenFormSafe` / `FindAcrossOpenForms` → 跨 Form 遍历基础

**当前状态**：
`ControlFinder.ForEachOpenFormSafe` 和 `FindAcrossOpenForms` / `FindInForm` 是 WinForms 特有的跨 Form 遍历。

**提取方案**：
`ForEachOpenFormSafe` 的模式（`try-foreach-catch ObjectDisposedException`）是通用安全遍历模式，可提取为泛型辅助方法。

**具体步骤**：

1. 在 `Shared/Utils/` 创建 `SafeEnumerator.cs`：

```csharp
// Shared/Utils/SafeEnumerator.cs
namespace FrameworkAbstractions.Utilities;

/// <summary>
/// 安全枚举器 — 处理枚举过程中集合被修改的场景
/// <para>通用化 Application.OpenForms 遍历中 try-catch ObjectDisposedException 的模式。</para>
/// </summary>
public static class SafeEnumerator
{
    /// <summary>
    /// 安全遍历集合，处理枚举过程中元素被移除的场景
    /// </summary>
    /// <typeparam name="T">集合元素类型</typeparam>
    /// <param name="collection">要遍历的集合</param>
    /// <param name="predicate">对每个元素执行的操作，返回 true 停止遍历</param>
    /// <param name="isValid">判断元素是否有效的函数（默认可为 null）</param>
    /// <returns>如果 predicate 返回 true 则返回 true</returns>
    public static bool ForEachSafe<T>(
        System.Collections.IEnumerable collection,
        Func<T, bool> predicate,
        Func<T, bool>? isValid = null)
    {
        try
        {
            foreach (T item in collection)
            {
                if (isValid != null && !isValid(item))
                    continue;
                try
                {
                    if (predicate(item))
                        return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                    continue;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 集合在遍历过程中被修改
        }
        return false;
    }
}
```

2. `ControlFinder.ForEachOpenFormSafe` 改为：

```csharp
public static bool ForEachOpenFormSafe(Func<Form, bool> formAction)
{
    return SafeEnumerator.ForEachSafe<Form>(
        Application.OpenForms,
        formAction,
        isValid: form => !form.IsDisposed);
}
```

**影响范围**：
- 新增文件：`Shared/Utils/SafeEnumerator.cs`
- 修改文件：`WinForms/Finder/ControlFinder.cs`
- 预计删除重复代码：~30 行

---

### 2.3 `IListSelectionAdapter` 接口移动

**当前状态**：
纯接口文件，位于 `WinForms/Abstractions/IListSelectionAdapter.cs`，无任何 WinForms 依赖。

**提取方案**：
直接移动接口文件到 `Shared/Abstractions/`，保留命名空间为 `FrontEndDriven.WinForms.Abstractions` 的别名兼容性，或统一到 `FrameworkAbstractions.Controls`。

**具体步骤**：

1. 复制到 `Shared/FrameworkAbstractions/Controls/IListSelectionAdapter.cs`
2. 命名空间改为 `FrameworkAbstractions.Controls`
3. `WinForms/Abstractions/IListSelectionAdapter.cs` 改为 using 转发（或直接删除并更新所有引用方）

```csharp
// Shared/FrameworkAbstractions/Controls/IListSelectionAdapter.cs
namespace FrameworkAbstractions.Controls;

public interface IListSelectionAdapter
{
    int GetSelectedCount(object control);
    void ClearSelection(object control);
    bool IsSelected(object control, int index);
    void SelectItem(object control, int index);
    int GetItemCount(object control);
}
```

**影响范围**：
- 新增文件：`Shared/FrameworkAbstractions/Controls/IListSelectionAdapter.cs`
- 修改文件：`WinForms/Abstractions/IListSelectionAdapter.cs`、`WinForms/Adapter/ListSelectionAdapter.cs`（更新 using）
- 预计工作量：~0.2h

---

### 2.4 `UiThreadHelper` 中 `FireAndForget` + `RunOnSynchronizationContext` 提取

**当前状态**：
`UiThreadHelper` 中以下方法不依赖 WinForms：
- `FireAndForget(Func<Task>, Action<Exception>)`（L19-34）
- `FireAndForget(Action, Action<Exception>)`（L168-181）
- `RunOnSynchronizationContext(Func<Task>)`（L75-98）

其余方法依赖 `Control.InvokeRequired` / `Control.Invoke`。

**提取方案**：

1. 在 `Shared/Threading/` 创建 `AsyncHelper.cs`：

```csharp
// Shared/Threading/AsyncHelper.cs
namespace FrameworkAbstractions.Threading;

/// <summary>
/// 通用异步辅助方法 — 不依赖任何 UI 框架
/// </summary>
public static class AsyncHelper
{
    /// <summary>异步执行并忘记（Func<Task> 重载）</summary>
    public static async void FireAndForget(Func<Task> asyncAction, Action<Exception>? onError = null)
    {
        try { await asyncAction(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { onError?.Invoke(ex); }
    }

    /// <summary>异步执行并忘记（Action 重载）</summary>
    public static async void FireAndForget(Action action, Action<Exception>? onError = null)
    {
        try { await Task.Run(action); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { onError?.Invoke(ex); }
    }

    /// <summary>
    /// 通过 SynchronizationContext 调度到目标线程（不依赖控件句柄）
    /// </summary>
    public static Task RunOnSynchronizationContext(Func<Task> asyncAction)
    {
        var sc = SynchronizationContext.Current;
        if (sc != null)
        {
            var tcs = new TaskCompletionSource<object?>();
            sc.Post(async _ =>
            {
                try { await asyncAction(); tcs.TrySetResult(null); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            }, null);
            return tcs.Task;
        }
        return asyncAction();
    }
}
```

2. `UiThreadHelper` 中的对应方法改为委托：

```csharp
public static async void FireAndForget(Func<Task> asyncAction, Action<Exception>? onError = null)
    => AsyncHelper.FireAndForget(asyncAction, onError);
```

**影响范围**：
- 新增文件：`Shared/Threading/AsyncHelper.cs`
- 修改文件：`WinForms/Utils/UiThreadHelper.cs`
- 预计删除重复代码：~40 行

---

### 2.5 `ContainerRegistry.ExecuteLocked` 模式 → 通用线程安全包装

**当前状态**：
`ContainerRegistry` 中的 `ExecuteLocked<T>(Func<T>)` 和 `ExecuteLocked(Action)` 是通用模板方法，仅使用了 `lock (_lock)` + 委托。

**提取方案**：

1. 在 `Shared/Threading/` 创建 `SynchronizedAccessor.cs`：

```csharp
// Shared/Threading/SynchronizedAccessor.cs
namespace FrameworkAbstractions.Threading;

/// <summary>
/// 线程安全访问器 — 基于 lock 的通用同步包装
/// </summary>
/// <typeparam name="TState">被保护的状态类型</typeparam>
public sealed class SynchronizedAccessor<TState>
{
    private readonly TState _state;
    private readonly object _lock = new();

    public SynchronizedAccessor(TState state) => _state = state;

    public TResult Read<TResult>(Func<TState, TResult> reader)
    {
        lock (_lock) { return reader(_state); }
    }

    public void Write(Action<TState> writer)
    {
        lock (_lock) { writer(_state); }
    }

    public TResult Write<TResult>(Func<TState, TResult> writer)
    {
        lock (_lock) { return writer(_state); }
    }
}
```

2. `ContainerRegistry` 改为组合：

```csharp
public class ContainerRegistry : IControlRegistry
{
    private readonly SynchronizedAccessor<ControlIndex> _syncIndex;

    public ContainerRegistry()
    {
        _syncIndex = new SynchronizedAccessor<ControlIndex>(new ControlIndex());
    }

    public void Register(string name, object control) { /* 验证... */ _syncIndex.Write(idx => idx.Register(ctrl)); }
    public Control? FindByName(string name) => _syncIndex.Read(idx => idx.FindByName(name));
    // ...
}
```

**影响范围**：
- 新增文件：`Shared/Threading/SynchronizedAccessor.cs`
- 修改文件：`WinForms/Container/ContainerRegistry.cs`（替换重复的 `ExecuteLocked` 样板）
- 预计删除重复代码：~15 行

---

## 三、第二梯队：需接口适配后提取

### 3.1 `ConfigSelectorController` → 业务逻辑与 UI 解耦

**当前状态**：
`RefreshConfigList`、`UpdateConfigStatus`、`LoadSelectedConfig` 三个方法混合了文件扫描逻辑和 WinForms 控件操作（`ListView`、`Label`）。

**提取方案**：
通过 `IConfigSelectorView` 接口解耦。

**新接口**：

```csharp
// Shared/Abstractions/IConfigSelectorView.cs
namespace FrontEndDriven.Abstractions;

/// <summary>
/// 配置选择器视图接口 — 供 ConfigSelectorService 调用以更新 UI
/// </summary>
public interface IConfigSelectorView
{
    void SetConfigList(IEnumerable<ConfigFileInfo> files);
    void SetStatus(string text, string colorArgb); // 使用字符串而非 Color 避免 WinForms 依赖
}
```

**业务服务**：

```csharp
// 提取到 Shared/Services/ConfigSelectorService.cs
public class ConfigSelectorService
{
    private readonly string _configsDirectory;
    private readonly IConfigSelectorView _view;

    public void RefreshConfigList(string? searchPattern = null)
    {
        var files = Directory.Exists(_configsDirectory)
            ? Directory.GetFiles(_configsDirectory, $"*{searchPattern ?? ""}*.json", SearchOption.AllDirectories)
            : Array.Empty<string>();
        _view.SetConfigList(files.Select(f => new ConfigFileInfo(f)));
        _view.SetStatus(files.Length == 0 ? "未找到配置文件" : $"找到 {files.Length} 个配置文件",
            files.Length == 0 ? "DarkOrange" : "DarkGreen");
    }
}

public record ConfigFileInfo(string FullPath)
{
    public string FileName => Path.GetFileName(FullPath);
    public DateTime LastWriteTime => File.GetLastWriteTime(FullPath);
}
```

**影响范围**：
- 新增文件：`Shared/Abstractions/IConfigSelectorView.cs`、`Shared/Services/ConfigSelectorService.cs`
- 修改文件：`WinForms/ConfigSelectorController.cs`（改为实现 `IConfigSelectorView` + 委托给 `ConfigSelectorService`）
- 预计代码精简：~40 行

---

### 3.2 `WinFormsControlValueWriter` → 适配器注册表框架提取

**当前状态**：
`WinFormsControlValueWriter` 包含三部分：
1. **适配器注册表逻辑**（`_adapters` 字段 + `FindAdapter` + `RegisterAdapter` + `ClearAdapters`）
2. **UI 线程包装**（`WrapWithUiThreadDispatch`、`WrapFuncWithUiThreadDispatch`、`WrapActionWithUiThreadDispatch`）
3. **19 种具体控件适配器注册**（`RegisterTextControlAdapter<Label>` 等）

其中第 1 部分（~50 行）是纯通用逻辑。第 2 部分用到了 `UiThreadHelper.DispatchIfNeeded`，仍耦合 WinForms。第 3 部分是 WinForms 特有的。

**提取方案**：

1. 将适配器注册表的通用逻辑提取为 `TypedAdapterRegistry<TAdapter>`：

```csharp
// Shared/Patterns/TypedAdapterRegistry.cs
namespace FrameworkAbstractions.Patterns;

/// <summary>
/// 类型适配器注册表 — 支持精确类型匹配 + 基类/接口回退
/// </summary>
/// <typeparam name="TAdapter">适配器类型</typeparam>
public class TypedAdapterRegistry<TAdapter>
{
    protected readonly Dictionary<Type, TAdapter> _adapters = new();

    public void Register(Type type, TAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(type);
        _adapters[type] = adapter;
    }

    public TAdapter? Find(Type controlType)
    {
        if (_adapters.TryGetValue(controlType, out var entry))
            return entry;
        foreach (var kvp in _adapters)
        {
            if (kvp.Key.IsAssignableFrom(controlType))
                return kvp.Value;
        }
        return null;
    }

    public void Clear() => _adapters.Clear();
}
```

2. `WinFormsControlValueWriter` 改为继承：

```csharp
public class WinFormsControlValueWriter : IControlValueAccessor
{
    private readonly TypedAdapterRegistry<AdapterEntry> _adapterRegistry = new();
    // 使用时 _adapterRegistry.Find(control.GetType()) 替代 FindAdapter
}
```

**影响范围**：
- 新增文件：`Shared/Patterns/TypedAdapterRegistry.cs`
- 修改文件：`WinForms/ValueSync/WinFormsControlValueWriter.cs`（使用通用注册表）
- 预计删除重复代码：~30 行

---

### 3.3 `IWinFormsControlAdapter` → 重命名为 `IControlAdapter` 并移动

**当前状态**：
接口定义在 `WinForms/Abstractions/IWinFormsControlAdapter.cs`，方法涵盖了大多数 UI 框架共有的控件操作（布局、样式、属性、容器操作）。

**存在问题**：
- `GetOrientation` 返回 `System.Windows.Forms.Orientation` → 需改为 `string` 或自定义枚举
- `SubscribeSplitterMoved` / `UnsubscribeSplitterMoved` 是 SplitContainer 专有 → 可提取到独立接口 `ISplitContainerAdapter`

**提取方案**：

```
  WinForms/Abstractions/                      Shared/Abstractions/
    IWinFormsControlAdapter.cs  ──→               IControlAdapter.cs (通用部分)
                                                   ISplitContainerAdapter.cs (SplitContainer 特有)
```

**具体步骤**：

1. 创建 `IControlAdapter`：

```csharp
// Shared/Abstractions/IControlAdapter.cs
namespace FrameworkAbstractions.Controls;

public interface IControlAdapter
{
    // 布局
    void SetBounds(object control, int left, int top, int width, int height);
    int GetLeft(object control);
    int GetTop(object control);
    int GetWidth(object control);
    int GetHeight(object control);
    object? GetParent(object control);

    // 样式
    void SetFont(object control, string fontFamily, float fontSize, bool bold, bool italic, bool underline);
    void SetForeColor(object control, string htmlColor);
    void SetBackColor(object control, string htmlColor);

    // 控件树
    void AddControl(object parent, object child);
    void RemoveControl(object parent, object child);
    void ClearControls(object parent);

    // 属性
    string GetText(object control);
    void SetText(object control, string text);
    bool GetEnabled(object control);
    void SetEnabled(object control, bool enabled);
    bool GetVisible(object control);
    void SetVisible(object control, bool visible);
    string? GetName(object control);

    // 容器
    IEnumerable GetControls(object parent);
    object? FindForm(object control);
    bool IsDisposed(object control);
    bool GetInvokeRequired(object control);
    void InvokeOnUiThread(object control, Action action);
}
```

2. `IWinFormsControlAdapter` 改为继承：

```csharp
public interface IWinFormsControlAdapter : IControlAdapter
{
    // SplitContainer 特有操作
    int GetSplitterDistance(object splitContainer);
    void SetSplitterDistance(object splitContainer, int distance);
    string GetOrientation(object splitContainer); // string 避免 WinForms 依赖
    object? GetSplitterPanel(object splitContainer, string panelSide);
    Action SubscribeSplitterMoved(object splitContainer, EventHandler handler);
    void UnsubscribeSplitterMoved(object splitContainer, EventHandler handler);
}
```

**影响范围**：
- 新增文件：`Shared/FrameworkAbstractions/Controls/IControlAdapter.cs`
- 修改文件：`WinForms/Abstractions/IWinFormsControlAdapter.cs`、`WinForms/Adapter/WinFormsControlAdapter.cs`（实现新接口）
- 预计工作量：~1h

---

### 3.4 `ConfigContainerExtensions` + `ContainerFinder.FindAndBindMainContainers` → 解耦到接口

**当前状态**：
`ConfigContainerExtensions.FindConfigSelectorControls` 和 `ContainerFinder.FindAndBindMainContainers` 是纯委托方法，通过 `EcsConfigContainer.FindControl<T>` 查找控件。这些方法的业务逻辑（"查找哪些控件"）是配置相关的，而非 WinForms 特有的。

**提取方案**：

1. 定义 `IContainerControlFinder` 接口：

```csharp
// 在 Shared/Abstractions/
public interface IContainerControlFinder
{
    T? FindControl<T>(string name) where T : class;
}
```

2. 扩展方法改为接口扩展：

```csharp
// WinForms 保留
public static class ConfigContainerExtensions
{
    public static void FindConfigSelectorControls(
        this IContainerControlFinder contentContainer,
        out ListView? configListView, ...)
    {
        configListView = contentContainer.FindControl<ListView>("configListView");
        // ...
    }
}
```

**影响范围**：
- 新增文件：`Shared/Abstractions/IContainerControlFinder.cs`
- 修改文件：`WinForms/Container/ConfigContainerExtensions.cs`、`WinForms/Container/ContainerFinder.cs`
- 预计工作量：~0.5h

---

## 四、提取后的目录结构

### 4.1 新增文件清单

```
Shared/
  Tree/
    TreeIndexWrapper.cs               ← 2.1: 通用树索引包装
  Threading/
    AsyncHelper.cs                    ← 2.4: 通用异步辅助
    SynchronizedAccessor.cs            ← 2.5: 线程安全访问器
  Patterns/
    TypedAdapterRegistry.cs            ← 3.2: 类型适配器注册表
  Utils/
    SafeEnumerator.cs                  ← 2.2: 安全枚举器
  Abstractions/
    IContainerControlFinder.cs         ← 3.4: 容器控件查找接口
    IListSelectionAdapter.cs           ← 2.3: 列表选择操作接口 (从 WinForms 移入)
    IConfigSelectorView.cs             ← 3.1: 配置选择器视图接口
  Services/
    ConfigSelectorService.cs           ← 3.1: 配置选择器业务逻辑
  FrameworkAbstractions/
    Controls/
      IControlAdapter.cs               ← 3.3: 通用控件适配器接口
      IListSelectionAdapter.cs          ← 2.3: (如果选择放到这里而非 Shared/Abstractions/)
```

### 4.2 修改的 WinForms 文件

```
WinForms/
  Finder/
    ControlIndex.cs                    ← 2.1: 继承 TreeIndexWrapper<Control, string>
    ControlFinder.cs                   ← 2.2: 使用 SafeEnumerator
  Utils/
    UiThreadHelper.cs                  ← 2.4: 委托给 AsyncHelper
  Container/
    ContainerRegistry.cs               ← 2.5: 使用 SynchronizedAccessor<ControlIndex>
  Abstractions/
    IListSelectionAdapter.cs            ← 2.3: 删除(移动到 Shared) 或 保留别名
    IWinFormsControlAdapter.cs          ← 3.3: 继承 IControlAdapter
  Adapter/
    ListSelectionAdapter.cs             ← 2.3: 更新 using
    WinFormsControlAdapter.cs           ← 3.3: 实现 IControlAdapter + IWinFormsControlAdapter
  ValueSync/
    WinFormsControlValueWriter.cs       ← 3.2: 使用 TypedAdapterRegistry<AdapterEntry>
  ConfigSelectorController.cs          ← 3.1: 实现 IConfigSelectorView + 委托
```

### 4.3 不受影响的 WinForms 文件（保持原位）

```
CommandHandler.cs             强绑定 TextBox/KeyPressEventArgs
LayoutJsonExporter.cs         引用 WinFormsUIInspector
StandardControlStrategies.cs  注册 new Button() 等实例
UIInspector.cs                整文件依赖 Control/Point/Size/Form
WinFormsEventAdapter.cs       操作 ECS ControlPool
WinFormsUIAdapter.cs          桥接 CSG 协议
ContainerFinder.cs            操作 EcsConfigContainer (但重构方法签名)
UILogger.cs                   Control.InvokeRequired
UIEventBinder.cs              操作具体控件事件
ControlRegistryAwareFinder.cs 依赖 ControlPool + EntityTreeService
WinFormsEventRegistrar.cs     操作 EcsConfigContainer
EntityTreeService.cs          依赖 EntityManager + ControlPool
FileDrivenWdUIBridge.cs       操作 EcsConfigContainer
UnifiedControlService.cs      已通过接口注入解耦
WinFormsControlMetadataStore.cs 操作 Control.Tag
ControlMapAdapter.cs          操作 ControlPool
```

---

## 五、实施优先级与影响评估

| 优先级 | 重构项 | 预计工时 | 风险 | 收益 |
|--------|--------|----------|------|------|
| **P0** | 2.1 TreeIndexWrapper | 0.5h | 低（有测试可验证） | 消除 80 行重复，复用泛型索引 |
| **P0** | 2.3 IListSelectionAdapter 移动 | 0.2h | 极低（纯移动） | 接口可在非 WinForms 项目引用 |
| **P1** | 2.4 AsyncHelper 提取 | 0.5h | 低（纯新增，不改现有行为） | 通用异步方法可被任意项目使用 |
| **P1** | 2.5 SynchronizedAccessor | 0.3h | 低 | 消除 lock 样板代码 |
| **P1** | 2.2 SafeEnumerator | 0.3h | 低 | 消除 try-catch 遍历模式重复 |
| **P2** | 3.2 TypedAdapterRegistry | 0.5h | 中（影响适配器注册逻辑） | 适配器模式可复用 |
| **P2** | 3.1 ConfigSelectorService | 1h | 中（需要接口适配） | 文件扫描+列表构建可单元测试 |
| **P2** | 3.3 IControlAdapter | 1h | 中（修改接口继承体系） | 通用控件操作抽象 |
| **P3** | 3.4 IContainerControlFinder | 0.5h | 低 | 扩展方法可适用更多容器类型 |

**总计**：约 **4.8h** 工作量，预计删除/减少重复代码约 **250 行**。

---

## 六、风险与注意事项

1. **不破坏现有构建**：每个提取步骤应独立可编译，分步实施而非一次性大改
2. **命名空间兼容**：提取到 Shared 的类型应使用 `FrameworkAbstractions.*` 命名空间，保持与现有 `FrameworkAbstractions` 项目一致
3. **`Shared` 项目依赖**：确认 `Shared.csproj` 的 target framework（当前 `net10.0-windows`）——如果不需要 `windows` 特化，新建无 `-windows` 后缀的项目文件
4. **`FrameworkAbstractions` 已有 `UseWindowsForms`**：新的泛型代码应放到不依赖 WinForms 的新项目中，或确认 `Shared.csproj` 能否去掉 `-windows`
5. **`IWinFormsControlAdapter` 的回退路径**：提取 `IControlAdapter` 后，`WinFormsReflectionExtractor` 中的反射回退路径应标记为废弃（已有 `_adapter != null` 分支）
6. **重构后清理**：所有提取完成后，全局搜索不再使用的 WinForms 引用并移除冗余的 using 声明