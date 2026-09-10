
using System.Threading.Tasks;

namespace CommandFramework.Invoke;

public static class HandlerInvoker
{
    public static async Task InvokeAsync(Delegate handler, object context)
    {
        var handlerMethod = handler.Method;
        if (handlerMethod.ReturnType == typeof(Task)
            || handlerMethod.ReturnType == typeof(ValueTask))
        {
            var task = (Task)handler.DynamicInvoke(context)!;
            await task;
        }
        else
        {
            handler.DynamicInvoke(context);
        }
    }
}
