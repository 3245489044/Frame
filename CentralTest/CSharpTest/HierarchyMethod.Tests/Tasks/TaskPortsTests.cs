using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Abstractions;
using Operation.HierarchyMethod.Tasks;
using Operation.HierarchyMethod.Tasks.Loops;
using Operation.HierarchyMethod.Tasks.Ports;

namespace HierarchyMethod.Tests.Tasks
{
    /// <summary>注入端口 + 可空守卫验证：未装配端口走确定性路径，不消耗 AI 额度（P3/P5）。</summary>
    [TestClass]
    public class TaskPortsTests
    {
        [TestMethod]
        public void UninjectedResolver_ReturnsNull_DeterministicPath()
        {
            ITaskExecutorResolver? resolver = null; // 未注入
            var record = new TaskRecord();
            Assert.IsNull(resolver?.Resolve(record)); // 可空守卫 → 确定性执行
        }

        [TestMethod]
        public void UninjectedDirectorProvider_ReturnsNull_DeterministicDirector()
        {
            IStateDirectorProvider? provider = null; // 未注入
            var state = AgentLoopState.FromTaskRecord(new TaskRecord());
            Assert.IsNull(provider?.Resolve(state)); // 缺省确定性导演（零 LLM）
        }

        [TestMethod]
        public void UnavailableDiagnostics_Constants_AreGuardPlaceholders()
        {
            Assert.AreEqual(-1, UnavailableDiagnostics.UnavailableLevel);
            Assert.IsFalse(UnavailableDiagnostics.UnavailableFlag);
            Assert.AreEqual(0, UnavailableDiagnostics.EmptyKeys.Count);
        }
    }
}
