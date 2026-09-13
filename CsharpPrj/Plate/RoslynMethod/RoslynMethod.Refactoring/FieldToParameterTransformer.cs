using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;

namespace RoslynSpace.RoslynSyntaxUtilities.Refactoring
{
    public static class FieldToParameterTransformer
    {
        #region 模型类

        public class TransformationResult
        {
            public string ModifiedCode { get; set; }
            public List<TransformationLog> Logs { get; set; } = new List<TransformationLog>();
            public Dictionary<string, List<string>> FieldUsage { get; set; } = new Dictionary<string, List<string>>();
        }

        public class TransformationLog
        {
            public string Type { get; set; }
            public string Message { get; set; }
            public string FunctionName { get; set; }
        }

        public class FieldInfo
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Accessibility { get; set; }
            public bool IsStatic { get; set; }
            public bool IsReadOnly { get; set; }
            public bool IsConst { get; set; }
            public string Initializer { get; set; }
        }

        public class FunctionInfo
        {
            public string Name { get; set; }
            public string FullName { get; set; }
            public SyntaxNode Node { get; set; }
            public List<string> CalledFunctions { get; set; } = new List<string>();
            public string ContainingType { get; set; }
            public bool IsStatic { get; set; }
            public bool IsConstructor { get; set; }
        }

        #endregion

        public static TransformationResult TransformFieldsToParameters(string code, string className = null)
        {
            var result = new TransformationResult();

            try
            {
                var build = CompilationSkeleton.BuildOrThrow(code, new CompilationSkeleton.CompilationConfig
                {
                    AssemblyName = "TempAssembly",
                    AutoResolveReferences = false,
                    CollectDiagnostics = false,
                    AdditionalReferences = { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }
                });
                var root = build.Root;
                var semanticModel = build.SemanticModel;

                var fields = ExtractFields(code, className);

                var functions = AnalyzeFunctionCalls(root);

                CollectFieldUsage(root, semanticModel, fields, result.FieldUsage, result.Logs);

                var rewriter = new FieldToParameterRewriter(semanticModel, fields, functions, result.FieldUsage, result.Logs);
                var newRoot = rewriter.Visit(root);

                result.ModifiedCode = newRoot.NormalizeWhitespace().ToFullString();

                result.Logs.Add(new TransformationLog
                {
                    Type = "INFO",
                    Message = $"转换完成。处理了 {fields.Count} 个字段，{functions.Count} 个函数"
                });
            }
            catch (Exception ex)
            {
                result.Logs.Add(new TransformationLog
                {
                    Type = "ERROR",
                    Message = $"转换过程中发生错误: {ex.Message}"
                });
            }

            return result;
        }

        #region 字段提取功能

        private static List<FieldInfo> ExtractFields(string code, string className = null)
        {
            var syntaxTree = CSharpSourceProvider.Instance.Parse(code);
            var root = syntaxTree.GetRoot();

            IEnumerable<FieldDeclarationSyntax> fieldDeclarations;

            if (string.IsNullOrEmpty(className))
            {
                fieldDeclarations = root.DescendantNodes().OfType<FieldDeclarationSyntax>();
            }
            else
            {
                var targetClass = root.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .FirstOrDefault(c => c.Identifier.ValueText == className);

                if (targetClass == null)
                    return new List<FieldInfo>();

                fieldDeclarations = targetClass.DescendantNodes().OfType<FieldDeclarationSyntax>();
            }

            var fieldInfos = new List<FieldInfo>();

            foreach (var fieldDeclaration in fieldDeclarations)
            {
                foreach (var variable in fieldDeclaration.Declaration.Variables)
                {
                    fieldInfos.Add(new FieldInfo
                    {
                        Name = variable.Identifier.ValueText,
                        Type = fieldDeclaration.Declaration.Type.ToString(),
                        Accessibility = GetAccessibility(fieldDeclaration.Modifiers),
                        IsStatic = fieldDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword),
                        IsReadOnly = fieldDeclaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword),
                        IsConst = fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword),
                        Initializer = variable.Initializer?.Value?.ToString() ?? string.Empty
                    });
                }
            }

            return fieldInfos;
        }

        private static string GetAccessibility(SyntaxTokenList modifiers)
            => AccessibilityConverter.GetAccessibilityString(modifiers);

        #endregion

        #region 函数调用分析功能

        public static Dictionary<string, FunctionInfo> AnalyzeFunctionCalls(SyntaxNode root)
        {
            var functions = CollectAllFunctions(root);

            foreach (var function in functions.Values)
            {
                AnalyzeFunctionCalls(function, functions);
            }

            return functions;
        }

        private static Dictionary<string, FunctionInfo> CollectAllFunctions(SyntaxNode root)
        {
            var functions = new Dictionary<string, FunctionInfo>();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var containingType = GetContainingTypeName(method);
                var fullName = $"{containingType}.{method.Identifier.ValueText}";
                var isStatic = method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));

                functions[fullName] = new FunctionInfo
                {
                    Name = method.Identifier.ValueText,
                    FullName = fullName,
                    Node = method,
                    ContainingType = containingType,
                    IsStatic = isStatic,
                    IsConstructor = false
                };
            }

            foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                var containingType = GetContainingTypeName(constructor);
                var fullName = $"{containingType}.{constructor.Identifier.ValueText}";

                functions[fullName] = new FunctionInfo
                {
                    Name = constructor.Identifier.ValueText,
                    FullName = fullName,
                    Node = constructor,
                    ContainingType = containingType,
                    IsStatic = false,
                    IsConstructor = true
                };
            }

            return functions;
        }

        private static void AnalyzeFunctionCalls(FunctionInfo function, Dictionary<string, FunctionInfo> allFunctions)
        {
            var invocations = function.Node.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                AnalyzeInvocation(invocation, function, allFunctions);
            }

            var objectCreations = function.Node.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var objectCreation in objectCreations)
            {
                AnalyzeObjectCreation(objectCreation, function, allFunctions);
            }
        }

        private static void AnalyzeInvocation(InvocationExpressionSyntax invocation, FunctionInfo callingFunction, Dictionary<string, FunctionInfo> allFunctions)
        {
            string methodName = null;
            string targetType = null;

            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    (methodName, targetType) = AnalyzeMemberAccess(memberAccess, callingFunction);
                    break;

                case IdentifierNameSyntax identifier:
                    methodName = identifier.Identifier.ValueText;
                    targetType = callingFunction.ContainingType;
                    break;

                case MemberBindingExpressionSyntax memberBinding:
                    methodName = (memberBinding.Name as IdentifierNameSyntax)?.Identifier.ValueText;
                    targetType = callingFunction.ContainingType;
                    break;
            }

            if (!string.IsNullOrEmpty(methodName) && !string.IsNullOrEmpty(targetType))
            {
                FindAndAddCalledFunction(callingFunction, methodName, targetType, allFunctions);
            }
        }

        private static (string methodName, string targetType) AnalyzeMemberAccess(MemberAccessExpressionSyntax memberAccess, FunctionInfo callingFunction)
        {
            var methodName = (memberAccess.Name as IdentifierNameSyntax)?.Identifier.ValueText;
            if (string.IsNullOrEmpty(methodName)) return (null, null);

            var targetType = AnalyzeTargetType(memberAccess.Expression, callingFunction);
            return (methodName, targetType);
        }

        private static string AnalyzeTargetType(ExpressionSyntax expression, FunctionInfo callingFunction)
        {
            return expression switch
            {
                IdentifierNameSyntax identifier => AnalyzeIdentifierTarget(identifier, callingFunction),
                ThisExpressionSyntax _ => callingFunction.ContainingType,
                BaseExpressionSyntax _ => callingFunction.ContainingType + "Base",
                ObjectCreationExpressionSyntax objectCreation =>
                    (objectCreation.Type as IdentifierNameSyntax)?.Identifier.ValueText,
                _ => callingFunction.ContainingType
            };
        }

        private static string AnalyzeIdentifierTarget(IdentifierNameSyntax identifier, FunctionInfo callingFunction)
        {
            var identifierName = identifier.Identifier.ValueText;

            var isClassName = callingFunction.CalledFunctions.Any(f => f.StartsWith(identifierName + "."));
            if (isClassName)
            {
                return identifierName;
            }

            return callingFunction.ContainingType;
        }

        private static void AnalyzeObjectCreation(ObjectCreationExpressionSyntax objectCreation, FunctionInfo callingFunction, Dictionary<string, FunctionInfo> allFunctions)
        {
            if (objectCreation.Type is IdentifierNameSyntax typeName)
            {
                var className = typeName.Identifier.ValueText;
                FindAndAddCalledFunction(callingFunction, className, className, allFunctions);
            }
        }

        private static void FindAndAddCalledFunction(FunctionInfo callingFunction, string methodName, string targetType, Dictionary<string, FunctionInfo> allFunctions)
        {
            var matchingFunctions = allFunctions.Values
                .Where(f => f.Name == methodName && f.ContainingType == targetType)
                .Select(f => f.FullName)
                .Distinct();

            foreach (var calledFunction in matchingFunctions)
            {
                if (!callingFunction.CalledFunctions.Contains(calledFunction))
                {
                    callingFunction.CalledFunctions.Add(calledFunction);
                }
            }
        }

        private static string GetContainingTypeName(SyntaxNode node)
        {
            var typeDeclaration = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            return typeDeclaration?.Identifier.ValueText ?? "Global";
        }

        #endregion

        #region 顶层函数分析

        public static class TopLevelClassAnalyzer
        {
            public class EnhancedFunctionInfo : FunctionInfo
            {
                public Microsoft.CodeAnalysis.Accessibility AccessModifier { get; set; }
            }

            public static List<EnhancedFunctionInfo> AnalyzeTopLevelFunctionsFromRoot(
                SyntaxNode root,
                bool enhancedMode = false)
            {
                var originalFunctions = AnalyzeFunctionCalls(root);
                var enhancedFunctions = ConvertToEnhancedFunctionInfo(originalFunctions);
                return AnalyzeTopLevelFunctions(enhancedFunctions, enhancedMode);
            }

            public static List<string> AnalyzeTopLevelFunctionsFromRoot(
                SyntaxNode root,
                bool returnFullNames,
                bool enhancedMode = false)
            {
                var topLevelFunctions = AnalyzeTopLevelFunctionsFromRoot(root, enhancedMode);

                return returnFullNames
                    ? topLevelFunctions.Select(f => f.FullName).ToList()
                    : topLevelFunctions.Select(f => f.Name).ToList();
            }

            public static List<string> AnalyzeTopLevelFunctionsFromRoot(
                SyntaxNode root,
                Func<EnhancedFunctionInfo, string> nameSelector,
                bool enhancedMode = false)
            {
                var topLevelFunctions = AnalyzeTopLevelFunctionsFromRoot(root, enhancedMode);
                return topLevelFunctions.Select(nameSelector).ToList();
            }

            public static Dictionary<string, EnhancedFunctionInfo> ConvertToEnhancedFunctionInfo(
                Dictionary<string, FunctionInfo> originalFunctions)
            {
                var enhancedFunctions = new Dictionary<string, EnhancedFunctionInfo>();

                foreach (var function in originalFunctions.Values)
                {
                    var enhancedFunction = new EnhancedFunctionInfo
                    {
                        Name = function.Name,
                        FullName = function.FullName,
                        Node = function.Node,
                        CalledFunctions = function.CalledFunctions,
                        ContainingType = function.ContainingType,
                        IsStatic = function.IsStatic,
                        IsConstructor = function.IsConstructor,
                        AccessModifier = GetAccessibilityFromNode(function.Node)
                    };

                    enhancedFunctions[function.FullName] = enhancedFunction;
                }

                return enhancedFunctions;
            }

            public static List<EnhancedFunctionInfo> AnalyzeTopLevelFunctions(
                Dictionary<string, EnhancedFunctionInfo> allFunctions,
                bool enhancedMode = false)
            {
                return enhancedMode
                    ? AnalyzeEnhancedTopLevelFunctions(allFunctions)
                    : AnalyzeBasicTopLevelFunctions(allFunctions);
            }

            private static List<EnhancedFunctionInfo> AnalyzeBasicTopLevelFunctions(
                Dictionary<string, EnhancedFunctionInfo> allFunctions)
            {
                return allFunctions.Values
                    .Where(f => !f.IsConstructor)
                    .Where(f => f.AccessModifier == Microsoft.CodeAnalysis.Accessibility.Public)
                    .Where(f => !IsCalledByAnyFunction(f, allFunctions))
                    .ToList();
            }

            private static List<EnhancedFunctionInfo> AnalyzeEnhancedTopLevelFunctions(
                Dictionary<string, EnhancedFunctionInfo> allFunctions)
            {
                return allFunctions.Values
                    .Where(f => !f.IsConstructor)
                    .Where(f => !IsCalledByAnyFunction(f, allFunctions))
                    .Where(f => IsPotentialEntryPointByCallRelation(f, allFunctions))
                    .ToList();
            }

            private static bool IsPotentialEntryPointByCallRelation(
                EnhancedFunctionInfo function,
                Dictionary<string, EnhancedFunctionInfo> allFunctions)
            {
                return !IsCalledByAnyFunction(function, allFunctions) &&
                       function.CalledFunctions.Any();
            }

            private static bool IsCalledByAnyFunction(
                EnhancedFunctionInfo targetFunction,
                Dictionary<string, EnhancedFunctionInfo> allFunctions)
            {
                return allFunctions.Values.Any(caller =>
                    caller.CalledFunctions.Contains(targetFunction.FullName));
            }

            private static Microsoft.CodeAnalysis.Accessibility GetAccessibilityFromNode(SyntaxNode node)
                => AccessibilityConverter.GetAccessibility(node);
        }

        #endregion

        #region 字段使用情况分析

        private static void CollectFieldUsage(
            SyntaxNode root,
            SemanticModel semanticModel,
            List<FieldInfo> fields,
            Dictionary<string, List<string>> fieldUsage,
            List<TransformationLog> logs)
        {
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                AnalyzeFieldUsageInFunction(method, method.Identifier.ValueText, semanticModel, fields, fieldUsage);
            }

            foreach (var constructor in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            {
                AnalyzeFieldUsageInFunction(constructor, constructor.Identifier.ValueText, semanticModel, fields, fieldUsage);
            }
        }

        private static void AnalyzeFieldUsageInFunction(
            SyntaxNode functionNode,
            string functionName,
            SemanticModel semanticModel,
            List<FieldInfo> fields,
            Dictionary<string, List<string>> fieldUsage)
        {
            var memberAccesses = functionNode.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(ma => ma.Expression is ThisExpressionSyntax);

            foreach (var memberAccess in memberAccesses)
            {
                var fieldName = (memberAccess.Name as IdentifierNameSyntax)?.Identifier.ValueText;
                if (!string.IsNullOrEmpty(fieldName) && fields.Any(f => f.Name == fieldName))
                {
                    if (!fieldUsage.ContainsKey(fieldName))
                        fieldUsage[fieldName] = new List<string>();

                    if (!fieldUsage[fieldName].Contains(functionName))
                        fieldUsage[fieldName].Add(functionName);
                }
            }

            var identifiers = functionNode.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(id => fields.Any(f => f.Name == id.Identifier.ValueText));

            foreach (var identifier in identifiers)
            {
                var fieldName = identifier.Identifier.ValueText;
                if (!fieldUsage.ContainsKey(fieldName))
                    fieldUsage[fieldName] = new List<string>();

                if (!fieldUsage[fieldName].Contains(functionName))
                    fieldUsage[fieldName].Add(functionName);
            }
        }

        #endregion

        #region 工具方法

        public static class PackagedInvokeFunction
        {
            public static List<FunctionInfo> UnusedMethod(Dictionary<string, FunctionInfo> cyclicFunctions)
            {
                var unusedMethods = cyclicFunctions.Values
                .Where(f => !f.IsConstructor)
                .Where(f => !cyclicFunctions.Values.Any(caller =>
                    caller.CalledFunctions.Contains(f.FullName)))
                .ToList();

                return unusedMethods;
            }

            public static IEnumerable<string> CyclicRelations(Dictionary<string, FunctionInfo> cyclicFunctions)
            {
                var result = cyclicFunctions.Values
                   .SelectMany(f => f.CalledFunctions.Select(called => new { Caller = f, Called = called }))
                   .Where(x => cyclicFunctions.ContainsKey(x.Called))
                   .Where(x => cyclicFunctions[x.Called].ContainingType != x.Caller.ContainingType)
                   .Select(x => x.Called)
                   .Distinct();

                return result;
            }

            public static Dictionary<string, List<string>> CallGraph(SyntaxTree cyclicTree, Dictionary<string, FunctionInfo> cyclicFunctions)
            {
                var callGraph = cyclicFunctions.Values.ToDictionary(
                      f => f.FullName,
                      f => f.CalledFunctions
                  );
                return callGraph;
            }
        }

        #endregion
    }
}
