using System;
using System.Globalization;
using System.Text;

namespace Share.Signal;

/// <summary>
/// 信号级别（编码分层信号）：Primary=一级（立即触发）；Secondary/Tertiary=多级（可攒批 / 条件触发）。
/// <para>默认 = <see cref="SignalLevel.Primary"/>（= 旧行为，零回归）。</para>
/// </summary>
public enum SignalLevel
{
    /// <summary>一级：直接作用于目标实体，立即触发。</summary>
    Primary = 1,

    /// <summary>二级：多级信号（可攒批 / 条件触发）。</summary>
    Secondary = 2,

    /// <summary>三级：多级信号（可攒批 / 条件触发）。</summary>
    Tertiary = 3,
}

/// <summary>信号触发语义。</summary>
public enum SignalTrigger
{
    /// <summary>立即触发（一级缺省）。</summary>
    Immediate,

    /// <summary>攒批触发（攒够 N 条 / 时间窗到期）。</summary>
    Batched,

    /// <summary>条件触发（谓词命中 / 显式 flush / 依赖满足）。</summary>
    Conditional,
}

/// <summary>
/// 编码分层信号信封（可序列化 / 可寻址 / 可去重）。
/// <para>一级：<see cref="Level"/>=Primary 或 <see cref="Trigger"/>=Immediate ⇒ 立即触发。</para>
/// <para>多级：暂存至任务池（Active=false 簿记），条件命中后批量分发。</para>
/// </summary>
public sealed record SignalEnvelope(
    SignalLevel Level,
    string Scope,
    string Target,
    string Kind,
    SignalTrigger Trigger = SignalTrigger.Immediate,
    string? CoalesceKey = null,
    string? ConditionId = null,
    string? Payload = null,
    long Timestamp = 0,
    int Priority = 0)
{
    /// <summary>是否立即触发（一级 + Immediate）。</summary>
    public bool IsImmediate => Level == SignalLevel.Primary || Trigger == SignalTrigger.Immediate;

    /// <summary>聚合键（缺省 = Target，末路完备）。</summary>
    public string EffectiveKey => string.IsNullOrWhiteSpace(CoalesceKey) ? Target : CoalesceKey!;
}

/// <summary>解码结果（Ok=false 时 Error 非空；违规→parse_error 语义，不抛）。</summary>
public sealed record SignalDecodeResult(SignalEnvelope? Envelope, string? Error)
{
    /// <summary>是否解码成功。</summary>
    public bool Ok => Envelope is not null;
}

/// <summary>
/// 编码分层信号 codec（纯函数，无副作用，不新增逻辑算子）：
/// <c>sig:&lt;level&gt;/&lt;scope&gt;/&lt;target&gt;#&lt;kind&gt;?trig=&amp;key=&amp;cond=&amp;p=&amp;ts=&amp;pri=</c>
/// <para>违规（格式不符 / level 非法）→ 返回错误（调用方按 parse_error 处理，禁静默）。</para>
/// </summary>
public static class SignalCodec
{
    /// <summary>编码 scheme（编码串前缀）。</summary>
    public const string Scheme = "sig";

    /// <summary>编码为可持久化/可寻址字符串。</summary>
    public static string Encode(SignalEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var sb = new StringBuilder();
        sb.Append(Scheme).Append(':')
          .Append((int)envelope.Level).Append('/')
          .Append(Escape(envelope.Scope)).Append('/')
          .Append(Escape(envelope.Target)).Append('#')
          .Append(Escape(envelope.Kind));

        var query = new System.Collections.Generic.List<string>(6);
        query.Add("trig=" + envelope.Trigger.ToString());
        if (!string.IsNullOrWhiteSpace(envelope.CoalesceKey)) query.Add("key=" + Escape(envelope.CoalesceKey!));
        if (!string.IsNullOrWhiteSpace(envelope.ConditionId)) query.Add("cond=" + Escape(envelope.ConditionId!));
        if (!string.IsNullOrWhiteSpace(envelope.Payload)) query.Add("p=" + Escape(envelope.Payload!));
        if (envelope.Timestamp != 0) query.Add("ts=" + envelope.Timestamp.ToString(CultureInfo.InvariantCulture));
        if (envelope.Priority != 0) query.Add("pri=" + envelope.Priority.ToString(CultureInfo.InvariantCulture));
        sb.Append('?').Append(string.Join('&', query));
        return sb.ToString();
    }

    /// <summary>解码编码串；失败返回错误（不抛）。</summary>
    public static SignalDecodeResult Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new SignalDecodeResult(null, "空信号串");

        var s = text.Trim();
        var ci = s.IndexOf(':');
        if (ci < 0 || !string.Equals(s.Substring(0, ci), Scheme, StringComparison.OrdinalIgnoreCase))
            return new SignalDecodeResult(null, $"scheme 非 {Scheme}");

        var rest = s.Substring(ci + 1);
        string query = string.Empty;
        var qi = rest.IndexOf('?');
        if (qi >= 0)
        {
            query = rest.Substring(qi + 1);
            rest = rest.Substring(0, qi);
        }

        var hi = rest.IndexOf('#');
        if (hi < 0) return new SignalDecodeResult(null, "缺 kind 段（#）");
        var kind = Unescape(rest.Substring(hi + 1));
        var body = rest.Substring(0, hi);

        var segments = body.Split('/');
        if (segments.Length != 3) return new SignalDecodeResult(null, "路径段须为 <level>/<scope>/<target>");
        if (!int.TryParse(segments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var levelNum)
            || !Enum.IsDefined(typeof(SignalLevel), levelNum))
            return new SignalDecodeResult(null, $"level 非法：{segments[0]}");

        var scope = Unescape(segments[1]);
        var target = Unescape(segments[2]);
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(target))
            return new SignalDecodeResult(null, "scope / target 不能为空");

        var trigger = SignalTrigger.Immediate;
        string? key = null, cond = null, payload = null;
        long ts = 0;
        var pri = 0;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var k = pair.Substring(0, eq);
            var v = pair.Substring(eq + 1);
            switch (k)
            {
                case "trig":
                    if (!Enum.TryParse<SignalTrigger>(v, ignoreCase: true, out trigger))
                        return new SignalDecodeResult(null, $"trig 非法：{v}");
                    break;
                case "key": key = Unescape(v); break;
                case "cond": cond = Unescape(v); break;
                case "p": payload = Unescape(v); break;
                case "ts":
                    if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out ts))
                        return new SignalDecodeResult(null, $"ts 非法：{v}");
                    break;
                case "pri":
                    if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out pri))
                        return new SignalDecodeResult(null, $"pri 非法：{v}");
                    break;
            }
        }

        var envelope = new SignalEnvelope(
            (SignalLevel)levelNum, scope, target, kind, trigger, key, cond, payload, ts, pri);
        return new SignalDecodeResult(envelope, null);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

    private static string Unescape(string value) => Uri.UnescapeDataString(value ?? string.Empty);
}

