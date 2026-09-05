using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;
using WorkFlowMethod.LoopFramework.Axioms;
using Share.StateManagement;
using Share.TaskStatusPool.Loops;

namespace Agent.Core.Tests
{
    /// <summary>
    /// 非限定快照驱动循环引擎测试：无内部 max-iterations/halt，仅响应外部终止（取消令牌 / externalTerminate 谓词）。
    /// </summary>
    [TestClass]
    [TestCategory(TestCategories.Concurrency)]
    public class SnapshotLoopEngineTests
    {
        [TestMethod]
        [Timeout(15000)]
        public async Task SnapshotLoop_NonRestrictive_StopsByExternalTerminate()
        {
            var engine = new SnapshotDrivenLoopEngine<int, int>();
            int rounds = 0;
            var result = await engine.RunAsync(
                initialState: 0,
                execute: (s, ct) => { rounds++; return Task.FromResult(s + 1); },
                next: (s, r, ct) => Task.FromResult(s + 1),
                ct: CancellationToken.None,
                externalTerminate: (s, ct) => Task.FromResult(rounds >= 3));
            Assert.AreEqual(3, result.Rounds.Count);      // 非限定：无内部 max-iterations，仅外部终止
            Assert.IsTrue(result.TerminatedByExternal);
            Assert.AreEqual(3, result.FinalState);
        }

        [TestMethod]
        [Timeout(15000)]
        public async Task SnapshotLoop_StopsByCancellation()
        {
            var engine = new SnapshotDrivenLoopEngine<int, int>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var result = await engine.RunAsync(
                0, (s, ct) => Task.FromResult(0), (s, r, ct) => Task.FromResult(s), cts.Token);
            Assert.AreEqual(0, result.Rounds.Count);
            Assert.IsTrue(result.TerminatedByExternal);
        }
    }
}
