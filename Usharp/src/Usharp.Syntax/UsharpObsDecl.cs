using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>[Obs] 声明的子类（由 <c>kind=</c> 区分；未知值 = <see cref="Unknown"/>，由闸A 报错）。</summary>
public enum UsharpObsKind
{
    /// <summary>未声明或非法的 <c>kind=</c>（闸A：非法 kind）。</summary>
    Unknown,

    /// <summary>语义文件头（承载 <c>version=</c>；至多一条）。</summary>
    File,

    /// <summary>作用域键（关联键词汇表；<c>parent=</c> 声明层级）。</summary>
    Scope,

    /// <summary>实体类型（+ 归属作用域）。</summary>
    Entity,

    /// <summary>事件分类（+ 实体 / 必填字段 / 度量键 / 枚举域）。</summary>
    Event,
}

/// <summary>
/// <c>[Obs] Name: kind=file|scope|entity|event</c> 的语法视图（第五语言单元：观测声明）。
/// <para>
/// 观测语义的唯一书写面：编译产出既有 <c>BoundaryContract</c> / <c>EventContract</c>（由适配工程映射，核心零观测依赖）。
/// 字段：<c>parent=</c> / <c>scope=</c> / <c>entity=</c> / <c>required=</c>（别名 <c>fields=</c>）/ <c>metrics=</c> /
/// <c>enum=&lt;字段&gt;:&lt;v1,v2&gt;[;…]</c> / <c>version=</c>。
/// </para>
/// </summary>
public sealed record UsharpObsDecl(
    string Name,
    UsharpObsKind Kind,
    string? Parent,
    string? Scope,
    string? Entity,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> Metrics,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Enums,
    string? Version,
    string SourceFile,
    int LineNumber)
{
    /// <summary>声明是否含某必填字段（<c>enum=</c> 字段须落在其中，US6004 判据）。</summary>
    public bool RequiresField(string name) => Required.Contains(name, StringComparer.Ordinal);

    public static UsharpObsDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var required = NullIfEmpty(d.Field(UsharpLanguage.FieldRequired)) ?? NullIfEmpty(d.Field(UsharpLanguage.FieldFields));

        return new UsharpObsDecl(
            d.Name,
            ParseKind(d.Field(UsharpLanguage.FieldKind)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldParent)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldScope)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldEntity)),
            SplitList(required),
            SplitList(d.Field(UsharpLanguage.FieldMetrics)),
            ParseEnums(d.Field(UsharpLanguage.FieldEnum)),
            NullIfEmpty(d.Field(UsharpLanguage.FieldVersion)),
            d.SourceFile,
            d.LineNumber);
    }

    private static UsharpObsKind ParseKind(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            UsharpLanguage.KindFile => UsharpObsKind.File,
            UsharpLanguage.KindScope => UsharpObsKind.Scope,
            UsharpLanguage.KindEntity => UsharpObsKind.Entity,
            UsharpLanguage.KindEvent => UsharpObsKind.Event,
            _ => UsharpObsKind.Unknown,
        };

    /// <summary>逗号列表（去空去空白；空声明 → 空表）。</summary>
    private static IReadOnlyList<string> SplitList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// <c>enum=</c> 解析：<c>&lt;字段&gt;:&lt;v1,v2&gt;</c>，多字段以 <c>;</c> 分段
    /// （<c>|</c> 为字段分隔、<c>,</c> 为列表分隔，均不可复用）。格式非法段忽略（不静默造字段）。
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseEnums(string? value)
    {
        var enums = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) return enums;

        foreach (var segment in UsharpLanguage.SplitSegments(value))
        {
            var (field, rawAllowed) = UsharpLanguage.SplitPair(segment);
            var allowed = SplitList(rawAllowed);
            if (field.Length == 0 || allowed.Count == 0) continue;
            enums[field] = allowed;
        }
        return enums;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
