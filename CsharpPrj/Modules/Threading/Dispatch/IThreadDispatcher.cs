using System;

namespace Modules.Threading
{
    /// <summary>
    /// 线程派发抽象 —— 把一次投递放到目标线程模型/执行上下文（StateBus 治理 P2/P4 的家）。
    /// 统一 Share.IPublishDispatcher（订阅回调交付）、IUIDispatcher（UI 封送）与 ThreadContext.Post
    /// 的『把 Action 放到某个上下文执行』形态；实现须**尽快返回**、不得反向阻塞调用（写）线程。
    /// 异常隔离不在本接口：投递内容的异常由投递者/消费者各自内联包装保证（如 handler 级 try/catch）。
    /// </summary>
    public interface IThreadDispatcher
    {
        /// <summary>投递一次工作。实现须尽快返回；可内联、后台串行保序或按线程模型回投。</summary>
        void Post(Action delivery);
    }
}
