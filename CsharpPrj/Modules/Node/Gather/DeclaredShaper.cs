using Modules.Node.Projection;

namespace Modules.Node.Gather;

/// <summary>
/// 默认塑形器（声明驱动）：依 <c>node-gather.json</c> 的 <c>kind-shaping</c> 表，
/// 把 <c>titleKey</c>/<c>summary[]</c>/<c>badge</c>/<c>timeKey</c> 映射到读项展平字段，生成
/// <see cref="DeclaredNodeShape"/>。<b>无需为每种 kind 手写 C# 类</b>（去代码化的关键）。
/// </summary>
public sealed class DeclaredShaper : INodeShaper<DeclaredNodeShape>
{
    /// <summary>摘要拼接上限（防大块载荷灌入面板；HTML 编码由模板承担）。</summary>
    public const int SummaryMaxChars = 200;

    private const string SummarySeparator = " · ";

    private readonly NodeGatherSpec _spec;

    public DeclaredShaper(NodeGatherSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    /// <summary>认领声明中存在且允许塑形的 kind。</summary>
    public bool Claims(string kind) =>
        _spec.TryGetShaping(kind, out var shaping) && shaping.Shaped;

    /// <summary>按声明塑形；未声明或 <c>shaped=false</c> ⇒ <c>null</c>（不产出）。</summary>
    public DeclaredNodeShape? Shape(NodeProjectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var kind = _spec.KindOf(item.Fields);
        if (!_spec.TryGetShaping(kind, out var shaping) || !shaping.Shaped)
        {
            return null;
        }

        return new DeclaredNodeShape(
            kind,
            shaping.Badge,
            item.Key,
            Truncate(Field(item, shaping.TitleKey)),
            Truncate(JoinSummary(item, shaping.SummaryKeys)),
            Field(item, shaping.TimeKey));
    }

    private static string? Field(NodeProjectionItem item, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return item.Fields.TryGetValue(name!, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }

    private static string? JoinSummary(NodeProjectionItem item, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            return null;
        }

        var parts = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            var value = Field(item, key);
            if (!string.IsNullOrEmpty(value))
            {
                parts.Add(value!);
            }
        }

        return parts.Count == 0 ? null : string.Join(SummarySeparator, parts);
    }

    private static string? Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var flat = text!.Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length > SummaryMaxChars ? flat[..SummaryMaxChars] + "…" : flat;
    }
}
