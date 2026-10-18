using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PluginHostContract;

namespace PluginHost;

public sealed partial class PluginHost
{
    private async Task<PluginSlot> ActivateSlotAsync(
        string pluginRoot,
        PluginManifest manifest,
        string generationDir,
        CancellationToken ct)
    {
        var entryPath = Path.Combine(generationDir, manifest.EntryAssembly);
        var depsPath = Path.Combine(generationDir, manifest.DepsJson);
        if (!File.Exists(entryPath))
            throw new FileNotFoundException($"插件入口缺失：{entryPath}");
        if (!File.Exists(depsPath))
            throw new FileNotFoundException($"插件 depsJson 缺失：{depsPath}");

        var generationName = Path.GetFileName(generationDir);
        var generation = ParseGeneration(generationName);
        var loadContext = new PluginLoadContext(depsPath, $"Plugin:{manifest.Id}:{generationName}");

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(entryPath);
            var entryType = string.IsNullOrWhiteSpace(manifest.EntryType)
                ? assembly.GetTypes().FirstOrDefault(t =>
                    typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                : assembly.GetType(manifest.EntryType, throwOnError: false);

            if (entryType is null || !typeof(IPlugin).IsAssignableFrom(entryType))
                throw new InvalidOperationException(
                    $"plugin.json entryType 未实现 IPlugin：{manifest.EntryType ?? "(empty)"} in {entryPath}");

            if (Activator.CreateInstance(entryType) is not IPlugin plugin)
                throw new InvalidOperationException($"插件入口无法实例化：{entryType.FullName}");

            var dataDir = Path.Combine(_runtimeRoot, "Data", "Plugins", manifest.Id);
            Directory.CreateDirectory(dataDir);

            var context = new PluginContext(
                manifest.Id,
                generation,
                _runtimeRoot,
                dataDir,
                message => _log($"plugin={manifest.Id}: {message}"),
                new PluginFunctionTable(),
                EmptyServiceProvider.Instance);

            // 域函数库 → 插件级 table（只认本代目录清单；不触碰全局 FunctionRegistry）。
            if (!string.IsNullOrWhiteSpace(manifest.FunctionLibraries))
            {
                var libraryManifest = Path.Combine(generationDir, manifest.FunctionLibraries);
                if (File.Exists(libraryManifest))
                {
                    Modules.Registry.FunctionLibraryLoader.LoadInto(
                        context.Functions,
                        libraryManifest,
                        typeStr => ResolveType(loadContext, typeStr));
                }
                else
                {
                    _log($"plugin={manifest.Id}: functionLibraries 缺失 {libraryManifest}");
                }
            }

            await plugin.InitializeAsync(context, ct).ConfigureAwait(false);
            await plugin.StartAsync(ct).ConfigureAwait(false);

            var health = await HealthWithTimeoutAsync(plugin, manifest.HealthTimeoutMs, ct).ConfigureAwait(false);
            if (!health.Healthy)
                throw new InvalidOperationException($"插件健康检查失败：{health.Message}");

            PluginFunctionRouter.Register(manifest.Id, context.Functions);

            var status = new PluginStatus(
                manifest.Id,
                manifest.Version,
                generationName,
                "Active",
                Path.Combine(pluginRoot, PluginManifestReader.ManifestFileName),
                entryPath,
                null);

            return new PluginSlot(
                manifest, pluginRoot, generationDir, generationName, generation,
                loadContext, plugin, context, status);
        }
        catch
        {
            try { loadContext.Unload(); } catch { }
            throw;
        }
    }

    private static async Task<PluginHealth> HealthWithTimeoutAsync(
        IPlugin plugin, int timeoutMs, CancellationToken ct)
    {
        var effectiveTimeout = timeoutMs > 0 ? timeoutMs : 5000;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(effectiveTimeout);

        try
        {
            return await plugin.HealthAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return PluginHealth.Fail($"健康检查超时（{effectiveTimeout}ms）");
        }
    }

    private static Type? ResolveType(PluginLoadContext loadContext, string qualifiedName)
    {
        var comma = qualifiedName.IndexOf(',');
        if (comma < 0)
            return null;

        var typeName = qualifiedName.Substring(0, comma).Trim();
        var assemblyName = qualifiedName.Substring(comma + 1).Trim();
        var nextComma = assemblyName.IndexOf(',');
        if (nextComma >= 0)
            assemblyName = assemblyName.Substring(0, nextComma).Trim();

        try
        {
            var assembly = loadContext.LoadFromAssemblyName(new System.Reflection.AssemblyName(assemblyName));
            return assembly.GetType(typeName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    private static int ParseGeneration(string name)
        => name.StartsWith("gen-", StringComparison.OrdinalIgnoreCase)
           && int.TryParse(name.Substring(4), out var n)
            ? n
            : 1;
}
