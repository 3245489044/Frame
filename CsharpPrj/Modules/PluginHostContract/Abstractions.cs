using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PluginHostContract;

/// <summary>插件生命周期入口（append-only；实现方为产品，消费方为 PluginHost）。</summary>
public interface IPlugin
{
    string Id { get; }

    Task InitializeAsync(IPluginContext context, CancellationToken ct);

    Task StartAsync(CancellationToken ct);

    Task<PluginHealth> HealthAsync(CancellationToken ct);

    Task StopAsync(CancellationToken ct);
}

/// <summary>插件上下文（宿主注入；插件只依赖本契约）。</summary>
public interface IPluginContext
{
    string PluginId { get; }
    int Generation { get; }
    string RuntimeRoot { get; }
    string PluginDataDir { get; }
    Action<string> Logger { get; }
    PluginFunctionTable Functions { get; }
    IDictionary<string, string> State { get; }
    IServiceProvider HostServices { get; }
}

/// <summary>插件健康结果。</summary>
public sealed record PluginHealth(bool Healthy, string? Message = null)
{
    public static PluginHealth Ok(string? message = null) => new(true, message);
    public static PluginHealth Fail(string message) => new(false, message);
}

/// <summary>插件发现/激活状态（可见化用；宿主只读投影）。</summary>
public sealed record PluginStatus(
    string PluginId,
    string Version,
    string Generation,
    string State,
    string ManifestPath,
    string? EntryAssemblyPath,
    string? LastError)
{
    public bool IsActive => string.Equals(State, "Active", StringComparison.OrdinalIgnoreCase);
}

/// <summary>plugin.json 的内存投影（字段与 Plugin-Manifest.md 对齐）。</summary>
public sealed record PluginManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string EntryAssembly { get; init; } = string.Empty;
    public string? EntryType { get; init; }
    public string DepsJson { get; init; } = string.Empty;
    public string ReloadStrategy { get; init; } = "collectible";
    public string UpdatePolicy { get; init; } = "drain";
    public string? FunctionLibraries { get; init; }
    public List<string> Requires { get; init; } = new();
    public List<string> SharedAssemblies { get; init; } = new();
    public int HealthTimeoutMs { get; init; } = 5000;
    public string? MinHostVersion { get; init; }
    public List<string> Assets { get; init; } = new();
}

/// <summary>更新请求（文件/命令触发；宿主负责 staging → activate → commit）。</summary>
public sealed record PluginUpdateRequest(
    string StagingDirectory,
    string? Generation = null,
    bool Drain = true);

/// <summary>插件宿主（发现 / 激活 / 换代 / 回滚）。</summary>
public interface IPluginHost
{
    IReadOnlyList<PluginStatus> Statuses { get; }

    Task<PluginStatus> ActivateAsync(string pluginId, CancellationToken ct);

    Task<PluginStatus> UpdateAsync(string pluginId, PluginUpdateRequest request, CancellationToken ct);

    Task<PluginStatus> RollbackAsync(string pluginId, CancellationToken ct);

    Task DeactivateAsync(string pluginId, CancellationToken ct);
}

/// <summary>端口/U I 重绑（宿主在 Committing 阶段调用；插件侧实现注册）。</summary>
public interface IPluginPortBinder
{
    Task RegisterAsync(string pluginId, int generation, IPluginContext context, CancellationToken ct);
    Task UnregisterAsync(string pluginId, int generation, CancellationToken ct);
}
