using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public static class ClassNameResolver
    {
        public static string GetFullClassName(ClassDeclarationSyntax classDecl)
        {
            if (classDecl == null) return string.Empty;
            var classNameParts = new List<string>();
            var current = classDecl;

            while (current != null)
            {
                classNameParts.Insert(0, current.Identifier.Text);
                if (current.TypeParameterList != null && current.TypeParameterList.Parameters.Any())
                    classNameParts[0] += $"`{current.TypeParameterList.Parameters.Count}";
                current = current.Parent as ClassDeclarationSyntax;
            }

            var namespaceDecl = classDecl.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (namespaceDecl != null)
            {
                var namespaceName = namespaceDecl.Name.ToString();
                if (!string.IsNullOrEmpty(namespaceName))
                    classNameParts.Insert(0, namespaceName);
            }

            return string.Join(".", classNameParts);
        }

        public static string GetSimpleClassName(string fullClassName)
        {
            if (string.IsNullOrEmpty(fullClassName)) return string.Empty;
            var lastDotIndex = fullClassName.LastIndexOf('.');
            if (lastDotIndex >= 0 && lastDotIndex < fullClassName.Length - 1)
            {
                var className = fullClassName.Substring(lastDotIndex + 1);
                var backtickIndex = className.IndexOf('`');
                return backtickIndex > 0 ? className.Substring(0, backtickIndex) : className;
            }
            return fullClassName;
        }

        public static ClassDeclarationSyntax FindClassByFullName(SyntaxNode root, string fullClassName)
        {
            if (string.IsNullOrEmpty(fullClassName) || root == null) return null;

            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDecl in classDeclarations)
                if (GetFullClassName(classDecl) == fullClassName)
                    return classDecl;

            var simpleClassName = GetSimpleClassName(fullClassName);
            return root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == simpleClassName);
        }

        public static Dictionary<string, ClassDeclarationSyntax> BuildClassCache(params SyntaxNode[] roots)
        {
            var cache = new Dictionary<string, ClassDeclarationSyntax>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
                foreach (var classDecl in classDeclarations)
                {
                    var fullClassName = GetFullClassName(classDecl);
                    if (!cache.ContainsKey(fullClassName))
                        cache[fullClassName] = classDecl;
                }
            }
            return cache;
        }
    }

    public static class MethodSignatureBuilder
    {
        public static string GetMethodSignature(MethodDeclarationSyntax method, string fullClassName = null)
        {
            if (method == null) return string.Empty;
            var signature = new StringBuilder();

            if (!string.IsNullOrEmpty(fullClassName))
                signature.Append(fullClassName).Append("::");

            signature.Append(method.Identifier.Text)
                     .Append('_')
                     .Append(method.ParameterList.Parameters.Count)
                     .Append('_');

            foreach (var param in method.ParameterList.Parameters)
                signature.Append(param.Type?.ToString() ?? "unknown").Append('|');

            signature.Append('_').Append(method.ReturnType.ToString());

            if (method.TypeParameterList != null && method.TypeParameterList.Parameters.Any())
            {
                signature.Append('_');
                foreach (var typeParam in method.TypeParameterList.Parameters)
                    signature.Append(typeParam.Identifier.Text).Append('|');
            }

            return signature.ToString();
        }

        public static string GetMethodSignature(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null) return string.Empty;
            var signature = new StringBuilder();

            if (methodSymbol.ContainingType != null)
                signature.Append(methodSymbol.ContainingType.ToString()).Append("::");

            signature.Append(methodSymbol.Name)
                     .Append('_')
                     .Append(methodSymbol.Parameters.Length)
                     .Append('_');

            foreach (var param in methodSymbol.Parameters)
                signature.Append(param.Type?.ToString() ?? "unknown").Append('|');

            signature.Append('_').Append(methodSymbol.ReturnType?.ToString() ?? "void");

            if (methodSymbol.TypeParameters.Length > 0)
            {
                signature.Append('_');
                foreach (var typeParam in methodSymbol.TypeParameters)
                    signature.Append(typeParam.Name).Append('|');
            }

            return signature.ToString();
        }
    }
}

