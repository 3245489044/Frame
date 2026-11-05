namespace Modules.Registry.AutoRegister;

/// <summary>
/// 配置驱动的"权威产物目录"定位器 —— 从 register-config 所在目录（configDir）<b>逐级上溯</b>，
/// 寻找含 <c>&lt;Sub&gt;\&lt;Config&gt;\&lt;assemblySpec&gt;</c> 的源码根，命中即返回该产物目录。
/// 语义：AutoRegister 直接 <c>LoadFrom</c> 源码树编译产出的<b>原二进制</b>，而<em>不是</em>要求运行目录
/// 持有该 dll（也不用把它拷贝进运行目录）。上溯到 CsharpProjectRoot 时
/// （如 <c>Frame\CsharpPrj</c>）即可命中 <c>Frame\CsharpPrj\BuildOutput\Debug\Mathematics.dll</c>。
/// 纯 IO、无状态，确定性可单测。
/// </summary>
public static class OutputArtifactLocator
{
    /// <summary>
    /// 从 <paramref name="startDirectory"/>（通常为 register-config 所在目录）上溯，返回首个
    /// <c>&lt;根&gt;\&lt;outputSubDir&gt;\&lt;configName&gt;\&lt;assemblySpec&gt;</c> 文件存在的
    /// 产物目录（<c>&lt;根&gt;\&lt;outputSubDir&gt;\&lt;configName&gt;</c>）；全部未命中返回 null。
    /// <paramref name="assemblySpec"/> 建议为相对文件名（如 <c>Mathematics.dll</c>）。
    /// </summary>
    public static string? LocateProductDirectory(
        string? startDirectory,
        string? outputSubDir,
        string? configName,
        string? assemblySpec)
    {
        if (string.IsNullOrWhiteSpace(startDirectory)
            || string.IsNullOrWhiteSpace(assemblySpec)
            || Path.IsPathRooted(assemblySpec))
        {
            return null;
        }

        var sub = string.IsNullOrWhiteSpace(outputSubDir) ? "BuildOutput" : outputSubDir;
        var config = string.IsNullOrWhiteSpace(configName) ? "Debug" : configName;

        var current = TryGetFullPath(startDirectory);
        while (!string.IsNullOrEmpty(current))
        {
            var productDir = TryCombine(current, sub, config);
            if (productDir != null)
            {
                var file = TryCombine(productDir, assemblySpec);
                if (file != null && File.Exists(file))
                    return productDir;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent == null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }

        return null;
    }

    /// <summary>若 <paramref name="productDirectory"/> 非空，返回其中 <paramref name="assemblySpec"/> 的绝对路径。</summary>
    public static string? ResolveAssemblyPath(string? productDirectory, string? assemblySpec)
        => productDirectory != null && !string.IsNullOrWhiteSpace(assemblySpec)
            ? TryCombine(productDirectory, assemblySpec)
            : null;

    private static string? TryCombine(string baseDir, params string[] parts)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(new[] { baseDir }.Concat(parts).ToArray()));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }
}
