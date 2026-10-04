namespace BasicMethod.Initialization.Errors;

/// <summary>
/// 执行期结果（不吞错）：把 try/catch 的处置统一为可记账的 Outcome。
/// 纯值类型；分类由 <c>IErrorClassifier</c> 提供，记账由 <c>FailSink</c> 承担。
/// </summary>
public readonly struct ErrorOutcome
{
    /// <summary>是否成功。</summary>
    public bool Ok { get; }

    /// <summary>操作标识（用于诊断与账目归因）。</summary>
    public string Op { get; }

    /// <summary>错误分类（成功时为 null）。</summary>
    public ErrorClass? Class { get; }

    /// <summary>原始异常（成功时为 null）。</summary>
    public Exception? Error { get; }

    private ErrorOutcome(bool ok, string op, ErrorClass? cls, Exception? error)
    {
        Ok = ok;
        Op = op;
        Class = cls;
        Error = error;
    }

    /// <summary>成功结果。</summary>
    public static ErrorOutcome Success(string op) => new(true, op, null, null);

    /// <summary>失败结果（分类由调用方先行完成）。</summary>
    public static ErrorOutcome From(Exception ex, string op, ErrorClass cls) => new(false, op, cls, ex);
}
