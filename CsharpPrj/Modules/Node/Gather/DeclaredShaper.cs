using Modules.Node.Projection;
using Modules.Node.Text;

namespace Modules.Node.Gather;

/// <summary>
/// 默认塑形器（声明驱动）：依 <c>node-gather.json</c> 的 <c>kind-shaping</c> 表，
/// 把 <c>titleKey</c>/<c>summary[]</c>/<c>badge</c>/<c>timeKey</c> 映射到读项展平字段，生成
/// <see cref="DeclaredNodeShape"/>。<b>无需为每种 kind 手写 C# 类</b>（去代码化的关键）。
/// </summary>
public sealed class DeclaredShaper : INodeShaper<DeclaredNodeShape>
{
    /// <summary>摘要拼接上限（截断策略单源：<see cref="TextClipPolicy.SummaryMaxChars"/>；HTML 编码由模板承担）。</summary>
    public const int SummaryMaxChars = TextClipPolicy.SummaryMaxChars;

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

        // 字段袋单源（payload 优先 + 记录级 meta 补缺）：禁各处自行取 Fields。
        var fields = NodeFieldBag.Of(item);
        var kind = _spec.KindOf(fields);
        if (!_spec.TryGetShaping(kind, out var shaping) || !shaping.Shaped)
        {
            return null;
        }

        var summary = JoinSummary(item, shaping.SummaryKeys);
        // 折叠计数可见化（×N）：重复不静默吞行，读侧折叠多少条必须上屏（权威: NodeGatherProjection.RepeatCountField）。
        var repeat = NodeFieldBag.ValueOf(item, NodeGatherProjection.RepeatCountField);
        if (int.TryParse(repeat, out var repeatCount) && repeatCount > 1)
        {
            summary = (summary ?? string.Empty) + " ×" + repeatCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new DeclaredNodeShape(
            kind,
            shaping.Badge,
            item.Key,
            TextClipPolicy.Summary(NodeFieldBag.ValueOf(item, shaping.TitleKey)),
            TextClipPolicy.Summary(summary),
            NodeFieldBag.ValueOf(item, shaping.TimeKey),
            item.DomainKey,
            item.SourceKey,
            PortsOf(item, shaping.EffectivePortKeys),
            item.WriteDomain);
    }

    /// <summary>端口取值：按声明 <c>ports=</c> 字段集读值并拆分（容 JSON 数组形 / 逗号分隔），保序去重；无值 ⇒ null。</summary>
    private static IReadOnlyList<string>? PortsOf(NodeProjectionItem item, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            return null;
        }

        var ports = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var value = NodeFieldBag.ValueOf(item, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var token in value!.Split(
                         new[] { ',', ';', ' ', '\r', '\n', '[', ']', '"', '\'' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var port = token.Trim();
                if (port.Length > 0 && seen.Add(port))
                {
                    ports.Add(port);
                }
            }
        }

        return ports.Count == 0 ? null : ports;
    }

    private static string? Field(IReadOnlyDictionary<string, string> fields, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return fields.TryGetValue(name!, out var value) && !string.IsNullOrEmpty(value) ? value : null;
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
            var value = NodeFieldBag.ValueOf(item, key);
            if (!string.IsNullOrEmpty(value))
            {
                parts.Add(value!);
            }
        }

        return parts.Count == 0 ? null : string.Join(SummarySeparator, parts);
    }

}
