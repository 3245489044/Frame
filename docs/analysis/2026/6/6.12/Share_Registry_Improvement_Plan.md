# Share.Registry 代码改进方案

> 基于综合分析报告，针对重复逻辑、错误逻辑、冗余逻辑的改进方案
> 日期：2026-06-12

---

## 一、改进项总览

| 优先级 | 改进项 | 类别 | 涉及文件 | 风险 |
|:------:|--------|:----:|---------|:----:|
| P0 | 修复 ObservableMapRegistry 事件触发时序 | 错误 | `INotifyRegistry.cs` | 低 |
| P0 | 修复 MetaMapRegistryBase metadata 值类型处理 | 错误 | `MetaMapRegistryBase.cs` | 低 |
| P1 | 让 GraphRegistryBase 继承 RegistryBase | 重复/冗余 | `GraphRegistryBase.cs` | 中 |
| P1 | 让 StageRegistryBase 继承 RegistryBase | 重复/冗余 | `StageRegistryBase.cs` | 中 |
| P1 | 修复 StageRegistryBase.ClearStage 语义 | 错误 | `StageRegistryBase.cs` | 低 |
| P2 | 统一生命周期回调（SetRegistryBase/FactoryRegistryBase 补全回调） | 重复 | `SetRegistryBase.cs`, `FactoryRegistryBase.cs` | 低 |
| P2 | 移除自定义 ThrowIfNull，改用内置方法 | 冗余 | `RegistryBase.cs` | 低 |
| P3 | 消除各子类 StoreContainsKey/StoreTryRemove/StoreClear 重复 | 重复 | 所有 Base 类 | 高 |

---

## 二、详细改进方案

### P0-1: 修复 ObservableMapRegistry 事件触发时序

**问题**：`OnBeforeRegister` 在值写入存储前触发事件，事件名 `ItemRegistered`/`ItemUpdated` 语义暗示"已发生"。

**方案**：将事件触发逻辑从 `OnBeforeRegister` 移到 `Register` 方法中值写入之后，或改为 `OnAfterRegister` 模式。

**具体修改**：

在 `MapRegistryBase.cs` 中，修改 `Register` 方法：

```csharp
public virtual void Register(TKey key, TValue value)
{
    ThrowIfNull(key);
    ThrowIfNull(value);
    OnBeforeRegister(key, value);  // 保留回调，但不在此触发事件

    if (!_allowOverwrite && _store.ContainsKey(key))
        throw new InvalidOperationException($"Key '{key}' is already registered.");

    _store[key] = value;
    
    OnAfterRegister(key, value);   // ★ 新增：值写入后回调
}

// ★ 新增虚方法
protected virtual void OnAfterRegister(TKey key, TValue value) { }
```

在 `ObservableMapRegistry` 中，重写 `OnAfterRegister` 而非 `OnBeforeRegister`：

```csharp
protected override void OnAfterRegister(TKey key, TValue value)
{
    // 此时值已写入 Store，可以安全获取旧值
    // 但注意：ConcurrentDictionary 的 [] 操作是 upsert，无法区分新增/更新
    // 需要额外机制来区分
}
```

**简化方案**（推荐）：保持现有架构，但将事件触发移到值写入后，通过返回值或状态标记来区分新增/更新。

### P0-2: 修复 MetaMapRegistryBase metadata 值类型处理

**问题**：`metadata is not null` 对值类型行为不一致。

**方案**：使用 `EqualityComparer<TMeta>.Default.Equals()` 结合 `Nullable.GetUnderlyingType` 判断。

```csharp
public virtual void Register(TKey key, TValue value, TMeta? metadata = default)
{
    ThrowIfNull(key);
    ThrowIfNull(value);
    OnBeforeRegister(key, value, metadata);
    _values[key] = value;
    
    if (IsMetadataDefault(metadata))
        _metadatas.TryRemove(key, out _);
    else
        _metadatas[key] = metadata;
}

private static bool IsMetadataDefault(TMeta? metadata)
{
    if (typeof(TMeta).IsValueType)
    {
        var nullableType = Nullable.GetUnderlyingType(typeof(TMeta));
        if (nullableType != null)
            return metadata == null; // Nullable<T>
        return EqualityComparer<TMeta>.Default.Equals(metadata!, default!);
    }
    return metadata is null; // 引用类型
}
```

### P1-1: 让 GraphRegistryBase 继承 RegistryBase

**问题**：`GraphRegistryBase` 未继承 `RegistryBase<TNodeId>`，导致参数校验方式不一致、Clear 独立实现、缺少生命周期回调。

**方案**：让 `GraphRegistryBase` 继承 `RegistryBase<TNodeId>`，实现抽象方法。

```csharp
public class GraphRegistryBase<TNodeId, TNode, TEdge> : RegistryBase<TNodeId>, 
    IGraphRegistry<TNodeId, TNode, TEdge>
    where TNodeId : notnull
{
    // 实现抽象方法
    protected override bool StoreContainsKey(TNodeId key) => _nodes.ContainsKey(key);
    protected override bool StoreTryRemove(TNodeId key) { ... } // 移除节点+相关边
    protected override void StoreClear() { ... } // 清空所有
    
    // 利用基类的 ThrowIfNull 替代 ArgumentNullException.ThrowIfNull
    public void RegisterNode(TNodeId id, TNode node)
    {
        ThrowIfNull(id);
        ThrowIfNull(node);
        ...
    }
}
```

### P1-2: 让 StageRegistryBase 继承 RegistryBase

**问题**：`StageRegistryBase` 未继承 `RegistryBase<TStage>`，参数校验方式不一致、Clear 独立实现。

**方案**：让 `StageRegistryBase` 继承 `RegistryBase<TStage>`。

**注意**：`StageRegistryBase` 的键是 `TStage`（枚举），但它的操作模式与 `RegistryBase` 的 `Contains(TKey)`/`Unregister(TKey)` 不完全匹配（阶段注册表关注的是阶段内的值列表，而非阶段本身作为键值对）。因此**继承 RegistryBase 可能不自然**。

**替代方案**：改为实现 `IRegistry` 接口（而非继承 `RegistryBase`），但统一使用 `ThrowIfNull` 辅助方法。

### P1-3: 修复 StageRegistryBase.ClearStage 语义

**问题**：`ClearStage` 替换为空列表而非移除键，导致 `GetStages()` 仍返回已清空的阶段。

**方案**：改为移除键：

```csharp
public void ClearStage(TStage stage)
{
    _lock.EnterWriteLock();
    try
    {
        _store.TryRemove(stage, out _);  // 直接移除键
    }
    finally { _lock.ExitWriteLock(); }
}
```

### P2-1: 统一生命周期回调

**问题**：`SetRegistryBase` 和 `FactoryRegistryBase` 缺少 `OnBeforeUnregister` 和 `OnBeforeClear` 回调。

**方案**：补全缺失的回调调用。

在 `SetRegistryBase.cs` 中：
```csharp
public virtual void Unregister(TKey key)
{
    ThrowIfNull(key);
    OnBeforeUnregister(key);  // ★ 新增
    _store.TryRemove(key, out _);
}

public virtual void Clear()
{
    OnBeforeClear();  // ★ 新增
    _store.Clear();
}
```

在 `FactoryRegistryBase.cs` 中：
```csharp
public virtual void Unregister(TKey key)
{
    ThrowIfNull(key);
    OnBeforeUnregister(key);  // ★ 新增
    _factories.TryRemove(key, out _);
}

public virtual void Clear()
{
    OnBeforeClear();  // ★ 新增
    _factories.Clear();
}
```

### P2-2: 移除自定义 ThrowIfNull

**问题**：`RegistryBase.ThrowIfNull` 是对 .NET 内置 `ArgumentNullException.ThrowIfNull` 的冗余封装。

**方案**：移除自定义方法，改用内置方法。

```csharp
// 移除 RegistryBase.cs 中的 ThrowIfNull 方法
// 所有子类中改用 ArgumentNullException.ThrowIfNull(xxx)
```

**注意**：`ThrowIfNull` 使用了 `[CallerArgumentExpression]` 特性，能自动获取参数名。内置的 `ArgumentNullException.ThrowIfNull` 在 .NET 6+ 中也支持此特性，因此功能完全等价。

---

## 三、实施路线图

### Phase 1: 错误修复（P0）
1. 修复 ObservableMapRegistry 事件触发时序
2. 修复 MetaMapRegistryBase metadata 值类型处理

### Phase 2: 架构统一（P1）
3. 让 GraphRegistryBase 继承 RegistryBase
4. 评估 StageRegistryBase 继承 RegistryBase 的可行性
5. 修复 StageRegistryBase.ClearStage 语义

### Phase 3: 代码精简（P2）
6. 统一生命周期回调
7. 移除自定义 ThrowIfNull

### Phase 4: 深度重构（P3，可选）
8. 消除各子类 StoreContainsKey/StoreTryRemove/StoreClear 重复
