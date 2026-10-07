using System.Text.Json;
using DataCenter.Protocol.Standards;

namespace DataCenter.Standards;

/// <summary>
/// 描述符读取器的半规则层钩子（描述符单源化 B-3b-1；组合根 = <see cref="StandardDescriptorReader"/> 静态构造装配）。
/// <para>
/// 纪律：本件只做「把本质语义显式委托给既有手写读取器 / 派生位」——**不写进引擎**；未装配时引擎自然回落
/// 「子层未登记即抛」。覆盖面：
/// ① <c>impl</c> 成员的 <c>kind</c> 条件分派（scripted / composed 门控）；
/// ② <c>step</c> 层四形态判别（委托 <see cref="StandardJson.ReadStep"/>）；
/// ③ <c>valueSource</c> 层多形态归一（委托 <see cref="StandardJson.ReadArg"/>）；
/// ④ 派生 / 无键成员（<c>fakeCall.HasValue</c>）；
/// ⑤ 未登记子层（<c>expr</c> / <c>foreach</c>）整层委托。
/// </para>
/// </summary>
internal static class StandardReaderHooks
{
    /// <summary>装配成员钩子与层钩子（幂等）。</summary>
    public static void Install()
    {
        StandardReaderEngine.Wire(HandleMember);
        StandardReaderEngine.WireLayer(HandleLayer);
    }

    /// <summary>成员级钩子：<c>impl</c> 的 kind 门控 + 派生位。</summary>
    private static object? HandleMember(
        string layer, string member, string? jsonKey, bool found, JsonElement element, JsonElement container, string context)
        => layer switch
        {
            "impl" => ImplMember(member, container, context),
            "fakeCall" when string.Equals(member, nameof(FakeCallStandard.HasValue), StringComparison.Ordinal)
                => StandardJson.CallValueSource(context, container) is null
                   && container.ValueKind == JsonValueKind.Object
                   && container.TryGetProperty("value", out _),
            _ => StandardReaderEngine.Unhandled,
        };

    /// <summary>层级钩子：半规则层整层接管（未登记子层亦经此）；<paramref name="context"/> = 键路径。</summary>
    private static object? HandleLayer(string layer, JsonElement obj, string context) => layer switch
    {
        "step" => StandardJson.ReadStep(context, obj),
        "valueSource" => StandardJson.ReadArg(context, obj, context),
        "expr" => StandardJson.ReadExpr(context, obj, context),
        "foreach" => StandardJson.ReadForeach(context, obj),
        "dep" => StandardJson.ReadDep(context, obj),
        _ => StandardReaderEngine.Unhandled,
    };

    /// <summary>
    /// <c>impl</c> 成员的 kind 条件分派（与手写 <c>StandardJson.ReadImpl</c> 逐位对齐）：
    /// 门控通过 → <see cref="StandardReaderEngine.Unhandled"/>（交引擎结构递归）；
    /// 门控不通过 → null（该形态下该成员不参与）。<paramref name="context"/> = 键路径（供诊断文案）。
    /// </summary>
    private static object? ImplMember(string member, JsonElement impl, string context)
    {
        var scripted = IsKind(impl, PortImplVocabulary.KindName.Scripted);

        return member switch
        {
            // 脚本族专属：非脚本 → null；脚本 → 交引擎递归（Binds/Scopes 归一见描述符 EmptyOnAbsent）。
            "Binds" => scripted ? StandardReaderEngine.Unhandled : null,
            "Script" => scripted ? StandardReaderEngine.Unhandled : null,
            // 聚合 / 无键读取：整值委托手写（其入参即 impl 承载对象）。
            "ScriptProject" => scripted ? StandardJson.ReadScriptProject(context, impl) : null,
            "Scopes" => scripted ? StandardJson.ReadScopes(context, impl) : null,
            "Deliver" => scripted ? StandardJson.ReadDeliver(context, impl) : null,
            "Scenarios" => scripted ? StandardJson.ReadScenarios(context, impl) : null,
            // 组合族专属：非组合 → null；组合 → 交引擎递归（Call / ComposedField 层）。
            "Calls" => IsKind(impl, PortImplVocabulary.KindName.Composed) ? StandardReaderEngine.Unhandled : null,
            "Project" => IsKind(impl, PortImplVocabulary.KindName.Composed) ? StandardReaderEngine.Unhandled : null,
            _ => StandardReaderEngine.Unhandled,
        };
    }

    /// <summary>承载对象的 <c>kind</c> 是否等于给定值（缺失 / 非字符串 → false）。</summary>
    private static bool IsKind(JsonElement impl, string kind)
        => impl.ValueKind == JsonValueKind.Object
           && impl.TryGetProperty("kind", out var declared)
           && declared.ValueKind == JsonValueKind.String
           && string.Equals(declared.GetString(), kind, StringComparison.Ordinal);
}
