#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 桥就绪探测（纯函数，收敛 <c>document.__domBridgeInstalled</c> 与桥模块清单回读）——
/// 消除「就绪判定」在控件与传输组件间的重复实现。调用方须在 UI 线程执行（或自行封送）。
/// </summary>
public static class WebViewBridgeProbe
{
    /// <summary>当前活 document 是否已注入 __domBridge（点击桥随其就位）；异常 / 空 → false。</summary>
    public static async Task<bool> IsBridgeReadyAsync(CoreWebView2? core)
    {
        if (core is null) return false;
        try
        {
            // 就绪断言用 document 级标志（对齐 BridgeScript）：WebView2 NavigateToString 重渲染替换 document 保留 window，
            // window 级标志会误判「桥已注入」导致新 document 缺点击监听器（[data-cmd] 点击转 postMessage 失效）。
            var raw = await core.ExecuteScriptAsync("(!!document.__domBridgeInstalled);").ConfigureAwait(false);
            return raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>文档级行为模块就绪清单（回读 <c>document.__bridgeModules</c>）；异常 / 空 → None。</summary>
    public static async Task<UiBridgeModuleState> GetBridgeModulesAsync(CoreWebView2? core)
    {
        if (core is null) return UiBridgeModuleState.None;
        try
        {
            var raw = await core.ExecuteScriptAsync(BridgeModuleManifest.QueryScript).ConfigureAwait(false);
            return BridgeModuleManifest.Parse(raw);
        }
        catch (Exception)
        {
            return UiBridgeModuleState.None;
        }
    }
}
