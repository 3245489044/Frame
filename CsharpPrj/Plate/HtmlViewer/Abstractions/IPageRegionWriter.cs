#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Html;

namespace UiBackend.Abstractions;

/// <summary>
/// 页面区域写端口（B4/R2）：总线/事件桥把 <c>region:&lt;key&gt;</c> 目标交给拥有该页面的运行时执行，
/// 由运行时转 <see cref="UiWritePlan"/> 经单写者落地（禁第二套 DOM 写路径）。
/// </summary>
public interface IPageRegionWriter
{
    /// <summary>该运行时是否承载指定域限定元素（<c>page:name</c> / <c>host:name</c>）。</summary>
    bool OwnsElement(string elementName);

    /// <summary>页面声明的区域目录（null = 未声明，任何区域目标均拒绝）。</summary>
    RegionCatalog? Regions { get; }

    /// <summary>区域是否声明为流式段（追加写）。</summary>
    bool IsStreamRegion(string regionKey);

    /// <summary>执行写回计划（计划自带目标；区域目标可经此单点落地）。</summary>
    Task<UiWriteResult> ExecutePlanAsync(UiWritePlan plan, string? traceId = null, CancellationToken ct = default);

    /// <summary>写区域（<paramref name="append"/>=true 走 append-only 流面）。</summary>
    Task<UiWriteResult> WriteRegionAsync(
        string regionKey,
        string html,
        bool append,
        string? traceId = null,
        CancellationToken ct = default);
}

/// <summary>
/// 区域写端口注册表（运行时自注册）：事件总线按「元素归属」解析拥有者。
/// <para>生命周期跟随 <see cref="HtmlBackendRuntime"/>：装配注册、Dispose 注销（防旧运行时成孤儿写者）。</para>
/// </summary>
public static class PageRegionWriterRegistry
{
    private static readonly List<IPageRegionWriter> Writers = new();
    private static readonly object Gate = new();

    public static void Register(IPageRegionWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        lock (Gate)
        {
            if (!Writers.Contains(writer)) Writers.Add(writer);
        }
    }

    public static void Unregister(IPageRegionWriter writer)
    {
        if (writer is null) return;
        lock (Gate)
        {
            Writers.Remove(writer);
        }
    }

    /// <summary>按元素归属解析写者；无归属 → null（调用方须显式拒绝并记日志）。</summary>
    public static IPageRegionWriter? Resolve(string elementName)
    {
        if (string.IsNullOrWhiteSpace(elementName)) return null;
        lock (Gate)
        {
            foreach (var writer in Writers)
            {
                if (writer.OwnsElement(elementName)) return writer;
            }
        }
        return null;
    }
}