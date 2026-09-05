using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynSpace.RoslynStringCodeGeneration;

public class CodeGenerationContext
{
    public string Namespace { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public bool GenerateAsStatic { get; set; }
    public bool GenerateAsReadOnly { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();

    public CodeGenerationContext WithProperty(string key, object value)
    {
        Properties[key] = value;
        return this;
    }
}

public class CodeGenerationResult
{
    public bool Success { get; set; }
    public string GeneratedCode { get; set; } = string.Empty;
    public Exception? Error { get; set; }

    public static CodeGenerationResult SuccessResult(string code) =>
        new() { Success = true, GeneratedCode = code };

    public static CodeGenerationResult ErrorResult(Exception ex) =>
        new() { Success = false, Error = ex };
}

public class StaticStringClassModel
{
    public Dictionary<string, string> Fields { get; set; } = new();
    public List<string> AdditionalUsings { get; set; } = new();
}
