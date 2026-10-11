using System;
using System.Collections.Generic;

namespace Operation.LoopFoldMethod.Catalog;

/// <summary>
/// **域键分派器**（通用）—— 持「域键 → 服务」映射，无键重载走默认键，未命中不伪造（返回 false / null）。
/// <para>
/// 存在理由：注册表按类型键注册时，同类型多域无法各注册一次；故单槽注册本分派器，多域在内装填。
/// </para>
/// </summary>
/// <typeparam name="TService">域服务类型。</typeparam>
public sealed class KeyedDispatcher<TService>
{
    private readonly IReadOnlyDictionary<string, TService> _services;
    private readonly string _defaultKey;

    /// <param name="services">域键 → 服务。</param>
    /// <param name="defaultKey">无键重载的默认域键。</param>
    public KeyedDispatcher(IReadOnlyDictionary<string, TService> services, string defaultKey)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _defaultKey = string.IsNullOrWhiteSpace(defaultKey)
            ? throw new ArgumentException("默认域键不可为空", nameof(defaultKey))
            : defaultKey;
    }

    /// <summary>已装配的域键集（可读枚举；供诊断）。</summary>
    public IReadOnlyDictionary<string, TService> Services => _services;

    /// <summary>默认域键（无键重载使用）。</summary>
    public string DefaultKey => _defaultKey;

    /// <summary>按域键取服务；命中 ⇒ true。</summary>
    /// <param name="key">域键（空 ⇒ 默认键）。</param>
    /// <param name="service">服务（未命中为 default）。</param>
    public bool TryGet(string? key, out TService service)
    {
        var effective = string.IsNullOrWhiteSpace(key) ? _defaultKey : key!;
        if (_services.TryGetValue(effective, out var found))
        {
            service = found;
            return true;
        }

        service = default!;
        return false;
    }

    /// <summary>按域键取服务；未命中返回 default（不伪造）。</summary>
    /// <param name="key">域键（空 ⇒ 默认键）。</param>
    public TService? For(string? key) => TryGet(key, out var service) ? service : default;
}
