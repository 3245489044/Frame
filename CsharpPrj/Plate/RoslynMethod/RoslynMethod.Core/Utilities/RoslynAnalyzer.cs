using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynSpace.RoslynSyntaxUtilities.Base;

public static class RoslynAnalyzer
{
    public static bool TryInitialize(string sourceCode, out AnalysisContext? context)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            context = null;
            return false;
        }

        try
        {
            context = new AnalysisContext(sourceCode);
            return true;
        }
        catch
        {
            context = null;
            return false;
        }
    }

    public static Task<AnalysisContext> InitializeAsync(string sourceCode)
    {
        return Task.Run(() => new AnalysisContext(sourceCode));
    }
}
