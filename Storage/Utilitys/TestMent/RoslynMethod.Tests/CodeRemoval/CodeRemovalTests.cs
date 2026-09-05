using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Removal;

namespace RoslynMethod.Tests.CodeRemoval;

public class CodeRemovalTests
{
    private const string TestCode = @"
using System;
namespace TestNamespace
{
    public class TestClass
    {
        private int _field;

        public void MethodA() { Console.WriteLine(""A""); }
        public void MethodB() { Console.WriteLine(""B""); }

        public string Property1 { get; set; }
    }

    public interface ITestInterface { void DoSomething(); }
    public struct TestStruct { public int X; }
    public enum TestEnum { Value1, Value2 }
    public delegate void TestDelegate();
}";

    [Fact]
    public void RemoveNode_WithValidNode_ReturnsModifiedCode()
    {
        var context = CreateContext(TestCode);
        var method = context.GetMethods(null, "MethodA").FirstOrDefault();
        method.Should().NotBeNull();

        var result = CodeRemovalService.RemoveNode(context, method!);

        result.Should().NotContain("MethodA");
    }

    [Fact]
    public void RemoveNode_WithNullRoot_ThrowsArgumentNullException()
    {
        var action = () => CodeRemovalService.RemoveNode((SyntaxNode)null!, SyntaxFactory.EmptyStatement());

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveNode_WithNullNode_ThrowsArgumentNullException()
    {
        var context = CreateContext(TestCode);

        var action = () => CodeRemovalService.RemoveNode(context, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveClass_WithExistingClass_RemovesClass()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveClass(context, "TestClass");

        result.Should().NotContain("class TestClass");
    }

    [Fact]
    public void RemoveClass_WithNonExistentClass_ReturnsOriginalCode()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveClass(context, "NonExistentClass");

        result.Should().Contain("TestClass");
    }

    [Fact]
    public void RemoveClass_WithEmptyClassName_ThrowsArgumentException()
    {
        var context = CreateContext(TestCode);

        var action = () => CodeRemovalService.RemoveClass(context, "");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RemoveMethod_WithExistingMethod_RemovesMethod()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveMethod(context, "MethodA");

        result.Should().NotContain("MethodA");
    }

    [Fact]
    public void RemoveMethod_WithNonExistentMethod_ReturnsOriginalCode()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveMethod(context, "NonExistentMethod");

        result.Should().Contain("MethodA");
    }

    [Fact]
    public void RemoveMethod_WithClassName_RemovesOnlyFromSpecifiedClass()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveMethod(context, "MethodA", "TestClass");

        result.Should().NotContain("MethodA");
    }

    [Fact]
    public void RemoveProperty_WithExistingProperty_RemovesProperty()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveProperty(context, "Property1");

        result.Should().NotContain("Property1");
    }

    [Fact]
    public void RemoveField_WithExistingField_RemovesField()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveField(context, "_field");

        result.Should().NotContain("_field");
    }

    [Fact]
    public void RemoveInterface_WithExistingInterface_RemovesInterface()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveInterface(context, "ITestInterface");

        result.Should().NotContain("ITestInterface");
    }

    [Fact]
    public void RemoveStruct_WithExistingStruct_RemovesStruct()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveStruct(context, "TestStruct");

        result.Should().NotContain("TestStruct");
    }

    [Fact]
    public void RemoveEnum_WithExistingEnum_RemovesEnum()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveEnum(context, "TestEnum");

        result.Should().NotContain("TestEnum");
    }

    [Fact]
    public void RemoveDelegate_WithExistingDelegate_RemovesDelegate()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveDelegate(context, "TestDelegate");

        result.Should().NotContain("TestDelegate");
    }

    [Fact]
    public void RemoveWhere_WithPredicate_RemovesMatchingNodes()
    {
        var context = CreateContext(TestCode);

        var result = CodeRemovalService.RemoveWhere(context,
            node => node is MethodDeclarationSyntax method && method.Identifier.Text == "MethodA");

        result.Should().NotContain("MethodA");
        result.Should().Contain("MethodB");
    }

    [Fact]
    public void TryRemoveNode_WithValidNode_ReturnsTrue()
    {
        var context = CreateContext(TestCode);
        var method = context.GetMethods(null, "MethodA").FirstOrDefault()!;

        var success = CodeRemovalService.TryRemoveNode(context, method, out var newCode);

        success.Should().BeTrue();
        newCode.Should().NotContain("MethodA");
    }

    [Fact]
    public void TryRemoveNode_WithInvalidNode_ReturnsFalse()
    {
        var context = CreateContext(TestCode);

        var success = CodeRemovalService.TryRemoveNode(context, null!, out var newCode);

        success.Should().BeFalse();
        newCode.Should().BeNull();
    }

    private static AnalysisContext CreateContext(string code)
    {
        return AnalysisContext.Create(code);
    }
}
