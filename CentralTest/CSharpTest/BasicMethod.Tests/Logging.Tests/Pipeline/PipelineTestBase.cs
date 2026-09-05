
using Common.Diagnostics;
using Logging.Tests.Shared;
using SkeletonFlow;

namespace Logging.Tests.Pipeline
{
    public abstract class PipelineTestBase : LoggingTestBase<PipelineFixtureContext>
    {
        protected override void CleanupTest()
        {
            LoggingTestCleanup.CleanupBatchRouterFull();
            LoggingTestCleanup.CleanupEnvironmentVariables();
        }
    }
}
