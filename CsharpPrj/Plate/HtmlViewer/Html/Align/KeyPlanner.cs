#nullable enable

using System;
using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 键规划器（KeyPlanner）——键由渲染层派生、**同父唯一**，冲突即 fail-fast（源头拦截）。
/// 列表渲染（{#each} / ToAnchorRow 等）经此产身份键（<c>k:{parent}/{local}</c>）；模板**不得**手写身份键。
/// <para>纯结构状态（零领域语义）；每次渲染一遍前调 <see cref="Reset"/>（或每父一实例），使"同父重复分配"可被检出。</para>
/// </summary>
public sealed class KeyPlanner
{
    private readonly string _prefix;
    private readonly Dictionary<string, HashSet<string>> _byParent = new(StringComparer.Ordinal);

    /// <param name="prefix">键前缀（默认 <c>k:</c>）。</param>
    public KeyPlanner(string prefix = "k:") => _prefix = prefix;

    /// <summary>作用域内唯一分配：<c>k:{parentKey}/{localName}</c>；同一父级重复分配 ⇒ fail-fast。</summary>
    public string Assign(string? parentKey, string localName)
    {
        if (string.IsNullOrEmpty(localName))
            throw new ArgumentException("localName 不可为空", nameof(localName));
        var parent = parentKey ?? string.Empty;
        if (!_byParent.TryGetValue(parent, out var set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            _byParent[parent] = set;
        }
        if (!set.Add(localName))
            throw new InvalidOperationException(
                $"KeyPlanner: 同父键重复分配（parent={parent}, local={localName}）—— 身份键在同一父级必须唯一");
        return _prefix + parent + "/" + localName;
    }

    /// <summary>清空规划状态（下一次渲染前重置）。</summary>
    public void Reset() => _byParent.Clear();
}
