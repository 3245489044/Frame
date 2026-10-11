#nullable enable

using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 通用输入区（composer）投影器 —— 「输入框 + 发送按钮」的通用 HTML 片段投影，
/// 把「页面里再写一份输入区模板 + 行模型」收敛为框架单实现。
/// <para>
/// 交互协议（<c>data-cmd</c> 点击桥 / <c>data-input</c> 读取同页输入值）已由
/// <see cref="HtmlAssetKeys.BridgeScript"/> 原生承担，本投影器只做「模板 + 行模型」投影：
/// 领域只需提供上下文锚（<c>session</c>/<c>history</c>）与文案，模板由 JSON 声明（键 <c>composer</c>）。
/// </para>
/// 纯模板投影、零领域类型依赖（不感知视图枚举 / 业务模型）。
/// </summary>
public static class HtmlComposerProjector
{
    /// <summary>composer 模板名（JSON templates 键）。</summary>
    public const string TemplateName = "composer";

    /// <summary>
    /// 渲染输入区 HTML：由 <paramref name="templates"/> 的 <c>composer</c> 模板承担结构，
    /// 本方法只装填行模型（上下文锚 + 文案）；缺模板 → 返回空串（调用方据此不挂输入区）。
    /// </summary>
    /// <param name="session">发送目标任务锚（<c>data-session</c>；任务粒度时给出）。</param>
    /// <param name="history">发送目标历史容器锚（<c>data-history</c>；历史粒度时给出）。</param>
    /// <param name="placeholder">输入框占位文案。</param>
    /// <param name="sendLabel">发送按钮文案（缺省「发送」）。</param>
    /// <param name="sendTitle">发送按钮悬浮提示。</param>
    /// <param name="extra">领域扩展字段（如额外按钮文案）；并入行模型（同名键以 <paramref name="extra"/> 为准），null = 无。</param>
    public static string Render(
        IReadOnlyDictionary<string, string> templates,
        string? session = null,
        string? history = null,
        string? placeholder = null,
        string? sendLabel = null,
        string? sendTitle = null,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["session"] = session ?? string.Empty,
            ["history"] = history ?? string.Empty,
            ["placeholder"] = placeholder ?? string.Empty,
            ["sendLabel"] = sendLabel ?? "发送",
            ["sendTitle"] = sendTitle ?? string.Empty,
        };
        // 领域扩展字段并入（模板可选绑定，缺省渲染为空，零回归）。
        if (extra is not null)
            foreach (var kv in extra)
                row[kv.Key] = kv.Value;
        return HtmlTemplateEngine.Render(templates, TemplateName, row);
    }
}
