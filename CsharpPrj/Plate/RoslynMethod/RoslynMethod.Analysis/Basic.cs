using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EC = RoslynSpace.RoslynSyntaxUtilities.Base.EnhancedCodeElement;
using ET = RoslynSpace.RoslynSyntaxUtilities.Base.ElementType;
using VU = RoslynSpace.RoslynSyntaxUtilities.Base.VariableUsageInfo;

namespace RoslynSpace
{
    #region 代码聚类

    public static class SyntaxElementExtractor
    {
        public static List<EC> Extract(string code)
        {
            try
            {
                var (syntaxTree, semanticModel) = SyntaxParser.ParseCode(code);
                var root = syntaxTree.GetRoot();
                var elements = new ConcurrentBag<EC>();

                var classDeclarations = QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(root);

                Parallel.ForEach(classDeclarations, classDecl =>
                {
                    var className = classDecl.Identifier.Text;

                    ExtractElementsFromType<MethodDeclarationSyntax>(
                        classDecl, className, semanticModel, elements,
                        (method, cn, sm) => CreateMethodElement(method, cn, sm));

                    ExtractElementsFromType<PropertyDeclarationSyntax>(
                        classDecl, className, semanticModel, elements,
                        (property, cn, sm) => CreatePropertyElement(property, cn, sm));

                    ExtractElementsFromType<FieldDeclarationSyntax>(
                        classDecl, className, semanticModel, elements,
                        (field, cn, sm) => CreateFieldElements(field, cn, sm));

                    ExtractElementsFromType<ConstructorDeclarationSyntax>(
                        classDecl, className, semanticModel, elements,
                        (constructor, cn, sm) => CreateConstructorElement(constructor, cn, sm));
                });

                return elements.ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"提取失败: {ex.Message}");
                return new List<EC>();
            }
        }

        private static void ExtractElementsFromType<T>(ClassDeclarationSyntax classDecl, string className,
            SemanticModel semanticModel, ConcurrentBag<EC> elements,
            Func<T, string, SemanticModel, List<EC>> elementCreator) where T : SyntaxNode
        {
            var nodes = classDecl.DescendantNodes().OfType<T>();
            foreach (var node in nodes)
            {
                var createdElements = elementCreator(node, className, semanticModel);
                foreach (var element in createdElements)
                {
                    elements.Add(element);
                }
            }
        }

        private static List<EC> CreateMethodElement(MethodDeclarationSyntax method,
            string className, SemanticModel semanticModel)
        {
            var element = CreateBaseElement(method, ET.Method, className);
            if (element == null) return new List<EC>();

            var symbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            SetSymbolProperties(element, symbol);

            element.ReturnType = method.ReturnType.ToString();
            element.Parameters.AddRange(method.ParameterList.Parameters.Select(p => p.Identifier.Text));
            element.ComplexityScore = ComplexityCalculator.CalculateMethodComplexity(method);

            element.IsPure = PurityAnalyzer.AnalyzeMethodPurity(method, semanticModel);
            element.HasSideEffects = PurityAnalyzer.AnalyzeSideEffects(method);

            element.HasOverloads = CheckForOverloads(method, semanticModel, className);

            return new List<EC> { element };
        }

        private static bool CheckForOverloads(MethodDeclarationSyntax method, SemanticModel semanticModel, string className)
        {
            try
            {
                var classSymbol = semanticModel.GetDeclaredSymbol(method.Parent as ClassDeclarationSyntax);
                if (classSymbol == null) return false;

                var methodsWithSameName = classSymbol.GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.Name == method.Identifier.Text)
                    .ToList();

                return methodsWithSameName.Count > 1;
            }
            catch
            {
                return false;
            }
        }

        private static List<EC> CreateConstructorElement(ConstructorDeclarationSyntax constructor,
            string className, SemanticModel semanticModel)
        {
            var element = CreateBaseElement(constructor, ET.Constructor, className);
            if (element == null) return new List<EC>();

            var symbol = semanticModel.GetDeclaredSymbol(constructor) as IMethodSymbol;
            SetSymbolProperties(element, symbol);

            element.Parameters.AddRange(constructor.ParameterList.Parameters.Select(p => p.Identifier.Text));
            element.ComplexityScore = ComplexityCalculator.CalculateConstructorComplexity(constructor);
            element.IsPure = false;
            element.HasSideEffects = true;

            return new List<EC> { element };
        }

        private static List<EC> CreatePropertyElement(PropertyDeclarationSyntax property,
            string className, SemanticModel semanticModel)
        {
            var element = CreateBaseElement(property, ET.Property, className);
            if (element == null) return new List<EC>();

            var symbol = semanticModel.GetDeclaredSymbol(property) as IPropertySymbol;
            SetSymbolProperties(element, symbol);

            return new List<EC> { element };
        }

        private static List<EC> CreateFieldElements(FieldDeclarationSyntax field,
            string className, SemanticModel semanticModel)
        {
            var elements = new List<EC>();

            foreach (var variable in field.Declaration.Variables)
            {
                var element = CreateBaseElement(variable, ET.Field, className);
                if (element == null) continue;

                var symbol = semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                SetSymbolProperties(element, symbol);

                elements.Add(element);
            }

            return elements;
        }

        private static EC CreateBaseElement(SyntaxNode node, ET type, string className)
        {
            int lineNumber = SyntaxLocator.GetLineNumber(node);
            string name = SyntaxAccessor.GetNodeName(node);

            if (string.IsNullOrEmpty(name)) return null;

            return new EC(
                $"{type}_{name}_{lineNumber}_{className}",
                name, type, lineNumber, className);
        }

        private static void SetSymbolProperties(EC element, ISymbol symbol)
        {
            if (symbol == null) return;

            element.Accessibility = GetAccessibilityFromSymbol(symbol.DeclaredAccessibility);
            element.IsStatic = symbol.IsStatic;

            if (symbol is IMethodSymbol methodSymbol)
            {
                element.IsVirtual = methodSymbol.IsVirtual;
                element.IsAbstract = methodSymbol.IsAbstract;
            }
            else if (symbol is IPropertySymbol propertySymbol)
            {
                element.IsVirtual = propertySymbol.IsVirtual;
                element.IsAbstract = propertySymbol.IsAbstract;
            }
            else if (symbol is IFieldSymbol fieldSymbol)
            {
                element.IsVirtual = false;
                element.IsAbstract = false;
            }
        }

        private static string GetAccessibilityFromSymbol(Microsoft.CodeAnalysis.Accessibility accessibility) => accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => "public",
            Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
            Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
            Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
            _ => "private"
        };
    }

    public static class VariableAnalyzer
    {
        public static Dictionary<string, List<VU>> AnalyzeVariables(SyntaxNode root)
        {
            var variableMap = new Dictionary<string, List<VU>>();

            var classDeclarations = QuerySkeleton.FindTopLevelDeclarations<ClassDeclarationSyntax>(root);

            foreach (var classDecl in classDeclarations)
            {
                var className = classDecl.Identifier.Text;

                AnalyzeMemberVariables<MethodDeclarationSyntax>(
                    classDecl, className, variableMap,
                    (method, cn) => AnalyzeMethodVariables(method, cn, variableMap));

                AnalyzeMemberVariables<ConstructorDeclarationSyntax>(
                    classDecl, className, variableMap,
                    (constructor, cn) => AnalyzeConstructorVariables(constructor, cn, variableMap));

                AnalyzeMemberVariables<PropertyDeclarationSyntax>(
                    classDecl, className, variableMap,
                    (property, cn) => AnalyzePropertyVariables(property, cn, variableMap));
            }

            return variableMap;
        }

        private static void AnalyzeMemberVariables<T>(ClassDeclarationSyntax classDecl, string className,
            Dictionary<string, List<VU>> variableMap,
            Action<T, string> analyzer) where T : SyntaxNode
        {
            var members = classDecl.DescendantNodes().OfType<T>();
            foreach (var member in members)
            {
                analyzer(member, className);
            }
        }

        private static void AnalyzeMethodVariables(MethodDeclarationSyntax method, string className,
                                                  Dictionary<string, List<VU>> variableMap)
        {
            var methodName = method.Identifier.Text;
            var methodKey = $"{className}.{methodName}";
            var variables = new List<VU>();

            AnalyzeParameters(method.ParameterList, methodKey, variables);

            if (method.Body != null)
            {
                AnalyzeBlockVariables(method.Body, methodKey, variables);
            }

            if (method.ExpressionBody != null)
            {
                AnalyzeExpressionVariables(method.ExpressionBody, methodKey, variables);
            }

            if (variables.Any())
            {
                variableMap[methodKey] = variables;
            }
        }

        private static void AnalyzeConstructorVariables(ConstructorDeclarationSyntax constructor, string className,
                                                       Dictionary<string, List<VU>> variableMap)
        {
            var constructorName = constructor.Identifier.Text;
            var methodKey = $"{className}.{constructorName}";
            var variables = new List<VU>();

            AnalyzeParameters(constructor.ParameterList, methodKey, variables);

            if (constructor.Body != null)
            {
                AnalyzeBlockVariables(constructor.Body, methodKey, variables);
            }

            if (variables.Any())
            {
                variableMap[methodKey] = variables;
            }
        }

        private static void AnalyzePropertyVariables(PropertyDeclarationSyntax property, string className,
                                                   Dictionary<string, List<VU>> variableMap)
        {
            var propertyName = property.Identifier.Text;
            var methodKey = $"{className}.{propertyName}";
            var variables = new List<VU>();

            if (property.AccessorList != null)
            {
                foreach (var accessor in property.AccessorList.Accessors)
                {
                    if (accessor.Body != null)
                    {
                        AnalyzeBlockVariables(accessor.Body, methodKey, variables);
                    }

                    if (accessor.ExpressionBody != null)
                    {
                        AnalyzeExpressionVariables(accessor.ExpressionBody, methodKey, variables);
                    }
                }
            }

            if (variables.Any())
            {
                variableMap[methodKey] = variables;
            }
        }

        private static void AnalyzeParameters(BaseParameterListSyntax parameterList, string methodKey,
                                            List<VU> variables)
        {
            if (parameterList == null) return;

            foreach (var parameter in parameterList.Parameters)
            {
                var varInfo = new VU(
                    parameter.Identifier.Text,
                    parameter.Type?.ToString() ?? "unknown",
                    SyntaxLocator.GetLineNumber(parameter),
                    isParameter: true,
                    isLocal: false,
                    containingMethod: methodKey
                );

                variables.Add(varInfo);
            }
        }

        private static void AnalyzeBlockVariables(BlockSyntax block, string methodKey,
                                                List<VU> variables)
        {
            AnalyzeLocalDeclarations(block, methodKey, variables);

            AnalyzeLoopVariables(block, methodKey, variables);
        }

        private static void AnalyzeLocalDeclarations(SyntaxNode scope, string methodKey,
                                                   List<VU> variables)
        {
            var localDeclarations = scope.DescendantNodes().OfType<LocalDeclarationStatementSyntax>();

            foreach (var declaration in localDeclarations)
            {
                foreach (var variable in declaration.Declaration.Variables)
                {
                    var varInfo = new VU(
                        variable.Identifier.Text,
                        declaration.Declaration.Type.ToString(),
                        SyntaxLocator.GetLineNumber(variable),
                        isParameter: false,
                        isLocal: true,
                        containingMethod: methodKey
                    );

                    AnalyzeVariableUsage(variable.Identifier.Text, scope, varInfo);

                    variables.Add(varInfo);
                }
            }
        }

        private static void AnalyzeLoopVariables(SyntaxNode scope, string methodKey,
                                         List<VU> variables)
        {
            var forStatements = scope.DescendantNodes().OfType<ForStatementSyntax>();
            foreach (var forStmt in forStatements)
            {
                if (forStmt.Declaration != null)
                {
                    foreach (var variable in forStmt.Declaration.Variables)
                    {
                        var varInfo = new VU(
                            variable.Identifier.Text,
                            forStmt.Declaration.Type.ToString(),
                            SyntaxLocator.GetLineNumber(variable),
                            isParameter: false,
                            isLocal: true,
                            containingMethod: methodKey
                        );

                        AnalyzeVariableUsage(variable.Identifier.Text, forStmt, varInfo);
                        variables.Add(varInfo);
                    }
                }
            }

            var foreachStatements = scope.DescendantNodes().OfType<ForEachStatementSyntax>();
            foreach (var foreachStmt in foreachStatements)
            {
                int lineNumber = SyntaxLocator.GetLineNumber(foreachStmt);

                var varInfo = new VU(
                    foreachStmt.Identifier.Text,
                    foreachStmt.Type.ToString(),
                    lineNumber,
                    isParameter: false,
                    isLocal: true,
                    containingMethod: methodKey
                );

                AnalyzeVariableUsage(foreachStmt.Identifier.Text, foreachStmt, varInfo);
                variables.Add(varInfo);
            }
        }


        private static void AnalyzeExpressionVariables(ArrowExpressionClauseSyntax expressionBody,
                                                       string methodKey, List<VU> variables)
        {
            var identifiers = expressionBody.DescendantNodes().OfType<IdentifierNameSyntax>();

            foreach (var identifier in identifiers)
            {
                var identifierName = identifier.Identifier.Text;
                var line = SyntaxLocator.GetLineNumber(identifier);

                var variable = variables.FirstOrDefault(v => v.Name == identifierName);
                if (variable != null)
                {
                    variable.AddUsage(line);
                }
            }
        }

        private static void AnalyzeVariableUsage(string variableName, SyntaxNode scope, VU variableInfo)
        {
            var usages = scope.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(id => id.Identifier.Text == variableName);

            foreach (var usage in usages)
            {
                var line = SyntaxLocator.GetLineNumber(usage);
                variableInfo.AddUsage(line);
            }
        }

        public static void UpdateElementVariables(List<EC> elements,
                                                Dictionary<string, List<VU>> variableMap)
        {
            foreach (var element in elements)
            {
                if (element.Type != ET.Method &&
                    element.Type != ET.Constructor &&
                    element.Type != ET.Property)
                    continue;

                var elementKey = $"{element.ContainingType}.{element.Name}";

                if (variableMap.TryGetValue(elementKey, out var variables))
                {
                    element.Variables.Clear();
                    element.Variables.AddRange(variables);

                    foreach (var variable in variables)
                    {
                        if (!string.IsNullOrEmpty(variable.Type) && variable.Type != "unknown")
                        {
                            element.UsedTypes.Add(variable.Type);

                            if (!element.TypeUsageFrequency.ContainsKey(variable.Type))
                                element.TypeUsageFrequency[variable.Type] = 0;
                            element.TypeUsageFrequency[variable.Type]++;
                        }
                    }
                }
            }
        }

        public static void CalculateVariableMetrics(List<EC> elements)
        {
            foreach (var element in elements)
            {
                if (element.Variables.Count == 0)
                {
                    element.VariableReuseRatio = 0;
                    element.VariableScopeComplexity = 0;
                    continue;
                }

                int reusedVariables = element.Variables.Count(v => v.UsageCount > 1);
                element.VariableReuseRatio = VariableMetricsCalculator.CalculateVariableReuseRatio(
                    reusedVariables, element.Variables.Count);

                element.VariableScopeComplexity = VariableMetricsCalculator.CalculateVariableScopeComplexity(
                    element.Variables);
            }
        }
    }

    public static class ReferenceAnalyzer
    {
        public static void Analyze(List<EC> elements, SyntaxNode root)
        {
            var elementMap = CreateElementMap(elements);
            var referenceData = new ConcurrentDictionary<string, Dictionary<string, int>>();

            Parallel.ForEach(elements, element =>
            {
                var node = FindSyntaxNode(element, root);
                if (node != null)
                {
                    var references = CollectReferences(node, elementMap);
                    referenceData[element.Id] = references;
                }
            });

            UpdateAllReferences(elements, referenceData, elementMap);
        }

        private static Dictionary<string, List<EC>> CreateElementMap(
            List<EC> elements)
        {
            var map = new Dictionary<string, List<EC>>();

            foreach (var element in elements)
            {
                if (!map.ContainsKey(element.Name))
                    map[element.Name] = new List<EC>();
                map[element.Name].Add(element);
            }

            return map;
        }

        private static Dictionary<string, int> CollectReferences(SyntaxNode node,
            Dictionary<string, List<EC>> elementMap)
        {
            var references = new Dictionary<string, int>();
            CollectReferencesRecursive(node, references, elementMap);
            return references;
        }

        private static void CollectReferencesRecursive(SyntaxNode node,
            Dictionary<string, int> references,
            Dictionary<string, List<EC>> elementMap)
        {
            var identifier = SyntaxAccessor.ExtractIdentifier(node);
            if (!string.IsNullOrEmpty(identifier) && elementMap.ContainsKey(identifier))
            {
                references[identifier] = references.GetValueOrDefault(identifier) + 1;
            }

            foreach (var child in node.ChildNodes())
                CollectReferencesRecursive(child, references, elementMap);
        }

        private static void UpdateAllReferences(List<EC> elements,
            ConcurrentDictionary<string, Dictionary<string, int>> referenceData,
            Dictionary<string, List<EC>> elementMap)
        {
            foreach (var element in elements)
            {
                if (referenceData.TryGetValue(element.Id, out var references))
                {
                    element.References.Clear();
                    foreach (var kvp in references)
                        element.References[kvp.Key] = kvp.Value;

                    UpdateReferencedBy(element, references, elementMap);
                }
            }
        }

        private static void UpdateReferencedBy(EC source,
            Dictionary<string, int> references,
            Dictionary<string, List<EC>> elementMap)
        {
            foreach (var refName in references.Keys)
            {
                if (elementMap.TryGetValue(refName, out var targets))
                {
                    foreach (var target in targets)
                    {
                        target.ReferencedBy.Add(source.Id);
                    }
                }
            }
        }

        private static SyntaxNode FindSyntaxNode(EC element, SyntaxNode root) =>
            root.DescendantNodes().FirstOrDefault(n => SyntaxMatcher.IsMatch(n, element));
    }

    #endregion
}
