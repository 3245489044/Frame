using System.Threading;
using System.Threading.Tasks;
using PluginHostContract;

namespace PluginHost;

internal sealed class PluginSlot
{
    public PluginSlot(
        PluginManifest manifest,
        string pluginRoot,
        string generationDir,
        string generationName,
        int generation,
        PluginLoadContext loadContext,
        IPlugin plugin,
        PluginContext context,
        PluginStatus status)
    {
        Manifest = manifest;
        PluginRoot = pluginRoot;
        GenerationDir = generationDir;
        GenerationName = generationName;
        Generation = generation;
        LoadContext = loadContext;
        Plugin = plugin;
        Context = context;
        Status = status;
    }

    public PluginManifest Manifest { get; }
    public string PluginRoot { get; }
    public string GenerationDir { get; }
    public string GenerationName { get; }
    public int Generation { get; }
    public PluginLoadContext LoadContext { get; }
    public IPlugin Plugin { get; }
    public PluginContext Context { get; }
    public PluginStatus Status { get; }

    public async Task StopAsync()
    {
        try
        {
            await Plugin.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 停机失败不阻断换代；旧 ALC 仍尝试卸载
        }

        PluginFunctionRouter.Unregister(Manifest.Id);
        try { LoadContext.Unload(); } catch { }
    }
}
