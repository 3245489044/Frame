using System;
using System.Collections.Generic;
using System.Globalization;
using Share.Conversation.Ids;

namespace Share.Conversation.Facts;

/// <summary>
/// 事实条目（线格式单元，方案 §2.1）——一条事实 = 「归属键 + 类别 + 序号 + 时刻 + 载荷」。
/// <para>
/// 不变量：<see cref="Key"/> 恒为轮级键（F5 归属完备）；<see cref="Seq"/> 同文档内严格递增无空洞（F3，
/// 由 <see cref="SessionFactWriter"/> 单点分配）；事实只增不改（F2，无 update/delete API）。
/// </para>
/// </summary>
/// <param name="V">线格式版本（见 <see cref="CurrentVersion"/>）。</param>
/// <param name="Key">归属键（轮级）。</param>
/// <param name="Kind">事实类别。</param>
/// <param name="Seq">同文档内单调序号（0 起，由 Writer 分配）。</param>
/// <param name="AtUtc">发生时刻（UTC）。</param>
/// <param name="Actor">执行体（如 <c>agent/cline@worker-1</c>；容器层 / 纯簿记层为空）。</param>
/// <param name="Source">事实来源。</param>
/// <param name="Authority">权威级。</param>
/// <param name="PayloadJson">kind 专属载荷（浅层 JSON 对象，键名小写下划线）。</param>
/// <param name="ExternalRefs">外部代号（**只作属性，禁参与关联**）。</param>
public sealed record FactEnvelope(
    string V,
    FactId Key,
    string Kind,
    long Seq,
    DateTimeOffset AtUtc,
    string? Actor,
    FactSource Source,
    FactAuthority Authority,
    string PayloadJson,
    IReadOnlyList<ExternalRef> ExternalRefs)
{
    /// <summary>当前线格式版本（升版本 = 增字段；**禁改既有字段语义**）。</summary>
    public const string CurrentVersion = "1";

    /// <summary>归属键文本（线格式）。</summary>
    public string KeyText => Key.ToString();

    /// <summary>
    /// 幂等指纹（F4 去重键）：同 <c>(Key, Kind, 载荷原文)</c> 视为同一事实。
    /// <para>口径：载荷原文一致即去重 —— 载荷内随时刻变化的字段（elapsed_ms 等）应由产事实点控制，
    /// 需要「同回合重放只落一条」的语义时，产事实点不得把时刻写进载荷。</para>
    /// </summary>
    public string Fingerprint => FingerprintOf(Kind, PayloadJson);

    /// <summary>指纹计算（唯一实现；供去重集合与诊断共用）。</summary>
    /// <param name="kind">事实类别（线格式值）。</param>
    /// <param name="payloadJson">载荷原文。</param>
    public static string FingerprintOf(string kind, string? payloadJson)
        => (kind ?? string.Empty) + '\u0001' + (payloadJson ?? string.Empty);

    /// <summary>构造条目（Writer 之外的调用方仅用于测试 / 反序列化）。</summary>
    /// <param name="key">归属键（轮级）。</param>
    /// <param name="kind">事实类别。</param>
    /// <param name="seq">序号。</param>
    /// <param name="payloadJson">载荷文本。</param>
    /// <param name="actor">执行体。</param>
    /// <param name="source">来源。</param>
    /// <param name="authority">权威级。</param>
    /// <param name="externalRefs">外部代号。</param>
    /// <param name="atUtc">发生时刻（缺省 = 当前 UTC）。</param>
    public static FactEnvelope Create(
        FactId key,
        string kind,
        long seq,
        string payloadJson,
        string? actor = null,
        FactSource source = FactSource.Csharp,
        FactAuthority authority = FactAuthority.Primary,
        IReadOnlyList<ExternalRef>? externalRefs = null,
        DateTimeOffset? atUtc = null)
        => new(CurrentVersion, key, kind, seq, atUtc ?? DateTimeOffset.UtcNow, actor, source, authority,
            string.IsNullOrWhiteSpace(payloadJson) ? FactPayload.Empty : payloadJson,
            externalRefs ?? Array.Empty<ExternalRef>());
}
