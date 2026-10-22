using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Plate.PowerShellHost;

/// <summary>通用超时（秒）。<c>ScriptSeconds=0</c> ⇒ 一次性脚本默认无上限（由调用方显式给值）。</summary>
public sealed record PowerShellHostTimeouts(
    int StartSeconds = 30,
    int RequestSeconds = 60,
    int ScriptSeconds = 0);

/// <summary>
/// <c>ps1-runtime.json</c> 的通用子集（executable / hostScript / projectRoot / timeouts）。
/// <para>
/// 领域字段（contractDirs / basemethodDeps / registry）不在此读取 —— 由调用方各自解析，
/// 避免通用框架反向依赖业务语义。回退**可见**：<see cref="FallbackReason"/> 非空即表示用了默认值。
/// </para>
/// </summary>
public sealed record PowerShellHostOptions(
    string? ExecutablePath,
    string? HostScript,
    string? ProjectRoot,
    PowerShellHostTimeouts Timeouts)
{
    /// <summary>实际命中的配置文件路径（未命中 ⇒ null；来源可审计）。</summary>
    public string? ConfigSource { get; init; }

    /// <summary>回退原因（null = 完整命中；非空 = 缺失/损坏/字段默认，禁静默）。</summary>
    public string? FallbackReason { get; init; }

    /// <summary>缺失或非法而取默认值的字段名（有界，供诊断）。</summary>
    public IReadOnlyList<string> DefaultedFields { get; init; } = Array.Empty<string>();

    public TimeSpan StartTimeout => TimeSpan.FromSeconds(Math.Max(1, Timeouts.StartSeconds));

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Max(1, Timeouts.RequestSeconds));

    /// <summary>脚本超时（0 ⇒ 无上限 ⇒ null）。</summary>
    public TimeSpan? ScriptTimeout => Timeouts.ScriptSeconds > 0
        ? TimeSpan.FromSeconds(Timeouts.ScriptSeconds)
        : null;

    /// <summary>读取配置；缺文件/坏 JSON/坏字段 ⇒ 默认值 + 可见回退标记。</summary>
    public static PowerShellHostOptions Load(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return Fallback(null, "missing: 未配置路径");
        }

        if (!File.Exists(configPath))
        {
            return Fallback(configPath, "missing: 文件不存在");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(configPath));
        }
        catch (JsonException ex)
        {
            return Fallback(configPath, "invalid: JSON 解析失败（" + ex.Message + "）");
        }
        catch (IOException ex)
        {
            return Fallback(configPath, "invalid: 读取失败（" + ex.Message + "）");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Fallback(configPath, "invalid: 无读取权限（" + ex.Message + "）");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fallback(configPath, "invalid: 根节点不是对象");
            }

            var defaulted = new List<string>();
            var timeoutIssues = new List<string>();
            var executable = ReadString(root, "executable", defaulted);
            var hostScript = ReadString(root, "hostScript", defaulted);
            var projectRoot = ReadString(root, "projectRoot", defaulted);

            var timeouts = new PowerShellHostTimeouts();
            if (root.TryGetProperty("timeouts", out var t) && t.ValueKind == JsonValueKind.Object)
            {
                timeouts = new PowerShellHostTimeouts(
                    ReadInt(t, "startSeconds", timeouts.StartSeconds, timeoutIssues),
                    ReadInt(t, "callSeconds", timeouts.RequestSeconds, timeoutIssues),
                    ReadInt(t, "scriptSeconds", timeouts.ScriptSeconds, timeoutIssues));
            }
            else
            {
                timeoutIssues.Add("timeouts");
            }

            // 回退可见性只按"真正取默认"计：可选字段（executable/hostScript/projectRoot）缺失是合法形态，
            // 不构成回退；timeouts 缺失/非法才影响运行时行为，必须可见。
            var reason = timeoutIssues.Count == 0
                ? null
                : "defaulted: " + string.Join(", ", timeoutIssues);

            return new PowerShellHostOptions(executable, hostScript, projectRoot, timeouts)
            {
                ConfigSource = configPath,
                FallbackReason = reason,
                DefaultedFields = defaulted,
            };
        }
    }

    private static PowerShellHostOptions Fallback(string? source, string reason)
    {
        var defaults = new PowerShellHostOptions(null, null, null, new PowerShellHostTimeouts())
        {
            ConfigSource = source,
            FallbackReason = reason,
        };
        return defaults with { DefaultedFields = new[] { "executable", "hostScript", "projectRoot", "timeouts" } };
    }

    private static string? ReadString(JsonElement root, string name, List<string> defaulted)
    {
        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        defaulted.Add(name);
        return null;
    }

    private static int ReadInt(JsonElement root, string name, int fallback, List<string> defaulted)
    {
        if (root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) && number > 0)
            return number;

        defaulted.Add("timeouts." + name);
        return fallback;
    }
}
