namespace Strategy.Observability.Ports;

/// <summary>
/// 端口错误分类（内核**唯一分类器**）：异常 → 边界契约 error 枚举域的短名。
/// <para>
/// 口径分工：记录面（<see cref="PortCallView.ErrorClass"/>）保存**异常类型名**（出证需可定位到具体类型）；
/// 事件面（PortFailed 的 <c>errorClass</c>）保存本分类短名（契约域可比、可跨实现聚合）。两处口径不同是刻意的，
/// 但「超时族的判定」只在本件实现一次。
/// </para>
/// </summary>
public static class PortErrorClass
{
    /// <summary>契约域短名：超时（含取消：调用方取消与超时在预算口径上同属 fatal 类）。</summary>
    public const string Timeout = "timeout";

    /// <summary>契约域短名：执行失败（兜底）。</summary>
    public const string ExecutionFailure = "execution_failure";

    /// <summary>异常 → 契约域短名（名称含 <c>Timeout</c> 的派生异常同样归超时族）。</summary>
    public static string Of(Exception exception)
    {
        PortGuard.Required(exception, nameof(exception));

        return exception switch
        {
            OperationCanceledException => Timeout,
            TimeoutException => Timeout,
            _ when exception.GetType().Name.Contains("Timeout", StringComparison.Ordinal) => Timeout,
            _ => ExecutionFailure,
        };
    }
}
