using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;

namespace RoslynSpace.RoslynSyntaxUtilities.Core
{
    public class CodeExtractor
    {
        public static (string extractedCode, string remainingCode, string extractedFunctionName) ExtractFunctionWithClass(string sourceCode, string functionName)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                throw new ArgumentException("源代码不能为空", nameof(sourceCode));

            if (string.IsNullOrWhiteSpace(functionName))
                throw new ArgumentException("函数名不能为空", nameof(functionName));

            var syntaxTree = CSharpSourceProvider.Instance.Parse(sourceCode);
            var root = syntaxTree.GetRoot();

            var targetMethod = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.ValueText == functionName);

            if (targetMethod == null)
                throw new ArgumentException($"函数 '{functionName}' 未在代码中找到");

            var parentClass = targetMethod.Ancestors()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault();

            if (parentClass == null)
                throw new InvalidOperationException($"函数 '{functionName}' 不在任何类中");

            var extractedClass = CreateClassWithSingleMethod(parentClass, targetMethod);

            var remainingClass = RemoveMethodFromClass(parentClass, targetMethod);
            var remainingRoot = root.ReplaceNode(parentClass, remainingClass);

            string extractedFunctionName = targetMethod.Identifier.ValueText;

            return (
                extractedClass.NormalizeWhitespace().ToFullString(),
                remainingRoot.NormalizeWhitespace().ToFullString(),
                extractedFunctionName
            );
        }

        private static ClassDeclarationSyntax CreateClassWithSingleMethod(
            ClassDeclarationSyntax originalClass,
            MethodDeclarationSyntax targetMethod)
        {
            return originalClass
                .WithMembers(SyntaxFactory.List(new MemberDeclarationSyntax[] { targetMethod }))
                .WithoutTrivia();
        }

        private static ClassDeclarationSyntax? RemoveMethodFromClass(
            ClassDeclarationSyntax originalClass,
            MethodDeclarationSyntax methodToRemove)
        {
            var remainingMembers = originalClass.Members
                .Where(member => !member.Equals(methodToRemove))
                .ToList();

            if (remainingMembers.Any())
            {
                return originalClass.WithMembers(SyntaxFactory.List(remainingMembers));
            }

            return null;
        }
    }
}
