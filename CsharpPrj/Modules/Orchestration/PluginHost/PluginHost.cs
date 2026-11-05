using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PluginHostContract;
using PathManager;

namespace PluginHost;

/// <summary>
/// 通用插件宿主（Agent 单插件试点）。目录/指针契约见 Docs/design/Runtime/Plugin-Manifest.md：
/// <c>Plugins/&lt;id&gt;/{plugin.json,current,previous,state.json,Generations/gen-n}</c>。
/// </summary>
public sealed partial class PluginHost : IPluginHost, IDisposable
{
    private readonly string _runtimeRoot;
    private readonly string _pluginsRoot;
    private readonly Action<string> _log;
    private readonly IServiceProvider _hostServices;
    private readonly Dictionary<string, PluginSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public PluginHost(string? runtimeRoot = null, Action<string>? log = null, IServiceProvider? hostServices = null)
    {
        _runtimeRoot = runtimeRoot
            ?? RuntimeLayout.TryGetPublishedRoot()
            ?? throw new InvalidOperationException("PluginHost 需要 Runtime 根；Dev 模式请显式传入 runtimeRoot。");
        _pluginsRoot = Path.Combine(_runtimeRoot, "Plugins");
        _log = log ?? (message => Console.WriteLine("[PluginHost] " + message));
        // 宿主服务注入（可选）：插件经 IPluginContext.HostServices 取宿主能力（控件解析/ECS sink 等）。
        // 缺省空实现 ⇒ 插件按「无宿主服务」自降级（不把缺失伪装成可用）。
        _hostServices = hostServices ?? EmptyServiceProvider.Instance;
    }

    public IReadOnlyList<PluginStatus> Statuses
    {
        get
        {
            lock (_gate)
                return _slots.Values.Select(slot => slot.Status).ToArray();
        }
    }

    public async Task<PluginStatus> ActivateAsync(string pluginId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        lock (_gate)
        {
            if (_slots.TryGetValue(pluginId, out var existing) && existing.Status.IsActive)
                return existing.Status;
            if (existing is not null)
                _slots.Remove(pluginId);
        }

        var pluginRoot = PluginRoot(pluginId);
        var manifest = PluginManifestReader.TryRead(pluginRoot)
            ?? throw new FileNotFoundException($"插件缺 plugin.json：{pluginRoot}");
        var generationDir = PluginManifestReader.ResolveGenerationDir(pluginRoot)
            ?? throw new DirectoryNotFoundException($"插件缺生成代目录：{pluginRoot}/Generations");

        var slot = await ActivateSlotAsync(pluginRoot, manifest, generationDir, ct).ConfigureAwait(false);
        lock (_gate)
            _slots[pluginId] = slot;

        WriteState(pluginRoot, slot);
        _log($"Activated plugin={pluginId}, generation={slot.GenerationName}, state={slot.Status.State}");
        return slot.Status;
    }

    public async Task DeactivateAsync(string pluginId, CancellationToken ct)
    {
        PluginSlot? slot;
        lock (_gate)
        {
            if (!_slots.TryGetValue(pluginId, out slot))
                return;
            _slots.Remove(pluginId);
        }

        await slot.StopAsync().ConfigureAwait(false);
        WriteState(PluginRoot(pluginId), null);
    }

    public void Dispose()
    {
        List<PluginSlot> slots;
        lock (_gate)
        {
            slots = _slots.Values.ToList();
            _slots.Clear();
        }

        foreach (var slot in slots)
            slot.StopAsync().GetAwaiter().GetResult();
    }

    private string PluginRoot(string pluginId) => Path.Combine(_pluginsRoot, pluginId);
}
