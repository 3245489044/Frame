
using System.Collections.Generic;
using System.Threading.Tasks;
using Share.EventBindingOrchestrator.Execution;

namespace Share.EventBindingOrchestrator.Abstractions;

public sealed record FunctionCallFacadeResult(
    bool IsSuccess,
    string? Output,
    string? ErrorMessage,
    long ElapsedMs,
    // 阶段2.2：函数渲染副作用元数据（随执行结果携带，供事件发布/渲染桥消费，见 RenderingAffinity）
    RenderingAffinity RenderingAffinity = RenderingAffinity.None);

public interface IEventPlatform
{
    Task<FunctionCallFacadeResult> InvokeFunctionAsync(
        string functionName,
        Dictionary<string, object?>? parameters = null,
        string? source = null,
        int timeoutMs = 30_000,
        string scope = "");

    FunctionCallFacadeResult InvokeFunction(
        string functionName,
        Dictionary<string, object?>? parameters = null,
        string? source = null,
        int timeoutMs = 30_000,
        string scope = "");
}
