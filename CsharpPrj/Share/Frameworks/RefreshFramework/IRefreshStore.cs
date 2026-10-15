namespace Share.RefreshFramework
{
    /// <summary>
    /// 单一状态源（L0 契约，通用）：每 slotKey → { Baseline, LastFingerprint, Next }。
    /// <para><b>判定规则（不可协商）</b>：</para>
    /// <list type="bullet">
    /// <item><c>version &lt; baseline[slotKey]</c> ⇒ <see cref="AcceptDecision.Stale"/>（后到的旧快照过期即丢）。</item>
    /// <item><c>fingerprint == last[slotKey]</c> ⇒ <see cref="AcceptDecision.Skip"/>（内容未变）。</item>
    /// <item>其余 ⇒ <see cref="AcceptDecision.Apply"/>。</item>
    /// </list>
    /// <para>落地成功（sink 返回 true）才 <see cref="Commit"/> 推进 baseline；未送达则不推进（可重试）。</para>
    /// </summary>
    public interface IRefreshStore
    {
        /// <summary>单调分配该 slot 的下一个版本号（同一状态源，唯一分配处）。</summary>
        long NextVersion(string slotKey);

        /// <summary>按判定规则给出版本/指纹决策（判定处唯一）。</summary>
        AcceptDecision Decide(string slotKey, long version, string fingerprint);

        /// <summary>交付落地成功后推进 baseline 与 last 指纹（仅此一路写状态）。</summary>
        void Commit(string slotKey, long version, string fingerprint);
    }
}
