#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

namespace UiBackend.Html;

/// <summary>
/// 页面模板集解析器（通用件）—— 「框架默认模板 ⊕ 宿主注入槽位」合并、显式注入优先、引用变化缓存、
/// 必需键缺失一次性可见化。
/// <para>
/// 模板来源、必需键与诊断出口全部由宿主注入；本类不含领域键名与日志实现。
/// 语义：已显式注入 ⇒ 注入实例（默认集为底合并）；否则框架默认集为底、槽位同名键覆盖其上
/// （槽位未注入/为空 ⇒ 直接用默认集）；必需键缺失 ⇒ 仅首次回调一次（禁静默渲空）。
/// </para>
/// </summary>
public sealed class HtmlTemplateSetResolver
{
    /// <summary>空模板集（框架默认资产不可用时的兜底；防止 null 传播到渲染器）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private readonly Func<IReadOnlyDictionary<string, string>?> _defaults;
    private readonly Func<IReadOnlyDictionary<string, string>?> _slot;
    private readonly IReadOnlyList<string> _requiredKeys;
    private readonly Action<IReadOnlyList<string>, IReadOnlyDictionary<string, string>>? _onMissing;
    private readonly object _gate = new();

    private IReadOnlyDictionary<string, string>? _configured;
    private IReadOnlyDictionary<string, string>? _cachedSlot;
    private IReadOnlyDictionary<string, string>? _cachedResolved;
    private int _reportedMissing;

    /// <param name="defaults">框架默认模板集提供者（可返回 null/空 ⇒ 仅用槽位）。</param>
    /// <param name="slot">宿主槽位模板集提供者（每次解析读取，引用变化才重新合并）。</param>
    /// <param name="requiredKeys">区域渲染必需模板键（缺一即区域片段/壳渲染为空）。</param>
    /// <param name="onMissing">必需键缺失首次回调（缺失键 + 生效模板集），用于领域诊断。</param>
    public HtmlTemplateSetResolver(
        Func<IReadOnlyDictionary<string, string>?> defaults,
        Func<IReadOnlyDictionary<string, string>?> slot,
        IReadOnlyList<string>? requiredKeys = null,
        Action<IReadOnlyList<string>, IReadOnlyDictionary<string, string>>? onMissing = null)
    {
        _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        _slot = slot ?? throw new ArgumentNullException(nameof(slot));
        _requiredKeys = requiredKeys ?? Array.Empty<string>();
        _onMissing = onMissing;
    }

    /// <summary>
    /// 组合根显式注入（单点）：装配层在模板/主题附加成功后调用一次；此后生产渲染只读注入实例。
    /// null/空 ⇒ 忽略（保持既有槽位路径）。
    /// </summary>
    public void Configure(IReadOnlyDictionary<string, string>? templates)
    {
        if (templates is not { Count: > 0 }) return;
        var merged = Merge(templates);
        lock (_gate)
        {
            _configured = merged;
            _cachedResolved = merged;
            _cachedSlot = templates;
        }
    }

    /// <summary>解析生效模板集（注入优先；否则默认集 ⊕ 槽位，按槽位引用缓存）。</summary>
    public IReadOnlyDictionary<string, string> Resolve()
    {
        IReadOnlyDictionary<string, string> resolved;
        lock (_gate)
        {
            if (_configured is { Count: > 0 })
            {
                resolved = _configured;
            }
            else
            {
                var slot = _slot();
                if (_cachedResolved is null || !ReferenceEquals(slot, _cachedSlot))
                {
                    _cachedSlot = slot;
                    _cachedResolved = Merge(slot);
                }

                resolved = _cachedResolved;
            }
        }

        ReportMissingOnce(resolved);
        return resolved;
    }

    /// <summary>默认集 ⊕ 槽位（同名覆盖）；默认集不可用且槽位为空 ⇒ 空集（可判定失败，非静默）。</summary>
    private IReadOnlyDictionary<string, string> Merge(IReadOnlyDictionary<string, string>? slot)
    {
        var defaults = _defaults();
        if (defaults is not { Count: > 0 })
            return slot is { Count: > 0 } ? slot : Empty;
        if (slot is not { Count: > 0 })
            return defaults;

        var merged = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in slot) merged[kv.Key] = kv.Value;
        return merged;
    }

    /// <summary>必需键缺失可见化（一次；禁静默渲空）。</summary>
    private void ReportMissingOnce(IReadOnlyDictionary<string, string> templates)
    {
        if (_requiredKeys.Count == 0 || _onMissing is null) return;

        List<string>? missing = null;
        foreach (var key in _requiredKeys)
        {
            if (templates.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) continue;
            (missing ??= new List<string>()).Add(key);
        }

        if (missing is null) return;
        if (Interlocked.Exchange(ref _reportedMissing, 1) != 0) return;
        _onMissing(missing, templates);
    }
}
