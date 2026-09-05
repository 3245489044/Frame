namespace Share.StateManagement
{
    /// <summary>
    /// 状态订阅派发器 —— 把「订阅 handler 在写者线程内联执行」与写线程解耦（总线-1 / 同步广播不阻塞写线程的注入点）。
    /// 架构背景：StateCenter.Write 每写一次同步触发两级广播（StorageCell.DataChanged + StateSubscriptionHub.Publish → Dispatch），
    /// 订阅 handler 全在写者线程上执行；若任一 handler 阻塞/封送/反向等待，写线程被焊死在 Write 内，
    /// 叠加引擎后台/UI/Outbox 多线程同进程（D3）即成「写线程被事件链卡住而污染全局」的架构缺陷。
    /// <para>
    /// StateSubscriptionHub 未配置本派发器（null）→ 内联同步执行，保持现状零回归；
    /// 上层组合根可按其线程模型注入实现（后台串行保序 / 按订阅者 SynchronizationContext 回投），
    /// 使写线程只提交发布、不执行订阅回调，从而不被订阅者阻塞或封送。
    /// </para>
    /// <para>
    /// 派发器只决定「在何时/哪个线程交付订阅回调」，不负责吞异常——handler 的异常隔离仍由 StateSubscriptionHub 内联包装保证
    /// （单 handler 失败不影响其余）。实现应保证同一发布内多次 delivery 的相对顺序，并尽快返回（不得阻塞写者线程）。
    /// </para>
    /// </summary>
    public interface IPublishDispatcher
    {
        /// <summary>交付一次订阅回调。实现必须尽快返回；可异步排布、串行保序或按订阅上下文回投。</summary>
        void Dispatch(Action delivery);
    }
}
