using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    internal class CommentAddingRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly IEnumerable<ICommentProvider> _providers;
        private readonly CommentGenerationOptions _options;

        public CommentAddingRewriter(
            SemanticModel semanticModel,
            IEnumerable<ICommentProvider> providers,
            CommentGenerationOptions options)
        {
            _semanticModel = semanticModel;
            _providers = providers;
            _options = options;
        }

        public override SyntaxNode Visit(SyntaxNode node)
        {
            if (node == null)
                return null;

            node = base.Visit(node);

            if (_options.SkipIfExistingComment && HasAnyComment(node))
                return node;

            foreach (var provider in _providers)
            {
                var comment = provider.GenerateComment(node, _semanticModel, _options.DefaultStyle);
                if (comment != null)
                {
                    var newLeadingTrivia = CreateCommentTrivia(comment, _options.DefaultStyle);
                    var existingLeading = node.GetLeadingTrivia();
                    node = node.WithLeadingTrivia(newLeadingTrivia.Concat(existingLeading));
                    break;
                }
            }

            return node;
        }

        private static bool HasAnyComment(SyntaxNode node)
        {
            var trivia = node.GetLeadingTrivia();
            return trivia.Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                                   t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                                   t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                   t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
        }

        private static SyntaxTriviaList CreateCommentTrivia(string comment, CommentStyle style)
        {
            var list = new SyntaxTriviaList();
            switch (style)
            {
                case CommentStyle.SingleLine:
                    var lines = comment.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        list = list.Add(SyntaxFactory.Comment($"// {line}"));
                        list = list.Add(SyntaxFactory.CarriageReturnLineFeed);
                    }
                    break;

                case CommentStyle.MultiLine:
                    list = list.Add(SyntaxFactory.Comment($"/* {comment} */"));
                    list = list.Add(SyntaxFactory.CarriageReturnLineFeed);
                    break;

                case CommentStyle.Documentation:
                    var xmlNodes = new List<XmlNodeSyntax>();
                    var docLines = comment.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
                    for (int i = 0; i < docLines.Length; i++)
                    {
                        xmlNodes.Add(SyntaxFactory.XmlText(" " + docLines[i]));
                        if (i < docLines.Length - 1)
                            xmlNodes.Add(SyntaxFactory.XmlText("\r\n"));
                    }
                    var docComment = SyntaxFactory.DocumentationCommentTrivia(
                        SyntaxKind.SingleLineDocumentationCommentTrivia,
                        SyntaxFactory.List(xmlNodes));
                    var docTrivia = SyntaxFactory.Trivia(docComment);
                    list = list.Add(docTrivia);
                    list = list.Add(SyntaxFactory.CarriageReturnLineFeed);
                    break;
            }
            return list;
        }
    }
}
