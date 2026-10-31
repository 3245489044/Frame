namespace DataCenter.Protocol.Dataflow;

/// <summary>追溯字段名（跨语言交换的唯一拼写源；禁各处手写字符串键）。</summary>
public static class TraceStampFields
{
    /// <summary>写入者标识。</summary>
    public const string Writer = "writer";

    /// <summary>写入时刻（UTC）。</summary>
    public const string At = "at";

    /// <summary>进程内单调序号（同一写入者连续写入可排序）。</summary>
    public const string Seq = "seq";

    /// <summary>因果来源（域侧显式传入；Core 不推断语义）。</summary>
    public const string CausedBy = "causedBy";
}

/// <summary>
/// 追溯盖章（G2 缺口的最小闭环）—— <c>writer</c> / <c>at</c> / <c>seq</c> 三要素，
/// 与域侧 <c>causedBy</c> 合成四要素判据（K7 抽样齐备率）。
/// <para>
/// 纪律：时间戳<b>不进</b>图投影产物（投影幂等要求同状态两次投影字节级一致）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-可追踪数据流图框架-设计与实施.md §5.4（D2 设计首次落地）。
/// </summary>
/// <param name="Writer">写入者（如组件名；非空）。</param>
/// <param name="At">写入时刻（UTC）。</param>
/// <param name="Seq">序号（&gt; 0；由盖章方单调分配）。</param>
public sealed record TraceStamp(string Writer, DateTimeOffset At, long Seq)
{
    /// <summary>三要素是否齐备（writer 非空 ∧ seq &gt; 0；at 由构造保证有值）。</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(Writer) && Seq > 0;

    /// <summary>线上属性形态（供图/台账附加；键名取自 <see cref="TraceStampFields"/>）。</summary>
    public IReadOnlyDictionary<string, object?> ToProperties() => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [TraceStampFields.Writer] = Writer,
        [TraceStampFields.At] = At.ToUniversalTime().ToString("O"),
        [TraceStampFields.Seq] = Seq,
    };
}
