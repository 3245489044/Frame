#nullable enable

using System;
using System.Collections.Generic;

namespace FrontEndDriven.HtmlHosting;

/// <summary>
/// 通用 HTML 宿主槽注册表 —— 按插件 key 取用各自 <see cref="HtmlUiHostSlot"/> 实例（首次取用时创建并缓存），
/// 使多插件（Agent / Produce / 未来）共享同一套宿主门面机制而**槽位互相隔离**（消除单一全局槽被后装配方覆盖）。
/// 未指定 key 的消费方沿用 <see cref="HtmlUiHost.Default"/>（单插件兼容形态）。
/// </summary>
public static class HtmlUiHostRegistry
{
    private static readonly object _gate = new();
    private static readonly Dictionary<string, HtmlUiHostSlot> _slots = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取（或创建）指定插件的宿主槽实例。key 不可为空。</summary>
    public static HtmlUiHostSlot For(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("宿主槽 key 不可为空", nameof(key));
        lock (_gate)
        {
            if (_slots.TryGetValue(key, out var existing)) return existing;
            var created = new HtmlUiHostSlot();
            _slots[key] = created;
            return created;
        }
    }

    /// <summary>登记指定插件的宿主槽实例（幂等：覆盖式）。供组合根显式注入自建实例。</summary>
    public static void Register(string key, HtmlUiHostSlot slot)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("宿主槽 key 不可为空", nameof(key));
        ArgumentNullException.ThrowIfNull(slot);
        lock (_gate) _slots[key] = slot;
    }
}
