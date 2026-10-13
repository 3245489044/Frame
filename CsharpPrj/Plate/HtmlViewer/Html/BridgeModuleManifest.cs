#nullable enable

using System.Text.Json;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 桥模块清单（L2 HTML 实现，L1 <see cref="UiBridgeModuleState"/> 的 HTML 落地）——把「文档级行为模块是否就绪」
/// 从单一 <c>document.__domBridgeInstalled</c> 标志升级为 <c>document.__bridgeModules</c> 模块位清单：
/// 各脚本注入时置位，就绪查询经 <see cref="QueryScript"/> 回读并 <see cref="Parse"/>。
/// <para>用途：写协调器/通道自愈据清单判定「点击/补丁/对齐/刷新」是否全就绪，消除「点击桥就绪但补丁/对齐模块缺失
/// → 区域写永久 STATE_NOT_READY → 整页回退」的降级。</para>
/// </summary>
public static class BridgeModuleManifest
{
    /// <summary>文档级模块位全局对象名。</summary>
    public const string JsGlobal = "document.__bridgeModules";

    /// <summary>模块位 → JS 属性名。</summary>
    public static string Property(UiBridgeModules module) => module switch
    {
        UiBridgeModules.Click => "click",
        UiBridgeModules.Patch => "patch",
        UiBridgeModules.Reconcile => "reconcile",
        UiBridgeModules.Refresh => "refresh",
        _ => "unknown",
    };

    /// <summary>置位片段（内嵌脚本在幂等守卫后追加；幂等：对象惰性创建，重复执行无副作用）。</summary>
    public static string FlagSnippet(UiBridgeModules module)
        => JsGlobal + "=" + JsGlobal + "||{};" + JsGlobal + "." + Property(module) + "=true;";

    /// <summary>清单回读脚本：返回 JSON 对象 <c>{click,patch,reconcile,refresh}</c>。</summary>
    public const string QueryScript =
        "(function(){var m=(document&&document.__bridgeModules)||{};" +
        "return JSON.stringify({click:!!m.click,patch:!!m.patch,reconcile:!!m.reconcile,refresh:!!m.refresh});})()";

    /// <summary>解析 ExecuteScriptAsync 回串（JSON 对象；空/异常 → None）。</summary>
    public static UiBridgeModuleState Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return UiBridgeModuleState.None;
        try
        {
            // ExecuteScriptAsync 返回 JSON 字面量（字符串形式）：先解一层字符串，再解对象。
            var inner = raw.Trim();
            if (inner.Length >= 2 && inner[0] == '"')
                inner = JsonSerializer.Deserialize<string>(inner) ?? inner;
            using var doc = JsonDocument.Parse(inner);
            var root = doc.RootElement;
            var modules = UiBridgeModules.None;
            if (Flag(root, "click")) modules |= UiBridgeModules.Click;
            if (Flag(root, "patch")) modules |= UiBridgeModules.Patch;
            if (Flag(root, "reconcile")) modules |= UiBridgeModules.Reconcile;
            if (Flag(root, "refresh")) modules |= UiBridgeModules.Refresh;
            return new UiBridgeModuleState(modules);
        }
        catch (JsonException)
        {
            return UiBridgeModuleState.None;
        }
    }

    private static bool Flag(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
