using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public static class QuerySkeleton
    {
        private const int MaxNodeSize = 10000;

        public static IEnumerable<T> FindTopLevelDeclarations<T>(
            SyntaxNode root,
            string name = null) where T : SyntaxNode
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var nodes = root.ChildNodes()
                .OfType<T>()
                .Take(MaxNodeSize);

            return FilterByName(nodes, name);
        }

        public static IEnumerable<T> FindMembersInClass<T>(
            SyntaxNode root,
            string className,
            string memberName = null) where T : SyntaxNode
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            if (string.IsNullOrEmpty(className))
                return FilterByName(root.DescendantNodes().OfType<T>().Take(MaxNodeSize), memberName);

            var classNode = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == className);

            if (classNode == null)
                return Enumerable.Empty<T>();

            var members = classNode.ChildNodes()
                .OfType<T>()
                .Take(MaxNodeSize);

            return FilterByName(members, memberName);
        }

        public static IEnumerable<T> Query<T>(
            SyntaxNode root,
            QueryCriteria criteria = null) where T : SyntaxNode
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            if (criteria == null)
                return FindTopLevelDeclarations<T>(root);

            if (IsTypeDeclaration<T>())
            {
                var nodes = FindTopLevelDeclarations<T>(root, criteria.Name);
                if (criteria.IncludeNested && typeof(T) == typeof(ClassDeclarationSyntax))
                {
                    var nestedClasses = root.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .Where(c => c.Parent is ClassDeclarationSyntax)
                        .Take(MaxNodeSize);
                    nodes = nodes.Concat((IEnumerable<T>)nestedClasses);
                    if (!string.IsNullOrEmpty(criteria.Name))
                        nodes = nodes.Where(n => GetNodeName(n) == criteria.Name);
                }
                return nodes;
            }

            return FindMembersInClass<T>(root, criteria.ContainerName, criteria.Name);
        }

        public static IEnumerable<T> FindAllNodes<T>(
            SyntaxNode root,
            string name = null) where T : SyntaxNode
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var nodes = root.DescendantNodes()
                .OfType<T>()
                .Take(MaxNodeSize);

            return FilterByName(nodes, name);
        }

        public static string GetNodeName<T>(T node) where T : SyntaxNode => node switch
        {
            ClassDeclarationSyntax c => c.Identifier.Text,
            StructDeclarationSyntax s => s.Identifier.Text,
            InterfaceDeclarationSyntax i => i.Identifier.Text,
            EnumDeclarationSyntax e => e.Identifier.Text,
            DelegateDeclarationSyntax d => d.Identifier.Text,
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            DestructorDeclarationSyntax d => d.Identifier.Text,
            EventDeclarationSyntax e => e.Identifier.Text,
            FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "",
            _ => throw new NotSupportedException($"不支持的节点类型: {typeof(T).Name}")
        };

        private static IEnumerable<T> FilterByName<T>(IEnumerable<T> items, string name) where T : SyntaxNode
        {
            if (string.IsNullOrEmpty(name))
                return items;
            return items.Where(item => GetNodeName(item) == name);
        }

        private static bool IsTypeDeclaration<T>() where T : SyntaxNode
        {
            var type = typeof(T);
            return type == typeof(ClassDeclarationSyntax) ||
                   type == typeof(StructDeclarationSyntax) ||
                   type == typeof(InterfaceDeclarationSyntax) ||
                   type == typeof(EnumDeclarationSyntax) ||
                   type == typeof(DelegateDeclarationSyntax);
        }
    }

    public record QueryCriteria
    {
        public string Name { get; init; }

        public string ContainerName { get; init; }

        public bool IncludeNested { get; init; }
    }
}
