#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 字段可见性裁决结果（声明面唯一出口；禁消费方各自推断）。
/// </summary>
internal enum FrameFieldVisibility
{
    /// <summary>未装配该 kind（调用方按旧口径）。</summary>
    Undeclared = 0,

    /// <summary>值可用。</summary>
    Known = 1,

    /// <summary>该 kind 应有此键但值为空（可见占位 + 原因码）。</summary>
    Unknown = 2,

    /// <summary>该 kind 声明面不适用（不渲染；不是缺值）。</summary>
    NotApplicable = 3,
}

/// <summary>
/// 字段口径（**单一来源**）：kind → 该 kind 的声明键集，加读路径事实集（写侧键集）。
/// <para>派生自既有声明（frame-kinds.fields[].from ∪ fields 词表 ∪ read-paths.facts），不新增手写清单。</para>
/// <para>裁决语义（<see cref="VerdictOf"/>）：<c>NotApplicable</c> = 该 kind 无此键（不渲染，不是缺值）；
/// <c>Unknown</c> = 键存在且契约 <c>always</c> 但值为空（可见占位）；<c>Undeclared</c> = 未覆盖该 kind（旧口径，零回归）。</para>
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
        // 判据只读该 kind 的声明键集：全局读写事实键不构成「该 kind 应有此键」（E11）。
        return keys.Contains(key);
    }

    /// <summary>
    /// 三态裁决（**唯一判据**：声明覆盖度 + 契约必填性；消费方禁再自行推断）。
    /// <para>
    /// 语义：<c>NotApplicable</c> = 该 kind 的声明键集/写侧事实集中无此键（不渲染，**不是**缺值）；
    /// <c>Unknown</c> = 键存在且契约声明为 <c>always</c>（应写而未写 ⇒ 可见占位）；
    /// <c>Known</c> = 值存在；<c>Undeclared</c> = 未装配该 kind（调用方按旧口径，零回归）。
    /// </para>
    /// </summary>
    /// <param name="kind">帧类别。</param>
    /// <param name="key">来源字段键（契约 <c>from</c>）。</param>
    /// <param name="valuePresent">该键在本帧是否有非空值。</param>
    /// <param name="required">契约是否声明为 <c>always</c>（必写键）。</param>
    internal FrameFieldVisibility VerdictOf(string kind, string key, bool valuePresent, bool required)
    {
        if (!IsDeclared || kind is not { Length: > 0 }) return FrameFieldVisibility.Undeclared;
        if (!_keys.TryGetValue(kind, out var keys)) return FrameFieldVisibility.Undeclared;
        if (valuePresent) return FrameFieldVisibility.Known;

        var declared = key is { Length: > 0 } && keys.Contains(key);
        if (!declared) return FrameFieldVisibility.NotApplicable;
        return required ? FrameFieldVisibility.Unknown : FrameFieldVisibility.NotApplicable;
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
