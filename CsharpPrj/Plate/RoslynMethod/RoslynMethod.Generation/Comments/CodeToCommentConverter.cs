using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public class CodeToCommentConverter
    {
        private readonly List<ICommentProvider> _providers = new();
        private readonly CommentGenerationOptions _options;

        public CodeToCommentConverter(CommentGenerationOptions options = null)
        {
            _options = options ?? new CommentGenerationOptions();
        }

        public void AddProvider(ICommentProvider provider) => _providers.Add(provider);

        private static MetadataReference[] GetStandardReferences()
        {
            var assemblies = new[]
            {
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Attribute).Assembly,
                typeof(Stack<>).Assembly,
                typeof(ValueTuple).Assembly,
                typeof(Console).Assembly,
                Assembly.Load("netstandard"),
                Assembly.Load("System.Runtime"),
                Assembly.Load("System.Collections"),
                Assembly.Load("System.Linq"),
                Assembly.Load("System.Threading.Tasks"),
                Assembly.Load("System.Text.RegularExpressions")
            };

            return assemblies
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .ToArray();
        }

        public string Convert(string code, CancellationToken cancellationToken = default)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var compilation = CSharpCompilation.Create("TempAssembly",
                new[] { syntaxTree },
                GetStandardReferences());
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            var rewriter = new CommentAddingRewriter(semanticModel, _providers, _options);
            var newRoot = rewriter.Visit(root);
            return newRoot.NormalizeWhitespace().ToFullString();
        }
    }
}
