using System;
using System.Text.RegularExpressions;

namespace Share.Conversation.Ids;

/// <summary>
/// 键归一结果（读侧一次性使用）：<see cref="Key"/> 非空表示**已归因**（可进事实归属轴）；
/// 否则为**未归因事实** —— 仅登记 <see cref="ExternalRef"/> 与原因，**禁止猜测轮号**
/// （对齐方案 §1.6「不猜测」与 <c>AX-FRAME.CriterionWellFormedness</c>：无判据即不可判定）。
/// </summary>
/// <param name="Key">归一后的键（未归因时为 null）。</param>
/// <param name="Legacy">是否来自存量数据（投影时标 <c>source=Legacy</c>）。</param>
/// <param name="ExternalRef">外部代号（未归因时承载原值）。</param>
/// <param name="Reason">归一 / 未归因原因（可见化依据）。</param>
public sealed record FactKeyNormalization(FactId? Key, bool Legacy, ExternalRef? ExternalRef, string? Reason)
{
    /// <summary>是否已归因（可作事实归属键）。</summary>
    public bool Attributed => Key is not null;

    /// <summary>已归因：键 + 可选外部引用。</summary>
    /// <param name="key">归一后的键。</param>
    /// <param name="legacy">是否存量数据。</param>
    /// <param name="externalRef">可选外部引用。</param>
    public static FactKeyNormalization Of(FactId key, bool legacy = false, ExternalRef? externalRef = null)
        => new(key, legacy, externalRef, null);

    /// <summary>未归因：保留原因（读侧须可见化，禁静默丢弃）。</summary>
    /// <param name="reason">未归因原因。</param>
    /// <param name="externalRef">可选外部引用。</param>
    public static FactKeyNormalization Unattributed(string reason, ExternalRef? externalRef = null)
        => new(null, Legacy: true, externalRef, reason);
}

/// <summary>
/// 存量键形态归一（方案 §1.6）——**纯函数映射，读侧一次性归一，不做批量改库**
/// （对齐 <c>AX-CORE.InformationMonotonicity</c>：只增不丢，不回改历史行）。
/// <para>
/// 映射表：<c>default@1 → default#R1</c>；<c>defaultR1 → default#R1</c>；
/// <c>default#1#1 → 未归因</c>（<c>gen/seq</c> 无轮号信息，禁猜测，标 <c>Lease</c> 外部引用）；
/// <c>conv_*</c> / 时间戳 id → 未归因（仅 <c>TsSdk</c> 外部引用）。
/// </para>
/// </summary>
public static class FactIdMigration
{
    private static readonly Regex AtRoundRegex =
        new(FactIdGrammar.LegacyAtRoundPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ObservedRoundRegex =
        new(FactIdGrammar.LegacyObservedRoundPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LeaseRegex =
        new(FactIdGrammar.LegacyLeasePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExternalKeyRegex =
        new(FactIdGrammar.LegacyExternalKeyPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 归一存量键（新形态直接返回、不标 legacy）。
    /// </summary>
    /// <param name="legacyKey">存量键串（可为 null / 空白 = 未归因事实）。</param>
    public static FactKeyNormalization Normalize(string? legacyKey)
    {
        if (string.IsNullOrWhiteSpace(legacyKey))
            return FactKeyNormalization.Unattributed("空键：无归属信息（未归因事实）");

        var key = legacyKey!.Trim();

        // ① 已是新形态（轮 / 步 / 调用键）：直接采用，不标 legacy。
        if (FactIdGrammar.TryParse(key, out var parsed)) return FactKeyNormalization.Of(parsed);

        // ② 框架轮次键 {taskKey}@{seq} → #R{seq}。
        var atRound = AtRoundRegex.Match(key);
        if (atRound.Success && FactIdGrammar.IsId(atRound.Groups[1].Value)
            && TryPositive(atRound.Groups[2].Value, out var atRoundNo))
            return FactKeyNormalization.Of(FactIds.Round(atRound.Groups[1].Value, atRoundNo), legacy: true);

        // ③ 观测轮次键 {runId}R{n} → #R{n}（存量观测事件的读侧归一）。
        //    判定序口径：**存量优先** —— 形如 `<prefix>R<digits>` 的串优先解读为存量观测轮次键，
        //    不作裸会话键（两者字符集重叠，取存量语义可避免把历史事件误判为 run 级事实）。
        var observed = ObservedRoundRegex.Match(key);
        if (observed.Success && FactIdGrammar.IsId(observed.Groups[1].Value)
            && TryPositive(observed.Groups[2].Value, out var observedRoundNo))
            return FactKeyNormalization.Of(
                FactIds.Round(observed.Groups[1].Value, observedRoundNo), legacy: true);

        // ④ 轮次租约键 {taskKey}#{gen}#{seq}：无轮号信息 ⇒ 不猜（标未归因而非伪造轮号）。
        if (LeaseRegex.IsMatch(key))
            return FactKeyNormalization.Unattributed(
                "轮次租约键（gen/seq 无轮号信息）——标识资格已废除，禁猜测轮号", ExternalRef.Lease(key));

        // ⑤ SDK 外部代号：禁作会话键 / 关联键，仅登记为外部引用（同样存量优先于裸会话键判定）。
        if (ExternalKeyRegex.IsMatch(key))
            return FactKeyNormalization.Unattributed(
                "执行面外部代号——禁作会话键/关联键，仅作 ExternalRef", ExternalRef.TsSdk(key));

        // ⑥ 裸会话键（run 级键形态）：会话级归属合法，非存量形态。
        if (FactIdGrammar.IsId(key)) return FactKeyNormalization.Of(FactIds.Run(key));

        return FactKeyNormalization.Unattributed("未识别形态：不归一（禁止猜测）");
    }

    private static bool TryPositive(string value, out int number)
        => int.TryParse(value, System.Globalization.NumberStyles.None,
               System.Globalization.CultureInfo.InvariantCulture, out number) && number > 0;
}
