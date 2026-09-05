using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using RoslynSpace.RoslynSyntaxUtilities.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Analysis
{
    public static class CompilationValidationSkeleton
    {
        public static CompilationValidationResult Validate(
            string code,
            IProgress<AnalysisProgress> progress = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Code cannot be null or empty", nameof(code));

            progress?.Report(new AnalysisProgress(0, 100, "Validation: Step 1/4 - Parsing code..."));

            var config = new CompilationSkeleton.CompilationConfig
            {
                AssemblyName = $"Validation_{Guid.NewGuid():N}"
            };

            progress?.Report(new AnalysisProgress(25, 100, "Validation: Step 2/4 - Building compilation..."));

            var buildResult = CompilationSkeleton.Build(code, config);

            progress?.Report(new AnalysisProgress(50, 100, "Validation: Step 3/4 - Analyzing errors..."));

            var errors = CompilationErrorAnalyzer.Analyze(buildResult.Compilation);

            progress?.Report(new AnalysisProgress(75, 100, "Validation: Step 4/4 - Generating suggestions..."));

            var suggestions = CompilationSuggestionService.GenerateSuggestions(errors);

            progress?.Report(new AnalysisProgress(100, 100, "Validation: Complete"));

            return new CompilationValidationResult
            {
                Errors = errors,
                Suggestions = suggestions,
                IsValid = !errors.Any()
            };
        }

        public static CompilationValidationResult ValidateSnippet(
            string snippet,
            IProgress<AnalysisProgress> progress = null)
        {
            if (string.IsNullOrWhiteSpace(snippet))
                throw new ArgumentException("Snippet cannot be null or empty", nameof(snippet));

            var wrappedCode = WrapSnippet(snippet);
            var result = Validate(wrappedCode, progress);
            return result;
        }

        public static Task<CompilationValidationResult> ValidateAsync(
            string code,
            IProgress<AnalysisProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Validate(code, progress);
            }, cancellationToken);
        }

        public static Task<CompilationValidationResult> ValidateSnippetAsync(
            string snippet,
            IProgress<AnalysisProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValidateSnippet(snippet, progress);
            }, cancellationToken);
        }

        private static string WrapSnippet(string snippet)
        {
            return $@"
using System;
using System.Collections.Generic;
using System.Linq;

public class SnippetWrapper
{{
    public void Execute()
    {{
        {snippet}
    }}
}}";
        }
    }
}
