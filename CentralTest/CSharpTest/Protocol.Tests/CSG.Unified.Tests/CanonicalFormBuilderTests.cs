using CSG.Unified;
using FluentAssertions;
using Xunit;

namespace CSG.Unified.Tests;

public class CanonicalFormBuilderTests
{

    [Fact]
    public void Build_EmptyForm_ShouldSucceed()
    {
        var form = new CanonicalFormBuilder()
            .Build();

        form.Should().NotBeNull();
        form.Nodes.Should().BeEmpty();
        form.Edges.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithSingleNode_ShouldContainNode()
    {
        var form = new CanonicalFormBuilder()
            .AddNode("n1", "Start Node", CanonicalNodeType.Start)
            .Build();

        form.Nodes.Should().HaveCount(1);
        form.Nodes[0].Id.Should().Be("n1");
        form.Nodes[0].Type.Should().Be(CanonicalNodeType.Start);
        form.Nodes[0].Label.Should().Be("Start Node");
    }

    [Fact]
    public void Build_WithNodesAndEdges_ShouldContainAll()
    {
        var form = new CanonicalFormBuilder()
            .AddNode("n1", type: CanonicalNodeType.Start)
            .AddNode("n2", type: CanonicalNodeType.Action)
            .AddNode("n3", type: CanonicalNodeType.End)
            .AddEdge("e1", "n1", "n2", "start->process")
            .AddEdge("e2", "n2", "n3", "process->end")
            .Build();

        form.Nodes.Should().HaveCount(3);
        form.Edges.Should().HaveCount(2);
    }


    [Fact]
    public void Build_WithMetadata_ShouldContainMetadata()
    {
        var form = new CanonicalFormBuilder()
            .WithMetadata("version", "1.0")
            .WithMetadata("author", "test")
            .Build();

        form.Metadata.Should().ContainKey("version");
        form.Metadata.Should().ContainKey("author");
        form.Metadata["version"].Should().Be("1.0");
    }


    [Fact]
    public void Build_ChainedCalls_ShouldWork()
    {
        var form = new CanonicalFormBuilder()
            .AddNode("n1", type: CanonicalNodeType.Start)
            .AddNode("n2", type: CanonicalNodeType.End)
            .AddEdge("e1", "n1", "n2")
            .WithMetadata("source", "test")
            .Build();

        form.Nodes.Should().HaveCount(2);
        form.Edges.Should().HaveCount(1);
        form.Metadata.Should().ContainKey("source");
    }


    [Fact]
    public void Build_FromExistingForm_ShouldPreserveData()
    {
        var existing = new CanonicalForm
        {
            Nodes = new List<CanonicalNode> { new() { Id = "n1" } },
            Metadata = new Dictionary<string, object?> { ["version"] = "1.0" }
        };

        var form = new CanonicalFormBuilder()
            .AddNode(existing.Nodes[0])
            .WithMetadataRange(existing.Metadata)
            .AddNode("n2", type: CanonicalNodeType.Action)
            .Build();

        form.Nodes.Should().HaveCount(2);
        form.Metadata.Should().ContainKey("version");
    }


    [Fact]
    public void Clear_ShouldClearAllData()
    {
        var builder = new CanonicalFormBuilder()
            .AddNode("n1", type: CanonicalNodeType.Start)
            .AddEdge("e1", "n1", "n2");

        builder.Clear();

        var form = builder.Build();
        form.Nodes.Should().BeEmpty();
        form.Edges.Should().BeEmpty();
    }


    [Fact]
    public void Build_ComplexWorkflow_ShouldSucceed()
    {
        var form = new CanonicalFormBuilder()
            .AddNode("start", "开始", CanonicalNodeType.Start)
            .AddNode("validate", "验证", CanonicalNodeType.Condition)
            .AddNode("process", "处理", CanonicalNodeType.Action)
            .AddNode("notify", "通知", CanonicalNodeType.Action)
            .AddNode("end", "结束", CanonicalNodeType.End)
            .AddEdge("e1", "start", "validate", "开始验证")
            .AddEdge("e2", "validate", "process", "验证通过")
            .AddEdge("e3", "validate", "end", "验证失败")
            .AddEdge("e4", "process", "notify", "处理完成")
            .AddEdge("e5", "notify", "end", "通知完成")
            .WithMetadata("name", "工作流示例")
            .WithMetadata("version", "1.0")
            .Build();

        form.Nodes.Should().HaveCount(5);
        form.Edges.Should().HaveCount(5);
        form.Metadata["name"].Should().Be("工作流示例");
    }
}
