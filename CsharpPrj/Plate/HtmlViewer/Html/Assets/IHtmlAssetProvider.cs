#nullable enable

namespace UiBackend.Html;

/// <summary>前端资产键（脚本 / 样式 / 参数化模板的统一命名空间）。容纳 HtmlViewer 原内嵌 JS/CSS/JS 模板，消除散落 const 字符串。</summary>
public static class HtmlAssetKeys
{
    // 静态脚本
    public const string BridgeScript = "bridge";
    public const string TabSwapScript = "tabswap";

    /// <summary>反向刷新桥（宿主推送 + 1s trailing 节流 + 兜底慢轮询 + 手动刷新）。</summary>
    public const string RefreshBridgeScript = "refreshBridge";

    // 参数化模板（含 $PLACEHOLDER，由 HtmlScriptExecutor 统一 JSON 序列化展开）
    public const string DomHandleTemplate = "domHandle";
    public const string RectQueryTemplate = "rectQuery";
    public const string BridgeDiagnosisTemplate = "bridgeDiagnosis";

    // 样式
    public const string DefaultCss = "defaultCss";
    public const string BaseCss = "baseCss";
}

/// <summary>
/// 前端资产提供者——把 JS/CSS/JS 模板从 C# 方法体字符串提升为**具名资产**（声明驱动，"C# 只供数据" 哲学的延伸）。
/// 默认实现见 <see cref="HtmlAssetRegistry"/>；可替换为外部 .js/.css 资源或 JSON 声明（运行时按 key 解析）。
/// </summary>
public interface IHtmlAssetProvider
{
    /// <summary>取整段可注入脚本（桥脚本 / tab 切换脚本等）；key 缺失返回空串。</summary>
    string GetScript(string key);

    /// <summary>取主题 / 布局 CSS 片段；key 缺失返回空串。</summary>
    string GetStyle(string key);

    /// <summary>取参数化 JS 模板；key 缺失返回空串。</summary>
    string GetTemplate(string key);
}