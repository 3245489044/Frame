using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;

namespace PluginHost;

/// <summary>
/// 每插件一个 collectible ALC。共享面（契约/Frame）一律复用默认 ALC 已加载副本，
/// 仅插件私有闭包才从代目录自装（唯一物理副本纪律）。
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string depsJsonPath, string name)
        : base(name, isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(depsJsonPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null)
            return null;

        // 默认 ALC 已加载同名程序集 ⇒ 复用（防止契约/共享面类型身份分裂）。
        foreach (var loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                return loaded;
        }

        // 默认 ALC 可解析 ⇒ 复用（Frame 共享面单一来源）。
        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException) { }
        catch (FileLoadException) { }
        catch (BadImageFormatException) { }
        catch (IOException) { }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    public IReadOnlyList<Assembly> LoadAll(string assemblyPath)
        => new[] { LoadFromAssemblyPath(assemblyPath) };
}
