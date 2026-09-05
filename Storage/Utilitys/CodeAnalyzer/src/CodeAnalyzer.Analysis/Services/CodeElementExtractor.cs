using CodeAnalyzer.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeAnalyzer.Analysis.Services;

public class CodeElementExtractor
{
    private readonly AnalysisContext _context;

    public CodeElementExtractor(AnalysisContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public List<EnhancedCodeElement> ExtractElements()
    {
        var elements = new List<EnhancedCodeElement>();
        if (_context.Root == null) return elements;

        var root = _context.Root;
        var semanticModel = _context.SemanticModel;

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var element = CreateElementFromClass(classDecl, semanticModel);
            if (element != null) elements.Add(element);
        }

        foreach (var structDecl in root.DescendantNodes().OfType<StructDeclarationSyntax>())
        {
            var element = CreateElementFromStruct(structDecl, semanticModel);
            if (element != null) elements.Add(element);
        }

        foreach (var interfaceDecl in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
        {
            var element = CreateElementFromInterface(interfaceDecl, semanticModel);
            if (element != null) elements.Add(element);
        }

        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var element = CreateElementFromMethod(methodDecl, semanticModel);
            if (element != null) elements.Add(element);
        }

        foreach (var propDecl in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            var element = CreateElementFromProperty(propDecl, semanticModel);
            if (element != null) elements.Add(element);
        }

        return elements;
    }

    private EnhancedCodeElement? CreateElementFromClass(ClassDeclarationSyntax classDecl, SemanticModel? semanticModel)
    {
        var lineSpan = classDecl.GetLocation().GetLineSpan();
        var symbol = semanticModel?.GetDeclaredSymbol(classDecl);

        return new EnhancedCodeElement
        {
            Id = Guid.NewGuid().ToString(),
            Name = classDecl.Identifier.Text,
            Type = ElementType.Class,
            LineNumber = lineSpan.StartLinePosition.Line + 1,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            ContainingType = "",
            Accessibility = symbol?.DeclaredAccessibility.ToString() ?? "Public",
            IsStatic = symbol?.IsStatic ?? false,
            IsAbstract = symbol?.IsAbstract ?? false,
            LinesOfCode = CountLines(classDecl.Span),
            CommentLines = CountCommentLines(classDecl)
        };
    }

    private EnhancedCodeElement? CreateElementFromStruct(StructDeclarationSyntax structDecl, SemanticModel? semanticModel)
    {
        var lineSpan = structDecl.GetLocation().GetLineSpan();
        return new EnhancedCodeElement
        {
            Id = Guid.NewGuid().ToString(),
            Name = structDecl.Identifier.Text,
            Type = ElementType.Struct,
            LineNumber = lineSpan.StartLinePosition.Line + 1,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            LinesOfCode = CountLines(structDecl.Span),
            CommentLines = CountCommentLines(structDecl)
        };
    }

    private EnhancedCodeElement? CreateElementFromInterface(InterfaceDeclarationSyntax interfaceDecl, SemanticModel? semanticModel)
    {
        var lineSpan = interfaceDecl.GetLocation().GetLineSpan();
        return new EnhancedCodeElement
        {
            Id = Guid.NewGuid().ToString(),
            Name = interfaceDecl.Identifier.Text,
            Type = ElementType.Interface,
            LineNumber = lineSpan.StartLinePosition.Line + 1,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            LinesOfCode = CountLines(interfaceDecl.Span),
            CommentLines = CountCommentLines(interfaceDecl)
        };
    }

    private EnhancedCodeElement? CreateElementFromMethod(MethodDeclarationSyntax methodDecl, SemanticModel? semanticModel)
    {
        var lineSpan = methodDecl.GetLocation().GetLineSpan();
        var symbol = semanticModel?.GetDeclaredSymbol(methodDecl);

        var element = new EnhancedCodeElement
        {
            Id = Guid.NewGuid().ToString(),
            Name = methodDecl.Identifier.Text,
            Type = ElementType.Method,
            LineNumber = lineSpan.StartLinePosition.Line + 1,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            ContainingType = GetContainingTypeName(methodDecl),
            ReturnType = methodDecl.ReturnType.ToString(),
            Accessibility = symbol?.DeclaredAccessibility.ToString() ?? "Public",
            IsStatic = symbol?.IsStatic ?? false,
            IsVirtual = symbol?.IsVirtual ?? false,
            IsAbstract = symbol?.IsAbstract ?? false,
            IsOverride = symbol?.IsOverride ?? false,
            HasOverloads = false,
            ComplexityScore = CalculateComplexity(methodDecl),
            BodyLines = CountBodyLines(methodDecl),
            ParameterCount = methodDecl.ParameterList.Parameters.Count,
            LinesOfCode = CountLines(methodDecl.Span),
            CommentLines = CountCommentLines(methodDecl)
        };

        foreach (var param in methodDecl.ParameterList.Parameters)
        {
            element.Parameters.Add(param.Identifier.Text);
        }

        foreach (var variable in methodDecl.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var varLineSpan = variable.GetLocation().GetLineSpan();
            var varType = variable.Parent is VariableDeclarationSyntax varDecl ? varDecl.Type.ToString() : "var";
            element.Variables.Add(new VariableUsageInfo(
                variable.Identifier.Text,
                varType,
                varLineSpan.StartLinePosition.Line + 1,
                false,
                true,
                methodDecl.Identifier.Text));
        }

        return element;
    }

    private EnhancedCodeElement? CreateElementFromProperty(PropertyDeclarationSyntax propDecl, SemanticModel? semanticModel)
    {
        var lineSpan = propDecl.GetLocation().GetLineSpan();
        var symbol = semanticModel?.GetDeclaredSymbol(propDecl);

        return new EnhancedCodeElement
        {
            Id = Guid.NewGuid().ToString(),
            Name = propDecl.Identifier.Text,
            Type = ElementType.Property,
            LineNumber = lineSpan.StartLinePosition.Line + 1,
            StartLine = lineSpan.StartLinePosition.Line + 1,
            EndLine = lineSpan.EndLinePosition.Line + 1,
            ContainingType = GetContainingTypeName(propDecl),
            ReturnType = propDecl.Type.ToString(),
            Accessibility = symbol?.DeclaredAccessibility.ToString() ?? "Public",
            IsStatic = symbol?.IsStatic ?? false,
            IsVirtual = symbol?.IsVirtual ?? false,
            LinesOfCode = CountLines(propDecl.Span),
            CommentLines = CountCommentLines(propDecl)
        };
    }

    private static string GetContainingTypeName(SyntaxNode node)
    {
        var typeDecl = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return typeDecl?.Identifier.Text ?? "";
    }

    private static int CalculateComplexity(MethodDeclarationSyntax method)
    {
        int complexity = 1;
        complexity += method.DescendantNodes().OfType<IfStatementSyntax>().Count();
        complexity += method.DescendantNodes().OfType<WhileStatementSyntax>().Count();
        complexity += method.DescendantNodes().OfType<ForStatementSyntax>().Count();
        complexity += method.DescendantNodes().OfType<ForEachStatementSyntax>().Count();
        complexity += method.DescendantNodes().OfType<CaseSwitchLabelSyntax>().Count();
        complexity += method.DescendantNodes().OfType<CatchClauseSyntax>().Count();
        complexity += method.DescendantNodes().OfType<ConditionalExpressionSyntax>().Count();
        complexity += method.DescendantNodes().OfType<BinaryExpressionSyntax>()
            .Count(b => b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression));
        return complexity;
    }

    private static double CountBodyLines(MethodDeclarationSyntax method)
    {
        if (method.Body == null) return 0;
        var lineSpan = method.Body.GetLocation().GetLineSpan();
        return lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;
    }

    private static int CountLines(TextSpan span)
    {
        return Math.Max(1, span.Length / 40);
    }

    private static int CountCommentLines(SyntaxNode node)
    {
        var trivia = node.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia));
        return trivia.Count();
    }

    #region 代码片段成员提取（从 INsert.cs 迁移）

    public static List<MemberDeclarationSyntax> ExtractInsertableMembers(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new List<MemberDeclarationSyntax>();

        var root = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot();
        var members = new List<MemberDeclarationSyntax>();
        CollectClassMembers(root, members);
        return members;
    }

    private static void CollectClassMembers(SyntaxNode node, List<MemberDeclarationSyntax> members)
    {
        foreach (var child in node.ChildNodes())
        {
            if (child is MemberDeclarationSyntax member)
            {
                members.Add(member);
            }
            else if (child is BaseNamespaceDeclarationSyntax ns)
            {
                CollectClassMembers(ns, members);
            }
        }
    }

    #endregion
}


