using BasicMethod.FrameMethod.Usings;
using CentralConsole.Testing;
using CentralConsole.Testing.Attributes;
using ChainGraph.Core;
using GraphBuild.FromGraph;
using GraphBuildingFramework;
using GraphConstruction;
using Graphkey.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkeletonFlow;

namespace BasicMethod.Tests;

[TestClass]
[TestGroup("GraphKeyUsings")]
public class GraphKeyUsingsTests : TestBase
{

    [TestMethod]
    [TestCategory("Placeholder")]
    public void Input_CreatesInputPlaceholder()
    {
        var ph = GraphKeyUsings.Input("userName");
        Assert.IsNotNull(ph);
        Assert.AreEqual(PlaceholderKind.Input, ph.Kind);
    }

    [TestMethod]
    [TestCategory("Placeholder")]
    public void Prev_CreatesPrevPlaceholder()
    {
        var ph = GraphKeyUsings.Prev();
        Assert.IsNotNull(ph);
        Assert.AreEqual(PlaceholderKind.Prev, ph.Kind);
    }

    [TestMethod]
    [TestCategory("Placeholder")]
    public void PrevByIndex_CreatesPrevPlaceholderWithIndex()
    {
        var ph = GraphKeyUsings.PrevByIndex(0);
        Assert.IsNotNull(ph);
        Assert.AreEqual(PlaceholderKind.Prev, ph.Kind);
    }

    [TestMethod]
    [TestCategory("Placeholder")]
    public void PrevByKey_CreatesPrevPlaceholderWithKey()
    {
        var ph = GraphKeyUsings.PrevByKey("result");
        Assert.IsNotNull(ph);
        Assert.AreEqual(PlaceholderKind.Prev, ph.Kind);
    }

    [TestMethod]
    [TestCategory("Placeholder")]
    public void From_NodeId_CreatesNodeReference()
    {
        var ph = GraphKeyUsings.From("nodeA");
        Assert.IsNotNull(ph);
        Assert.AreEqual(PlaceholderKind.NodeReference, ph.Kind);
    }

    [TestMethod]
    [TestCategory("Placeholder")]
    public void From_NodeIdAndIndex_CreatesNodeReferenceWithIndex()
    {
        var ph = GraphKeyUsings.From("nodeA", 0);
        Assert.IsNotNull(ph);
        Assert.AreEqual(PlaceholderKind.NodeReference, ph.Kind);
    }

    [TestMethod]
    [TestCategory("Placeholder")]
    public void From_NodeIdAndKey_CreatesNodeReferenceWithKey()
    {
        var ph = GraphKeyUsings.From("nodeA", "output");
        Assert.IsNotNull(ph);
        Assert.AreEqual(PlaceholderKind.NodeReference, ph.Kind);
    }

    [TestMethod]
    [TestCategory("Placeholder")]
    public void Node_CreatesNodeIndexPlaceholder()
    {
        var ph = GraphKeyUsings.Node(0);
        Assert.IsNotNull(ph);
        Assert.AreEqual(PlaceholderKind.NodeIndex, ph.Kind);
    }


    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void ParsePlaceholder_InputRef_ReturnsPlaceholder()
    {
        var result = GraphKeyUsings.ParsePlaceholder("@input.name");
        Assert.IsNotNull(result);
        Assert.IsTrue(result is Placeholder);
    }

    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void ParsePlaceholder_NodeRef_ReturnsPlaceholder()
    {
        var result = GraphKeyUsings.ParsePlaceholder("@node.id");
        Assert.IsNotNull(result);
        Assert.IsTrue(result is Placeholder);
    }

    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void IsInputRef_WithInputRef_ReturnsTrue()
    {
        Assert.IsTrue(GraphKeyUsings.IsInputRef("@input.name"));
    }

    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void IsInputRef_WithNonInputRef_ReturnsFalse()
    {
        Assert.IsFalse(GraphKeyUsings.IsInputRef("@node.id"));
        Assert.IsFalse(GraphKeyUsings.IsInputRef("plain text"));
    }

    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void IsNodeRef_WithNodeRef_ReturnsTrue()
    {
        Assert.IsTrue(GraphKeyUsings.IsNodeRef("@node.id"));
    }

    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void IsNodeRef_WithNonNodeRef_ReturnsFalse()
    {
        Assert.IsFalse(GraphKeyUsings.IsNodeRef("@input.x"));
    }

    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void IsPrevRef_WithPrevRef_ReturnsTrue()
    {
        Assert.IsTrue(GraphKeyUsings.IsPrevRef("@prev"));
    }

    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void ExtractInputKey_ReturnsKey()
    {
        var key = GraphKeyUsings.ExtractInputKey("@input.userName");
        Assert.AreEqual("userName", key);
    }

    [TestMethod]
    [TestCategory("PlaceholderParser")]
    public void ExtractNodePath_ReturnsPath()
    {
        var path = GraphKeyUsings.ExtractNodePath("@node.myNode.output");
        Assert.IsNotNull(path);
    }


    [TestMethod]
    [TestCategory("PlaceholderResolver")]
    public void ResolvePlaceholder_Input_ReturnsKey()
    {
        var ph = GraphKeyUsings.Input("testKey");
        var context = new SimpleResolutionContext();
        var resolved = GraphKeyUsings.ResolvePlaceholder(ph, context);
        Assert.AreEqual("testKey", resolved);
    }


    [TestMethod]
    [TestCategory("Pipeline1")]
    public void CreateWorkflow_ReturnsBuilder()
    {
        var handler = new TestNodeHandler();
        var workflow = GraphKeyUsings.CreateWorkflow(handler);
        Assert.IsNotNull(workflow);
    }

    [TestMethod]
    [TestCategory("Pipeline1")]
    public void BeginChain_ReturnsChainBuilder()
    {
        var handler = new TestNodeHandler();
        var workflow = GraphKeyUsings.CreateWorkflow(handler);
        var chain = GraphKeyUsings.BeginChain(workflow, "testChain");
        Assert.IsNotNull(chain);
    }

    [TestMethod]
    [TestCategory("Pipeline1")]
    public void SetValidation_EnablesValidation()
    {
        var handler = new TestNodeHandler();
        var workflow = GraphKeyUsings.CreateWorkflow(handler);
        var result = GraphKeyUsings.SetValidation(workflow, true);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    [TestCategory("Pipeline1")]
    public void BuildWorkflow_ReturnsNodeList()
    {
        var handler = new TestNodeHandler();
        var workflow = GraphKeyUsings.CreateWorkflow(handler);
        var chain = GraphKeyUsings.BeginChain(workflow, "testChain");
        chain.Then(new { Symbol = "NodeA", ResultKeys = new[] { "out1" } });
        var nodes = GraphKeyUsings.BuildWorkflow(workflow);
        Assert.IsNotNull(nodes);
    }

    [TestMethod]
    [TestCategory("Pipeline1")]
    public void BuildWorkflowJson_ReturnsJsonString()
    {
        var handler = new TestNodeHandler();
        var workflow = GraphKeyUsings.CreateWorkflow(handler);
        var chain = GraphKeyUsings.BeginChain(workflow, "testChain");
        chain.Then(new { Symbol = "NodeA", ResultKeys = new[] { "out1" } });
        var json = GraphKeyUsings.BuildWorkflowJson(workflow);
        Assert.IsFalse(string.IsNullOrEmpty(json));
    }


    [TestMethod]
    [TestCategory("Pipeline2")]
    public void CreateGraph_ReturnsEmptyGraph()
    {
        var graph = GraphKeyUsings.CreateGraph("testGraph");
        Assert.IsNotNull(graph);
        Assert.AreEqual("testGraph", graph.Name);
    }

    [TestMethod]
    [TestCategory("Pipeline2")]
    public void TopologicalSort_EmptyGraph_ReturnsEmptyList()
    {
        var graph = GraphKeyUsings.CreateGraph();
        var sorted = GraphKeyUsings.TopologicalSort(graph);
        Assert.IsNotNull(sorted);
        Assert.AreEqual(0, sorted.Count);
    }

    [TestMethod]
    [TestCategory("Pipeline2")]
    public void TopologicalSort_SingleNode_ReturnsNode()
    {
        var graph = GraphKeyUsings.CreateGraph();
        graph.Nodes.Add(new GraphNode { Key = "A", Symbol = "Start" });
        var sorted = GraphKeyUsings.TopologicalSort(graph);
        Assert.AreEqual(1, sorted.Count);
        Assert.AreEqual("A", sorted[0]);
    }

    [TestMethod]
    [TestCategory("Pipeline2")]
    public void MergeGraphs_MergesNodes()
    {
        var target = GraphKeyUsings.CreateGraph("target");
        target.Nodes.Add(new GraphNode { Key = "A", Symbol = "NodeA" });

        var source = GraphKeyUsings.CreateGraph("source");
        source.Nodes.Add(new GraphNode { Key = "B", Symbol = "NodeB" });

        GraphKeyUsings.MergeGraphs(target, source);
        Assert.AreEqual(2, target.Nodes.Count);
    }


    [TestMethod]
    [TestCategory("Pipeline3")]
    public void CreateGraphBuilder_ReturnsBuilder()
    {
        var builder = GraphKeyUsings.CreateGraphBuilder("demo");
        Assert.IsNotNull(builder);
    }

    [TestMethod]
    [TestCategory("Pipeline3")]
    public void CreateTemplateBuilder_ReturnsBuilder()
    {
        var builder = GraphKeyUsings.CreateTemplateBuilder("MyTemplate");
        Assert.IsNotNull(builder);
    }

    [TestMethod]
    [TestCategory("Pipeline3")]
    public void BuildToJson_ReturnsJson()
    {
        var builder = GraphKeyUsings.CreateGraphBuilder("demo");
        builder.AddInput("input1");
        builder.AddNode("node1", "FuncA");
        var json = GraphKeyUsings.BuildToJson(builder);
        Assert.IsFalse(string.IsNullOrEmpty(json));
    }


    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void CreateBuildContext_ReturnsContext()
    {
        using var ctx = new SkeletonContext();
        var buildCtx = GraphKeyUsings.CreateBuildContext(ctx);
        Assert.IsNotNull(buildCtx);
    }

    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void SingleNode_ReturnsBuilder()
    {
        var builder = GraphKeyUsings.SingleNode("FuncA", new[] { "input1" }, "output1");
        Assert.IsNotNull(builder);
    }

    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void Sequential_ReturnsBuilder()
    {
        var b1 = GraphKeyUsings.SingleNode("FuncA", new[] { "input1" }, "out1");
        var b2 = GraphKeyUsings.SingleNode("FuncB", new[] { "input2" }, "out2");
        var seq = GraphKeyUsings.Sequential(b1, b2);
        Assert.IsNotNull(seq);
    }

    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void Conditional_ReturnsBuilder()
    {
        var cond = GraphKeyUsings.Conditional(
            ctx => true,
            GraphKeyUsings.SingleNode("True", new[] { "in" }, "out"));
        Assert.IsNotNull(cond);
    }

    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void MultiNode_ReturnsBuilder()
    {
        var multi = GraphKeyUsings.MultiNode("FuncB", new[] { "in1", "in2" }, i => $"out_{i}");
        Assert.IsNotNull(multi);
    }

    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void CustomNode_ReturnsBuilder()
    {
        var custom = GraphKeyUsings.CustomNode("FuncC", "result");
        Assert.IsNotNull(custom);
    }

    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void CreateParameterBuilder_ReturnsBuilder()
    {
        var pb = GraphKeyUsings.CreateParameterBuilder();
        Assert.IsNotNull(pb);
    }

    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void ParameterBuilder_AddConstant_BuildsArray()
    {
        var result = GraphKeyUsings.CreateParameterBuilder()
            .AddConstant("value1")
            .AddConstant(42)
            .Build();
        Assert.AreEqual(2, result.Length);
        Assert.AreEqual("value1", result[0]);
        Assert.AreEqual(42, result[1]);
    }

    [TestMethod]
    [TestCategory("NodeBuilder")]
    public void ParameterBuilder_AddPlaceholder_BuildsArray()
    {
        var result = GraphKeyUsings.CreateParameterBuilder()
            .AddPlaceholder(GraphKeyUsings.Input("key"))
            .Build();
        Assert.AreEqual(1, result.Length);
        Assert.IsTrue(result[0] is Placeholder);
    }


    [TestMethod]
    [TestCategory("GraphComponent")]
    public void CreateComponent_ReturnsComponent()
    {
        var builder = GraphKeyUsings.SingleNode("FuncA", new[] { "input" }, "output");
        var component = GraphKeyUsings.CreateComponent("Step1", new[] { "input" }, new[] { "output" }, builder);
        Assert.IsNotNull(component);
    }

    [TestMethod]
    [TestCategory("GraphComponent")]
    public void CreateComposite_ReturnsComposite()
    {
        var builder = GraphKeyUsings.SingleNode("FuncA", new[] { "input" }, "output");
        var comp1 = GraphKeyUsings.CreateComponent("Step1", new[] { "input" }, new[] { "output" }, builder);
        var composite = GraphKeyUsings.CreateComposite("Pipeline", new IGraphComponent[] { comp1 });
        Assert.IsNotNull(composite);
    }

    [TestMethod]
    [TestCategory("GraphComponent")]
    public void ToSkeleton_ReturnsSkeleton()
    {
        var builder = GraphKeyUsings.SingleNode("FuncA", new[] { "input" }, "output");
        var component = GraphKeyUsings.CreateComponent("Step1", new[] { "input" }, new[] { "output" }, builder);
        var skeleton = GraphKeyUsings.ToSkeleton(component);
        Assert.IsNotNull(skeleton);
    }


    [TestMethod]
    [TestCategory("Template")]
    public void CreateTemplate_ReturnsTemplate()
    {
        var template = GraphKeyUsings.CreateTemplate<GraphNode>("MyTemplate");
        Assert.IsNotNull(template);
        Assert.AreEqual("MyTemplate", template.Name);
    }

    [TestMethod]
    [TestCategory("Template")]
    public void CreateRegistry_ReturnsRegistry()
    {
        var registry = GraphKeyUsings.CreateRegistry<GraphNode>();
        Assert.IsNotNull(registry);
    }

    [TestMethod]
    [TestCategory("Template")]
    public void RegisterTemplate_RegistersSuccessfully()
    {
        var registry = GraphKeyUsings.CreateRegistry<GraphNode>();
        var template = GraphKeyUsings.CreateTemplate<GraphNode>("TestTemplate");
        var node = new GraphNode { Key = "n1", Symbol = "FuncA" };
        template.Nodes.Add(node);
        template.OriginalKeys[node] = "n1";
        GraphKeyUsings.RegisterTemplate(registry, template);

        Assert.IsTrue(registry.TryGet("TestTemplate", out var retrieved));
        Assert.IsNotNull(retrieved);
    }

    [TestMethod]
    [TestCategory("Template")]
    public void CreateInstanceRef_ReturnsReference()
    {
        var instanceRef = GraphKeyUsings.CreateInstanceRef("inst1", "MyTemplate",
            new Dictionary<string, object> { { "param1", "value1" } });
        Assert.IsNotNull(instanceRef);
        Assert.AreEqual("inst1", instanceRef.InstanceKey);
        Assert.AreEqual("MyTemplate", instanceRef.TemplateName);
    }

    [TestMethod]
    [TestCategory("Template")]
    public void CreateInstanceRef_NullBindings_UsesEmptyDict()
    {
        var instanceRef = GraphKeyUsings.CreateInstanceRef("inst1", "MyTemplate", null);
        Assert.IsNotNull(instanceRef.ParameterBindings);
        Assert.AreEqual(0, instanceRef.ParameterBindings.Count);
    }


    [TestMethod]
    [TestCategory("Cloner")]
    public void CloneJson_ClonesObject()
    {
        var original = new GraphNode { Key = "test", Symbol = "TestNode" };
        var cloned = GraphKeyUsings.CloneJson(original);
        Assert.IsNotNull(cloned);
        Assert.AreEqual(original.Key, cloned.Key);
        Assert.AreEqual(original.Symbol, cloned.Symbol);
    }

    [TestMethod]
    [TestCategory("Cloner")]
    public void CreateJsonCloner_ReturnsCloner()
    {
        var cloner = GraphKeyUsings.CreateJsonCloner<GraphNode>();
        Assert.IsNotNull(cloner);
    }


    [TestMethod]
    [TestCategory("Validator")]
    public void ValidateResultKeys_EmptyKeys_Throws()
    {
        var handler = new TestNodeHandler();
        Assert.ThrowsException<ArgumentException>(() =>
            GraphKeyUsings.ValidateResultKeys(new[] { "" }, "Result", "TestNode"));
    }


    [TestMethod]
    [TestCategory("GraphKeyNode")]
    public void AddGraphNode_AddsNode()
    {
        var nodes = new List<object>();
        GraphKeyUsings.AddGraphNode(nodes, "FuncA", "node1", "funcA",
            new object[] { "param1" }, new[] { "result" });
        Assert.AreEqual(1, nodes.Count);
    }

    [TestMethod]
    [TestCategory("GraphKeyNode")]
    public void BuildGraphNodeJson_ReturnsJson()
    {
        var nodes = new List<object>();
        GraphKeyUsings.AddGraphNode(nodes, "FuncA", "node1", "funcA",
            new object[] { "param1" }, new[] { "result" });
        var json = GraphKeyUsings.BuildGraphNodeJson(nodes);
        Assert.IsFalse(string.IsNullOrEmpty(json));
    }


    [TestMethod]
    [TestCategory("Prefix")]
    public void PrefixInput_ReturnsAtInput()
    {
        Assert.AreEqual("@input.", GraphKeyUsings.PrefixInput);
    }

    [TestMethod]
    [TestCategory("Prefix")]
    public void PrefixNode_ReturnsAtNode()
    {
        Assert.AreEqual("@node.", GraphKeyUsings.PrefixNode);
    }

    [TestMethod]
    [TestCategory("Prefix")]
    public void PrefixPrev_ReturnsAtPrev()
    {
        Assert.AreEqual("@prev", GraphKeyUsings.PrefixPrev);
    }

    [TestMethod]
    [TestCategory("Prefix")]
    public void AtSymbol_ReturnsAt()
    {
        Assert.AreEqual("@", GraphKeyUsings.AtSymbol);
    }


    [TestMethod]
    [TestCategory("DefaultKey")]
    public void GetDefaultOutputKey_ReturnsFormattedKey()
    {
        var key = GraphKeyUsings.GetDefaultOutputKey("Func", "node1");
        Assert.AreEqual("Func_node1", key);
    }


    private class SimpleResolutionContext : IResolutionContext
    {
        public IReadOnlyList<string> PreviousOutputs { get; set; } = new List<string> { "prevOut" };
        public IReadOnlyCollection<string> InputKeys { get; set; } = new List<string> { "input1" };

        public IReadOnlyList<string> GetNodeOutputs(string nodeId)
            => new List<string> { "output1" };

        public IReadOnlyList<string> GetNodeOutputs(int index)
            => new List<string> { "output1" };
    }

    private class TestNodeHandler : INodeHandler
    {
        public string GetNodeId(object node)
            => node?.GetType().GetProperty("Symbol")?.GetValue(node)?.ToString() ?? "unknown";

        public IEnumerable<object> GetParameterKeys(object node)
            => node?.GetType().GetProperty("ParameterKeys")?.GetValue(node) as IEnumerable<object>
               ?? Array.Empty<object>();

        public IEnumerable<string> GetResultKeys(object node)
            => node?.GetType().GetProperty("ResultKeys")?.GetValue(node) as IEnumerable<string>
               ?? Array.Empty<string>();

        public void SetParameterKeys(object node, IEnumerable<object> keys)
        {
            var prop = node?.GetType().GetProperty("ParameterKeys");
            if (prop != null) prop.SetValue(node, keys);
        }

        public string GetDisplayName(object node)
            => node?.GetType().GetProperty("Symbol")?.GetValue(node)?.ToString() ?? "unknown";

        public object CloneNode(object node)
        {
            var type = node?.GetType();
            if (type == null) return null!;
            var json = System.Text.Json.JsonSerializer.Serialize(node, type);
            return System.Text.Json.JsonSerializer.Deserialize(json, type)!;
        }
    }
}
