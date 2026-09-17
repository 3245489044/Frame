namespace Strategy.Observability.Ports;

using System.Text.Json;
using DataCenter.Protocol.Standards;

/// <summary>
/// 端口取值声明（R2-1，**唯一转换表**）：三套旧模型（<see cref="PortArgSpec"/> / <see cref="ComposedArgSpec"/> /
/// <see cref="ScriptedArgSpec"/>）的**公共内核** —— 转换规则 / 可省略 / 空字面 / 缺省值 / 结构化来源 / 键名。
/// <para>
/// 分工：本件只承载「机制可见」的取值信息（不含领域语义）。三套旧模型经各自 <c>Options</c> 视图投影到本件，
/// 使转换与取值判定的消费方（R2-2 指令模型 / R2-3 求值引擎）只需面对**一份**声明形态。
/// </para>
/// <para>
/// 值域：<see cref="Convert"/> 为空 = 无转换声明（值归一单源，见 <c>PortValues.TryNormalize</c>）；
/// 非空取值见 <see cref="CommonConversions"/>（三通道共同）与 <see cref="ExtendedConversions"/>（通道特有，待 R2-2 收口）。
/// </para>
/// </summary>
/// <param name="Convert">转换规则（空 = 无转换声明 → 值归一）。</param>
/// <param name="Optional">可省略（无值 → <paramref name="Default"/> 或目标类型默认值）。</param>
/// <param name="NullLiteral">字面串 <c>null</c> 视为 null。</param>
/// <param name="Default">缺省值（可选槽位缺参时使用）。</param>
/// <param name="From">结构化取值来源（如 <c>with</c> = 具名实参）。</param>
/// <param name="Key">结构化取值键名 / 槽名。</param>
public sealed record PortValueOptions(
    string? Convert = null,
    bool Optional = false,
    bool NullLiteral = false,
    JsonElement? Default = null,
    string? From = null,
    string? Key = null)
{
    /// <summary>三通道**共同**转换词汇（唯一清单；R2-3 后为终态词表）；词表唯一 C# 源 = <see cref="PortImplVocabulary.CommonConversions"/>。
    /// （B5 判定上收：内核不再另立清单，禁两处各自维护。）</summary>
    public static IReadOnlyList<string> CommonConversions => PortImplVocabulary.CommonConversions;

    /// <summary>通道**特有**转换词汇（D-1 分歧登记：<c>array</c>/<c>graph</c>/<c>passthrough</c> 仅脚本通道声明过）；
    /// 词表唯一 C# 源 = <see cref="PortImplVocabulary.ExtendedConversions"/>。</summary>
    public static IReadOnlyList<string> ExtendedConversions => PortImplVocabulary.ExtendedConversions;

    /// <summary>转换词是否已登记（共同 ∪ 特有 ∪ 组合指记 <c>dep</c>；空串 = 无转换声明）；判定源 = <see cref="PortImplVocabulary.IsKnownConversion"/>。</summary>
    public static bool IsKnownConversion(string? convert)
        => PortImplVocabulary.IsKnownConversion(convert);

    /// <summary>结构化来源是否为具名实参（<c>with</c>）。</summary>
    public bool IsWithSource => string.Equals(From, "with", StringComparison.Ordinal);
}

/// <summary>
/// 端口**形参**声明（R2-1）：位序即语义（<see cref="Index"/>）+ 可选名（<see cref="Name"/>）+ 取值声明
/// （<see cref="Options"/>）。三通道的「位置实参转换表」在本件归一，供指令模型（R2-2）与求值引擎（R3）单源消费。
/// </summary>
/// <param name="Index">位置（0 基；声明序即传参序）。</param>
/// <param name="Name">形参名（具名绑定 / 诊断用；未知可为空）。</param>
/// <param name="Options">取值声明（唯一转换表）。</param>
public sealed record PortParam(int Index, string? Name, PortValueOptions Options);
