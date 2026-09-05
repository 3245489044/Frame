
using CentralConsole.Testing.BaseClasses;
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logging.Tests.Shared
{
    public abstract class LoggingIntegrationTestBase<TFixture> : IntegrationTestBase<TFixture>
    {
        protected override void EnsureInitialized()
        {
            LoggingInitSkeleton.EnsureInitialized();
        }

        protected override void CleanupBeforeTest()
        {
            LogRepository.Reset();
        }
    }
}
