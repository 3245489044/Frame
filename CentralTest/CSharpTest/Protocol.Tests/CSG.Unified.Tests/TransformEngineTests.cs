using CSG.Unified;
using FluentAssertions;
using Xunit;

namespace CSG.Unified.Tests;

public class TransformEngineTests
{
    private readonly TransformEngine _engine = new();


    [Fact]
    public void ConfigToCanonical_ShouldReturnCanonicalForm()
    {
        var config = new DefaultConfiguration();
        var form = _engine.ConfigToCanonical(config);

        form.Should().NotBeNull();
        form.Metadata.Should().ContainKey("source");
    }

    [Fact]
    public void CanonicalToConfig_ShouldReturnDefaultConfiguration()
    {
        var form = new CanonicalForm
        {
            Metadata = new Dictionary<string, object?>
            {
                ["source"] = "test"
            }
        };

        var config = _engine.CanonicalToConfig<DefaultConfiguration>(form);

        config.Should().NotBeNull();
        config.Should().BeOfType<DefaultConfiguration>();
        config.Form.Should().Be(form);
    }


    [Fact]
    public void StateMachineToCanonical_ShouldReturnCanonicalForm()
    {
        var sm = new TestStateMachine();
        var form = _engine.StateMachineToCanonical(sm);

        form.Should().NotBeNull();
    }

    [Fact]
    public void CanonicalToStateMachine_WithoutAdapter_ShouldThrow()
    {
        var form = new CanonicalForm();

        var act = () => _engine.CanonicalToStateMachine(form);

        act.Should().Throw<NotSupportedException>();
    }


    [Fact]
    public void GraphToCanonical_ShouldReturnCanonicalForm()
    {
        var graph = new TestGraph();
        var form = _engine.GraphToCanonical(graph);

        form.Should().NotBeNull();
    }

    [Fact]
    public void CanonicalToGraph_WithoutAdapter_ShouldThrow()
    {
        var form = new CanonicalForm();

        var act = () => _engine.CanonicalToGraph(form);

        act.Should().Throw<NotSupportedException>();
    }


    [Fact]
    public void ListOperations_ShouldReturnAllBuiltInOperations()
    {
        var operations = _engine.ListOperations();

        operations.Should().NotBeEmpty();
        operations.Select(o => o.Name).Should().Contain("ConfigToCanonical");
        operations.Select(o => o.Name).Should().Contain("CanonicalToConfig");
        operations.Select(o => o.Name).Should().Contain("StateMachineToCanonical");
        operations.Select(o => o.Name).Should().Contain("GraphToCanonical");
        operations.Select(o => o.Name).Should().Contain("ValidateConsistency");
    }

    [Fact]
    public void ListOperationsByCategory_ShouldFilterCorrectly()
    {
        var transformOps = _engine.ListOperationsByCategory("Transform");
        var validateOps = _engine.ListOperationsByCategory("Validate");

        transformOps.Should().NotBeEmpty();
        validateOps.Should().NotBeEmpty();
    }


    [Fact]
    public void CallOperation_ValidOperation_ShouldSucceed()
    {
        var config = new DefaultConfiguration();
        var result = _engine.CallOperation("ConfigToCanonical", config);

        result.Should().BeOfType<CanonicalForm>();
    }

    [Fact]
    public void CallOperation_InvalidOperation_ShouldThrow()
    {
        var act = () => _engine.CallOperation("NonExistent", new object());

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void CallOperation_WrongInputType_ShouldThrow()
    {
        var act = () => _engine.CallOperation("ConfigToCanonical", "wrong type");

        act.Should().Throw<InvalidOperationException>();
    }


    [Fact]
    public void ValidateConsistency_IdenticalForms_ShouldBeValid()
    {
        var form = new CanonicalForm
        {
            Nodes = new List<CanonicalNode>
            {
                new() { Id = "n1", Type = CanonicalNodeType.Start },
                new() { Id = "n2", Type = CanonicalNodeType.End }
            },
            Edges = new List<CanonicalEdge>
            {
                new() { Id = "e1", SourceId = "n1", TargetId = "n2" }
            }
        };

        var result = _engine.ValidateConsistency(form, form, form);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateConsistency_MissingNodeInSM_ShouldBeInvalid()
    {
        var configForm = new CanonicalForm
        {
            Nodes = new List<CanonicalNode> { new() { Id = "n1" }, new() { Id = "n2" } }
        };
        var smForm = new CanonicalForm
        {
            Nodes = new List<CanonicalNode> { new() { Id = "n1" } }
        };
        var graphForm = new CanonicalForm
        {
            Nodes = new List<CanonicalNode> { new() { Id = "n1" }, new() { Id = "n2" } }
        };

        var result = _engine.ValidateConsistency(configForm, smForm, graphForm);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Rule == "NodeCoverage");
    }


    [Fact]
    public void RegisterOperation_ShouldBeCallable()
    {
        _engine.RegisterOperation(
            new TransformOperation
            {
                Name = "CustomOp",
                Label = "Custom",
                Description = "A custom operation",
                Category = "Custom",
                InputType = typeof(string),
                OutputType = typeof(string)
            },
            input => $"Processed: {input}"
        );

        var result = _engine.CallOperation("CustomOp", "hello");

        result.Should().Be("Processed: hello");
    }
}

public class TestStateMachine : IStateMachine
{
    public string CurrentState => "Idle";

    public event Action<string, string, string>? OnStateChanged;

    public bool CanTransitionTo(string state) => true;

    public bool TryTransitionTo(string state, object? trigger = null)
    {
        OnStateChanged?.Invoke("test", CurrentState, state);
        return true;
    }

    public CanonicalForm ToCanonical()
    {
        return new CanonicalForm
        {
            Metadata = new Dictionary<string, object?>
            {
                ["source"] = "TestStateMachine"
            }
        };
    }
}

public class TestGraph : IGraph
{
    public IEnumerable<string> NodeIds => new[] { "n1", "n2" };

    public IEnumerable<(string Source, string Target)> EdgePairs => new[] { ("n1", "n2") };

    public CanonicalForm ToCanonical()
    {
        return new CanonicalForm
        {
            Metadata = new Dictionary<string, object?>
            {
                ["source"] = "TestGraph"
            }
        };
    }
}
