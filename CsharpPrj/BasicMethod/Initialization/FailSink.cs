using BasicMethod.Initialization.Errors;
using BasicMethod.Initialization.Ports;

namespace BasicMethod.Initialization;

/// <summary>
/// 错误分类与账本接线的单一入口（对齐 axioms Gate.ClassifyError + AX.FailureBudgetEscalation）。
/// 分类委托上提端口 <see cref="IErrorClassifier"/>，记账委托上提端口 <see cref="IFailureBudget"/>——
/// 本层不引下游实现，实现经组合根注入。
/// </summary>
public sealed class FailSink
{
    private readonly IErrorClassifier _classifier;
    private readonly IFailureBudget? _budget;

    /// <param name="classifier">错误分类端口（必填）。</param>
    /// <param name="budget">失败账本端口（可选；未注入时只折叠不记账）。</param>
    public FailSink(IErrorClassifier classifier, IFailureBudget? budget = null)
    {
        _classifier = classifier ?? throw new MissingDependencyException(nameof(classifier));
        _budget = budget;
    }

    /// <summary>执行期：把可能失败的操作折叠为 Outcome（不吞错，落账本）。</summary>
    public ErrorOutcome Try(Action body, string op)
    {
        try
        {
            body();
            return ErrorOutcome.Success(op);
        }
        catch (Exception ex)
        {
            return Record(ErrorOutcome.From(ex, op, _classifier.Classify(ex)));
        }
    }

    /// <summary>记账：分类 → FailureBudget（tolerated 只记账 / fatal 升级 level）。</summary>
    public ErrorOutcome Record(ErrorOutcome outcome)
    {
        if (!outcome.Ok && outcome.Class is { } cls)
            _budget?.Apply(ClassifyToTolerance(cls));
        return outcome;
    }

    /// <summary>
    /// 错误类别 → 容忍度（axioms §5 FailureBudgetEscalation）：
    /// compilation_error / execution_failure（单步增量试错）→ tolerated；其余 → fatal。
    /// </summary>
    public static ErrorTolerance ClassifyToTolerance(ErrorClass cls)
        => cls is ErrorClass.CompilationError or ErrorClass.ExecutionFailure
            ? ErrorTolerance.Tolerated
            : ErrorTolerance.Fatal;
}
