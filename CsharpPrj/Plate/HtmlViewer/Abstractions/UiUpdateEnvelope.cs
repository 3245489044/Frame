#nullable enable

using System.Collections.Generic;

namespace UiBackend.Abstractions;

/// <summary>
/// 更新信封（L1 通用契约，L4 范围协议）——宿主→页面推送的范围/版本/指纹三元组，
/// 使「按范围增量」全程承载（scope 不再在回投丢失），入口指纹短路在取数前可判定。
/// </summary>
public sealed record UiUpdateEnvelope(
    IReadOnlyList<string> Scopes,
    int Version,
    string? Fingerprint = null)
{
    /// <summary>是否全量（无范围声明）。</summary>
    public bool IsAll => Scopes is null || Scopes.Count == 0;
}
