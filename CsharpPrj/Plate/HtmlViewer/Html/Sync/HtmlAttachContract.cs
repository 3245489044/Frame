#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TConfig;

namespace UiBackend.Html;

/// <summary>
/// 装配期契约检查与模板/主题合成（通用件，唯一实现）—— 把「绑定函数在注册库 / output 目标唯一 / 区域目标已声明 /
/// 模板键契约 / 基础主题 + 应用差异」从装配方法体中分离：任意 HTML 宿主复用同一判据与同一合成口径。
/// </summary>
public static class HtmlAttachContract
{
    /// <summary>R2：写回目标 <c>region:&lt;key&gt;</c> 须在页面 regions 声明内；未声明 → 装配期问题（不静默）。</summary>
    public static void ValidateRegionTargets(
        HtmlRuntimeReadinessReport report,
        IReadOnlyDictionary<string, ControlConfig> index,
        RegionCatalog regions)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(regions);

        foreach (var control in index.Values)
        {
            var output = control.Events?.Bindings?.Output;
            if (!RegionCatalog.IsRegionTarget(output)) continue;
            var key = RegionCatalog.Normalize(output!);
            if (!regions.IsDeclared(key))
                report.AddProblem($"output 区域未声明: {control.Name} → region:{key}");
        }
    }

    /// <summary>填充就绪报告静态可判定项：绑定函数是否在注册库、output 写回目标是否唯一命中索引；缺失记入 Problems。</summary>
    public static void PopulateStaticReadiness(
        HtmlRuntimeReadinessReport report,
        IReadOnlyDictionary<string, ControlConfig> index,
        ITrConfigRegistry registry,
        IReadOnlySet<string>? workflowNames = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(registry);

        var missing = new List<string>();
        var unresolvedOutputs = new List<string>();
        foreach (var control in index.Values)
        {
            if (control.Events?.EventsDictionary is not { Count: > 0 } functions)
                continue;
            foreach (var fn in functions.Values.SelectMany(f => f))
            {
                if (string.IsNullOrWhiteSpace(fn)) continue;
                if (!registry.Contains(fn) && (workflowNames is null || !workflowNames.Contains(fn)))
                    missing.Add($"{control.Name}.{fn}");
            }
            var output = control.Events.Bindings?.Output;
            // R2：region: 目标由区域声明单独校验（ValidateRegionTargets），不参与控件索引唯一性判定。
            if (!string.IsNullOrWhiteSpace(output)
                && !RegionCatalog.IsRegionTarget(output)
                && !index.ContainsKey(output)
                && !index.ContainsKey(HtmlDeclarationParser.HostKey(output))
                && !index.ContainsKey(HtmlDeclarationParser.PageKey(output)))
                unresolvedOutputs.Add($"{control.Name}→{output}");
        }
        report.FunctionsInRegistry = missing.Count == 0;
        report.OutputTargetUnique = unresolvedOutputs.Count == 0;
        foreach (var m in missing)
            report.AddProblem($"绑定函数未在注册库: {m}");
        foreach (var o in unresolvedOutputs)
            report.AddProblem($"output 目标未命中索引: {o}");
    }

    /// <summary>
    /// 模板集合成（框架默认为底 + 配置覆盖）：默认集为空 ⇒ 诊断可见（配置未声明时页面将缺壳）。
    /// 合成结果由调用方注入宿主槽后，再经 <see cref="EnforceTemplateKeys"/> 做键契约（保持原装配顺序）。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ComposeTemplates(
        IHtmlAssetProvider provider,
        IReadOnlyDictionary<string, string> templates,
        Action<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(templates);

        var defaults = provider.GetDefaultTemplates();
        if (defaults.Count == 0)
            diagnostics?.Invoke("HtmlAssets_default_templates_empty",
                "框架默认模板集为空（资产未嵌入或提供者未覆写 GetDefaultTemplates）→ 配置模板独占；若配置也未声明，页面将缺壳。");
        var resolved = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in templates)
            resolved[kv.Key] = kv.Value;
        return resolved;
    }

    /// <summary>P0 键契约（I2/I4）：全元素 × 同父键唯一性 + 缺稳定键；违规先诊断可见化再装配期 fail-fast。</summary>
    public static void EnforceTemplateKeys(
        IReadOnlyDictionary<string, string> resolved,
        Action<string, string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var keyViolations = HtmlKeyContract.ValidateAll(resolved);
        if (keyViolations.Count == 0) return;
        diagnostics?.Invoke("HtmlKeyContract",
            $"模板键契约：{keyViolations.Count} 处违规（缺稳定键 / 同父键冲突）→ 展开态不保持或键控对齐失效（P0）。首项: {keyViolations[0]}");
        HtmlKeyContract.Enforce(keyViolations);
    }

    /// <summary>P1 主题「基础 + 差异」：框架基础主题恒为底，应用 theme 追加/覆盖其上（应用只声明差异）。</summary>
    public static string ComposeThemeCss(string? baseCss, string themeCss)
        => string.IsNullOrWhiteSpace(baseCss) ? themeCss : baseCss + "\n" + themeCss;
}
