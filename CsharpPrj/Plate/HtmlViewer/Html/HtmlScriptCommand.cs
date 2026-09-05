#nullable enable

using System.Text.Json.Serialization;

namespace UiBackend.Html;

/// <summary>
/// HTML 脚本命令（轻量模型，字段对齐 04-json-script 的 ScriptCommand：action/target/value/output）。
/// UiBackend 不引用 FileDriven 全链路，宿主（AgentUi_HtmlScript）负责 JSON 反序列化与模板展开后按本模型执行。
/// </summary>
public sealed class HtmlScriptCommand
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    /// <summary>模板命令名（{template} 语法）；宿主展开为普通命令或返回"不支持"诊断。</summary>
    [JsonPropertyName("template")]
    public string? Template { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// <summary>json-script 命令动作白名单中命令特有项（set/get/click 复用 <see cref="DomOps"/>；exec/sleep 为命令级动作）。</summary>
public static class ScriptCommand
{
    public const string Exec = "exec";
    public const string Sleep = "sleep";
}
