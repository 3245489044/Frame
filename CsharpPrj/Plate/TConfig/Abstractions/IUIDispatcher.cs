#nullable enable

using System;
using System.Threading.Tasks;

namespace TConfig.Abstractions;

public interface IUIDispatcher
{
    bool IsUIThread { get; }

    void Post(Action action);

    void Send(Action action);

    Task InvokeAsync(Func<Task> asyncFunc);

    Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc);

    void AssertUIThread();
}