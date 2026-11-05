
using System.Runtime.CompilerServices;

namespace ParameterResolver.Utilities;

public static class ReentryGuard
{
    private static readonly ConditionalWeakTable<object, object> _reentering = new();

    public static bool IsReentering(object target)
    {
        return _reentering.TryGetValue(target, out _);
    }

    public static IDisposable Enter(object target)
    {
        _reentering.Add(target, target);
        return new ReentryScope(target);
    }

    private sealed class ReentryScope : IDisposable
    {
        private readonly object _target;
        private bool _disposed;

        public ReentryScope(object target)
        {
            _target = target;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _reentering.Remove(_target);
                _disposed = true;
            }
        }
    }
}
