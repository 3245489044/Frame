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

    /// <summary>
    /// **调用唯一入口**：执行 + 期望异常归一 + 异步折取。折取（<see cref="Fold"/>）在归一保护之内 ——
    /// 「返回 Task 的方法内部抛期望异常」与同步抛出同语义（否则异常绕过 <paramref name="expect"/> 上抛）。
    /// <para>
    /// <paramref name="fold"/>=false（声明 <c>keepTask:true</c>，D2）时**不折取**：任务作为**值**返回，
    /// 供「Task 当数据传」的语义（下游编排 / <c>Task.WhenAll</c>）；缺省 true = 折取为同步值。
    /// </para>
    /// </summary>
    internal static object? Invoke(Func<object?> body, IReadOnlyList<Type>? expect, bool fold = true)
        => ExecuteAny(() => fold ? Fold(body()) : body(), expect);

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

    /// <summary>
    /// 反射构造唯一入口：<see cref="Activator.CreateInstance(Type, object[])"/> 会把构造函数抛出的业务异常
    /// 包成 <see cref="System.Reflection.TargetInvocationException"/> —— 这里拆包原样重抛（保留原始栈，
    /// 与方法调用 <see cref="ExecuteAny"/> 的拆包口径一致）；构造函数缺失等直接异常保持原样。
    /// </summary>
    internal static object Construct(Type type, object?[]? args)
    {
        try
        {
            return Activator.CreateInstance(type, args);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            ExceptionDispatchInfo.Capture(inner).Throw();
            throw new InvalidOperationException("不可达（ExceptionDispatchInfo.Throw 不返回）");
        }
    }

    /// <summary>异常归一：命中任一期望类型 → 异常对象即结果；否则原样上抛（保留原始栈）。</summary>
    internal static object? ExpectAny(Exception exception, IReadOnlyList<Type>? expect)
    {
        if (expect is { Count: > 0 } expected && expected.Any(type => type.IsInstanceOfType(exception)))
            return exception;

        ExceptionDispatchInfo.Capture(exception).Throw();
        throw new InvalidOperationException("不可达（ExceptionDispatchInfo.Throw 不返回）");
    }

    /// <summary>
    /// 任务类判定（D2 单源）：<c>Task</c> / <c>ValueTask</c> / <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c>。
    /// 消费方：装配期红线（形参声明任务类却收到已折取值）、返回值归一（<see cref="Realize"/>）。
    /// </summary>
    internal static bool IsAwaitable(Type type)
        => type == typeof(Task) || type == typeof(ValueTask)
           || (type.IsGenericType
               && (type.GetGenericTypeDefinition() == typeof(Task<>)
                   || type.GetGenericTypeDefinition() == typeof(ValueTask<>)));

    /// <summary>
    /// 返回值归一（**造任务**唯一实现，D2）：同步产物按声明返回类型包装为已完成任务
    /// （<c>Task</c> → <c>CompletedTask</c>；<c>Task&lt;T&gt;</c> → <c>Task.FromResult</c>；<c>ValueTask</c> / <c>ValueTask&lt;T&gt;</c> 同口径）；
    /// 非任务返回类型原样返回值。与 <see cref="Fold"/>（**拆任务**）构成互补对。
    /// </summary>
    internal static object? Realize(object? value, Type returnType)
    {
        if (returnType == typeof(Task)) return value as Task ?? Task.CompletedTask;

        // 非泛型 ValueTask 无结果：同步体 → 已完成 ValueTask。
        if (returnType == typeof(ValueTask)) return value is ValueTask existing ? existing : default(ValueTask);

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            if (value is Task) return value;
            return TaskOfResult(returnType, value);
        }

        // ValueTask<T>：同步产物（或 null）→ 包成已完成 ValueTask<T>；已是 ValueTask<T> 原样返回。
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            if (value is not null && value.GetType() == returnType) return value;

            var result = value ?? ReflectivePortResolver.DefaultFor(returnType.GetGenericArguments()[0]);
            return Construct(returnType, [result]);
        }

        return value;
    }

    /// <summary>已完成 <c>Task&lt;T&gt;</c> 包装（<c>Task.FromResult</c>，按声明结果类型闭合）。</summary>
    private static object TaskOfResult(Type taskType, object? value)
    {
        var resultType = taskType.GetGenericArguments()[0];
        var boxed = value ?? ReflectivePortResolver.DefaultFor(resultType);
        return typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType).Invoke(null, new[] { boxed })!;
    }

    /// <summary>
    /// 异常构造唯一入口（G1 应答脚本 / G3 脚本步骤 <c>throw</c> 共用）：按声明类型名构造异常实例。
    /// <para>
    /// 判据：类型须可解析且为 <see cref="Exception"/> 子类；声明的消息须有 <c>(string)</c> 构造可施加；
    /// 构造不可用即抛（**禁静默回落**为无参构造 / 丢弃消息）。类型解析口径 = <see cref="ReflectivePortResolver.ResolveType"/>。
    /// </para>
    /// </summary>
    internal static Exception ForgeThrow(string typeName, string? message, string context)
    {
        var type = ReflectivePortResolver.ResolveType(typeName);

        if (!typeof(Exception).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"[port:throw] 声明的抛出类型 '{typeName}' 不是异常类型（{context}）");

        var textual = type.GetConstructor([typeof(string)]);
        var parameterless = type.GetConstructor(Type.EmptyTypes);

        if (message is not null)
            return textual is not null
                ? (Exception)textual.Invoke([message])
                : throw new InvalidOperationException(
                    $"[port:throw] 抛出类型 '{typeName}' 无 (string) 构造，无法施加声明消息（{context}）");

        if (parameterless is not null) return (Exception)parameterless.Invoke(null);
        if (textual is not null) return (Exception)textual.Invoke([typeName]);

        throw new InvalidOperationException(
            $"[port:throw] 抛出类型 '{typeName}' 无可用构造（需 () 或 (string)；{context}）");
    }

    private static object? TaskResult(Task task)
    {
        var type = task.GetType();
        return type.IsGenericType ? type.GetProperty(nameof(Task<int>.Result))!.GetValue(task) : null;
    }
}
