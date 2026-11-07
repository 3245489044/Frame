#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Projection;

/// <summary>一次投影的只读上下文（声明/参数/载体/诊断 sink）。</summary>
/// <param name="Family">投影家族（surface/readmodel/uiresult/...）。</param>
/// <param name="CarrierId">目标载体 id（可空 = 由 Plan 决定）。</param>
/// <param name="Parameters">声明面参数（只读）。</param>
public sealed record ProjectionContext(
    string Family,
    string? CarrierId = null,
    IReadOnlyDictionary<string, string?>? Parameters = null)
{
    /// <summary>空上下文。</summary>
    public static readonly ProjectionContext Empty = new(string.Empty);

    /// <summary>参数（null ⇒ 空表）。</summary>
    public IReadOnlyDictionary<string, string?> SafeParameters =>
        Parameters ?? EmptyParameters;

    private static readonly IReadOnlyDictionary<string, string?> EmptyParameters =
        new Dictionary<string, string?>(StringComparer.Ordinal);
}
