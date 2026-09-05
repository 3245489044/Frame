using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Nodes.Workflows;
using Operation.HierarchyMethod.Tasks;

namespace HierarchyMethod.Tests.Tasks
{
    /// <summary>任务作用域验证（Tasks 层阶段四）：TaskScope 记录承载种子数据空间 + 轮状态头；ITaskScopeProvider 端口可被消费方解析。</summary>
    [TestClass]
    public class TaskScopeTests
    {
        [TestMethod]
        public void TaskScope_CarriesAllFields()
        {
            var seeds = new Dictionary<string, object?> { ["user_input"] = "hello", ["goal"] = "g" };
            var scope = new TaskScope("t-1", "s-1", 3, seeds, "h-7");

            Assert.AreEqual("t-1", scope.TaskKey);
            Assert.AreEqual("s-1", scope.SessionId);
            Assert.AreEqual(3, scope.Round);
            Assert.AreEqual(2, scope.Seeds.Count);
            Assert.AreEqual("h-7", scope.HistoryId);
        }

        [TestMethod]
        public void TaskScope_HistoryId_DefaultsNull()
        {
            var scope = new TaskScope("t-1", "s-1", 1, new Dictionary<string, object?>());
            Assert.IsNull(scope.HistoryId);
        }

        [TestMethod]
        public async Task TaskScopeProvider_ResolvesScope_WithSeeds()
        {
            // ITaskScopeProvider 端口：给定工作流模板 + taskKey/session，产出 TaskScope（种子字典为通用形态）。
            var provider = new SimpleTaskScopeProvider();
            var spec = new RoundSpec("wf", Array.Empty<RoundStep>());

            var scope = await provider.ResolveTaskScopeAsync(spec, "t-9", "s-9", CancellationToken.None);

            Assert.AreEqual("t-9", scope.TaskKey);
            Assert.AreEqual("s-9", scope.SessionId);
            Assert.AreEqual("seeded", scope.Seeds["seed_key"]);
        }

        /// <summary>ITaskScopeProvider 最小实现：演示「种子提取」的单一职责（Agent 侧以 RoundStateMapper/StateCenter 适配）。</summary>
        private sealed class SimpleTaskScopeProvider : ITaskScopeProvider
        {
            public Task<TaskScope> ResolveTaskScopeAsync(RoundSpec spec, string taskKey, string? sessionId, CancellationToken ct)
                => Task.FromResult(new TaskScope(taskKey, sessionId, 1, new Dictionary<string, object?> { ["seed_key"] = "seeded" }));
        }
    }
}
