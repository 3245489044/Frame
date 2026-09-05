using System;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.Center;

/// <summary>
/// 可取消执行边界载体（Center 门面）—— 贯穿「引擎 → 编排 → 步骤 → 存储」的同一条取消/预算语义。
/// 本类为 <c>Modules.Threading.BoundedExecution</c> 的**薄门面**：有界执行机制（CTS 根 + 层级 Derive + 预算窗口自证超时）
/// 唯一权威归 Modules.Threading；此处把框架 <c>BoundedTimeoutException</c> 映射回 Operation 强类型
/// <see cref="ExecutionBoundaryException"/> 族（Step/Round/Store，实现 IBudgetExhausted），使既有消费方按强类型归因不变。
/// 预算语义（对齐原实现）：外层取消优先、仅自身预算窗口耗尽抛强类型；未给预算（null）退化为直跑（仅响应取消），默认零误杀。
/// </summary>
public sealed class ExecutionScope : IDisposable
{
    private readonly Modules.Threading.BoundedExecution _inner;

    private ExecutionScope(Modules.Threading.BoundedExecution inner) => _inner = inner;

    /// <summary>创建根边界。budget = 根自身默认预算（整轮有界）；external 把既有多线程取消并入根；onStall 为超时诊断钩子（非阻塞）。</summary>
    public static ExecutionScope CreateRoot(
        string name,
        TimeSpan? budget = null,
        CancellationToken external = default,
        Action<ExecutionBoundaryException>? onStall = null)
        => new(Modules.Threading.BoundedExecution.CreateRoot(name, budget, external, MapStall(onStall)));

    /// <summary>本级名称（诊断归属）。</summary>
    public string Name => _inner.Name;

    /// <summary>本级取消令牌（级联父链；任何祖先取消均传导）。</summary>
    public CancellationToken Token => _inner.Token;

    /// <summary>是否已请求取消。</summary>
    public bool IsCancellationRequested => _inner.IsCancellationRequested;

    /// <summary>派生层级子边界：独立 linked-CTS（可单独取消、级联父取消），可覆盖默认预算。</summary>
    public ExecutionScope Derive(string name, TimeSpan? budget = null) => new(_inner.Derive(name, budget));

    /// <summary>请求取消本级及全部派生（调用方只应取消根；子级取消只影响其子树）。</summary>
    public void Cancel() => _inner.Cancel();

    /// <summary>步骤级有界执行：预算耗尽抛 <see cref="StepTimeoutException"/>。</summary>
    public Task<T> RunStepAsync<T>(string step, Func<CancellationToken, Task<T>> body, TimeSpan? timeout = null)
        => Translate(_inner.RunStepAsync(step, body, timeout), ex => new StepTimeoutException(ex.Subject, ex.Budget));

    /// <summary>步骤级有界执行（无返回值体）。</summary>
    public async Task RunStepAsync(string step, Func<CancellationToken, Task> body, TimeSpan? timeout = null)
        => await RunStepAsync<bool>(step, async t => { await body(t).ConfigureAwait(false); return true; }, timeout)
            .ConfigureAwait(false);

    /// <summary>整轮级有界执行：预算耗尽抛 <see cref="RoundTimeoutException"/>（收敛为终止决策）。</summary>
    public Task<T> RunRoundAsync<T>(string stage, Func<CancellationToken, Task<T>> body, TimeSpan? timeout = null)
        => Translate(_inner.RunRoundAsync(stage, body, timeout), ex => new RoundTimeoutException(ex.Subject, ex.Budget));

    /// <summary>整轮级有界执行（无返回值体）。</summary>
    public async Task RunRoundAsync(string stage, Func<CancellationToken, Task> body, TimeSpan? timeout = null)
        => await RunRoundAsync<bool>(stage, async t => { await body(t).ConfigureAwait(false); return true; }, timeout)
            .ConfigureAwait(false);

    /// <summary>存储级有界执行：预算耗尽抛 <see cref="StoreTimeoutException"/>（自证写读对象）。</summary>
    public Task<T> RunStoreAsync<T>(string target, Func<CancellationToken, Task<T>> body, TimeSpan? timeout = null)
        => Translate(_inner.RunStoreAsync(target, body, timeout), ex => new StoreTimeoutException(ex.Subject, ex.Budget));

    /// <summary>存储级有界执行（无返回值体）。</summary>
    public async Task RunStoreAsync(string target, Func<CancellationToken, Task> body, TimeSpan? timeout = null)
        => await RunStoreAsync<bool>(target, async t => { await body(t).ConfigureAwait(false); return true; }, timeout)
            .ConfigureAwait(false);

    public void Dispose() => _inner.Dispose();

    /// <summary>框架超时 → Operation 强类型异常映射（仅转 BoundedTimeoutException；取消原样上抛）。</summary>
    private static async Task<T> Translate<T>(Task<T> task, Func<Modules.Threading.BoundedTimeoutException, ExecutionBoundaryException> map)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Modules.Threading.BoundedTimeoutException ex)
        {
            throw map(ex);
        }
    }

    private static Action<Modules.Threading.BoundedTimeoutException>? MapStall(Action<ExecutionBoundaryException>? onStall)
        => onStall is null ? null : ex => onStall(ToBoundary(ex));

    private static ExecutionBoundaryException ToBoundary(Modules.Threading.BoundedTimeoutException ex)
        => ex.Kind switch
        {
            Modules.Threading.BoundedTimeoutKind.Step => new StepTimeoutException(ex.Subject, ex.Budget),
            Modules.Threading.BoundedTimeoutKind.Round => new RoundTimeoutException(ex.Subject, ex.Budget),
            _ => new StoreTimeoutException(ex.Subject, ex.Budget)
        };
}
