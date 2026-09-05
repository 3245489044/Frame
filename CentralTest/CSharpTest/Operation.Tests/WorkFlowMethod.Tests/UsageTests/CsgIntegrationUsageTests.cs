using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.CSG;
using WorkFlowMethod.Infrastructure;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFramework.Models;
using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("CSG")]
    public class CsgIntegrationUsageTests
    {
        [TestMethod]
        public void CsgIntegrationService_CreateAndDispose_ShouldNotThrow()
        {
            using var service = new CsgIntegrationService();
            Assert.IsNotNull(service);
            Assert.IsNotNull(service.Facade);
        }

        [TestMethod]
        public void CsgIntegrationService_DoubleDispose_ShouldNotThrow()
        {
            var service = new CsgIntegrationService();
            service.Dispose();
            service.Dispose();
        }

        [TestMethod]
        public void CsgIntegrationService_NullArgs_ShouldThrow()
        {
            using var service = new CsgIntegrationService();
            Assert.ThrowsException<ArgumentNullException>(() => service.ChainToCanonical(null));
            Assert.ThrowsException<ArgumentNullException>(() => service.ValidateWorkflow(null));
            Assert.ThrowsException<ArgumentNullException>(() => service.ExportToJson(null));
        }

        [TestMethod]
        public void CsgIntegrationService_ChainToCanonical_ShouldWork()
        {
            using var service = new CsgIntegrationService();
            var chain = new Chain<string, NodeConfig>("start", new System.Collections.Generic.List<NodeConfig>
            {
                new NodeConfig { Symbol = "N1", basicConfig = new NodeConfig.BasicConfig { Callkey = "c1" }, NextNodeId = "c2" },
                new NodeConfig { Symbol = "N2", basicConfig = new NodeConfig.BasicConfig { Callkey = "c2" } }
            });

            var form = service.ChainToCanonical(chain);
            Assert.IsNotNull(form);
            Assert.IsTrue(form.Nodes.Count > 0);
        }

        [TestMethod]
        public void CsgIntegrationService_ResolveUri_ShouldWork()
        {
            using var service = new CsgIntegrationService();
            var context = new WfPipelineContext("ctx_001");

            Assert.ThrowsException<ArgumentNullException>(() => service.ResolveUri(null, context));
            Assert.ThrowsException<ArgumentNullException>(() => service.ResolveUri("csg://node/test", null));

            Assert.IsNull(service.ResolveUri("http://example.com", context));
            Assert.IsNull(service.ResolveUri("csg://", context));
            Assert.IsNull(service.ResolveUri("csg://unknown/something", context));

            Assert.AreSame(context, service.ResolveUri("csg://context/ctx_001", context));
            Assert.IsNull(service.ResolveUri("csg://context/wrong_id", context));
        }

        [TestMethod]
        public void ValidationResult_CSG_ShouldInheritAndExtend()
        {
            var result = new WorkFlowMethod.CSG.ValidationResult();
            Assert.IsTrue(result.IsValid);
            Assert.IsNotNull(result.Warnings);
            Assert.IsNull(result.ConfigForm);

            result.AddError("CSG错误");
            Assert.IsFalse(result.IsValid);

            result.Warnings.Add("警告");
            Assert.AreEqual(1, result.Warnings.Count);
        }
    }
}
