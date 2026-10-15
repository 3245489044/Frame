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
                .attach-text { font-size: 12px; color: var(--muted); white-space: pre-wrap; word-break: break-word; max-height: 220px; overflow: auto; }
                
                
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
            ["attach"] = CssAttach,
            ["data"] = CssData,
        };

    /// <summary>通用组件样式组合顺序（模块名有序；顺序固定 = 级联稳定）。</summary>
    internal static readonly IReadOnlyList<string> FrameworkComponentCssOrder = new[]
    {
        "tokens", "block", "shell", "tree", "switch", "grouped", "state", "attach", "data",
    };
}
