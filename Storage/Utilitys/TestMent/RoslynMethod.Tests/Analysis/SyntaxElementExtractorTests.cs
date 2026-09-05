using RoslynSpace;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tests.Analysis;

public class SyntaxElementExtractorTests
{
    private const string TestCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        private int _field;

        public TestClass()
        {
            _field = 42;
        }

        public string GetName(int id)
        {
            var result = ""Name_"" + id.ToString();
            return result;
        }

        public int Value { get; set; }
    }
}";

    [Fact]
    public void Extract_WithValidCode_ReturnsElements()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);

        elements.Should().NotBeEmpty();
    }

    [Fact]
    public void Extract_WithValidCode_ContainsMethods()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);

        elements.Should().Contain(e => e.Type == ElementType.Method);
    }

    [Fact]
    public void Extract_WithValidCode_ContainsProperties()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);

        elements.Should().Contain(e => e.Type == ElementType.Property);
    }

    [Fact]
    public void Extract_WithValidCode_ContainsFields()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);

        elements.Should().Contain(e => e.Type == ElementType.Field);
    }

    [Fact]
    public void Extract_WithValidCode_ContainsConstructors()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);

        elements.Should().Contain(e => e.Type == ElementType.Constructor);
    }

    [Fact]
    public void Extract_WithEmptyCode_ReturnsEmptyList()
    {
        var elements = SyntaxElementExtractor.Extract("");

        elements.Should().BeEmpty();
    }

    [Fact]
    public void Extract_WithInvalidCode_ReturnsEmptyList()
    {
        var elements = SyntaxElementExtractor.Extract("invalid code {{{");

        elements.Should().BeEmpty();
    }

    [Fact]
    public void Extract_WithMultipleClasses_ExtractsAll()
    {
        var code = @"
public class ClassA { public void MethodA() { } }
public class ClassB { public void MethodB() { } }
";

        var elements = SyntaxElementExtractor.Extract(code);

        elements.Should().Contain(e => e.Name == "MethodA");
        elements.Should().Contain(e => e.Name == "MethodB");
    }

    [Fact]
    public void Extract_ElementHasCorrectProperties()
    {
        var elements = SyntaxElementExtractor.Extract(TestCode);

        var method = elements.FirstOrDefault(e => e.Name == "GetName");
        method.Should().NotBeNull();
        method!.Type.Should().Be(ElementType.Method);
        method.ContainingType.Should().Be("TestClass");
    }

    [Fact]
    public void Extract_WithStaticMethod_DetectsStatic()
    {
        var code = @"
public class Test
{
    public static void StaticMethod() { }
    public void InstanceMethod() { }
}
";

        var elements = SyntaxElementExtractor.Extract(code);

        var staticMethod = elements.FirstOrDefault(e => e.Name == "StaticMethod");
        staticMethod.Should().NotBeNull();
        staticMethod!.IsStatic.Should().BeTrue();

        var instanceMethod = elements.FirstOrDefault(e => e.Name == "InstanceMethod");
        instanceMethod.Should().NotBeNull();
        instanceMethod!.IsStatic.Should().BeFalse();
    }
}
