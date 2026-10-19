#nullable enable

using System;
using System.Threading.Tasks;

namespace TConfig.Abstractions;

public interface IUIDispatcher
{
    bool IsUIThread { get; }

    void Post(Action action);

    /// <summary>
    /// IEF+ C3：尝试投递——宿主已关闭 / 控件已释放 ⇒ 丢弃并返回 false（不抛、可计数可见化）。
    /// 默认实现退化为 <see cref="Post"/>（既有实现零改动）；宿主实现（WinFormsUIDispatcher）应覆写为
    /// <c>HostAlive ∧ IsHandleCreated ∧ !IsDisposed</c> 栅栏。
    /// </summary>
    bool TryPost(Action action)
    {
        Post(action);
        return true;
    }

    void Send(Action action);

    Task InvokeAsync(Func<Task> asyncFunc);

    Task<T> InvokeAsync<T>(Func<Task<T>> asyncFunc);

    void AssertUIThread();
}