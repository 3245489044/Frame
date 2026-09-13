
namespace Share.RoslynJson.Emitters;

/// <summary>内嵌 JSON 的生成类规格。</summary>
public sealed class EmbeddedJsonClassSpec
{
    /// <summary>类名。</summary>
    public string ClassName { get; set; } = "GeneratedJsonPayload";

    /// <summary>承载 JSON 的常量成员名。</summary>
    public string MemberName { get; set; } = "Json";

    /// <summary>命名空间（null/空 = 使用全局命名空间）。</summary>
    public string? Namespace { get; set; }

    /// <summary>是否 static 类。</summary>
    public bool IsStatic { get; set; } = true;

    /// <summary>是否 sealed 类（非 static 时生效）。</summary>
    public bool IsSealed { get; set; } = true;

    /// <summary>字面量形态。</summary>
    public EmbeddedJsonStyle Style { get; set; } = EmbeddedJsonStyle.RawStringLiteral;

    /// <summary>生成文件头部注释（C# <c>//</c> 注释，如「生成物勿手改」；不进入 JSON 值）。</summary>
    public string? HeaderComment { get; set; }

    public static EmbeddedJsonClassSpec Default => new EmbeddedJsonClassSpec();
}
