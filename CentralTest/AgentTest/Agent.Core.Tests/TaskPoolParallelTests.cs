using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Agent.Core.TaskPool;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.TaskPool;
using TestInfrastructure;
using WorkFlowMethod.LoopFramework.Axioms;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 并行设计验证：每任务独立状态基座（<see cref="AgentTaskItem"/> 自带独立 Data/核心对象），
    /// 在 <see cref="TaskPool{TItem,TResult}"/> MaxConcurrency&gt;1 并发下运行多个任务，断言无跨任务状态污染。
    /// 并发写持久化经 <see cref="InMemoryTaskPoolPersistence{TItem}"/>（线程安全字典）按 TaskKey 隔离。
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Concurrency)]
    public class TaskPoolParallelTests
    {
        private const int Count = 12;
        private const int MaxConcurrency = 4;

        [TestMethod]
        [Timeout(30000)]
        public async Task ParallelTasks_IsolatePerTaskStateBase()
        {
            var persistence = new InMemoryTaskPoolPersistence<AgentTaskItem>();
            await persistence.InitializeAsync();

            var pool = new TaskPool<AgentTaskItem, AxiomLoopResult>(
                persistence,
                (item, execId, ct) =>
                {
                    // 每任务只写自己的状态基座（Data / Round / 策略），不触碰任何共享/全局状态。
                    item.Data.Set("owner", item.TaskKey);
                    item.Round = item.Round + 1;
                    item.CurrentStrategyId = "S-" + item.TaskKey;
                    return Task.FromResult(new AxiomLoopResult { FinalDecision = LoopDecision.Success });
                },
                new TaskPoolOptions { MaxConcurrency = MaxConcurrency, MinTriggerIntervalMs = 0 });

            for (int i = 0; i < Count; i++)
            {
                // 经任务池入队（写入持久化 NotStarted + 唤醒消费循环，写即激活），而非直写持久化。
                await pool.EnqueueAsync(new AgentTaskItem { TaskKey = $"t{i}", UserInput = $"input{i}", Round = 1 });
            }

            pool.Start();
            await AsyncTestHelper.WaitUntilOrThrowAsync(async () =>
            {
                var done = await persistence.ListAsync(TaskExecutionStatus.Completed).ConfigureAwait(false);
                var failed = await persistence.ListAsync(TaskExecutionStatus.Faulted).ConfigureAwait(false);
                return done.Count + failed.Count >= Count;
            }, timeoutMs: 15000, pollIntervalMs: 20,
                description: "并行任务池未在超时内完成");
            await pool.StopAsync();

            // 每个任务快照保留自己的 owner / strategy / round，无跨任务污染。
            for (int i = 0; i < Count; i++)
            {
                var restored = await persistence.GetAsync($"t{i}");
                Assert.IsNotNull(restored);
                Assert.AreEqual($"t{i}", restored!.Data.Get<object?>("owner"));
                Assert.AreEqual($"S-t{i}", restored.CurrentStrategyId);
                Assert.AreEqual(2, restored.Round);
            }
        }
    }
}
