using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkFlowMethod.Orchestration.Content;
using WorkFlowMethod.Skeletons.NodeProcess.Models;
using GraphFramework.Models;

namespace WorkFlowMethod.Tests.UsageTests
{
    [TestClass]
    [TestCategory("Orchestration")]
    public class OrchestrationUsageTests
    {
        [TestMethod]
        public void WorkflowMediator_Create_ShouldNotThrow()
        {
            var mediator = new WorkflowMediator();
            Assert.IsNotNull(mediator);
        }

        [TestMethod]
        public void WorkflowMediator_BuildFromConfig_ShouldReturnChain()
        {
            var mediator = new WorkflowMediator();
            var config = new ChainConfig
            {
                WorkflowId = "wf_001",
                Chains = new List<ConnectionPositionInfo<string>>
                {
                    new ConnectionPositionInfo<string>
                    {
                        ChainConfigValue = "TestSymbol",
                        ListCount = 1
                    }
                }
            };

            var chain = mediator.BuildFromConfig(config);
            Assert.IsNotNull(chain);
            Assert.IsNotNull(chain.AllNodes);
        }

        [TestMethod]
        public void WorkflowMediator_BuildFromConfig_NullConfig_ShouldReturnEmpty()
        {
            var mediator = new WorkflowMediator();
            var chain = mediator.BuildFromConfig(null);
            Assert.IsNotNull(chain);
            Assert.AreEqual(0, chain.AllNodes.Count);
        }

        [TestMethod]
        public void WorkflowMediator_ConnectWorkflows_ShouldMerge()
        {
            var mediator = new WorkflowMediator();
            var source = new Chain<string, NodeConfig>
            {
                AllNodes = new List<NodeConfig>
                {
                    new NodeConfig { Symbol = "N1", basicConfig = new NodeConfig.BasicConfig { Callkey = "c1" } }
                },
                StartNode = new NodeConfig { Symbol = "N1", basicConfig = new NodeConfig.BasicConfig { Callkey = "c1" } },
                EndNode = new NodeConfig { Symbol = "N1", basicConfig = new NodeConfig.BasicConfig { Callkey = "c1" } }
            };
            var target = new Chain<string, NodeConfig>
            {
                AllNodes = new List<NodeConfig>
                {
                    new NodeConfig { Symbol = "N2", basicConfig = new NodeConfig.BasicConfig { Callkey = "c2" } }
                },
                StartNode = new NodeConfig { Symbol = "N2", basicConfig = new NodeConfig.BasicConfig { Callkey = "c2" } },
                EndNode = new NodeConfig { Symbol = "N2", basicConfig = new NodeConfig.BasicConfig { Callkey = "c2" } }
            };

            var merged = mediator.ConnectWorkflows(source, target, null);
            Assert.IsNotNull(merged);
            Assert.AreEqual(2, merged.AllNodes.Count);
        }

        [TestMethod]
        public void WorkflowMediator_ConnectChains_ShouldReturnChain()
        {
            var mediator = new WorkflowMediator();
            var request = new ConnectRequest
            {
                SourceEndNodeCallkey = "c1",
                TargetStartNodeCallkey = "c2"
            };

            var chain = mediator.ConnectChains<NodeConfig>(request);
            Assert.IsNotNull(chain);
        }

        [TestMethod]
        public void DefaultBridgeContext_ShouldManageData()
        {
            using var context = new WorkFlowMethod.Orchestration.DefaultBridgeContext();
            Assert.AreEqual("OrchestrationBridgeContext", context.Id);

            context.SetData("test_value", "myKey");
            Assert.AreEqual("test_value", context.GetData<string>("myKey"));
            Assert.IsNull(context.GetData<string>("nonexistent"));

            Assert.IsTrue(context.TryGetData<string>(out var val, "myKey"));
            Assert.AreEqual("test_value", val);
            Assert.IsFalse(context.TryGetData<string>(out _, "missing"));

            Assert.IsTrue(context.ContainsData<string>("myKey"));
            Assert.IsTrue(context.RemoveData<string>("myKey"));
            Assert.IsFalse(context.RemoveData<string>("missing"));

            context.SetData("v1", "k1");
            context.SetData("v2", "k2");
            Assert.AreEqual(2, context.GetAllData().Count());
            Assert.AreEqual(2, context.GetAllData<string>().Count());

            var fired = false;
            context.DataChanged += (_, _) => fired = true;
            context.SetData("new", "changeKey");
            Assert.IsTrue(fired);
        }
    }
}
