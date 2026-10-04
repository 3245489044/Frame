using BasicMethod.Initialization.Errors;
using BasicMethod.Initialization.Ports;

namespace BasicMethod.Initialization;

/// <summary>
/// 无操作守卫实现（N 出口）：分类恒为执行失败、账本为空操作。
/// 供未接线场景与测试作为确定性替身，避免在调用点匿名 <c>?? new</c>。
/// </summary>
public sealed class NullGuardSink : IErrorClassifier, IFailureBudget
{
    /// <summary>单例。</summary>
    public static NullGuardSink Instance { get; } = new();

    private NullGuardSink() { }

    /// <inheritdoc />
    public ErrorClass Classify(Exception ex) => ErrorClass.ExecutionFailure;

    /// <inheritdoc />
    public void Apply(ErrorTolerance tolerance) { }
}
