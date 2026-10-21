#nullable enable

using System;
using System.Collections.Concurrent;

namespace UiBackend.Abstractions;

/// <summary>
/// 视图受控态容器（通用件）—— 会话选中、历史选中、浮层内容等「一等模型状态」按具名槽外置。
/// <para>
/// 刷新拍据此复用状态，不再依赖「上一拍是否传过 HTML / 是否带 history」的渲染副作用；
/// 槽值 null/空白即清空（收起）。线程安全（并发读写安全，单槽后写胜出）。
/// </para>
/// </summary>
public sealed class UiViewState
{
    private readonly ConcurrentDictionary<string, string> _slots = new(StringComparer.Ordinal);

    /// <summary>读取槽值；未设置 / 已清空 ⇒ null。</summary>
    public string? Get(string slot)
        => _slots.TryGetValue(slot, out var value) ? value : null;

    /// <summary>设置槽值；null/空白 ⇒ 清空该槽。</summary>
    public void Set(string slot, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _slots.TryRemove(slot, out _);
            return;
        }

        _slots[slot] = value!;
    }

    /// <summary>清空槽值。</summary>
    public void Clear(string slot) => _slots.TryRemove(slot, out _);

    /// <summary>是否已置位（非空）。</summary>
    public bool Has(string slot) => _slots.ContainsKey(slot);
}
