using CSG.Unified;
using FluentAssertions;
using Xunit;

namespace CSG.Unified.Tests;

public class ValidationEngineTests
{
    private readonly ValidationEngine _validator = new();


    [Fact]
    public void Validate_DuplicateNodeIds_ShouldReturnError()
    {
        var form = new CanonicalForm
        {
            State = FormState.Populated,
            Nodes = new List<CanonicalNode>
            {
                new() { Id = "n1" },
                new() { Id = "n1" }
            }
        };

        var result = _validator.Validate(form);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CsgErrorCode.DuplicateNodeId);
    }

    [Fact]
    public void Validate_DuplicateEdgeIds_ShouldReturnError()
    {
        var form = new CanonicalForm
        {
            State = FormState.Populated,
            Nodes = new List<CanonicalNode>
            {
                new() { Id = "n1" },
                new() { Id = "n2" }
            },
            Edges = new List<CanonicalEdge>
            {
                new() { Id = "e1", SourceId = "n1", TargetId = "n2" },
                new() { Id = "e1", SourceId = "n2", TargetId = "n1" }
            }
        };

        var result = _validator.Validate(form);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CsgErrorCode.DuplicateEdgeId);
    }


    [Fact]
    public void Validate_MissingSourceNode_ShouldReturnError()
    {
        var form = new CanonicalForm
        {
            State = FormState.Populated,
            Nodes = new List<CanonicalNode> { new() { Id = "n2" } },
            Edges = new List<CanonicalEdge>
            {
                new() { Id = "e1", SourceId = "n1", TargetId = "n2" }
            }
        };

        var result = _validator.Validate(form);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CsgErrorCode.MissingSourceNode);
    }

    [Fact]
    public void Validate_MissingTargetNode_ShouldReturnError()
    {
        var form = new CanonicalForm
        {
            State = FormState.Populated,
            Nodes = new List<CanonicalNode> { new() { Id = "n1" } },
            Edges = new List<CanonicalEdge>
            {
                new() { Id = "e1", SourceId = "n1", TargetId = "nonexistent" }
            }
        };

        var result = _validator.Validate(form);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CsgErrorCode.MissingTargetNode);
    }


    [Fact]
    public void Validate_EmptyForm_ShouldReturnError()
    {
        var form = new CanonicalForm { State = FormState.Populated };

        var result = _validator.Validate(form);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CsgErrorCode.EmptyForm);
    }


    [Fact]
    public void Validate_IsolatedNode_ShouldReturnWarning()
    {
        var form = new CanonicalForm
        {
            State = FormState.Populated,
            Nodes = new List<CanonicalNode>
            {
                new() { Id = "n1" },
                new() { Id = "n2" }
            },
            Edges = new List<CanonicalEdge>()
        };

        var result = _validator.Validate(form);

        result.Warnings.Should().Contain(w => w.Rule == "IsolatedNode");
    }


    [Fact]
    public void Validate_ValidForm_ShouldPass()
    {
        var form = new CanonicalForm
        {
            State = FormState.Populated,
            Nodes = new List<CanonicalNode>
            {
                new() { Id = "n1", Type = CanonicalNodeType.Start },
                new() { Id = "n2", Type = CanonicalNodeType.Action },
                new() { Id = "n3", Type = CanonicalNodeType.End }
            },
            Edges = new List<CanonicalEdge>
            {
                new() { Id = "e1", SourceId = "n1", TargetId = "n2" },
                new() { Id = "e2", SourceId = "n2", TargetId = "n3" }
            }
        };

        var result = _validator.Validate(form);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }


    [Fact]
    public void ValidateAll_ShouldValidateMultipleForms()
    {
        var validForm = new CanonicalForm
        {
            State = FormState.Populated,
            Nodes = new List<CanonicalNode> { new() { Id = "n1" } }
        };
        var invalidForm = new CanonicalForm { State = FormState.Populated };

        var results = new[] { validForm, invalidForm }
            .Select(f => _validator.Validate(f))
            .ToList();

        results.Should().HaveCount(2);
        results[0].IsValid.Should().BeTrue();
        results[1].IsValid.Should().BeFalse();
    }
}
