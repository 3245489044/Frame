using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FrontEndDriven.ProductionConsole.Console;

/// <summary>控制台区域键（与面板 htmlPage.regions 声明一一对应；写回按 region:&lt;key&gt; 收敛）。</summary>
public static class ConsoleRegions
{
    public const string Caps = "region-caps";
    public const string Form = "region-form";
    public const string Jobs = "region-jobs";
    public const string Result = "region-result";
    public const string Drawer = "region-drawer";
    public const string Status = "region-status";

    public static readonly string[] All = { Caps, Form, Jobs, Result, Drawer, Status };
}

/// <summary>
/// 通用生产控制台渲染器：把 <see cref="ConsolePageModel"/> 投影为自包含 HTML（三栏 + 抽屉 + 状态栏）。
/// 只输出 HTML，不触通道；区域可单独渲染供 patch 写回（保展开/滚动/焦点）。
/// </summary>
public static class ConsoleHtmlKit
{
    /// <summary>整页 body 片段（由宿主 htmlShell 包壳）。</summary>
    public static string RenderPage(ConsolePageModel model)
    {
        model ??= new ConsolePageModel();
        var sb = new StringBuilder();
        sb.Append("<style>").Append(Css).Append("</style>");
        sb.Append("<div class=\"pcx\">");
        RegionBlock(sb, ConsoleRegions.Caps, "pcx-col-caps", RenderCaps(model));
        RegionBlock(sb, ConsoleRegions.Form, "pcx-col-form", RenderForm(model));
        RegionBlock(sb, ConsoleRegions.Jobs, "pcx-col-jobs", RenderJobs(model));
        RegionBlock(sb, ConsoleRegions.Result, "pcx-col-result", RenderResult(model));
        RegionBlock(sb, ConsoleRegions.Drawer, "pcx-row-drawer", RenderDrawer(model));
        RegionBlock(sb, ConsoleRegions.Status, "pcx-row-status", RenderStatus(model));
        sb.Append("</div>");
        sb.Append("<script>").Append(Script).Append("</script>");
        return sb.ToString();
    }

    /// <summary>单区域 HTML（patch 写回目标）。</summary>
    public static string RenderRegion(string region, ConsolePageModel model)
    {
        model ??= new ConsolePageModel();
        return region switch
        {
            ConsoleRegions.Caps => RenderCaps(model),
            ConsoleRegions.Form => RenderForm(model),
            ConsoleRegions.Jobs => RenderJobs(model),
            ConsoleRegions.Result => RenderResult(model),
            ConsoleRegions.Drawer => RenderDrawer(model),
            ConsoleRegions.Status => RenderStatus(model),
            _ => $"<div class=\"pcx-hint\">未知区域: {E(region)}</div>",
        };
    }

    private static void RegionBlock(StringBuilder sb, string region, string cssClass, string inner)
    {
        sb.Append("<div class=\"pcx-region ").Append(cssClass).Append("\" data-name=\"").Append(region)
          .Append("\" data-key=\"").Append(region).Append("\">").Append(inner).Append("</div>");
    }

    // ── 能力 ────────────────────────────────────────────────

    private static string RenderCaps(ConsolePageModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"pcx-title\">能力</div>");
        if (model.Capabilities.Count == 0)
            return sb.Append("<div class=\"pcx-hint\">未注册能力</div>").ToString();

        foreach (var group in model.Capabilities.GroupBy(c => string.IsNullOrWhiteSpace(c.Group) ? "默认" : c.Group))
        {
            sb.Append("<div class=\"pcx-group\"><div class=\"pcx-group-title\">").Append(E(group.Key)).Append("</div>");
            foreach (var cap in group)
            {
                var active = string.Equals(cap.Id, model.SelectedCapabilityId, StringComparison.Ordinal);
                sb.Append("<button type=\"button\" class=\"pcx-cap")
                  .Append(active ? " active" : "")
                  .Append("\" data-pcx=\"cap.select\" data-cap=\"").Append(A(cap.Id))
                  .Append("\" data-key=\"cap-").Append(A(cap.Id))
                  .Append("\" data-arg=\"").Append(A(Arg("capability", cap.Id)))
                  .Append("\" title=\"").Append(A(cap.Description))
                  .Append("\">").Append(E(cap.Name)).Append("</button>");
            }
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    // ── 表单 ────────────────────────────────────────────────

    private static string RenderForm(ConsolePageModel model)
    {
        var sb = new StringBuilder();
        var cap = model.Capabilities.FirstOrDefault(c =>
            string.Equals(c.Id, model.SelectedCapabilityId, StringComparison.Ordinal));

        if (cap is null)
            return sb.Append("<div class=\"pcx-title\">参数</div><div class=\"pcx-hint\">选择左侧能力后填写参数</div>").ToString();

        sb.Append("<div class=\"pcx-title\">").Append(E(cap.Name)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(cap.Description))
            sb.Append("<div class=\"pcx-desc\">").Append(E(cap.Description)).Append("</div>");

        sb.Append("<div class=\"pcx-fields\">");
        foreach (var spec in cap.Params.Where(p => !p.Advanced)) RenderField(sb, spec, model);

        var advanced = cap.Params.Where(p => p.Advanced).ToList();
        if (advanced.Count > 0)
        {
            sb.Append("<details class=\"pcx-advanced\" data-key=\"advanced\"><summary>高级参数</summary>");
            foreach (var spec in advanced) RenderField(sb, spec, model);
            sb.Append("</details>");
        }
        sb.Append("</div>");

        sb.Append("<div class=\"pcx-form-actions\">");
        sb.Append("<button type=\"button\" class=\"pcx-btn primary\" data-pcx=\"form.run\" data-pcx-collect=\"1\">▶ 生成</button>");
        sb.Append("<button type=\"button\" class=\"pcx-btn\" data-pcx=\"form.reset\">重置</button>");
        foreach (var action in cap.Actions)
            sb.Append("<button type=\"button\" class=\"pcx-btn\" data-pcx=\"cap.action\" data-arg=\"")
              .Append(A(Arg("action", action))).Append("\">").Append(E(action)).Append("</button>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static void RenderField(StringBuilder sb, ParamSpec spec, ConsolePageModel model)
    {
        var value = model.FormValues.TryGetValue(spec.Key, out var v) ? v : spec.Default;
        var text = Format(value);
        var name = "param-" + spec.Key;

        sb.Append("<label class=\"pcx-field\" data-key=\"field-").Append(A(spec.Key)).Append("\">");
        sb.Append("<span class=\"pcx-label\">").Append(E(ParamSet.Label(spec)));
        if (spec.Required) sb.Append("<b class=\"pcx-req\">*</b>");
        if (!string.IsNullOrWhiteSpace(spec.Unit)) sb.Append("<i class=\"pcx-unit\">(").Append(E(spec.Unit)).Append(")</i>");
        sb.Append("</span>");

        switch (spec.Kind)
        {
            case ParamKind.Multiline:
                sb.Append("<textarea rows=\"4\" data-param=\"").Append(A(spec.Key)).Append("\" data-name=\"").Append(A(name))
                  .Append("\" title=\"").Append(A(spec.Help)).Append("\">").Append(E(text)).Append("</textarea>");
                break;

            case ParamKind.Number:
                sb.Append("<input type=\"number\" data-param=\"").Append(A(spec.Key)).Append("\" data-name=\"").Append(A(name))
                  .Append("\" value=\"").Append(A(text)).Append("\"")
                  .Append(spec.Min is { } min ? " min=\"" + A(Fmt(min)) + "\"" : "")
                  .Append(spec.Max is { } max ? " max=\"" + A(Fmt(max)) + "\"" : "")
                  .Append(spec.Step is { } step ? " step=\"" + A(Fmt(step)) + "\"" : "")
                  .Append(">");
                break;

            case ParamKind.Range:
                sb.Append("<input type=\"range\" data-range=\"1\" data-param=\"").Append(A(spec.Key)).Append("\" data-name=\"")
                  .Append(A(name)).Append("\" value=\"").Append(A(text)).Append("\"")
                  .Append(" min=\"").Append(A(Fmt(spec.Min ?? 0))).Append("\"")
                  .Append(" max=\"").Append(A(Fmt(spec.Max ?? 100))).Append("\"")
                  .Append(" step=\"").Append(A(Fmt(spec.Step ?? 1))).Append("\">")
                  .Append("<output class=\"pcx-range-out\">").Append(E(text)).Append("</output>");
                break;

            case ParamKind.Bool:
                sb.Append("<span class=\"pcx-switch\"><input type=\"checkbox\" data-param=\"").Append(A(spec.Key))
                  .Append("\" data-name=\"").Append(A(name)).Append("\"")
                  .Append(string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? " checked" : "")
                  .Append(">").Append(string.IsNullOrWhiteSpace(spec.Help) ? "" : E(spec.Help)).Append("</span>");
                break;

            case ParamKind.Enum:
                sb.Append("<select data-param=\"").Append(A(spec.Key)).Append("\" data-name=\"").Append(A(name))
                  .Append("\" title=\"").Append(A(spec.Help)).Append("\">");
                foreach (var option in spec.Options ?? new List<string>())
                    sb.Append("<option value=\"").Append(A(option)).Append("\"")
                      .Append(string.Equals(option, text, StringComparison.OrdinalIgnoreCase) ? " selected" : "")
                      .Append(">").Append(E(option)).Append("</option>");
                sb.Append("</select>");
                break;

            case ParamKind.Json:
                sb.Append("<textarea rows=\"3\" class=\"mono\" data-param=\"").Append(A(spec.Key)).Append("\" data-name=\"")
                  .Append(A(name)).Append("\" title=\"").Append(A(spec.Help)).Append("\">").Append(E(text)).Append("</textarea>");
                break;

            default:
                sb.Append("<input type=\"text\" data-param=\"").Append(A(spec.Key)).Append("\" data-name=\"").Append(A(name))
                  .Append("\" value=\"").Append(A(text)).Append("\" title=\"").Append(A(spec.Help)).Append("\">");
                break;
        }

        if (!string.IsNullOrWhiteSpace(spec.Help) && spec.Kind != ParamKind.Bool)
            sb.Append("<small class=\"pcx-help\">").Append(E(spec.Help)).Append("</small>");
        sb.Append("</label>");
    }

    // ── 任务 ────────────────────────────────────────────────

    private static string RenderJobs(ConsolePageModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"pcx-title\">任务</div>");
        if (model.Jobs.Any(j => !j.IsTerminal))
            // 活动任务标记：页面脚本据此做 2s 自动刷新（无活动任务时零轮询）
            sb.Append("<span class=\"pcx-live\" data-pcx-live=\"1\" style=\"display:none\"></span>");
        if (model.Jobs.Count == 0)
            return sb.Append("<div class=\"pcx-hint\">暂无任务</div>").ToString();

        foreach (var job in model.Jobs)
        {
            sb.Append("<article class=\"pcx-job ").Append(StatusClass(job.Status))
              .Append("\" data-key=\"job-").Append(A(job.JobId))
              .Append("\" data-name=\"job-").Append(A(job.JobId)).Append("\">");

            sb.Append("<header><span class=\"pcx-badge ").Append(StatusClass(job.Status)).Append("\">")
              .Append(E(StatusText(job.Status))).Append("</span><code class=\"pcx-cap-id\">").Append(E(job.CapabilityId))
              .Append("</code>");
            if (job.CacheHit) sb.Append("<span class=\"pcx-badge cache\">cache-hit</span>");
            if (job.Planned) sb.Append("<span class=\"pcx-badge plan\">plan</span>");
            sb.Append("<time>").Append(E(job.CreatedAt.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append("</time></header>");

            sb.Append("<div class=\"pcx-progress\"><div style=\"width:")
              .Append(A(Fmt(Math.Round(job.Progress * 100, 1)))).Append("%\"></div></div>");
            sb.Append("<div class=\"pcx-job-meta\">").Append(E(job.Phase));
            sb.Append(" · ").Append(job.ElapsedMs).Append("ms");
            if (job.QueuePosition > 0) sb.Append(" · 队列第 ").Append(job.QueuePosition).Append(" 位");
            if (!string.IsNullOrWhiteSpace(job.InputHash))
                sb.Append(" · <code>").Append(E(job.InputHash)).Append("</code>");
            sb.Append("</div>");

            if (!string.IsNullOrWhiteSpace(job.Error))
                sb.Append("<div class=\"pcx-job-err\">").Append(E(job.Error)).Append("</div>");
            else if (!string.IsNullOrWhiteSpace(job.Reason))
                sb.Append("<div class=\"pcx-job-reason\">").Append(E(job.Reason)).Append("</div>");

            sb.Append("<div class=\"pcx-actions\">");
            JobButton(sb, "job.select", job.JobId, "查看", "");
            if (!job.IsTerminal) JobButton(sb, "job.cancel", job.JobId, "取消", "");
            if (job.IsTerminal) JobButton(sb, "job.reuse", job.JobId, "复用参数", "");
            if (job.Status is JobStatus.Failed or JobStatus.Cancelled) JobButton(sb, "job.retry", job.JobId, "重试", "");
            sb.Append("</div></article>");
        }
        return sb.ToString();
    }

    private static void JobButton(StringBuilder sb, string cmd, string jobId, string label, string extraCss)
    {
        sb.Append("<button type=\"button\" class=\"pcx-btn small ").Append(extraCss)
          .Append("\" data-pcx=\"").Append(A(cmd)).Append("\" data-arg=\"")
          .Append(A(Arg("jobId", jobId))).Append("\">").Append(E(label)).Append("</button>");
    }

    // ── 结果 ────────────────────────────────────────────────

    private static string RenderResult(ConsolePageModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"pcx-title\">结果</div>");
        var job = model.SelectedJob;
        if (job is null)
            return sb.Append("<div class=\"pcx-hint\">选择任务后查看产物</div>").ToString();

        sb.Append("<div class=\"pcx-result-head\"><span class=\"pcx-badge ").Append(StatusClass(job.Status)).Append("\">")
          .Append(E(StatusText(job.Status))).Append("</span><code>").Append(E(job.JobId)).Append("</code>");
        if (job.CacheHit) sb.Append("<span class=\"pcx-badge cache\">cache-hit</span>");
        if (job.Planned) sb.Append("<span class=\"pcx-badge plan\">plan</span>");
        sb.Append("</div>");

        if (!string.IsNullOrWhiteSpace(job.Reason))
            sb.Append("<div class=\"pcx-job-reason\">").Append(E(job.Reason)).Append("</div>");

        if (model.Artifacts.Count == 0)
            return sb.Append("<div class=\"pcx-hint\">该任务无产物</div>").ToString();

        sb.Append("<div class=\"pcx-gallery\">");
        foreach (var view in model.Artifacts) RenderArtifact(sb, view);
        sb.Append("</div>");
        return sb.ToString();
    }

    private static void RenderArtifact(StringBuilder sb, ConsoleArtifactView view)
    {
        var spec = view.Spec;
        var url = view.Url;
        sb.Append("<figure class=\"pcx-artifact\" data-key=\"artifact-").Append(A(System.IO.Path.GetFileName(spec.Path))).Append("\">");

        if (!url.IsUsable)
        {
            sb.Append("<div class=\"pcx-artifact-err\">").Append(E(url.Error ?? "产物不可用")).Append("</div>");
            sb.Append("<figcaption><code>").Append(E(spec.Path)).Append("</code></figcaption>");
            sb.Append("</figure>");
            return;
        }

        switch (spec.Kind)
        {
            case ArtifactKind.Image:
                sb.Append("<img loading=\"lazy\" src=\"").Append(A(url.Url)).Append("\" alt=\"").Append(A(System.IO.Path.GetFileName(spec.Path))).Append("\">");
                break;

            case ArtifactKind.Audio:
                sb.Append("<audio controls preload=\"metadata\" src=\"").Append(A(url.Url)).Append("\"></audio>");
                break;

            case ArtifactKind.Text:
            case ArtifactKind.Json:
                sb.Append("<pre class=\"pcx-text\">").Append(E(Truncate(view.Text ?? spec.Path, 4000))).Append("</pre>");
                break;

            default:
                sb.Append("<code class=\"pcx-path\">").Append(E(spec.Path)).Append("</code>");
                break;
        }

        sb.Append("<figcaption>");
        sb.Append("<code>").Append(E(System.IO.Path.GetFileName(spec.Path))).Append("</code>");
        sb.Append(" <span class=\"pcx-bytes\">").Append(FormatBytes(spec.Bytes)).Append("</span>");
        if (spec.Meta is { Count: > 0 })
            foreach (var kv in spec.Meta)
                sb.Append(" <span class=\"pcx-meta\">").Append(E(kv.Key)).Append("=").Append(E(kv.Value)).Append("</span>");
        sb.Append(" <button type=\"button\" class=\"pcx-btn small\" data-pcx=\"artifact.copy\" data-arg=\"")
          .Append(A(Arg("path", spec.Path))).Append("\">复制路径</button>");
        sb.Append(" <button type=\"button\" class=\"pcx-btn small\" data-pcx=\"artifact.open\" data-arg=\"")
          .Append(A(Arg("path", spec.Path))).Append("\">打开目录</button>");
        sb.Append("</figcaption></figure>");
    }

    // ── 抽屉 / 状态 ─────────────────────────────────────────

    private static string RenderDrawer(ConsolePageModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"pcx-drawer-tabs\">");
        DrawerTab(sb, "history", "历史", model.Drawer);
        DrawerTab(sb, "graph", "结构图", model.Drawer);
        DrawerTab(sb, "diag", "诊断", model.Drawer);
        sb.Append("<button type=\"button\" class=\"pcx-btn small\" data-pcx=\"drawer.open\" data-arg=\"")
          .Append(A(Arg("drawer", "none"))).Append("\">收起</button>");
        sb.Append("</div>");

        switch (model.Drawer)
        {
            case "history":
                RenderHistory(sb, model.History);
                break;
            case "graph":
                sb.Append("<div class=\"pcx-graph\">").Append(model.GraphSvg ?? "<div class=\"pcx-hint\">无结构图数据</div>").Append("</div>");
                break;
            case "diag":
                sb.Append("<pre class=\"pcx-text\">").Append(E(model.DrawerText ?? "(无诊断)")).Append("</pre>");
                break;
            default:
                sb.Append("<div class=\"pcx-hint\">打开抽屉查看历史 / 结构图 / 诊断</div>");
                break;
        }
        return sb.ToString();
    }

    private static void DrawerTab(StringBuilder sb, string drawer, string label, string current)
    {
        sb.Append("<button type=\"button\" class=\"pcx-btn small")
          .Append(string.Equals(drawer, current, StringComparison.Ordinal) ? " active" : "")
          .Append("\" data-pcx=\"drawer.open\" data-arg=\"").Append(A(Arg("drawer", drawer)))
          .Append("\">").Append(E(label)).Append("</button>");
    }

    private static void RenderHistory(StringBuilder sb, PagedResult<JobSnapshot>? history)
    {
        if (history is null)
        {
            sb.Append("<div class=\"pcx-hint\">加载中…</div>");
            return;
        }

        sb.Append("<div class=\"pcx-history-head\">共 ").Append(history.Total).Append(" 条 · 第 ")
          .Append(history.Page).Append("/").Append(Math.Max(1, history.PageCount)).Append(" 页</div>");
        if (history.Items.Count == 0)
        {
            sb.Append("<div class=\"pcx-hint\">无历史记录</div>");
            return;
        }

        sb.Append("<ul class=\"pcx-history\">");
        foreach (var item in history.Items)
        {
            sb.Append("<li data-key=\"h-").Append(A(item.JobId)).Append("\">");
            sb.Append("<span class=\"pcx-badge ").Append(StatusClass(item.Status)).Append("\">")
              .Append(E(StatusText(item.Status))).Append("</span>");
            sb.Append("<code>").Append(E(item.CapabilityId)).Append("</code>");
            sb.Append("<time>").Append(E(item.CreatedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture))).Append("</time>");
            if (!string.IsNullOrWhiteSpace(item.InputHash))
                sb.Append("<code class=\"pcx-hash\">").Append(E(item.InputHash)).Append("</code>");
            sb.Append("<button type=\"button\" class=\"pcx-btn small\" data-pcx=\"job.reuse\" data-arg=\"")
              .Append(A(Arg("jobId", item.JobId))).Append("\">复用</button>");
            sb.Append("</li>");
        }
        sb.Append("</ul>");
    }

    private static string RenderStatus(ConsolePageModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<span class=\"pcx-status-text\">").Append(E(model.StatusText ?? "")).Append("</span>");
        sb.Append("<button type=\"button\" class=\"pcx-btn small\" data-pcx=\"diag.probe\" title=\"重新探测 ComfyUI / ACE-Step 并刷新状态\">重新探活</button>");
        if (!string.IsNullOrWhiteSpace(model.Notice))
            sb.Append("<span class=\"pcx-notice\">").Append(E(model.Notice!)).Append("</span>");
        return sb.ToString();
    }

    // ── 公共工具 ────────────────────────────────────────────

    /// <summary>构造单个键值参数 JSON（写入 data-arg，由页面脚本并入 payload）。</summary>
    private static string Arg(string key, string value) =>
        JsonSerializer.Serialize(new Dictionary<string, object?> { [key] = value });

    private static string Format(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        double d => Fmt(d),
        float f => Fmt(f),
        decimal m => Fmt((double)m),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Fmt(double value) =>
        Math.Abs(value - Math.Round(value)) < 1e-9
            ? ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string StatusText(JobStatus status) => status switch
    {
        JobStatus.Queued => "排队",
        JobStatus.Running => "运行中",
        JobStatus.Succeeded => "完成",
        JobStatus.Failed => "失败",
        JobStatus.Cancelled => "已取消",
        _ => status.ToString(),
    };

    private static string StatusClass(JobStatus status) => status switch
    {
        JobStatus.Queued => "queued",
        JobStatus.Running => "running",
        JobStatus.Succeeded => "ok",
        JobStatus.Failed => "err",
        JobStatus.Cancelled => "cancel",
        _ => "queued",
    };

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#}MB"
        : bytes >= 1024 ? $"{bytes / 1024d:0.#}KB"
        : $"{bytes}B";

    private static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max] + "…";

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    private static string A(string? text) => WebUtility.HtmlEncode(text ?? "");

    /// <summary>页面交互脚本：捕获阶段收集表单值并注入 data-cmd/data-payload，交给既有点击桥回传（零新协议）。</summary>
    private const string Script =
        "(function(){if(window.__pcxInstalled){return;}window.__pcxInstalled=true;" +
        "function collect(){var p={};document.querySelectorAll('[data-param]').forEach(function(el){" +
        "var k=el.getAttribute('data-param');p[k]=(el.type==='checkbox')?el.checked:el.value;});return p;}" +
        "document.addEventListener('click',function(e){var b=e.target&&e.target.closest?e.target.closest('[data-pcx]'):null;if(!b){return;}" +
        "var cmd0=b.getAttribute('data-pcx');" +
        "if(cmd0==='artifact.copy'){try{var o0=JSON.parse(b.getAttribute('data-arg')||'{}');" +
        "if(navigator.clipboard&&o0.path){navigator.clipboard.writeText(o0.path);}}catch(_){}" +
        "e.preventDefault();e.stopPropagation();return;}" +
        "var p={cmd:cmd0};" +
        "if(b.getAttribute('data-pcx-collect')==='1'){p.params=collect();}" +
        "var arg=b.getAttribute('data-arg');if(arg){try{Object.assign(p,JSON.parse(arg));}catch(_){}}" +
        "if(!p.capability){var a=document.querySelector('.pcx-cap.active');if(a){p.capability=a.getAttribute('data-cap');}}" +
        "b.setAttribute('data-cmd','event:click');b.setAttribute('data-route','pcx');" +
        "b.setAttribute('data-payload',JSON.stringify(p));},true);" +
        "document.addEventListener('input',function(e){var t=e.target;if(t&&t.getAttribute&&t.getAttribute('data-range')==='1'){" +
        "var o=t.parentElement&&t.parentElement.querySelector('output');if(o){o.value=t.value;}}});" +
        // 活动任务自动刷新：有 [data-pcx-live] 且焦点不在表单控件时，2s 提交 job.tick（携带未提交表单值）
        "window.setInterval(function(){if(!document.querySelector('[data-pcx-live=\"1\"]')){return;}" +
        "var a=document.activeElement;if(a&&/^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName)){return;}" +
        "try{if(window.chrome&&window.chrome.webview){window.chrome.webview.postMessage({event:'click',name:'pcx'," +
        "payload:JSON.stringify({cmd:'job.tick',params:collect()})});}}catch(_){}},2000);})();";

    public const string Css =
        ".pcx{--pcx-bg:#fff;--pcx-fg:#24292f;--pcx-mut:#57606a;--pcx-line:#d0d7de;--pcx-soft:#f6f8fa;" +
        "--pcx-accent:#0969da;--pcx-ok:#1a7f37;--pcx-warn:#9a6700;--pcx-err:#cf222e;" +
        "display:grid;grid-template-columns:200px 320px minmax(0,1fr);" +
        "grid-template-rows:minmax(0,1fr) minmax(150px,220px) auto auto;gap:8px;height:calc(100vh - 8px);" +
        "color:var(--pcx-fg);font-family:'Segoe UI','Microsoft YaHei',sans-serif;font-size:13px}" +
        "@media (prefers-color-scheme:dark){.pcx{--pcx-bg:#0d1117;--pcx-fg:#e6edf3;--pcx-mut:#8b949e;" +
        "--pcx-line:#30363d;--pcx-soft:#161b22;--pcx-accent:#4493f8;--pcx-ok:#3fb950;--pcx-warn:#d29922;--pcx-err:#f85149}}" +
        ".pcx-region{background:var(--pcx-bg);border:1px solid var(--pcx-line);border-radius:8px;padding:8px;overflow:auto;min-height:0}" +
        ".pcx-col-caps{grid-column:1;grid-row:1/3}.pcx-col-form{grid-column:2;grid-row:1/3}" +
        ".pcx-col-jobs{grid-column:3;grid-row:1}.pcx-col-result{grid-column:3;grid-row:2}" +
        ".pcx-row-drawer{grid-column:1/4;grid-row:3}.pcx-row-status{grid-column:1/4;grid-row:4;padding:4px 8px}" +
        ".pcx-title{font-weight:600;font-size:13px;margin-bottom:6px}" +
        ".pcx-desc,.pcx-hint{color:var(--pcx-mut);font-size:12px;margin:4px 0}" +
        ".pcx-group{margin-bottom:8px}.pcx-group-title{color:var(--pcx-mut);font-size:11px;text-transform:uppercase;margin:6px 0 4px}" +
        ".pcx-cap{display:block;width:100%;text-align:left;padding:6px 8px;margin:2px 0;border:1px solid transparent;" +
        "border-radius:6px;background:var(--pcx-soft);color:inherit;cursor:pointer;font-size:12.5px}" +
        ".pcx-cap:hover{border-color:var(--pcx-line)}.pcx-cap.active{border-color:var(--pcx-accent);color:var(--pcx-accent);font-weight:600}" +
        ".pcx-fields{display:flex;flex-direction:column;gap:8px}" +
        ".pcx-field{display:flex;flex-direction:column;gap:4px}" +
        ".pcx-label{font-size:12px;color:var(--pcx-mut)}.pcx-req{color:var(--pcx-err);margin-left:2px}.pcx-unit{font-style:normal;margin-left:4px}" +
        ".pcx-field input[type=text],.pcx-field input[type=number],.pcx-field textarea,.pcx-field select{width:100%;box-sizing:border-box;" +
        "padding:5px 7px;border:1px solid var(--pcx-line);border-radius:5px;background:var(--pcx-bg);color:inherit;font:inherit}" +
        ".pcx-field textarea{resize:vertical;min-height:64px}.pcx-field textarea.mono{font-family:Consolas,monospace;font-size:12px}" +
        ".pcx-help{color:var(--pcx-mut);font-size:11px}" +
        ".pcx-switch{display:flex;align-items:center;gap:6px;font-size:12px}" +
        ".pcx-range-out{font-size:12px;color:var(--pcx-accent)}" +
        ".pcx-advanced{margin-top:4px}.pcx-advanced summary{cursor:pointer;color:var(--pcx-mut);font-size:12px}" +
        ".pcx-form-actions{display:flex;gap:6px;margin-top:10px;flex-wrap:wrap}" +
        ".pcx-btn{padding:5px 10px;border:1px solid var(--pcx-line);border-radius:6px;background:var(--pcx-soft);color:inherit;" +
        "cursor:pointer;font-size:12px}.pcx-btn:hover{border-color:var(--pcx-accent)}" +
        ".pcx-btn.primary{background:var(--pcx-accent);border-color:var(--pcx-accent);color:#fff;font-weight:600}" +
        ".pcx-btn.small{padding:2px 7px;font-size:11.5px}.pcx-btn.active{border-color:var(--pcx-accent);color:var(--pcx-accent)}" +
        ".pcx-job{border:1px solid var(--pcx-line);border-radius:8px;padding:7px 9px;margin-bottom:7px;background:var(--pcx-soft)}" +
        ".pcx-job.running{border-color:var(--pcx-accent)}.pcx-job.err{border-color:var(--pcx-err)}" +
        ".pcx-job header{display:flex;align-items:center;gap:6px;flex-wrap:wrap}" +
        ".pcx-job header time{margin-left:auto;color:var(--pcx-mut);font-size:11px}" +
        ".pcx-cap-id{font-size:11.5px;color:var(--pcx-mut);background:transparent}" +
        ".pcx-badge{font-size:11px;border-radius:10px;padding:1px 7px;border:1px solid var(--pcx-line);background:var(--pcx-bg)}" +
        ".pcx-badge.queued{color:var(--pcx-mut)}.pcx-badge.running{color:var(--pcx-accent);border-color:var(--pcx-accent)}" +
        ".pcx-badge.ok{color:var(--pcx-ok);border-color:var(--pcx-ok)}.pcx-badge.err{color:var(--pcx-err);border-color:var(--pcx-err)}" +
        ".pcx-badge.cancel{color:var(--pcx-warn)}.pcx-badge.cache,.pcx-badge.plan{color:var(--pcx-accent)}" +
        ".pcx-progress{height:5px;border-radius:3px;background:var(--pcx-line);overflow:hidden;margin:6px 0}" +
        ".pcx-progress>div{height:100%;background:var(--pcx-accent);transition:width .2s ease}" +
        ".pcx-job-meta{font-size:11.5px;color:var(--pcx-mut)}.pcx-job-meta code{font-size:11px}" +
        ".pcx-job-err{color:var(--pcx-err);font-size:11.5px;margin-top:3px}" +
        ".pcx-job-reason{color:var(--pcx-warn);font-size:11.5px;margin-top:3px}" +
        ".pcx-actions{display:flex;gap:5px;margin-top:6px;flex-wrap:wrap}" +
        ".pcx-result-head{display:flex;align-items:center;gap:6px;margin-bottom:6px}" +
        ".pcx-gallery{display:flex;flex-wrap:wrap;gap:8px}" +
        ".pcx-artifact{margin:0;border:1px solid var(--pcx-line);border-radius:8px;padding:6px;max-width:100%;background:var(--pcx-soft)}" +
        ".pcx-artifact img{max-width:100%;max-height:320px;border-radius:4px;display:block}" +
        ".pcx-artifact audio{width:320px;max-width:100%}" +
        ".pcx-artifact figcaption{font-size:11px;color:var(--pcx-mut);margin-top:5px;display:flex;gap:5px;flex-wrap:wrap;align-items:center}" +
        ".pcx-artifact-err{color:var(--pcx-err);font-size:12px}" +
        ".pcx-text{margin:0;white-space:pre-wrap;word-break:break-word;font-family:Consolas,monospace;font-size:11.5px;max-height:260px;overflow:auto}" +
        ".pcx-drawer-tabs{display:flex;gap:6px;margin-bottom:6px;align-items:center}" +
        ".pcx-drawer-tabs .pcx-btn.active{border-color:var(--pcx-accent);color:var(--pcx-accent)}" +
        ".pcx-history{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:4px}" +
        ".pcx-history li{display:flex;align-items:center;gap:7px;font-size:12px}" +
        ".pcx-history time,.pcx-hash{color:var(--pcx-mut);font-size:11px}" +
        ".pcx-graph svg{max-width:100%;height:auto}" +
        ".pcx-status-text{color:var(--pcx-mut);font-size:12px}" +
        ".pcx-notice{color:var(--pcx-err);font-size:12px;margin-left:12px}";
}
