using System;

namespace Strategy.Catalog.Diagnostics;

/// <summary>
/// 目录查询框架异常基类 —— 错误一律在调用边界抛出（不做内部降级/静默兜底），
/// 与 Strategy.Observability 的错误契约一致。
/// </summary>
public class CatalogException : Exception
{
    public CatalogException(string message) : base(message) { }

    public CatalogException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// 引用了未注册的目录源（装配期/调用期显式失败，不给静默空集兜底）。
/// <para>已注册源清单经 <see cref="Registered"/> 一并带出，供调用方自纠。</para>
/// </summary>
public sealed class UnknownCatalogSourceException : CatalogException
{
    /// <summary>请求的源 Id。</summary>
    public string RequestedSourceId { get; }

    /// <summary>当前已注册的源 Id 集（保序）。</summary>
    public System.Collections.Generic.IReadOnlyList<string> Registered { get; }

    public UnknownCatalogSourceException(string sourceId, System.Collections.Generic.IReadOnlyList<string> registered)
        : base($"未注册的目录源 '{sourceId}'（已注册: {(registered is { Count: > 0 } ? string.Join(", ", registered) : "(空)")}）。"
               + "请先经 CatalogRegistry.Register 装配对应 ICatalogSource。")
    {
        RequestedSourceId = sourceId;
        Registered = registered ?? Array.Empty<string>();
    }
}

/// <summary>
/// 目录源注册冲突（同一 Id 注册了不同实例）—— 装配期显式失败，杜绝"后注册静默覆盖先注册"。
/// </summary>
public sealed class DuplicateCatalogSourceException : CatalogException
{
    public DuplicateCatalogSourceException(string sourceId)
        : base($"目录源 Id 重复注册：'{sourceId}'（同一 Id 已存在不同实例）。"
               + "如需替换请显式调用 CatalogRegistry.Replace。")
    {
    }
}

/// <summary>
/// 目录轴未声明（按未声明的轴查询）—— 显式失败并带出可用轴清单，避免"查不到却以为为空"。
/// </summary>
public sealed class UnknownCatalogAxisException : CatalogException
{
    public UnknownCatalogAxisException(string sourceId, string axisId, System.Collections.Generic.IReadOnlyList<string> declared)
        : base($"目录源 '{sourceId}' 未声明轴 '{axisId}'（已声明: {(declared is { Count: > 0 } ? string.Join(", ", declared) : "(空)")}）。")
    {
    }
}
