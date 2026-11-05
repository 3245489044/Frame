using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Plate.PowerShellHost;

/// <summary>
/// 声明根解析事实（A4）：<c>ps1-runtime.json</c> 的 <c>projectRoot</c> + 某个根数组键（如 <c>contractDirs</c>）
/// 解析为绝对路径的唯一实现。基根 = projectRoot（相对时相对仓库根；缺省时用调用方给的默认基根），
/// 根数组逐项相对基根解析、只保留真实存在的目录；已解析为空时回落 <c>fallbackRoots</c>。
/// <para>纯路径事实，不含业务语义（键名与回退根由调用方传入）。</para>
/// </summary>
public sealed record DeclaredRootsFacts(
    string? ProjectRoot,
    string BaseRoot,
    IReadOnlyList<string> RawRoots,
    IReadOnlyList<string> ResolvedRoots,
    IReadOnlyList<string> MissingRoots)
{
    /// <summary>实际命中的配置文件（未命中 ⇒ null）。</summary>
    public string? ConfigSource { get; init; }

    /// <summary>配置回退原因（null = 完整命中）。</summary>
    public string? FallbackReason { get; init; }
}

/// <summary>声明根解析（A4 单一实现）：PS 门禁与 PsRuntime 共用本类，禁各自再写 projectRoot/contractDirs 推导。</summary>
public static class PowerShellHostRoots
{
    /// <summary>从配置文件解析（<paramref name="rootsKey"/> 为根数组键名）。</summary>
    /// <param name="repoRoot">仓库根（projectRoot 相对基准）。</param>
    /// <param name="configPath">配置文件路径（可为 null）。</param>
    /// <param name="rootsKey">根数组键名（如 <c>contractDirs</c>）。</param>
    /// <param name="defaultBaseRoot">projectRoot 缺省时的基根（绝对；通常为 Frame 根）。</param>
    /// <param name="fallbackRoots">已解析为空时的回落根（可含相对 repoRoot 的路径）。</param>
    public static DeclaredRootsFacts ResolveFromConfig(
        string repoRoot,
        string? configPath,
        string rootsKey,
        string? defaultBaseRoot,
        IReadOnlyList<string>? fallbackRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootsKey);

        var options = PowerShellHostOptions.Load(configPath);
        var facts = Resolve(repoRoot, options.ProjectRoot, defaultBaseRoot, ReadStringArray(configPath, rootsKey), fallbackRoots);
        return facts with { ConfigSource = options.ConfigSource, FallbackReason = options.FallbackReason };
    }

    /// <summary>从已解析的字段值解析（PsRuntime 运行期使用；避免二次 IO）。</summary>
    public static DeclaredRootsFacts Resolve(
        string repoRoot,
        string? projectRoot,
        string? defaultBaseRoot,
        IReadOnlyList<string> rawRoots,
        IReadOnlyList<string>? fallbackRoots = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var baseRoot = string.IsNullOrWhiteSpace(projectRoot)
            ? (string.IsNullOrWhiteSpace(defaultBaseRoot) ? repoRoot : defaultBaseRoot!)
            : (Path.IsPathRooted(projectRoot) ? projectRoot : Path.Combine(repoRoot, projectRoot));

        var resolved = new List<string>();
        var missing = new List<string>();
        foreach (var raw in rawRoots)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var path = Path.IsPathRooted(raw) ? raw : Path.Combine(baseRoot, raw);
            if (Directory.Exists(path)) resolved.Add(path);
            else missing.Add(path);
        }

        if (resolved.Count == 0 && fallbackRoots is { Count: > 0 })
        {
            foreach (var raw in fallbackRoots)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var path = Path.IsPathRooted(raw) ? raw : Path.Combine(repoRoot, raw);
                if (Directory.Exists(path) && !resolved.Contains(path)) resolved.Add(path);
            }
        }

        return new DeclaredRootsFacts(projectRoot, baseRoot, rawRoots, resolved, missing);
    }

    private static IReadOnlyList<string> ReadStringArray(string? configPath, string name)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) return list;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text)) list.Add(text!);
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return list;
    }
}
