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

            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetRoot();

            progress?.Report(new AnalysisProgress(33, 100, "Comment: Step 2/3 - Building compilation..."));

            var compilation = CSharpCompilation.Create(
                $"CommentGeneration_{Guid.NewGuid():N}",
                new[] { syntaxTree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

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
