using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    internal static class SymbolAnalyzer
    {
        public static ISymbol GetSymbol(SemanticModel semanticModel, SyntaxNode node)
            => semanticModel.GetDeclaredSymbol(node) ?? semanticModel.GetSymbolInfo(node).Symbol ?? semanticModel.GetSymbolInfo(node).CandidateSymbols.FirstOrDefault();

        public static IMethodSymbol GetMethodSymbol(SemanticModel semanticModel, MethodDeclarationSyntax method) => semanticModel.GetDeclaredSymbol(method);
        public static IFieldSymbol GetFieldSymbol(SemanticModel semanticModel, VariableDeclaratorSyntax variable) => semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;

        public static string GetMethodSignature(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null) return string.Empty;
            var parameters = string.Join(", ", methodSymbol.Parameters.Select(p => $"{p.Type} {p.Name}"));
            return $"{methodSymbol.ReturnType} {methodSymbol.Name}({parameters})";
        }
        public static IPropertySymbol GetPropertySymbol(SemanticModel semanticModel, PropertyDeclarationSyntax property) => semanticModel.GetDeclaredSymbol(property);

        public static ITypeSymbol GetTypeSymbol(SemanticModel semanticModel, SyntaxNode typeNode) => typeNode switch
        {
            TypeSyntax typeSyntax => semanticModel.GetTypeInfo(typeSyntax).Type,
            TypeDeclarationSyntax typeDeclaration => semanticModel.GetDeclaredSymbol(typeDeclaration),
            _ => null
        };
        public static ITypeSymbol GetTypeSymbol(SyntaxNode n, SemanticModel semanticModel)
        {
            var typeInfo = semanticModel.GetTypeInfo(n);
            if (typeInfo.Type != null) return typeInfo.Type;

            return n switch
            {
                IdentifierNameSyntax ins => GetSymbolType(semanticModel.GetSymbolInfo(ins).Symbol),
                GenericNameSyntax genericName => semanticModel.GetSymbolInfo(genericName).Symbol as INamedTypeSymbol,
                PredefinedTypeSyntax _ => null,
                AliasQualifiedNameSyntax aliasQualifiedName => GetTypeSymbol(aliasQualifiedName.Name, semanticModel),
                TupleTypeSyntax _ => null,
                FunctionPointerTypeSyntax _ => null,
                NameMemberCrefSyntax cref => GetTypeSymbol(cref.Name, semanticModel),
                SimpleNameSyntax simpleName => semanticModel.GetSymbolInfo(simpleName).Symbol as ITypeSymbol ??
                    GetSymbolType(semanticModel.GetSymbolInfo(simpleName).Symbol),
                _ => null
            };
        }
        private const int MaxRecursionDepth = 10; private const int MaxTypeExtractionCount = 1000;

        public static IEnumerable<string> ExtractTypeInfoInternal(SyntaxNode node, SemanticModel semanticModel, int depth)
        {
            if (depth > MaxRecursionDepth) yield break;

            var nodes = node.DescendantNodes().Take(MaxTypeExtractionCount);
            foreach (var n in nodes)
            {
                if (n is ArrayTypeSyntax arrayType)
                {
                    foreach (var type in ExtractTypeInfoInternal(arrayType.ElementType, semanticModel, depth + 1))
                        yield return type;
                }
                else if (n is NullableTypeSyntax nullableType)
                {
                    foreach (var type in ExtractTypeInfoInternal(nullableType.ElementType, semanticModel, depth + 1))
                        yield return type;
                }
                else if (n is PointerTypeSyntax pointerType)
                {
                    foreach (var type in ExtractTypeInfoInternal(pointerType.ElementType, semanticModel, depth + 1))
                        yield return type;
                }
                else if (n is QualifiedNameSyntax qualifiedName)
                {
                    foreach (var type in ExtractTypeInfoInternal(qualifiedName.Left, semanticModel, depth + 1))
                        yield return type;
                    foreach (var type in ExtractTypeInfoInternal(qualifiedName.Right, semanticModel, depth + 1))
                        yield return type;
                }
                else
                {
                    var typeSymbol = GetTypeSymbol(n, semanticModel);
                    if (typeSymbol?.ToDisplayString() is string type)
                        yield return type;
                }
            }
        }

        private static ITypeSymbol GetSymbolType(ISymbol symbol) => symbol switch
        {
            ITypeSymbol typeSymbol => typeSymbol,
            ILocalSymbol local => local.Type,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            IMethodSymbol method => method.ReturnType,
            _ => null
        };

        public static ITypeSymbol GetExpressionType(SemanticModel semanticModel, ExpressionSyntax expression) => semanticModel.GetTypeInfo(expression).Type;

        public static IEnumerable<InvocationExpressionSyntax> FindMethodInvocations(SyntaxNode root, SemanticModel semanticModel, IMethodSymbol methodSymbol)
            => root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(invocation => SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(invocation).Symbol, methodSymbol) ||
                                   semanticModel.GetSymbolInfo(invocation).CandidateSymbols.Any(s => SymbolEqualityComparer.Default.Equals(s, methodSymbol)));

        public static IEnumerable<SyntaxNode> FindTypeReferences(SyntaxNode root, SemanticModel semanticModel, ITypeSymbol typeSymbol)
            => root.DescendantNodes()
                .Where(node => SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(node).Symbol?.ContainingType, typeSymbol) ||
                              SymbolEqualityComparer.Default.Equals(semanticModel.GetTypeInfo(node).Type, typeSymbol) ||
                              SymbolEqualityComparer.Default.Equals(semanticModel.GetTypeInfo(node).ConvertedType, typeSymbol));

        public static string GetXmlDocumentation(SemanticModel semanticModel, SyntaxNode node) => GetSymbol(semanticModel, node)?.GetDocumentationCommentXml();
        public static string GetXmlDocumentation(SyntaxNode node) =>
    node.GetLeadingTrivia()
        .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
        .ToFullString();

        public static string GetFullTypeName(SemanticModel semanticModel, TypeDeclarationSyntax typeDecl)
        {
            var namespaceSymbol = typeDecl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            var namespaceName = namespaceSymbol != null ? semanticModel.GetDeclaredSymbol(namespaceSymbol)?.ToDisplayString() ?? "" : "";
            return string.IsNullOrEmpty(namespaceName) ? typeDecl.Identifier.Text : $"{namespaceName}.{typeDecl.Identifier.Text}";
        }
    }

    internal static class PurityAnalyzer
    {
        public static bool AnalyzeMethodPurity(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                return false;

            if (method.Body == null) return true;

            var assignments = method.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>();
            foreach (var assignment in assignments)
            {
                if (assignment.Left is MemberAccessExpressionSyntax memberAccess)
                {
                    var symbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
                    if (symbol is IFieldSymbol or IPropertySymbol)
                        return false;
                }
            }

            var invocations = method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (symbol != null && HasSideEffect(symbol))
                    return false;
            }

            return true;
        }

        public static bool AnalyzeSideEffects(MethodDeclarationSyntax method)
        {
            if (method.Body == null) return false;

            var hasAssignment = method.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any();
            var hasInvocations = method.Body.DescendantNodes().OfType<InvocationExpressionSyntax>().Any();
            var hasObjectCreation = method.Body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Any();

            return hasAssignment || hasInvocations || hasObjectCreation;
        }

        private static bool HasSideEffect(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null) return false;
            var name = methodSymbol.Name.ToLower();
            return name.StartsWith("set") ||
                   name.StartsWith("add") ||
                   name.StartsWith("remove") ||
                   name.StartsWith("clear") ||
                   name.StartsWith("write");
        }
    }
}
