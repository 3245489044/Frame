
using ParameterResolver.Utilities;

namespace ParameterResolver.Strategies.Abstractions;

public abstract class WriteStrategyBase<TAdapter> : IWriteStrategy<TAdapter>
    where TAdapter : class
{
    protected abstract string StrategyName { get; }

    protected abstract string LogPrefix { get; }

    protected IThreadDispatcher? ThreadDispatcher { get; }

    protected WriteStrategyBase(IThreadDispatcher? threadDispatcher = null)
    {
        ThreadDispatcher = threadDispatcher;
    }

    protected abstract void ExecuteWrite(object target, object value, TAdapter? adapter);

    public void Write(object target, object? value, TAdapter? adapter)
    {
        if (target == null || value == null)
            return;

        if (ThreadDispatcher != null)
        {
            if (ThreadDispatcher.TryBeginInvoke(target, () => Write(target, value, adapter)))
                return;

            if (ThreadDispatcher.TryInvokeOnUIThread(target, () => Write(target, value, adapter)))
                return;
        }

        if (ReentryGuard.IsReentering(target))
            return;

        try
        {
            using (ReentryGuard.Enter(target))
            {
                ExecuteWrite(target, value, adapter);
            }
        }
        catch (Exception ex)
        {
            OnWriteFailed(ex, target, value, adapter);
            throw new InvalidOperationException($"[{StrategyName}] Write failed: {ex.Message}", ex);
        }
    }

    protected virtual void OnWriteFailed(Exception ex, object target, object value, TAdapter? adapter)
    {
    }
}
