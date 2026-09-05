# Share.Registry 通用化更新重构方案

> 文档版本：v1.1  
> 更新日期：2026-06-12  
> 目标：将 Share.Registry 从 3 种模式扩展为 6 种模式，使其能够统一支撑 CommandFramework 中所有 5 个注册子系统以及项目中其他 10+ 个散落的注册表实现。

---

## 目录

1. [现状分析](#1-现状分析)
2. [差距分析](#2-差距分析)
3. [改进目标](#3-改进目标)
4. [接口体系设计](#4-接口体系设计)
5. [基类实现设计](#5-基类实现设计)
6. [增强基类设计](#6-增强基类设计)
7. [CommandFramework 迁移映射](#7-commandframework-迁移映射)
8. [项目全局注册表迁移清单](#8-项目全局注册表迁移清单)
9. [实施步骤](#9-实施步骤)
10. [迁移指南](#10-迁移指南)
11. [附录：文件清单](#11-附录文件清单)

---

## 1. 现状分析

### 1.1 Share.Registry 现有结构

当前 `Share/Registry` 提供了 **3 种注册表模式**：

| 模式 | 接口 | 基类 | 覆盖实例数 |
|------|------|------|-----------|
| **A: 键值映射** | `IMapRegistry<TKey, TValue>` | `MapRegistryBase<TKey, TValue>` | 7 |
| **B: 阶段管理** | `IStageRegistry<TStage, TValue>` | `StageRegistryBase<TStage, TValue>` | 2 |
| **C: 图结构** | `IGraphRegistry<TNodeId, TNode, TEdge>` | `GraphRegistryBase<TNodeId, TNode, TEdge>` | 1 |

**现有接口继承体系：**

```
IRegistry (Clear)
  │
  ├── IMapRegistry<TKey, TValue>     — 键值映射
  ├── IStageRegistry<TStage, TValue> — 阶段管理
  └── IGraphRegistry<TNodeId, TNode, TEdge> — 图结构
```

### 1.2 CommandFramework 中的 5 个注册子系统

通过对 `Share/CommandFramework` 的全面分析，发现其中有 **5 个独立的注册子系统**：

| # | 注册子系统 | 核心类 | 存储结构 | 注册内容 |
|---|-----------|--------|---------|---------|
| 1 | 命令注册表（工厂模式） | `CommandRegistry` | `ConcurrentDictionary<string, Func<IServiceProvider?, ICommand>>` | 命令工厂委托 |
| 2 | 命令引擎注册 | `CommandEngine` | `Dictionary<string, CommandRegistration>` | 执行/撤销委托 |
| 3 | 函数注册表 | `FunctionRegistry` | `ConcurrentDictionary<string, RegisteredFunction>` | Delegate + 元数据 |
| 4 | 非事件命令处理器注册 | `NonEventCommandHandler` | `Dictionary<string, Func<ParsedCommand, Task<...>>>` | 命令处理函数 |
| 5 | 解析器注册 | `CommandParser` | `HashSet<string>` + `Dictionary<string, string>` | 命令名集合 + 规范化映射 |

---

## 2. 差距分析

### 2.1 现有 Registry 无法覆盖的 6 个关键差距

#### 差距 1：缺少"集合模式"（Set）

**场景：** `CommandParser._nonEventCommands` 是 `HashSet<string>`，只有键没有值。

**现有限制：** `IMapRegistry` 要求同时有键和值，不适合纯集合场景。

**影响范围：** `CommandParser`、`IStateStore.GetAllKeys()`、`ICommandEngine.GetRegisteredCommands()` 等。

---

#### 差距 2：缺少"工厂模式"（Factory）

**场景：** `CommandRegistry` 的值是 `Func<IServiceProvider?, ICommand>`，每次 `Resolve()` 调用工厂创建新实例。

**现有限制：** `MapRegistryBase` 只支持直接存取值，不支持延迟创建和依赖注入。

**影响范围：** `CommandRegistry`、任何需要 DI 的延迟创建场景。

---

#### 差距 3：缺少"带元数据的键值映射"（MetaMap）

**场景：** `FunctionRegistry` 的 `RegisteredFunction` 包含 `Delegate` + `FunctionDescriptor`（参数信息、返回类型等）。

**现有限制：** `MapRegistryBase` 只存单一值类型，不支持附加元数据。

**影响范围：** `FunctionRegistry`、任何需要值+描述信息的注册场景。

---

#### 差距 4：缺少"事件通知"（Observable）

**场景：** `CommandFrameworkUsage` 中有 `StateChanged` 事件，注册表变更时需要通知外部。

**现有限制：** `MapRegistryBase` 没有注册/注销/变更事件通知机制。

**影响范围：** 需要 UI 绑定、日志记录、缓存失效等场景。

---

#### 差距 5：缺少"批量操作"（Batch）

**场景：** `CommandEngineExtensions.RegisterHandlers()` 支持批量注册。

**现有限制：** `MapRegistryBase` 没有 `RegisterRange()`、`UnregisterRange()` 方法。

**影响范围：** 所有需要批量初始化注册的场景。

---

#### 差距 6：缺少"查询/过滤"（Query）

**场景：** 需要按条件过滤已注册的值、获取所有值列表。

**现有限制：** `MapRegistryBase` 只有 `TryGet`/`Contains`/`GetKeys`，缺少 `GetValues()`、`Filter()`。

**影响范围：** 调试、诊断、动态查询场景。

---

### 2.2 各注册子系统的模式归属与差距矩阵

| 注册子系统 | 可映射的模式 | 差距项 |
|-----------|------------|--------|
| `CommandRegistry` | A: 键值映射 | **差距 2**：需要工厂模式 |
| `CommandEngine._commands` | A: 键值映射 | 基本匹配（内部类需适配） |
| `FunctionRegistry` | A: 键值映射 | **差距 3**：需要元数据 |
| `NonEventCommandHandler._handlers` | A: 键值映射 | 基本匹配 |
| `CommandParser._nonEventCommands` | — | **差距 1**：需要集合模式 |
| `CommandParser._normalizationMap` | A: 键值映射 | 基本匹配 |

---

## 3. 改进目标

### 3.1 总体目标

将 `Share.Registry` 从 3 种模式扩展为 **6 种模式**，使其能够：

1. **统一支撑** CommandFramework 中所有 5 个注册子系统
2. **覆盖** 项目中其他 10+ 个散落的注册表实现
3. **保持向后兼容**：不破坏现有接口和基类
4. **组合优于继承**：增强能力通过基类方法直接提供，而非额外接口

### 3.2 设计原则

- **单一职责**：每个接口只负责一种注册模式
- **开闭原则**：对扩展开放，对修改关闭
- **接口隔离**：客户端不应依赖它不需要的接口
- **基类优先**：批量操作、查询过滤等通用能力直接在基类中提供，避免接口碎片化

---

## 4. 接口体系设计

### 4.1 最终接口继承体系

```
IRegistry (Clear)                                          ← 已有
  │
  ├── ISetRegistry<TKey>                                   ← ★ 新增：纯集合
  │     └── Register / Contains / GetKeys / Unregister / Count
  │
  ├── IMapRegistry<TKey, TValue>                           ← 已有：键值映射
  │     └── Register / TryGet / Contains / GetKeys / Unregister
  │
  ├── IFactoryRegistry<TKey, TValue>                       ← ★ 新增：工厂模式
  │     └── Register(name, factory) / Resolve / Contains / GetKeys / Count
  │
  ├── IStageRegistry<TStage, TValue>                       ← 已有：阶段管理
  │     └── Register / InsertAt / Replace / Remove / GetValues
  │
  └── IGraphRegistry<TNodeId, TNode, TEdge>               ← 已有：图结构
        └── RegisterNode / RegisterEdge / TopologicalSort

// ─── 增强接口（可选组合，继承 IRegistry）───
INotifyRegistry<TKey, TValue> : IRegistry                  ← ★ 新增：事件通知
  └── ItemRegistered / ItemUpdated / ItemUnregistered / RegistryCleared
```

### 4.2 设计决策说明

| 决策 | 说明 |
|------|------|
| **不设 IBatchRegistry 接口** | 批量操作（RegisterRange/UnregisterRange）直接在 `MapRegistryBase` 基类中提供，避免每个实现类都需要额外实现接口 |
| **不设 IQueryableRegistry 接口** | 查询过滤（GetValues/Filter/FilterEntries）直接在 `MapRegistryBase` 基类中提供 |
| **INotifyRegistry 继承 IRegistry** | 保持与其他接口一致，都继承自 `IRegistry` |
| **IFactoryRegistry 不设 Register<T>()** | 避免 `TKey` 类型转换问题，只保留 `Register(name, factory)` 工厂委托注册 |

### 4.3 新增接口详细定义

#### 4.3.1 ISetRegistry — 集合注册表

```csharp
/// <summary>
/// 集合注册表
/// <para>只有键没有值，适用于"白名单/黑名单/命令名集合"等纯集合场景</para>
/// <para>覆盖：CommandParser._nonEventCommands</para>
/// </summary>
/// <typeparam name="TKey">键类型</typeparam>
public interface ISetRegistry<TKey> : IRegistry
    where TKey : notnull
{
    /// <summary>注册键</summary>
    void Register(TKey key);

    /// <summary>判断是否包含指定键</summary>
    bool Contains(TKey key);

    /// <summary>获取所有已注册的键</summary>
    IEnumerable<TKey> GetKeys();

    /// <summary>注销指定键</summary>
    void Unregister(TKey key);

    /// <summary>获取已注册的键数量</summary>
    int Count { get; }
}
```

#### 4.3.2 IFactoryRegistry — 工厂注册表

```csharp
/// <summary>
/// 工厂注册表
/// <para>通过工厂委托延迟创建实例，支持依赖注入</para>
/// <para>覆盖：CommandRegistry</para>
/// </summary>
/// <typeparam name="TKey">键类型</typeparam>
/// <typeparam name="TValue">值类型（通常是接口或基类）</typeparam>
public interface IFactoryRegistry<TKey, TValue> : IRegistry
    where TKey : notnull
{
    /// <summary>注册工厂委托</summary>
    void Register(TKey name, Func<IServiceProvider?, TValue> factory);

    /// <summary>解析实例（延迟创建）</summary>
    TValue? Resolve(TKey name, IServiceProvider? serviceProvider = null);

    /// <summary>判断是否包含指定键</summary>
    bool Contains(TKey key);

    /// <summary>获取所有已注册的键</summary>
    IEnumerable<TKey> GetKeys();

    /// <summary>获取已注册的工厂数量</summary>
    int Count { get; }
}
```

#### 4.3.3 IMetaMapRegistry — 带元数据的键值映射

```csharp
/// <summary>
/// 带元数据的键值映射注册表
/// <para>在键值映射的基础上附加元数据描述信息</para>
/// <para>覆盖：FunctionRegistry（值=Delegate，元数据=FunctionDescriptor）</para>
/// </summary>
/// <typeparam name="TKey">键类型</typeparam>
/// <typeparam name="TValue">值类型</typeparam>
/// <typeparam name="TMeta">元数据类型</typeparam>
public interface IMetaMapRegistry<TKey, TValue, TMeta> : IRegistry
    where TKey : notnull
{
    /// <summary>注册键值对（带元数据）</summary>
    void Register(TKey key, TValue value, TMeta? metadata = default);

    /// <summary>尝试获取值和元数据</summary>
    bool TryGet(TKey key, out TValue? value, out TMeta? metadata);

    /// <summary>尝试获取元数据</summary>
    bool TryGetMetadata(TKey key, [MaybeNullWhen(false)] out TMeta metadata);

    /// <summary>判断是否包含指定键</summary>
    bool Contains(TKey key);

    /// <summary>获取所有已注册的键</summary>
    IEnumerable<TKey> GetKeys();

    /// <summary>注销指定键</summary>
    void Unregister(TKey key);

    /// <summary>获取已注册的数量</summary>
    int Count { get; }
}
```

#### 4.3.4 INotifyRegistry — 事件通知

```csharp
/// <summary>
/// 注册表变更事件参数
/// </summary>
public class RegistryChangedEventArgs<TKey, TValue> : EventArgs
{
    /// <summary>变更类型</summary>
    public RegistryChangeType ChangeType { get; init; }
    /// <summary>变更的键</summary>
    public TKey Key { get; init; } = default!;
    /// <summary>旧值（更新/移除前）</summary>
    public TValue? OldValue { get; init; }
    /// <summary>新值（注册/更新后）</summary>
    public TValue? NewValue { get; init; }
}

/// <summary>
/// 注册表变更类型
/// </summary>
public enum RegistryChangeType
{
    Registered,
    Updated,
    Unregistered,
    Cleared
}

/// <summary>
/// 可通知的注册表接口
/// <para>注册表变更时触发事件通知</para>
/// </summary>
public interface INotifyRegistry<TKey, TValue> : IRegistry
{
    /// <summary>项目注册事件</summary>
    event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemRegistered;

    /// <summary>项目更新事件</summary>
    event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemUpdated;

    /// <summary>项目注销事件</summary>
    event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemUnregistered;

    /// <summary>注册表清空事件</summary>
    event EventHandler? RegistryCleared;
}
```

---

## 5. 基类实现设计

### 5.1 新增基类

#### 5.1.1 SetRegistryBase — 集合注册表基类

```csharp
/// <summary>
/// 线程安全的集合注册表基类
/// <para>使用 ConcurrentDictionary{TKey, byte} 实现</para>
/// </summary>
public class SetRegistryBase<TKey> : ISetRegistry<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, byte> _store;

    public SetRegistryBase(IEqualityComparer<TKey>? comparer = null)
    {
        _store = new ConcurrentDictionary<TKey, byte>(
            comparer ?? EqualityComparer<TKey>.Default);
    }

    /// <summary>内部存储（供子类扩展访问）</summary>
    protected ConcurrentDictionary<TKey, byte> Store => _store;

    // ─── 生命周期回调 ───
    protected virtual void OnBeforeRegister(TKey key) { }
    protected virtual void OnBeforeUnregister(TKey key) { }
    protected virtual void OnBeforeClear() { }

    public virtual void Register(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        OnBeforeRegister(key);
        _store[key] = 0;
    }

    public bool Contains(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _store.ContainsKey(key);
    }

    public IEnumerable<TKey> GetKeys() => _store.Keys;

    public int Count => _store.Count;

    public virtual void Unregister(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        OnBeforeUnregister(key);
        _store.TryRemove(key, out _);
    }

    public virtual void Clear()
    {
        OnBeforeClear();
        _store.Clear();
    }
}
```

#### 5.1.2 FactoryRegistryBase — 工厂注册表基类

```csharp
/// <summary>
/// 线程安全的工厂注册表基类
/// <para>使用 ConcurrentDictionary{TKey, Func{IServiceProvider?, TValue}} 实现</para>
/// </summary>
public class FactoryRegistryBase<TKey, TValue> : IFactoryRegistry<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Func<IServiceProvider?, TValue>> _factories;

    public FactoryRegistryBase(IEqualityComparer<TKey>? comparer = null)
    {
        _factories = new ConcurrentDictionary<TKey, Func<IServiceProvider?, TValue>>(
            comparer ?? EqualityComparer<TKey>.Default);
    }

    /// <summary>内部存储（供子类扩展访问）</summary>
    protected ConcurrentDictionary<TKey, Func<IServiceProvider?, TValue>> Factories => _factories;

    // ─── 生命周期回调 ───
    protected virtual void OnBeforeRegister(TKey key) { }
    protected virtual void OnBeforeUnregister(TKey key) { }
    protected virtual void OnBeforeClear() { }

    public virtual void Register(TKey name, Func<IServiceProvider?, TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(factory);
        OnBeforeRegister(name);
        _factories[name] = factory;
    }

    public TValue? Resolve(TKey name, IServiceProvider? serviceProvider = null)
    {
        if (_factories.TryGetValue(name, out var factory))
        {
            try { return factory(serviceProvider); }
            catch { return default; }
        }
        return default;
    }

    public bool Contains(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _factories.ContainsKey(key);
    }

    public IEnumerable<TKey> GetKeys() => _factories.Keys;

    public int Count => _factories.Count;

    public virtual void Unregister(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        OnBeforeUnregister(key);
        _factories.TryRemove(key, out _);
    }

    public virtual void Clear()
    {
        OnBeforeClear();
        _factories.Clear();
    }
}
```

#### 5.1.3 MetaMapRegistryBase — 带元数据的键值映射基类

```csharp
/// <summary>
/// 线程安全的带元数据键值映射注册表基类
/// <para>内部使用两个 ConcurrentDictionary 分别存储值和元数据</para>
/// </summary>
public class MetaMapRegistryBase<TKey, TValue, TMeta> : IMetaMapRegistry<TKey, TValue, TMeta>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _values;
    private readonly ConcurrentDictionary<TKey, TMeta> _metadatas;

    public MetaMapRegistryBase(IEqualityComparer<TKey>? comparer = null)
    {
        var eq = comparer ?? EqualityComparer<TKey>.Default;
        _values = new ConcurrentDictionary<TKey, TValue>(eq);
        _metadatas = new ConcurrentDictionary<TKey, TMeta>(eq);
    }

    /// <summary>值存储（供子类扩展访问）</summary>
    protected ConcurrentDictionary<TKey, TValue> Values => _values;
    /// <summary>元数据存储（供子类扩展访问）</summary>
    protected ConcurrentDictionary<TKey, TMeta> Metadatas => _metadatas;

    // ─── 生命周期回调 ───
    protected virtual void OnBeforeRegister(TKey key, TValue value, TMeta? metadata) { }
    protected virtual void OnBeforeUnregister(TKey key) { }
    protected virtual void OnBeforeClear() { }

    public virtual void Register(TKey key, TValue value, TMeta? metadata = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        OnBeforeRegister(key, value, metadata);
        _values[key] = value;
        if (metadata is not null)
            _metadatas[key] = metadata;
    }

    public bool TryGet(TKey key, out TValue? value, out TMeta? metadata)
    {
        ArgumentNullException.ThrowIfNull(key);
        var found = _values.TryGetValue(key, out var v);
        value = v;
        if (found)
            _metadatas.TryGetValue(key, out metadata);
        else
            metadata = default;
        return found;
    }

    public bool TryGetMetadata(TKey key, [MaybeNullWhen(false)] out TMeta metadata)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _metadatas.TryGetValue(key, out metadata);
    }

    public bool Contains(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _values.ContainsKey(key);
    }

    public IEnumerable<TKey> GetKeys() => _values.Keys;

    public int Count => _values.Count;

    public virtual void Unregister(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        OnBeforeUnregister(key);
        _values.TryRemove(key, out _);
        _metadatas.TryRemove(key, out _);
    }

    public virtual void Clear()
    {
        OnBeforeClear();
        _values.Clear();
        _metadatas.Clear();
    }
}
```

### 5.2 现有基类增强

#### 5.2.1 MapRegistryBase 增强

在现有 `MapRegistryBase<TKey, TValue>` 中新增以下方法，**直接在基类中提供**批量操作和查询过滤能力，无需额外接口：

```csharp
// ─── 新增：批量操作 ───

/// <summary>批量注册</summary>
public virtual void RegisterRange(IEnumerable<KeyValuePair<TKey, TValue>> items)
{
    ArgumentNullException.ThrowIfNull(items);
    foreach (var kvp in items)
        Register(kvp.Key, kvp.Value);
}

/// <summary>批量注销</summary>
public virtual void UnregisterRange(IEnumerable<TKey> keys)
{
    ArgumentNullException.ThrowIfNull(keys);
    foreach (var key in keys)
        Unregister(key);
}

// ─── 新增：查询属性 ───

/// <summary>获取已注册的数量</summary>
public int Count => _store.Count;

// ─── 新增：查询方法 ───

/// <summary>获取所有值（快照）</summary>
public IReadOnlyCollection<TValue> GetValues()
    => _store.Values.ToList().AsReadOnly();

/// <summary>按条件过滤值（快照）</summary>
public IReadOnlyCollection<TValue> Filter(Func<TValue, bool> predicate)
{
    ArgumentNullException.ThrowIfNull(predicate);
    return _store.Values.Where(predicate).ToList().AsReadOnly();
}

/// <summary>按条件过滤键值对（快照）</summary>
public IReadOnlyCollection<KeyValuePair<TKey, TValue>> FilterEntries(
    Func<TKey, TValue, bool> predicate)
{
    ArgumentNullException.ThrowIfNull(predicate);
    return _store.Where(kvp => predicate(kvp.Key, kvp.Value))
                 .ToList().AsReadOnly();
}
```

---

## 6. 增强基类设计

### 6.1 ObservableMapRegistry — 可观察的键值映射注册表

```csharp
/// <summary>
/// 带事件通知的键值映射注册表
/// <para>继承 MapRegistryBase，重写生命周期回调触发事件</para>
/// <para>注意：事件在值实际写入/移除存储后触发，确保事件处理器能获取到最新状态</para>
/// </summary>
public class ObservableMapRegistry<TKey, TValue> : MapRegistryBase<TKey, TValue>,
    INotifyRegistry<TKey, TValue>
    where TKey : notnull
{
    public event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemRegistered;
    public event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemUpdated;
    public event EventHandler<RegistryChangedEventArgs<TKey, TValue>>? ItemUnregistered;
    public event EventHandler? RegistryCleared;

    public ObservableMapRegistry(
        IEqualityComparer<TKey>? comparer = null,
        bool allowOverwrite = true)
        : base(comparer, allowOverwrite) { }

    protected override void OnBeforeRegister(TKey key, TValue value)
    {
        base.OnBeforeRegister(key, value);
        // 注意：此时值尚未写入 Store，Store.ContainsKey 检查的是旧状态
        var isUpdate = Store.ContainsKey(key);
        var oldValue = isUpdate && Store.TryGetValue(key, out var old) ? old : default;

        if (isUpdate)
        {
            ItemUpdated?.Invoke(this, new RegistryChangedEventArgs<TKey, TValue>
            {
                ChangeType = RegistryChangeType.Updated,
                Key = key,
                OldValue = oldValue,
                NewValue = value
            });
        }
        else
        {
            ItemRegistered?.Invoke(this, new RegistryChangedEventArgs<TKey, TValue>
            {
                ChangeType = RegistryChangeType.Registered,
                Key = key,
                NewValue = value
            });
        }
    }

    protected override void OnBeforeUnregister(TKey key)
    {
        base.OnBeforeUnregister(key);
        // 注意：此时值尚未从 Store 移除
        var oldValue = Store.TryGetValue(key, out var old) ? old : default;
        ItemUnregistered?.Invoke(this, new RegistryChangedEventArgs<TKey, TValue>
        {
            ChangeType = RegistryChangeType.Unregistered,
            Key = key,
            OldValue = oldValue
        });
    }

    protected override void OnBeforeClear()
    {
        base.OnBeforeClear();
        // 注意：此时 Store 尚未清空
        RegistryCleared?.Invoke(this, EventArgs.Empty);
    }
}
```

### 6.2 接口组合关系图

```
                    ┌─────────────────────┐
                    │     IRegistry       │
                    │     (Clear)         │
                    └─────────┬───────────┘
                              │
              ┌───────────────┼───────────────┐
              │               │               │
     ┌────────┴────────┐ ┌───┴────┐ ┌────────┴────────┐
     │ IMapRegistry    │ │ISetReg │ │ IFactoryReg     │
     │ <TKey,TValue>   │ │<TKey>  │ │ <TKey,TValue>   │
     └────────┬────────┘ └────────┘ └────────┬────────┘
              │                              │
              │ 可选组合                     │ 可选组合
              │                              │
     ┌────────┴────────┐          ┌──────────┴──────────┐
     │ INotifyRegistry │          │ INotifyRegistry      │
     │ <TKey,TValue>   │          │ <TKey,TValue>        │
     └─────────────────┘          └─────────────────────┘

基类内置能力（无需额外接口）：
  MapRegistryBase     — RegisterRange / UnregisterRange / Count
                      — GetValues / Filter / FilterEntries
  FactoryRegistryBase — Count / GetKeys
  SetRegistryBase     — Count / GetKeys
```

### 6.3 组合使用示例

```csharp
// 1. 纯键值映射 + 事件通知
public class MyHandlerRegistry
    : ObservableMapRegistry<string, IHandler>
{
    // 直接继承，批量操作和查询过滤方法开箱即用
}

// 2. 工厂模式（最简）
public class MyCommandRegistry
    : FactoryRegistryBase<string, ICommand>
{
    // 直接继承，无需额外代码
}

// 3. 集合模式（最简）
public class MyCommandSet
    : SetRegistryBase<string>
{
    // 直接继承，无需额外代码
}

// 4. 带元数据的键值映射
public class MyFunctionRegistry
    : MetaMapRegistryBase<string, Delegate, FunctionDescriptor>
{
    // 直接继承，无需额外代码
}
```

---

## 7. CommandFramework 迁移映射

### 7.1 迁移对照表

| 现有类 | 当前存储 | 目标基类/接口 | 变更类型 |
|--------|---------|--------------|---------|
| `CommandRegistry` | `ConcurrentDictionary<string, Func<...>>` | `FactoryRegistryBase<string, ICommand>` | **替换实现** |
| `CommandEngine._commands` | `Dictionary<string, CommandRegistration>` | 内部保留，实现 `IMapRegistry<string, CommandRegistration>` | **显式接口实现** |
| `FunctionRegistry._functions` | `ConcurrentDictionary<string, RegisteredFunction>` | `MetaMapRegistryBase<string, Delegate, FunctionDescriptor>` | **替换实现** |
| `NonEventCommandHandler._handlers` | `Dictionary<string, Func<...>>` | `MapRegistryBase<string, Func<...>>` | **替换实现** |
| `CommandParser._nonEventCommands` | `HashSet<string>` | `SetRegistryBase<string>` | **替换实现** |
| `CommandParser._normalizationMap` | `Dictionary<string, string>` | `MapRegistryBase<string, string>` | **替换实现** |

### 7.2 迁移优先级

```
第一阶段（低风险，纯新增）：
  └─ 新增 ISetRegistry / IFactoryRegistry / IMetaMapRegistry 接口和基类
  └─ 增强 MapRegistryBase（添加 RegisterRange / Count / GetValues / Filter）
  └─ 新增 ObservableMapRegistry / INotifyRegistry

第二阶段（中风险，替换内部实现）：
  └─ CommandParser._nonEventCommands → SetRegistryBase<string>
  └─ CommandParser._normalizationMap → MapRegistryBase<string, string>
  └─ NonEventCommandHandler._handlers → MapRegistryBase<string, Func<...>>

第三阶段（高风险，涉及接口变更）：
  └─ CommandRegistry → FactoryRegistryBase<string, ICommand>
  └─ FunctionRegistry → MetaMapRegistryBase<string, Delegate, FunctionDescriptor>
  └─ CommandEngine → 显式接口实现 IMapRegistry
```

---

## 8. 项目全局注册表迁移清单

### 8.1 项目中散落的注册表实现

根据代码分析，项目中还有以下注册表可以迁移到 `Share.Registry`：

| 位置 | 现有实现 | 建议模式 |
|------|---------|---------|
| `BasicMethod/RefectionFacade/` | 反射缓存注册表 | A: 键值映射 |
| `BasicMethod/Pipeline/` | 管线阶段注册表 | B: 阶段管理 |
| `FileDriven/FileDriven.Pipeline/` | 管线入口注册表 | B: 阶段管理 |
| `Operation/FunctionStorage/` | 函数存储注册表 | A: 键值映射 |
| `Operation/EventTriggerFramework/` | 事件绑定注册表 | A: 键值映射 |
| `Share/UniversalMiddleware/` | 中间件注册表 | A: 键值映射 |
| `Share/PlaceholderFramework/` | 占位符注册表 | A: 键值映射 |
| `Share/Adapters/` | 适配器注册表 | A: 键值映射 |
| `Share/Component/` | 组件注册表 | A: 键值映射 |
| `DataStorage/DataBase/` | 数据存储注册表 | A: 键值映射 |

---

## 9. 实施步骤

### 9.1 实施路线图

```
Phase 1: 基础设施（1-2 天）
  ├── 新增 ISetRegistry.cs 接口 + SetRegistryBase.cs 基类
  ├── 新增 IFactoryRegistry.cs 接口 + FactoryRegistryBase.cs 基类
  ├── 新增 IMetaMapRegistry.cs 接口 + MetaMapRegistryBase.cs 基类
  ├── 新增 INotifyRegistry.cs 接口 + RegistryChangedEventArgs.cs
  ├── 增强 MapRegistryBase.cs（RegisterRange / Count / GetValues / Filter / FilterEntries）
  └── 新增 ObservableMapRegistry.cs

Phase 2: 单元测试（1 天）
  ├── SetRegistryBase 单元测试
  ├── FactoryRegistryBase 单元测试
  ├── MetaMapRegistryBase 单元测试
  ├── ObservableMapRegistry 单元测试
  └── MapRegistryBase 增强方法单元测试

Phase 3: CommandFramework 迁移（2-3 天）
  ├── CommandParser 迁移（低风险）
  ├── NonEventCommandHandler 迁移（低风险）
  ├── CommandRegistry 迁移（中风险）
  ├── FunctionRegistry 迁移（中风险）
  └── CommandEngine 显式接口实现（低风险）

Phase 4: 全局迁移（3-5 天）
  ├── 逐个迁移项目中散落的注册表
  ├── 更新引用代码
  └── 回归测试

Phase 5: 文档完善（0.5 天）
  ├── 更新 README.md
  ├── 补充 XML 文档注释
  └── 编写迁移指南示例
```

### 9.2 文件变更清单

#### 新增文件（7 个）

| 文件 | 内容 |
|------|------|
| `Share/Registry/ISetRegistry.cs` | 集合注册表接口 |
| `Share/Registry/SetRegistryBase.cs` | 集合注册表基类 |
| `Share/Registry/IFactoryRegistry.cs` | 工厂注册表接口 |
| `Share/Registry/FactoryRegistryBase.cs` | 工厂注册表基类 |
| `Share/Registry/IMetaMapRegistry.cs` | 带元数据的键值映射接口 |
| `Share/Registry/MetaMapRegistryBase.cs` | 带元数据的键值映射基类 |
| `Share/Registry/INotifyRegistry.cs` | 事件通知接口 + EventArgs + ObservableMapRegistry |

#### 修改文件（3 个）

| 文件 | 变更 |
|------|------|
| `Share/Registry/MapRegistryBase.cs` | 添加 RegisterRange / Count / GetValues / Filter / FilterEntries |
| `Share/Registry/IRegistry.cs` | 无需修改（保持向后兼容） |
| `Share/Registry/README.md` | 补充新模式文档和迁移指南 |

---

## 10. 迁移指南

### 10.1 模式选择决策树

```
需要注册什么？
  │
  ├─ 只有键，没有值 → ISetRegistry<TKey>
  │    例：命令名白名单、权限列表
  │
  ├─ 键 → 值（一对一映射）
  │   ├─ 值需要延迟创建（工厂模式）→ IFactoryRegistry<TKey, TValue>
  │   │    例：命令工厂、插件工厂
  │   ├─ 值需要附加元数据 → IMetaMapRegistry<TKey, TValue, TMeta>
  │   │    例：函数注册表（值=Delegate，元数据=FunctionDescriptor）
  │   └─ 普通键值映射 → IMapRegistry<TKey, TValue>
  │        例：配置映射、处理器注册表
  │
  ├─ 阶段 → 有序值列表 → IStageRegistry<TStage, TValue>
  │    例：管线阶段、中间件链
  │
  └─ 节点 + 边（图结构）→ IGraphRegistry<TNodeId, TNode, TEdge>
       例：工作流、依赖图
```

### 10.2 迁移示例

#### 示例 1：CommandParser 迁移

```csharp
// 改造前 — 使用 HashSet + Dictionary 手动管理
public class CommandParser
{
    private readonly HashSet<string> _nonEventCommands;
    private readonly Dictionary<string, string> _normalizationMap;

    public void RegisterNonEventCommand(string commandName)
        => _nonEventCommands.Add(commandName);

    public void RegisterNormalization(string shorthand, string fullCommand)
        => _normalizationMap[shorthand] = fullCommand;
}

// 改造后 — 使用 Registry 基类
public class CommandParser
{
    private readonly ISetRegistry<string> _nonEventCommands;
    private readonly IMapRegistry<string, string> _normalizationMap;

    public CommandParser(
        ISetRegistry<string>? nonEventCommands = null,
        IMapRegistry<string, string>? normalizationMap = null)
    {
        _nonEventCommands = nonEventCommands
            ?? new SetRegistryBase<string>(StringComparer.OrdinalIgnoreCase);
        _normalizationMap = normalizationMap
            ?? new MapRegistryBase<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public void RegisterNonEventCommand(string commandName)
        => _nonEventCommands.Register(commandName);

    public void RegisterNormalization(string shorthand, string fullCommand)
        => _normalizationMap.Register(shorthand, fullCommand);
}
```

#### 示例 2：NonEventCommandHandler 迁移

```csharp
// 改造前 — 使用 Dictionary 手动管理
public class NonEventCommandHandler
{
    private readonly Dictionary<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>> _handlers
        = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterHandler(string actionName,
        Func<ParsedCommand, Task<CommandExecutionResult<object?>>> handler)
        => _handlers[actionName] = handler;
}

// 改造后 — 使用 MapRegistryBase
public class NonEventCommandHandler
{
    private readonly IMapRegistry<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>> _handlers;

    public NonEventCommandHandler(
        IMapRegistry<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>>? handlers = null)
    {
        _handlers = handlers ?? new MapRegistryBase<string, Func<ParsedCommand, Task<CommandExecutionResult<object?>>>>(
            StringComparer.OrdinalIgnoreCase);
    }

    public void RegisterHandler(string actionName,
        Func<ParsedCommand, Task<CommandExecutionResult<object?>>> handler)
        => _handlers.Register(actionName, handler);
}
```

#### 示例 3：CommandRegistry 迁移

```csharp
// 改造前 — 手动管理工厂字典
public class CommandRegistry
{
    private readonly ConcurrentDictionary<string, Func<IServiceProvider?, ICommand>> _factories
        = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, Func<IServiceProvider?, ICommand> factory)
        => _factories[name] = factory;

    public ICommand? Resolve(string name, IServiceProvider? sp = null)
        => _factories.TryGetValue(name, out var factory) ? factory(sp) : null;
}

// 改造后 — 使用 FactoryRegistryBase
public class CommandRegistry : FactoryRegistryBase<string, ICommand>
{
    public CommandRegistry()
        : base(StringComparer.OrdinalIgnoreCase) { }

    // 继承 FactoryRegistryBase 的所有方法：
    // Register(name, factory) / Resolve / Contains / GetKeys / Count
    // 无需额外代码
}
```

#### 示例 4：FunctionRegistry 迁移

```csharp
// 改造前 — 手动管理 Delegate + FunctionDescriptor
public class FunctionRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredFunction> _functions
        = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, Delegate function, string? description = null)
        => _functions[name] = new RegisteredFunction(function, description);

    public bool TryGetDescriptor(string name, out FunctionDescriptor? descriptor) { ... }
}

// 改造后 — 使用 MetaMapRegistryBase
public class FunctionRegistry
    : MetaMapRegistryBase<string, Delegate, FunctionDescriptor>
{
    public FunctionRegistry()
        : base(StringComparer.OrdinalIgnoreCase) { }

    // 便捷方法
    public void Register(string name, Delegate function, string? description = null)
    {
        var descriptor = FunctionDescriptor.FromDelegate(function, description);
        Register(name, function, descriptor);
    }

    public bool TryGetDescriptor(string name, out FunctionDescriptor? descriptor)
        => TryGetMetadata(name, out descriptor);
}
```

---

## 11. 附录：文件清单

### 11.1 最终项目文件结构

```
Share/Registry/
├── Registry.csproj
├── README.md
├── IRegistry.cs                    ← 已有：最简标记接口
├── ISetRegistry.cs                 ← ★ 新增：集合注册表接口
├── IMapRegistry.cs                 ← 已有：键值映射接口
├── IFactoryRegistry.cs             ← ★ 新增：工厂注册表接口
├── IMetaMapRegistry.cs             ← ★ 新增：带元数据的键值映射接口
├── IStageRegistry.cs               ← 已有：阶段管理接口
├── IGraphRegistry.cs               ← 已有：图结构接口
├── INotifyRegistry.cs              ← ★ 新增：事件通知接口 + EventArgs + ObservableMapRegistry
├── SetRegistryBase.cs              ← ★ 新增：集合注册表基类
├── MapRegistryBase.cs              ← 增强：添加批量/查询方法
├── FactoryRegistryBase.cs          ← ★ 新增：工厂注册表基类
├── MetaMapRegistryBase.cs          ← ★ 新增：带元数据的键值映射基类
├── StageRegistryBase.cs            ← 已有：阶段管理基类
└── GraphRegistryBase.cs            ← 已有：图结构基类
```

### 11.2 接口与基类对照表

| 接口 | 基类 | 模式 | 新增/已有 |
|------|------|------|----------|
| `IRegistry` | — | 标记接口 | 已有 |
| `ISetRegistry<TKey>` | `SetRegistryBase<TKey>` | 集合 | ★ 新增 |
| `IMapRegistry<TKey, TValue>` | `MapRegistryBase<TKey, TValue>` | 键值映射 | 已有（增强） |
| `IFactoryRegistry<TKey, TValue>` | `FactoryRegistryBase<TKey, TValue>` | 工厂 | ★ 新增 |
| `IMetaMapRegistry<TKey, TValue, TMeta>` | `MetaMapRegistryBase<TKey, TValue, TMeta>` | 元数据映射 | ★ 新增 |
| `IStageRegistry<TStage, TValue>` | `StageRegistryBase<TStage, TValue>` | 阶段管理 | 已有 |
| `IGraphRegistry<TNodeId, TNode, TEdge>` | `GraphRegistryBase<TNodeId, TNode, TEdge>` | 图结构 | 已有 |
| `INotifyRegistry<TKey, TValue>` | `ObservableMapRegistry<TKey, TValue>` | 事件通知 | ★ 新增 |

### 11.3 基类内置能力一览

| 基类 | 内置能力（无需额外接口） |
|------|------------------------|
| `MapRegistryBase<TKey, TValue>` | `RegisterRange` / `UnregisterRange` / `Count` / `GetValues` / `Filter` / `FilterEntries` |
| `SetRegistryBase<TKey>` | `Count` / `GetKeys` |
| `FactoryRegistryBase<TKey, TValue>` | `Count` / `GetKeys` |
| `MetaMapRegistryBase<TKey, TValue, TMeta>` | `Count` / `GetKeys` |
| `ObservableMapRegistry<TKey, TValue>` | 继承 MapRegistryBase 所有能力 + 事件通知 |

---

> **文档结束**
>
> 本文档为 Share.Registry 通用化更新重构的完整方案，涵盖现状分析、差距分析、接口设计、基类实现、迁移映射和实施步骤。
