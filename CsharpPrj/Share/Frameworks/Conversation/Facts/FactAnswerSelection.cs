using System.Collections.Generic;

namespace Share.Conversation.Facts;

/// <summary>
/// 终答事实选取（**单一实现**）：权威级高者胜，同级 last-wins；低权威不得覆盖权威交付。
/// <para>
/// 评估顺序 = 传入顺序（<see cref="FactDocument"/> 的 <c>Facts</c> 已按 Seq 升序归一；
/// <see cref="FactCompleteness.Of"/> 用调用方给定顺序）——本件不做排序，避免改变既有语义。
/// </para>
/// </summary>
internal static class FactAnswerSelection
{
    /// <summary>从事实集合选出终答条目（无 ⇒ null）。</summary>
    /// <param name="facts">待评估事实（按期望的评估顺序）。</param>
    public static FactEnvelope? SelectFinalAnswer(IEnumerable<FactEnvelope> facts)
    {
        FactEnvelope? selected = null;
        var selectedRank = -1;
        foreach (var fact in facts)
        {
            if (fact.Kind != FactKindNames.FinalAnswer) continue;

            var rank = FactCompleteness.AuthorityRank(fact.Authority);
            if (selected is null || rank >= selectedRank)
            {
                selected = fact;
                selectedRank = rank;
            }
        }

        return selected;
    }
}
