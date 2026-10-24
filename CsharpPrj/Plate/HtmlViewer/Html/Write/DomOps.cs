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
    /// <summary>桥本身未就绪（脚本注入未完成；与"未知回串"区分，供补注入自愈定向判断）。</summary>
    BridgeNotReady,
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
            "BRIDGE_NOT_READY" => DomOpStatus.BridgeNotReady,
            "NOT_VISIBLE" => DomOpStatus.NotVisible,
            _ => DomOpStatus.Unknown,
        };

    /// <summary>成功判定单点（消费方禁再写 <c>== "OK"</c>）。</summary>
    public static bool IsOk(string? status) => Parse(status) == DomOpStatus.Ok;

    public const string Set = "set";

    /// <summary>流追加原语（append-only 面：日志 / 过程帧 / 气泡）：按项稳定键定点更新同名项、其余插入流首、按 cap 裁尾，
    /// 全程不重建文档（滚动/展开/焦点保持）。与 <see cref="Reconcile"/> 同属写落地原语族，由 <c>__uiReconcile.append</c> 承载。</summary>
    public const string Append = "append";

    /// <summary>流裁剪原语：保留流首 N 项（与 <see cref="Append"/> 的 cap 同语义，供显式裁尾调用）。</summary>
    public const string Trim = "trim";

    /// <summary>键控对齐写（**唯一写原语**；Region/节点树/首挂载整页共用）：**恒**经键控对齐原语（reconcile）对齐活 DOM，
    /// 保留展开/滚动/焦点/选中态（无「对齐原语不可用 → 整块 innerHTML 落地」回退链；原语缺失 ⇒ <c>STATE_NOT_READY</c> 由自愈补注入）。
    /// name 为空 → 目标为 <c>document.body</c>（**仅首挂载整页**）；具名缺失 ⇒ NOT_FOUND（无隐式整页替换）。</summary>
    public const string Reconcile = "reconcile";

    /// <summary>内容补丁（语义等价于 <see cref="Reconcile"/>，收敛为同一原语，避免两条写落地）。
    /// <c>[dep]</c> 保留为向后兼容别名；新代码一律用 <see cref="Reconcile"/>。</summary>
    public const string Patch = Reconcile;

    /// <summary>整体替换原语（第二落地原语，**非回退**）：由 <c>SectionPlan.BulkReplace</c> 显式选择，
    /// 一次 DOM 赋值替代 N 次结构变更；仅对 stable 节，丢节内交互态（滚动锚由原语补偿）。
    /// JS 承载 = <c>__uiReconcile.bulk</c>。</summary>
    public const string Bulk = "bulk";

    /// <summary>批处理原语（一拍一次跨进程）：逐节分派 apply/applyTree/bulk/set/append/trim，
    /// 回串 <c>BATCH:[...]</c>（逐节 outcome）。JS 承载 = <c>__uiReconcile.batch</c>。</summary>
    public const string Batch = "batch";

    public const string Get = "get";
    public const string Click = "click";
    public const string RealClick = "realclick";
    public const string Bounds = "bounds";
    public const string Event = "event";
}
