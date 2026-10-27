namespace Operation.HierarchyMethod.History.Content;

/// <summary>
/// 轮次归档结果（B5b）：<see cref="Archived"/> = 本轮原文是否已落库；<see cref="Messages"/> = 本次消息条数
/// （与归档条数分离，供账本对账复算）；<see cref="Reason"/> = 未归档的显式归因（成功为空串）。
/// <para>纪律：空归档必须带 reason（<c>AX.NoSilentOutlet</c>），禁裸 return。</para>
/// </summary>
/// <param name="Archived">是否已归档（false ⇒ 必有 reason）。</param>
/// <param name="Messages">本次消息条数（解析失败为 0，不退化为成功判据）。</param>
/// <param name="Reason">未归档归因（成功为空串）。</param>
public readonly record struct RoundArchiveOutcome(bool Archived, int Messages, string Reason)
{
    /// <summary>已归档。</summary>
    public static RoundArchiveOutcome Ok(int messages) => new(true, messages, string.Empty);

    /// <summary>未归档（显式归因）。</summary>
    public static RoundArchiveOutcome Skipped(string reason, int messages = 0) => new(false, messages, reason);
}
