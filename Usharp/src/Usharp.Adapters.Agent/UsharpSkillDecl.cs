using Plate.DslMethod.Model;

namespace Usharp.Adapters.Agent;

/// <summary>
/// <c>[Skill] Name: module=… | kind=… | dsl=… | body=… | backend=…</c> 的语法视图（U-V4 唯一新增语言单元）。
/// <para>
/// <b>K3 决策</b>：技能正文保留在 <c>SKILL.md</c>，Usharp 只管理 <c>body=</c> 引用与元数据；
/// 元数据（module/kind/dsl/backend/always/…) 进 <c>.ushar</c>，正文零迁移。
/// </para>
/// </summary>
public sealed record UsharpSkillDecl(
    string Name,
    string? Kind,
    string? Module,
    string? Dsl,
    string? Body,
    string? Backend,
    string? Executor,
    string? Description,
    string? Schema,
    string? Trigger,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Bands,
    IReadOnlyList<string> Sections,
    IReadOnlyList<string> Refs,
    string? Version,
    bool Always,
    bool OnDemand,
    string SourceFile,
    int LineNumber)
{
    /// <summary>由 <c>[Skill]</c> 声明投影（无名 → null，由闸A 报出）。</summary>
    public static UsharpSkillDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        return new UsharpSkillDecl(
            d.Name.Trim(),
            NullIfEmpty(d.Field("kind")),
            NullIfEmpty(d.Field("module")),
            NullIfEmpty(d.Field("dsl")),
            NullIfEmpty(d.Field("body")),
            NullIfEmpty(d.Field("backend")),
            NullIfEmpty(d.Field("executor")),
            NullIfEmpty(d.Field("description")),
            NullIfEmpty(d.Field("schema")),
            NullIfEmpty(d.Field("trigger")),
            SplitList(d.Field("tags")),
            SplitList(d.Field("bands")),
            SplitList(d.Field("sections")),
            SplitList(d.Field("refs")),
            NullIfEmpty(d.Field("version")),
            IsOn(d.Field("always")),
            string.Equals(NullIfEmpty(d.Field("on_demand")), "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(NullIfEmpty(d.Field("rare")), "on", StringComparison.OrdinalIgnoreCase),
            d.SourceFile,
            d.LineNumber);
    }

    /// <summary>正文是否附引用（<c>body=</c> 非空 ⇒ Usharp 管理该 SKILL.md 引用）。</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsOn(string? value)
    {
        var v = NullIfEmpty(value);
        return string.Equals(v, "on", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "1", StringComparison.Ordinal)
               || string.Equals(v, "l0", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
