using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    [Obsolete("请使用 SyntaxHelper (RoslynSyntaxUtilities.Core.Utilities) 替代。SyntaxHelper 合并了 SyntaxAccessor、SyntaxLocator、SyntaxMatcher、SyntaxParser 的功能。")]
    internal static class SyntaxAccessor
    {
        public static string GetAccessibilityString(SyntaxTokenList modifiers)
        {
            if (modifiers.Any(SyntaxKind.PublicKeyword)) return "public";
            if (modifiers.Any(SyntaxKind.PrivateKeyword)) return "private";
            if (modifiers.Any(SyntaxKind.ProtectedKeyword))
                return modifiers.Any(SyntaxKind.InternalKeyword) ? "protected internal" : "protected";
            if (modifiers.Any(SyntaxKind.InternalKeyword)) return "internal";
            if (modifiers.Any(SyntaxKind.FileKeyword)) return "file";
            return "private";
        }

        public static Microsoft.CodeAnalysis.Accessibility GetAccessibilityEnum(SyntaxTokenList modifiers)
        {
            if (modifiers.Any(SyntaxKind.PublicKeyword)) return Microsoft.CodeAnalysis.Accessibility.Public;
            if (modifiers.Any(SyntaxKind.PrivateKeyword)) return Microsoft.CodeAnalysis.Accessibility.Private;
            if (modifiers.Any(SyntaxKind.ProtectedKeyword))
                return modifiers.Any(SyntaxKind.InternalKeyword) ? Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal : Microsoft.CodeAnalysis.Accessibility.Protected;
            if (modifiers.Any(SyntaxKind.InternalKeyword)) return Microsoft.CodeAnalysis.Accessibility.Internal;
            if (modifiers.Any(SyntaxKind.FileKeyword)) return Microsoft.CodeAnalysis.Accessibility.Private;
            return Microsoft.CodeAnalysis.Accessibility.Private;
        }

        public static string GetNodeName(SyntaxNode node) => node switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.Text,
            DestructorDeclarationSyntax destructor => destructor.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            EventDeclarationSyntax @event => @event.Identifier.Text,
            VariableDeclaratorSyntax variable => variable.Identifier.Text,
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "",
            ClassDeclarationSyntax classDecl => classDecl.Identifier.Text,
            StructDeclarationSyntax structDecl => structDecl.Identifier.Text,
            InterfaceDeclarationSyntax interfaceDecl => interfaceDecl.Identifier.Text,
            EnumDeclarationSyntax enumDecl => enumDecl.Identifier.Text,
            DelegateDeclarationSyntax delegateDecl => delegateDecl.Identifier.Text,
            ParameterSyntax parameter => parameter.Identifier.Text,
            LocalDeclarationStatementSyntax local => local.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "",
            TypeParameterSyntax typeParam => typeParam.Identifier.Text,
            NamespaceDeclarationSyntax ns => ns.Name.ToString(),
            _ => node.ToString()
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

        public static IEnumerable<string> GetComments(SyntaxNode node)
            => node.GetLeadingTrivia()
                .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                           t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                .Select(t => t.ToString());

        public static string ExtractIdentifier(SyntaxNode node) => node switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            _ => null
        };

        public static bool IsStatic(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.StaticKeyword);
        public static bool IsReadOnly(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.ReadOnlyKeyword);
        public static bool IsConst(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.ConstKeyword);
        public static bool IsPartial(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.PartialKeyword);
        public static bool IsAbstract(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.AbstractKeyword);
        public static bool IsVirtual(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.VirtualKeyword);
        public static bool IsSealed(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.SealedKeyword);
        public static bool IsAsync(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.AsyncKeyword);
    }

    [Obsolete("请使用 SyntaxHelper (RoslynSyntaxUtilities.Core.Utilities) 替代。")]
    internal static class SyntaxLocator
    {
        public static int GetLineNumber(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        public static (int line, int column) GetPosition(SyntaxNode node)
        {
            var span = node.GetLocation().GetLineSpan();
            return (span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
        }

        public static LocationInfo GetLocationInfo(SyntaxNode node, string filePath = null) =>
            node == null ? null : new LocationInfo(node, filePath);
    }

    [Obsolete("请使用 SyntaxHelper (RoslynSyntaxUtilities.Core.Utilities) 替代。")]
    internal static class SyntaxMatcher
    {
        public static bool IsMatch(SyntaxNode node, EnhancedCodeElement element)
        {
            int line = SyntaxLocator.GetLineNumber(node);
            return (node, element.Type) switch
            {
                (MethodDeclarationSyntax method, ElementType.Method) => method.Identifier.Text == element.Name && line == element.LineNumber,
                (ConstructorDeclarationSyntax constructor, ElementType.Constructor) => constructor.Identifier.Text == element.Name && line == element.LineNumber,
                (PropertyDeclarationSyntax property, ElementType.Property) => property.Identifier.Text == element.Name && line == element.LineNumber,
                (VariableDeclaratorSyntax variable, ElementType.Field) => variable.Identifier.Text == element.Name && line == element.LineNumber,
                _ => false
            };
        }
    }

    [Obsolete("请使用 SyntaxHelper (RoslynSyntaxUtilities.Core.Utilities) 替代。")]
    internal static class SyntaxParser
    {
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
    }

    [Obsolete("请使用 SyntaxHelper (RoslynSyntaxUtilities.Core.Utilities) 替代。")]
    internal static class CodeCompilation
    {
        internal static class CompilationFactory
        {
            public static Compilation Create(SyntaxTree syntaxTree)
            {
                return CSharpCompilation.Create("Analysis")
                    .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                    .AddSyntaxTrees(syntaxTree);
            }
        }
    }
}
