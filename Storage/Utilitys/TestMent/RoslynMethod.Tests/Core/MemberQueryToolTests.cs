using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tests.Core;

public class MemberQueryToolTests
{
    private const string CodeWithMembers = @"
using System;
class TestClass
{
    public void Method1() { }
    private int Method2() { return 0; }
    protected string Method3(string s) { return s; }

    public TestClass() { }
    ~TestClass() { }

    public string Property1 { get; set; }
    private int _field1;
    public event EventHandler Event1;
}
class Program { public static void Main() { } }
";

    [Fact]
    public void GetMethods_WithoutFilter_ReturnsAllMethods()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var methods = context.GetMethods().ToList();

        methods.Should().HaveCount(3);
        methods.Select(m => m.Identifier.Text).Should().Contain(new[] { "Method1", "Method2", "Method3" });
    }

    [Fact]
    public void GetMethods_WithClassName_ReturnsMethodsInClass()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var methods = context.GetMethods("TestClass").ToList();

        methods.Should().HaveCount(3);
    }

    [Fact]
    public void GetMethods_WithMethodName_ReturnsMatchingMethod()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var methods = context.GetMethods(methodName: "Method1").ToList();

        methods.Should().HaveCount(1);
        methods[0].Identifier.Text.Should().Be("Method1");
    }

    [Fact]
    public void GetMethods_WithClassNameAndMethodName_ReturnsMatchingMethod()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var methods = context.GetMethods("TestClass", "Method1").ToList();

        methods.Should().HaveCount(1);
    }

    [Fact]
    public void GetMethods_WithNonExistentName_ReturnsEmpty()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var methods = context.GetMethods(methodName: "NonExistent").ToList();

        methods.Should().BeEmpty();
    }

    [Fact]
    public void GetConstructors_WithoutFilter_ReturnsAllConstructors()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var constructors = context.GetConstructors().ToList();

        constructors.Should().HaveCount(1);
        constructors[0].Identifier.Text.Should().Be("TestClass");
    }

    [Fact]
    public void GetConstructors_WithClassName_ReturnsConstructorsInClass()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var constructors = context.GetConstructors("TestClass").ToList();

        constructors.Should().HaveCount(1);
    }

    [Fact]
    public void GetDestructors_WithClassName_ReturnsDestructor()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var destructors = context.GetDestructors("TestClass").ToList();

        destructors.Should().HaveCount(1);
    }

    [Fact]
    public void GetFields_WithoutFilter_ReturnsAllFields()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var fields = context.GetFields().ToList();

        fields.Should().HaveCount(1);
    }

    [Fact]
    public void GetFields_WithFieldName_ReturnsMatchingField()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var fields = context.GetFields(fieldName: "_field1").ToList();

        fields.Should().HaveCount(1);
    }

    [Fact]
    public void GetProperties_WithoutFilter_ReturnsAllProperties()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var properties = context.GetProperties().ToList();

        properties.Should().HaveCount(1);
        properties[0].Identifier.Text.Should().Be("Property1");
    }

    [Fact]
    public void GetProperties_WithPropertyName_ReturnsMatchingProperty()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var properties = context.GetProperties(propertyName: "Property1").ToList();

        properties.Should().HaveCount(1);
    }

    [Fact]
    public void GetEvents_WithoutFilter_ReturnsAllEvents()
    {
        var context = AnalysisContext.Create(CodeWithMembers);

        var events = context.GetEvents().ToList();

        events.Should().HaveCount(1);
        events[0].Identifier.Text.Should().Be("Event1");
    }
}
