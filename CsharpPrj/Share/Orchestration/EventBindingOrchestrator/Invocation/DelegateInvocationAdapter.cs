#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FrameworkAbstractions.ComponentRegistry;
using TConfig;

namespace Share.EventBindingOrchestrator.Invocation;

/// <summary>
/// 默认适配器：经 IEventExecutionService（FunctionHub → 工作流/RGE → 注册委托）执行函数。
/// 保留既有执行路径的能力，但统一纳入内核生命周期/分类/观测，并取消贯穿到执行服务。
/// </summary>
public sealed class DelegateInvocationAdapter : IInvocationAdapter
{
    public bool SupportsCancellation => true;

    public async Task<object?> InvokeAsync(InvocationRequest request, IInvocationContext ctx)
    {
        var svc = AppServiceProvider.Resolve<IEventExecutionService>();
        if (svc == null)
            throw new InvalidOperationException("IEventExecutionService 未注册到 AppServiceProvider");

        var parameters = new Dictionary<string, object?>(request.Parameters);
        var result = await svc.ExecuteFunctionAsync(request.FunctionName, parameters, ctx.CancellationToken)
            .ConfigureAwait(false);

        // 执行服务返回失败：抛异常使其归入 FunctionFault（函数自身失败），而非静默成功。
        if (result != null && !result.IsSuccess)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? $"函数 '{request.FunctionName}' 执行失败"
                    : result.ErrorMessage);

        return result?.Value;
    }
}
