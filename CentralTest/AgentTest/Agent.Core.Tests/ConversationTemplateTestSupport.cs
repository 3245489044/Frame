using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Agent.Launcher;

namespace Agent.Core.Tests;

/// <summary>
/// 会话渲染测试装配帮助：从 AgentRuntimeUI.json（已拷入测试输出 Configs\ 目录）加载 templates，挂到
/// <see cref="AgentUiHost.ConversationTemplates"/> 槽位，使渲染测试以「配置为唯一模板源」运行（不再依赖 C# 内嵌默认）。
/// </summary>
internal static class ConversationTemplateTestSupport
{
    public static void AttachFromConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Configs", "AgentRuntimeUI.json");
        var json = File.ReadAllText(path);
        var templates = ParseTemplates(json);
        AgentUiHost.AttachConversationTemplates(templates);
    }

    public static void Detach() => AgentUiHost.DetachConversationTemplates();

    /// <summary>从 AgentRuntimeUI.json 文本提取 templates 段（对象 → 字符串字典）。</summary>
    private static IReadOnlyDictionary<string, string> ParseTemplates(string json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("templates", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in t.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    dict[p.Name] = p.Value.GetString()!;
        }
        return dict;
    }
}
