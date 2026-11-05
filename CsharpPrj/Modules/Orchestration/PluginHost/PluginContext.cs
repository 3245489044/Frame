using System;
using System.Collections.Generic;
using PluginHostContract;

namespace PluginHost;

internal sealed class PluginContext : IPluginContext
{
    public PluginContext(
        string pluginId,
        int generation,
        string runtimeRoot,
        string pluginDataDir,
        Action<string> logger,
        PluginFunctionTable functions,
        IServiceProvider hostServices)
    {
        PluginId = pluginId;
        Generation = generation;
        RuntimeRoot = runtimeRoot;
        PluginDataDir = pluginDataDir;
        Logger = logger;
        Functions = functions;
        HostServices = hostServices;
    }

    public string PluginId { get; }
    public int Generation { get; }
    public string RuntimeRoot { get; }
    public string PluginDataDir { get; }
    public Action<string> Logger { get; }
    public PluginFunctionTable Functions { get; }
    public IDictionary<string, string> State { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IServiceProvider HostServices { get; }
}

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public static readonly EmptyServiceProvider Instance = new();
    public object? GetService(Type serviceType) => null;
}
