
using ChainGraph.Core;
using Graphkey.Core;
using GraphFramework.Graph;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace GraphFramework.Tests
{
    [TestClass]
    public class GraphKeyTests : GraphFrameworkTestBase
    {
        [TestMethod]
        public void RunAll_PlaceholderCreation()
        {
            var result = new TestGroupBuilder("PlaceholderCreation")
                .Add("CreateInputPlaceholder_ShouldSucceed", () =>
                {
                    var placeholder = Placeholder.Input("param1");
                    return placeholder != null;
                })
                .Add("CreatePrevPlaceholder_ShouldSucceed", () =>
                {
                    var placeholder = Placeholder.Prev();
                    return placeholder != null;
                })
                .Add("CreateNodeReferencePlaceholder_ShouldSucceed", () =>
                {
                    var placeholder = Placeholder.From("node1");
                    return placeholder != null;
                })
                .Add("CreateNodeIndexPlaceholder_ShouldSucceed", () =>
                {
                    var placeholder = Placeholder.Node(0);
                    return placeholder != null;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PlaceholderResolver()
        {
            var result = new TestGroupBuilder("PlaceholderResolver")
                .Add("ResolveInput_ShouldReturnValue", () =>
                {
                    var placeholder = Placeholder.Input("param1");
                    var context = new SimpleResolutionContext(
                        previousOutputs: new List<string>(),
                        inputKeys: new List<string> { "param1" },
                        getNodeOutputsById: id => new List<string> { "output1" },
                        getNodeOutputsByIndex: idx => new List<string> { "output1" });
                    var resolved = PlaceholderResolver.Resolve(placeholder, context);
                    return resolved == "param1";
                })
                .Add("ResolveNodeRef_ShouldReturnValue", () =>
                {
                    var placeholder = Placeholder.From("node1");
                    var context = new SimpleResolutionContext(
                        previousOutputs: new List<string>(),
                        inputKeys: new List<string>(),
                        getNodeOutputsById: id => new List<string> { "output1" },
                        getNodeOutputsByIndex: idx => new List<string> { "output1" });
                    var resolved = PlaceholderResolver.Resolve(placeholder, context);
                    return resolved == "output1";
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DynamicWorkflowBuilder()
        {
            var result = new TestGroupBuilder("DynamicWorkflowBuilder")
                .Add("CreateWorkflowBuilder_ShouldSucceed", () =>
                {
                    var handler = new SimpleNodeHandler();
                    var builder = new DynamicWorkflowBuilder(handler);
                    return builder != null;
                })
                .Add("WorkflowBuilder_Build_ShouldReturnWorkflow", () =>
                {
                    var handler = new SimpleNodeHandler();
                    var builder = new DynamicWorkflowBuilder(handler);
                    var chain = builder.CreateChain("chain1");
                    chain.Then(new { Name = "Step1", ResultKeys = new[] { "result1" } });
                    chain.Then(new { Name = "Step2", ResultKeys = new[] { "result2" } });
                    chain.EndChain();
                    var workflow = builder.Build();
                    return workflow.Count == 2;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_DynamicChainBuilder()
        {
            var result = new TestGroupBuilder("DynamicChainBuilder")
                .Add("ChainBuilder_Then_ShouldAddNode", () =>
                {
                    var handler = new SimpleNodeHandler();
                    var builder = new DynamicWorkflowBuilder(handler);
                    var chain = builder.CreateChain("chain1");
                    chain.Then(new { Name = "Step1", ResultKeys = new[] { "result1" } });
                    chain.Then(new { Name = "Step2", ResultKeys = new[] { "result2" } });
                    chain.EndChain();
                    var workflow = builder.Build();
                    return workflow.Count == 2;
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_PlaceholderParser()
        {
            var result = new TestGroupBuilder("PlaceholderParser")
                .Add("ParseToParam_Input_ShouldReturnPlaceholder", () =>
                {
                    var param = PlaceholderParser.ParseToParam("@input.param1");
                    return param is Placeholder p && p.Kind == PlaceholderKind.Input;
                })
                .Add("ParseToParam_Prev_ShouldReturnPlaceholder", () =>
                {
                    var param = PlaceholderParser.ParseToParam("@prev");
                    return param is Placeholder p && p.Kind == PlaceholderKind.Prev;
                })
                .Add("ParseToParam_NodeRef_ShouldReturnPlaceholder", () =>
                {
                    var param = PlaceholderParser.ParseToParam("@node.node1");
                    return param is Placeholder p && p.Kind == PlaceholderKind.NodeReference;
                })
                .Add("IsInputReference_ShouldDetectInput", () =>
                {
                    return PlaceholderParser.IsInputReference("@input.param1");
                })
                .Add("IsNodeReference_ShouldDetectNodeRef", () =>
                {
                    return PlaceholderParser.IsNodeReference("@node.node1");
                })
                .Add("IsPrevReference_ShouldDetectPrev", () =>
                {
                    return PlaceholderParser.IsPrevReference("@prev");
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_ClonerFactory()
        {
            var result = new TestGroupBuilder("ClonerFactory")
                .Add("CreateJsonCloner_ShouldSucceed", () =>
                {
                    var cloner = ClonerFactory.CreateJsonCloner<object>();
                    return cloner != null;
                })
                .Add("JsonCloner_Clone_ShouldCreateCopy", () =>
                {
                    var cloner = ClonerFactory.CreateJsonCloner<Dictionary<string, object>>();
                    var original = new Dictionary<string, object> { { "key", "value" } };
                    var clone = cloner.Clone(original);
                    return clone != null && clone["key"].ToString() == "value";
                })
                .Run();
            result.AssertAllPassed();
        }

        [TestMethod]
        public void RunAll_SimpleCoreGraph()
        {
            var result = new TestGroupBuilder("SimpleCoreGraph")
                .Add("CreateSimpleCoreGraph_ShouldSucceed", () =>
                {
                    var graph = new SimpleCoreGraph<string, object, object>();
                    return graph != null;
                })
                .Add("SimpleCoreGraph_AddNode_ShouldWork", () =>
                {
                    var graph = new SimpleCoreGraph<string, object, object>();
                    graph.AddNode("n1", new { Name = "Test" }, "symbol1");
                    return graph.ContainsNode("n1");
                })
                .Add("SimpleCoreGraph_AddEdge_ShouldWork", () =>
                {
                    var graph = new SimpleCoreGraph<string, object, object>();
                    graph.AddNode("n1", new { Name = "Source" }, "src");
                    graph.AddNode("n2", new { Name = "Target" }, "tgt");
                    graph.AddEdge("e1", "n1", "n2", true, new { Weight = 1.0 }, 1.0);
                    return graph.ContainsEdge("e1");
                })
                .Add("SimpleCoreGraph_GetNeighbors_ShouldReturnNeighbors", () =>
                {
                    var graph = new SimpleCoreGraph<string, object, object>();
                    graph.AddNode("n1", new { Name = "N1" }, "n1");
                    graph.AddNode("n2", new { Name = "N2" }, "n2");
                    graph.AddNode("n3", new { Name = "N3" }, "n3");
                    graph.AddEdge("e1", "n1", "n2", true, null, 1.0);
                    graph.AddEdge("e2", "n1", "n3", true, null, 1.0);
                    var neighbors = graph.GetNeighbors("n1");
                    return neighbors.Count == 2;
                })
                .Run();
            result.AssertAllPassed();
        }

        private class SimpleResolutionContext : IResolutionContext
        {
            private readonly IReadOnlyList<string> _previousOutputs;
            private readonly IReadOnlyCollection<string> _inputKeys;
            private readonly Func<string, IReadOnlyList<string>> _getNodeOutputsById;
            private readonly Func<int, IReadOnlyList<string>> _getNodeOutputsByIndex;

            public SimpleResolutionContext(
                IReadOnlyList<string> previousOutputs,
                IReadOnlyCollection<string> inputKeys,
                Func<string, IReadOnlyList<string>> getNodeOutputsById,
                Func<int, IReadOnlyList<string>> getNodeOutputsByIndex)
            {
                _previousOutputs = previousOutputs;
                _inputKeys = inputKeys;
                _getNodeOutputsById = getNodeOutputsById;
                _getNodeOutputsByIndex = getNodeOutputsByIndex;
            }

            public IReadOnlyList<string> PreviousOutputs => _previousOutputs;
            public IReadOnlyCollection<string> InputKeys => _inputKeys;
            public IReadOnlyList<string> GetNodeOutputs(string nodeId) => _getNodeOutputsById(nodeId);
            public IReadOnlyList<string> GetNodeOutputs(int index) => _getNodeOutputsByIndex(index);
        }

        private class SimpleNodeHandler : INodeHandler
        {
            public string GetNodeId(object node)
            {
                var dict = node as IDictionary<string, object>;
                return dict?.ContainsKey("NodeId") == true ? dict["NodeId"]?.ToString() : null;
            }

            public IEnumerable<object> GetParameterKeys(object node)
            {
                var dict = node as IDictionary<string, object>;
                return dict?.ContainsKey("ParameterKeys") == true
                    ? (IEnumerable<object>)dict["ParameterKeys"]
                    : Enumerable.Empty<object>();
            }

            public IEnumerable<string> GetResultKeys(object node)
            {
                var dict = node as IDictionary<string, object>;
                return dict?.ContainsKey("ResultKeys") == true
                    ? ((IEnumerable<string>)dict["ResultKeys"])
                    : Enumerable.Empty<string>();
            }

            public void SetParameterKeys(object node, IEnumerable<object> keys)
            {
                if (node is IDictionary<string, object> dict)
                    dict["ParameterKeys"] = keys.ToList();
            }

            public string GetDisplayName(object node)
            {
                var dict = node as IDictionary<string, object>;
                return dict?.ContainsKey("Name") == true ? dict["Name"]?.ToString() : "unknown";
            }

            public object CloneNode(object node)
            {
                if (node is IDictionary<string, object> dict)
                    return new Dictionary<string, object>(dict);
                return node;
            }
        }
    }
}
