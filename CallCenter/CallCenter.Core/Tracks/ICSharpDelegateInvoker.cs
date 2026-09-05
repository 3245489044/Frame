using CallCenter.Core.Tools;

namespace CallCenter.Core.Tracks;

/// <summary>
/// C#Track Runtime 轨的委托执行缝（P2 等价深化 / SingleImplementation 语义）：把「一次 delegate 的真实调用与结果/异常映射」
/// 从 <see cref="CSharpTrack"/> 中解耦。宿主（CallCenter.Host / Agent / FileDriven）可注入统一执行后端（如
/// ExecutionFramework.FunctionExecutor，统一超时/取消/异步解包/异常→ExecutionResult），避免 CSharpTrack 与某一执行器实现
/// 硬绑成平行分叉（对齐 TH-12/SingleImplementation）。
/// Core 缺省用 <see cref="DirectDelegateInvoker"/>（DynamicInvoke + JSON 序列化），保持零回归；Core 本身不引用执行框架，
/// 需对接既有执行栈的宿主注入自定义实现。控制面/上层仍只经 JSON 与 <see cref="ToolExecutionResult"/> 交互，不感知本接口。
/// </summary>
public interface ICSharpDelegateInvoker
{
    /// <summary>后端名（观测/审计）。</summary>
    string Name { get; }

    /// <summary>
    /// 执行一次已绑定好位置参数的委托调用，返回统一结果。实现负责真实调用语义
    /// （同步/异步解包、超时、取消、异常映射），并把返回值或错误映射为 <see cref="ToolExecutionResult"/>。
    /// </summary>
    Task<ToolExecutionResult> InvokeAsync(Delegate function, object?[] arguments, CancellationToken ct = default);
}
