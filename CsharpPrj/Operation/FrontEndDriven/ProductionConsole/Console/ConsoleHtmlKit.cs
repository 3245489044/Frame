using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using FrontEndDriven.ProductionConsole.Assistant;
using UiBackend.Html;

namespace FrontEndDriven.ProductionConsole.Console;

/// <summary>
/// 通用生产控制台渲染器（声明驱动）：只把 <see cref="ConsolePageModel"/> 投影成**数据模型**，
/// 结构与文案由 <see cref="ConsoleTemplateSet"/>（面板 JSON）承担 —— 本类不含 HTML 片段、CSS、JS。
/// <para>区域片段可单独渲染供 patch 写回（保展开/滚动/焦点）；整页壳 <c>consoleShell</c> 负责区域容器与样式脚本挂载。</para>
/// </summary>
public static class ConsoleHtmlKit
{
    public const string ShellTemplate = "consoleShell";
    public const string StyleTemplate = "consoleStyle";
    public const string ScriptTemplate = "consoleScript";

    /// <summary>整页 body 片段（区域容器 + 样式 + 脚本均由 consoleShell 模板声明）。</summary>
    /// <param name="renderedRegions">已渲染的区域片段（命中则直接复用，避免同拍二次渲染；缺省则按需渲染）。</param>
    public static string RenderPage(
        ConsolePageModel model,
        ConsoleTemplateSet? templates = null,
        IReadOnlyDictionary<string, string>? renderedRegions = null)
    {
        model ??= new ConsolePageModel();
        var set = templates ?? ConsoleTemplateSet.Empty;
        var shell = set.Template(ShellTemplate);
        if (shell is null) return Missing(ShellTemplate);

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["css"] = set.Template(StyleTemplate) ?? string.Empty,
            ["script"] = set.Template(ScriptTemplate) ?? string.Empty,
            ["caps"] = RegionHtml(ConsoleRegions.Caps, model, set, renderedRegions),
            ["form"] = RegionHtml(ConsoleRegions.Form, model, set, renderedRegions),
            ["assist"] = RegionHtml(ConsoleRegions.Assist, model, set, renderedRegions),
            ["jobs"] = RegionHtml(ConsoleRegions.Jobs, model, set, renderedRegions),
            ["result"] = RegionHtml(ConsoleRegions.Result, model, set, renderedRegions),
            ["drawer"] = RegionHtml(ConsoleRegions.Drawer, model, set, renderedRegions),
            ["status"] = RegionHtml(ConsoleRegions.Status, model, set, renderedRegions),
        };
        return HtmlTemplateEngine.RenderTemplate(shell, set.Templates, data);
    }

    private static string RegionHtml(
        string region,
        ConsolePageModel model,
        ConsoleTemplateSet set,
        IReadOnlyDictionary<string, string>? rendered)
        => rendered is not null && rendered.TryGetValue(region, out var html)
            ? html
            : RenderRegion(region, model, set);

    /// <summary>单区域 HTML（patch 写回目标）。</summary>
    public static string RenderRegion(string region, ConsolePageModel model, ConsoleTemplateSet? templates = null)
    {
        model ??= new ConsolePageModel();
        var set = templates ?? ConsoleTemplateSet.Empty;
        var name = TemplateFor(region);
        var template = set.Template(name);
        if (template is null) return Missing(name);
        return HtmlTemplateEngine.RenderTemplate(template, set.Templates, BuildModel(region, model, set));
    }

    private static string TemplateFor(string region) => region switch
    {
        ConsoleRegions.Caps => "consoleCaps",
        ConsoleRegions.Form => "consoleForm",
        ConsoleRegions.Assist => "consoleAssist",
        ConsoleRegions.Jobs => "consoleJobs",
        ConsoleRegions.Result => "consoleResult",
        ConsoleRegions.Drawer => "consoleDrawer",
        ConsoleRegions.Status => "consoleStatus",
        _ => "consoleUnknown",
    };

    private static string Missing(string templateName)
        => $"<div class=\"pcx-hint\">控制台模板未装配：{templateName}（请在面板 templates 中声明）</div>";

    private static Dictionary<string, object?> BuildModel(string region, ConsolePageModel model, ConsoleTemplateSet set)
        => region switch
        {
            ConsoleRegions.Caps => CapsModel(model, set),
            ConsoleRegions.Form => FormModel(model, set),
            ConsoleRegions.Assist => AssistModel(model, set),
            ConsoleRegions.Jobs => JobsModel(model, set),
            ConsoleRegions.Result => ResultModel(model, set),
            ConsoleRegions.Drawer => DrawerModel(model, set),
            ConsoleRegions.Status => StatusModel(model, set),
            _ => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
        };

    // ── 能力 ────────────────────────────────────────────────

    private static Dictionary<string, object?> CapsModel(ConsolePageModel model, ConsoleTemplateSet set)
    {
        var groups = new List<object?>();
        foreach (var group in model.Capabilities.GroupBy(c => string.IsNullOrWhiteSpace(c.Group) ? "默认" : c.Group))
        {
            var caps = new List<object?>();
            foreach (var cap in group)
            {
                caps.Add(set.Catalog.Apply("capRow", new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = cap.Id,
                    ["name"] = cap.Name,
                    ["description"] = cap.Description,
                    ["current"] = model.SelectedCapabilityId ?? string.Empty,
                    ["payload"] = Payload("cap.select", ("capability", cap.Id)),
                }));
            }
            groups.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = group.Key,
                ["caps"] = caps,
            });
        }

        return Model(
            ("hasCaps", Flag(model.Capabilities.Count > 0)),
            ("groups", groups));
    }

    // ── 表单 ────────────────────────────────────────────────

    private static Dictionary<string, object?> FormModel(ConsolePageModel model, ConsoleTemplateSet set)
    {
        var cap = SelectedCapability(model);
        if (cap is null)
            return Model(("hasCap", string.Empty));

        var fields = new List<object?>();
        var advanced = new List<object?>();
        foreach (var spec in cap.Params)
            (spec.Advanced ? advanced : fields).Add(FieldModel(spec, model, set));

        var actions = new List<object?>();
        for (var i = 0; i < cap.Actions.Count; i++)
        {
            actions.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["label"] = cap.Actions[i],
                ["action"] = cap.Actions[i],
                ["index"] = i.ToString(CultureInfo.InvariantCulture),
                ["payload"] = Payload("cap.action", ("action", cap.Actions[i]), ("capability", cap.Id)),
            });
        }

        return Model(
            ("hasCap", Flag(true)),
            ("capability", cap.Id),
            ("capName", cap.Name),
            ("capDescription", cap.Description),
            ("hasDescription", Flag(!string.IsNullOrWhiteSpace(cap.Description))),
            ("runPayload", Payload("form.run", ("capability", cap.Id))),
            ("resetPayload", Payload("form.reset", ("capability", cap.Id))),
            ("fields", fields),
            ("advancedFields", advanced),
            ("hasAdvanced", Flag(advanced.Count > 0)),
            ("actions", actions));
    }

    private static IReadOnlyDictionary<string, object?> FieldModel(ParamSpec spec, ConsolePageModel model, ConsoleTemplateSet set)
    {
        var value = model.FormValues.TryGetValue(spec.Key, out var raw) ? raw : spec.Default;
        var text = Format(value);
        var options = new List<object?>();
        foreach (var option in spec.Options ?? new List<string>())
        {
            options.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["value"] = option,
                ["label"] = option,
                ["selected"] = Flag(string.Equals(option, text, StringComparison.OrdinalIgnoreCase)),
            });
        }

        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = spec.Key,
            ["name"] = "param-" + spec.Key,
            ["label"] = ParamSet.Label(spec),
            ["kind"] = KindText(spec.Kind),
            ["required"] = Flag(spec.Required),
            ["unit"] = spec.Unit ?? string.Empty,
            ["hasUnit"] = Flag(!string.IsNullOrWhiteSpace(spec.Unit)),
            ["help"] = spec.Help ?? string.Empty,
            ["hasHelp"] = Flag(!string.IsNullOrWhiteSpace(spec.Help)),
            ["value"] = text,
            ["checked"] = Flag(string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)),
            ["min"] = spec.Min is { } min ? Fmt(min) : string.Empty,
            ["max"] = spec.Max is { } max ? Fmt(max) : string.Empty,
            ["step"] = spec.Step is { } step ? Fmt(step) : string.Empty,
            ["hasMin"] = Flag(spec.Min is not null),
            ["hasMax"] = Flag(spec.Max is not null),
            ["hasStep"] = Flag(spec.Step is not null),
            ["rangeMin"] = Fmt(spec.Min ?? 0),
            ["rangeMax"] = Fmt(spec.Max ?? 100),
            ["rangeStep"] = Fmt(spec.Step ?? 1),
            ["options"] = options,
        };
        return set.Catalog.Apply("fieldRow", row);
    }

    // ── AI 参数助理 ─────────────────────────────────────────

    private static Dictionary<string, object?> AssistModel(ConsolePageModel model, ConsoleTemplateSet set)
    {
        var view = model.Assistant;
        var pending = new List<object?>();
        if (view?.PendingProposal is { Count: > 0 } proposal)
            foreach (var kv in proposal)
                pending.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["key"] = kv.Key,
                    ["value"] = Format(kv.Value),
                });

        return Model(
            ("unavailable", Flag(view is null || !view.IsAvailable)),
            ("prompt", view?.Prompt ?? string.Empty),
            ("proposePayload", Payload("assist.propose")),
            ("applyPayload", Payload("assist.apply")),
            ("hasPending", Flag(pending.Count > 0)),
            ("pending", pending),
            ("hasError", Flag(!string.IsNullOrWhiteSpace(view?.ProposalError))),
            ("error", view?.ProposalError ?? string.Empty));
    }

    // ── 任务 ────────────────────────────────────────────────

    private static Dictionary<string, object?> JobsModel(ConsolePageModel model, ConsoleTemplateSet set)
    {
        var jobs = new List<object?>();
        foreach (var job in model.Jobs)
            jobs.Add(set.Catalog.Apply("jobRow", JobModel(job)));

        return Model(
            ("hasLive", Flag(model.Jobs.Any(j => !j.IsTerminal))),
            ("jobs", jobs),
            ("empty", Flag(jobs.Count == 0)));
    }

    private static Dictionary<string, object?> JobModel(JobSnapshot job) => Model(
        ("jobId", job.JobId),
        ("capabilityId", job.CapabilityId),
        ("status", job.Status.ToString()),
        ("progress", Fmt(Math.Round(job.Progress * 100, 1))),
        ("phase", job.Phase),
        ("elapsedMs", job.ElapsedMs.ToString(CultureInfo.InvariantCulture)),
        ("createdAt", job.CreatedAt.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        ("queuePosition", job.QueuePosition.ToString(CultureInfo.InvariantCulture)),
        ("hasQueue", Flag(job.QueuePosition > 0)),
        ("inputHash", job.InputHash ?? string.Empty),
        ("hasInputHash", Flag(!string.IsNullOrWhiteSpace(job.InputHash))),
        ("cacheHit", Flag(job.CacheHit)),
        ("planned", Flag(job.Planned)),
        ("error", job.Error ?? string.Empty),
        ("hasError", Flag(!string.IsNullOrWhiteSpace(job.Error))),
        ("reason", job.Reason ?? string.Empty),
        ("hasReason", Flag(!string.IsNullOrWhiteSpace(job.Reason))),
        ("canCancel", Flag(!job.IsTerminal)),
        ("canReuse", Flag(job.IsTerminal)),
        ("canRetry", Flag(job.Status is JobStatus.Failed or JobStatus.Cancelled)),
        ("canDelete", Flag(job.IsTerminal)),
        ("selectPayload", Payload("job.select", ("jobId", job.JobId))),
        ("cancelPayload", Payload("job.cancel", ("jobId", job.JobId))),
        ("reusePayload", Payload("job.reuse", ("jobId", job.JobId))),
        ("retryPayload", Payload("job.retry", ("jobId", job.JobId))),
        ("deletePayload", Payload("job.delete", ("jobId", job.JobId))));

    // ── 结果 ────────────────────────────────────────────────

    private static Dictionary<string, object?> ResultModel(ConsolePageModel model, ConsoleTemplateSet set)
    {
        var job = model.SelectedJob;
        if (job is null) return Model(("hasJob", string.Empty));

        var artifacts = new List<object?>();
        foreach (var view in model.Artifacts)
        {
            var spec = view.Spec;
            var meta = new List<object?>();
            if (spec.Meta is { Count: > 0 })
                foreach (var kv in spec.Meta)
                    meta.Add(Model(("key", kv.Key), ("value", kv.Value)));

            artifacts.Add(set.Catalog.Apply("artifactRow", Model(
                ("name", System.IO.Path.GetFileName(spec.Path)),
                ("path", spec.Path),
                ("url", view.Url.Url ?? string.Empty),
                ("usable", Flag(view.Url.IsUsable)),
                ("error", view.Url.Error ?? "产物不可用"),
                ("kind", spec.Kind),
                ("text", Truncate(view.Text ?? spec.Path, 4000)),
                ("bytes", spec.Bytes.ToString(CultureInfo.InvariantCulture)),
                ("meta", meta),
                ("openPayload", Payload("artifact.open", ("path", spec.Path))))));
        }

        var data = Model(
            ("hasJob", Flag(true)),
            ("jobId", job.JobId),
            ("status", job.Status.ToString()),
            ("cacheHit", Flag(job.CacheHit)),
            ("planned", Flag(job.Planned)),
            ("reason", job.Reason ?? string.Empty),
            ("hasReason", Flag(!string.IsNullOrWhiteSpace(job.Reason))),
            ("hasArtifacts", Flag(artifacts.Count > 0)),
            ("canVerify", Flag(job.IsTerminal && artifacts.Count > 0)),
            ("verifyPayload", Payload("artifact.verify", ("jobId", job.JobId))),
            ("artifacts", artifacts));

        // 结果区状态徽章与任务卡同一组件口径（否则 statusText/statusClass 缺失 → 空徽章）。
        foreach (var kv in set.Catalog.Apply("jobRow", data)) data[kv.Key] = kv.Value;
        return data;
    }

    // ── 抽屉 / 状态 ─────────────────────────────────────────

    private static Dictionary<string, object?> DrawerModel(ConsolePageModel model, ConsoleTemplateSet set)
    {
        ConsoleDrawerSpec? current = null;
        foreach (var spec in model.Drawers)
        {
            if (string.Equals(spec.Key, model.Drawer, StringComparison.OrdinalIgnoreCase)) current = spec;
        }

        var tabs = new List<object?>();
        foreach (var spec in model.Drawers)
        {
            tabs.Add(set.Catalog.Apply("tabRow", Model(
                ("key", spec.Key),
                ("label", spec.Label),
                ("current", model.Drawer),
                ("payload", Payload("drawer.open", ("drawer", spec.Key))))));
        }

        var drawer = model.Drawer;
        var data = Model(
            ("drawer", drawer),
            ("tabs", tabs),
            ("isHistory", Flag(current?.Kind == DrawerKind.Container)),
            ("isGraph", Flag(current?.Kind == DrawerKind.Graph)),
            ("isDiag", Flag(current?.Kind == DrawerKind.Text)),
            ("isNone", Flag(current is null)),
            ("graphBody", model.GraphSvg ?? "<div class=\"pcx-hint\">无结构图数据</div>"),
            ("diagText", model.DrawerText ?? "(无诊断)"),
            ("closePayload", Payload("drawer.open", ("drawer", "none"))));

        // 历史子模型扁平化（模板 {>consoleHistory} 以当前模型渲染，命名模板不切换作用域）。
        foreach (var kv in HistoryModel(model, set)) data["history" + char.ToUpperInvariant(kv.Key[0]) + kv.Key[1..]] = kv.Value;
        return data;
    }

    private static Dictionary<string, object?> HistoryModel(ConsolePageModel model, ConsoleTemplateSet set)
    {
        var paged = model.History;
        if (paged is null)
            return Model(("loading", Flag(true)), ("hasItems", string.Empty));

        var items = new List<object?>();
        foreach (var item in paged.Items)
        {
            items.Add(set.Catalog.Apply("historyRow", Model(
                ("jobId", item.JobId),
                ("capabilityId", item.CapabilityId),
                ("status", item.Status.ToString()),
                ("createdAt", item.CreatedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)),
                ("inputHash", item.InputHash ?? string.Empty),
                ("hasInputHash", Flag(!string.IsNullOrWhiteSpace(item.InputHash))))));
        }

        return Model(
            ("loading", string.Empty),
            ("filter", model.HistoryFilter ?? string.Empty),
            ("total", paged.Total.ToString(CultureInfo.InvariantCulture)),
            ("page", paged.Page.ToString(CultureInfo.InvariantCulture)),
            ("pageCount", Math.Max(1, paged.PageCount).ToString(CultureInfo.InvariantCulture)),
            ("hasPrev", Flag(paged.Page > 1)),
            ("hasNext", Flag(paged.Page < paged.PageCount)),
            ("prevPage", Math.Max(1, paged.Page - 1).ToString(CultureInfo.InvariantCulture)),
            ("nextPage", (paged.Page + 1).ToString(CultureInfo.InvariantCulture)),
            ("filterPayload", Payload("history.query", ("page", 1))),
            ("prevPayload", Payload("history.query", ("page", Math.Max(1, paged.Page - 1)))),
            ("nextPayload", Payload("history.query", ("page", paged.Page + 1))),
            ("hasItems", Flag(items.Count > 0)),
            ("items", items));
    }

    private static Dictionary<string, object?> StatusModel(ConsolePageModel model, ConsoleTemplateSet set) => Model(
        ("statusText", model.StatusText ?? string.Empty),
        ("hasNotice", Flag(!string.IsNullOrWhiteSpace(model.Notice))),
        ("notice", model.Notice ?? string.Empty),
        ("diagPayload", Payload("diag.probe")));

    // ── 公共工具 ────────────────────────────────────────────

    private static Dictionary<string, object?> Model(params (string Key, object? Value)[] pairs)
    {
        var model = new Dictionary<string, object?>(pairs.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs) model[key] = value;
        return model;
    }

    /// <summary>布尔 → 模板真值（"1"/""；模板对非空串判真）。</summary>
    private static string Flag(bool value) => value ? "1" : string.Empty;

    private static CapabilityDescriptor? SelectedCapability(ConsolePageModel model) =>
        string.IsNullOrWhiteSpace(model.SelectedCapabilityId)
            ? null
            : model.Capabilities.FirstOrDefault(c =>
                string.Equals(c.Id, model.SelectedCapabilityId, StringComparison.OrdinalIgnoreCase));

    private static string KindText(ParamKind kind) => kind switch
    {
        ParamKind.Multiline => "multiline",
        ParamKind.Number => "number",
        ParamKind.Range => "range",
        ParamKind.Bool => "bool",
        ParamKind.Enum => "enum",
        ParamKind.Json => "json",
        ParamKind.Path => "path",
        _ => "text",
    };

    /// <summary>页面命令负载（C# 单点序列化 → 模板只做属性绑定；避免自由文本插值破坏 JSON）。</summary>
    public static string Payload(string cmd, params (string Key, object? Value)[] args)
    {
        var data = new Dictionary<string, object?>(args.Length + 1) { ["cmd"] = cmd };
        foreach (var (key, value) in args) data[key] = value;
        return System.Text.Json.JsonSerializer.Serialize(data);
    }

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

    private static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max] + "…";
}
