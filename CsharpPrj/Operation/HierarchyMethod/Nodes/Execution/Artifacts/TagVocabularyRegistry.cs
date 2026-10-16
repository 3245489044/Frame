using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 轴化标签词表注册表端口（<c>axis → 词表</c>）—— 通用标签框架的**轴级单源**。
/// <para>
/// 语义：每轴独立词表（token ⇔ 位值），互不共享位空间；注册**幂等合并**（同轴同映射 = 空操作），
/// 冲突（同轴异映射且既有非内置缺省）⇒ 诊断 + 拒绝覆盖（不静默降级）。消除单轴静态词表被多域装配互相覆盖的缺陷。
/// </para>
/// </summary>
public interface ITagVocabularyRegistry
{
    /// <summary>注册某轴词表（幂等合并；冲突 ⇒ 诊断 + 拒绝覆盖）。</summary>
    void Register(string axis, INodeTagVocabulary vocabulary);

    /// <summary>取某轴词表。</summary>
    bool TryGet(string axis, out INodeTagVocabulary vocabulary);

    /// <summary>已注册轴名（取证 / 对齐门用）。</summary>
    IReadOnlyCollection<string> Axes { get; }

    /// <summary>在某轴内解析 token → 位值（大小写不敏感）。</summary>
    bool TryResolve(string axis, string token, out int bit);

    /// <summary>冲突 / 拒绝覆盖诊断（只增；供取证）。</summary>
    IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// 轴注册表实现（进程级单例可由宿主持有；本类本身无进程静态状态，便于测试注入局部词表）。
/// </summary>
public sealed class TagVocabularyRegistry : ITagVocabularyRegistry
{
    private readonly Dictionary<string, INodeTagVocabulary> _axes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _diagnostics = new();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Axes => _axes.Keys.ToArray();

    /// <inheritdoc />
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <inheritdoc />
    public void Register(string axis, INodeTagVocabulary vocabulary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axis);
        ArgumentNullException.ThrowIfNull(vocabulary);

        if (_axes.TryGetValue(axis, out var existing))
        {
            if (ReferenceEquals(existing, vocabulary) || SameMapping(existing, vocabulary))
            {
                return; // 幂等合并：同映射 = 空操作
            }

            // 既有为内置缺省 ⇒ 允许声明面覆盖；否则拒绝覆盖（多域/多轴互不污染）。
            if (existing is not DefaultNodeTagVocabulary)
            {
                _diagnostics.Add(
                    $"axis.conflict|{axis}|既有词表非内置缺省：拒绝覆盖（不静默降级）");
                return;
            }
        }

        _axes[axis] = vocabulary;
    }

    /// <inheritdoc />
    public bool TryGet(string axis, out INodeTagVocabulary vocabulary)
        => _axes.TryGetValue(axis ?? string.Empty, out vocabulary!);

    /// <inheritdoc />
    public bool TryResolve(string axis, string token, out int bit)
    {
        bit = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (!_axes.TryGetValue(axis ?? string.Empty, out var vocabulary)) return false;
        return vocabulary.Marks.TryGetValue(token, out bit);
    }

    private static bool SameMapping(INodeTagVocabulary left, INodeTagVocabulary right)
    {
        if (left.Marks.Count != right.Marks.Count) return false;
        foreach (var pair in left.Marks)
        {
            if (!right.Marks.TryGetValue(pair.Key, out var value) || value != pair.Value) return false;
        }
        return true;
    }
}
