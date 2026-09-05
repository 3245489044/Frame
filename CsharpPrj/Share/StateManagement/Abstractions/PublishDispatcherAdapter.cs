using System;
using Modules.Threading;

namespace Share.StateManagement
{
    /// <summary>
    /// <c>IPublishDispatcher</c> 的 Modules.Threading 桥接适配器：把广播回调交付委托给 Modules.Threading 派发原语
    /// （SerialDispatcher=后台保序 / InlineDispatcher=内联零回归 / MainThreadDispatcher=UI 回投）。
    /// 使 <c>StateSubscriptionHub</c> 在不感知 Modules.Threading 的前提下接入其派发实现（StateBus 治理 P3 装配口）。
    /// 交付只决定在哪个线程模型执行订阅回调，不吞异常（handler 异常隔离仍由 hub 内联包装保证）。
    /// </summary>
    public sealed class PublishDispatcherAdapter : IPublishDispatcher
    {
        private readonly IThreadDispatcher _inner;

        public PublishDispatcherAdapter(IThreadDispatcher inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Dispatch(Action delivery)
        {
            ArgumentNullException.ThrowIfNull(delivery);
            _inner.Post(delivery);
        }
    }
}
