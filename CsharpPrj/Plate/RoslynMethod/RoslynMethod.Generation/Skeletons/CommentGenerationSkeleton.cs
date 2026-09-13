using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public static class CommentGenerationSkeleton
    {
        public static string Generate(
            string code,
            IEnumerable<ICommentProvider> providers,
            CommentGenerationOptions options = null,
            IProgress<AnalysisProgress> progress = null)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Code cannot be null or empty", nameof(code));

            options ??= new CommentGenerationOptions();
            progress?.Report(new AnalysisProgress(0, 100, "Comment: Step 1/3 - Parsing code..."));

            var build = CompilationSkeleton.BuildOrThrow(code, new CompilationSkeleton.CompilationConfig
            {
                AssemblyName = $"CommentGeneration_{Guid.NewGuid():N}",
                AutoResolveReferences = false,
                CollectDiagnostics = false,
                AdditionalReferences = { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }
            });
            var root = build.Root;

            progress?.Report(new AnalysisProgress(33, 100, "Comment: Step 2/3 - Building compilation..."));

            var semanticModel = build.SemanticModel;

            progress?.Report(new AnalysisProgress(66, 100, "Comment: Step 3/3 - Adding comments..."));

            var rewriter = new CommentAddingRewriter(semanticModel, providers, options);
            var newRoot = rewriter.Visit(root);

            progress?.Report(new AnalysisProgress(100, 100, "Comment: Complete"));

            return newRoot.NormalizeWhitespace().ToFullString();
        }

        public static Task<string> GenerateAsync(
            string code,
            IEnumerable<ICommentProvider> providers,
            CommentGenerationOptions options = null,
            IProgress<AnalysisProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Generate(code, providers, options, progress);
            }, cancellationToken);
        }
    }
}
