#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// HTML 更新推送器（L4 通用实现，零领域类型）——把 <see cref="UiUpdateEnvelope"/>（范围/版本/追踪号）序列化为
/// 页面更新协议（<c>{cmd:'update', scope:[…]?, all?:true, v:N, traceId}</c>）并经 <see cref="IHtmlChannel.PostToPageAsync"/> 推送。
/// <para>版本号与 <c>refreshTraceId</c> 由本类单调生成（一次推送一条轨迹）；领域仅提供变更范围与通道解析委托。</para>
/// </summary>
public sealed class HtmlUpdatePusher
{
    private readonly Func<IHtmlChannel?> _channelResolver;
    private readonly Func<string, string> _traceFactory;
    private int _version;

    /// <param name="channelResolver">通道解析（返回 null = 页面未挂载 → 静默零推送）。</param>
    /// <param name="traceFactory">追踪号工厂（缺省 plate 本地序 <see cref="RefreshTrace.New"/>；宿主可注入框架
    /// <c>Strategy.Refresh.Diagnostics.RefreshTraceId.New</c> 统一一次刷新一条轨迹）。</param>
    public HtmlUpdatePusher(Func<IHtmlChannel?> channelResolver, Func<string, string>? traceFactory = null)
    {
        _channelResolver = channelResolver ?? throw new ArgumentNullException(nameof(channelResolver));
        _traceFactory = traceFactory ?? RefreshTrace.New;
    }

    /// <summary>推送一次更新（scope=null/空 = 显式全量）；false = 未送达（未挂载 / 通道异常）。</summary>
    public async Task<bool> PushAsync(IReadOnlyCollection<string>? scopes, CancellationToken ct = default)
    {
        var channel = _channelResolver();
        if (channel is null) return false;
        var envelope = new UiUpdateEnvelope(
            scopes is null ? Array.Empty<string>() : new List<string>(scopes),
            Interlocked.Increment(ref _version),
            TraceId: _traceFactory("push"));
        try
        {
            return await channel.PostToPageAsync(BuildPayload(envelope), ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>序列化更新信封为页面协议 JSON（单一序列化点；页面脚本按 DOM 上下文补全 session/history 后回投）。</summary>
    public static string BuildPayload(UiUpdateEnvelope envelope)
    {
        var sb = new StringBuilder();
        sb.Append("{\"cmd\":\"update\"");
        if (envelope.IsAll)
        {
            sb.Append(",\"all\":true");
        }
        else
        {
            sb.Append(",\"scope\":[");
            var first = true;
            foreach (var s in envelope.Scopes)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(JsonSerializer.Serialize(s));
            }
            sb.Append(']');
        }
        sb.Append(",\"v\":").Append(envelope.Version);
        if (!string.IsNullOrEmpty(envelope.TraceId)) sb.Append(",\"traceId\":").Append(JsonSerializer.Serialize(envelope.TraceId));
        if (!string.IsNullOrEmpty(envelope.Fingerprint)) sb.Append(",\"fp\":").Append(JsonSerializer.Serialize(envelope.Fingerprint));
        sb.Append('}');
        return sb.ToString();
    }
}

