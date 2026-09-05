using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Security
{
    public interface ISecurityRule
    {
        string Id { get; }

        string Title { get; }

        string Description { get; }

        void Analyze(SyntaxNode node, SemanticModel model, List<SecurityIssue> issues);
    }
}
