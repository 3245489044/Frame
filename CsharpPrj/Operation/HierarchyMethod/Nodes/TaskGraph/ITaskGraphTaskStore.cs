using System.Threading;
using System.Threading.Tasks;
using Share.TaskStatusPool.TaskPool;

namespace Operation.HierarchyMethod.Nodes.TaskGraph;

/// <summary>
/// 任务图任务存储端口 —— 「创建 task + 维护引用关系」复合服务（Agent.Reference.TaskGraphService）的
/// 任务入队/状态/删除抽象。由 Agent.Core 的 <c>AgentTaskPoolAdapter</c> 适配实现（映射 <see cref="TaskGraphTaskSpec"/>
/// → <c>AgentTaskItem</c>）。可空装配：未装配时服务返回诊断（-1 / false）而非抛错。
/// </summary>
public interface ITaskGraphTaskStore
{
    /// <summary>入队任务并激活消费（写 ur_tasks 即激活 AI 循环）。返回 -1 表示未装配/未入队。</summary>
    Task<long> EnqueueAsync(TaskGraphTaskSpec spec, CancellationToken ct = default);

    /// <summary>查询任务状态（未装配/不存在 → NotStarted）。</summary>
    Task<TaskExecutionStatus> GetStatusAsync(string taskKey, CancellationToken ct = default);

    /// <summary>删除任务（持久化清理）。返回是否命中删除。</summary>
    Task<bool> DeleteAsync(string taskKey, CancellationToken ct = default);
}
