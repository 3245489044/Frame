#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UiBackend.Abstractions;

/// <summary>
/// 浮层注册表（通用件，**载荷无关**）—— 把「哪个视图的哪种浮层当前打开」收敛为
/// <c>(view, kind) → 片段</c> 单源受控态。
/// <para>
/// 纪律：① 键 = <c>(view, kind)</c>；<c>key</c> 只作身份/诊断，不参与查找；
/// ② <see cref="Show"/> 是唯一写入口（空/空白片段 ⇒ 关该 kind）；
/// ③ <see cref="Resolve"/> 只读，刷新拍不得产生写；④ <see cref="Close(string,string?)"/> 只清目标视图，
/// 不触碰其他视图（治「切视图后旧键复活」）。
/// </para>
/// </summary>
public sealed class UiOverlayRegistry
{
    private sealed record Entry(string? Key, string Html);

    private readonly ConcurrentDictionary<string, Entry> _open = new(StringComparer.Ordinal);

    /// <summary>展示/替换某视图某类浮层；<paramref name="html"/> 空/空白 ⇒ 关闭该 (view, kind)。</summary>
    public void Show(string view, string kind, string? key, string? html)
    {
        var id = KeyOf(view, kind);
        if (string.IsNullOrWhiteSpace(html))
        {
            _open.TryRemove(id, out _);
            return;
        }

        _open[id] = new Entry(string.IsNullOrWhiteSpace(key) ? null : key!.Trim(), html);
    }

    /// <summary>读取某视图某类浮层片段（未打开 ⇒ null；不产生写）。</summary>
    public string? Resolve(string view, string kind)
        => _open.TryGetValue(KeyOf(view, kind), out var entry) ? entry.Html : null;

    /// <summary>读取该浮层的身份键（诊断/断言用；未打开 ⇒ null）。</summary>
    public string? ResolveKey(string view, string kind)
        => _open.TryGetValue(KeyOf(view, kind), out var entry) ? entry.Key : null;

    /// <summary>关闭该视图的某类浮层（<paramref name="kind"/> 为 null ⇒ 关闭该视图全部浮层）。</summary>
    public void Close(string view, string? kind = null)
    {
        var viewName = Normalize(view, nameof(view));
        if (!string.IsNullOrWhiteSpace(kind))
        {
            _open.TryRemove(viewName + "/" + kind!.Trim(), out _);
            return;
        }

        var prefix = viewName + "/";
        foreach (var id in _open.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            _open.TryRemove(id, out _);
    }

    /// <summary>关闭全部浮层（跨视图；仅在整页重置时使用）。</summary>
    public void CloseAll() => _open.Clear();

    /// <summary>该视图当前打开的浮层身份键（诊断/断言；按键序返回，空 ⇒ 空数组）。</summary>
    public IReadOnlyList<string> OpenKeys(string view)
    {
        var prefix = Normalize(view, nameof(view)) + "/";
        return _open
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && !string.IsNullOrEmpty(kv.Value.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value.Key!)
            .ToArray();
    }

    private static string KeyOf(string view, string kind) => Normalize(view, nameof(view)) + "/" + Normalize(kind, nameof(kind));

    private static string Normalize(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("浮层键必须声明视图与类型。", name)
            : value!.Trim();
}
