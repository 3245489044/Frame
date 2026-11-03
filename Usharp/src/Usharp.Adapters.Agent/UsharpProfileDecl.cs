using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Adapters.Agent;

/// <summary>
/// <c>[Profile] key: bands=a,b | skills=on | mode=off | tools=on | resources=on</c> 的语法视图。
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
    bool Base = false,
    bool IncludeResources = false)
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
            IsOn(d.Field("base")),
            IsOn(d.Field("resources")));
    }

    private static bool IsOn(string? value)
        => UsharpLineReader.TryBool(value, UsharpBoolLexicon.OnOff, out var parsed) && parsed;

    private static IReadOnlyList<string> SplitList(string? value)
        => UsharpLineReader.SplitList(value, ',', ' ');
}
