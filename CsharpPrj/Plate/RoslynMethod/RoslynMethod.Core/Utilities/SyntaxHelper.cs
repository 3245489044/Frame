using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using EC = RoslynSpace.RoslynSyntaxUtilities.Base.EnhancedCodeElement;
using ET = RoslynSpace.RoslynSyntaxUtilities.Base.ElementType;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Core.Utilities
{
    public static class SyntaxHelper
    {
        #region 节点名称获取

        public static string GetNodeName(SyntaxNode node) => node switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            DestructorDeclarationSyntax d => d.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            EventDeclarationSyntax e => e.Identifier.Text,
            VariableDeclaratorSyntax v => v.Identifier.Text,
            FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "",
            ClassDeclarationSyntax c => c.Identifier.Text,
            StructDeclarationSyntax s => s.Identifier.Text,
            InterfaceDeclarationSyntax i => i.Identifier.Text,
            EnumDeclarationSyntax e => e.Identifier.Text,
            DelegateDeclarationSyntax d => d.Identifier.Text,
            ParameterSyntax p => p.Identifier.Text,
            LocalDeclarationStatementSyntax l => l.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "",
            TypeParameterSyntax t => t.Identifier.Text,
            NamespaceDeclarationSyntax ns => ns.Name.ToString(),
            _ => null
        };

        public static string GetNodeIdentifier(SyntaxNode node) => node switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            TypeDeclarationSyntax t => t.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            FieldDeclarationSyntax f => string.Join(",", f.Declaration.Variables.Select(v => v.Identifier.Text)),
            _ => null
        };

        public static string GetContainingTypeName(SyntaxNode node) =>
            node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Global";

        public static string ExtractIdentifier(SyntaxNode node) => node switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            _ => null
        };

        #endregion

        #region 位置定位

        public static int GetLineNumber(SyntaxNode node) =>
            node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        public static (int line, int column) GetPosition(SyntaxNode node)
        {
            var span = node.GetLocation().GetLineSpan();
            return (span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
        }

        public static (int line, int column, string filePath) GetLocationInfo(SyntaxNode node, string filePath = null)
        {
            if (node == null) return (0, 0, filePath);
            var (line, col) = GetPosition(node);
            return (line, col, filePath);
        }

        #endregion

        #region 修饰符检查

        public static bool IsStatic(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.StaticKeyword);
        public static bool IsReadOnly(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.ReadOnlyKeyword);
        public static bool IsConst(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.ConstKeyword);
        public static bool IsPartial(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.PartialKeyword);
        public static bool IsAbstract(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.AbstractKeyword);
        public static bool IsVirtual(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.VirtualKeyword);
        public static bool IsSealed(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.SealedKeyword);
        public static bool IsAsync(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.AsyncKeyword);

        #endregion

        #region 注释获取

        public static IEnumerable<string> GetComments(SyntaxNode node) =>
            node.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                            t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                .Select(t => t.ToString());

        #endregion

        #region 通用搜索方法

        public static T FindNode<T>(SyntaxNode root, Func<T, bool>? predicate = null) where T : SyntaxNode
        {
            var nodes = root.DescendantNodes().OfType<T>();
            return predicate != null ? nodes.FirstOrDefault(predicate) : nodes.FirstOrDefault();
        }

        public static List<T> FindAllNodes<T>(SyntaxNode root, Func<T, bool>? predicate = null) where T : SyntaxNode
        {
            var nodes = root.DescendantNodes().OfType<T>();
            return predicate != null ? nodes.Where(predicate).ToList() : nodes.ToList();
        }

        public static bool IsMatch(SyntaxNode node, EC element)
        {
            int line = GetLineNumber(node);
            return (node, element.Type) switch
            {
                (MethodDeclarationSyntax method, ET.Method) => method.Identifier.Text == element.Name && line == element.LineNumber,
                (ConstructorDeclarationSyntax constructor, ET.Constructor) => constructor.Identifier.Text == element.Name && line == element.LineNumber,
                (PropertyDeclarationSyntax property, ET.Property) => property.Identifier.Text == element.Name && line == element.LineNumber,
                (VariableDeclaratorSyntax variable, ET.Field) => variable.Identifier.Text == element.Name && line == element.LineNumber,
                _ => false
            };
        }

        #endregion

        #region 语法树解析

        public static CompilationUnitSyntax ParseSyntaxTree(string code)
        {
            return CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot();
        }

        public static (SyntaxTree syntaxTree, SemanticModel semanticModel) ParseCode(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("Temp")
                .AddReferences(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location))
                .AddSyntaxTrees(syntaxTree);

            return (syntaxTree, compilation.GetSemanticModel(syntaxTree));
        }

        #endregion

        #region 类相关操作

        public static ClassDeclarationSyntax? FindClass(CompilationUnitSyntax root, string className)
        {
            return FindNode<ClassDeclarationSyntax>(root, c => c.Identifier.Text == className);
        }

        public static List<ClassDeclarationSyntax> FindAllClasses(CompilationUnitSyntax root)
        {
            return root != null ? FindAllNodes<ClassDeclarationSyntax>(root) : new List<ClassDeclarationSyntax>();
        }

        public static List<string> GetClassNames(string code)
        {
            var root = ParseSyntaxTree(code);
            return FindAllClasses(root)
                .Select(c => c.Identifier.Text)
                .ToList();
        }

        #endregion

        #region 方法相关操作

        public static MethodDeclarationSyntax? FindMethod(ClassDeclarationSyntax targetClass, string methodName)
        {
            return FindNode<MethodDeclarationSyntax>(targetClass, m => m.Identifier.Text == methodName);
        }

        public static List<MethodDeclarationSyntax> FindAllMethods(ClassDeclarationSyntax targetClass)
        {
            return FindAllNodes<MethodDeclarationSyntax>(targetClass);
        }

        public static List<string> GetMethodNames(string code, string className, string? methodFilter = null)
        {
            var root = ParseSyntaxTree(code);
            var targetClass = FindClass(root, className);

            if (targetClass == null)
                return new List<string>();

            var methods = FindAllMethods(targetClass);

            return string.IsNullOrEmpty(methodFilter)
                ? methods.Select(m => m.Identifier.Text).ToList()
                : methods.Where(m => m.Identifier.Text.Equals(methodFilter, StringComparison.Ordinal))
                         .Select(m => m.Identifier.Text)
                         .ToList();
        }

        #endregion

        #region 变量相关操作

        public static List<VariableDeclaratorSyntax> FindAllVariables(MethodDeclarationSyntax method)
        {
            return FindAllNodes<VariableDeclaratorSyntax>(method);
        }

        public static List<string> GetVariableTypes(MethodDeclarationSyntax method)
        {
            return method.DescendantNodes()
                .OfType<VariableDeclarationSyntax>()
                .Select(v => v.Type.ToString())
                .Distinct()
                .ToList();
        }

        #endregion

        #region 表达式相关操作

        public static IEnumerable<InvocationExpressionSyntax> FindExpressions(
            MethodDeclarationSyntax method,
            string? expressionFilter = null)
        {
            var expressions = method.DescendantNodes().OfType<InvocationExpressionSyntax>();

            return string.IsNullOrEmpty(expressionFilter)
                ? expressions
                : expressions.Where(i => i.Expression.ToString().Contains(expressionFilter));
        }

        public static List<MemberDeclarationSyntax> ExtractClassMembers(SyntaxNode node)
        {
            var members = new List<MemberDeclarationSyntax>();
            CollectClassMembers(node, members);
            return members;
        }

        private static void CollectClassMembers(SyntaxNode node, List<MemberDeclarationSyntax> members)
        {
            foreach (var child in node.ChildNodes())
            {
                if (child is MemberDeclarationSyntax member)
                {
                    members.Add(member);
                }
                else if (child is BaseNamespaceDeclarationSyntax ns)
                {
                    CollectClassMembers(ns, members);
                }
            }
        }

        #endregion

        #region 综合解析方法

        public static (List<string> ClassNames, List<string> MethodNames, List<string> VariableTypes) AnalyzeCode(string code)
        {
            var root = ParseSyntaxTree(code);
            var classNames = new List<string>();
            var methodNames = new List<string>();
            var variableTypes = new List<string>();

            var classes = FindAllClasses(root);

            foreach (var classDecl in classes)
            {
                classNames.Add(classDecl.Identifier.Text);

                var methods = FindAllMethods(classDecl);
                methodNames.AddRange(methods.Select(m => m.Identifier.Text));

                foreach (var method in methods)
                {
                    variableTypes.AddRange(GetVariableTypes(method));
                }

                var fields = classDecl.Members.OfType<FieldDeclarationSyntax>();
                foreach (var field in fields)
                {
                    variableTypes.Add(field.Declaration.Type.ToString());
                }
            }

            return (classNames.Distinct().ToList(),
                    methodNames.Distinct().ToList(),
                    variableTypes.Distinct().ToList());
        }

        #endregion
    }
}
