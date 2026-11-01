using System;
using System.Collections.Generic;
using System.Globalization;
using Modules.Node.Identity;

namespace Modules.Node.Frame;

/// <summary>
/// 帧身份键（**唯一构造/反解入口**）：由键文法内核求值，禁手拼分隔符。
/// <para>
/// 默认模板 <c>{run}|{round}|{seq}</c> **逐字复刻**既有键形（缺 run 段先用 <c>-</c> 规范化后参与求值），
/// 迁移期以「新旧键逐条对拍」验收（FR1/FR2）。
/// </para>
/// <para>
/// 本键只用于**合并去重/绑定**；显示键走 <see cref="IFrameKeyRenderer"/> 的键空间 <c>Display</c> 面，
/// 二者禁互用（FR2）。
/// </para>
/// </summary>
public static class FrameKey
{
    /// <summary>文法 id（键文法声明单源，见 <see cref="NodeStoreKeyGrammars.FrameGrammar"/>）。</summary>
    public const string GrammarId = "stream-frame";

    /// <summary>默认模板（逐字复刻既有键形）。</summary>
    public const string Template = "{run}|{round}|{seq}";

    /// <summary>缺失 run 段时的占位（不与真实值混淆）。</summary>
    public const string MissingRunToken = "-";

    /// <summary>按身份构造键（缺值不产出半成品；run 空 ⇒ <c>-</c>）。</summary>
    /// <param name="identity">帧身份。</param>
    public static string Build(FrameIdentity identity)
        => NodeStoreKeyGrammars.FrameGrammar.Build(ValuesOf(identity.RunId, identity.Round, identity.Seq));

    /// <summary>按轴值构造键；缺值 ⇒ <c>false</c> + 原因（不产出半成品）。</summary>
    /// <param name="runId">run 身份（可空）。</param>
    /// <param name="round">轮次。</param>
    /// <param name="seq">到达序。</param>
    /// <param name="key">键（成功时给出）。</param>
    /// <param name="reason">失败原因。</param>
    public static bool TryBuild(string? runId, int round, long seq, out string key, out string reason)
    {
        if (NodeStoreKeyGrammars.FrameGrammar.TryBuild(ValuesOf(runId, round, seq), out key, out var missing))
        {
            reason = string.Empty;
            return true;
        }

        key = string.Empty;
        reason = $"frame.key-missing（模板 '{Template}' 缺值: {string.Join(",", missing)}）";
        return false;
    }

    /// <summary>反解键 → 帧身份（不同形 ⇒ <c>false</c> + 原因，不猜）。</summary>
    /// <param name="key">帧身份键。</param>
    /// <param name="identity">反解产物（失败为 default）。</param>
    /// <param name="reason">失败原因。</param>
    public static bool TryParse(string? key, out FrameIdentity identity, out string reason)
    {
        identity = default;
        if (string.IsNullOrWhiteSpace(key))
        {
            reason = "frame.key-empty（键为空，不猜身份）";
            return false;
        }

        if (!KeyTemplateEvaluator.TryParseValues(Template, key!, out var values)
            || !values.TryGetValue("run", out var runText)
            || !values.TryGetValue("round", out var roundText)
            || !values.TryGetValue("seq", out var seqText))
        {
            reason = $"frame.key-unparsable（键 '{key}' 与模板 '{Template}' 不同形）";
            return false;
        }

        if (!int.TryParse(roundText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var round)
            || !long.TryParse(seqText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
        {
            reason = $"frame.key-shape-invalid（键 '{key}' 的 round/seq 非整数）";
            return false;
        }

        identity = new FrameIdentity(
            string.Equals(runText, MissingRunToken, StringComparison.Ordinal) ? null : runText,
            round,
            seq);
        reason = string.Empty;
        return true;
    }

    private static IReadOnlyDictionary<string, string> ValuesOf(string? runId, int round, long seq)
        => new Dictionary<string, string>(3, StringComparer.Ordinal)
        {
            ["run"] = string.IsNullOrWhiteSpace(runId) ? MissingRunToken : runId!,
            ["round"] = round.ToString(CultureInfo.InvariantCulture),
            ["seq"] = seq.ToString(CultureInfo.InvariantCulture),
        };
}
