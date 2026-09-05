using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Removal
{
    public static class CodeRemovalService
    {
        #region 基础移除方法

        public static SyntaxNode RemoveNode(SyntaxNode root, SyntaxNode nodeToRemove)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (nodeToRemove == null) throw new ArgumentNullException(nameof(nodeToRemove));
            return root.RemoveNode(nodeToRemove, SyntaxRemoveOptions.KeepNoTrivia);
        }

        public static SyntaxNode RemoveNodes(SyntaxNode root, IEnumerable<SyntaxNode> nodesToRemove)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (nodesToRemove == null) throw new ArgumentNullException(nameof(nodesToRemove));
            return root.RemoveNodes(nodesToRemove, SyntaxRemoveOptions.KeepNoTrivia);
        }

        #endregion

        #region 基于 AnalysisContext 的便捷移除（返回新代码）

        public static string RemoveNode(AnalysisContext context, SyntaxNode nodeToRemove)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (nodeToRemove == null) throw new ArgumentNullException(nameof(nodeToRemove));

            var newRoot = RemoveNode(context.Root, nodeToRemove);
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        private static string RemoveByName<T>(
            AnalysisContext context,
            string name,
            string containerName,
            Func<T, string> nameSelector,
            Func<T, string> containerSelector) where T : SyntaxNode
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("名称不能为空", nameof(name));

            var nodes = QuerySkeleton.FindAllNodes<T>(context.Root);

            if (!string.IsNullOrWhiteSpace(containerName))
                nodes = nodes.Where(n => containerSelector(n) == containerName);

            nodes = nodes.Where(n => nameSelector(n) == name);

            var nodesToRemove = nodes.ToList();
            if (!nodesToRemove.Any()) return context.GetFormattedCode();

            var newRoot = RemoveNodes(context.Root, nodesToRemove);
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        public static string RemoveClass(AnalysisContext context, string className, bool includeNested = false)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(className)) throw new ArgumentException("类名不能为空", nameof(className));

            var classes = context.GetClasses(className, includeNested).ToList();
            if (!classes.Any()) return context.GetFormattedCode();

            var newRoot = RemoveNodes(context.Root, classes);
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        public static string RemoveMethod(AnalysisContext context, string methodName, string className = null)
            => RemoveByName<MethodDeclarationSyntax>(context, methodName, className,
                m => m.Identifier.Text,
                m => m.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "");

        public static string RemoveProperty(AnalysisContext context, string propertyName, string className = null)
            => RemoveByName<PropertyDeclarationSyntax>(context, propertyName, className,
                p => p.Identifier.Text,
                p => p.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "");

        public static string RemoveField(AnalysisContext context, string fieldName, string className = null)
            => RemoveByName<FieldDeclarationSyntax>(context, fieldName, className,
                f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "",
                f => f.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "");

        public static string RemoveInterface(AnalysisContext context, string interfaceName)
            => RemoveByName<InterfaceDeclarationSyntax>(context, interfaceName, null,
                i => i.Identifier.Text,
                _ => "");

        public static string RemoveStruct(AnalysisContext context, string structName)
            => RemoveByName<StructDeclarationSyntax>(context, structName, null,
                s => s.Identifier.Text,
                _ => "");

        public static string RemoveEnum(AnalysisContext context, string enumName)
            => RemoveByName<EnumDeclarationSyntax>(context, enumName, null,
                e => e.Identifier.Text,
                _ => "");

        public static string RemoveDelegate(AnalysisContext context, string delegateName)
            => RemoveByName<DelegateDeclarationSyntax>(context, delegateName, null,
                d => d.Identifier.Text,
                _ => "");

        #endregion

        #region 高级移除（基于谓词）

        public static string RemoveWhere(AnalysisContext context, Func<SyntaxNode, bool> predicate)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            var nodesToRemove = context.Root.DescendantNodes().Where(predicate).ToList();
            if (!nodesToRemove.Any()) return context.GetFormattedCode();

            var newRoot = RemoveNodes(context.Root, nodesToRemove);
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        #endregion

        #region 尝试模式（避免异常）

        public static bool TryRemoveNode(AnalysisContext context, SyntaxNode nodeToRemove, out string newCode)
        {
            newCode = null;
            try
            {
                newCode = RemoveNode(context, nodeToRemove);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}


