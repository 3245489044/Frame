using System;
using System.Threading.Tasks;

namespace Share.HostAssembly.Errors;

/// <summary>
/// 类型化边界结果 —— 错误边界统一包装后的产物：成功（<paramref name="Ok"/> 为 true）携带结果；
/// 失败携带分类与保留的完整异常（类型/堆栈），不降级为纯字符串。
/// </summary>
public sealed record BoundaryResult<T>(
    bool Ok,
    T? Value = default,
    ErrorCategory Category = ErrorCategory.Unknown,
    string? Error = null,
    Exception? Exception = null)
{
    public static BoundaryResult<T> Success(T value) => new(true, value);
    public static BoundaryResult<T> Failure(ErrorCategory category, string error, Exception? ex = null)
        => new(false, default, category, error, ex);
}
