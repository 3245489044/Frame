using System;

using Strategy.Refresh.Model;

namespace Strategy.Refresh.Ports
{
    /// <summary>
    /// 单一状态源（L0 契约，通用）：每 slotKey → 版本水位 / 指纹 / 连续无进展 / 退避 / 最近结论。
    /// <para><b>判定规则（不可协商）</b>：</para>
    /// <list type="bullet">
    /// <item><c>version &lt; baseline[slotKey]</c> ⇒ <see cref="AcceptDecision.Stale"/>（后到的旧快照过期即丢）。</item>
    /// <item><c>fingerprint == last[slotKey]</c> ⇒ <see cref="AcceptDecision.Skip"/>（内容未变）。</item>
    /// <item>其余 ⇒ <see cref="AcceptDecision.Apply"/>。</item>
    /// </list>
    /// <para>只有「送达且内容变化」才 <see cref="Commit"/> 推进 baseline；未送达 / 未变化不推进。</para>
    /// </summary>
    public interface IRefreshStore
    {
        /// <summary>单调分配该 slot 的下一个版本号（同一状态源，唯一分配处）。</summary>
        long NextVersion(string slotKey);

        /// <summary>按判定规则给出版本/指纹决策（判定处唯一）。</summary>
        AcceptDecision Decide(string slotKey, long version, string fingerprint);

        /// <summary>交付落地且内容变化后推进 baseline 与 last 指纹（仅此一路写版本水位）。</summary>
        void Commit(string slotKey, long version, string fingerprint);

        /// <summary>读取该槽的反馈状态（未观测 ⇒ <see cref="SlotRefreshState.Empty"/>）。</summary>
        SlotRefreshState GetState(string slotKey);

        /// <summary>记录一次刷新结论（推进 / 重置连续无进展与退避）。</summary>
        void RecordOutcome(
            string slotKey,
            RefreshOutcome outcome,
            string fingerprint,
            string? progressToken,
            DateTimeOffset at);

        /// <summary>重置退避与无进展水位（Change / Manual 触发时调用）。</summary>
        void ResetProgress(string slotKey, DateTimeOffset at);
    }
}

