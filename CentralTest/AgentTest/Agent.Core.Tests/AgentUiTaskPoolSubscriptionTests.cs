using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Scheduling;
using Agent.Core.TaskPool;
using Agent.Launcher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Share.StateManagement;
using Share.TaskStatusPool.TaskPool;
using TestInfrastructure;

namespace Agent.Core.Tests;

/// <summary>
/// AgentUi 任务池状态变更订阅（<see cref="AgentUiLibraryTaskPool"/>）定向测试 —— 覆盖 R-E2 订阅泄漏修复：
/// 旧实现用一次性静态 bool <c>_poolSubscribed</c> 永不重置，Detach→Attach 新池后新池不再被订阅（旧池委托残留泄漏）。
/// 修复后改为按池实例订阅：Attach 重订、Detach 退订。本测试验证「Detach→Attach 新池后新池仍能收到 TaskStateChanged」。
/// </summary>
[TestClass]
[TestCategory(TestCategories.Concurrency)]
public class AgentUiTaskPoolSubscriptionTests
{
    private const string RulesKey = "scheduler.rules.tp_sub";

    private static async Task<AgentTaskPoolAdapter> NewAdapterAsync(string rulesJson)
    {
        var store = new InMemoryConfigStore();
        if (rulesJson is not null) await store.SetAsync(RulesKey, rulesJson).ConfigureAwait(false);
        var adapter = new AgentTaskPoolAdapter(
            new InMemoryTaskPoolPersistence<AgentTaskItem>(),
            new ITaskExecutor[] { new RuleTaskExecutor(store) });
        adapter.Start();
        return adapter;
    }

    private static async Task EnqueueAndWaitTerminalAsync(AgentTaskPoolAdapter adapter, string taskKey)
    {
        var item = new AgentTaskItem
        {
            UserInput = "rule",
            SchedulingMode = SchedulingMode.Config,
            SchedulerRulesKey = RulesKey,
            TaskKey = taskKey,
        };
        await adapter.EnqueueAsync(item, CancellationToken.None).ConfigureAwait(false);
        await AsyncTestHelper.WaitUntilOrThrowAsync(async () =>
        {
            var s = await adapter.GetStatusAsync(taskKey, CancellationToken.None).ConfigureAwait(false);
            return s is not (TaskExecutionStatus.NotStarted or TaskExecutionStatus.Running);
        }, timeoutMs: 5000, pollIntervalMs: 25, description: $"任务 {taskKey} 未在超时内到达终态");
    }

    private static async Task WaitMirrorAsync(string taskKey, string expected)
        => await AsyncTestHelper.WaitUntilOrThrowAsync(() =>
        {
            var s = AgentUiLibraryTaskPool.Mirror.TryGet(taskKey);
            return string.Equals(s, expected, System.StringComparison.Ordinal);
        }, timeoutMs: 2000, pollIntervalMs: 10, description: $"镜像未出现 {taskKey}={expected}");

    [TestMethod]
    [Timeout(30000)]
    public async Task DetachThenAttach_NewPool_ReceivesTaskStateChanged()
    {
        AgentTaskPoolAdapter? poolA = null, poolB = null;
        try
        {
            // 池 A：Attach → 订阅 A；状态变更应进镜像。
            poolA = await NewAdapterAsync("{\"rules\":[{\"action\":\"continue\"}]}");
            AgentUiHost.AttachTaskPool(poolA);
            await EnqueueAndWaitTerminalAsync(poolA, "ta");
            await WaitMirrorAsync("ta", TaskExecutionStatus.Completed.ToString());
            Assert.AreEqual(TaskExecutionStatus.Completed.ToString(), AgentUiLibraryTaskPool.Mirror.TryGet("ta"),
                "池 A 的 Completed 状态应进入镜像");
            Assert.AreSame(poolA, AgentUiHost.TaskPool, "Attach 后宿主任务池引用应指向池 A");

            // Detach A → 退订（宿主槽位清空，旧池委托移除）。
            AgentUiHost.DetachTaskPool();
            Assert.IsNull(AgentUiHost.TaskPool, "Detach 后宿主任务池槽位应清空");

            // 池 B：Attach → 订阅 B；新池状态变更应进镜像（验证修复：旧一次性布尔已改为按实例订阅）。
            poolB = await NewAdapterAsync("{\"rules\":[{\"action\":\"continue\"}]}");
            AgentUiHost.AttachTaskPool(poolB);
            await EnqueueAndWaitTerminalAsync(poolB, "tb");
            await WaitMirrorAsync("tb", TaskExecutionStatus.Completed.ToString());
            Assert.AreEqual(TaskExecutionStatus.Completed.ToString(), AgentUiLibraryTaskPool.Mirror.TryGet("tb"),
                "换池后新池 B 的状态变更应进入镜像（旧一次性布尔已改为按实例订阅）");
            Assert.AreSame(poolB, AgentUiHost.TaskPool, "宿主任务池引用应指向新池 B");
        }
        finally
        {
            AgentUiHost.DetachTaskPool();
            if (poolA is not null) await poolA.DisposeAsync();
            if (poolB is not null) await poolB.DisposeAsync();
        }
    }
}
