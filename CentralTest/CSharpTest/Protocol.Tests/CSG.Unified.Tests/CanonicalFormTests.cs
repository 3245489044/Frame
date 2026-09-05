using CSG.Unified;
using FluentAssertions;
using Xunit;

namespace CSG.Unified.Tests;

public class CanonicalFormTests
{

    [Fact]
    public void CreateEmptyForm_ShouldHaveNoNodesAndNoEdges()
    {
        var form = new CanonicalForm();

        form.Nodes.Should().BeEmpty();
        form.Edges.Should().BeEmpty();
        form.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void CreateFormWithNodesAndEdges_ShouldStoreCorrectly()
    {
        var form = new CanonicalForm
        {
            Nodes = new List<CanonicalNode>
            {
                new() { Id = "n1", Type = CanonicalNodeType.Start, Label = "Start" },
                new() { Id = "n2", Type = CanonicalNodeType.Action, Label = "Process" },
                new() { Id = "n3", Type = CanonicalNodeType.End, Label = "End" }
            },
            Edges = new List<CanonicalEdge>
            {
                new() { Id = "e1", SourceId = "n1", TargetId = "n2", Label = "start->process" },
                new() { Id = "e2", SourceId = "n2", TargetId = "n3", Label = "process->end" }
            }
        };

        form.Nodes.Should().HaveCount(3);
        form.Edges.Should().HaveCount(2);
    }


    [Fact]
    public void ResolveUri_Nodes_ShouldReturnAllNodes()
    {
        var form = new CanonicalForm
        {
            Nodes = new List<CanonicalNode>
            {
                new() { Id = "n1" }, new() { Id = "n2" }
            }
        };

        var result = form.ResolveUri("csg://nodes");

        result.Should().BeOfType<List<CanonicalNode>>();
        ((List<CanonicalNode>)result!).Should().HaveCount(2);
    }

    [Fact]
    public void ResolveUri_SingleNode_ShouldReturnNode()
    {
        var form = new CanonicalForm
        {
            Nodes = new List<CanonicalNode> { new() { Id = "n1" } }
        };

        var result = form.ResolveUri("csg://node/n1");

        result.Should().BeOfType<CanonicalNode>();
        ((CanonicalNode)result!).Id.Should().Be("n1");
    }

    [Fact]
    public void ResolveUri_InvalidScheme_ShouldThrow()
    {
        var form = new CanonicalForm();

        var act = () => form.ResolveUri("invalid://path");

        act.Should().Throw<ArgumentException>();
    }


    [Fact]
    public void ToJson_ShouldProduceValidJson()
    {
        var form = new CanonicalForm
        {
            Nodes = new List<CanonicalNode> { new() { Id = "n1" } }
        };

        var json = form.ToJson();

        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("n1");
    }

    [Fact]
    public void FromJson_ShouldDeserializeCorrectly()
    {
        var json = """{"nodes":[{"id":"n1"}],"edges":[],"metadata":{}}""";

        var form = CanonicalForm.FromJson(json);

        form.Should().NotBeNull();
        form!.Nodes.Should().HaveCount(1);
        form.Nodes[0].Id.Should().Be("n1");
    }

    [Fact]
    public void ToJsonAndBack_ShouldPreserveData()
    {
        var original = new CanonicalForm
        {
            Nodes = new List<CanonicalNode>
            {
                new() { Id = "n1", Type = CanonicalNodeType.Default, Label = "Test Node" }
            },
            Edges = new List<CanonicalEdge>(),
            Metadata = new Dictionary<string, object?>
            {
                ["version"] = "1.0"
            }
        };

        var json = original.ToJson();
        var restored = CanonicalForm.FromJson(json);

        restored.Should().NotBeNull();
        restored!.Nodes[0].Id.Should().Be(original.Nodes[0].Id);
        restored!.Nodes[0].Type.Should().Be(original.Nodes[0].Type);
        restored.Nodes[0].Label.Should().Be(original.Nodes[0].Label);
    }


    [Fact]
    public void ListUriTemplates_ShouldReturnAllTemplates()
    {
        var templates = CanonicalForm.ListUriTemplates();

        templates.Should().NotBeEmpty();
        templates.Should().AllBeOfType<UriTemplate>();
        templates.Select(t => t.UriPattern).Should().Contain("csg://nodes");
        templates.Select(t => t.UriPattern).Should().Contain("csg://node/{id}");
    }
}
