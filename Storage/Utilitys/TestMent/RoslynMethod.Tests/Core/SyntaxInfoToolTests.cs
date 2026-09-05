using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynMethod.Tests.Core;

public class SyntaxInfoToolTests
{
    private const string Code = @"
class TestClass
{
    public int Add(int a, int b) { return a + b; }
    public void DoNothing() { }
    public string Name { get; set; }
}
";

    [Fact]
    public void GetAccessibilityString_WithPublicModifier_ReturnsPublic()
    {
        var context = AnalysisContext.Create(Code);
        var method = context.GetMethods("TestClass", "Add").First();

        var accessibility = SyntaxInfoTool.GetAccessibilityString(method.Modifiers);

        accessibility.Should().Be("public");
    }

    [Fact]
    public void GetMethodParameters_ReturnsCorrectParameters()
    {
        var context = AnalysisContext.Create(Code);
        var method = context.GetMethods("TestClass", "Add").First();

        var parameters = SyntaxInfoTool.GetMethodParameters(method).ToList();

        parameters.Should().HaveCount(2);
        parameters[0].Identifier.Text.Should().Be("a");
        parameters[1].Identifier.Text.Should().Be("b");
    }

    [Fact]
    public void GetMethodReturnType_ReturnsCorrectType()
    {
        var context = AnalysisContext.Create(Code);
        var method = context.GetMethods("TestClass", "Add").First();

        var returnType = SyntaxInfoTool.GetMethodReturnType(method);

        returnType.ToString().Should().Be("int");
    }

    [Fact]
    public void GetMethodReturnType_ForVoidMethod_ReturnsVoid()
    {
        var context = AnalysisContext.Create(Code);
        var method = context.GetMethods("TestClass", "DoNothing").First();

        var returnType = SyntaxInfoTool.GetMethodReturnType(method);

        returnType.ToString().Should().Be("void");
    }

    [Fact]
    public void IsVoidReturnType_WithVoidType_ReturnsTrue()
    {
        var voidType = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword));

        var result = SyntaxInfoTool.IsVoidReturnType(voidType);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsVoidReturnType_WithNonVoidType_ReturnsFalse()
    {
        var intType = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword));

        var result = SyntaxInfoTool.IsVoidReturnType(intType);

        result.Should().BeFalse();
    }

    [Fact]
    public void GetPosition_ReturnsValidPosition()
    {
        var context = AnalysisContext.Create(Code);
        var method = context.GetMethods("TestClass", "Add").First();

        var (line, column) = SyntaxInfoTool.GetPosition(method);

        line.Should().BeGreaterThan(0);
        column.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetFullTypeName_ReturnsCorrectName()
    {
        const string nsCode = @"
namespace MyNamespace
{
    class MyClass { }
}";
        var context = AnalysisContext.Create(nsCode);
        var classDecl = context.GetClasses("MyClass").First();

        var fullName = SyntaxInfoTool.GetFullTypeName(classDecl);

        fullName.Should().Be("MyNamespace.MyClass");
    }

    [Fact]
    public void GetFullTypeName_WithoutNamespace_ReturnsClassName()
    {
        var context = AnalysisContext.Create(Code);
        var classDecl = context.GetClasses("TestClass").First();

        var fullName = SyntaxInfoTool.GetFullTypeName(classDecl);

        fullName.Should().Be("TestClass");
    }

    [Fact]
    public void HasReturnStatement_WithReturn_ReturnsTrue()
    {
        var context = AnalysisContext.Create(Code);
        var method = context.GetMethods("TestClass", "Add").First();

        var result = SyntaxInfoTool.HasReturnStatement(method.Body!);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasReturnStatement_WithoutReturn_ReturnsFalse()
    {
        var context = AnalysisContext.Create(Code);
        var method = context.GetMethods("TestClass", "DoNothing").First();

        var result = SyntaxInfoTool.HasReturnStatement(method.Body!);

        result.Should().BeFalse();
    }
}
