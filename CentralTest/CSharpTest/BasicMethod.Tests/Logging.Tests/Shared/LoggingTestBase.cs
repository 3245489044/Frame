
using CentralConsole.Testing;
using CentralConsole.Testing.BaseClasses;
using Common.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Logging.Tests.Shared
{
    public abstract class LoggingTestBase<TFixture> : SkeletonTestBase<TFixture>
    {
        protected override void CleanupTest() { }

        [TestInitialize]
        public void TestInitialize()
        {
            LogController.CategoryResolver = CategoryResolver.Instance;
        }

        [TestCleanup]
        public void TestCleanup()
        {
            CleanupTest();
        }
    }
}
