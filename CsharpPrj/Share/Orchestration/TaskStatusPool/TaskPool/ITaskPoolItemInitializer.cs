namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务项入池初始化钩子（U3）：由任务池在 <c>EnqueueAsync</c> 持久化前调用一次。
/// <para>框架只定义时序（唯一入队初始化点），初始化内容（默认策略/执行器视图等）由宿主实现；
/// 不把任何产品领域词（策略/执行器）带进通用池。</para>
/// </summary>
public interface ITaskPoolItemInitializer<in TItem> where TItem : ITaskPoolItem
{
    void Initialize(TItem item);
}
