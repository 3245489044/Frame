using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.Skeletons;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.WorkFlowPipeline;
using GraphFoundation.Abstractions;
using GraphFoundation.Core;
using GraphFoundation.Storage;
using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("WorkFlowPipeline")]
    public class WorkFlowPipelineUsageTests
    {
        [TestMethod]
        public void PipelineBuilder_BuildCustomPipeline_ShouldReturnSkeleton()
        {
            var graph = CreateSampleGraph();
            var pipeline = PipelineBuilder.BuildCustomPipeline(graph);
            Assert.IsNotNull(pipeline);
        }

        [TestMethod]
        public void PipelineBuilder_BuildBranchPipeline_ShouldReturnSkeleton()
        {
            var pipeline = PipelineBuilder.BuildBranchPipeline();
            Assert.IsNotNull(pipeline);
        }

        [TestMethod]
        public void PipelineBuilder_BuildParallelPipeline_ShouldReturnSkeleton()
        {
            var pipeline = PipelineBuilder.BuildParallelPipeline();
            Assert.IsNotNull(pipeline);
        }

        [TestMethod]
        public void PipelineBuilder_BuildCustomPipeline_NullGraph_ShouldThrow()
        {
            Assert.ThrowsException<ArgumentNullException>(() => PipelineBuilder.BuildCustomPipeline(null));
        }

        [TestMethod]
        public void PipelineContextAccessor_ShouldWrapContext()
        {
            var context = new WfPipelineContext("ctx_test");
            var accessor = new PipelineContextAccessor(context);

            Assert.AreEqual("ctx_test", accessor.Context.ContextId);

            Assert.ThrowsException<ArgumentNullException>(() => new PipelineContextAccessor(null));
        }

        [TestMethod]
        public void PipelineContext_Properties_ShouldRoundtrip()
        {
            var context = new WfPipelineContext("ctx_001");
            Assert.AreEqual("ctx_001", context.ContextId);

            context.CurrentNodeKey = "node_001";
            context.NextNodeKey = "node_002";
            context.CurrentInput = "input_data";
            context.CurrentOutput = "output_data";

            Assert.AreEqual("node_001", context.CurrentNodeKey);
            Assert.AreEqual("node_002", context.NextNodeKey);
            Assert.AreEqual("input_data", context.CurrentInput);
            Assert.AreEqual("output_data", context.CurrentOutput);
        }

        private static IGraph<string, NodeConfig, object> CreateSampleGraph()
        {
            return new CoreGraph<string, NodeConfig, object>(
                new InMemoryGraphStorage<string, NodeConfig, object>());
        }
    }
}
