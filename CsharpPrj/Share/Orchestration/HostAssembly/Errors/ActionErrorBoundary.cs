using System;
using System.Threading;
using System.Threading.Tasks;

namespace Share.HostAssembly.Errors;

/// <summary>
/// 统一错误边界 —— 把「try/catch + 分类 + 统一诊断文案」收敛为一个切面包围器，
/// 消除各调用点散落重复的 try/catch。取消经 <see cref="OperationCanceledException"/> 显式透传（非失败包装）。
/// </summary>
public sealed class ActionErrorBoundary
{
    private readonly IErrorClassifier _classifier;

    public ActionErrorBoundary(IErrorClassifier? classifier = null)
        => _classifier = classifier ?? new DefaultErrorClassifier();

    /// <summary>包装类型化异步动作：成功返回结果；异常分类后包装为 <see cref="BoundaryResult{T}"/>（取消透传）。</summary>
    public async Task<BoundaryResult<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        PortGuard.Required(action, nameof(action));
        try
        {
            return BoundaryResult<T>.Success(await action().ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var category = _classifier.Classify(ex);
            return BoundaryResult<T>.Failure(category, ex.Message, ex);
        }
    }

    /// <summary>包装返回诊断文案的动作（调用方既产出文案又可能抛异常的场景），失败返回统一「[{类别}] 失败：{原因}」文案。</summary>
    public async Task<string> RunMessageAsync(Func<Task<string>> action, string actionName, CancellationToken ct = default)
    {
        PortGuard.Required(action, nameof(action));
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var category = _classifier.Classify(ex);
            return $"{actionName} 失败[{category}]：{ex.Message}";
        }
    }
}
