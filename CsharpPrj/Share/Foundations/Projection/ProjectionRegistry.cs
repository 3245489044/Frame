#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Projection;

/// <summary>载体注册表（每族恰一个条目；重复注册 ⇒ 抛）。</summary>
public sealed class ProjectionRegistry
{
    private readonly Dictionary<string, object> _carriers = new(StringComparer.Ordinal);

    /// <summary>注册载体（空 id / 重复 id ⇒ 抛）。</summary>
    public ProjectionRegistry Register<TPlan, TArtifact>(IProjectionCarrier<TPlan, TArtifact> carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        if (string.IsNullOrWhiteSpace(carrier.CarrierId))
        {
            throw new ArgumentException("carrier id 不能为空", nameof(carrier));
        }

        if (!_carriers.TryAdd(carrier.CarrierId, carrier))
        {
            throw new InvalidOperationException($"carrier 重复注册：{carrier.CarrierId}");
        }

        return this;
    }

    /// <summary>查找载体。</summary>
    public bool TryGet<TPlan, TArtifact>(string carrierId, out IProjectionCarrier<TPlan, TArtifact>? carrier)
    {
        if (!string.IsNullOrEmpty(carrierId)
            && _carriers.TryGetValue(carrierId, out var value)
            && value is IProjectionCarrier<TPlan, TArtifact> typed)
        {
            carrier = typed;
            return true;
        }

        carrier = null;
        return false;
    }

    /// <summary>已注册载体 id 集。</summary>
    public IReadOnlyCollection<string> CarrierIds => _carriers.Keys;
}
