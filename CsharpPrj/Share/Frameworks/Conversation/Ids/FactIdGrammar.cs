using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Share.Conversation.Ids;

/// <summary>
/// FactId 文法（**唯一字面量来源**）—— 会话事实归属键的段定义、分隔符与正则。
/// <para>
/// 权威：<c>Projects\Plug\Agent\doc\会话事实标准与唯一标识-设计与实施方案.md</c> §1.1。
/// 除本文件外**禁止**任何落点拼写 <c>"#R"</c> / <c>"@"</c> 等键拼接串（机检 C1），
/// 生成与解析一律经 <see cref="FactId"/> / <see cref="FactIds"/>。
/// </para>
/// <para>
/// 落位（2026-09-24 S1 下沉）：本件原在 <c>Agent.Session\Ids</c>（Agent 插件内共享层），
/// 现为**平台层**能力 <c>Share.Conversation.Ids</c>（零 Agent 依赖），供任意插件产出可关联的会话键。
/// 消费方经 <c>using Share.Conversation.Ids;</c> 直连（旧命名空间的类型转发 shim 已废：
/// 实测 C# 编译器不解析 TypeForwardedTo，转发仅对运行时标识解析生效）。
/// </para>
/// <para>
/// 文法（BNF）：
/// <code>
/// RunKey     ::= SessionKey
/// RoundKey   ::= SessionKey "#R" ROUND_NO
/// StepKey    ::= RoundKey "/" STEP_NAME ["#" ATTEMPT]
/// CallKey    ::= StepKey "/" ITERATION "/" CALL_ID
/// </code>
/// 声明面（P0-5）：<c>Config\session-facts.ushar</c> 的 <c>[Space] FactIdSegment</c>（段名与段序）
/// 与 <c>[Space] FactIdOrdinals</c>（数值段值域）；本文件的常量与正则为**发射生成物**的当前形态
/// （P2-13 起由 <c>Usharp.Cli</c> 发射替代手写，届时删除手写副本）。
/// </para>
/// </summary>
public static class FactIdGrammar
{
    /// <summary>轮次段标记（轮键 = 会话键 + 本标记 + 轮号，如 <c>default#R1</c>）。</summary>
    public const string RoundMarker = "#R";

    /// <summary>段分隔符（轮 → 步骤 → 调用 的层级分隔）。</summary>
    public const string SegmentSeparator = "/";

    /// <summary>重试代次标记（同一步骤的重派发代次）。</summary>
    public const string AttemptMarker = "#";

    /// <summary>轮键正则（仅 <c>SessionKey#R&lt;n&gt;</c>；轮号从 1 起，故数值段拒前导 0）。</summary>
    public const string RoundKeyPattern = @"^[A-Za-z0-9_.\-]+#R[1-9][0-9]*$";

    /// <summary>全形键正则（轮 / 步 / 调用 三形态；供解析器与机检脚本共用）。</summary>
    public const string AnyKeyPattern = @"^[A-Za-z0-9_.\-]+#R[1-9][0-9]*(?:\/[A-Za-z0-9_.\-]+#[1-9][0-9]*(?:\/[1-9][0-9]*\/[A-Za-z0-9_.\-]+)?)?$";

    /// <summary>会话键 / 段名（<c>ID</c>）正则：字母数字与 <c>_.-</c>。</summary>
    public const string IdPattern = @"^[A-Za-z0-9_.\-]+$";

    /// <summary>存量形态：框架轮次键 <c>{taskKey}@{seq}</c>（迁移用，新写作禁用）。</summary>
    public const string LegacyAtRoundPattern = @"^(.+)@([0-9]+)$";

    /// <summary>存量形态：观测轮次键 <c>{runId}R{n}</c>（迁移用，新写作禁用）。</summary>
    public const string LegacyObservedRoundPattern = @"^(.+)R([0-9]+)$";

    /// <summary>存量形态：轮次租约键 <c>{taskKey}#{gen}#{seq}</c>（迁移用；无轮号信息）。</summary>
    public const string LegacyLeasePattern = @"^(.+)#([0-9]+)#([0-9]+)$";

    /// <summary>存量形态：SDK 外部代号（<c>conv_*</c> 或 10 位以上时间戳前缀）。</summary>
    public const string LegacyExternalKeyPattern = @"^(conv_[A-Za-z0-9_\-]+|[0-9]{10,}[A-Za-z0-9_\-]*)$";

    private static readonly Regex RoundKeyRegex =
        new(RoundKeyPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AnyKeyRegex =
        new(AnyKeyPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IdRegex =
        new(IdPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>ID 形态判定（会话键与段名共用同一字符集）。</summary>
    /// <param name="value">待判定串。</param>
    public static bool IsId(string? value)
        => !string.IsNullOrWhiteSpace(value) && IdRegex.IsMatch(value!);

    /// <summary>轮键形态判定（<c>SessionKey#R&lt;n&gt;</c>）。</summary>
    /// <param name="value">待判定串。</param>
    public static bool IsRoundKey(string? value)
        => !string.IsNullOrWhiteSpace(value) && RoundKeyRegex.IsMatch(value!);

    /// <summary>任意键形态判定（轮 / 步 / 调用）。</summary>
    /// <param name="value">待判定串。</param>
    public static bool IsAnyKey(string? value)
        => !string.IsNullOrWhiteSpace(value) && AnyKeyRegex.IsMatch(value!);

    /// <summary>
    /// 键 → 线格式串。run 级键（<c>RoundNo&lt;=0</c>）输出裸会话键；步骤/调用段按文法拼接。
    /// </summary>
    /// <param name="id">键值对象。</param>
    /// <exception cref="ArgumentException">键不合法（会话键非法 / 段组合越界）。</exception>
    public static string Format(FactId id)
    {
        if (!IsId(id.SessionKey))
            throw new ArgumentException($"非法会话键 '{id.SessionKey}'（须匹配 {IdPattern}）", nameof(id));

        if (id.RoundNo <= 0)
        {
            if (id.StepName is not null || id.Iteration is not null || id.CallId is not null)
                throw new ArgumentException("run 级键（RoundNo<=0）不得携带步骤/调用段", nameof(id));
            return id.SessionKey;
        }

        var text = id.SessionKey + RoundMarker + id.RoundNo.ToString(CultureInfo.InvariantCulture);
        if (id.StepName is null)
        {
            if (id.Iteration is not null || id.CallId is not null)
                throw new ArgumentException("缺步骤段不得直接携带调用段（须先给 StepName）", nameof(id));
            return text;
        }

        if (!IsId(id.StepName))
            throw new ArgumentException($"非法步骤名 '{id.StepName}'（须匹配 {IdPattern}）", nameof(id));

        text += SegmentSeparator + id.StepName + AttemptMarker
            + (id.Attempt ?? 1).ToString(CultureInfo.InvariantCulture);

        if (id.Iteration is null) return text;
        if (id.CallId is null)
            throw new ArgumentException("调用段须同时给出 iteration 与 callId", nameof(id));
        if (!IsId(id.CallId))
            throw new ArgumentException($"非法调用标识 '{id.CallId}'（须匹配 {IdPattern}）", nameof(id));

        return text + SegmentSeparator + id.Iteration.Value.ToString(CultureInfo.InvariantCulture)
            + SegmentSeparator + id.CallId;
    }

    /// <summary>
    /// 串 → 键值对象（严格：不匹配文法即 false，**不猜测**）；可反解出全部段（不变式 I3，无需外部查表）。
    /// </summary>
    /// <param name="text">键串。</param>
    /// <param name="id">out：解析结果（失败为 <c>default</c>）。</param>
    public static bool TryParse(string? text, out FactId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text!.Trim();
        if (!AnyKeyRegex.IsMatch(value)) return false;

        var segments = value.Split(SegmentSeparator);
        var head = segments[0];
        var roundMarkerIndex = head.IndexOf(RoundMarker, StringComparison.Ordinal);
        if (roundMarkerIndex <= 0) return false;

        var sessionKey = head[..roundMarkerIndex];
        if (!IsId(sessionKey)) return false;
        if (!int.TryParse(head[(roundMarkerIndex + RoundMarker.Length)..], NumberStyles.None,
                CultureInfo.InvariantCulture, out var roundNo) || roundNo <= 0)
            return false;

        switch (segments.Length)
        {
            case 1:
                id = new FactId(sessionKey, roundNo);
                return true;

            case 2:
                if (!TryParseStepSegment(segments[1], out var stepName, out var attempt)) return false;
                id = new FactId(sessionKey, roundNo, stepName, attempt);
                return true;

            case 4:
                if (!TryParseStepSegment(segments[1], out var stepName4, out var attempt4)) return false;
                if (!int.TryParse(segments[2], NumberStyles.None, CultureInfo.InvariantCulture,
                        out var iteration) || iteration <= 0)
                    return false;
                if (!IsId(segments[3])) return false;
                id = new FactId(sessionKey, roundNo, stepName4, attempt4, iteration, segments[3]);
                return true;

            default:
                return false;
        }
    }

    /// <summary>串 → 键值对象（失败即抛）。</summary>
    /// <param name="text">键串。</param>
    /// <exception cref="FormatException">不符合 <see cref="AnyKeyPattern"/>。</exception>
    public static FactId Parse(string? text)
        => TryParse(text, out var id)
            ? id
            : throw new FormatException($"非法 FactId '{text}'（须匹配 {AnyKeyPattern}）");

    /// <summary>
    /// 键序比较（不变式 I4）：会话键（Ordinal）→ 轮号（**数值**）→ 步骤名（Ordinal）→
    /// 代次 / 回合（数值）→ 调用标识（Ordinal）。**禁止**对键串做字典序比较（<c>#R10</c> 会被误判小于 <c>#R2</c>）。
    /// </summary>
    /// <param name="x">左键。</param>
    /// <param name="y">右键。</param>
    public static int Compare(FactId x, FactId y)
    {
        var result = string.CompareOrdinal(x.SessionKey ?? string.Empty, y.SessionKey ?? string.Empty);
        if (result != 0) return result;

        result = x.RoundNo.CompareTo(y.RoundNo);          // 数值段：禁字典序
        if (result != 0) return result;

        result = string.CompareOrdinal(x.StepName ?? string.Empty, y.StepName ?? string.Empty);
        if (result != 0) return result;

        result = Nullable.Compare(x.Attempt, y.Attempt);  // 数值段（null 在前）
        if (result != 0) return result;

        result = Nullable.Compare(x.Iteration, y.Iteration);
        if (result != 0) return result;

        return string.CompareOrdinal(x.CallId ?? string.Empty, y.CallId ?? string.Empty);
    }

    /// <summary>解析步骤段 <c>{step}#{attempt}</c>（失败返回 false）。</summary>
    private static bool TryParseStepSegment(string segment, out string stepName, out int attempt)
    {
        stepName = string.Empty;
        attempt = 0;
        var markerIndex = segment.LastIndexOf(AttemptMarker, StringComparison.Ordinal);
        if (markerIndex <= 0) return false;

        var name = segment[..markerIndex];
        if (!IsId(name)) return false;
        if (!int.TryParse(segment[(markerIndex + AttemptMarker.Length)..], NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            return false;

        stepName = name;
        attempt = parsed;
        return true;
    }
}
