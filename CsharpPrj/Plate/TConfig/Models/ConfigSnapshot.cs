using System;
using System.Collections.Generic;

namespace TConfig;

/// <summary>
/// 配置的不可变快照标识（CW-13 的标的）。D 层发布给 I 层的唯一配置持有形式。
///
/// 不可变语义 = <b>句柄不可变</b>：不提供任何写入/取出可变实例的出口，读取必须经投影函数。
/// 注意（诚实边界）：本类型不做深拷贝，源对象仍由组合根（C）持有并可变更；
/// 「配置模型自身不可变」不在本类范围内，属 CW-12/后续标的工作。
///
/// 与回写的关系：
///   · 快照是单向读取标：不允许从快照取回可写视图，也不允许以快照为参数回写。
///   · 回写只能发生在组合根（C）的显式可变视图；该视图不经由本类型暴露。
/// </summary>
/// <typeparam name="T">被快照的配置模型类型。</typeparam>
public sealed class ConfigSnapshot<T> where T : class
{
    private readonly T _source;

    private ConfigSnapshot(T source, string fingerprint)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            throw new ArgumentException("配置快照必须携带非空指纹（否则无法判定快照代际）", nameof(fingerprint));
        }

        Fingerprint = fingerprint;
        TypeName = typeof(T).Name;
    }

    /// <summary>配置类型名（诊断用，不暴露实例）。</summary>
    public string TypeName { get; }

    /// <summary>快照指纹（由组合根在冻结时计算；为空即构造失败）。</summary>
    public string Fingerprint { get; }

    /// <summary>快照恒为冻结态；不存在「未冻结快照」这一中间态。</summary>
    public bool IsFrozen => true;

    /// <summary>
    /// 组合根构造入口。只允许在 C 侧调用：I 侧调用即违反「配置构造/冻结归组合根」。
    /// </summary>
    public static ConfigSnapshot<T> Create(T source, string fingerprint)
        => new ConfigSnapshot<T>(source, fingerprint);

    /// <summary>只读投影：I 侧只能通过投影读取，拿不到可变的 T 实例。</summary>
    public TResult Read<TResult>(Func<T, TResult> projector)
    {
        if (projector is null) { throw new ArgumentNullException(nameof(projector)); }
        return projector(_source);
    }

    /// <summary>只读投影（集合形态）；返回副本由投影函数自行决定。</summary>
    public IReadOnlyList<TResult> ReadMany<TResult>(Func<T, IReadOnlyList<TResult>> projector)
    {
        if (projector is null) { throw new ArgumentNullException(nameof(projector)); }
        return projector(_source);
    }
}
