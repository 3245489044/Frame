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
    private readonly Func<long>? _versionProvider;
    private int _version;

    /// <param name="channelResolver">通道解析（返回 null = 页面未挂载 → 静默零推送）。</param>
    /// <param name="traceFactory">追踪号工厂（必填；唯一实现由上层注入
    /// <c>Strategy.Refresh.Diagnostics.RefreshTraceId.New</c>，Plate 不持有第二个序号空间）。</param>
    /// <param name="versionProvider">内容版本提供者（须与 <c>IRefreshStore</c> 已交付版本同源；
    /// 缺省或非正数时回落本类内部序号——仅兼容面，不得作为版本单源）。</param>
    public HtmlUpdatePusher(Func<IHtmlChannel?> channelResolver, Func<string, string> traceFactory,
        Func<long>? versionProvider = null)
    {
        _channelResolver = channelResolver ?? throw new ArgumentNullException(nameof(channelResolver));
        _traceFactory = traceFactory ?? throw new ArgumentNullException(nameof(traceFactory));
        _versionProvider = versionProvider;
    }

    /// <summary>推送一次更新（scope=null/空 = 显式全量）；false = 未送达（未挂载 / 通道异常）。</summary>
    public async Task<bool> PushAsync(IReadOnlyCollection<string>? scopes, CancellationToken ct = default)
    {
        var channel = _channelResolver();
        if (channel is null) return false;
        var version = _versionProvider?.Invoke() ?? 0;
        if (version <= 0) version = Interlocked.Increment(ref _version);
        var envelopeVersion = version > int.MaxValue ? int.MaxValue : (int)version;
        var envelope = new UiUpdateEnvelope(
            scopes is null ? Array.Empty<string>() : new List<string>(scopes),
            envelopeVersion,
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

