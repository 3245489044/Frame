#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UiBackend.Html;

/// <summary>
/// 默认前端资产注册表——HtmlViewer 全部内嵌 JS/CSS/JS 模板的唯一真源（从方法体 const 字符串提升为具名资产）。
/// 静态方法供静态渲染器（HtmlDocumentRenderer / HtmlLayoutProjector / HtmlControlFactory）直接取用；
/// 实例实现 <see cref="IHtmlAssetProvider"/> 供执行层 / 生命周期层注入（IoC 可替换为外部源）。
/// </summary>
public sealed partial class HtmlAssetRegistry : IHtmlAssetProvider
{
    /// <summary>默认单例（组合根未显式装配时的兜底）。</summary>
    public static IHtmlAssetProvider Default { get; } = new HtmlAssetRegistry();

    private static IReadOnlyDictionary<string, string>? _scripts;

    /// <summary>脚本资产（延迟初始化：<see cref="UiReconcileScript"/> 为运行时派生串，须待静态字段初始化后构建）。</summary>
    private static IReadOnlyDictionary<string, string> Scripts => _scripts ??= new Dictionary<string, string>
    {
        [HtmlAssetKeys.BridgeScript] = ClickBridgeScript,
        [HtmlAssetKeys.UiReconcileScript] = UiReconcileScript,
        [HtmlAssetKeys.TabSwapScript] = TabSwapScript,
        [HtmlAssetKeys.RefreshBridgeScript] = RefreshBridgeScript,
    };

    private static readonly IReadOnlyDictionary<string, string> Templates = new Dictionary<string, string>
    {
        [HtmlAssetKeys.DomHandleTemplate] = DomHandleTemplate,
        [HtmlAssetKeys.RectQueryTemplate] = RectQueryTemplate,
        [HtmlAssetKeys.BridgeDiagnosisTemplate] = BridgeDiagnosisTemplate,
        [HtmlAssetKeys.EmptyDivTemplate] = EmptyDivTemplate,
        [HtmlAssetKeys.EmptyPTemplate] = EmptyPTemplate,
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

    /// <summary>外部注册的样式模块覆盖（域可注入/覆盖同名通用模块，无需新建配置文件）。</summary>
    private static readonly ConcurrentDictionary<string, string> StyleModuleOverrides =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册/覆盖样式模块（幂等：覆盖式）。<paramref name="name"/> 取通用模块名（tokens/block/shell/tree/switch/grouped/state/attach/data）。</summary>
    public static void RegisterStyleModule(string name, string css)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        StyleModuleOverrides[name] = css ?? string.Empty;
    }

    /// <summary>取样式模块：外部覆盖优先，否则内置默认；未知名返回空串。</summary>
    public static string GetStyleModule(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        if (StyleModuleOverrides.TryGetValue(name, out var overridden)) return overridden;
        return CssModules.TryGetValue(name, out var builtin) ? builtin : string.Empty;
    }

    /// <summary>通用组件生效样式 = 通用模块按固定序拼接（顺序固定 ⇒ 级联稳定；模块可被域覆盖）。</summary>
    internal static string FrameworkComponentCss =>
        string.Join("\n", FrameworkComponentCssOrder.Select(GetStyleModule));

    /// <summary>点击桥脚本（写原语=键控对齐；reconcile 不可用 → STATE_NOT_READY）：定义 <c>window.__domBridge</c>
    /// （受控 DOM 操作 handle：set/get/click/bounds/event/reconcile/patch/append/trim）+ 点击桥（<c>[data-cmd]</c> → postMessage）；
    /// patch/reconcile 委托键控对齐引擎 <c>window.__uiReconcile</c>，append/trim 委托流原语（缺失 → <c>STATE_NOT_READY</c>，可见化不静默）。
    /// <para>幂等守卫用 <c>document.__domBridgeClickInstalled</c>（**document 级**，非 window 级）：WebView2 经 NavigateToString 重渲染会替换 document 而保留 window，
    /// 若守卫挂在 window 上会因「window 持久 → 误判已注入」而不再为新 document 重新挂载 document 级点击监听器 → [data-cmd] 点击转 postMessage 失效
    /// （8.26 实证：回流探针正常但按钮点击无效）。document 级守卫保证每个新 document 都重新注入桥、重新挂载点击监听器。</para>
    /// <para>同时置 <c>document.__domBridgeInstalled</c> 作为就绪查询兼容标志（HtmlViewer.IsBridgeReadyCoreAsync 读取）。</para></summary>
    internal const string ClickBridgeScript =
        "(function(){"
        + "if(document.__domBridgeClickInstalled){return;}"
        + "document.__domBridgeClickInstalled=true;"
        + "document.__domBridgeInstalled=true;"
        + "document.__bridgeModules=document.__bridgeModules||{};document.__bridgeModules.click=true;"
        + "window.__domBridge={handle:function(op,name,value){"
        + "var el=name?document.querySelector('[data-name=\"'+name+'\"]'):null;"
        + "if(!el&&op!=='patch')return 'NOT_FOUND';"
        + "var isForm=!!el&&(el.tagName==='SELECT'||el.tagName==='INPUT'||el.tagName==='TEXTAREA');"
        + "switch(op){"
        + "case 'set':if(isForm){el.value=value;}else if(el.classList.contains('host')){el.innerHTML=value;}else{el.textContent=value;}return 'OK';"
        // patch（不重载文档）：**具名**区域缺失 ⇒ 明确 NOT_FOUND（D4：删隐式回退 `el||document.body`，禁一次区域写静默整页替换）；
        // 仅 name 缺省（全页 patch）= 显式目标 document.body。patch/reconcile 均委托键控对齐引擎（保留展开/滚动/焦点）。
        + "case 'patch':if(name){if(!el){return 'NOT_FOUND';}}else{el=document.body;}if(!el){return 'NOT_FOUND';}if(!window.__uiReconcile){return 'STATE_NOT_READY';}return window.__uiReconcile.apply(el,value);"
        + "case 'reconcile':if(name){if(!el){return 'NOT_FOUND';}}else{el=document.body;}if(!el){return 'NOT_FOUND';}if(!window.__uiReconcile){return 'STATE_NOT_READY';}return window.__uiReconcile.apply(el,value);"
        // 流原语（append-only 面）：按稳定键定点更新/插入流首 + cap 裁尾；不做任何文档重建。
        + "case 'append':if(!el){return 'NOT_FOUND';}if(!window.__uiReconcile||!window.__uiReconcile.append){return 'STATE_NOT_READY';}return window.__uiReconcile.append(el,value);"
        + "case 'trim':if(!el){return 'NOT_FOUND';}if(!window.__uiReconcile||!window.__uiReconcile.trim){return 'STATE_NOT_READY';}return window.__uiReconcile.trim(el,value);"
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
        + "var r=t.closest('.section')||t.closest('[data-round]');"
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
    internal static readonly string RefreshBridgeScript =
        "(function(){"
        + "if(document.__htmlRefreshInstalled){return;}"
        + "document.__htmlRefreshInstalled=true;"
        + "document.__bridgeModules=document.__bridgeModules||{};document.__bridgeModules.refresh=true;"
        + "function pst(o){if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage(o);}}"
        + "function ctxOf(n){"
        + "var sel=n.getAttribute('data-poll-ctx');if(!sel){return null;}"
        + "var el=document.querySelector(sel);if(!el){return null;}"
        + "var attrs=(n.getAttribute('data-poll-ctx-attrs')||'').split(',');"
        + "var o=new Object();for(var i=0;i<attrs.length;i++){"
        + "var a=(attrs[i]||'').trim();if(!a){continue;}"
        + "var v=el.getAttribute('data-'+a);if(v!==null){o[a]=v;}"
        + "}return o;}"
        + "var curScopes=null;var curTrace=null;"
        + "function subsOf(n){var s=n.getAttribute('data-poll-subscribe');if(!s){return null;}var a=s.split(',');var o=[];for(var i=0;i<a.length;i++){var t=(a[i]||'').trim();if(t){o.push(t);}}return o.length?o:null;}"
        // D1/D3：curScopes 为空（宿主全量心跳 all:true，或未收到 scope）⇒ 恒真（显式"全量"语义，非隐式）；
        // 订阅键含 '*' ⇒ 恒真（页面显式声明"关心全部实体"，如任务总览页；避免"未声明即全量"的隐式含义）。
        + "function scopeMatch(n){if(!curScopes||curScopes.length===0){return true;}var subs=subsOf(n);if(!subs){return true;}for(var s0=0;s0<subs.length;s0++){if(subs[s0]==='*'){return true;}}for(var i=0;i<subs.length;i++){for(var j=0;j<curScopes.length;j++){if(subs[i]===curScopes[j]){return true;}}}return false;}"
        + "function fire(n){"
        + "if(!scopeMatch(n)){return;}"
        + "var cmd=n.getAttribute('data-poll-cmd');if(!cmd){cmd='" + HtmlRefreshProtocol.Command + "';}"
        + "var o=new Object();o.cmd=cmd;var ctx=ctxOf(n);"
        + "if(ctx){for(var k in ctx){o[k]=ctx[k];}}"
        // I3 范围协议（D5 打通）：回投保留宿主推送的范围集合，服务端据此定向更新（无 scope = 显式全量）。
        + "if(curScopes&&curScopes.length){o.scope=curScopes.slice();}"
        // L6 追踪：回投携带宿主推送的 refreshTraceId（无推送=兜底/手动刷新 → 生成页面本地号，仍一条轨迹）。
        + "o.traceId=curTrace||('page-'+((window.__uiTraceSeq=(window.__uiTraceSeq||0)+1)));"
        // L5 态外置：回投受控态（<details> 展开集合，按稳定键），服务端渲染时绑定（f(data,state)）。
        + "if(window.__uiReconcile&&window.__uiReconcile.capture){o.uiState=window.__uiReconcile.capture();}"
        + "pst(o);}"
        // ── 1s 刷新限制（反向节流）：空闲即刷；窗口内合并，窗口到期补刷最后一次（trailing 必达）──
        + "var last=-1e9;var pendNode=null;var pendTimer=null;"
        + "function nowMs(){return (window.performance&&window.performance.now)?window.performance.now():Date.now();}"
        + "function throttleOf(n){var v=parseInt(n.getAttribute('data-refresh-throttle'),10);return v>0?v:" + HtmlRefreshProtocol.ThrottleMs + ";}"
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
        + "if(cmd!=='" + HtmlRefreshProtocol.Command + "'&&cmd!=='update'){return;}"
        + "curScopes=(d&&d.scope&&d.scope.length)?d.scope:null;curTrace=(d&&d.traceId)?d.traceId:null;"
        + "pushSeen=true;stopFallback();refreshNow();"
        + "});}"
        // 手动刷新按钮：点击即刷（不受 1s 节流限制）
        + "var rbs=document.querySelectorAll('[data-poll-refresh]');"
        + "for(var j=0;j<rbs.length;j++){(function(b){"
        + "b.addEventListener('click',function(e){last=0;fire(b);if(e){e.preventDefault();}});"
        + "})(rbs[j]);}"
        // 兜底慢轮询收编（P6「入口单源」B6）：页面侧**不再自建**兜底定时器——兜底唯一生产者 =
        // 宿主框架 RefreshService.EnableIdlePolling（见 AgentUiRefreshPublisher）；页面仅在收到宿主推送时刷新。
        // （如需页面侧自主兜底，可在模板上显式声明 data-poll-interval 重新启用下方分支。）
        + "var fn=pickNode();"
        + "if(fn&&fn.hasAttribute('data-poll-interval')){var iv=parseInt(fn.getAttribute('data-poll-interval'),10);if(!(iv>0)){iv=" + HtmlRefreshProtocol.IntervalMs + ";}"
        + "fallbackTimer=window.setInterval(function(){if(pushSeen){stopFallback();return;}var n=pickNode();if(n){fire(n);}},iv);}"
        + "})();";

    /// <summary>键控对齐引擎（**唯一写原语**，恒开无回退）：**递归**按稳定键对齐复用 DOM 节点
    /// （保留 &lt;details&gt;.open / 焦点 / 滚动，免 save/restore 启发式）。document 级幂等守卫 <c>__uiReconcileInstalled</c>；
    /// 提供 <c>apply(host,html)</c>（HTML 片段）、<c>applyTree(host,json)</c>（键控节点树）与
    /// <c>append(host,payload)</c>/<c>trim(host,payload)</c>（append-only 流原语：定点更新同名项 + 流首插入 + cap 裁尾 + 滚动锚补偿）。
    /// 红线：不得再引入整块 <c>innerHTML</c> 落地回退（那是「第二条写落地」，与单写者不变量冲突）。</summary>
    internal static readonly string UiReconcileScript =
        "(function(){"
        + "if(document.__uiReconcileInstalled){return;}"
        + "document.__uiReconcileInstalled=true;"
        // reconcile 内核即 patch 写原语（DomOps.Patch 已收敛为 reconcile）：同置 patch 位，使模块清单可达全就绪。
        + "document.__bridgeModules=document.__bridgeModules||{};document.__bridgeModules.reconcile=true;document.__bridgeModules.patch=true;document.__bridgeModules.append=true;document.__bridgeModules.trim=true;"
        + "var __kk=" + HtmlKey.JsArrayLiteral + ";"
        + "function keyOf(n){if(!n||!n.getAttribute){return null;}for(var i=0;i<__kk.length;i++){var v=n.getAttribute(__kk[i]);if(v){return __kk[i]+'='+v;}}return null;}"
        + "function syncAttrs(oldN,newN){"
        + "var na=newN.attributes;for(var i=0;i<na.length;i++){var a=na[i];if(a.name==='open'){continue;}if(oldN.getAttribute(a.name)!==a.value){oldN.setAttribute(a.name,a.value);}}"
        + "var oa=oldN.attributes;for(var j=oa.length-1;j>=0;j--){var nm=oa[j].name;if(nm==='open'){continue;}if(!newN.hasAttribute(nm)){oldN.removeAttribute(nm);}}}"
        + "function reconcile(oldN,newN){"
        + "if(oldN.tagName!==newN.tagName){return newN;}"
        + "var wasOpen=(oldN.tagName==='DETAILS')?oldN.open:null;"
        + "syncAttrs(oldN,newN);"
        // P-D 模型驱动展开态：键控 <details> 由产物带 data-open-state（"1"/"0"，UiStateBinder 绑定）⇒ 内核**可增可减**跟随模型；
        // 无受控标记（未键控/未绑定）⇒ 保留 DOM 现值（wasOpen||new.open），不回退旧行为（宿主推送不带 uiState 时不误折叠）。
        + "if(oldN.tagName==='DETAILS'){var ctl=newN.hasAttribute('data-open-state');oldN.open=ctl?(newN.getAttribute('data-open-state')==='1'):(wasOpen||newN.hasAttribute('open'));}"
        + "var hasElem=false;for(var c=0;c<newN.childNodes.length;c++){if(newN.childNodes[c].nodeType===1){hasElem=true;break;}}"
        + "if(!hasElem){if(oldN.textContent!==newN.textContent){oldN.textContent=newN.textContent;}return oldN;}"
        + "reconcileChildren(oldN,newN);return oldN;}"
        + "var lastAnomalies=[];"
        + "function reconcileChildren(oldParent,newParent){"
        // Pass1：建 oldIndex（Map<key,Queue<Node>>；同键多节点入队**不覆盖**）+ 无键队列；顺带清除非元素旧节点。
        + "var oldIndex={};var unkeyed=[];var i;"
        + "for(i=0;i<oldParent.childNodes.length;i++){var c=oldParent.childNodes[i];if(c.nodeType!==1){if(c.parentNode){c.parentNode.removeChild(c);}continue;}var k=keyOf(c);if(k){if(oldIndex[k]){lastAnomalies.push('duplicate-key-old:'+k);oldIndex[k].push(c);}else{oldIndex[k]=[c];}}else{unkeyed.push(c);}}"
        // Pass2：按新序消费（同 FDIO 一一对应）；新节点同键 >1 记 anomaly。
        + "var matched=[];var newSeen={};"
        + "for(var j=0;j<newParent.childNodes.length;j++){var nn=newParent.childNodes[j];if(nn.nodeType!==1){continue;}var nk=keyOf(nn);var re=null;"
        + "if(nk){if(newSeen[nk]){lastAnomalies.push('duplicate-key-new:'+nk);}newSeen[nk]=true;"
        + "if(oldIndex[nk]&&oldIndex[nk].length){re=oldIndex[nk].shift();if(!oldIndex[nk].length){delete oldIndex[nk];}}}"
        + "else if(unkeyed.length){re=unkeyed.shift();}"
        + "matched.push([re,nn]);}"
        // Pass3：递归对齐（tag 相同复用 / 不同则替换）并按新序落位。
        // 修复：tag 不同 ⇒ reconcile 返回替换节点（newN），此时须显式移除旧节点 it[0]——
        // 它已被 Pass2 从 oldIndex/unkeyed 消费，Pass4 回收不到 ⇒ 否则成永久孤儿（旧内容残留 + 新内容追加 = 重复叠加）。
        + "for(var m=0;m<matched.length;m++){var it=matched[m];if(it[0]){var r=reconcile(it[0],it[1]);if(r!==it[0]&&it[0].parentNode){it[0].parentNode.removeChild(it[0]);}oldParent.appendChild(r);}else{oldParent.appendChild(it[1]);}}"
        // Pass4：回收**所有未被消费的旧节点**（含重复键残留 + 无键残留）⇒ 无孤儿（幂等 I1 / 不丢不增 I2）。
        + "for(var kk in oldIndex){var q=oldIndex[kk];for(var qq=0;qq<q.length;qq++){if(q[qq].parentNode){q[qq].parentNode.removeChild(q[qq]);}}}"
        + "for(var u=0;u<unkeyed.length;u++){if(unkeyed[u].parentNode){unkeyed[u].parentNode.removeChild(unkeyed[u]);}}}"
        + "function findByKey(root,key){if(!key){return null;}var eq=key.indexOf('=');if(eq<=0){return null;}var a=key.substring(0,eq);var v=key.substring(eq+1);if(a==='id'){var e=document.getElementById(v);return (e&&(!root||root.contains(e)||root===e))?e:null;}return root.querySelector('['+a+'='+JSON.stringify(v)+']');}"
        + "function applyState(host,state){if(!host||!state){return;}if(state.focus){var f=findByKey(host,state.focus);if(f&&f.focus){try{f.focus();}catch(e){}}}if(state.scroll){for(var s=0;s<state.scroll.length;s++){var it=state.scroll[s];var e=findByKey(host,it.k);if(!e){continue;}if(it.b){e.scrollTop=e.scrollHeight;}else{e.scrollTop=it.t;e.scrollLeft=it.l;}}}}"
        + "function apply(host,html,state){"
        + "if(!host){return 'NOT_FOUND';}"
        + "lastAnomalies=[];"
        // 整页 HTML 串（含 <html>/<head>/<style>）用于 body 级对齐时，仅取 <body> 内容参与按位对齐，
        // 避免 head/style/meta 等非内容节点混入 tmp 参与位置匹配（防「同 tag 误替换 / 壳体破坏」）。
        + "var tmp=document.createElement('div');var mb=/<body[^>]*>([\\s\\S]*)<\\/body>/i.exec(html);tmp.innerHTML=mb?mb[1]:html;"
        + "reconcileChildren(host,tmp);applyState(host,state);window.__uiReconcile.lastAnomalies=lastAnomalies;return 'OK';}"
        // 无 tag（文本/片段节点）须在 buildNode 内先分流——否则 createElement('') 抛 InvalidCharacterError，使整棵键控树写回失败。
        // 根因：HtmlNodeParser 把元素内文本投影为 UiNode{Tag="",Text=...}；整页首挂载走 innerHTML 不经 buildNode 故正常，增量 applyTree 必抛。
        + "function buildNode(n){if(n==null){return document.createTextNode('');}if(typeof n==='string'){return document.createTextNode(n);}"
        + "if(!n.tag){if(n.text!=null){return document.createTextNode(''+n.text);}var fr=document.createDocumentFragment();if(n.children){for(var i=0;i<n.children.length;i++){fr.appendChild(buildNode(n.children[i]));}}return fr;}"
        + "var el=document.createElement(n.tag);if(n.attributes){for(var k in n.attributes){if(n.attributes[k]!=null){el.setAttribute(k,''+n.attributes[k]);}}}"
        + "if(n.text!=null){el.appendChild(document.createTextNode(''+n.text));}"
        + "if(n.children){for(var i=0;i<n.children.length;i++){el.appendChild(buildNode(n.children[i]));}}return el;}"
        + "function applyTree(host,tree,state){if(!host){return 'NOT_FOUND';}var t=(typeof tree==='string')?JSON.parse(tree):tree;"
        + "lastAnomalies=[];"
        + "var tmp=document.createElement('div');appendInto(tmp,t);reconcileChildren(host,tmp);applyState(host,state);window.__uiReconcile.lastAnomalies=lastAnomalies;"
        + "return lastAnomalies.length?('OK:'+lastAnomalies.join(';')):'OK';}"
        + "function appendInto(container,t){if(t==null){return;}if(t.tag){container.appendChild(buildNode(t));return;}"
        + "if(t.children){for(var i=0;i<t.children.length;i++){var c=t.children[i];if(!c){continue;}if(c.tag){container.appendChild(buildNode(c));}else if(c.text!=null){container.appendChild(document.createTextNode(''+c.text));}}}}"
        + "function capture(){var open=[];var ds=document.querySelectorAll('details');for(var i=0;i<ds.length;i++){if(ds[i].open){var k=keyOf(ds[i]);if(k){open.push(k);}}}var scroll=[];var ae=document.activeElement;var focus=(ae&&ae!==document.body)?keyOf(ae):null;var esc=document.scrollingElement||document.documentElement;if(esc){var ty=(window.scrollY||window.pageYOffset||0),tx=(window.scrollX||window.pageXOffset||0);if(ty>0||tx>0){var ek=keyOf(esc);if(ek&&ek!=='id=null'){scroll.push({k:ek,t:esc.scrollTop,l:esc.scrollLeft,b:false});}}}var ns=document.querySelectorAll('[data-key],[data-name],[id]');for(var j=0;j<ns.length;j++){var n=ns[j];if(n.scrollTop>0||n.scrollLeft>0){var nk=keyOf(n);if(nk){scroll.push({k:nk,t:n.scrollTop,l:n.scrollLeft,b:(n.scrollHeight-n.clientHeight-n.scrollTop)<=2});}}}var sel=[];var sl=document.querySelectorAll('[data-select-slot]');for(var si=0;si<sl.length;si++){var se=sl[si];if(se.hasAttribute('data-selected')){sel.push(se.getAttribute('data-select-slot')+'='+(se.getAttribute('data-select-key')||''));}}return {open:open,scroll:scroll,focus:focus,select:sel};}"
        // 流原语（append-only 面）：按稳定键**定点更新**同名项（重复折叠 ⇒ xN 更新不新增行），其余插入流首；cap 裁尾。
        // 顶部插入按滚动锚补偿：非顶部读者保持视觉位置（不跳动），顶部读者恒见最新（scrollTop=0）。
        + "function findByKeyIn(host,k){var ns=host.querySelectorAll('[data-key]');for(var i=0;i<ns.length;i++){if(ns[i].getAttribute('data-key')===k){return ns[i];}}return null;}"
        + "function streamAppend(host,payload){if(!host){return 'NOT_FOUND';}var p=(typeof payload==='string')?JSON.parse(payload):payload;var items=(p&&p.items)||[];var cap=(p&&p.cap)?p.cap:0;lastAnomalies=[];"
        + "var sc=host.scrollTop||0;var atTop=sc<=2;var sh=host.scrollHeight;"
        + "for(var i=items.length-1;i>=0;i--){var it=items[i];if(!it||!it.html){continue;}var tmp=document.createElement('div');tmp.innerHTML=it.html;var nn=tmp.firstElementChild;if(!nn){continue;}"
        + "if(it.key&&!nn.getAttribute('data-key')){nn.setAttribute('data-key',it.key);}"
        + "var old=it.key?findByKeyIn(host,it.key):null;"
        + "if(old){var r=reconcile(old,nn);if(r!==old&&old.parentNode){old.parentNode.replaceChild(r,old);}}"
        + "else if(host.firstElementChild){host.insertBefore(nn,host.firstElementChild);}else{host.appendChild(nn);}}"
        + "if(cap>0){while(host.children.length>cap){host.removeChild(host.children[host.children.length-1]);}}"
        + "if(atTop){host.scrollTop=0;}else{host.scrollTop=sc+(host.scrollHeight-sh);}"
        + "window.__uiReconcile.lastAnomalies=lastAnomalies;return lastAnomalies.length?('OK:'+lastAnomalies.join(';')):'OK';}"
        + "function streamTrim(host,payload){if(!host){return 'NOT_FOUND';}var p=(typeof payload==='string')?JSON.parse(payload):payload;var keep=(p&&p.keep)?p.keep:0;"
        + "if(keep>0){while(host.children.length>keep){host.removeChild(host.children[host.children.length-1]);}}return 'OK';}"
        + "window.__uiReconcile={apply:apply,applyTree:applyTree,capture:capture,append:streamAppend,trim:streamTrim,lastAnomalies:[]};"
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

    /// <summary>块级空态默认模板（引擎语法；供 <see cref="HtmlEmptyState"/> 缺 JSON 模板时回退）。</summary>
    internal const string EmptyDivTemplate = "<div class=\"empty\">{text}</div>";

    /// <summary>段落空态默认模板（引擎语法；供 <see cref="HtmlEmptyState"/> 缺 JSON 模板时回退）。</summary>
    internal const string EmptyPTemplate = "<p class=\"empty\">{text}</p>";

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
        /* ── 块式对话 ── */
        .block { display: flex; flex-direction: column; margin: 10px 0; }
        .block.user { align-items: flex-end; }
        .block.assistant { align-items: flex-start; }
        .block-badge {
            font-size: 12px; color: var(--muted);
            margin: 0 6px 3px; letter-spacing: .02em;
        }
        .block-body {
            max-width: 86%; padding: 8px 12px;
            border-radius: 10px;
            white-space: pre-wrap; word-break: break-word;
        }
        .block.user .block-body {
            background: var(--user-bg); color: var(--user-fg);
            border-top-right-radius: 2px;
        }
        .block.assistant .block-body {
            background: var(--agent-bg); color: var(--agent-fg);
            border-top-left-radius: 2px;
        }
        .empty { color: var(--muted); text-align: center; margin-top: 24px; }
        /* ── 层次化对话 ── */
        .section { margin: 4px 0 14px; }
        .section-header {
            font-size: 12px; color: var(--muted);
            margin: 8px 0 4px; padding-bottom: 3px;
            letter-spacing: .04em; cursor: pointer; user-select: none;
            border-bottom: 1px solid var(--border);
        }
        .section-header:hover { color: var(--fg); }
        .block.nested .fold-layer { margin: 0 6px; }
        .fold-summary { font-size: 12px; color: var(--muted); cursor: pointer; user-select: none; }
        .fold-summary::before { content: "▸ "; }
        details.fold-layer[open] > .fold-summary::before { content: "▾ "; }
        .block.error .block-body { background: #fbe3e3; color: #7a1f1f; }
        .block.system { align-items: center; }
        .block.system .block-body {
            background: transparent; color: var(--muted);
            border: 1px dashed var(--border); font-size: 13px;
        }
        /* ── 域外扩展位 ──
           交互式会话页（task 侧栏 / history 两级树 / dot / composer 等）属应用域样式，
           不属 plate 纯渲染基础：由应用侧经 JSON templates.theme 以「基础 + 差异」方式叠加
           （装配点 HtmlBridgeAssembly.AttachTemplatesAndTheme 会把本基础主题与应用主题合并），
           避免「框架 DefaultCss ↔ 应用 theme」两份逐字复制漂移。 */
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
        ".ui-root .pane.active{display:block}" +
        // S2/L1 布局隔离：区域容器限定 layout/style/paint，区域内替换不牵动区域外（收敛整块替换/布局读的 thrash）。
        ".region{contain:layout style paint}";
}
