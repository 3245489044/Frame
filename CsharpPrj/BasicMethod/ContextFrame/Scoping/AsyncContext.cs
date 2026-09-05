
using System;
using System.Collections.Concurrent;
using System.Threading;
using TConfig.ContextFrame.Abstractions;

namespace BasicMethod.ContextFrame.Scoping
{
    public static class AsyncContext<TContext>
        where TContext : class
    {

        private static readonly AsyncLocal<ScopeNode?> _currentScope = new();
        private static readonly ConcurrentDictionary<string, object?> _extendedProperties = new();


        public static event Action<TContext>? OnScopeCreated;

        public static event Action<TContext>? OnScopeDisposing;

        public static event Action<TContext>? OnScopeDisposed;


        public static TContext? Current => _currentScope.Value?.Context;

        public static bool IsInScope => _currentScope.Value != null;


        public static IDisposable CreateScope(TContext context)
        {
            var previous = _currentScope.Value;
            var node = new ScopeNode(context, previous);
            _currentScope.Value = node;

            OnScopeCreated?.Invoke(context);

            return new ScopeDisposable(context, previous);
        }

        public static void ExecuteInScope(TContext context, Action action)
        {
            using (CreateScope(context))
            {
                action();
            }
        }

        public static TResult ExecuteInScope<TResult>(TContext context, Func<TResult> action)
        {
            using (CreateScope(context))
            {
                return action();
            }
        }


        public static object? GetExtendedProperty(string key)
        {
            _extendedProperties.TryGetValue(key, out var value);
            return value;
        }

        public static void SetExtendedProperty(string key, object? value)
        {
            if (value != null)
                _extendedProperties[key] = value;
            else
                _extendedProperties.TryRemove(key, out _);
        }


        private class ScopeNode
        {
            public TContext Context { get; }
            public ScopeNode? Previous { get; }

            public ScopeNode(TContext context, ScopeNode? previous)
            {
                Context = context;
                Previous = previous;
            }
        }

        private class ScopeDisposable : IDisposable
        {
            private readonly TContext _context;
            private readonly ScopeNode? _previous;
            private bool _disposed;

            public ScopeDisposable(TContext context, ScopeNode? previous)
            {
                _context = context;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                OnScopeDisposing?.Invoke(_context);
                _currentScope.Value = _previous;
                OnScopeDisposed?.Invoke(_context);
            }
        }
    }
}
