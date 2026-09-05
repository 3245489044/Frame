using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tests.Core;

public class TypeQueryToolTests
{
    private const string CodeWithMultipleTypes = @"
class ClassA { }
class ClassB { }
struct StructA { }
interface IInterfaceA { }
enum EnumA { Value1, Value2 }
delegate void DelegateA();
";

    private const string CodeWithNestedClass = @"
class OuterClass
{
    class NestedClass { }
}
";

    [Fact]
    public void GetClasses_WithoutFilter_ReturnsAllClasses()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var classes = context.GetClasses().ToList();

        classes.Should().HaveCount(2);
        classes.Select(c => c.Identifier.Text).Should().Contain(new[] { "ClassA", "ClassB" });
    }

    [Fact]
    public void GetClasses_WithClassName_ReturnsMatchingClass()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var classes = context.GetClasses("ClassA").ToList();

        classes.Should().HaveCount(1);
        classes[0].Identifier.Text.Should().Be("ClassA");
    }

    [Fact]
    public void GetClasses_WithNonExistentName_ReturnsEmpty()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var classes = context.GetClasses("NonExistent").ToList();

        classes.Should().BeEmpty();
    }

    [Fact]
    public void GetClasses_WithIncludeNested_ReturnsNestedClasses()
    {
        var context = AnalysisContext.Create(CodeWithNestedClass);

        var classes = context.GetClasses(includeNested: true).ToList();

        classes.Should().HaveCount(2);
        classes.Select(c => c.Identifier.Text).Should().Contain(new[] { "OuterClass", "NestedClass" });
    }

    [Fact]
    public void GetClasses_WithoutIncludeNested_DoesNotReturnNestedClasses()
    {
        var context = AnalysisContext.Create(CodeWithNestedClass);

        var classes = context.GetClasses(includeNested: false).ToList();

        classes.Should().Contain(c => c.Identifier.Text == "OuterClass");
    }

    [Fact]
    public void GetStructs_WithoutFilter_ReturnsAllStructs()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var structs = context.GetStructs().ToList();

        structs.Should().HaveCount(1);
        structs[0].Identifier.Text.Should().Be("StructA");
    }

    [Fact]
    public void GetStructs_WithName_ReturnsMatchingStruct()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var structs = context.GetStructs("StructA").ToList();

        structs.Should().HaveCount(1);
    }

    [Fact]
    public void GetInterfaces_WithoutFilter_ReturnsAllInterfaces()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var interfaces = context.GetInterfaces().ToList();

        interfaces.Should().HaveCount(1);
        interfaces[0].Identifier.Text.Should().Be("IInterfaceA");
    }

    [Fact]
    public void GetEnums_WithoutFilter_ReturnsAllEnums()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var enums = context.GetEnums().ToList();

        enums.Should().HaveCount(1);
        enums[0].Identifier.Text.Should().Be("EnumA");
    }

    [Fact]
    public void GetDelegates_WithoutFilter_ReturnsAllDelegates()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var delegates = context.GetDelegates().ToList();

        delegates.Should().HaveCount(1);
        delegates[0].Identifier.Text.Should().Be("DelegateA");
    }

    [Fact]
    public void GetNamespaces_WithNoNamespace_ReturnsEmpty()
    {
        var context = AnalysisContext.Create(CodeWithMultipleTypes);

        var namespaces = context.GetNamespaces().ToList();

        namespaces.Should().BeEmpty();
    }

    [Fact]
    public void GetNamespaces_WithNamespace_ReturnsNamespaces()
    {
        const string code = @"
namespace MyNamespace
{
    class MyClass { }
}";
        var context = AnalysisContext.Create(code);

        var namespaces = context.GetNamespaces().ToList();

        namespaces.Should().HaveCount(1);
        namespaces[0].Name.ToString().Should().Be("MyNamespace");
    }
}
