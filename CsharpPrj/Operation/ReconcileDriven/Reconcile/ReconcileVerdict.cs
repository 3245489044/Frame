namespace Operation.ReconcileDriven.Reconcile;

/// <summary>
/// 对账裁决（声明信号契约 vs 实际信号流）四判结果。
/// </summary>
/// <param name="Passed">是否通过（四判全空且终止满足）。</param>
/// <param name="Missing">完整性失配：应发未发的 EventKind 清单（C/R1）。</param>
/// <param name="OutOfOrder">顺序失配：未按声明序成链的 EventKind 清单（R2）。</param>
/// <param name="TerminalMissing">终止信号缺失（R3）。</param>
/// <param name="ScopeMismatch">归属串台：<c>{kind}@{scope}</c> 清单（R4）。</param>
public sealed record ReconcileVerdict(
    bool Passed,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> OutOfOrder,
    bool TerminalMissing,
    IReadOnlyList<string> ScopeMismatch)
{
    /// <summary>首个失配判据号（通过时返回 null；用于落账 <c>code</c> 字段）。</summary>
    public string? FailingCode
    {
        get
        {
            if (Passed) return null;
            if (Missing.Count > 0) return ReconcileCodes.R1Missing;
            if (OutOfOrder.Count > 0) return ReconcileCodes.R2OutOfOrder;
            if (TerminalMissing) return ReconcileCodes.R3TerminalMissing;
            if (ScopeMismatch.Count > 0) return ReconcileCodes.R4ScopeMismatch;
            return null;
        }
    }

    /// <summary>通过裁决。</summary>
    public static ReconcileVerdict Pass() => new(
        true, Array.Empty<string>(), Array.Empty<string>(), false, Array.Empty<string>());

    /// <summary>一行式摘要（诊断/异常消息用）。</summary>
    public string ToSummary()
        => Passed
            ? "PASS"
            : $"FAIL code={FailingCode} missing=[{string.Join(",", Missing)}] " +
              $"outOfOrder=[{string.Join(",", OutOfOrder)}] terminalMissing={TerminalMissing} " +
              $"scopeMismatch=[{string.Join(",", ScopeMismatch)}]";
}
