using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlow;
using WorkFlowMethod.Infrastructure;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("WorkflowBuilder")]
    public class WorkflowBuilderUsageTests
    {
        [TestMethod]
        public void BuildWorkflow_WithNodes_ShouldReturnJson()
        {
            var nodes = new List<object>();
            WorkflowBuilder.AddNode(nodes, "StartNode", "node_001", "InitFunc",
                new object[] { "param1" }, new object[] { "result1" }, "开始节点");
            var json = WorkflowBuilder.BuildWorkflow(nodes);
            Assert.IsTrue(json.Contains("StartNode"));
        }

        [TestMethod]
        public void AddNode_ShouldAddToNodes()
        {
            var nodes = new List<object>();
            WorkflowBuilder.AddNode(nodes, "TestNode", "n1", "TestFunc",
                new object[] { "p1" }, new object[] { "r1" }, "测试");
            Assert.AreEqual(1, nodes.Count);
        }

        [TestMethod]
        public void BuildSingleNode_ShouldReturnJson()
        {
            var json = WorkflowBuilder.BuildSingleNode("SingleNode", "n_single", "SingleFunc",
                new object[] { "input" }, new object[] { "output" }, "单节点");
            Assert.IsTrue(json.Contains("SingleNode"));
        }

        [TestMethod]
        public void CreateNode_ShouldReturnDynamicNode()
        {
            var node = WorkflowBuilder.CreateNode("DynamicNode", "n_dyn", "DynFunc",
                new object[] { "p1" }, new object[] { "r1" }, "动态节点");
            Assert.AreEqual("DynamicNode", node.Symbol);
        }

        [TestMethod]
        public void BuildFromStrings_ShouldBuildWorkflow()
        {
            var json = WorkflowBuilder.BuildFromStrings(
                "StartNode:n1:InitFunc:input:output:开始",
                "ProcessNode:n2:ProcessFunc:data:result:处理");
            Assert.IsTrue(json.Contains("StartNode") && json.Contains("ProcessNode"));
        }

        [TestMethod]
        public void ParseFromJson_Valid_ShouldReturnNodes()
        {
            var json = "[{\"Symbol\":\"Test\",\"NodeId\":\"n1\",\"FuncPipelineConfig\":{\"FuncName\":\"F\"}}]";
            Assert.AreEqual(1, WorkflowBuilder.ParseFromJson(json).Count);
        }

        [TestMethod]
        public void ParseFromJson_Invalid_ShouldThrow()
        {
            Assert.ThrowsException<System.Text.Json.JsonException>(() => WorkflowBuilder.ParseFromJson("not json"));
        }

        [TestMethod]
        public void ValidateWorkflow_ShouldWork()
        {
            var valid = new List<object> { new Dictionary<string, object> { ["Symbol"] = "S1", ["NodeId"] = "n1" } };
            Assert.IsTrue(WorkflowBuilder.ValidateWorkflow(valid).IsValid);

            var empty = new List<object>();
            Assert.IsFalse(WorkflowBuilder.ValidateWorkflow(empty).IsValid);
        }

        [TestMethod]
        public void GetSummary_ShouldReturnFormatted()
        {
            var nodes = new List<object> { new Dictionary<string, object> { ["Symbol"] = "Start", ["NodeId"] = "n1" } };
            var summary = WorkflowBuilder.GetSummary(nodes);
            Assert.IsFalse(string.IsNullOrEmpty(summary));
            Assert.AreEqual("Empty Workflow", WorkflowBuilder.GetSummary(new List<object>()));
        }

        [TestMethod]
        public void SaveAndLoadFile_ShouldRoundtrip()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var nodes = new List<object>();
                WorkflowBuilder.AddNode(nodes, "FileNode", "n_file", "FileFunc",
                    new object[] { "p1" }, new object[] { "r1" });
                WorkflowBuilder.SaveToFile(nodes, tempFile);
                Assert.IsTrue(File.Exists(tempFile));

                var loaded = WorkflowBuilder.LoadNodesFromFile(tempFile);
                Assert.AreEqual(1, loaded.Count);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [TestMethod]
        public void LoadFromFile_NonExistent_ShouldThrow()
        {
            Assert.ThrowsException<FileNotFoundException>(
                () => WorkflowBuilder.LoadFromFile("nonexistent_file.json"));
        }

        [TestMethod]
        public void ToShareWorkflow_ShouldConvert()
        {
            var nodes = new List<object> { new Dictionary<string, object> { ["Symbol"] = "S1", ["NodeId"] = "n1" } };
            var workflow = WorkFlowMethod.Execution.Adapters.WorkflowBuilderBridge.ToShareWorkflow(nodes, "wf_test");
            Assert.AreEqual("wf_test", workflow.Id);
            Assert.AreEqual(1, workflow.Nodes.Count);
        }
    }
}
