namespace Modules.Threading;

/// <summary>
/// 调用深度闸门（通用件）：实例级 AsyncLocal 深度计数 + 上限判定。
/// <para>
/// C1：从 Operation 层下沉；零业务语义。异常类型由调用方以工厂注入（框架不引用上层异常）。
/// 不同实例的计数互相隔离（治「static AsyncLocal 跨 guard 污染」）。
/// </para>
/// </summary>
public sealed class CallDepthGuard
{
    private readonly AsyncLocal<int> _depth = new();
    private readonly Func<string, int, Exception> _exceptionFactory;
    private readonly Action<string, int>? _onExceeded;

    public CallDepthGuard(
        int maxDepth,
        Func<string, int, Exception> exceptionFactory,
        Action<string, int>? onExceeded = null)
    {
        if (maxDepth <= 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        MaxDepth = maxDepth;
        _exceptionFactory = exceptionFactory ?? throw new ArgumentNullException(nameof(exceptionFactory));
        _onExceeded = onExceeded;
    }

    public int MaxDepth { get; }

    public int CurrentDepth => _depth.Value;

    public async Task<T> ProtectAsync<T>(string callPoint, Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Enter(callPoint);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    public T Protect<T>(string callPoint, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Enter(callPoint);
        try
        {
            return action();
        }
        finally
        {
            Exit();
        }
    }

    private void Enter(string callPoint)
    {
        _depth.Value++;
        if (_depth.Value <= MaxDepth) return;

        _depth.Value--;
        _onExceeded?.Invoke(callPoint, MaxDepth);
        throw _exceptionFactory(callPoint, MaxDepth);
    }

    private void Exit() => _depth.Value--;
}
