using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Refactoring.Services
{
    public static class CodeInsertionService
    {
        public static string InsertIntoNamespaceOrClass(string targetCode, string codeToInsert)
        {
            if (string.IsNullOrWhiteSpace(targetCode))
                throw new ArgumentException("目标代码不能为空", nameof(targetCode));
            if (string.IsNullOrWhiteSpace(codeToInsert))
                throw new ArgumentException("要插入的代码不能为空", nameof(codeToInsert));

            var targetRoot = CSharpSyntaxTree.ParseText(targetCode).GetCompilationUnitRoot();
            var insertRoot = CSharpSyntaxTree.ParseText(codeToInsert).GetCompilationUnitRoot();

            var targetHasClass = targetRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Any();
            var insertHasClass = insertRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Any();

            if (!targetHasClass && insertHasClass)
            {
                return PerformInsertion(insertRoot, targetCode);
            }
            else
            {
                return PerformInsertion(targetRoot, codeToInsert);
            }
        }

        private static string PerformInsertion(CompilationUnitSyntax targetRoot, string sourceCode)
        {
            var firstClass = targetRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (firstClass == null)
                throw new InvalidOperationException("目标代码中没有找到任何类定义。");

            var insertTree = CSharpSyntaxTree.ParseText(sourceCode);
            var insertRoot = insertTree.GetCompilationUnitRoot();

            var membersToInsert = ExtractClassMembers(insertRoot).ToList();
            if (!membersToInsert.Any())
                throw new InvalidOperationException("要插入的代码中没有找到可作为类成员的有效声明。");

            var newClass = firstClass.AddMembers(membersToInsert.ToArray());
            var rootWithNewClass = targetRoot.ReplaceNode(firstClass, newClass);

            var insertUsingStrings = insertRoot.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.ToString())
                .Distinct()
                .ToList();

            if (insertUsingStrings.Any())
            {
                var targetUsingStrings = targetRoot.DescendantNodes()
                    .OfType<UsingDirectiveSyntax>()
                    .Select(u => u.ToString())
                    .ToHashSet();

                var newUsingStrings = insertUsingStrings
                    .Where(u => !targetUsingStrings.Contains(u))
                    .ToList();

                if (newUsingStrings.Any())
                {
                    var newUsingDirectives = newUsingStrings
                        .Select(u => SyntaxFactory.ParseCompilationUnit(u).Usings[0])
                        .ToArray();
                    rootWithNewClass = rootWithNewClass.AddUsings(newUsingDirectives);
                }
            }

            return rootWithNewClass.NormalizeWhitespace().ToFullString();
        }

        private static IEnumerable<MemberDeclarationSyntax> ExtractClassMembers(SyntaxNode node)
        {
            foreach (var child in node.ChildNodes())
            {
                if (child is MemberDeclarationSyntax member)
                {
                    yield return member;
                }
                else if (child is BaseNamespaceDeclarationSyntax ns)
                {
                    foreach (var inner in ExtractClassMembers(ns))
                        yield return inner;
                }
            }
        }
    }
}
