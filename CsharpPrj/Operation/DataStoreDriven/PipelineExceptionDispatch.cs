using System.Runtime.ExceptionServices;
using Modules.Pipeline.StaticPipeline;

namespace Operation.DataStoreDriven;

/// <summary>
/// 管线初始化共享辅助（R3 收敛）：消除 <see cref="Core.FacadeDataStore"/> / <see cref="PersistenceStoreFacade"/> 重复的
/// <c>StepExecutionException</c> → <c>ExceptionDispatchInfo</c> 解包样板。语义等价于
/// <c>catch (StepExecutionException ex) when (ex.InnerException is not null)</c> 的「过滤 + 保栈回抛」组合：
/// 有内部异常 → 回抛原始异常；无内部异常 → 回抛 StepExecutionException 自身（两者均保原始栈，不吞异常）。
/// </summary>
internal static class PipelineExceptionDispatch
{
    /// <summary>统一解包：经 <see cref="ExceptionDispatchInfo"/> 保栈回抛（内层异常优先，否则回抛外层 StepExecutionException）。</summary>
    public static void RethrowOriginal(StepExecutionException ex)
        => ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
}
