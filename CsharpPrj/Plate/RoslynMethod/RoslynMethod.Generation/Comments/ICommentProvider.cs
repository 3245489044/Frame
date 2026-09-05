using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSpace.RoslynSyntaxUtilities.Generation
{
    public interface ICommentProvider
    {
        string GenerateComment(SyntaxNode node, SemanticModel semanticModel, CommentStyle style);
    }
}
