using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Core.Utilities
{
    public static class UsingManager
    {
        private static readonly SymbolDisplayFormat FullyQualifiedNoAliasFormat =
            SymbolDisplayFormat.FullyQualifiedFormat
                .WithMiscellaneousOptions(
                    SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                    & ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        #region 提取

        public static List<string> ExtractUsings(string sourceCode)
        {
            return (CSharpSyntaxTree.ParseText(sourceCode).GetRoot() as CompilationUnitSyntax)
                ?.Usings.Select(u => u.ToString()).ToList() ?? new List<string>();
        }

        public static List<string> ExtractUsings(SyntaxNode root)
        {
            return (root as CompilationUnitSyntax)
                ?.Usings.Select(u => u.ToString()).ToList() ?? new List<string>();
        }

        public static List<string> ExtractUsingNames(string sourceCode)
        {
            return (CSharpSyntaxTree.ParseText(sourceCode).GetRoot() as CompilationUnitSyntax)
                ?.Usings.Select(u => u.Name.ToString()).ToList() ?? new List<string>();
        }

        public static List<string> ExtractUsingNames(SyntaxNode root)
        {
            return (root as CompilationUnitSyntax)
                ?.Usings.Select(u => u.Name.ToString()).ToList() ?? new List<string>();
        }

        #endregion

        #region 添加普通 Using

        public static string AddMissingUsings(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return sourceCode;

            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetCompilationUnitRoot();

            var references = CompilationReferenceHelper.GetRequiredReferences(sourceCode);

            var compilation = CSharpCompilation.Create(
                "TempAssembly",
                new[] { tree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var semanticModel = compilation.GetSemanticModel(tree);

            var existingUsings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingStaticUsings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var u in root.Usings)
            {
                if (u.Alias != null)
                    continue;

                if (u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                {
                    existingStaticUsings.Add(u.Name.ToString());
                }
                else
                {
                    existingUsings.Add(u.Name.ToString());
                }
            }

            var requiredNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var requiredStaticTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in root.DescendantNodes())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol;

                if (symbol == null)
                    continue;

                ProcessSymbol(symbol, requiredNamespaces, requiredStaticTypes);
            }

            var toAddUsings = UsingConfig.FixedUsings.Union(requiredNamespaces)
                                                     .Except(existingUsings)
                                                     .OrderBy(ns => ns)
                                                     .ToList();

            var toAddStaticUsings = UsingConfig.FixedStaticUsings.Union(requiredStaticTypes)
                                                                 .Except(existingStaticUsings)
                                                                 .OrderBy(t => t)
                                                                 .ToList();

            if (toAddUsings.Count == 0 && toAddStaticUsings.Count == 0)
                return sourceCode;

            var newUsings = new List<UsingDirectiveSyntax>();

            foreach (var ns in toAddUsings)
                newUsings.Add(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns)));

            foreach (var type in toAddStaticUsings)
            {
                var staticUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(type))
                                               .WithStaticKeyword(SyntaxFactory.Token(SyntaxKind.StaticKeyword));
                newUsings.Add(staticUsing);
            }

            var allUsings = root.Usings.Concat(newUsings)
                .OrderBy(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? 1 : 0)
                .ThenBy(u => u.Alias != null ? 1 : 0)
                .ThenBy(u => u.Name?.ToString() ?? "")
                .ToList();

            var newRoot = root.WithUsings(SyntaxFactory.List(allUsings))
                              .NormalizeWhitespace();

            return newRoot.ToFullString();
        }

        public static string AddUsings(string code, params string[] namespaces)
        {
            if (namespaces == null || namespaces.Length == 0) return code;

            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root == null) return code;

            var existingUsings = root.Usings
                .Select(u => u.Name.ToString())
                .ToHashSet(StringComparer.Ordinal);

            var newUsings = namespaces
                .Where(ns => !existingUsings.Contains(ns))
                .Select(ns => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns)))
                .ToList();

            if (newUsings.Count == 0) return code;

            var allUsings = root.Usings.Concat(newUsings)
                .OrderBy(u => u.Name.ToString())
                .ToList();

            var newRoot = root.WithUsings(SyntaxFactory.List(allUsings));
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        #endregion

        #region 添加静态 Using

        public static string AddStaticUsings(string code, params string[] staticTypes)
        {
            if (staticTypes == null || staticTypes.Length == 0) return code;

            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root == null) return code;

            var existingStaticUsings = root.Usings
                .Where(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                .Select(u => u.Name.ToString())
                .ToHashSet(StringComparer.Ordinal);

            var newUsings = staticTypes
                .Where(t => !existingStaticUsings.Contains(t))
                .Select(t => SyntaxFactory.UsingDirective(
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                    null,
                    SyntaxFactory.ParseName(t)))
                .ToList();

            if (newUsings.Count == 0) return code;

            var allUsings = root.Usings.Concat(newUsings).ToList();
            var newRoot = root.WithUsings(SyntaxFactory.List(allUsings));
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        public static string AddStaticClassReferences(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode)) return sourceCode;

            try
            {
                var root = CSharpSyntaxTree.ParseText(sourceCode).GetRoot() as CompilationUnitSyntax;
                if (root == null) return sourceCode;

                var existing = root.Usings
                    .Where(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                    .Select(u => u.Name?.ToString())
                    .Where(n => n != null)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var toAdd = new List<string>();

                foreach (var r in UsingConfig.FixedStaticUsings.Where(r => !existing.Contains(r)))
                    toAdd.Add(r);

                var used = root.DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Where(m => m.Expression is IdentifierNameSyntax || m.Expression is MemberAccessExpressionSyntax)
                    .Select(m => m.Expression.ToString())
                    .Distinct();

                foreach (var u in used)
                {
                    var match = UsingConfig.CustomStaticClasses.FirstOrDefault(c =>
                        c.EndsWith("." + u, StringComparison.OrdinalIgnoreCase) ||
                        c.Equals(u, StringComparison.OrdinalIgnoreCase) ||
                        c.Split('.').Last().Equals(u.Split('.').Last(), StringComparison.OrdinalIgnoreCase));

                    if (match != null && !existing.Contains(match) && !UsingConfig.FixedStaticUsings.Contains(match))
                        toAdd.Add(match);
                }

                if (!toAdd.Any()) return sourceCode;

                var newUsings = root.Usings.Concat(
                    toAdd.Select(ns =>
                        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns))
                            .WithStaticKeyword(SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                ).OrderBy(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                 .ThenBy(u => u.Name?.ToString())
                 .ToList();

                return root.WithUsings(SyntaxFactory.List(newUsings))
                    .NormalizeWhitespace()
                    .ToFullString();
            }
            catch
            {
                return sourceCode;
            }
        }

        #endregion

        #region 移除 Using

        public static string RemoveUsings(string code, params string[] namespaces)
        {
            if (namespaces == null || namespaces.Length == 0) return code;

            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root == null) return code;

            var removeSet = namespaces.ToHashSet(StringComparer.Ordinal);
            var remainingUsings = root.Usings
                .Where(u => !removeSet.Contains(u.Name.ToString()))
                .ToList();

            var newRoot = root.WithUsings(SyntaxFactory.List(remainingUsings));
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        public static string RemoveUnusedUsings(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetRoot() as CompilationUnitSyntax;
            if (root == null) return code;

            var usedNames = new HashSet<string>();
            CollectUsedNames(root, usedNames);

            var usedUsings = root.Usings
                .Where(u => IsUsingUsed(u, usedNames))
                .ToList();

            var newRoot = root.WithUsings(SyntaxFactory.List(usedUsings));
            return newRoot.NormalizeWhitespace().ToFullString();
        }

        #endregion

        #region 合并 Using

        public static string MergeUsings(string targetCode, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(targetCode))
                return sourceCode;
            if (string.IsNullOrWhiteSpace(sourceCode))
                return targetCode;

            var targetTree = CSharpSyntaxTree.ParseText(targetCode);
            var targetRoot = targetTree.GetCompilationUnitRoot();

            var sourceTree = CSharpSyntaxTree.ParseText(sourceCode);
            var sourceRoot = sourceTree.GetCompilationUnitRoot();

            var sourceUsingStrings = sourceRoot.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.ToString())
                .Distinct()
                .ToList();

            if (!sourceUsingStrings.Any())
                return targetCode;

            var targetUsingStrings = targetRoot.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Select(u => u.ToString())
                .ToHashSet();

            var newUsingStrings = sourceUsingStrings
                .Where(u => !targetUsingStrings.Contains(u))
                .ToList();

            if (!newUsingStrings.Any())
                return targetCode;

            var newUsingDirectives = newUsingStrings
                .Select(u => SyntaxFactory.ParseCompilationUnit(u).Usings[0])
                .ToArray();

            var rootWithNewUsings = targetRoot.AddUsings(newUsingDirectives);
            return rootWithNewUsings.NormalizeWhitespace().ToFullString();
        }

        #endregion

        #region 确保 System 命名空间

        public static string EnsureUsingSystem(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode)) return sourceCode;

            try
            {
                var root = CSharpSyntaxTree.ParseText(sourceCode).GetRoot() as CompilationUnitSyntax;
                if (root == null) return sourceCode;

                if (root.Usings.Any(u => u.Name?.ToString() == "System" &&
                    !u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)))
                    return sourceCode;

                var newUsings = new[] { SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")) }
                    .Concat(root.Usings)
                    .OrderBy(u => u.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
                    .ThenBy(u => u.Name?.ToString() ?? "")
                    .ToList();

                return root.WithUsings(SyntaxFactory.List(newUsings))
                    .NormalizeWhitespace()
                    .ToFullString();
            }
            catch
            {
                return sourceCode;
            }
        }

        public static string AddAllReferences(string sourceCode) =>
            AddStaticClassReferences(EnsureUsingSystem(sourceCode));

        #endregion

        #region 私有方法

        private static void ProcessSymbol(ISymbol symbol, HashSet<string> namespaces, HashSet<string> staticTypes)
        {
            switch (symbol)
            {
                case ITypeSymbol type:
                    AddNamespace(type.ContainingNamespace, namespaces);
                    break;

                case IMethodSymbol { IsExtensionMethod: true } extMethod:
                    AddNamespace(extMethod.ContainingNamespace, namespaces);
                    break;

                case IMethodSymbol method:
                    AddMemberSymbol(method, namespaces, staticTypes);
                    break;
                case IPropertySymbol prop:
                    AddMemberSymbol(prop, namespaces, staticTypes);
                    break;
                case IFieldSymbol field:
                    AddMemberSymbol(field, namespaces, staticTypes);
                    break;
                case IEventSymbol ev:
                    AddMemberSymbol(ev, namespaces, staticTypes);
                    break;

                case ILocalSymbol local:
                    AddNamespace(local.Type?.ContainingNamespace, namespaces);
                    break;

                case IParameterSymbol param:
                    AddNamespace(param.Type?.ContainingNamespace, namespaces);
                    break;
            }
        }

        private static void AddMemberSymbol(ISymbol member, HashSet<string> namespaces, HashSet<string> staticTypes)
        {
            var containingType = member.ContainingType;
            if (containingType == null)
                return;

            if (member.IsStatic)
            {
                if (!containingType.IsGenericType)
                    AddStaticType(containingType, staticTypes);
            }
            else
            {
                AddNamespace(containingType.ContainingNamespace, namespaces);
            }
        }

        private static void AddNamespace(INamespaceSymbol ns, HashSet<string> namespaces)
        {
            if (ns != null && !ns.IsGlobalNamespace)
                namespaces.Add(ns.ToString());
        }

        private static void AddStaticType(ITypeSymbol type, HashSet<string> staticTypes)
        {
            if (type == null) return;

            if (type is not INamedTypeSymbol namedType || namedType.IsGenericType)
                return;

            if (namedType.TypeKind != TypeKind.Class)
                return;

            var fullName = namedType.ToDisplayString(FullyQualifiedNoAliasFormat);
            if (fullName.StartsWith("global::"))
                fullName = fullName.Substring(8);

            staticTypes.Add(fullName);
        }

        private static void CollectUsedNames(SyntaxNode root, HashSet<string> names)
        {
            foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                names.Add(identifier.Identifier.ValueText);
            }

            foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                if (memberAccess.Expression is IdentifierNameSyntax id)
                {
                    names.Add(id.Identifier.ValueText);
                }
            }
        }

        private static bool IsUsingUsed(UsingDirectiveSyntax usingDirective, HashSet<string> usedNames)
        {
            var name = usingDirective.Name.ToString();

            if (name == "System" || name == "System.Linq" || name == "System.Collections.Generic")
                return true;

            var lastPart = name.Contains('.')
                ? name.Substring(name.LastIndexOf('.') + 1)
                : name;

            return usedNames.Contains(lastPart);
        }

        #endregion
    }
}
