namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 契约台账违规（校验失败即**阻断**，不 warn）：声明漂移必须当场可见。
/// <para>
/// 刻意**非 sealed**：消费方可派生子类以保留自身异常身份（既有 <c>catch</c> 面不破）。
/// </para>
/// </summary>
public class DeclarationViolationException : Exception
{
    /// <summary>单条违规（无明细时为空）。</summary>
    public DeclarationViolationException(string message) : base(message)
        => Violations = [];

    /// <summary>批量违规（多判据一次报全，便于一次修数据）。</summary>
    public DeclarationViolationException(string message, IReadOnlyList<string> violations) : base(message)
        => Violations = violations ?? [];

    /// <summary>违规明细（含判据编号，如 <c>R1</c>）。</summary>
    public IReadOnlyList<string> Violations { get; }
}
