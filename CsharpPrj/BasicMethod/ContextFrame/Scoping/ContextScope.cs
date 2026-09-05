
using System;

namespace BasicMethod.ContextFrame.Scoping
{
    public class ContextScope : IDisposable
    {
        private readonly Action? _onDispose;
        private bool _disposed;

        public ContextScope(Action? onCreate = null, Action? onDispose = null)
        {
            _onDispose = onDispose;
            onCreate?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _onDispose?.Invoke();
        }

        public static void Execute(Action action, Action? onCreate = null, Action? onDispose = null)
        {
            using (new ContextScope(onCreate, onDispose))
            {
                action();
            }
        }

        public static T Execute<T>(Func<T> func, Action? onCreate = null, Action? onDispose = null)
        {
            using (new ContextScope(onCreate, onDispose))
            {
                return func();
            }
        }
    }
}
