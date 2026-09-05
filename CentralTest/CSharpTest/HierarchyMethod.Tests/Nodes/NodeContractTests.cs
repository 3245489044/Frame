using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.HierarchyMethod.Nodes;

namespace HierarchyMethod.Tests.Nodes
{
    /// <summary>节点契约验证：节点定义 / 存储键方案 / 输出编解码（LLM-free 自包含）。</summary>
    [TestClass]
    public class NodeContractTests
    {
        [TestMethod]
        public void NodeDefinition_Defaults_WorkflowIdIsDefault()
        {
            var def = new NodeDefinition { NodeKey = "collect" };
            Assert.AreEqual("collect", def.NodeKey);
            Assert.AreEqual(NodeKeys.DefaultWorkflowId, def.WorkflowId);
            Assert.AreEqual(string.Empty, def.FuncName);
        }

        [TestMethod]
        public void NodeDefinition_CarriesPortsAndMappings()
        {
            var def = new NodeDefinition
            {
                NodeKey = "act",
                FuncName = "cline_execute",
                ParameterKeys = new[] { "L1", "L2" },
                ResultKeys = new[] { "answer" },
            };
            Assert.AreEqual(2, def.ParameterKeys.Count);
            Assert.AreEqual("answer", def.ResultKeys[0]);
        }

        [TestMethod]
        public void NodeKeys_DelegatedPrefixTo_SingleSource()
        {
            // 节点存储统一按 NodeOutputKeys 键方案（每节点一条记录、端口在 JSON 载荷内），
            // NodeKeys 仅保留 DefaultWorkflowId 常量，前缀由 NodeOutputKeys 单一权威提供。
            Assert.AreEqual("nodes.agent-round.node", NodeOutputKeys.NodeKey("", "node"));
            Assert.AreEqual("nodes.agent-round.", NodeOutputKeys.Prefix(null));
        }

        [TestMethod]
        public void NodeOutputKeys_NodeKey_UsesNormalizedWorkflow()
        {
            Assert.AreEqual("nodes.wf.node", NodeOutputKeys.NodeKey("wf", "node"));
            // 空 workflow 归一为默认
            Assert.AreEqual("nodes.agent-round.node", NodeOutputKeys.NodeKey(null, "node"));
        }

        [TestMethod]
        public void NodeOutputCodec_Roundtrips_StrongTypedValue()
        {
            var encoded = NodeOutputCodec.EncodeValue(42);
            Assert.AreEqual(typeof(int).AssemblyQualifiedName, encoded.Type);
            var decoded = NodeOutputCodec.DecodeValue(encoded);
            Assert.AreEqual(42, decoded);
        }
    }
}
