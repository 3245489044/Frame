namespace Usharp.Semantics;

/// <summary>
/// 失败姿态内核：可成功/可带原因失败（**禁止同函数内又抛又吞**）。
/// </summary>
/// <param name="Ok">是否成功。</param>
/// <param name="Value">成功值（失败为 default）。</param>
/// <param name="Reason">失败原因（成功为 null）。</param>
public readonly record struct UsharpTry<T>(bool Ok, T Value, string? Reason)
{
    /// <summary>成功。</summary>
    public static UsharpTry<T> Success(T value) => new(true, value, null);

    /// <summary>失败（带原因）。</summary>
    public static UsharpTry<T> Failure(string reason) => new(false, default!, reason);

    /// <summary>分支消费。</summary>
    public TOut Match<TOut>(Func<T, TOut> ok, Func<string, TOut> fail)
        => Ok ? ok(Value) : fail(Reason ?? "(unknown)");
}

/// <summary>一次字段回填/转换失败（路径 + 原因）。</summary>
/// <param name="Reason">失败原因（如 <c>enum-invalid</c> / <c>depth-limit</c>）。</param>
/// <param name="Path">字段路径（如 <c>Task.State</c>）。</param>
/// <param name="File">归因文件（可空）。</param>
/// <param name="Line">归因行（可 0）。</param>
public sealed record UsharpFailure(string Reason, string Path, string File = "", int Line = 0);

/// <summary>
/// 一次对象回填的可见化报告：成功字段数 / 是否因深度截断 / 全部失败。
/// </summary>
/// <param name="Applied">成功回填的成员数。</param>
/// <param name="TruncatedAtDepth">是否触发深度上限。</param>
/// <param name="Failures">失败列表。</param>
public sealed record UsharpFillReport(
    int Applied, bool TruncatedAtDepth, IReadOnlyList<UsharpFailure> Failures)
{
    /// <summary>空报告。</summary>
    public static readonly UsharpFillReport Empty =
        new(0, false, Array.Empty<UsharpFailure>());

    /// <summary>是否有失败或截断（任一 ⇒ 不得静默视为成功）。</summary>
    public bool HasIssues => TruncatedAtDepth || Failures.Count > 0;
}

/// <summary>登记类端口的统一结果（成功 / 冲突 + 冲突键）。</summary>
/// <param name="Registered">是否登记成功。</param>
/// <param name="ConflictId">冲突键（失败时）。</param>
public sealed record UsharpRegistrationResult(bool Registered, string? ConflictId)
{
    /// <summary>成功。</summary>
    public static readonly UsharpRegistrationResult Success = new(true, null);

    /// <summary>冲突。</summary>
    public static UsharpRegistrationResult Conflict(string? conflictId) => new(false, conflictId);
}
