#nullable enable

using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 默认前端资产注册表——HtmlViewer 全部内嵌 JS/CSS/JS 模板的唯一真源（从方法体 const 字符串提升为具名资产）。
/// 静态方法供静态渲染器（HtmlDocumentRenderer / HtmlLayoutProjector / HtmlControlFactory）直接取用；
/// 实例实现 <see cref="IHtmlAssetProvider"/> 供执行层 / 生命周期层注入（IoC 可替换为外部源）。
/// </summary>
public sealed class HtmlAssetRegistry : IHtmlAssetProvider
{
    /// <summary>默认单例（组合根未显式装配时的兜底）。</summary>
    public static IHtmlAssetProvider Default { get; } = new HtmlAssetRegistry();

    private static readonly IReadOnlyDictionary<string, string> Scripts = new Dictionary<string, string>
    {
        [HtmlAssetKeys.BridgeScript] = BridgeScript,
        [HtmlAssetKeys.TabSwapScript] = TabSwapScript,
        [HtmlAssetKeys.PollBridgeScript] = PollBridgeScript,
    };

    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>
    {
        [HtmlAssetKeys.DomHandleTemplate] = DomHandleTemplate,
        [HtmlAssetKeys.RectQueryTemplate] = RectQueryTemplate,
        [HtmlAssetKeys.BridgeDiagnosisTemplate] = BridgeDiagnosisTemplate,
    };

    private static readonly IReadOnlyDictionary<string, string> Styles = new Dictionary<string, string>
    {
        [HtmlAssetKeys.DefaultCss] = DefaultCss,
        [HtmlAssetKeys.BaseCss] = BaseCss,
    };

    public static string GetScript(string key) => Scripts.TryGetValue(key, out var v) ? v : string.Empty;
    public static string GetStyle(string key) => Styles.TryGetValue(key, out var v) ? v : string.Empty;
    public static string GetTemplate(string key) => Templates.TryGetValue(key, out var v) ? v : string.Empty;

    string IHtmlAssetProvider.GetScript(string key) => GetScript(key);
    string IHtmlAssetProvider.GetStyle(string key) => GetStyle(key);
    string IHtmlAssetProvider.GetTemplate(string key) => GetTemplate(key);

    /// <summary>桥梁脚本：定义 <c>window.__domBridge</c>（受控 DOM 操作 handle：set/get/click/bounds/event）+ 点击桥（[data-cmd] → postMessage）。
    /// 幂等守卫用 <c>document.__domBridgeInstalled</c>（**document 级**，非 window 级）：WebView2 经 NavigateToString 重渲染会替换 document 而保留 window，
    /// 若守卫挂在 window 上会因「window 持久 → 误判已注入」而不再为新 document 重新挂载 document 级点击监听器 → [data-cmd] 点击转 postMessage 失效
    /// （8.26 实证：回流探针正常但按钮点击无效）。document 级守卫保证每个新 document 都重新注入桥、重新挂载点击监听器。</summary>
    internal const string BridgeScript =
        "(function(){"
        + "if(document.__domBridgeInstalled){return;}"
        + "document.__domBridgeInstalled=true;"
        + "window.__domBridge={handle:function(op,name,value){"
        + "var el=name?document.querySelector('[data-name=\"'+name+'\"]'):null;"
        + "if(!el)return 'NOT_FOUND';"
        + "var isForm=el.tagName==='SELECT'||el.tagName==='INPUT'||el.tagName==='TEXTAREA';"
        + "switch(op){"
        + "case 'set':if(isForm){el.value=value;}else if(el.classList.contains('host')){el.innerHTML=value;}else{el.textContent=value;}return 'OK';"
        + "case 'get':if(isForm){return el.value;}else if(el.classList.contains('list')){var s=el.querySelector('li.selected');return s?s.textContent:'';}else{return el.textContent;}"
        + "case 'click':el.dispatchEvent(new MouseEvent('click',{bubbles:true}));return 'OK';"
        + "case 'bounds':var r=value?JSON.parse(value):null;if(r){el.style.position='absolute';el.style.left=r.x+'px';el.style.top=r.y+'px';el.style.width=r.w+'px';el.style.height=r.h+'px';}return 'OK';"
        + "case 'event':el.dispatchEvent(new Event(value,{bubbles:true}));return 'OK';"
        + "default:return 'UNKNOWN_OP';"
        + "}}};"
        + "document.addEventListener('click',function(e){"
        + "var t=(e.target&&e.target.closest)?e.target.closest('[data-cmd]'):null;"
        + "if(!t)return;"
        + "var cmd=t.getAttribute('data-cmd');"
        + "if(!cmd)return;"
        + "if(cmd==='selectHistory'){"
        + "var r=t.closest('.round')||t.closest('[data-round]');"
        + "if(r){r.scrollIntoView({behavior:'smooth',block:'start'});}"
        + "e.preventDefault();return;}"
        + "if(cmd.indexOf('event:')===0){"
        + "window.chrome.webview.postMessage({event:cmd.substring(6),name:t.getAttribute('data-name'),payload:t.getAttribute('data-payload')});"
        + "e.preventDefault();return;}"
        + "window.chrome.webview.postMessage({"
        + "cmd:cmd,"
        + "session:t.getAttribute('data-session'),"
        + "history:t.getAttribute('data-history'),"
        + "round:t.getAttribute('data-round'),"
        + "type:t.getAttribute('data-type')"
        + "});"
        + "e.preventDefault();"
        + "});})();";

    /// <summary>tab 切换脚本（事件委托、幂等守卫）：点击 tab → 同容器内切换 pane 显隐。渲染端自产自收，纯客户端展示逻辑。</summary>
    internal const string TabSwapScript =
        "(function(){if(document.__tabCtrlInit)return;document.__tabCtrlInit=true;"
        + "document.addEventListener('click',function(e){"
        + "var b=(e.target&&e.target.closest)?e.target.closest('.tabcontrol [data-tab]'):null;"
        + "if(!b)return;var tc=b.closest('.tabcontrol');if(!tc)return;var idx=b.getAttribute('data-tab');"
        + "Array.prototype.forEach.call(tc.querySelectorAll('[data-tab]'),function(x){x.classList.toggle('active',x===b);});"
        + "Array.prototype.forEach.call(tc.querySelectorAll('.pane'),function(p){p.classList.toggle('active',p.getAttribute('data-pane')===idx);});"
        + "});})();";

    /// <summary>
    /// 通用轮询/手动刷新桥（document 级幂等守卫 <c>__htmlPollInstalled</c>，重渲染即重新挂载，跨刷新不叠加定时器）。
    /// 纯数据驱动、零业务耦合：页面作者仅需在任意元素上声明——<c>[data-poll-cmd]</c>（要投递的 cmd，如 refresh）+ 可选
    /// <c>[data-poll-interval]</c>（轮询间隔 ms，默认 30000）+ 可选 <c>[data-poll-ctx]</c>/<c>[data-poll-ctx-attrs]</c>
    /// （从指定选择器元素复制其 data-* 属性并入消息，用于保持当前选中态）；手动刷新按钮加 <c>[data-poll-refresh]</c>（点击即触发一次）。
    /// 消息经 <c>window.chrome.webview.postMessage</c> 回宿主，宿主既有 MessageReceived→events.message 路由接管（refreshed 动作由宿主定义）。
    /// 注入经 <see cref="HtmlScriptHost.EnsureBridgeLoadedAsync"/> 随点击桥一起在导航后完成（本脚本不经过模板引擎，可含花括号）。
    /// </summary>
    internal const string PollBridgeScript =
        "(function(){"
        + "if(document.__htmlPollInstalled){return;}"
        + "document.__htmlPollInstalled=true;"
        + "function pst(o){if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(o);}}"
        + "function ctxOf(n){"
        + "var sel=n.getAttribute('data-poll-ctx');if(!sel){return null;}"
        + "var el=document.querySelector(sel);if(!el){return null;}"
        + "var attrs=(n.getAttribute('data-poll-ctx-attrs')||'').split(',');"
        + "var o=new Object();for(var i=0;i<attrs.length;i++){"
        + "var a=(attrs[i]||'').trim();if(!a){continue;}"
        + "var v=el.getAttribute('data-'+a);if(v!==null){o[a]=v;}"
        + "}return o;}"
        + "function fire(n){"
        + "var cmd=n.getAttribute('data-poll-cmd');if(!cmd){cmd='refresh';}"
        + "var o=new Object();o.cmd=cmd;var ctx=ctxOf(n);"
        + "if(ctx){for(var k in ctx){o[k]=ctx[k];}}pst(o);}"
        // 定时器选择器排除手动刷新按钮（data-poll-refresh）：按钮只做点击触发，挂同一定时器会造成每周期重复派发/重复渲染。
        + "var list=document.querySelectorAll('[data-poll-cmd]:not([data-poll-refresh])');"
        + "for(var i=0;i<list.length;i++){(function(n){"
        + "var iv=parseInt(n.getAttribute('data-poll-interval'),10);if(!(iv>0)){iv=30000;}"
        + "window.setInterval(function(){fire(n);},iv);"
        + "})(list[i]);}"
        + "var rbs=document.querySelectorAll('[data-poll-refresh]');"
        + "for(var j=0;j<rbs.length;j++){(function(b){"
        + "b.addEventListener('click',function(e){fire(b);if(e){e.preventDefault();}});"
        + "})(rbs[j]);}"
        + "})();";

    /// <summary>DOM 指令执行模板：$OP/$NAME/$VALUE 由执行层 JSON 序列化展开（防引号转义漂移）。</summary>
    internal const string DomHandleTemplate = "window.__domBridge.handle($OP,$NAME,$VALUE);";

    /// <summary>真实点击坐标查询模板：读元素中心 + DPR；$NAME 由执行层 JSON 展开。NOT_FOUND 附文档诊断后缀。</summary>
    internal const string RectQueryTemplate =
        "(function(){var nm=$NAME;"
        + "var el=document.querySelector('[data-name=\"'+nm+'\"]');"
        + "if(!el){var names=Array.from(document.querySelectorAll('[data-name]')).map(function(e){return e.getAttribute('data-name');}).join(',');"
        + "var bodyLen=(document.body&&document.body.innerHTML)?document.body.innerHTML.length:0;"
        + "return 'NOT_FOUND|names='+names+'|body='+bodyLen+'|state='+document.readyState+'|href='+location.href;}"
        + "var r=el.getBoundingClientRect();"
        + "if(!r||(r.width===0&&r.height===0))return 'NOT_VISIBLE';"
        + "return JSON.stringify({x:r.left+r.width/2,y:r.top+r.height/2,dpr:window.devicePixelRatio||1});})()";

    /// <summary>桥注入失败时的 document 诊断模板（readyState/href/body/__domBridgeInstalled/chrome.webview 可用性）。</summary>
    internal const string BridgeDiagnosisTemplate =
        "JSON.stringify({ready:document.readyState,href:location.href,body:!!document.body,"
        + "installed:!!document.__domBridgeInstalled,chromeWebview:!!(window.chrome&&window.chrome.webview)});";

    /// <summary>默认主题 CSS：气泡式对话布局 + 深浅双主题（跟随系统）+ CJK 排版。见 <see cref="HtmlDocumentRenderer"/>。</summary>
    internal const string DefaultCss =
        """
        :root {
            --bg: #ffffff;
            --fg: #24292f;
            --muted: #57606a;
            --border: #d0d7de;
            --user-bg: #dbe7ff;
            --user-fg: #0b2a52;
            --agent-bg: #f1f2f4;
            --agent-fg: #1f2328;
        }
        @media (prefers-color-scheme: dark) {
            :root {
                --bg: #0d1117;
                --fg: #e6edf3;
                --muted: #8b949e;
                --border: #30363d;
                --user-bg: #1f3a5f;
                --user-fg: #e6edf3;
                --agent-bg: #21262d;
                --agent-fg: #e6edf3;
            }
        }
        * { box-sizing: border-box; }
        body {
            font-family: "Segoe UI", "Microsoft YaHei", sans-serif;
            margin: 0; padding: 14px 16px;
            font-size: 15px; line-height: 1.7;
            color: var(--fg); background: var(--bg);
            word-wrap: break-word;
        }
        /* ── 气泡式对话 ── */
        .msg { display: flex; flex-direction: column; margin: 10px 0; }
        .msg.user { align-items: flex-end; }
        .msg.agent { align-items: flex-start; }
        .badge {
            font-size: 12px; color: var(--muted);
            margin: 0 6px 3px; letter-spacing: .02em;
        }
        .bubble {
            max-width: 86%; padding: 8px 12px;
            border-radius: 10px;
            white-space: pre-wrap; word-break: break-word;
        }
        .msg.user .bubble {
            background: var(--user-bg); color: var(--user-fg);
            border-top-right-radius: 2px;
        }
        .msg.agent .bubble {
            background: var(--agent-bg); color: var(--agent-fg);
            border-top-left-radius: 2px;
        }
        .empty { color: var(--muted); text-align: center; margin-top: 24px; }
        /* ── 层次化对话 ── */
        .round { margin: 4px 0 14px; }
        .round-header {
            font-size: 12px; color: var(--muted);
            margin: 8px 0 4px; padding-bottom: 3px;
            letter-spacing: .04em; cursor: pointer; user-select: none;
            border-bottom: 1px solid var(--border);
        }
        .round-header:hover { color: var(--fg); }
        .msg.tool .tool-layer { margin: 0 6px; }
        .tool-summary { font-size: 12px; color: var(--muted); cursor: pointer; user-select: none; }
        .tool-summary::before { content: "▸ "; }
        details.tool-layer[open] > .tool-summary::before { content: "▾ "; }
        .msg.error .bubble { background: #fbe3e3; color: #7a1f1f; }
        .msg.system { align-items: center; }
        .msg.system .bubble {
            background: transparent; color: var(--muted);
            border: 1px dashed var(--border); font-size: 13px;
        }
        /* ── 交互式会话页（task 侧栏 + 历史）── */
        .conv-layout { display: flex; gap: 14px; align-items: flex-start; }
        .session-bar {
            flex: 0 0 176px; width: 176px;
            border-right: 1px solid var(--border); padding-right: 10px;
            display: flex; flex-direction: column; gap: 4px;
            position: sticky; top: 0; max-height: 88vh; overflow-y: auto;
        }
        .bar-title { font-size: 12px; color: var(--muted); letter-spacing: .04em; margin-bottom: 4px; }
        .task-item {
            text-align: left; padding: 6px 8px; border-radius: 6px;
            border: 1px solid var(--border); background: transparent; color: var(--fg);
            cursor: pointer; font-size: 13px; font-family: inherit;
        }
        .task-item:hover { background: var(--agent-bg); }
        .task-item.active { background: var(--user-bg); color: var(--user-fg); border-color: var(--user-bg); }
        .conv-main { flex: 1; min-width: 0; }
        .conv-title { font-size: 13px; color: var(--muted); margin-bottom: 4px; letter-spacing: .03em; }
        /* ── 历史 → 任务 两级树（History = Task 更上层容器，归拢多 task）── */
        .history-item { margin: 2px 0; }
        .history-header {
            font-size: 12px; color: var(--muted); cursor: pointer; user-select: none;
            padding: 3px 6px; border-radius: 6px; letter-spacing: .02em;
        }
        .history-header:hover { background: var(--agent-bg); color: var(--fg); }
        .history-tasks { display: flex; flex-direction: column; gap: 3px; padding-left: 6px; margin-top: 2px; }
        """;

    /// <summary>基础组件主题 CSS（HTML 布局投影；不承担布局定位，仅组件默认形态）。见 <see cref="HtmlLayoutProjector"/>。</summary>
    internal const string BaseCss =
        ".ui-root{overflow:hidden;box-sizing:border-box}" +
        ".ui-root.flow{display:flex;flex-direction:column;gap:6px;overflow:auto}" +
        ".ui-root.flow .container{display:flex;flex-direction:column;gap:6px}" +
        ".ui-root .container{overflow:auto;box-sizing:border-box}" +
        ".ui-root .host{overflow:auto;box-sizing:border-box}" +
        ".ui-root .list{list-style:none;margin:0;padding:2px;box-sizing:border-box}" +
        ".ui-root .tree{list-style:none;margin:0;padding:2px;box-sizing:border-box}" +
        ".ui-root .tabcontrol{display:flex;flex-direction:column;box-sizing:border-box}" +
        ".ui-root .tabbar{display:flex;gap:4px;flex:0 0 auto}" +
        ".ui-root .tab{padding:2px 8px;border:1px solid #ccc;background:#eee;cursor:pointer}" +
        ".ui-root .tab.active{background:#fff;border-bottom-color:#fff}" +
        ".ui-root .panes{flex:1 1 auto;position:relative;overflow:auto;box-sizing:border-box}" +
        ".ui-root .pane{display:none;position:relative;box-sizing:border-box}" +
        ".ui-root .pane.active{display:block}";
}