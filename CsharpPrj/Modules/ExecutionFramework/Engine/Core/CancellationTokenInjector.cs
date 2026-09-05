using System.Reflection;

namespace ExecutionFramework.Engine;

/// <summary>
/// 取消令牌注入器（P3）：若委托声明了 <see cref="CancellationToken"/> 参数（通常在末位），
/// 把调用方 ct 注入到对应位置，使框架取消能贯穿到真正执行长任务/子进程的函数（如 ClineClient）。
/// 未声明 ct 参数的委托原样返回（零影响）；数组委托（唯一参数为 object[]）不做注入。
/// </summary>
public static class CancellationTokenInjector
{
    public static object?[] Inject(Delegate function, object[] args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(function);

        var parameters = function.Method.GetParameters();
        if (parameters.Length == 0)
            return Array.Empty<object?>();

        // 数组委托（唯一参数为 object[]）：把整个参数数组作为单参传入，不注入 ct。
        bool isArrayDelegate = parameters.Length == 1 && parameters[0].ParameterType == typeof(object[]);
        if (isArrayDelegate)
            return new object?[] { args };

        int ctIndex = -1;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                ctIndex = i;
                break;
            }
        }
        if (ctIndex < 0)
            return args;

        var bound = new object?[parameters.Length];
        Array.Copy(args, bound, Math.Min(args.Length, parameters.Length));
        bound[ctIndex] = ct;
        return bound;
    }
}
