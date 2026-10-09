using System.Text.Json;
using DataCenter.Protocol.Standards.Descriptors;

namespace DataCenter.Standards;

/// <summary>
/// 半规则层 / 成员的读取器注册表（描述符单源化 §3.2；**取代硬编码钩子** <c>StandardReaderHooks</c>）。
/// <para>
/// **接线单一真源 = SSD（清单）**：<see cref="LayerDescriptor.Reader"/> /
/// <see cref="MemberDescriptor.Reader"/> 声明 reader id；本表按 id 提供处理器。
/// 处理器**复用既有手写读取器 <see cref="StandardJson"/>**（语义零变更）——因此本改造只收敛「接线」这一处多源，
/// 不改变任何解析语义。
/// </para>
/// <para>纪律：新增半规则层 / 成员 = ① SSD 一行（声明 <c>Reader</c>/<c>Gate</c>）+ ② 必要时本表一行处理器。</para>
/// </summary>
internal static class StandardReaders
{
    /// <summary>层读取器：整层接管（<paramref name="layer"/> = 层名，<paramref name="context"/> = 键路径）。</summary>
    public delegate object? LayerReader(string layer, JsonElement obj, string context);

    /// <summary>成员读取器：整值委托（<paramref name="context"/> = 成员键路径，<paramref name="container"/> = 承载对象）。</summary>
    public delegate object? MemberReader(string context, JsonElement container);

    /// <summary>层读取器表（id → 处理器）；id 约定 = 层名 / 子层名。</summary>
    private static readonly IReadOnlyDictionary<string, LayerReader> Layers =
        new Dictionary<string, LayerReader>(StringComparer.Ordinal)
        {
            ["step"] = (layer, obj, context) => StandardJson.ReadStep(context, obj),
            ["valueSource"] = (layer, obj, context) => StandardJson.ReadArg(context, obj, context),
            ["expr"] = (layer, obj, context) => StandardJson.ReadExpr(context, obj, context),
            ["foreach"] = (layer, obj, context) => StandardJson.ReadForeach(context, obj),
            ["dep"] = (layer, obj, context) => StandardJson.ReadDep(context, obj),
            ["call"] = (layer, obj, context) => StandardJson.ReadCall(context, obj),
        };

    /// <summary>成员读取器表（id → 处理器）；仅 <c>impl</c> 半规则成员与派生位用。</summary>
    private static readonly IReadOnlyDictionary<string, MemberReader> Members =
        new Dictionary<string, MemberReader>(StringComparer.Ordinal)
        {
            ["scriptProject"] = (context, container) => StandardJson.ReadScriptProject(context, container),
            ["scopes"] = (context, container) => StandardJson.ReadScopes(context, container),
            ["deliver"] = (context, container) => StandardJson.ReadDeliver(context, container),
            ["scenarios"] = (context, container) => StandardJson.ReadScenarios(context, container),
            ["fakeCallHasValue"] = (context, container) =>
                StandardJson.CallValueSource(context, container) is null
                && container.ValueKind == JsonValueKind.Object
                && container.TryGetProperty("value", out _),
        };

    /// <summary>按 id 取层读取器（未登记 → false）。</summary>
    public static bool TryGetLayer(string? id, out LayerReader reader)
    {
        reader = null!;
        return id is not null && Layers.TryGetValue(id, out reader!);
    }

    /// <summary>按 id 取成员读取器（未登记 → false）。</summary>
    public static bool TryGetMember(string? id, out MemberReader reader)
    {
        reader = null!;
        return id is not null && Members.TryGetValue(id, out reader!);
    }
}
