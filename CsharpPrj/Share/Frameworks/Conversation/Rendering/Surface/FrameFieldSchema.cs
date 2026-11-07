#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 字段口径（**单一来源**）：kind → 该 kind 的声明键集，加读路径通用键集。
/// <para>派生自既有三份声明（frame-kinds.fields[].from ∪ fields 词表 ∪ read-paths.facts），不新增手写清单。</para>
/// <para>裁决语义：<c>IsApplicable(kind,key)=false ⇒ NotApplicable(kind-has-no-such-key)</c>（不渲染，不是缺值）；
/// <c>null ⇒ 未覆盖该 kind</c>（调用方按旧口径，零回归）。</para>
/// </summary>
public sealed class FrameFieldSchema
{
    /// <summary>空口径（未装配）：一切裁决返回 <c>null</c>（旧口径）。</summary>
    public static readonly FrameFieldSchema Empty = new(
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal),
        ReadPathFactsDeclaration.Empty);

    private readonly Dictionary<string, HashSet<string>> _keys;
    private readonly ReadPathFactsDeclaration _readPaths;

    private FrameFieldSchema(Dictionary<string, HashSet<string>> keys, ReadPathFactsDeclaration readPaths)
    {
        _keys = keys;
        _readPaths = readPaths;
        IsDeclared = keys.Count > 0;
    }

    /// <summary>是否来自可用声明。</summary>
    public bool IsDeclared { get; }

    /// <summary>已登记的 kind 集。</summary>
    public IReadOnlyCollection<string> Kinds => _keys.Keys;

    /// <summary>该 kind 是否已登记。</summary>
    /// <param name="kind">帧类别。</param>
    public bool HasKind(string kind) => kind is { Length: > 0 } && _keys.ContainsKey(kind);

    /// <summary>该 kind 的声明键集（未登记 ⇒ 空集）。</summary>
    /// <param name="kind">帧类别。</param>
    public IReadOnlyCollection<string> KeysOf(string kind)
        => kind is { Length: > 0 } && _keys.TryGetValue(kind, out var keys) ? keys : Array.Empty<string>();

    /// <summary>
    /// 适用性裁决：<c>true</c>=适用；<c>false</c>=不适用（该 kind 无此键）；<c>null</c>=未覆盖该 kind（不裁决）。
    /// </summary>
    /// <param name="kind">帧类别（渲染项的 kind 字段）。</param>
    /// <param name="key">来源字段键（契约 <c>from</c> / 字段名）。</param>
    public bool? IsApplicable(string kind, string key)
    {
        if (!IsDeclared || kind is not { Length: > 0 }) return null;
        if (!_keys.TryGetValue(kind, out var keys)) return null;
        if (key is not { Length: > 0 }) return false;
        return keys.Contains(key) || _readPaths.Contains(key);
    }

    /// <summary>由既有声明派生口径（纯函数；无 IO、无缓存）。</summary>
    /// <param name="contracts">kind → 字段契约（<c>frame-kinds.fields[]</c>）。</param>
    /// <param name="kindFields">kind → 字段名（旧口径兜底）。</param>
    /// <param name="readPaths">读路径事实键集（可空）。</param>
    public static FrameFieldSchema From(
        IReadOnlyDictionary<string, IReadOnlyList<FrameFieldContract>>? contracts,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? kindFields,
        ReadPathFactsDeclaration? readPaths = null)
    {
        var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        if (contracts is not null)
        {
            foreach (var pair in contracts)
            {
                if (pair.Key is not { Length: > 0 } || pair.Value is null) continue;
                var set = SetOf(keys, pair.Key);
                foreach (var contract in pair.Value)
                {
                    if (contract?.From is { Length: > 0 } from) set.Add(from);
                }
            }
        }

        if (kindFields is not null)
        {
            foreach (var pair in kindFields)
            {
                if (pair.Key is not { Length: > 0 } || pair.Value is null) continue;
                var set = SetOf(keys, pair.Key);
                foreach (var name in pair.Value)
                {
                    if (name is { Length: > 0 }) set.Add(name);
                }
            }
        }

        return new FrameFieldSchema(keys, readPaths ?? ReadPathFactsDeclaration.Empty);
    }

    private static HashSet<string> SetOf(Dictionary<string, HashSet<string>> keys, string kind)
    {
        if (keys.TryGetValue(kind, out var set)) return set;
        set = new HashSet<string>(StringComparer.Ordinal);
        keys[kind] = set;
        return set;
    }
}
