
using BasicMethod.Declarative.Constraints;
using ParameterResolver.Abstractions;
using ParameterResolver.Validation;

namespace ParameterResolver.Validation.Tests;

[TestClass]
public class ParameterMappingValidatorTests
{
    [TestMethod]
    public void Constructor_Default_ShouldInitialize()
    {
        var validator = new ParameterMappingValidator();

        Assert.IsNotNull(validator);
        Assert.AreEqual("ParameterMappingValidator", validator.Name);
    }

    [TestMethod]
    public void Constructor_Default_ShouldHaveConstraints()
    {
        var validator = new ParameterMappingValidator();

        Assert.IsTrue(validator.ConstraintCount > 0);
    }

    [TestMethod]
    public void Constructor_WithName_ShouldSetName()
    {
        var validator = new ParameterMappingValidator("CustomValidator");

        Assert.AreEqual("CustomValidator", validator.Name);
    }

    [TestMethod]
    public void Constructor_NullName_ShouldThrow()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new ParameterMappingValidator(null!));
    }

    [TestMethod]
    public void Validate_ValidMappings_ShouldReturnValid()
    {
        var validator = new ParameterMappingValidator();
        var mappings = new List<ParameterMapping>
        {
            new() { Target = "width", SourceType = "literal", Source = "100" },
            new() { Target = "height", SourceType = "literal", Source = "200" }
        };

        var result = validator.Validate(mappings);

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void Validate_EmptyTarget_ShouldReturnInvalid()
    {
        var validator = new ParameterMappingValidator();
        var mappings = new List<ParameterMapping>
        {
            new() { Target = "", SourceType = "literal", Source = "100" }
        };

        var result = validator.Validate(mappings);

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_DuplicateTarget_ShouldReturnInvalid()
    {
        var validator = new ParameterMappingValidator();
        var mappings = new List<ParameterMapping>
        {
            new() { Target = "width", SourceType = "literal", Source = "100" },
            new() { Target = "width", SourceType = "literal", Source = "200" }
        };

        var result = validator.Validate(mappings);

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_InvalidSourceType_ShouldReturnInvalid()
    {
        var validator = new ParameterMappingValidator();
        var mappings = new List<ParameterMapping>
        {
            new() { Target = "width", SourceType = "invalid_type", Source = "100" }
        };

        var result = validator.Validate(mappings);

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void Validate_ControlSourceWithoutSource_ShouldReturnInvalid()
    {
        var validator = new ParameterMappingValidator();
        var mappings = new List<ParameterMapping>
        {
            new() { Target = "width", SourceType = "control", Source = "" }
        };

        var result = validator.Validate(mappings);

        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void ValidateOrThrow_ValidMappings_ShouldNotThrow()
    {
        var validator = new ParameterMappingValidator();
        var mappings = new List<ParameterMapping>
        {
            new() { Target = "width", SourceType = "literal", Source = "100" }
        };

        validator.ValidateOrThrow(mappings);
    }

    [TestMethod]
    public void ValidateOrThrow_InvalidMappings_ShouldThrow()
    {
        var validator = new ParameterMappingValidator();
        var mappings = new List<ParameterMapping>
        {
            new() { Target = "", SourceType = "literal", Source = "100" }
        };

        Assert.ThrowsException<InvalidOperationException>(() =>
            validator.ValidateOrThrow(mappings));
    }

    [TestMethod]
    public void AddConstraint_ShouldIncreaseConstraintCount()
    {
        var validator = new ParameterMappingValidator("Test");
        var countBefore = validator.ConstraintCount;

        validator.AddConstraint(
            new PredicateConstraint<IReadOnlyList<ParameterMapping>>(
                "Test", "Test constraint", m => true));

        Assert.AreEqual(countBefore + 1, validator.ConstraintCount);
    }

    [TestMethod]
    public void AddConstraint_Null_ShouldThrow()
    {
        var validator = new ParameterMappingValidator();

        Assert.ThrowsException<ArgumentNullException>(() =>
            validator.AddConstraint(null!));
    }
}
