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
        [HtmlAssetKeys.RefreshBridgeScript] = RefreshBridgeScript,
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
        + "if(!el&&op!=='patch')return 'NOT_FOUND';"
        + "var isForm=!!el&&(el.tagName==='SELECT'||el.tagName==='INPUT'||el.tagName==='TEXTAREA');"
        + "switch(op){"
        + "case 'set':if(isForm){el.value=value;}else if(el.classList.contains('host')){el.innerHTML=value;}else{el.textContent=value;}return 'OK';"
        // patch（不重载文档）：目标=name 命中元素，缺省 document.body；替换 innerHTML 前保存 / 后恢复
        // 滚动位置（含"原贴底则贴底"流式跟随）、<details> 展开态与焦点/选区 —— 消除刷新重置 UI 位置。
        + "case 'patch':var pt=el||document.body;if(!pt){return 'NOT_FOUND';}__applyPatch(pt,value);return 'OK';"
        + "case 'get':if(isForm){return el.value;}else if(el.classList.contains('list')){var s=el.querySelector('li.selected');return s?s.textContent:'';}else{return el.textContent;}"
        + "case 'click':el.dispatchEvent(new MouseEvent('click',{bubbles:true}));return 'OK';"
        + "case 'bounds':var r=value?JSON.parse(value):null;if(r){el.style.position='absolute';el.style.left=r.x+'px';el.style.top=r.y+'px';el.style.width=r.w+'px';el.style.height=r.h+'px';}return 'OK';"
        + "case 'event':el.dispatchEvent(new Event(value,{bubbles:true}));return 'OK';"
        + "default:return 'UNKNOWN_OP';"
        + "}}};"
        // ── 补丁态保存 / 恢复（__pk 取稳定键：优先 data-*/id，回退子索引路径；__fk 按键回查新内容）──
        + "function __pk(n,root){"
        + "var ks=['data-name','data-history','data-session','data-round','data-pane','id'];"
        + "for(var i=0;i<ks.length;i++){var v=(n.getAttribute?n.getAttribute(ks[i]):null);if(v){return ks[i]+'='+v;}}"
        + "var path=[];var cur=n;"
        + "while(cur&&cur!==root&&cur.parentNode){path.unshift(Array.prototype.indexOf.call(cur.parentNode.children,cur));cur=cur.parentNode;}"
        + "return 'p:'+path.join('.');}"
        + "function __fk(root,key){"
        + "if(key.indexOf('p:')===0){var segs=key.substring(2).split('.');var cur=root;"
        + "for(var i=0;i<segs.length;i++){var j=parseInt(segs[i],10);if(!(j>=0)||!cur.children||!cur.children[j]){return null;}cur=cur.children[j];}return cur;}"
        + "var eq=key.indexOf('=');var a=key.substring(0,eq);var v=key.substring(eq+1);"
        + "if(a==='id'){var e=document.getElementById(v);return (e&&root.contains(e))?e:null;}"
        + "return root.querySelector('['+a+'='+JSON.stringify(v)+']');}"
        + "function __savePatch(root){"
        + "var st={scroll:[],open:[],focus:null,sel:null,doc:null};"
        // 文档级滚动容器（body 补丁会重建内容，documentElement.scrollingElement 滚动量会被钳回 0）——须独立保存。
        + "var se0=document.scrollingElement||document.documentElement;"
        + "if(se0){var ty=(window.scrollY||window.pageYOffset||0),tx=(window.scrollX||window.pageXOffset||0);"
        + "if(se0.scrollTop>0||se0.scrollLeft>0||ty>0||tx>0){st.doc=[se0.scrollTop,se0.scrollLeft,ty,tx];}}"
        // 祖先链滚动（补丁根为子树时，其祖先滚动容器的位置同样会因高度变化被钳）——上溯保存。
        + "st.anc=[];var up=root.parentNode;"
        + "while(up&&up.nodeType===1){if(up.scrollTop>0||up.scrollLeft>0){st.anc.push({n:up,t:up.scrollTop,l:up.scrollLeft,b:(up.scrollHeight-up.clientHeight-up.scrollTop)<=2});}up=up.parentNode;}"
        + "var nodes=root.querySelectorAll('*');var all=[root];"
        + "for(var i=0;i<nodes.length;i++){all.push(nodes[i]);}"
        + "for(var j=0;j<all.length;j++){var n=all[j];"
        + "if(n.scrollTop>0||n.scrollLeft>0){st.scroll.push({k:__pk(n,root),t:n.scrollTop,l:n.scrollLeft,b:(n.scrollHeight-n.clientHeight-n.scrollTop)<=2});}"
        + "if(n.tagName==='DETAILS'&&n.open){st.open.push(__pk(n,root));}}"
        + "var ae=document.activeElement;"
        + "if(ae&&ae!==root&&root.contains(ae)){st.focus=__pk(ae,root);try{st.sel=[ae.selectionStart,ae.selectionEnd];}catch(e){st.sel=null;}}"
        + "return st;}"
        + "function __restorePatch(root,st){"
        + "if(!st){return;}"
        + "for(var i=0;i<st.open.length;i++){var d=__fk(root,st.open[i]);if(d){d.open=true;}}"
        + "for(var j=0;j<st.scroll.length;j++){var it=st.scroll[j];var e=__fk(root,it.k);if(!e){continue;}"
        + "if(it.b){e.scrollTop=e.scrollHeight;}else{e.scrollTop=it.t;e.scrollLeft=it.l;}}"
        + "for(var a=0;a<st.anc.length;a++){var an=st.anc[a];if(an.b){an.n.scrollTop=an.n.scrollHeight;}else{an.n.scrollTop=an.t;an.n.scrollLeft=an.l;}}"
        // 文档滚动容器恢复：同步置位 + rAF 复置（innerHTML 重建后同步赋值可能因未布局被钳，rAF 兜底）。
        + "if(st.doc){var se2=document.scrollingElement||document.documentElement;var rs=function(){if(se2){se2.scrollTop=st.doc[0];se2.scrollLeft=st.doc[1];}window.scrollTo(st.doc[3],st.doc[2]);};rs();if(window.requestAnimationFrame){window.requestAnimationFrame(rs);}}"
        + "if(st.focus){var f=__fk(root,st.focus);if(f&&f.focus){try{f.focus();if(st.sel&&f.setSelectionRange){f.setSelectionRange(st.sel[0],st.sel[1]);}}catch(e){}}}}"
        + "function __applyPatch(root,html){var st=__savePatch(root);root.innerHTML=html;__restorePatch(root,st);return 'OK';}"
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
        + "var v=null;var di=t.getAttribute('data-input');"
        + "if(di){var ie=document.querySelector('[data-name=\\\"'+di+'\\\"]')||document.getElementById(di);"
        + "if(ie){v=('value'in ie)?ie.value:(ie.isContentEditable?ie.innerText:ie.textContent);}}"
        + "window.chrome.webview.postMessage({"
        + "cmd:cmd,"
        + "session:t.getAttribute('data-session'),"
        + "history:t.getAttribute('data-history'),"
        + "round:t.getAttribute('data-round'),"
        + "type:t.getAttribute('data-type'),"
        + "value:v"
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
    /// 反向刷新桥（document 级幂等守卫 <c>__htmlRefreshInstalled</c>，重渲染即重新挂载，跨刷新不叠加定时器）。
    /// 纯数据驱动、零业务耦合，页面作者声明——<c>[data-poll-cmd]</c>（回投的 cmd，如 refresh）+ 可选
    /// <c>[data-refresh-throttle]</c>（**刷新频率上限**，默认 1000ms，trailing 合并保证最后一次变更必达）+
    /// 可选 <c>[data-poll-ctx]</c>/<c>[data-poll-ctx-attrs]</c>（从选择器元素复制 data-* 属性并入消息，保持选中态）+ 
    /// 可选 <c>[data-poll-interval]</c>（**兜底慢轮询**：仅在未收到任何宿主推送前生效，收到推送即停；默认 30000）；
    /// 手动刷新按钮加 <c>[data-poll-refresh]</c>（点击即触发，不受节流限制）。
    /// <para><b>方向反转</b>：刷新由宿主经 <see cref="IHtmlChannel.PostToPageAsync"/> 推送 <c>{cmd:'refresh'}</c> 驱动
    /// （状态变化即刷，最坏延迟 = 节流窗口 1s），页面不再定长拉取；推送通道不可用时回退兜底慢轮询（at-least-once，不静默失刷）。</para>
    /// 消息经 <c>window.chrome.webview.postMessage</c> 回宿主，宿主既有 MessageReceived→events.message 路由接管。
    /// 注入经 <see cref="HtmlScriptHost.EnsureBridgeLoadedAsync"/> 随点击桥一起在导航后完成（本脚本不经过模板引擎，可含花括号）。
    /// </summary>
    internal const string RefreshBridgeScript =
        "(function(){"
        + "if(document.__htmlRefreshInstalled){return;}"
        + "document.__htmlRefreshInstalled=true;"
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
        // ── 1s 刷新限制（反向节流）：空闲即刷；窗口内合并，窗口到期补刷最后一次（trailing 必达）──
        + "var last=-1e9;var pendNode=null;var pendTimer=null;"
        + "function nowMs(){return (window.performance&&window.performance.now)?window.performance.now():Date.now();}"
        + "function throttleOf(n){var v=parseInt(n.getAttribute('data-refresh-throttle'),10);return v>0?v:1000;}"
        + "function pickNode(){return document.querySelector('[data-poll-cmd=\"refresh\"]:not([data-poll-refresh])')||document.querySelector('[data-poll-cmd]');}"
        + "function refreshNow(){"
        + "var n=pickNode();if(!n){return;}"
        + "var t=nowMs();var wait=last+throttleOf(n)-t;"
        + "if(wait<=0){last=t;fire(n);return;}"
        + "pendNode=n;if(pendTimer){return;}"
        + "pendTimer=window.setTimeout(function(){pendTimer=null;var node=pendNode;pendNode=null;last=nowMs();if(node){fire(node);}},wait);}"
        + "var pushSeen=false;var fallbackTimer=null;"
        + "function stopFallback(){if(fallbackTimer){window.clearInterval(fallbackTimer);fallbackTimer=null;}}"
        // 宿主推送监听（反向刷新入口）：仅响应 {cmd:'refresh'}；收到即停兜底轮询
        + "if(window.chrome&&window.chrome.webview&&window.chrome.webview.addEventListener){"
        + "window.chrome.webview.addEventListener('message',function(e){"
        + "var d=e?e.data:null;if(!d){return;}"
        + "var cmd=(typeof d==='string')?d:(d.cmd||'');"
        + "if(cmd!=='refresh'){return;}"
        + "pushSeen=true;stopFallback();refreshNow();"
        + "});}"
        // 手动刷新按钮：点击即刷（不受 1s 节流限制）
        + "var rbs=document.querySelectorAll('[data-poll-refresh]');"
        + "for(var j=0;j<rbs.length;j++){(function(b){"
        + "b.addEventListener('click',function(e){last=0;fire(b);if(e){e.preventDefault();}});"
        + "})(rbs[j]);}"
        // 兜底慢轮询：未收到宿主推送前保底（推送通道不可用/未注入时不失刷）
        + "var fn=pickNode();"
        + "if(fn){var iv=parseInt(fn.getAttribute('data-poll-interval'),10);if(!(iv>0)){iv=30000;}"
        + "fallbackTimer=window.setInterval(function(){if(pushSeen){stopFallback();return;}var n=pickNode();if(n){fire(n);}},iv);}"
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