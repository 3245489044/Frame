
using System;
using System.Collections.Generic;

namespace RoslynSpace.RoslynStringCodeGeneration;

/// <summary>代码生成上下文（自 RoslynMethod.Generation 上移至 RoslynMethod.Core）。</summary>
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

/// <summary>代码生成结果（自 RoslynMethod.Generation 上移至 RoslynMethod.Core）。</summary>
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
