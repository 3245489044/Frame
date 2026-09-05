using System;

namespace Share.HostAssembly.Errors;

/// <summary>
/// 默认错误分类器 —— 基于常见异常类型与可空提示的启发式分类，作为框架默认实现。
/// 领域可注入自定义实现（<see cref="IErrorClassifier"/>）覆盖默认。
/// </summary>
public sealed class DefaultErrorClassifier : IErrorClassifier
{
    public ErrorCategory Classify(Exception? exception, ErrorCategory? hint = null)
    {
        if (hint is { } h && h != ErrorCategory.Unknown)
            return h;

        return exception switch
        {
            null => ErrorCategory.EmptyResult,
            TaskCanceledException or OperationCanceledException => ErrorCategory.Timeout,
            TimeoutException => ErrorCategory.Timeout,
            UnauthorizedAccessException or InsufficientMemoryException or OutOfMemoryException
                => ErrorCategory.ResourceExhausted,
            _ => ErrorCategory.Execution,
        };
    }
}
