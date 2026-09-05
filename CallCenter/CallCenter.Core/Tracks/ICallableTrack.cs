using CallCenter.Core.Tools;

namespace CallCenter.Core.Tracks;

/// <summary>一次工具调用的执行结果（统一返回给调用方/上层，不绑定语言类型）。</summary>
public sealed record ToolExecutionResult(
    bool Success,
    string? OutputJson,
    string? Error)
{
    public static ToolExecutionResult Ok(string? outputJson = null)
        => new(true, outputJson, null);

    public static ToolExecutionResult Fail(string error)
        => new(false, null, error);
}

/// <summary>
/// 终端面可调用轨（Runtime 轨抽象）。每种后端轨道（C#Track / TSTrack / PSTrack / ExternalMCPTrack）
/// 以一个实现把 <see cref="ToolDescriptor"/> 与"按名调用"能力绑定，供 <see cref="Router.ToolRouter"/> 调度。
/// 控制面只面向 <see cref="ToolDescriptor"/> 与 JSON 入/出参，不感知语言细节。
/// </summary>
public interface ICallableTrack
{
    /// <summary>本轨后端（<see cref="ToolBackend"/> 之一）。</summary>
    string Backend { get; }

    /// <summary>本轨可调用的工具集。</summary>
    IReadOnlyCollection<ToolDescriptor> Tools { get; }

    /// <summary>本轨能否按名调用该工具。</summary>
    bool CanCall(string name);

    /// <summary>按名调用：入参为 JSON 文本（可为空），返回 JSON 文本结果或错误。</summary>
    Task<ToolExecutionResult> CallAsync(string name, string? argumentsJson, CancellationToken ct = default);
}
