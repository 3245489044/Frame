#nullable enable

using System;
using System.Text;
using System.Threading.Tasks;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.ProductionConsole.Assistant;
using FrontEndDriven.Shared.Catalog;
using Modules.Registry;
using TConfig;
using UiPathManager = FrontEndDriven.Shared.Utils.PathManager;

namespace FrontEndDriven;

/// <summary>
/// 通用 JSON 助理命令库：把「模板 + 自然语言 → 单步 JSON」暴露为 JSON 控制台可绑定的函数。
/// <para>生成结果写回 JSON 编辑器控件（唯一真值），执行仍走既有 <c>runJson</c> 命令同源路径；
/// 本库不新增执行面、不新增 LLM 客户端，来源由 <see cref="JsonAssistRuntime"/> 统一提供。</para>
/// </summary>
[FunctionLibrary]
public static class UiJsonAssistLibrary
{
    public static void RegisterAll(ITrConfigRegistry registry)
    {
        registry.Register("Ui_JsonAssistPropose",
            (Func<string, string, string, Task<string>>)(ProposeAsync),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "template", Type = typeof(string) },
                new ParameterDescriptor { Name = "prompt", Type = typeof(string) },
                new ParameterDescriptor { Name = "current", Type = typeof(string) },
            },
            "AI 生成：模板 id + 自然语言 + 当前 JSON → 单步提案（只覆盖被建议键），写回 JSON 编辑器；失败保持原文并记录日志");

        registry.Register("Ui_JsonAssistCheck",
            (Func<string, string, string>)(Check),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "template", Type = typeof(string) },
                new ParameterDescriptor { Name = "current", Type = typeof(string) },
            },
            "AI 校验：按模板顶层键/示例类型校验当前 JSON 编辑器内容（输出到终端；空错误 = 通过）");

        registry.Register("Ui_JsonAssistSchema",
            (Func<string, string>)(Schema),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "template", Type = typeof(string) },
            },
            "AI 说明：输出所选模板的可用键 schema 文本（供人工核对提示词覆盖面）");
    }

    /// <summary>生成单步提案并返回合并后的 JSON（写回编辑器控件）。</summary>
    private static async Task<string> ProposeAsync(string template, string prompt, string current)
    {
        var templateId = template?.Trim() ?? "";
        var templateJson = LoadTemplate(templateId);

        var request = new JsonProposalRequest
        {
            TargetId = templateId,
            Prompt = prompt ?? "",
            SchemaText = JsonSchemaText.FromTemplate(templateJson),
            TemplateJson = templateJson,
            CurrentJson = current,
        };

        var assistant = JsonAssistRuntime.Assistant;
        var ok = await assistant.ProposeAsync(request).ConfigureAwait(false);
        if (!ok)
        {
            var error = assistant.Snapshot(templateId).Error ?? "未知原因";
            FrontEndDrivenLog.Log("UiJsonAssist", $"{error}（模板: {templateId}）");
            return current ?? "";
        }

        var snapshot = assistant.Snapshot(templateId);
        if (snapshot.Warnings.Count > 0)
            FrontEndDrivenLog.Log("UiJsonAssist", string.Join("；", snapshot.Warnings));
        return snapshot.ProposedJson ?? current ?? "";
    }

    /// <summary>校验当前 JSON 是否满足模板约束（输出人类可读结论）。</summary>
    private static string Check(string template, string current)
    {
        var templateJson = LoadTemplate(template);
        var errors = JsonProposalPipeline.ValidateAgainstTemplate(current, templateJson);
        if (errors.Count == 0) return "JSON 校验通过（键与类型符合模板）";

        var sb = new StringBuilder("JSON 校验失败：");
        for (var i = 0; i < errors.Count; i++)
        {
            if (i > 0) sb.Append("；");
            sb.Append(errors[i]);
        }
        FrontEndDrivenLog.Log("UiJsonAssist", sb.ToString());
        return sb.ToString();
    }

    /// <summary>输出模板可用键的 schema 文本。</summary>
    private static string Schema(string template)
    {
        var templateJson = LoadTemplate(template);
        var text = JsonSchemaText.FromTemplate(templateJson);
        return string.IsNullOrWhiteSpace(text) ? "（未选择模板或模板不可读，无 schema）" : text.TrimEnd();
    }

    /// <summary>按模板 id 载入模板原文（空 id / 读取失败 → null）。</summary>
    private static string? LoadTemplate(string? template)
    {
        var id = FileCatalogService.ExtractId(template ?? "");
        if (id.Length == 0) return null;
        try
        {
            return FileCatalogService.LoadText(
                new FileCatalogSpec { Dir = UiPathManager.GetAgentRunTemplatesRoot() }, id);
        }
        catch (Exception ex)
        {
            FrontEndDrivenLog.Log("UiJsonAssist", $"模板读取失败: {id} :: {ex.Message}");
            return null;
        }
    }
}
