using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Share.RoslynJson.Diagnostics;

/// <summary>
/// 诊断描述符目录：稳定错误码 → <see cref="DiagnosticDescriptor"/>，未登记码回落 <see cref="Fallback"/>。
/// 领域生成器登记自己的 ID 段；框架级 RJ1xxx 由 <see cref="RoslynJsonDiagnostics"/> 承担。
/// </summary>
public sealed class DiagnosticCatalog
{
    private readonly Dictionary<string, DiagnosticDescriptor> _descriptors =
        new Dictionary<string, DiagnosticDescriptor>(StringComparer.Ordinal);

    /// <summary>构造目录。</summary>
    /// <param name="fallback">未登记码的回落描述符。</param>
    public DiagnosticCatalog(DiagnosticDescriptor fallback)
    {
        Fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    /// <summary>未登记码的回落描述符。</summary>
    public DiagnosticDescriptor Fallback { get; }

    /// <summary>已登记码数。</summary>
    public int Count => _descriptors.Count;

    /// <summary>登记码 → 描述符（重复登记即覆盖，便于派生替换基线）。</summary>
    public DiagnosticCatalog Register(string code, DiagnosticDescriptor descriptor)
    {
        if (string.IsNullOrEmpty(code)) throw new ArgumentException("码不能为空", nameof(code));
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));

        _descriptors[code] = descriptor;
        return this;
    }

    /// <summary>按稳定码取描述符（未登记回落 <see cref="Fallback"/>）。</summary>
    public DiagnosticDescriptor ForCode(string? code)
        => code is not null && _descriptors.TryGetValue(code, out var descriptor) ? descriptor : Fallback;
}
