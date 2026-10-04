using BasicMethod.Initialization.Errors;

namespace BasicMethod.Initialization.Ports;

/// <summary>
/// 错误分类端口（axioms §4 Gate.ClassifyError）。纯映射、无动作。
/// 抽象上提至本层，实现在下游（如 Share.TaskStatusPool.Loops.ErrorClassifier），经组合根注入——
/// 避免 BasicMethod 反向引用其它 root，同时不给分类表建第二实现。
/// </summary>
public interface IErrorClassifier
{
    /// <summary>纯分类：异常 → <see cref="ErrorClass"/>（无动作）。</summary>
    ErrorClass Classify(Exception ex);
}
