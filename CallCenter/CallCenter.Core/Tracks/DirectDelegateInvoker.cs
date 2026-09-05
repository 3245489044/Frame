using System.Text.Json;
using CallCenter.Core.Tools;

namespace CallCenter.Core.Tracks;

/// <summary>
/// Core 缺省执行后端：DynamicInvoke + JSON 序列化 + 异常→失败。此即 CSharpTrack 原生内联逻辑的提取
/// （原 CallAsync 中 <c>DynamicInvoke → serialize → catch</c> 一段），作为 <see cref="ICSharpDelegateInvoker"/>
/// 的缺省实现，保证未注入宿主执行后端时行为零回归。
/// </summary>
public sealed class DirectDelegateInvoker : ICSharpDelegateInvoker
{
    public static DirectDelegateInvoker Instance { get; } = new();

    public string Name => "direct.DynamicInvoke";

    private DirectDelegateInvoker() { }

    public Task<ToolExecutionResult> InvokeAsync(Delegate function, object?[] arguments, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var result = function.DynamicInvoke(arguments);
            var output = result == null ? "null" : JsonSerializer.Serialize(result);
            return Task.FromResult(ToolExecutionResult.Ok(output));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolExecutionResult.Fail($"执行失败: {ex.InnerException?.Message ?? ex.Message}"));
        }
    }
}
