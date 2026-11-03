using Plate.DslMethod.Model;
using Usharp.Syntax;

namespace Usharp.Adapters.Agent;

/// <summary>
/// <c>[Skill] Name: module=… | kind=… | dsl=… | body=… | backend=… | profiles=… | order=…</c> 的语法视图（U-V4 唯一新增语言单元）。
/// <para>
/// <c>profiles=</c>（段归属：该 <c>dsl</c> 段属于哪些档案，如 <c>task,history</c>）与 <c>order=</c>（派生补尾权重）
/// 供 <c>PromptProfileCatalog</c> 由声明派生档案段集（新增 L0 段 = 改本面一行，代码零 band 字面量）。
/// </para>
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
    IReadOnlyList<string> Profiles,
    int Order,
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
            UsharpLineReader.NullIfEmpty(d.Field("kind")),
            UsharpLineReader.NullIfEmpty(d.Field("module")),
            UsharpLineReader.NullIfEmpty(d.Field("dsl")),
            UsharpLineReader.NullIfEmpty(d.Field("body")),
            UsharpLineReader.NullIfEmpty(d.Field("backend")),
            UsharpLineReader.NullIfEmpty(d.Field("executor")),
            UsharpLineReader.NullIfEmpty(d.Field("description")),
            UsharpLineReader.NullIfEmpty(d.Field("schema")),
            UsharpLineReader.NullIfEmpty(d.Field("trigger")),
            SplitList(d.Field("tags")),
            SplitList(d.Field("bands")),
            SplitList(d.Field("profiles")),
            ParseOrder(d.Field("order")),
            SplitList(d.Field("sections")),
            SplitList(d.Field("refs")),
            UsharpLineReader.NullIfEmpty(d.Field("version")),
            IsOn(d.Field("always")),
            string.Equals(UsharpLineReader.NullIfEmpty(d.Field("on_demand")), "on", StringComparison.OrdinalIgnoreCase)
                || string.Equals(UsharpLineReader.NullIfEmpty(d.Field("rare")), "on", StringComparison.OrdinalIgnoreCase),
            d.SourceFile,
            d.LineNumber);
    }

    /// <summary>正文是否附引用（<c>body=</c> 非空 ⇒ Usharp 管理该 SKILL.md 引用）。</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    private static bool IsOn(string? value)
        => UsharpLineReader.TryBool(value, UsharpBoolLexicon.OnOff, out var parsed) && parsed;

    /// <summary>段序权重（<c>order=</c>；未声明/非法 ⇒ -1 = 用声明行序）。</summary>
    private static int ParseOrder(string? value)
        => int.TryParse(value?.Trim(), out var n) ? n : -1;

    private static IReadOnlyList<string> SplitList(string? value)
        => UsharpLineReader.SplitList(value, ',', ' ');
}
