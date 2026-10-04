using BasicMethod.Initialization.Ports;
using FrameErrorClass = BasicMethod.Initialization.Errors.ErrorClass;

namespace Share.TaskStatusPool.Loops;

/// <summary>
/// 分类端口适配器（P0.5-b 端口倒置接线）：把 canonical <see cref="ErrorClassifier.ClassifyError(Exception)"/>
/// 的 Share <see cref="ErrorClass"/> 分类桥接到框架端口 <see cref="IErrorClassifier"/>（框架 ErrorClass）。
/// 分类逻辑唯一（就地委派既有分类表，不建第二分类逻辑，Strategy.SingleImplementation）；
/// 本类只做类型身份桥接，采显式逐值映射（非 (int) 强转）——成员增删即编译期暴露，防静默漂移。
/// </summary>
public sealed class ErrorClassifierPortAdapter : IErrorClassifier
{
    /// <summary>单例（无状态，纯映射）。</summary>
    public static ErrorClassifierPortAdapter Instance { get; } = new();

    private ErrorClassifierPortAdapter() { }

    /// <inheritdoc />
    public FrameErrorClass Classify(Exception ex) => Map(ErrorClassifier.ClassifyError(ex));

    /// <summary>Share ErrorClass → 框架 ErrorClass 逐值映射（同名，显式以防成员漂移）。</summary>
    public static FrameErrorClass Map(ErrorClass cls) => cls switch
    {
        ErrorClass.ParseError => FrameErrorClass.ParseError,
        ErrorClass.CompilationError => FrameErrorClass.CompilationError,
        ErrorClass.ExecutionFailure => FrameErrorClass.ExecutionFailure,
        ErrorClass.Timeout => FrameErrorClass.Timeout,
        ErrorClass.PartialOutput => FrameErrorClass.PartialOutput,
        ErrorClass.EmptyResult => FrameErrorClass.EmptyResult,
        ErrorClass.ResourceExhausted => FrameErrorClass.ResourceExhausted,
        ErrorClass.Irrecoverable => FrameErrorClass.Irrecoverable,
        _ => FrameErrorClass.ExecutionFailure,
    };
}
