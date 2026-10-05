using System.Text.Json.Nodes;
using Modules.Node.Diagnostics;

namespace Modules.Node.Gather;

/// <summary>
/// 单 kind 的**塑形**声明（<c>node-gather.json</c> 的 <c>kind-shaping</c> 条目）。
/// <para>
/// 全部为**数据**（框架零领域词表）：族名 / 标题取值字段 / 摘要取值字段集 / 徽标 / 时间取值字段 /
/// 是否塑形。<c>Shaped=false</c> 表达「纯账目类不登面板」，替代散落于 UI 的启发式。
/// </para>
/// </summary>
/// <param name="Kind">节点类别（载荷判别字段值）。</param>
/// <param name="Family">所属家族名（聚拢轴）。</param>
/// <param name="TitleKey">标题取值字段（可空 = 无标题行）。</param>
/// <param name="SummaryKeys">摘要取值字段集（按序拼接）。</param>
/// <param name="Badge">徽标文本（可空）。</param>
/// <param name="TimeKey">时间取值字段（可空）。</param>
/// <param name="Shaped">是否塑形（<c>false</c> = 该 kind 的行不呈现）。</param>
public sealed record KindShaping(
    string Kind,
    string Family,
    string? TitleKey,
    IReadOnlyList<string> SummaryKeys,
    string? Badge,
    string? TimeKey,
    bool Shaped);
