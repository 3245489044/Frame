namespace UiBackend.Html;

/// <summary>
/// DOM 指令结果状态（D3 类型化）：由 JS 桥回串（OK/NOT_FOUND/STATE_NOT_READY/NOT_VISIBLE）映射为受控枚举，
/// 使副作用判定不再以裸串参与控制流（<c>string.Equals(status,"OK")</c> 脆弱且不可类型检查）。
/// <para>未知回串 → <see cref="Unknown"/>（不抛，保持旧判定等价：仅 Ok 视为成功）。</para>
/// </summary>
public enum DomOpStatus
{
    /// <summary>未识别回串（保守：非成功）。</summary>
    Unknown = 0,
    /// <summary>指令命中并生效。</summary>
    Ok,
    /// <summary>具名目标未找到（D4：区域缺失的显式结果，不再回退整页）。</summary>
    NotFound,
    /// <summary>桥补丁态模块未就绪（写回层据此按显式策略处置，非静默丢刷新）。</summary>
    StateNotReady,
    /// <summary>元素存在但不可见（真实点击坐标不可解析）。</summary>
    NotVisible,
}

/// <summary>IHtmlChannel 契约的受控 DOM 操作白名单（与 HtmlViewer 注入的 __domBridge JS 分支一一对应；消除各桥 magic string 漂移）。</summary>
public static class DomOps
{
    /// <summary>JS 桥回串 → 类型化状态（单一映射入口；与 __domBridge.handle 返回值约定一致）。</summary>
    public static DomOpStatus Parse(string? status)
        => status switch
        {
            "OK" => DomOpStatus.Ok,
            "NOT_FOUND" => DomOpStatus.NotFound,
            "STATE_NOT_READY" => DomOpStatus.StateNotReady,
            "NOT_VISIBLE" => DomOpStatus.NotVisible,
            _ => DomOpStatus.Unknown,
        };

    /// <summary>成功判定单点（消费方禁再写 <c>== "OK"</c>）。</summary>
    public static bool IsOk(string? status) => Parse(status) == DomOpStatus.Ok;

    public const string Set = "set";

    /// <summary>内容补丁（不重载文档）：默认经键控对齐原语（reconcile）对齐活 DOM，保留展开/滚动/焦点/选中态；
    /// 对齐原语不可用时回退补丁态保存/恢复。name 为空 → 目标为 <c>document.body</c>。具名缺失 ⇒ NOT_FOUND（无隐式整页替换）。</summary>
    public const string Patch = "patch";

    /// <summary>键控对齐写（Region/节点树共用；语义同 <see cref="Patch"/>，显式区分「按稳定键对齐」意图）。</summary>
    public const string Reconcile = "reconcile";

    public const string Get = "get";
    public const string Click = "click";
    public const string RealClick = "realclick";
    public const string Bounds = "bounds";
    public const string Event = "event";
}
