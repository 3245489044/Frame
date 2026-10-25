using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 域存储模块端口（核心定义、<c>Domains</c> 实现）：声明本域表 + 把域契约注册进 registry。
/// <para>核心零领域知识：模块清单唯一真源 = <c>DataStoreDrivenDomains.All()</c>。</para>
/// </summary>
public interface IDomainStoreModule
{
    /// <summary>模块名（可见化/诊断）。</summary>
    string Name { get; }

    /// <summary>声明本域表（建表/收敛策略），不执行任何 IO。</summary>
    void DeclareTables(ITableCatalog catalog);

    /// <summary>把本域契约 → 实现注册进 registry。</summary>
    void Register(IDomainStoreRegistry registry, StorageKernelContext kernel);
}

/// <summary>域存储注册表（契约 → 工厂）。</summary>
public interface IDomainStoreRegistry
{
    void Register(Type contract, Func<object> factory);
    object? Resolve(Type contract);
}

/// <summary>域存储包：按契约解析已注册实例（单例语义：首次解析后缓存）。</summary>
public sealed class DomainStoreBundle : IDomainStoreRegistry
{
    private readonly ConcurrentDictionary<Type, Func<object>> _factories = new();
    private readonly ConcurrentDictionary<Type, object> _instances = new();

    /// <inheritdoc />
    public void Register(Type contract, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[contract] = factory;
    }

    /// <inheritdoc />
    public object? Resolve(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (_instances.TryGetValue(contract, out var instance)) return instance;
        if (!_factories.TryGetValue(contract, out var factory)) return null;
        instance = factory();
        _instances[contract] = instance;
        return instance;
    }

    /// <summary>按契约解析（未注册 ⇒ null；由宿主决定是否 fail-fast）。</summary>
    public T? Resolve<T>() where T : class => Resolve(typeof(T)) as T;
}

/// <summary>
/// 域组合根：<c>DeclareTables → EnsureAllAsync → Register → bundle</c> 顺序冻结，
/// 避免「表未建先读写」；宿主不得拆序自行调用 <c>EnsureAllAsync</c>。
/// </summary>
public static class DomainComposition
{
    public static async Task<DomainStoreBundle> BuildAsync(
        StorageKernelContext kernel,
        IReadOnlyList<IDomainStoreModule> modules,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(modules);

        var catalog = kernel.Tables;
        foreach (var module in modules)
        {
            ct.ThrowIfCancellationRequested();
            module.DeclareTables(catalog);
        }

        await catalog.EnsureAllAsync(kernel.Executor, ct).ConfigureAwait(false);

        var bundle = new DomainStoreBundle();
        foreach (var module in modules)
        {
            ct.ThrowIfCancellationRequested();
            module.Register(bundle, kernel);
        }
        return bundle;
    }
}
