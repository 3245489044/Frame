# WdUI 缓存逻辑通用化提取方案

## 一、概述

WdUI 布局框架中存在多种缓存逻辑，它们解决的是通用问题（弱引用状态管理、去重、键值缓存、树结构缓存），不依赖 WdUI 的业务类型。本文分析将这些缓存逻辑提取到 `BasicMethod` 作为项目级通用基础设施的方案。

---

## 二、WdUI 中现有缓存逻辑全景

### 2.1 LayoutState — 控件级运行时状态

| 项目 | 描述 |
|------|------|
| **位置** | `WdUI/Layout/LayoutState.cs` |
| **基础设施** | `ConditionalWeakTable<Control, LayoutState>` |
| **键类型** | `Control`（WinForms 控件） |
| **值类型** | `LayoutState`（自定义状态对象） |
| **生命周期** | 控件存活期间，控件销毁时自动清理 |
| **存储内容** | `SplitterRatio`、`Config`、`IsResizeSubscribed`、`IsSplitterSubscribed` |

```csharp
// 当前实现（WdUI/Layout/LayoutState.cs）
public class LayoutState
{
    public double? SplitterRatio { get; set; }
    public ControlConfig? Config { get; set; }
    public bool IsResizeSubscribed { get; set; }
    public bool IsSplitterSubscribed { get; set; }
}

// 使用方式
context.LayoutContext.State.GetOrCreateValue(control).Config = config;
```

### 2.2 RenderSession — 渲染会话级缓存

| 项目 | 描述 |
|------|------|
| **位置** | `WdUI/Layout/RenderSession.cs` |
| **基础设施** | `Dictionary<Control, RenderSession>` |
| **键类型** | `Control` |
| **值类型** | `RenderSession`（会话对象） |
| **生命周期** | 渲染到重新渲染之间，手动清理 |
| **存储内容** | `Config`、`Context`、`Pipeline`、`Controls` |

```csharp
// 当前实现（WdUI/Layout/RenderPipeline.cs）
private readonly Dictionary<Control, RenderSession> _sessions = new();
```

### 2.3 CacheConfigStage — 配置树递归缓存

| 项目 | 描述 |
|------|------|
| **位置** | `WdUI/Layout/Pipeline/Stages/CacheConfigStage.cs` |
| **基础设施** | `ConditionalWeakTable<Control, LayoutState>` |
| **模式** | 递归遍历控件树，将配置关联到每个控件 |
| **生命周期** | 同 LayoutState |

```csharp
// 当前实现（CacheConfigStage.CacheChildConfigs）
private void CacheChildConfigs(Control parent, List<ControlConfig> controls, PipelineContext context)
{
    foreach (var controlConfig in controls)
    {
        var child = FindChildControlByName(parent, controlConfig.Name);
        if (child == null) continue;
        context.LayoutContext.State.GetOrCreateValue(child).Config = controlConfig;
        // 递归处理子控件...
    }
}
```

### 2.4 TrackResizeStage._resizeTracked — 去重缓存

| 项目 | 描述 |
|------|------|
| **位置** | `WdUI/Layout/Pipeline/Stages/TrackResizeStage.cs` |
| **基础设施** | `HashSet<Control>` |
| **用途** | 防止重复订阅 Resize 事件 |
| **生命周期** | 实例存活期间 |

```csharp
// 当前实现
private readonly HashSet<Control> _resizeTracked = new();
```

### 2.5 ConfigurationLoader._configCache — 配置文件缓存

| 项目 | 描述 |
|------|------|
| **位置** | `WdUI/UI/WdUI.Configuration.cs` |
| **基础设施** | `Dictionary<string, UIConfiguration>` |
| **键类型** | `string`（配置文件路径） |
| **值类型** | `UIConfiguration`（配置对象） |
| **生命周期** | 应用运行期间，手动清理 |

---

## 三、可提取的通用缓存模式

### 3.1 模式一：弱引用状态管理器（WeakStateManager）

**通用性：⭐⭐⭐（最高）**

`ConditionalWeakTable<TKey, TValue>` 是 .NET 提供的弱引用字典，键对象被 GC 回收时自动清理条目。这种模式适用于"给任意对象附加运行时状态"的场景。

**通用接口设计：**

```csharp
// BasicMethod/Common/WeakStateManager.cs
namespace BasicMethod.Common
{
    /// <summary>
    /// 弱引用状态管理器
    /// 为任意对象附加运行时状态，对象销毁时自动清理
    /// 使用 ConditionalWeakTable 实现，无内存泄漏风险
    /// </summary>
    /// <typeparam name="TKey">键类型（必须是引用类型）</typeparam>
    /// <typeparam name="TState">状态类型（必须是引用类型，建议使用 record 或 class）</typeparam>
    public class WeakStateManager<TKey, TState>
        where TKey : class
        where TState : class, new()
    {
        private readonly ConditionalWeakTable<TKey, TState> _table = new();

        /// <summary>获取或创建键关联的状态对象</summary>
        public TState GetOrCreate(TKey key) => _table.GetOrCreateValue(key);

        /// <summary>尝试获取键关联的状态对象</summary>
        public bool TryGet(TKey key, [MaybeNullWhen(false)] out TState state)
            => _table.TryGetValue(key, out state);

        /// <summary>移除键关联的状态对象</summary>
        public void Remove(TKey key) => _table.Remove(key);

        /// <summary>清空所有状态</summary>
        public void Clear() => _table.Clear();
    }
}
```

**WdUI 中的使用方式：**

```csharp
// 改造后：LayoutContext 使用 WeakStateManager
public class LayoutContext : ILayoutContext
{
    private readonly WeakStateManager<Control, LayoutState> _stateManager = new();
    
    // 对外暴露的 State 属性（兼容旧代码）
    public ConditionalWeakTable<Control, LayoutState> State => _stateManager.InnerTable;
    
    public LayoutState GetOrCreateState(Control control) => _stateManager.GetOrCreate(control);
}
```

**其他潜在使用场景：**
- `WorkFlow` 中为工作流节点附加运行时状态
- `DataManage` 中为数据记录附加缓存
- `CellFrame` 中为单元格附加临时数据

---

### 3.2 模式二：去重标志管理器（DedupFlags）

**通用性：⭐⭐⭐（最高）**

"防止重复操作"是极其常见的需求。当前 WdUI 中有三种实现方式：
1. `LayoutState.IsResizeSubscribed` — 布尔标志
2. `LayoutState.IsSplitterSubscribed` — 布尔标志
3. `TrackResizeStage._resizeTracked` — HashSet

**通用接口设计：**

```csharp
// BasicMethod/Common/DedupFlags.cs
namespace BasicMethod.Common
{
    /// <summary>
    /// 去重标志管理器
    /// 防止对同一对象重复执行操作（如事件订阅、初始化等）
    /// 使用 ConditionalWeakTable 实现，对象销毁时自动清理
    /// </summary>
    /// <typeparam name="TKey">键类型（必须是引用类型）</typeparam>
    public class DedupFlags<TKey> where TKey : class
    {
        private readonly ConditionalWeakTable<TKey, object> _flags = new();
        private static readonly object _flag = new();

        /// <summary>
        /// 尝试设置标志
        /// <para>如果已设置返回 false，否则设置并返回 true</para>
        /// </summary>
        public bool TrySet(TKey key)
        {
            if (_flags.TryGetValue(key, out _)) return false;
            _flags.Add(key, _flag);
            return true;
        }

        /// <summary>检查标志是否已设置</summary>
        public bool IsSet(TKey key) => _flags.TryGetValue(key, out _);

        /// <summary>重置标志</summary>
        public void Reset(TKey key) => _flags.Remove(key);

        /// <summary>清空所有标志</summary>
        public void Clear() => _flags.Clear();
    }
}
```

**WdUI 中的使用方式：**

```csharp
// 改造后：TrackResizeStage 使用 DedupFlags
public class TrackResizeStage : IPipelineStage
{
    private readonly DedupFlags<Control> _resizeTracked = new();
    
    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        if (!_resizeTracked.TrySet(target)) return; // 已订阅，跳过
        
        target.Resize += (s, e) =>
        {
            if (s is Control c) _renderPipeline.Relayout(c);
        };
    }
}

// 改造后：SplitContainerStrategy 使用 DedupFlags
public class SplitContainerStrategy : ILayoutStrategy
{
    private readonly DedupFlags<SplitContainer> _splitterSubscribed = new();
    
    public void Execute(Control control, ControlConfig config, ILayoutContext context)
    {
        if (control is not SplitContainer splitter) return;
        
        // 订阅 SplitterMoved（自动去重）
        if (_splitterSubscribed.TrySet(splitter))
        {
            splitter.SplitterMoved += (sender, e) => { /* ... */ };
        }
    }
}
```

**其他潜在使用场景：**
- 防止重复初始化
- 防止重复事件订阅
- 防止重复数据加载
- 防止重复动画触发

---

### 3.3 模式三：通用内存缓存（MemoryCache）

**通用性：⭐⭐（中）**

`Dictionary<TKey, TValue>` / `ConcurrentDictionary<TKey, TValue>` 的通用包装。

**通用接口设计：**

```csharp
// BasicMethod/Common/MemoryCache.cs
namespace BasicMethod.Common
{
    /// <summary>
    /// 通用内存缓存
    /// 线程安全的键值缓存，支持工厂模式创建值
    /// </summary>
    /// <typeparam name="TKey">键类型</typeparam>
    /// <typeparam name="TValue">值类型</typeparam>
    public class MemoryCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TValue> _cache = new();

        /// <summary>获取或添加缓存项</summary>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
            => _cache.GetOrAdd(key, factory);

        /// <summary>尝试获取缓存项</summary>
        public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
            => _cache.TryGetValue(key, out value);

        /// <summary>移除缓存项</summary>
        public void Remove(TKey key) => _cache.TryRemove(key, out _);

        /// <summary>清空所有缓存</summary>
        public void Clear() => _cache.Clear();

        /// <summary>当前缓存项数量</summary>
        public int Count => _cache.Count;
    }
}
```

**WdUI 中的使用方式：**

```csharp
// 改造后：RenderPipeline 使用 MemoryCache
public class RenderPipeline
{
    private readonly MemoryCache<Control, RenderSession> _sessions = new();
    
    public void Render(Control target, UIConfiguration config)
    {
        DisposeSession(target);
        var session = new RenderSession(config, context, _renderPipeline);
        _sessions.GetOrAdd(target, _ => session);
        // ...
    }
}
```

---

### 3.4 模式四：树结构缓存器（TreeCache）

**通用性：⭐⭐（中）**

递归遍历树结构，将数据关联到每个节点的通用模式。

**通用接口设计：**

```csharp
// BasicMethod/Common/TreeCache.cs
namespace BasicMethod.Common
{
    /// <summary>
    /// 树结构缓存器
    /// 递归遍历树结构，为每个节点缓存关联数据
    /// </summary>
    /// <typeparam name="TNode">节点类型</typeparam>
    /// <typeparam name="TData">数据类型</typeparam>
    public class TreeCache<TNode, TData>
        where TNode : class
        where TData : class
    {
        private readonly ConditionalWeakTable<TNode, TData> _cache = new();
        private readonly Func<TNode, IEnumerable<TNode>> _childrenSelector;

        public TreeCache(Func<TNode, IEnumerable<TNode>> childrenSelector)
        {
            _childrenSelector = childrenSelector;
        }

        /// <summary>
        /// 递归缓存树中所有节点的数据
        /// </summary>
        /// <param name="root">根节点</param>
        /// <param name="dataFactory">为每个节点创建数据的工厂</param>
        public void CacheRecursive(TNode root, Func<TNode, TData> dataFactory)
        {
            var data = dataFactory(root);
            _cache.AddOrUpdate(root, data); // 注意：ConditionalWeakTable 没有 AddOrUpdate，需要扩展
            foreach (var child in _childrenSelector(root))
            {
                CacheRecursive(child, dataFactory);
            }
        }

        /// <summary>尝试获取节点的缓存数据</summary>
        public bool TryGet(TNode node, [MaybeNullWhen(false)] out TData data)
            => _cache.TryGetValue(node, out data);
    }
}
```

**WdUI 中的使用方式：**

```csharp
// 改造后：CacheConfigStage 使用 TreeCache
public class CacheConfigStage : IPipelineStage
{
    private static readonly TreeCache<Control, ControlConfig> _configCache = new(
        parent => parent.Controls.Cast<Control>()
    );
    
    public void Execute(Control target, UIConfiguration config, PipelineContext context)
    {
        // 递归缓存所有控件的配置
        _configCache.CacheRecursive(target, control =>
        {
            // 从 config.Views 中查找对应的 ControlConfig
            return FindConfigForControl(config, control);
        });
    }
}
```

---

## 四、提取方案对比

| 模式 | 通用性 | 复杂度 | 依赖 | 收益 | 优先级 |
|------|--------|--------|------|------|--------|
| `WeakStateManager` | ⭐⭐⭐ | 低 | 无 | 高 | **P0** |
| `DedupFlags` | ⭐⭐⭐ | 低 | 无 | 高 | **P0** |
| `MemoryCache` | ⭐⭐ | 低 | 无 | 中 | P1 |
| `TreeCache` | ⭐⭐ | 中 | `WeakStateManager` | 中 | P1 |

### 推荐提取顺序

1. **P0（立即提取）**：`WeakStateManager` + `DedupFlags`
   - 最通用、最独立、收益最高
   - 不依赖任何业务类型
   - 可以立即被 WdUI 和其他模块使用

2. **P1（后续提取）**：`MemoryCache` + `TreeCache`
   - 通用性中等
   - `TreeCache` 依赖 `WeakStateManager`
   - 需要更多使用场景验证

---

## 五、提取后的项目结构

```
BasicMethod/
  Common/
    WeakStateManager.cs    ← 通用弱引用状态管理器
    DedupFlags.cs          ← 通用去重标志管理器
    MemoryCache.cs         ← 通用内存缓存（P1）
    TreeCache.cs           ← 通用树结构缓存器（P1）

WdUI/
  Layout/
    LayoutState.cs         ← 保留，继承 WeakStateManager 或使用其服务
    LayoutContext.cs       ← 使用 WeakStateManager
    Pipeline/Stages/
      CacheConfigStage.cs  ← 使用 WeakStateManager
      TrackResizeStage.cs  ← 使用 DedupFlags
    Strategies/
      SplitContainerStrategy.cs ← 使用 DedupFlags
```

---

## 六、迁移步骤

### 步骤 1：创建 WeakStateManager

```csharp
// BasicMethod/Common/WeakStateManager.cs
namespace BasicMethod.Common
{
    public class WeakStateManager<TKey, TState> where TKey : class where TState : class, new()
    {
        private readonly ConditionalWeakTable<TKey, TState> _table = new();
        public TState GetOrCreate(TKey key) => _table.GetOrCreateValue(key);
        public bool TryGet(TKey key, out TState state) => _table.TryGetValue(key, out state);
        public void Remove(TKey key) => _table.Remove(key);
        public void Clear() => _table.Clear();
    }
}
```

### 步骤 2：创建 DedupFlags

```csharp
// BasicMethod/Common/DedupFlags.cs
namespace BasicMethod.Common
{
    public class DedupFlags<TKey> where TKey : class
    {
        private readonly ConditionalWeakTable<TKey, object> _flags = new();
        private static readonly object _flag = new();
        public bool TrySet(TKey key) { /* ... */ }
        public bool IsSet(TKey key) { /* ... */ }
        public void Reset(TKey key) { /* ... */ }
        public void Clear() { /* ... */ }
    }
}
```

### 步骤 3：改造 WdUI 使用通用缓存

- `LayoutContext.State` → 使用 `WeakStateManager<Control, LayoutState>`
- `TrackResizeStage._resizeTracked` → 使用 `DedupFlags<Control>`
- `SplitContainerStrategy.IsSplitterSubscribed` → 使用 `DedupFlags<SplitContainer>`

### 步骤 4：构建验证

- 确保 `BasicMethod` 构建成功
- 确保 `WdUI` 构建成功
- 确保 `FileDriven.UI` 构建成功

---

## 七、注意事项

1. **ConditionalWeakTable 的线程安全**：`GetOrCreateValue` 是线程安全的，但 `Add` 不是。如果需要在多线程环境下使用 `Add`，需要加锁或使用 `GetOrCreateValue` 替代。

2. **ConditionalWeakTable 没有 AddOrUpdate**：如果需要更新已存在的值，需要先 `Remove` 再 `Add`，或者使用 `GetOrCreateValue` 获取后修改其属性。

3. **DedupFlags 的线程安全**：`TrySet` 中的 `TryGetValue` + `Add` 不是原子操作。如果需要在多线程环境下使用，需要加锁或使用 `ConcurrentDictionary` 替代。

4. **向后兼容**：提取后需要保持 WdUI 的公共 API 不变，避免破坏现有代码。

---

## 八、总结

| 缓存模式 | 提取优先级 | 通用性 | 当前使用场景 | 潜在使用场景 |
|---------|-----------|--------|-------------|-------------|
| WeakStateManager | **P0** | ⭐⭐⭐ | LayoutState | 任意对象附加运行时状态 |
| DedupFlags | **P0** | ⭐⭐⭐ | 事件订阅去重 | 初始化去重、加载去重 |
| MemoryCache | P1 | ⭐⭐ | RenderSession、配置缓存 | 通用键值缓存 |
| TreeCache | P1 | ⭐⭐ | 配置树递归缓存 | 树结构数据关联 |

**核心建议**：先提取 `WeakStateManager` 和 `DedupFlags` 到 `BasicMethod/Common/`，这两个最通用、最独立、收益最高。后续根据实际需求再提取 `MemoryCache` 和 `TreeCache`。
