using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PluginHostContract;

/// <summary>
/// 插件级函数表（name → delegate）。换代只原子替换本表，旧代委托随旧 ALC 弃引用；
/// 全局 FunctionRegistry 只保留稳定路由器，不再按 <c>FullName</c> 排斥新代同名库。
/// </summary>
public sealed class PluginFunctionTable
{
    private readonly ConcurrentDictionary<string, Delegate> _functions =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => _functions.Count;

    public void Set(string name, Delegate handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _functions[name] = handler;
    }

    public bool Remove(string name) => _functions.TryRemove(name, out _);

    public bool TryGet(string name, out Delegate? handler)
        => _functions.TryGetValue(name, out handler);

    public IReadOnlyDictionary<string, Delegate> Snapshot()
        => new Dictionary<string, Delegate>(_functions, StringComparer.OrdinalIgnoreCase);

    public void ReplaceAll(IReadOnlyDictionary<string, Delegate> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        _functions.Clear();
        foreach (var pair in replacements)
            _functions[pair.Key] = pair.Value;
    }
}

/// <summary>稳定路由器：全局注册 <c>PluginId::FunctionName</c>，换代只换 table。</summary>
public static class PluginFunctionRouter
{
    private static readonly ConcurrentDictionary<string, PluginFunctionTable> Tables =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string pluginId, PluginFunctionTable table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(table);
        Tables[pluginId] = table;
    }

    public static bool Unregister(string pluginId) => Tables.TryRemove(pluginId, out _);

    public static Delegate? Resolve(string pluginId, string functionName)
        => Tables.TryGetValue(pluginId, out var table)
           && table.TryGet(functionName, out var handler)
            ? handler
            : null;

    public static PluginFunctionTable? GetTable(string pluginId)
        => Tables.TryGetValue(pluginId, out var table) ? table : null;
}
