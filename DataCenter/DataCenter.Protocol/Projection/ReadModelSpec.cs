#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace DataCenter.Protocol.Projection;

/// <summary>
/// 读模型版本源（<b>声明处选定</b>，projector 不得自带版本 —— 否则版本源分散缺陷复发）。
/// </summary>
public enum ReadModelVersionMode
{
    /// <summary>无版本（缓存仅由 TTL 兜底；version 恒 0）。</summary>
    None,

    /// <summary>TTL 失效（version 恒 0，缓存存活期由 <see cref="ReadModelSpec.TtlMs"/> 决定）。</summary>
    Ttl,

    /// <summary>事件失效（须注册版本探针：订阅计数/事件序号）。</summary>
    Event,

    /// <summary>图成员轴版本（须注册版本探针：成员集版本号）。</summary>
    GraphMembership,

    /// <summary>声明指纹版本（须注册版本探针：声明图内容指纹；声明漂移 ⇒ 必刷新）。</summary>
    Declared,
}

/// <summary>
/// 声明式读模型契约：<c>(模型 id) → 源键 + 投影器 + 版本源 + 参数名表</c>。
/// <para>本体与消费方无关：只认「模型 id + 源键 + 投影委托」，不引任何领域/引擎类型。</para>
/// </summary>
/// <param name="ModelId">模型标识（唯一；声明处与 op 绑定同一字符串）。</param>
/// <param name="Sources">源键（<c>Slot.&lt;HostPort&gt;</c> / <c>Service.&lt;TypeName&gt;</c>；非空）。</param>
/// <param name="Version">版本源（声明处选定）。</param>
/// <param name="TtlMs">缓存存活期（<see cref="ReadModelVersionMode.Ttl"/> 必填且 &gt; 0）。</param>
/// <param name="Args">允许的读参数名（空 = 无参）。</param>
public sealed record ReadModelSpec(
    string ModelId,
    IReadOnlyList<string> Sources,
    ReadModelVersionMode Version,
    int? TtlMs,
    IReadOnlyList<string> Args)
{
    /// <summary>版本源是否需要外部探针（<see cref="ReadModelVersionMode.Event"/> / <see cref="ReadModelVersionMode.GraphMembership"/> / <see cref="ReadModelVersionMode.Declared"/>）。</summary>
    public bool RequiresVersionProbe
        => Version is ReadModelVersionMode.Event
            or ReadModelVersionMode.GraphMembership
            or ReadModelVersionMode.Declared;

    /// <summary>构造声明（去空白/去空项；空 id 或空源即抛，禁静默空声明）。</summary>
    public static ReadModelSpec Create(
        string modelId,
        IEnumerable<string> sources,
        ReadModelVersionMode version = ReadModelVersionMode.None,
        int? ttlMs = null,
        IEnumerable<string>? args = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(sources);

        var normalizedSources = sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToArray();
        if (normalizedSources.Length == 0)
            throw new ArgumentException("读模型至少声明一个源键。", nameof(sources));

        var normalizedArgs = (args ?? Array.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a.Trim())
            .ToArray();

        return new ReadModelSpec(modelId.Trim(), normalizedSources, version, ttlMs, normalizedArgs);
    }
}
