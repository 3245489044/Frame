using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynStringRefactoringFramework
{
    #region 代码重构上下文

    public class CodeRefactoringContext
    {
        public string SourceCode { get; set; }
        public string ProjectName { get; set; }
        public List<MetadataReference> References { get; }

        public CodeRefactoringContext(string sourceCode)
        {
            SourceCode = sourceCode ?? throw new ArgumentNullException(nameof(sourceCode));
            ProjectName = "TempProject";
            References = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)
        };
        }

    }

    public class RefactoringResult
    {
        public bool Success { get; set; }
        public string TransformedCode { get; set; }
        public List<string> AppliedRefactorings { get; set; }
        public List<Diagnostic> Diagnostics { get; set; }
        public Exception Error { get; set; }

        public RefactoringResult()
        {
            AppliedRefactorings = new List<string>();
            Diagnostics = new List<Diagnostic>();
        }

        public static RefactoringResult SuccessResult(string code, List<string> refactorings) => new()
        {
            Success = true,
            TransformedCode = code,
            AppliedRefactorings = refactorings
        };

        public static RefactoringResult ErrorResult(Exception ex) => new()
        {
            Success = false,
            Error = ex
        };
    }
    #endregion

    #region 重构规则系统

    public abstract class RefactoringRule
    {
        public string Name { get; protected set; }
        public int Priority { get; protected set; } = 100;

        protected RefactoringRule(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));

        public virtual bool ShouldApply(SyntaxNode root, SemanticModel semanticModel) => true;
        public abstract SyntaxNode Transform(SyntaxNode root, SemanticModel semanticModel);
    }
    #endregion

    #region 缓存管理器

    public class RoslynCacheManager
    {
        private readonly Dictionary<string, (SyntaxTree, Compilation, DateTime)> _cache = new();
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        public (SyntaxTree, Compilation, SemanticModel) GetOrCreateAnalysis(string code, List<MetadataReference> references, string projectName = "TempProject")
        {
            var cacheKey = $"{projectName}_{code}";

            if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.Now - cached.Item3 < _cacheDuration)
            {
                var semanticModel = cached.Item2.GetSemanticModel(cached.Item1);
                return (cached.Item1, cached.Item2, semanticModel);
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create(projectName, new[] { syntaxTree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            _cache[cacheKey] = (syntaxTree, compilation, DateTime.Now);
            var newSemanticModel = compilation.GetSemanticModel(syntaxTree);
            return (syntaxTree, compilation, newSemanticModel);
        }

        public void ClearExpiredCache()
        {
            var expiredKeys = _cache.Where(kv => DateTime.Now - kv.Value.Item3 > _cacheDuration).Select(kv => kv.Key).ToList();
            expiredKeys.ForEach(key => _cache.Remove(key));
        }
    }
    #endregion

    #region 重构框架核心

    public class RoslynRefactoringFramework
    {
        private readonly RoslynCacheManager _cacheManager = new();
        private readonly List<RefactoringRule> _rules = new();

        public RoslynRefactoringFramework AddRule(RefactoringRule rule)
        {
            _rules.Add(rule);
            return this;
        }

        public async Task<RefactoringResult> RefactorAsync(CodeRefactoringContext context)
        {
            if (!ValidateCodeSafety(context.SourceCode))
                return RefactoringResult.ErrorResult(new InvalidOperationException("Code failed safety validation"));

            try
            {
                var analysisResult = await Task.Run(() => _cacheManager.GetOrCreateAnalysis(context.SourceCode, context.References, context.ProjectName));
                var syntaxTree = analysisResult.Item1;
                var semanticModel = analysisResult.Item3;
                var root = await syntaxTree.GetRootAsync();
                var diagnostics = semanticModel.GetDiagnostics().ToList();

                var applicableRules = _rules.Where(r => r.ShouldApply(root, semanticModel)).OrderBy(r => r.Priority).ToList();
                if (applicableRules.Count == 0)
                    return RefactoringResult.SuccessResult(context.SourceCode, new List<string>());

                var transformedRoot = root;
                foreach (var rule in applicableRules)
                    transformedRoot = rule.Transform(transformedRoot, semanticModel);

                return RefactoringResult.SuccessResult(transformedRoot.ToFullString(), applicableRules.Select(r => r.Name).ToList());
            }
            catch (Exception ex)
            {
                _cacheManager.ClearExpiredCache();
                return RefactoringResult.ErrorResult(ex);
            }
        }

        private static bool ValidateCodeSafety(string code)
        {
            if (code.Length > 100000) return false;
            var dangerousPatterns = new[] { "System.IO.File.Delete", "System.Diagnostics.Process.Start", "Environment.Exit", "Assembly.Load" };
            return dangerousPatterns.All(pattern => code.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0);
        }
    }
    #endregion

    #region 内置重构规则

    public class UseVarKeywordRule : RefactoringRule
    {
        public UseVarKeywordRule() : base("UseVarKeyword") { Priority = 50; }

        public override bool ShouldApply(SyntaxNode root, SemanticModel semanticModel) =>
            QuerySkeleton.FindAllNodes<LocalDeclarationStatementSyntax>(root).Any(decl => !decl.Declaration.Type.IsVar);

        public override SyntaxNode Transform(SyntaxNode root, SemanticModel semanticModel) => new UseVarRewriter(semanticModel).Visit(root);

        private class UseVarRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _semanticModel;
            public UseVarRewriter(SemanticModel semanticModel) => _semanticModel = semanticModel;

            public override SyntaxNode VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
            {
                if (node.Declaration.Variables.Count == 1 && !node.Declaration.Type.IsVar)
                {
                    var variable = node.Declaration.Variables[0];
                    var typeInfo = _semanticModel.GetTypeInfo(node.Declaration.Type);
                    if (variable.Initializer != null && typeInfo.Type != null && !typeInfo.Type.IsAnonymousType)
                    {
                        var varKeyword = SyntaxFactory.IdentifierName("var")
                            .WithLeadingTrivia(node.Declaration.Type.GetLeadingTrivia())
                            .WithTrailingTrivia(node.Declaration.Type.GetTrailingTrivia());
                        var newDeclaration = node.Declaration.WithType(varKeyword);
                        return node.WithDeclaration(newDeclaration);
                    }
                }
                return base.VisitLocalDeclarationStatement(node);
            }
        }
    }

    public class SimplifyLinqRule : RefactoringRule
    {
        public SimplifyLinqRule() : base("SimplifyLinq") { Priority = 100; }
        public override SyntaxNode Transform(SyntaxNode root, SemanticModel semanticModel) => new LinqSimplifier().Visit(root);

        private class LinqSimplifier : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (node.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Name.Identifier.Text == "FirstOrDefault")
                {
                    var prevInvocation = memberAccess.Expression as InvocationExpressionSyntax;
                    if (prevInvocation?.Expression is MemberAccessExpressionSyntax prevMember && prevMember.Name.Identifier.Text == "Where")
                    {
                        var lambda = prevInvocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                        if (lambda != null)
                        {
                            var newArguments = SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(lambda)));
                            var newInvocation = SyntaxFactory.InvocationExpression(memberAccess.WithExpression(prevMember.Expression), newArguments);
                            return base.VisitInvocationExpression(newInvocation);
                        }
                    }
                }
                return base.VisitInvocationExpression(node);
            }
        }
    }

    public class SmartRoleSwapRule : RefactoringRule
    {
        public SmartRoleSwapRule() : base("SmartRoleSwap") { Priority = 10; }

        public static bool ShouldSwap(string targetCode, string codeToInsert)
        {
            if (string.IsNullOrWhiteSpace(targetCode) || string.IsNullOrWhiteSpace(codeToInsert))
                return false;

            var targetRoot = CSharpSyntaxTree.ParseText(targetCode).GetCompilationUnitRoot();
            var insertRoot = CSharpSyntaxTree.ParseText(codeToInsert).GetCompilationUnitRoot();

            var targetHasClass = targetRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Any();
            var insertHasClass = insertRoot.DescendantNodes().OfType<ClassDeclarationSyntax>().Any();

            return !targetHasClass && insertHasClass;
        }

        public override bool ShouldApply(SyntaxNode root, SemanticModel semanticModel) => true;

        public override SyntaxNode Transform(SyntaxNode root, SemanticModel semanticModel)
        {
            return root;
        }
    }

    public class AddNullChecksRule : RefactoringRule

    {
        public AddNullChecksRule() : base("AddNullChecks") { Priority = 150; }

        public override SyntaxNode Transform(SyntaxNode root, SemanticModel semanticModel)
        {
            var methods = QuerySkeleton.FindAllNodes<MethodDeclarationSyntax>(root).Where(m => m.ParameterList.Parameters.Count > 0);
            foreach (var method in methods)
            {
                if (method.Body == null) continue;

                var newBody = AddParameterNullChecks(method, semanticModel);
                if (newBody != null)
                    root = root.ReplaceNode(method.Body, newBody);
            }
            return root;
        }

        private static BlockSyntax AddParameterNullChecks(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            var statements = new List<StatementSyntax>();
            foreach (var parameter in method.ParameterList.Parameters)
            {
                var paramSymbol = semanticModel.GetDeclaredSymbol(parameter);
                if (paramSymbol != null && paramSymbol.Type.IsReferenceType && paramSymbol.Type.SpecialType != SpecialType.System_String)
                {
                    var nullCheck = SyntaxFactory.IfStatement(
                        SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression,
                            SyntaxFactory.IdentifierName(parameter.Identifier),
                            SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
                        SyntaxFactory.ThrowStatement(SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.IdentifierName("ArgumentNullException")).WithArgumentList(
                                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(parameter.Identifier.Text))))))));
                    statements.Add(nullCheck);
                }
            }

            if (statements.Count > 0 && method.Body != null)
            {
                var existingStatements = method.Body.Statements.ToList();
                statements.AddRange(existingStatements);
                return method.Body.WithStatements(SyntaxFactory.List<StatementSyntax>(statements));
            }
            return null;
        }
    }
    #endregion
}
