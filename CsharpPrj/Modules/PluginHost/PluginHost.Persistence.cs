using System;
using System.IO;
using System.Threading.Tasks;
using PluginHostContract;

namespace PluginHost;

public sealed partial class PluginHost
{
    private static string NextGenerationName(string pluginRoot)
    {
        var generations = Path.Combine(pluginRoot, "Generations");
        var best = 0;
        if (Directory.Exists(generations))
        {
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
        }

        return "gen-" + (best + 1);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void WritePointer(string pluginRoot, string name, string value)
    {
        var target = Path.Combine(pluginRoot, name);
        var temp = target + ".tmp";
        File.WriteAllText(temp, value);
        File.Move(temp, target, overwrite: true);
    }

    private void WriteState(string pluginRoot, PluginSlot? slot, string? error = null)
    {
        try
        {
            Directory.CreateDirectory(pluginRoot);
            var current = slot?.GenerationName
                ?? (File.Exists(Path.Combine(pluginRoot, "current"))
                    ? File.ReadAllText(Path.Combine(pluginRoot, "current")).Trim()
                    : null);
            var previous = File.Exists(Path.Combine(pluginRoot, "previous"))
                ? File.ReadAllText(Path.Combine(pluginRoot, "previous")).Trim()
                : null;

            var state = new
            {
                current,
                previous,
                lastError = error,
                updatedUtc = DateTime.UtcNow.ToString("o"),
            };

            File.WriteAllText(
                Path.Combine(pluginRoot, "state.json"),
                System.Text.Json.JsonSerializer.Serialize(state,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log($"状态写入失败 pluginRoot={pluginRoot}: {ex.Message}");
        }
    }
}
