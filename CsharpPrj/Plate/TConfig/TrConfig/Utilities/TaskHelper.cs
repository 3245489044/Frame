
namespace TConfig;

public static class TaskHelper
{
    /// <summary>
    /// 调用并解包异步委托（Task 解包唯一权威，TH-12 SingleImplementation）。
    /// 语义固化：
    ///  · 声明返回类型为 <see cref="Task"/>（非泛型）→ await 后返回 null（合法无值）；
    ///  · 声明返回类型为 <c>Task&lt;T&gt;</c> → await 后反射读取 .Result 原样返回（含 .Result==null）；
    ///  · 声明类型未知/非 Task → 运行时兜底，在类型层级中查找可赋值的 Task&lt;TResult&gt;
    ///    （能识别 async lambda 经 DynamicInvoke 返回的 AsyncStateMachineBox&lt;,&gt;，其
    ///    GetGenericTypeDefinition()!=Task&lt;&gt;，不能仅靠运行时 GetType() 判泛型），避免漏解包。
    /// </summary>
    public static async Task<object?> InvokeAndUnwrapAsync(
        Func<object?> invokeFunc,
        Type returnType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = invokeFunc();

        if (result is not Task task)
            return result;

        // 声明返回类型即非泛型 Task（无结果值）：await 后返回 null。
        if (returnType == typeof(Task))
        {
            await task.ConfigureAwait(false);
            return null;
        }

        await task.ConfigureAwait(false);

        // 声明返回类型为 Task<T>：以声明类型读 .Result（即便运行时为 AsyncStateMachineBox<TState,TResult>，
        // Result 属性继承自 Task<TResult>，在声明类型上仍可解析）。
        if (IsGenericTaskType(returnType))
            return GetTaskResult(task, returnType);

        // 运行时兜底：在 task 类型层级（含基类）找 Task<TResult>，兼容 async lambda 状态机盒。
        var resolved = ResolveGenericTaskType(task.GetType());
        return resolved is not null ? GetTaskResult(task, resolved) : null;
    }

    /// <summary>按 <see cref="ITrConfig"/> 声明返回类型调用并解包（注册表统一入口）。</summary>
    public static Task<object?> InvokeAndUnwrapAsync(
        ITrConfig config,
        object?[] args,
        CancellationToken cancellationToken = default)
        => InvokeAndUnwrapAsync(() => config.Invoke(args), config.ReturnType, cancellationToken);

    /// <summary>按委托声明返回类型调用并解包（注册表统一入口）。</summary>
    public static Task<object?> InvokeAndUnwrapAsync(
        Delegate function,
        object?[] args,
        CancellationToken cancellationToken = default)
        => InvokeAndUnwrapAsync(() => function.DynamicInvoke(args), function.Method.ReturnType, cancellationToken);

    private static bool IsGenericTaskType(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);

    /// <summary>在类型层级（含自身、基类）中查找第一个可赋值的 Task&lt;TResult&gt;；无则返回 null。</summary>
    private static Type? ResolveGenericTaskType(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsGenericTaskType(current))
                return current;
        }
        return null;
    }

    private static object? GetTaskResult(Task task, Type genericTaskType)
    {
        var resultProperty = genericTaskType.GetProperty("Result");
        return resultProperty?.GetValue(task);
    }
}
