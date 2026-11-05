namespace Modules.Node.Spec;

/// <summary>信号排序键（声明式）：字段名 + 方向；缺省降序（高值优先）。</summary>
/// <param name="Field">信号字段名（来自声明，框架不内置）。</param>
/// <param name="Descending">是否降序（默认 true）。</param>
public sealed record SignalOrderKey(string Field, bool Descending = true);

/// <summary>
/// 信号过滤（声明式）：字段集 + 期望值（可空 = 要求字段存在且非空）。
/// <para>语义 = 合取：任一字段不匹配即拒绝；字段名来自声明。</para>
/// </summary>
/// <param name="Fields">参与判定/输出的字段名（序即声明序）。</param>
/// <param name="Values">期望值（字段 → 文本；null = 只要求存在）。</param>
public sealed record SignalFilter(
    IReadOnlyList<string> Fields,
    IReadOnlyDictionary<string, string>? Values = null);

/// <summary>
/// 预算声明（N8：预算进声明面，禁代码常量）：token 上限 / 条数上限 / 每带条数上限 / 字符-token 换算。
/// <para>全部可空 = 未声明该维度；未声明即不生效（不静默采用缺省数值）。</para>
/// </summary>
public sealed record NodeBudgetSpec(
    int? Tokens = null,
    int? Limit = null,
    int? PerBand = null,
    int? CharsPerToken = null);

/// <summary>
/// 读侧选择声明（信号排序/过滤 + 预算）：<b>唯一来源 = 声明面</b>，运行期只读。
/// </summary>
public sealed record NodeSelectionSpec(
    IReadOnlyList<SignalOrderKey>? SignalOrder = null,
    SignalFilter? SignalFilter = null,
    NodeBudgetSpec? Budget = null)
{
    /// <summary>空声明（全部维度未声明）。</summary>
    public static readonly NodeSelectionSpec Empty = new();
}
