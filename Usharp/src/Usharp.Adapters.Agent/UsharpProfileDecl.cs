using Plate.DslMethod.Model;

namespace Usharp.Adapters.Agent;

/// <summary>
/// <c>[Profile] key: bands=a,b | skills=on | mode=off | tools=on</c> 的语法视图。
/// <para>
/// <c>[Profile]</c> 已是框架核心前缀（<c>DslSymbolKind.Profile</c>）：本类把 Agent 侧原本的
/// <c>PromptProfileCatalog.TryParse</c> 自写行解析替换为「读 <c>UsharpCompilation</c> facet」。
/// </para>
/// </summary>
public sealed record UsharpProfileDecl(
    string Key,
    IReadOnlyList<string> Bands,
    bool IncludeSkills,
    bool IncludeMode,
    bool IncludeTools,
    string SourceFile,
    int LineNumber,
    bool Base = false)
{
    /// <summary>由 <c>[Profile]</c> 声明投影（无名 → null）。</summary>
    public static UsharpProfileDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        return new UsharpProfileDecl(
            d.Name.Trim(),
            SplitList(d.Field("bands")),
            IsOn(d.Field("skills")),
            IsOn(d.Field("mode")),
            IsOn(d.Field("tools")),
            d.SourceFile,
            d.LineNumber,
            IsOn(d.Field("base")));
    }

    private static bool IsOn(string? value)
    {
        var v = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return string.Equals(v, "on", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "1", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
