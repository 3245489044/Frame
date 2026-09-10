using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.HostAssembly.Abstractions;

/// <summary>
/// 装配结果（不可变摘要）—— 描述一次组合根装配的总体可用性与降级清单。
/// 语义：Succeeded = 装配后宿主整体可用；DegradedModules = 未能装配但未阻断宿主启动的可选模块清单（可观测降级）。
/// </summary>
public sealed record BootstrapResult(
    bool Succeeded,
    IReadOnlyList<string> DegradedModules,
    string? Error = null,
    Exception? Exception = null)
{
    /// <summary>完全成功（无可选模块降级）。</summary>
    public static BootstrapResult Success()
        => new(true, Array.Empty<string>());

    /// <summary>整体可用，但部分可选模块降级（聚合降级清单供宿主可感知）。</summary>
    public static BootstrapResult Partial(IEnumerable<string> degradedModules, string? error = null)
        => new(true, degradedModules?.ToArray() ?? Array.Empty<string>(), error);

    /// <summary>整体失败（核心装配失败，宿主不可用）。</summary>
    public static BootstrapResult Failure(string error, Exception? exception = null)
        => new(false, Array.Empty<string>(), error, exception);
}
