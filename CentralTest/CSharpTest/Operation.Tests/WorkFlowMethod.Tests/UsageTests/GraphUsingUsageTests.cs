using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.GraphFramework.Services;
using WorkFlowMethod.GraphFramework.Pipeline;
using WorkFlowMethod.GraphFramework.Deduplication;
using WorkFlowMethod.GraphFramework.Directives;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFoundation.Abstractions;
using GraphFramework.Directives;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("GraphUsing")]
    public class GraphUsingUsageTests
    {
        #region 12.1 GraphUsing 服务

        [TestMethod]
        public void GraphValidator_Create_ShouldNotThrow()
        {
            var service = new GraphValidator();
            Assert.IsNotNull(service);
        }

        [TestMethod]
        public void NodeChainService_Create_ShouldNotThrow()
        {
            var service = new NodeChainService();
            Assert.IsNotNull(service);
        }

        [TestMethod]
        public void ConfigDeduplicationProcessor_Create_ShouldNotThrow()
        {
            Assert.IsNotNull(typeof(ConfigDeduplicationProcessor));
        }

        [TestMethod]
        public void DefaultDirectiveApplier_Create_ShouldNotThrow()
        {
            var applier = new DefaultDirectiveApplier<string, NodeConfig, object>();
            Assert.IsNotNull(applier);
        }

        #endregion
    }
}
