#nullable enable

namespace Share.Projection;

/// <summary>投影步骤（纯变换：无 I/O、无缓存、无写；失败以 Result 表达）。</summary>
/// <typeparam name="TSource">输入。</typeparam>
/// <typeparam name="TResult">输出。</typeparam>
public interface IProjectionStep<TSource, TResult>
{
    /// <summary>执行纯变换。</summary>
    ProjectionResult<TResult> Project(TSource source, ProjectionContext context);
}
