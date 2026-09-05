using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeRLFramework.Generation;
using CodeRLFramework.Pipeline;
using CodeRLFramework.VectorSpace;

namespace CodeRLFramework.Output
{
    public class VectorToCodeConverter
    {
        private readonly FunctionVectorSpace _functionSpace;
        private readonly CodeTokenizer _tokenizer;

        public VectorToCodeConverter(
            FunctionVectorSpace functionSpace,
            CodeTokenizer tokenizer)
        {
            _functionSpace = functionSpace ?? throw new ArgumentNullException(nameof(functionSpace));
            _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        }

        public string Convert(OutputVectorSpace outputSpace, string templateCode = "")
        {
            var functionCalls = DecodeOutputVectors(outputSpace);

            var code = GenerateCode(functionCalls, templateCode);

            code = OptimizeCode(code);

            return code;
        }

        private List<FunctionCall> DecodeOutputVectors(OutputVectorSpace outputSpace)
        {
            var calls = new List<FunctionCall>();

            foreach (var vector in outputSpace.Vectors)
            {
                if (vector.Vector == null) continue;

                var matches = _functionSpace.SearchByVector(vector.Vector, topK: 1);
                if (matches.Count > 0)
                {
                    var (bestMatch, similarity) = matches[0];
                    calls.Add(new FunctionCall
                    {
                        FunctionId = bestMatch.Id,
                        FunctionName = bestMatch.Name,
                        Parameters = bestMatch.Parameters
                            .Select(p => new ParameterBinding
                            {
                                Name = p.Name,
                                Type = p.Type,
                                Value = ResolveParameterValue(p, vector)
                            }).ToList(),
                        Confidence = similarity
                    });
                }
            }

            return calls;
        }

        private string GenerateCode(
            List<FunctionCall> calls,
            string templateCode)
        {
            if (!string.IsNullOrEmpty(templateCode))
            {
                return GenerateWithTemplate(calls, templateCode);
            }

            return GenerateStandaloneMethod(calls);
        }

        private string GenerateWithTemplate(
            List<FunctionCall> calls,
            string templateCode)
        {
            var tree = CSharpSyntaxTree.ParseText(templateCode);
            var root = tree.GetRoot();

            var rewriter = new CodeInsertionRewriter(calls);
            var newRoot = rewriter.Visit(root);

            return newRoot.NormalizeWhitespace().ToFullString();
        }

        private string GenerateStandaloneMethod(List<FunctionCall> calls)
        {
            if (calls.Count == 0)
                return "// 未生成任何函数调用";

            var classDeclaration = SyntaxFactory.ClassDeclaration("GeneratedClass")
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));

            var methodDeclaration = SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                    SyntaxFactory.Identifier("GeneratedMethod"))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithBody(SyntaxFactory.Block());

            foreach (var call in calls)
            {
                var invocation = CreateInvocationExpression(call);
                var statement = SyntaxFactory.ExpressionStatement(invocation);
                methodDeclaration = methodDeclaration.WithBody(
                    methodDeclaration.Body!.AddStatements(statement));
            }

            classDeclaration = classDeclaration.AddMembers(methodDeclaration);

            var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(
                    SyntaxFactory.ParseName("GeneratedCode"))
                .AddMembers(classDeclaration);

            return namespaceDeclaration.NormalizeWhitespace().ToFullString();
        }

        private static InvocationExpressionSyntax CreateInvocationExpression(FunctionCall call)
        {
            var arguments = call.Parameters
                .Select(p => SyntaxFactory.Argument(
                    SyntaxFactory.IdentifierName(p.Value ?? p.Name)))
                .ToList();

            return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName(call.FunctionName))
                .WithArgumentList(SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(arguments)));
        }

        private static string ResolveParameterValue(ParameterInfo param, OutputVector vector)
        {
            if (vector.ParameterBindings.TryGetValue(param.Name, out var value))
                return value;
            return param.Name;
        }

        private static string OptimizeCode(string code)
        {
            try
            {
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();
                return root.NormalizeWhitespace().ToFullString();
            }
            catch
            {
                return code;
            }
        }
    }

    public class FunctionCall
    {
        public string FunctionId { get; set; } = string.Empty;
        public string FunctionName { get; set; } = string.Empty;
        public List<ParameterBinding> Parameters { get; set; } = new();
        public double Confidence { get; set; }
    }

    public class ParameterBinding
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
