using System.Text.Json;

namespace Modules.Registry;

/// <summary>
/// deps.json 索引器 —— 只读 <c>runtimeTarget</c> 指向的生效 target，资产键归一到程序集简单名。
/// <para>deps.json 是“加载图”，不是“磁盘布局契约”；路径语义由 <see cref="ExtensionLoadContext"/> 负责。</para>
/// </summary>
internal static class DepsManifestIndex
{
    internal sealed record Asset(string Name, string? AssemblyVersion);

    internal sealed record ManifestAssets(
        string ManifestPath,
        IReadOnlyList<Asset> Assets,
        string? Error);

    public static ManifestAssets Read(string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
                return new ManifestAssets(manifestPath, Array.Empty<Asset>(), null);

            var targetName = root.TryGetProperty("runtimeTarget", out var runtimeTarget)
                && runtimeTarget.ValueKind == JsonValueKind.Object
                && runtimeTarget.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            JsonElement target;
            if (!string.IsNullOrEmpty(targetName) && targets.TryGetProperty(targetName, out target))
            {
                // 生效 target
            }
            else
            {
                target = targets.EnumerateObject().FirstOrDefault().Value;
            }

            if (target.ValueKind != JsonValueKind.Object)
                return new ManifestAssets(manifestPath, Array.Empty<Asset>(), null);

            var assets = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
            foreach (var lib in target.EnumerateObject())
            {
                if (lib.Value.ValueKind != JsonValueKind.Object
                    || !lib.Value.TryGetProperty("runtime", out var runtime)
                    || runtime.ValueKind != JsonValueKind.Object)
                    continue;

                var version = ReadAssemblyVersion(lib.Value);
                foreach (var runtimeAsset in runtime.EnumerateObject())
                {
                    var rel = runtimeAsset.Name;
                    if (!rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var simpleName = Path.GetFileNameWithoutExtension(rel);
                    if (string.IsNullOrWhiteSpace(simpleName))
                        continue;

                    assets[simpleName] = new Asset(simpleName, version);
                }
            }

            return new ManifestAssets(manifestPath, assets.Values.ToArray(), null);
        }
        catch (Exception ex)
        {
            return new ManifestAssets(manifestPath, Array.Empty<Asset>(), ex.Message);
        }
    }

    private static string? ReadAssemblyVersion(JsonElement lib)
    {
        if (lib.TryGetProperty("assemblyVersion", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString();

        if (lib.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object)
        {
            foreach (var asset in runtime.EnumerateObject())
            {
                if (asset.Value.ValueKind == JsonValueKind.Object
                    && asset.Value.TryGetProperty("assemblyVersion", out var version)
                    && version.ValueKind == JsonValueKind.String)
                    return version.GetString();
            }
        }

        return lib.TryGetProperty("version", out var packageVersion) && packageVersion.ValueKind == JsonValueKind.String
            ? packageVersion.GetString()
            : null;
    }
}
