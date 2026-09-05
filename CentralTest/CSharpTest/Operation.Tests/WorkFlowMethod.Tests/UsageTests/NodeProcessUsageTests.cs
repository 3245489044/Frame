using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.Skeletons.NodeProcess;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using WorkFlowMethod.Skeletons.NodeProcess.Abstractions;
using WorkFlowMethod.Skeletons.NodeProcess.Execution;
using WorkFlowMethod.Skeletons.NodeProcess.Graph;
using WorkFlowMethod.Skeletons.NodeProcess.Lifecycle;
using WorkflowExecution.Pipeline;
using GraphFoundation.Abstractions;
using GraphFramework.Models;
using Modules.Pipeline;
using SkeletonFlow;
using WfPipelineContext = WorkflowExecution.Pipeline.PipelineContext;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("NodeProcess")]
    public class NodeProcessUsageTests
    {
        #region 6.1 NodeProcessPipelineBuilder

        [TestMethod]
        public void NodeProcessPipelineBuilder_Build_ShouldReturnPipeline()
        {
            var builder = new NodeProcessPipelineBuilder("TestPipeline");
            var graph = CreateSampleGraph();

            builder
                .AddGraphDefinition(graph)
                .AddNodeChain()
                .AddNodeExtractor()
                .AddGraphChainUpdate()
                .AddNodeLoop()
                .AddNodeMetrics();

            var pipeline = builder.Build();
            Assert.IsNotNull(pipeline);
        }

        [TestMethod]
        public void NodeProcessPipelineBuilder_AddCustomStage_ShouldIncludeStage()
        {
            var builder = new NodeProcessPipelineBuilder("CustomStagePipeline");
            builder.AddStage(new CustomStage());
            var pipeline = builder.Build();
            Assert.IsNotNull(pipeline);
        }

        [TestMethod]
        public void NodeProcessPipelineBuilder_AddCustomPipeline_ShouldIncludePipeline()
        {
            var builder = new NodeProcessPipelineBuilder("CustomPipelineTest");
            builder.AddPipeline(new CustomPipeline());
            var pipeline = builder.Build();
            Assert.IsNotNull(pipeline);
        }

        #endregion

        #region 6.2 NodeConfigFactory

        [TestMethod]
        public void NodeConfigFactory_CreateNode_ShouldReturnJson()
        {
            var config = new NodeConfig
            {
                Symbol = "TestNode",
                Label = "测试节点",
                getStrConfig = new NodeConfig.GetStrConfig
                {
                    GetConfigLabel = "\"获取输入\"",
                    GetConfigSymbol = "GetConfig",
                    GetConfigGetstring = "ChainedNodeConfig.BaseNode"
                },
                funcPipelineConfig = new NodeConfig.FuncPipelineConfig
                {
                    FuncName = "ProcessData",
                    ParameterKeys = "\"param1\"",
                    ResultKeys = "\"result1\""
                },
                addNodeConfig = new NodeConfig.AddNodeConfig
                {
                    AddConfigAddKey = "ChainedNodeConfig.BaseNode",
                    AddAICallCellInvoke = true
                },
                nextNodeConfig = new NodeConfig.NextNodeConfig
                {
                    HandleNextNodeRun = true,
                    NextNodeKey = "next_node"
                },
                basicConfig = new NodeConfig.BasicConfig
                {
                    Callkey = "call_001",
                    DirectString = "DirectINPUT",
                    Show = true
                }
            };

            string nodeJson = NodeConfigFactory.CreateNode(config);
            Assert.IsFalse(string.IsNullOrEmpty(nodeJson));
        }

        [TestMethod]
        public void NodeConfigFactory_CompleteConfig_ShouldNotThrow()
        {
            var config = new NodeConfig
            {
                Symbol = "Test",
                basicConfig = new NodeConfig.BasicConfig { Callkey = "call_001" }
            };

            NodeConfigFactory.CompleteConfig(config, NodeConfigFactory.ConfigType.Default);
        }

        [TestMethod]
        public void NodeConfigFactory_BaseConfigTemplates_ShouldBeAccessible()
        {
            var getStrTemplate = NodeConfigFactory.BaseConfig.GetStrTemplates.Get("Default");
            var funcTemplate = NodeConfigFactory.BaseConfig.FuncPipelineTemplates.Get("Default");
            var addNodeTemplate = NodeConfigFactory.BaseConfig.AddNodeTemplates.Get("Default");
            var nextNodeTemplate = NodeConfigFactory.BaseConfig.NextNodeTemplates.Get("Default");
            var basicTemplate = NodeConfigFactory.BaseConfig.BasicTemplates.Get("Default");

            Assert.IsNotNull(getStrTemplate);
            Assert.IsNotNull(funcTemplate);
            Assert.IsNotNull(addNodeTemplate);
            Assert.IsNotNull(nextNodeTemplate);
            Assert.IsNotNull(basicTemplate);
        }

        #endregion

        #region 6.3 NodeChainSkeleton

        [TestMethod]
        public void NodeChainSkeleton_Create_ShouldNotThrow()
        {
            var skeleton = new NodeChainSkeleton();
            Assert.IsNotNull(skeleton);
        }

        #endregion

        #region 6.4 存储管理器

        [TestMethod]
        public void ChainInfoWorkflowStorageManager_SaveAndGet_ShouldRoundtrip()
        {
            var storage = new ChainInfoWorkflowStorageManager();
            var chain = new Chain<string, NodeConfig>("start", new List<NodeConfig>
            {
                new NodeConfig { Symbol = "N1", basicConfig = new NodeConfig.BasicConfig { Callkey = "c1" } }
            });

            storage.SaveChainInfo("wf_test", chain);
            Assert.IsTrue(storage.ContainsChainInfo("wf_test"));

            var loaded = storage.GetChainInfo("wf_test");
            Assert.IsNotNull(loaded);

            var timestamp = storage.GetTimestamp("wf_test");
            Assert.IsNotNull(timestamp);

            var allIds = storage.GetAllWorkflowIds();
            Assert.IsTrue(allIds.Contains("wf_test"));

            Assert.IsTrue(storage.DeleteChainInfo("wf_test"));
            Assert.IsFalse(storage.ContainsChainInfo("wf_test"));
        }

        [TestMethod]
        public void ChainInfoWorkflowStorageManager_Clear_ShouldRemoveAll()
        {
            var storage = new ChainInfoWorkflowStorageManager();
            var chain = new Chain<string, NodeConfig>("start", new List<NodeConfig>());

            storage.SaveChainInfo("wf_clear", chain);
            storage.Clear();
            Assert.AreEqual(0, storage.Count);
        }

        [TestMethod]
        public void WebCallManager_RegisterAndGet_ShouldRoundtrip()
        {
            var mgr = new WebCallManager();
            mgr.RegisterWebCall("call_test", new WebCallConfig
            {
                Url = "https://api.example.com/invoke",
                Method = "POST",
                Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer token" },
                BodyTemplate = "{\"input\":\"{0}\"}",
                TimeoutMs = 30000,
                RetryCount = 3,
                ValidateCertificate = false
            });

            Assert.IsTrue(mgr.ContainsWebCall("call_test"));

            var config = mgr.GetWebCallConfig("call_test");
            Assert.IsNotNull(config);
            Assert.AreEqual("https://api.example.com/invoke", config.Url);

            var keys = mgr.GetAllCallKeys();
            Assert.IsTrue(keys.Contains("call_test"));

            Assert.IsTrue(mgr.RemoveWebCall("call_test"));
            Assert.IsFalse(mgr.ContainsWebCall("call_test"));
        }

        [TestMethod]
        public void WebCallManager_Clear_ShouldRemoveAll()
        {
            var mgr = new WebCallManager();
            mgr.RegisterWebCall("c1", new WebCallConfig { Url = "http://test.com", Method = "GET" });
            mgr.Clear();
            Assert.IsFalse(mgr.ContainsWebCall("c1"));
        }

        #endregion

        private static IGraph<string, NodeConfig, object> CreateSampleGraph()
        {
            return new GraphFoundation.Core.CoreGraph<string, NodeConfig, object>(
                new GraphFoundation.Storage.InMemoryGraphStorage<string, NodeConfig, object>());
        }
    }

    public class CustomStage : IPipelineStage<WfPipelineContext>
    {
        public string Name => "CustomStage";
        public string Description => "自定义测试阶段";

        public bool CanExecute(WfPipelineContext context) => true;

        public Task ExecuteAsync(WfPipelineContext context, CancellationToken cancellationToken = default)
        {
            context.SetData("CustomStage.Executed", true);
            return Task.CompletedTask;
        }
    }

    public class CustomPipeline : IPipeline<WfPipelineContext, WfPipelineContext>
    {
        public string Name => "CustomPipeline";
        public string Description => "自定义测试管线";

        public WfPipelineContext Execute(WfPipelineContext input, Modules.Pipeline.PipelineContext? context = null)
        {
            return input;
        }

        public Task<WfPipelineContext> ExecuteAsync(WfPipelineContext input, Modules.Pipeline.PipelineContext? context = null)
        {
            return Task.FromResult(input);
        }

        WfPipelineContext ISkeleton<WfPipelineContext, WfPipelineContext>.Execute(WfPipelineContext input, SkeletonContext context)
        {
            return input;
        }

        Task<WfPipelineContext> ISkeleton<WfPipelineContext, WfPipelineContext>.ExecuteAsync(WfPipelineContext input, SkeletonContext context)
        {
            return Task.FromResult(input);
        }
    }
}
