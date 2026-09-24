using System;
using System.Collections.Generic;
using System.Linq;
using Share.Conversation.Ids;

namespace Share.Conversation.Facts;

/// <summary>
/// 会话事实文档（方案 §2.1）——**一轮 = 一份 append-only 事实文档**，文档由若干事实条目组成、由轮级键归属。
/// <para>
/// 只读视图：本类由存储读回后构造（<see cref="Of"/>）；写入只经 <see cref="SessionFactWriter"/>（F1 单写者）。
/// </para>
/// </summary>
public sealed class FactDocument
{
    /// <summary>构造文档视图（条目按序号升序归一）。</summary>
    /// <param name="key">文档归属键（轮级）。</param>
    /// <param name="facts">事实条目（可乱序，构造时按 <see cref="FactEnvelope.Seq"/> 升序）。</param>
    public FactDocument(FactId key, IReadOnlyList<FactEnvelope>? facts)
    {
        Key = key;
        Facts = (facts ?? Array.Empty<FactEnvelope>())
            .OrderBy(f => f.Seq)
            .ToList();
        NextSeq = Facts.Count == 0 ? 0 : Facts[^1].Seq + 1;
        Completeness = FactCompleteness.Of(Facts);
        FinalAnswer = Facts.LastOrDefault(f => f.Kind == FactKindNames.FinalAnswer);
    }

    /// <summary>文档归属键（= 轮键）。</summary>
    public FactId Key { get; }

    /// <summary>事实条目（按序号升序；只增不丢）。</summary>
    public IReadOnlyList<FactEnvelope> Facts { get; }

    /// <summary>下一个可用序号（同文档内单调无空洞，F3）。</summary>
    public long NextSeq { get; }

    /// <summary>完备性（可机检的「这一轮是否完整」）。</summary>
    public FactCompleteness Completeness { get; }

    /// <summary>终答事实（无则 null —— 上层须经完备性判定，**不得**以叙述冒充终答）。</summary>
    public FactEnvelope? FinalAnswer { get; }

    /// <summary>构造文档视图（唯一入口）。</summary>
    /// <param name="key">文档归属键。</param>
    /// <param name="facts">事实条目。</param>
    public static FactDocument Of(FactId key, IEnumerable<FactEnvelope>? facts)
        => new(key, facts?.ToList());

    /// <summary>取该类别全部条目（按序号升序）。</summary>
    /// <param name="kind">事实类别（线格式值；载体侧经 <c>SessionFactKind.ToString()</c> 传入）。</param>
    public IReadOnlyList<FactEnvelope> OfKind(string kind)
        => Facts.Where(f => f.Kind == kind).ToList();

    /// <summary>取该类别最后一条（无则 null）。</summary>
    /// <param name="kind">事实类别（线格式值）。</param>
    public FactEnvelope? LastOf(string kind)
        => Facts.LastOrDefault(f => f.Kind == kind);

    /// <summary>该类别条目数（计数由事实机械复算，不含 LLM 判断）。</summary>
    /// <param name="kind">事实类别（线格式值）。</param>
    public int CountOf(string kind) => Facts.Count(f => f.Kind == kind);
}
