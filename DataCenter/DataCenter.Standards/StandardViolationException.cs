namespace DataCenter.Standards;

/// <summary>
/// 标准一致性违规（校验失败即**阻断**，不 warn）——对齐上位方案「契约即断言」：
/// 声明漂移必须当场可见（设计文档 §3.4 纪律）。
/// </summary>
public sealed class StandardViolationException : Exception
{
    /// <summary>单条违规（无明细时为空）。</summary>
    public StandardViolationException(string message) : base(message)
        => Violations = [];

    /// <summary>批量违规（多判据一次报全，便于一次修数据）。</summary>
    public StandardViolationException(string message, IReadOnlyList<string> violations) : base(message)
        => Violations = violations ?? [];

    /// <summary>违规明细（含判据编号，如 <c>C2</c>）。</summary>
    public IReadOnlyList<string> Violations { get; }
}
