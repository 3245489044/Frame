using BasicMethod.Declarative.Ledger;

namespace DataCenter.Standards;

/// <summary>
/// 标准一致性违规（校验失败即**阻断**，不 warn）——对齐上位方案「契约即断言」：
/// 声明漂移必须当场可见（设计文档 §3.4 纪律）。
/// <para>
/// 类型身份保持不变（消费侧既有 <c>catch</c> 面不改）：机制宿主在框架，异常基类为
/// <see cref="DeclarationViolationException"/>。
/// </para>
/// </summary>
public sealed class StandardViolationException : DeclarationViolationException
{
    /// <summary>单条违规（无明细时为空）。</summary>
    public StandardViolationException(string message) : base(message)
    {
    }

    /// <summary>批量违规（多判据一次报全，便于一次修数据）。</summary>
    public StandardViolationException(string message, IReadOnlyList<string> violations) : base(message, violations)
    {
    }
}
