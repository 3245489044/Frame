namespace Modules.Registry;

/// <summary>
/// 部署契约构建器 —— 把多清单声明合并为“一个扩展表面”，按简单名去重后做 resolver 投影。
/// 缺失判定：所有 manifest resolver + 拓扑根 + App 根均失败才记 Missing。
/// </summary>
internal static class DeploymentContractBuilder
{
    public static DeploymentReport Build(
        int generation,
        IReadOnlyList<string> manifests,
        ExtensionTopology topology,
        ExtensionLoadContext? context)
    {
        var declared = new Dictionary<string, DeclaredAsset>(StringComparer.OrdinalIgnoreCase);
        var findings = new List<DeploymentFinding>();
        var occurrences = 0;

        foreach (var manifest in manifests)
        {
            var index = DepsManifestIndex.Read(manifest);
            if (index.Error is not null)
            {
                findings.Add(new DeploymentFinding(
                    DeploymentFindingKind.ManifestParseError,
                    Path.GetFileName(manifest),
                    new[] { index.Error }));
                continue;
            }

            foreach (var asset in index.Assets)
            {
                occurrences++;
                if (!declared.TryGetValue(asset.Name, out var entry))
                {
                    entry = new DeclaredAsset(asset.Name);
                    declared[asset.Name] = entry;
                }

                entry.Manifests.Add(manifest);
                if (!string.IsNullOrWhiteSpace(asset.AssemblyVersion))
                    entry.Versions.Add(asset.AssemblyVersion!);
            }
        }

        var assets = new List<DeploymentAsset>(declared.Count);
        foreach (var entry in declared.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var resolved = false;
            string? resolvedPath = null;
            string? resolvedVia = null;

            if (context is not null)
                resolved = context.TryResolveAsset(entry.Name, out resolvedPath, out resolvedVia);

            if (!resolved && topology.TryProbe(entry.Name, out resolvedPath))
            {
                resolved = true;
                resolvedVia = "topology";
            }

            if (!resolved && TryProbeApp(entry.Name, out var appPath))
            {
                resolved = true;
                resolvedPath = appPath;
                resolvedVia = "app";
            }

            var version = entry.Versions.Count == 1 ? entry.Versions.First() : null;
            assets.Add(new DeploymentAsset(
                entry.Name,
                resolved,
                resolvedPath,
                resolvedVia,
                version,
                entry.Manifests.ToArray()));

            if (!resolved)
            {
                findings.Add(new DeploymentFinding(
                    DeploymentFindingKind.Missing,
                    entry.Name,
                    entry.Manifests.Select(Path.GetFileName).ToArray()));
            }

            if (entry.Versions.Count > 1)
            {
                findings.Add(new DeploymentFinding(
                    DeploymentFindingKind.VersionConflict,
                    entry.Name,
                    entry.Versions.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray()));
            }
        }

        return new DeploymentReport(generation, manifests, occurrences, assets, findings);
    }

    private static bool TryProbeApp(string simpleName, out string? path)
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");
        if (File.Exists(candidate))
        {
            path = candidate;
            return true;
        }

        path = null;
        return false;
    }

    private sealed class DeclaredAsset
    {
        public DeclaredAsset(string name) => Name = name;

        public string Name { get; }

        public List<string> Manifests { get; } = new();

        public HashSet<string> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
