#nullable enable

using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>框架内嵌通用组件样式（按组件切分为有序模块；拼接序 = 声明序，可由 RegisterStyleModule 覆盖）。</summary>
public sealed partial class HtmlAssetRegistry
{
    /// <summary>状态/语义色 token（组件样式只引用 token，色系单源）。</summary>
    internal const string CssTokens = ":root{--ok:#2da44e;--info:#07b;--danger:#cf222e;--warn:#bf8700;--status-idle:#d0d7de;--status-done:#8b949e;--status-cancelled:#8250df;--error-bg:#fbe3e3;--error-fg:#7a1f1f;}";

    /// <summary>通用样式模块：CssBlock</summary>
    internal const string CssBlock =
        """
        
                /* ── 注记块（默认折叠） ── */
                .note-layer .block-body { border-left: 3px solid var(--border); font-size: 12px; color: var(--muted); max-height: 280px; overflow: auto; }
                .note-layer > .fold-summary { color: var(--muted); font-weight: 600; }
                .block.error .block-body { background: var(--error-bg); color: var(--error-fg); }
                .block.system { align-items: center; }
                .block.system .block-body {
                background: transparent; color: var(--muted);
                border: 1px dashed var(--border); font-size: 13px;
                }
                
        """;

    /// <summary>通用样式模块：CssShell</summary>
    internal const string CssShell =
        """
        /* ── 交互式页面（侧栏 + 主区）── */
                .shell-body { display: flex; gap: 14px; align-items: flex-start; }
                .shell-aside {
                flex: 0 0 176px; width: 176px;
                border-right: 1px solid var(--border); padding-right: 10px;
                display: flex; flex-direction: column; gap: 4px;
                position: sticky; top: 0; max-height: 88vh; overflow-y: auto;
                }
                .panel-title { font-size: 12px; color: var(--muted); letter-spacing: .04em; margin-bottom: 4px; }
                .item {
                text-align: left; padding: 6px 8px; border-radius: 6px;
                border: 1px solid var(--border); background: transparent; color: var(--fg);
                cursor: pointer; font-size: 13px; font-family: inherit;
                }
                .item:hover { background: var(--agent-bg); }
                .item.active { background: var(--user-bg); color: var(--user-fg); border-color: var(--user-bg); }
                .tag-list { display: inline-flex; gap: 3px; flex-wrap: wrap; margin-top: 4px; }
                .tag { font-size: 11px; color: var(--muted); background: var(--agent-bg); padding: 1px 5px; border-radius: 4px; line-height: 1.5; }
                .item.active .tag { background: var(--user-bg); color: var(--user-fg); opacity: 0.8; }
        .shell-main { flex: 1; min-width: 0; }
                .shell-title { font-size: 13px; color: var(--muted); margin-bottom: 4px; letter-spacing: .03em; }
                
        """;

    /// <summary>通用样式模块：CssTree</summary>
    internal const string CssTree =
        """
        /* ── 两级折叠树 ── */
                .fold { margin: 2px 0; }
                .fold-header {
                font-size: 12px; color: var(--muted); cursor: pointer; user-select: none;
                padding: 3px 6px; border-radius: 6px; letter-spacing: .02em;
                }
                .fold-header:hover { background: var(--agent-bg); color: var(--fg); }
                .fold-name { cursor: pointer; font-weight: 600; }
                .fold-name:hover { color: var(--fg); text-decoration: underline; }
                .fold-tags { display: inline; margin-left: 4px; }
                .fold-tags .tag { font-size: 10px; }
                .fold-body { display: flex; flex-direction: column; gap: 3px; padding-left: 6px; margin-top: 2px; }
                
        """;

    /// <summary>通用样式模块：CssSwitch</summary>
    internal const string CssSwitch =
        """
        /* ── 分段切换 ── */
                .seg { display: flex; gap: 6px; flex-wrap: wrap; }
                .seg-item { padding: 5px 14px; border-radius: 6px; border: 1px solid var(--border); background: transparent; color: var(--fg); cursor: pointer; font-size: 13px; font-family: inherit; }
                .seg-item:hover { background: var(--agent-bg); }
                .seg-item.active { background: var(--user-bg); color: var(--user-fg); border-color: var(--user-bg); }
                .shell { display: flex; flex-direction: column; gap: 6px; }
                .mono { font-family: Consolas, monospace; font-size: 12px; line-height: 1.5; white-space: pre-wrap; word-break: break-word; background: var(--agent-bg); color: var(--fg); padding: 8px 10px; border-radius: 6px; }
                
                
        """;

    /// <summary>通用样式模块：CssGrouped</summary>
    internal const string CssGrouped =
        """
        /* ── 分组面板（组 / 行）── */
                .panel { margin-bottom: 12px; padding: 8px 10px; border: 1px solid var(--border); border-radius: 8px; }
                .panel-head { font-size: 12px; color: var(--muted); margin-bottom: 6px; letter-spacing: .03em; }
                .panel-note { font-size: 12px; color: var(--muted); margin-bottom: 6px; padding: 3px 6px; border-left: 3px solid var(--border); }
                .group { margin: 4px 0; }
                .group-head { cursor: pointer; user-select: none; font-size: 12px; padding: 3px 6px; border-radius: 6px; background: var(--agent-bg); }
                .group-badge { font-weight: 600; }
                .group-count { color: var(--muted); margin-left: 6px; font-size: 11px; }
                .group-body { margin-top: 4px; padding-left: 8px; }
                .row { padding: 4px 6px; border-bottom: 1px dashed var(--border); }
                .row-key { font-family: Consolas, monospace; font-size: 12px; }
        .row-title { font-weight: 600; }
                .row-status { font-size: 11px; color: var(--user-fg); background: var(--user-bg); padding: 0 5px; border-radius: 4px; margin-left: 6px; }
                .row-updated { font-size: 11px; color: var(--muted); margin-left: 6px; }
                .row-summary { font-size: 12px; color: var(--muted); margin-top: 2px; white-space: pre-wrap; word-break: break-word; }
                .pane-secondary { }
                
        """;

    /// <summary>通用样式模块：CssState</summary>
    internal const string CssState =
        """
        /* ── 运行状态徽章 + 刷新按钮 ── */
                .item { display: flex; align-items: center; gap: 6px; }
                .item-title { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
                .dot { width: 8px; height: 8px; border-radius: 50%; flex: 0 0 auto; }
                .dot.running { background: var(--ok); animation: statusPulse 1.6s infinite; }
                .dot.paused { background: var(--warn); }
                .dot.done { background: var(--status-done); }
                .dot.error { background: var(--danger); }
                .dot.idle { background: var(--status-idle); }
                .dot.started { background: var(--ok); animation: statusPulse 1.6s infinite; }
                .dot.cancelled { background: var(--status-cancelled); }
                @keyframes statusPulse { 0% { opacity: 1; } 50% { opacity: .35; } 100% { opacity: 1; } }
                .status { font-size: 11px; color: var(--muted); }
                .status.running { color: var(--ok); }
                .status.paused { color: var(--warn); }
                .status.done { color: var(--status-done); }
                .status.error { color: var(--danger); }
                .status.started { color: var(--ok); }
                .status.cancelled { color: var(--status-cancelled); }
                .chip { font-size: 10px; color: var(--fg); background: var(--border); padding: 0 5px; border-radius: 4px; flex: 0 0 auto; }
                .chip.leg { color: var(--muted); background: transparent; border: 1px solid var(--border); font-family: var(--font-mono, monospace); }
                .item.active .chip { background: var(--user-fg); color: var(--user-bg); }
                .item-meta { font-size: 10px; color: var(--muted); flex: 0 0 auto; }
                .fold-count { font-size: 11px; color: var(--muted); margin-left: 6px; }
                .panel-title { display: flex; align-items: center; gap: 6px; }
                .btn-icon { border: 1px solid var(--border); background: transparent; color: var(--fg); cursor: pointer; border-radius: 4px; padding: 1px 6px; font-size: 12px; line-height: 1.4; font-family: inherit; }
                .btn-icon:hover { background: var(--agent-bg); }
                
        """;

    /// <summary>通用样式模块：CssAttach</summary>
    internal const string CssAttach =
        """
        /* ── 附属折叠栏 ── */
                .attach { margin: 2px 6px 0; }
                .attach-toggle { font-size: 12px; color: var(--muted); cursor: pointer; user-select: none; }
                .attach-toggle::before { content: "▸ "; }
                details.attach[open] > .attach-toggle::before { content: "▾ "; }
                .attach-item { border-left: 2px solid var(--border); margin: 6px 0 0 4px; padding-left: 8px; }
                .attach-head { font-size: 12px; color: var(--muted); }
                .attach-badge { display: inline-block; font-size: 10px; line-height: 1.6; color: var(--fg); background: var(--border); border-radius: 4px; padding: 0 5px; margin-right: 6px; }
                .attach-text { font-size: 12px; color: var(--muted); white-space: pre-wrap; word-break: break-word; max-height: 220px; overflow: auto; }
                .attach-group { margin: 8px 0 0; }
                .attach-group-head { display: flex; align-items: center; gap: 6px; padding: 2px 0; border-bottom: 1px dashed var(--border); }
                .attach-group-label { font-size: 11px; font-weight: 600; color: var(--fg); }
                .attach-group-count { font-size: 10px; color: var(--muted); background: var(--border); border-radius: 8px; padding: 0 6px; }
                
                
        """;

    /// <summary>通用样式模块：CssBoard（任务窗口板 + 分发流水；一任务一窗口，窗口行非气泡）</summary>
    internal const string CssBoard =
        """
        /* ── 任务窗口板 ── */
                .board { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 10px; margin: 8px 0 12px; align-items: stretch; }
                .card { display: flex; flex-direction: column; min-width: 0; border: 1px solid var(--border); border-radius: 8px; background: var(--bg); }
                .card.active { border-color: var(--user-bg); box-shadow: 0 0 0 1px var(--user-bg) inset; }
                .card-head { display: flex; align-items: center; flex-wrap: wrap; gap: 6px; padding: 8px 10px; border-bottom: 1px solid var(--border); cursor: pointer; }
                .card-head:hover { background: var(--agent-bg); }
                .card-title { flex: 1 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 600; font-size: 13px; }
                .card-meta { font-size: 11px; color: var(--muted); flex: 0 0 auto; }
                .card-note { padding: 6px 10px 0; font-size: 12px; color: var(--muted); white-space: pre-wrap; word-break: break-word; }
                .card-body { display: flex; flex-direction: column; gap: 4px; padding: 8px 10px; flex: 1 1 auto; }
                .card-empty { font-size: 12px; color: var(--muted); }
                .card-foot { display: flex; gap: 6px; padding: 0 10px 8px; }
                .wl { padding-left: 8px; border-left: 2px solid var(--border); font-size: 12px; line-height: 1.5; }
                .wl.user { border-left-color: var(--user-bg); }
                .wl.assistant { border-left-color: var(--agent-bg); }
                .wl.proc, .wl.system, .wl.error { border-left-style: dashed; }
                .wl-head { display: block; font-size: 11px; color: var(--muted); }
                .wl-text { white-space: pre-wrap; word-break: break-word; max-height: 140px; overflow: auto; }
                /* ── 分发流水（操作流）── */
                .feed { margin: 6px 0 10px; padding: 6px 10px; border: 1px solid var(--border); border-radius: 8px; background: var(--agent-bg); }
                .feed-summary { font-size: 12px; color: var(--muted); cursor: pointer; user-select: none; letter-spacing: .03em; }
                .feed-row { padding: 4px 0; border-top: 1px dashed var(--border); }
                .feed-badge { font-size: 12px; color: var(--muted); }
                .feed-subject, .feed-targets { font-size: 12px; color: var(--muted); margin-left: 4px; }
                .feed-text { font-size: 13px; white-space: pre-wrap; word-break: break-word; max-height: 120px; overflow: auto; }
                
        """;

    /// <summary>通用样式模块：CssData</summary>
    internal const string CssData =
        """
        /* ── 数据视图 ── */
                .data-view { display: flex; flex-direction: column; gap: 8px; }
                .toolbar { position: sticky; top: 0; z-index: 2; padding: 6px 8px; background: var(--bg); border-bottom: 1px solid var(--border); display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
                .toolbar-actions { display: inline-flex; gap: 6px; margin-left: auto; flex-wrap: wrap; }
                .btn { font: inherit; padding: 2px 10px; border: 1px solid var(--border); border-radius: 6px; background: var(--agent-bg); color: var(--fg); cursor: pointer; }
                .btn:hover { border-color: var(--fg); }
                .callout { border: 1px solid var(--border); border-left: 3px solid var(--warn); border-radius: 6px; padding: 6px 10px; }
                .data-row { display: flex; gap: 8px; align-items: flex-start; padding: 4px 6px; border-bottom: 1px dashed var(--border); }
                .data-row .block-badge { flex: 0 0 auto; }
                .data-row .key { font-family: Consolas, monospace; color: var(--muted); }
                .data-row .title { flex: 1 1 auto; font-weight: 600; word-break: break-word; }
                .data-row .summary { flex: 1 1 auto; white-space: pre-wrap; word-break: break-word; color: var(--muted); }
                .data-row .updated { flex: 0 0 auto; font-size: 11px; color: var(--muted); }
                .data-check { flex: 0 0 auto; }
                .data-item { flex: 1 1 auto; }
                .data-summary { cursor: pointer; display: flex; gap: 8px; align-items: baseline; }
                .data-summary::before { content: '\25B8'; color: var(--muted); }
                details.data-item[open] > .data-summary::before { content: '\25BE'; }
                .data-body { padding: 6px 10px 10px 22px; background: var(--agent-bg); border-radius: 6px; }
                .kv-grid { display: grid; grid-template-columns: 96px 1fr; gap: 4px 8px; font-size: 13px; }
                .kv-grid > span:nth-child(odd) { color: var(--muted); }
                .code-input { width: 100%; font-family: Consolas, monospace; min-height: 120px; }
                .block-badge.nt { background: var(--user-bg); color: var(--user-fg); }
                .block-badge.st { background: var(--agent-bg); color: var(--agent-fg); }
                .empty { color: var(--muted); }
                .input { font: inherit; padding: 2px 6px; border: 1px solid var(--border); border-radius: 6px; background: var(--bg); color: var(--fg); }
                .input-page { width: 64px; }
                .composer { display: flex; gap: 8px; align-items: flex-end; margin-top: 10px; padding-top: 8px; border-top: 1px solid var(--border); }
                .input-lg { flex: 1; resize: vertical; min-height: 44px; font: inherit; padding: 6px 8px; border: 1px solid var(--border); border-radius: 8px; background: var(--bg); color: var(--fg); }
                .btn-primary { padding: 8px 16px; border: 1px solid var(--border); border-radius: 8px; background: var(--user-bg); color: var(--user-fg); cursor: pointer; font: inherit; }
                .btn-primary:hover { filter: brightness(0.97); }
                .overlay-note { font-size: 12px; color: var(--muted); margin-bottom: 8px; }
                .overlay-item { border-left: 3px solid var(--border); margin: 8px 0; padding-left: 8px; }
                .overlay-head { font-size: 12px; color: var(--muted); font-weight: 600; }
                .overlay-body { white-space: pre-wrap; word-break: break-word; font-family: Consolas, monospace; font-size: 12px; margin: 4px 0 0; max-height: 320px; overflow: auto; }
                .state-panel { border:1px solid var(--border); border-radius:8px; margin:6px 0 10px; padding:8px 10px; background:var(--agent-bg); }
                .panel-head { font-size:12px; color:var(--muted); letter-spacing:.04em; margin-bottom:6px; }
                .state-row { display:flex; flex-wrap:wrap; align-items:center; gap:8px; font-size:13px; padding:2px 0; }
                .state-row .state-meta { color:var(--muted); font-size:12px; }
                .state-note { color:var(--muted); font-size:12px; }
                .state-tag, .state-tag { font-size:11px; border:1px solid var(--border); border-radius:6px; padding:0 5px; color:var(--muted); }
                .state-tag { color:var(--ok); border-color:var(--ok); }
                .state-tag.running { color:var(--ok); border-color:var(--ok); }
                .state-tag.completed { color:var(--info); border-color:var(--info); }
                .state-tag.faulted { color:var(--danger); border-color:var(--danger); }
                .state-tag.paused { color:var(--warn); border-color:var(--warn); }
                .state-tag.cancelled { color:var(--status-done); }
                
        """;

    /// <summary>通用样式模块：CssDataPageChrome（分组工具条 / 筛选条 / 选择条 / 页脚分页）</summary>
    internal const string CssDataPageChrome =
        """
        /* ── 数据页区域（分组工具条 / 筛选 / 选择 / 页脚） ── */
        .toolbar-leading { color: var(--muted); font-size: 12px; }
        .toolbar-group { display: inline-flex; align-items: center; gap: 6px; flex-wrap: wrap; }
        .toolbar-group-label { font-size: 11px; color: var(--muted); letter-spacing: .04em; }
        .toolbar-sep { display: inline-block; width: 1px; height: 16px; background: var(--border); margin: 0 2px; align-self: center; }
        .toolbar-sep-strong { background: var(--danger); opacity: .45; }
        .toolbar-more { position: relative; display: inline-flex; }
        .toolbar-more-summary { list-style: none; cursor: pointer; padding: 2px 8px; border: 1px solid var(--border); border-radius: 6px; background: var(--agent-bg); color: var(--fg); font-size: 12px; }
        .toolbar-more-summary::-webkit-details-marker { display: none; }
        .toolbar-more-summary::after { content: " ▾"; color: var(--muted); }
        .toolbar-more[open] > .toolbar-more-panel { display: flex; flex-direction: column; gap: 6px; position: absolute; right: 0; top: calc(100% + 4px); z-index: 8; min-width: 168px; padding: 8px; background: var(--bg); border: 1px solid var(--border); border-radius: 8px; box-shadow: 0 6px 18px rgba(0,0,0,.18); }
        .toolbar-more-panel { display: none; }
        .toolbar-more-panel .toolbar-sep { display: none; }
        .btn-ghost { background: transparent; color: var(--muted); }
        .btn-ghost:hover { color: var(--fg); }
        .btn-action-primary { background: var(--user-bg); color: var(--user-fg); border-color: var(--user-bg); }
        .btn-danger { color: var(--danger); border-color: var(--danger); background: transparent; }
        .btn-danger:hover { background: var(--error-bg); }
        .btn[aria-pressed="true"] { box-shadow: inset 0 0 0 1px var(--fg); }
        .btn[data-pending="1"] { outline: 2px solid var(--warn); outline-offset: 1px; }
        .btn:disabled { opacity: .55; cursor: not-allowed; }
        .filter-bar, .selection-bar, .pager { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; padding: 4px 8px; border-bottom: 1px dashed var(--border); }
        .filter-bar { background: var(--agent-bg); border-radius: 6px; }
        .filter-label, .selection-summary, .pager-info { font-size: 12px; color: var(--muted); }
        .pager { justify-content: flex-end; border-bottom: 0; border-top: 1px solid var(--border); }
        .pager-group, .pager-goto { display: inline-flex; align-items: center; gap: 6px; }
        .pager-info { padding: 0 4px; }
        .filter-bar .input, .pager .input { font: inherit; padding: 2px 6px; border: 1px solid var(--border); border-radius: 6px; background: var(--bg); color: var(--fg); }
        @media (max-width: 720px) {
            .toolbar-group-label { display: none; }
            .toolbar-leading { font-size: 11px; }
            .filter-bar .btn, .selection-bar .btn, .pager .btn { padding: 2px 6px; }
        }

        """;

    /// <summary>模块名 → 模块 CSS（内置默认；可经 RegisterStyleModule 覆盖）。</summary>
    internal static readonly IReadOnlyDictionary<string, string> CssModules =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["tokens"] = CssTokens,
            ["block"] = CssBlock,
            ["shell"] = CssShell,
            ["tree"] = CssTree,
            ["switch"] = CssSwitch,
            ["grouped"] = CssGrouped,
            ["state"] = CssState,
            ["board"] = CssBoard,
            ["attach"] = CssAttach,
            ["data"] = CssData,
            ["datachrome"] = CssDataPageChrome,
        };

    /// <summary>通用组件样式组合顺序（模块名有序；顺序固定 = 级联稳定）。</summary>
    internal static readonly IReadOnlyList<string> FrameworkComponentCssOrder = new[]
    {
        "tokens", "block", "shell", "tree", "switch", "grouped", "state", "board", "attach", "data", "datachrome",
    };
}
