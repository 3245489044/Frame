using System.Text.RegularExpressions;

namespace Modules.Node.Identity;

public enum IdentitySegmentKind
{
    /// <summary>上下文段（由调用方给定，如 container）。</summary>
    Context,

    /// <summary>序数段（由 <see cref="ISequenceAuthority"/> 单源分配，N6 顺序内嵌）。</summary>
    Sequence,

    /// <summary>字面量段（分隔符/固定前缀，由模板声明）。</summary>
    Literal,
}

public sealed record IdentitySegment(IdentitySegmentKind Kind, string Name);

/// <summary>
/// 键身份规范（N1/N2）：段序列模板的声明式表达式，如 <c>{container}.r{round}</c>。
/// 只描述身份形状，不碰载体（载体细节归 <c>ICarrier</c> 适配器）。
/// 占位符词法单源 = <see cref="KeyTemplateEvaluator"/>（读写两侧同一原子）。
/// </summary>
public sealed class IdentitySpec
{
    private IdentitySpec(string template, IReadOnlyList<IdentitySegment> segments, IReadOnlyList<string> contextKeys, IReadOnlyList<string> sequenceKeys)
    {
        Template = template;
        Segments = segments;
        ContextKeys = contextKeys;
        SequenceKeys = sequenceKeys;
    }

    public string Template { get; }

    public IReadOnlyList<IdentitySegment> Segments { get; }

    public IReadOnlyList<string> ContextKeys { get; }

    public IReadOnlyList<string> SequenceKeys { get; }

    public IReadOnlyList<string> Placeholders =>
        Segments.Where(s => s.Kind != IdentitySegmentKind.Literal).Select(s => s.Name).ToArray();

    /// <summary>L-4：键模板须含序数占位，否则序数丢失（退化为手写序号）。</summary>
    public bool HasSequencePlaceholder => SequenceKeys.Count > 0;

    public static IdentitySpec Create(string template, IEnumerable<string>? sequenceKeys = null)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new ArgumentException("身份模板为空", nameof(template));
        }

        var sequence = new HashSet<string>(sequenceKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
        var segments = new List<IdentitySegment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        foreach (Match match in KeyTemplateEvaluator.PlaceholderRegex.Matches(template))
        {
            if (match.Index > index)
            {
                segments.Add(new IdentitySegment(IdentitySegmentKind.Literal, template[index..match.Index]));
            }

            var name = match.Groups[1].Value;
            if (!seen.Add(name))
            {
                throw new ArgumentException($"身份模板占位符重复: {name}", nameof(template));
            }

            segments.Add(new IdentitySegment(
                sequence.Contains(name) ? IdentitySegmentKind.Sequence : IdentitySegmentKind.Context,
                name));
            index = match.Index + match.Length;
        }

        if (index < template.Length)
        {
            segments.Add(new IdentitySegment(IdentitySegmentKind.Literal, template[index..]));
        }

        if (seen.Count == 0)
        {
            throw new ArgumentException("身份模板不含占位符", nameof(template));
        }

        foreach (var key in sequence)
        {
            if (!seen.Contains(key))
            {
                throw new ArgumentException($"序数键未出现在模板中: {key}", nameof(sequenceKeys));
            }
        }

        return new IdentitySpec(
            template,
            segments,
            segments.Where(s => s.Kind == IdentitySegmentKind.Context).Select(s => s.Name).ToArray(),
            segments.Where(s => s.Kind == IdentitySegmentKind.Sequence).Select(s => s.Name).ToArray());
    }
}
