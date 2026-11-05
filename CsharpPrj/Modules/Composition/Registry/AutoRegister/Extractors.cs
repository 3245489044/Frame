
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Xml.Linq;
using Modules.ReflectionCommon;
using Modules.ReflectionMethod.Dynamic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Compilation;
using RoslynSpace.RoslynSyntaxUtilities.Core.Utilities;
using Modules.Registry.AutoRegister.Cache;
using Modules.Registry.AutoRegister.Filters;

using DelegateType = System.Delegate;

using Modules.Registry.AutoRegister;

namespace Modules.Registry.AutoRegister.Extractors
{
    using Modules.Registry.AutoRegister.Delegate;


    public class ExtractionContext
    {
        public string SourceCode { get; init; } = string.Empty;

        public SyntaxTree? SyntaxTree { get; set; }

        public SyntaxNode? Root { get; set; }

        public SemanticModel? SemanticModel { get; set; }

        public Assembly? CompiledAssembly { get; set; }

        public List<ExtractedMethodInfo> Methods { get; set; } = new();

        public string ClassName { get; init; } = string.Empty;

        public MethodFilterConfig? MethodFilters { get; set; }

        public Dictionary<string, object?> Bag { get; init; } = new();
    }

    public interface IExtractionStage
    {
        string StageName { get; }

        void Process(ExtractionContext context);
    }


    public interface IMethodExtractor
    {
        string SourceType { get; }

        List<ExtractedMethodInfo> ExtractMethods(string className, MethodFilterConfig? methodFilters = null);
    }


    public class ReflectionExtractor : IMethodExtractor
    {
        private readonly Assembly _assembly;
        private readonly IDelegateFactory _delegateFactory;

        public ReflectionExtractor(Assembly assembly, IDelegateFactory? delegateFactory = null)
        {
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            _delegateFactory = delegateFactory ?? new DelegateFactory();
        }

        public string SourceType => "assembly";

        public List<ExtractedMethodInfo> ExtractMethods(string className, MethodFilterConfig? methodFilters = null)
        {
            var type = _assembly.GetType(className);
            if (type == null)
                throw new InvalidOperationException($"Type '{className}' not found in assembly '{_assembly.FullName}'");

            var nestedTypes = type.GetNestedTypes(BindingFlags.Public);

            var allTypes = new List<Type> { type };
            allTypes.AddRange(nestedTypes);


            var result = new List<ExtractedMethodInfo>();
            foreach (var t in allTypes)
            {
                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var method in methods)
                {
                    var extracted = ConvertToExtractedMethodInfo(method, type);
                    if (extracted != null)
                        result.Add(extracted);
                }
            }

            result = MethodFilter.Apply(result, methodFilters);

            return result;
        }

        private ExtractedMethodInfo? ConvertToExtractedMethodInfo(MethodInfo method, Type mainType)
        {
            try
            {
                string? nestedClassName = null;
                if (method.DeclaringType != null && method.DeclaringType != mainType)
                {
                    nestedClassName = method.DeclaringType.Name;
                }

                var description = ExtractXmlDocComment(method);

                Func<object[], Task<object>>? executor = null;
                try
                {
                    executor = _delegateFactory.CreateExecutor(method);
                }
                catch
                {
                }

                var parameters = method.GetParameters().Select(p => new ExtractedMethodInfo.ParameterInfo
                {
                    Name = p.Name ?? "param",
                    Type = p.ParameterType,
                    IsOptional = p.IsOptional,
                    DefaultValue = p.IsOptional ? p.DefaultValue : null
                }).ToArray();

                return new ExtractedMethodInfo
                {
                    MethodName = method.Name,
                    ContainingClassName = mainType.Name,
                    ContainingNestedClassName = nestedClassName,
                    ReturnType = method.ReturnType,
                    Parameters = parameters,
                    Description = description,
                    IsStatic = method.IsStatic,
                    IsAsync = TypeHelper.IsAsyncType(method.ReturnType),
                    Executor = executor
                };
            }
            catch
            {
                return null;
            }
        }

        private string ExtractXmlDocComment(MethodInfo method)
        {
            try
            {
                var xmlDocPath = Path.ChangeExtension(_assembly.Location, ".xml");
                if (!File.Exists(xmlDocPath))
                    return string.Empty;

                var doc = XDocument.Load(xmlDocPath);
                var memberName = GetXmlDocMemberName(method);
                var member = doc.Descendants("member")
                    .FirstOrDefault(m => m.Attribute("name")?.Value == memberName);

                if (member == null)
                    return string.Empty;

                var summary = member.Element("summary");
                return summary != null ? summary.Value.Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetXmlDocMemberName(MethodInfo method)
        {
            if (method.DeclaringType == null)
                return method.Name;

            var fullName = $"M:{method.DeclaringType.FullName}.{method.Name}";

            var parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                var paramTypes = string.Join(",", parameters.Select(p =>
                {
                    if (p.ParameterType.IsGenericType)
                    {
                        var genericType = p.ParameterType.GetGenericTypeDefinition();
                        var args = string.Join(",",
                            p.ParameterType.GetGenericArguments().Select(a => a.Name));
                        return $"{genericType.FullName}{{{args}}}";
                    }
                    return p.ParameterType.FullName;
                }));
                fullName += $"({paramTypes})";
            }

            return fullName;
        }
    }


    public class RoslynExtractor : IMethodExtractor
    {
        private readonly string _sourceCode;
        private readonly IDelegateFactory _delegateFactory;
        private Assembly? _compiledAssembly;

        public RoslynExtractor(string sourceCode, IDelegateFactory? delegateFactory = null)
        {
            _sourceCode = sourceCode ?? throw new ArgumentNullException(nameof(sourceCode));
            _delegateFactory = delegateFactory ?? new DelegateFactory();
        }

        public string SourceType => "source";

        public List<ExtractedMethodInfo> ExtractMethods(string className, MethodFilterConfig? methodFilters = null)
        {
            var context = new ExtractionContext
            {
                SourceCode = _sourceCode,
                ClassName = className,
                MethodFilters = methodFilters
            };

            ParseSyntaxTree(context);

            BuildSemanticModel(context);

            ExtractMethodsFromTree(context);

            CompileAndBindDelegates(context);

            context.Methods = MethodFilter.Apply(context.Methods, methodFilters);

            _compiledAssembly = context.CompiledAssembly;
            return context.Methods;
        }

        private static void ParseSyntaxTree(ExtractionContext context)
        {
            context.SyntaxTree = CSharpSyntaxTree.ParseText(context.SourceCode);
            context.Root = context.SyntaxTree.GetRoot();
        }

        private static void BuildSemanticModel(ExtractionContext context)
        {
            try
            {
                var buildResult = CompilationSkeleton.Build(context.SourceCode);
                if (buildResult.Success)
                {
                    context.SemanticModel = buildResult.SemanticModel;
                    context.SyntaxTree = buildResult.SyntaxTree;
                    context.Root = buildResult.Root;
                }
            }
            catch
            {
            }
        }

        private void ExtractMethodsFromTree(ExtractionContext context)
        {
            var root = context.Root ?? throw new InvalidOperationException("Syntax tree not parsed");
            var className = context.ClassName;

            var classDecl = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == className
                    || c.Identifier.Text == GetClassNameFromFullPath(className));

            if (classDecl == null)
                throw new InvalidOperationException($"Class '{className}' not found in source code");

            var nestedClassDecls = classDecl.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(n => n.Parent == classDecl)
                .ToList();

            ExtractMethodsFromClass(classDecl, null, context);

            foreach (var nested in nestedClassDecls)
            {
                ExtractMethodsFromClass(nested, nested.Identifier.Text, context);
            }
        }

        private void ExtractMethodsFromClass(ClassDeclarationSyntax classDecl, string? nestedClassName, ExtractionContext context)
        {
            var methods = classDecl.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Parent == classDecl);

            foreach (var method in methods)
            {
                try
                {
                    var extracted = ConvertFromSyntax(method, classDecl.Identifier.Text, nestedClassName, context.SemanticModel);
                    if (extracted != null)
                        context.Methods.Add(extracted);
                }
                catch
                {
                }
            }
        }

        private ExtractedMethodInfo? ConvertFromSyntax(
            MethodDeclarationSyntax method,
            string className,
            string? nestedClassName,
            SemanticModel? semanticModel)
        {
            try
            {
                var methodName = method.Identifier.Text;

                var returnType = ResolveTypeFromSemantic(method.ReturnType, semanticModel)
                    ?? TypeHelper.ResolveType(method.ReturnType.ToString());

                var parameters = method.ParameterList.Parameters.Select(p =>
                {
                    var paramType = p.Type != null
                        ? (ResolveTypeFromSemantic(p.Type, semanticModel)
                           ?? TypeHelper.ResolveType(p.Type.ToString()))
                        : typeof(object);

                    var hasDefault = p.Default != null;
                    return new ExtractedMethodInfo.ParameterInfo
                    {
                        Name = p.Identifier.Text,
                        Type = paramType,
                        IsOptional = hasDefault,
                        DefaultValue = hasDefault ? p.Default?.ToString() : null
                    };
                }).ToArray();

                var description = ExtractXmlDocFromSyntax(method);

                var isStatic = method.Modifiers.Any(m => m.Text == "static");

                var isAsync = method.Modifiers.Any(m => m.Text == "async");

                return new ExtractedMethodInfo
                {
                    MethodName = methodName,
                    ContainingClassName = className,
                    ContainingNestedClassName = nestedClassName,
                    ReturnType = returnType,
                    Parameters = parameters,
                    Description = description,
                    IsStatic = isStatic,
                    IsAsync = isAsync,
                    Executor = null
                };
            }
            catch
            {
                return null;
            }
        }

        private static Type? ResolveTypeFromSemantic(TypeSyntax typeSyntax, SemanticModel? semanticModel)
        {
            if (semanticModel == null || typeSyntax == null)
                return null;

            try
            {
                var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
                if (typeInfo.Type == null)
                    return null;

                var typeSymbol = typeInfo.Type;
                var fullyQualifiedName = typeSymbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat);

                var resolvedType = Type.GetType(fullyQualifiedName, throwOnError: false);
                if (resolvedType != null)
                    return resolvedType;

                if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
                {
                    var openGenericName = namedType.ConstructUnboundGenericType()
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    resolvedType = Type.GetType(openGenericName, throwOnError: false);
                    if (resolvedType != null)
                        return resolvedType;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void CompileAndBindDelegates(ExtractionContext context)
        {
            try
            {
                context.CompiledAssembly = CompileToAssembly(context.SourceCode);

                if (context.CompiledAssembly == null)
                {
                    context.Bag["CompileError"] = "CompileToAssembly returned null";
                    return;
                }

                foreach (var method in context.Methods)
                {
                    try
                    {
                        var methodInfo = context.CompiledAssembly.FindMethodInfo(method);
                        if (methodInfo == null)
                        {
                            context.Bag[$"BindError_{method.MethodName}"] =
                                $"MethodInfo not found for {method.MethodName}";
                            continue;
                        }

                        method.Executor = _delegateFactory.CreateExecutor(methodInfo);
                    }
                    catch (Exception ex)
                    {
                        context.Bag[$"BindError_{method.MethodName}"] =
                            $"Failed to bind executor for {method.MethodName}: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                context.Bag["CompileError"] = $"Compilation failed: {ex.Message}";
            }
        }

        private static Assembly? CompileToAssembly(string sourceCode)
        {
            try
            {
                var assembly = DynamicCompilation.CompileAssembly(sourceCode)
                                                 .GetAwaiter().GetResult();
                return assembly;
            }
            catch
            {
                return null;
            }
        }

        private string ExtractXmlDocFromSyntax(MethodDeclarationSyntax method)
        {
            try
            {
                var trivia = method.GetLeadingTrivia()
                    .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                                      || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

                if (trivia.IsKind(SyntaxKind.None))

                    return string.Empty;

                var xml = trivia.GetStructure()?.ToString();
                if (string.IsNullOrEmpty(xml))
                    return string.Empty;

                var summaryStart = xml.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
                var summaryEnd = xml.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);
                if (summaryStart >= 0 && summaryEnd > summaryStart)
                {
                    var summary = xml.Substring(summaryStart + 9, summaryEnd - summaryStart - 9);
                    return summary.Trim().Trim('/', '*', ' ');
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetClassNameFromFullPath(string fullPath)
        {
            var lastDot = fullPath.LastIndexOf('.');
            return lastDot >= 0 ? fullPath.Substring(lastDot + 1) : fullPath;
        }

        public Assembly? GetCompiledAssembly() => _compiledAssembly;
    }
}


namespace Modules.Registry.AutoRegister.Delegate

{
    public interface IDelegateFactory
    {
        DelegateType CreateDelegate(MethodInfo method);

        Func<object[], Task<object>> CompileExecutor(DelegateType del);

        Func<object[], Task<object>> CreateExecutor(MethodInfo method);
    }

    public class DelegateFactory : IDelegateFactory
    {
        public DelegateType CreateDelegate(MethodInfo method)
        {
            var parameters = method.GetParameters();
            var paramTypes = parameters.Select(p => p.ParameterType).ToArray();

            var argsParam = Expression.Parameter(typeof(object[]), "args");

            var callArgs = new Expression[paramTypes.Length];
            for (int i = 0; i < paramTypes.Length; i++)
            {
                var indexExpr = Expression.Constant(i);
                var argExpr = Expression.ArrayIndex(argsParam, indexExpr);
                callArgs[i] = Expression.Convert(argExpr, paramTypes[i]);
            }

            if (method.IsStatic)
            {
                var callExpr = BuildCallExpression(null, method, callArgs);
                var lambda = Expression.Lambda<Func<object[], object>>(
                    Expression.Convert(callExpr, typeof(object)),
                    argsParam);
                return lambda.Compile();
            }
            else
            {
                var instanceParam = Expression.Parameter(typeof(object), "instance");
                var callExpr = BuildCallExpression(instanceParam, method, callArgs);
                var lambda = Expression.Lambda<Func<object, object[], object>>(
                    Expression.Convert(callExpr, typeof(object)),
                    instanceParam, argsParam);
                return lambda.Compile();
            }
        }

        private static Expression BuildCallExpression(Expression? instanceParam, MethodInfo method, Expression[] callArgs)
        {
            Expression callExpr;
            if (method.IsStatic)
            {
                callExpr = Expression.Call(method, callArgs);
            }
            else
            {
                var instanceExpr = Expression.Convert(instanceParam!, method.DeclaringType!);
                callExpr = Expression.Call(instanceExpr, method, callArgs);
            }

            if (method.ReturnType.IsGenericType &&
                method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var getAwaiterMethod = method.ReturnType.GetMethod("GetAwaiter");
                if (getAwaiterMethod != null)
                {
                    var awaiterType = getAwaiterMethod.ReturnType;
                    var getResultMethod = awaiterType.GetMethod("GetResult");
                    if (getResultMethod != null)
                    {
                        var awaiterExpr = Expression.Call(callExpr, getAwaiterMethod);
                        callExpr = Expression.Call(awaiterExpr, getResultMethod);
                    }
                }
            }

            return callExpr;
        }

        public Func<object[], Task<object>> CompileExecutor(DelegateType del)
        {
            if (del is Func<object[], object> syncStaticFunc)
            {
                return args =>
                {
                    try
                    {
                        return Task.FromResult(syncStaticFunc(args));
                    }
                    catch (Exception ex)
                    {
                        return Task.FromException<object>(ex);
                    }
                };
            }

            if (del is Func<object, object[], object> instanceFunc)
            {
                return args =>
                {
                    try
                    {
                        if (args.Length == 0)
                            return Task.FromException<object>(new ArgumentException("实例方法调用需要至少一个参数（实例）"));
                        var instance = args[0];
                        var methodArgs = args.Length > 1 ? args[1..] : Array.Empty<object>();
                        return Task.FromResult(instanceFunc(instance, methodArgs));
                    }
                    catch (Exception ex)
                    {
                        return Task.FromException<object>(ex);
                    }
                };
            }

            throw new InvalidOperationException($"不支持的委托类型: {del.GetType()}");
        }

        public Func<object[], Task<object>> CreateExecutor(MethodInfo method)
        {
            var del = CreateDelegate(method);
            return CompileExecutor(del);
        }
    }
}
