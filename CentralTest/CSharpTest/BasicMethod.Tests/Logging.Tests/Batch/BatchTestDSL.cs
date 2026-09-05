
using Common.Diagnostics;
using Logging.Tests.Shared;
using SkeletonFlow;
using SkeletonFlow.App;

namespace Logging.Tests.Batch
{
    public abstract class BatchTestBase : LoggingTestBase<BatchFixtureContext>
    {
        protected override void CleanupTest()
        {
            LoggingTestCleanup.CleanupBatchRouterFull();
        }


        protected static ISkeleton<Unit, bool> ClearThen(
            params ISkeleton<BatchFixtureContext, BatchFixtureContext>[] assertions) =>
            TestFlow(BatchFixtureSkeletons.ClearAll(), assertions);

        protected static ISkeleton<Unit, bool> RegisterThen(string batchId, string name,
            params ISkeleton<BatchFixtureContext, BatchFixtureContext>[] assertions) =>
            TestFlow(
                BatchFixtureSkeletons.ClearAll()
                    .Then(BatchFixtureSkeletons.RegisterBatch(batchId, name, "test")),
                assertions);

        protected static ISkeleton<Unit, bool> RegisterThenWithSource(string batchId, string name, string source,
            params ISkeleton<BatchFixtureContext, BatchFixtureContext>[] assertions) =>
            TestFlow(
                BatchFixtureSkeletons.ClearAll()
                    .Then(BatchFixtureSkeletons.RegisterBatch(batchId, name, source)),
                assertions);

        protected static ISkeleton<Unit, bool> FullFlowThen(string batchId, int logCount,
            params ISkeleton<BatchFixtureContext, BatchFixtureContext>[] assertions) =>
            TestFlow(BatchFixtureSkeletons.SetupFullFlow(batchId, logCount, "INFO"), assertions);

        protected static ISkeleton<Unit, bool> FullFlowThenWithLevel(string batchId, int logCount, string level,
            params ISkeleton<BatchFixtureContext, BatchFixtureContext>[] assertions) =>
            TestFlow(BatchFixtureSkeletons.SetupFullFlow(batchId, logCount, level), assertions);

        protected static ISkeleton<Unit, bool> MultiBatchThen(
            (string batchId, int logCount, string level) batchA,
            (string batchId, int logCount, string level) batchB,
            params ISkeleton<BatchFixtureContext, BatchFixtureContext>[] assertions) =>
            TestFlow(BatchFixtureSkeletons.SetupMultiBatchFlow(batchA, batchB), assertions);
    }
}
