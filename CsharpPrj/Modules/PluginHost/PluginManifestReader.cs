using System;
using System.IO;
using System.Text.Json;
using PluginHostContract;

namespace PluginHost;

internal static class PluginManifestReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public const string ManifestFileName = "plugin.json";

    public static PluginManifest? TryRead(string pluginRoot)
    {
        var path = Path.Combine(pluginRoot, ManifestFileName);
        return File.Exists(path) ? Read(path) : null;
    }

    public static PluginManifest Read(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Options)
            ?? throw new InvalidOperationException($"plugin.json 反序列化为空：{path}");

        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidOperationException($"plugin.json 缺 id：{path}");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            throw new InvalidOperationException($"plugin.json 缺 entryAssembly：{path}");
        if (string.IsNullOrWhiteSpace(manifest.DepsJson))
            throw new InvalidOperationException($"plugin.json 缺 depsJson：{path}");

        return manifest;
    }

    public static string? ResolveGenerationDir(string pluginRoot)
    {
        var currentPointer = Path.Combine(pluginRoot, "current");
        if (File.Exists(currentPointer))
        {
            try
            {
                var name = File.ReadAllText(currentPointer).Trim();
                var candidate = Path.Combine(pluginRoot, "Generations", name);
                if (Directory.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // 指针不可读 ⇒ 回落最高代
            }
        }

        var generations = Path.Combine(pluginRoot, "Generations");
        if (!Directory.Exists(generations))
            return null;

        var best = 0;
        foreach (var dir in Directory.GetDirectories(generations))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith("gen-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.Substring(4), out var n)
                && n > best)
            {
                best = n;
            }
        }

        return best > 0 ? Path.Combine(generations, "gen-" + best) : null;
    }
}
