namespace UiBackend.Html;

/// <summary>IHtmlChannel 契约的受控 DOM 操作白名单（与 HtmlViewer 注入的 __domBridge JS 分支一一对应；消除各桥 magic string 漂移）。</summary>
public static class DomOps
{
    public const string Set = "set";

    /// <summary>内容补丁（不重载文档）：替换目标容器 innerHTML 并保存/恢复滚动、<c>&lt;details&gt;</c> 展开与焦点态。
    /// 用于事件驱动的刷新写回（消除 NavigateToString 整文档重载导致的 UI 位置重置）。name 为空 → 目标为 <c>document.body</c>。</summary>
    public const string Patch = "patch";

    public const string Get = "get";
    public const string Click = "click";
    public const string RealClick = "realclick";
    public const string Bounds = "bounds";
    public const string Event = "event";
}
