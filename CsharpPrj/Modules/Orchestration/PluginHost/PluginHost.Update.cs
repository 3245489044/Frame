using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PluginHostContract;

namespace PluginHost;

public sealed partial class PluginHost
{
    public async Task<PluginStatus> UpdateAsync(string pluginId, PluginUpdateRequest request, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(request);

        var pluginRoot = PluginRoot(pluginId);
        var manifest = PluginManifestReader.TryRead(pluginRoot)
            ?? throw new FileNotFoundException($"插件缺 plugin.json：{pluginRoot}");

        var generationName = string.IsNullOrWhiteSpace(request.Generation)
            ? NextGenerationName(pluginRoot)
            : request.Generation!;
        var targetDir = Path.Combine(pluginRoot, "Generations", generationName);

        if (!Path.GetFullPath(request.StagingDirectory).Equals(
                Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
            CopyDirectory(request.StagingDirectory, targetDir);
        }

        if (!File.Exists(Path.Combine(targetDir, PluginManifestReader.ManifestFileName)))
        {
            File.Copy(
                Path.Combine(pluginRoot, PluginManifestReader.ManifestFileName),
                Path.Combine(targetDir, PluginManifestReader.ManifestFileName),
                overwrite: true);
        }

        PluginSlot candidate;
        try
        {
            candidate = await ActivateSlotAsync(pluginRoot, manifest, targetDir, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteState(pluginRoot, null, ex.Message);
            _log($"Update failed plugin={pluginId}, generation={generationName}: {ex.Message}");
            throw;
        }

        PluginSlot? previous;
        lock (_gate)
        {
            _slots.TryGetValue(pluginId, out previous);
            _slots[pluginId] = candidate;
        }

        var previousName = previous?.GenerationName;
        WritePointer(pluginRoot, "current", generationName);
        if (!string.IsNullOrWhiteSpace(previousName))
            WritePointer(pluginRoot, "previous", previousName);

        if (previous is not null)
            await previous.StopAsync().ConfigureAwait(false);

        WriteState(pluginRoot, candidate);
        _log($"Updated plugin={pluginId}, generation={generationName}");
        return candidate.Status;
    }

    public async Task<PluginStatus> RollbackAsync(string pluginId, CancellationToken ct)
    {
        var pluginRoot = PluginRoot(pluginId);
        var previousPointer = Path.Combine(pluginRoot, "previous");
        if (!File.Exists(previousPointer))
            throw new InvalidOperationException($"插件无 previous 可回滚：{pluginId}");

        var previousName = File.ReadAllText(previousPointer).Trim();
        var previousDir = Path.Combine(pluginRoot, "Generations", previousName);
        if (!Directory.Exists(previousDir))
            throw new DirectoryNotFoundException($"previous 代目录不存在：{previousDir}");

        var manifest = PluginManifestReader.TryRead(pluginRoot)
            ?? throw new FileNotFoundException($"插件缺 plugin.json：{pluginRoot}");

        var candidate = await ActivateSlotAsync(pluginRoot, manifest, previousDir, ct).ConfigureAwait(false);

        PluginSlot? current;
        lock (_gate)
        {
            _slots.TryGetValue(pluginId, out current);
            _slots[pluginId] = candidate;
        }

        WritePointer(pluginRoot, "current", previousName);
        if (current is not null)
        {
            WritePointer(pluginRoot, "previous", current.GenerationName);
            await current.StopAsync().ConfigureAwait(false);
        }

        WriteState(pluginRoot, candidate);
        return candidate.Status;
    }
}
