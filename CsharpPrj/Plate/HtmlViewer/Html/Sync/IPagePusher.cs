#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// 页面推送端口（通用件）—— 刷新编排只依赖「推送一次更新」的抽象，不再硬绑某一具体推送器；
/// HTML 页面用 <see cref="HtmlPagePusher"/>，其它宿主可实现自己的推送面。
/// </summary>
public interface IPagePusher
{
    /// <summary>推送一次更新（<paramref name="scopes"/> 为 null/空 = 显式全量）；false = 未送达。</summary>
    Task<bool> PushAsync(IReadOnlyCollection<string>? scopes, CancellationToken ct = default);
}

/// <summary>
/// HTML 页面推送器（通用实现）—— <see cref="IPagePusher"/> 到 <see cref="HtmlUpdatePusher"/> 的唯一适配点。
/// </summary>
public sealed class HtmlPagePusher : IPagePusher
{
    private readonly HtmlUpdatePusher _pusher;

    /// <param name="channelResolver">页面通道解析（返回 null = 未挂载 → 零推送）。</param>
    /// <param name="traceFactory">追踪号工厂（由组合根注入单一序号空间，例如 <c>RefreshTraceId.New</c>）。</param>
    /// <param name="beatVersion">推送携带的内容版本提供者（须与已交付版本同源）。</param>
    public HtmlPagePusher(
        Func<IHtmlChannel?> channelResolver,
        Func<string, string> traceFactory,
        Func<long>? beatVersion = null)
    {
        ArgumentNullException.ThrowIfNull(channelResolver);
        ArgumentNullException.ThrowIfNull(traceFactory);
        _pusher = new HtmlUpdatePusher(channelResolver, traceFactory, beatVersion);
    }

    /// <inheritdoc />
    public Task<bool> PushAsync(IReadOnlyCollection<string>? scopes, CancellationToken ct = default)
        => _pusher.PushAsync(scopes, ct);
}
