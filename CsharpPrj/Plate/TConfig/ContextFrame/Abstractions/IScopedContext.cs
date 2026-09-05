
using System;

namespace TConfig.ContextFrame.Abstractions
{
    public interface IScopedContext<TContext> where TContext : class
    {

        object? GetExtendedProperty(string key);

        void SetExtendedProperty(string key, object? value);


        event Action<TContext>? OnScopeCreated;

        event Action<TContext>? OnScopeDisposing;

        event Action<TContext>? OnScopeDisposed;
    }
}
