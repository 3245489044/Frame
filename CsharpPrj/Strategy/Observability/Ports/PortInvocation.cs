using System.Runtime.ExceptionServices;

namespace Strategy.Observability.Ports;

/// <summary>
/// 调用结果归一单源（组合绑定的调用与脚本绑定的步骤共用）：异步折取 + 期望异常归一。
/// <para>语义锁定（与旧夹具逐字等价）：<c>await</c> 折同步且异常原样上抛；
/// <c>expect</c> 命中 ⇒ 结果 = 该异常对象（供 <c>message</c>/<c>thrown</c> 取值），未命中即原样上抛。</para>
/// </summary>
internal static class PortInvocation
{
    /// <summary>执行并把异常按期望类型归一（单类型快捷形态；null = 不捕获）。</summary>
    internal static object? Execute(Func<object?> body, Type? expect)
        => ExecuteAny(body, expect is null ? null : new[] { expect });

    /// <summary>执行并把异常按期望类型集归一（任一命中 → 异常对象即结果；否则原样上抛）。</summary>
    internal static object? ExecuteAny(Func<object?> body, IReadOnlyList<Type>? expect)
    {
        try
        {
            return body();
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            return ExpectAny(inner, expect);
        }
        catch (Exception ex)
        {
            return ExpectAny(ex, expect);
        }
    }

    /// <summary>
    /// 异步折取（W10 定档：**缺省自动折取**）：<c>Task</c>/<c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c> 同步取值（异常原样上抛）；
    /// 非 awaitable 值原样返回（不再由声明开关决定，声明 <c>await</c> 降级为提示性字段）。
    /// </summary>
    internal static object? Fold(object? value)
    {
        if (value is null) return value;

        switch (value)
        {
            case Task task:
                task.GetAwaiter().GetResult();
                return TaskResult(task);

            case ValueTask valueTask:
                valueTask.GetAwaiter().GetResult();
                return null;
        }

        var type = value.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTask = (Task)type.GetMethod(nameof(ValueTask<int>.AsTask))!.Invoke(value, null)!;
            asTask.GetAwaiter().GetResult();
            return TaskResult(asTask);
        }

        return value;
    }

    /// <summary>异常归一：命中任一期望类型 → 异常对象即结果；否则原样上抛（保留原始栈）。</summary>
    internal static object? ExpectAny(Exception exception, IReadOnlyList<Type>? expect)
    {
        if (expect is { Count: > 0 } expected && expected.Any(type => type.IsInstanceOfType(exception)))
            return exception;

        ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("不可达（ExceptionDispatchInfo.Throw 不返回）");
    }

    private static object? TaskResult(Task task)
    {
        var type = task.GetType();
        return type.IsGenericType ? type.GetProperty(nameof(Task<int>.Result))!.GetValue(task) : null;
    }
}
