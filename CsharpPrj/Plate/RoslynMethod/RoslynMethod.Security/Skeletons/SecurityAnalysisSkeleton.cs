using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Core.Models;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public static class SecurityAnalysisSkeleton
    {
        public static SecurityCheckResult Analyze(
            string code,
            IReadOnlyList<ISecurityRule> rules = null,
            IProgress<AnalysisProgress> progress = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Code cannot be null or empty", nameof(code));

            rules ??= SecurityRuleRegistry.GetDefaultRules();

            progress?.Report(new AnalysisProgress(0, 100, "Security: Step 1/4 - Parsing syntax tree..."));

            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetRoot();

            progress?.Report(new AnalysisProgress(25, 100, "Security: Step 2/4 - Building compilation..."));

            var config = new CompilationSkeleton.CompilationConfig
            {
                AssemblyName = $"SecurityAnalysis_{Guid.NewGuid():N}",
                OptimizationLevel = OptimizationLevel.Debug,
                CheckOverflow = false
            };
            var buildResult = CompilationSkeleton.Build(code, config);
            var compilation = buildResult.Compilation;
            var semanticModel = buildResult.SemanticModel;

            progress?.Report(new AnalysisProgress(50, 100, "Security: Step 3/4 - Applying security rules..."));

            var issues = new ConcurrentBag<SecurityIssue>();
            Parallel.ForEach(root.DescendantNodes(), node =>
            {
                foreach (var rule in rules)
                {
                    var ruleIssues = new List<SecurityIssue>();
                    rule.Analyze(node, semanticModel, ruleIssues);
                    foreach (var issue in ruleIssues)
                        issues.Add(issue);
                }
            });

            progress?.Report(new AnalysisProgress(75, 100, "Security: Step 4/4 - Analyzing compilation errors..."));

            var compilationErrors = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d =>
                {
                    int? line = null;
                    int? column = null;
                    if (d.Location.GetLineSpan().IsValid)
                    {
                        var span = d.Location.GetLineSpan();
                        line = span.StartLinePosition.Line + 1;
                        column = span.StartLinePosition.Character + 1;
                    }
                    return new CompilationError(d.Id, d.GetMessage(), line, column, ErrorSeverity.CompilationError);
                })
                .ToList();

            var issueCountBySeverity = issues
                .GroupBy(i => i.Severity)
                .ToDictionary(g => g.Key, g => g.Count());

            progress?.Report(new AnalysisProgress(100, 100, "Security: Complete"));

            return new SecurityCheckResult
            {
                IsClean = !issues.Any() && !compilationErrors.Any(),
                Issues = issues.ToList(),
                CompilationErrors = compilationErrors,
                TotalIssues = issues.Count,
                TotalErrors = compilationErrors.Count,
                IssueCountBySeverity = issueCountBySeverity
            };
        }

        public static Task<SecurityCheckResult> AnalyzeAsync(
            string code,
            IReadOnlyList<ISecurityRule> rules = null,
            IProgress<AnalysisProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Analyze(code, rules, progress);
            }, cancellationToken);
        }
    }
}
