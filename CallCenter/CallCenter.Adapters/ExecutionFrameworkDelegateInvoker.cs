using System.Text.Json;
using CallCenter.Core.Tools;
using CallCenter.Core.Tracks;
using ExecutionFramework.Engine;

namespace CallCenter.Adapters;

/// <summary>
/// 阶段 A 宿主接线的真实执行后端：把仓库既有 <see cref="FunctionExecutor"/>（ExecutionFramework，统一
/// 数组委托适配 / 异步解包 / 超时 / 取消 / 异常→ExecutionResult）适配为 <see cref="ICSharpDelegateInvoker"/>，
/// 使 CSharpTrack 的每次真实调用与 FunctionTesterFunctions.Func_Execute 内部的调用走同一执行栈，消除
/// CSharpTrack 直调（DynamicInvoke）与既有执行栈的平行分叉（对齐 SingleImplementation/TH-12）。
/// 入参绑定仍由 CSharpTrack 完成；本后端只负责真实调用与结果/异常映射。
/// </summary>
public sealed class ExecutionFrameworkDelegateInvoker : ICSharpDelegateInvoker
{
    private readonly FunctionExecutor _executor;

    public string Name => "executionframework.FunctionExecutor";

    public ExecutionFrameworkDelegateInvoker(FunctionExecutor executor)
        => _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public async Task<ToolExecutionResult> InvokeAsync(Delegate function, object?[] arguments, CancellationToken ct = default)
    {
        try
        {
            var exec = await _executor.ExecuteDelegateAsync(function, arguments, ct).ConfigureAwait(false);

            if (!exec.IsSuccess)
                return ToolExecutionResult.Fail(string.IsNullOrWhiteSpace(exec.ErrorMessage)
                    ? "执行失败"
                    : exec.ErrorMessage);

            var output = exec.Value == null ? "null" : JsonSerializer.Serialize(exec.Value);
            return ToolExecutionResult.Ok(output);
        }
        catch (Exception ex)
        {
            return ToolExecutionResult.Fail($"执行失败: {ex.InnerException?.Message ?? ex.Message}");
        }
    }
}
