namespace DataCenter.Abstractions.Dispatch;

/// <summary>
/// 派发端口 —— 把写线程的订阅回调/事件交付移出调用栈（后台串行或按订阅上下文回投）。
/// null 语义 = 内联同步交付（默认）；非 null = 出队交付，写线程只提交不被订阅回调阻塞。
/// </summary>
public interface IStateDispatch
{
    void Dispatch(Action action);
}
