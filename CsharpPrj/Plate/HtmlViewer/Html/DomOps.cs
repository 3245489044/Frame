namespace UiBackend.Html;

/// <summary>IHtmlChannel 契约的受控 DOM 操作白名单（与 HtmlViewer 注入的 __domBridge JS 分支一一对应；消除各桥 magic string 漂移）。</summary>
public static class DomOps
{
    public const string Set = "set";
    public const string Get = "get";
    public const string Click = "click";
    public const string RealClick = "realclick";
    public const string Bounds = "bounds";
    public const string Event = "event";
}
