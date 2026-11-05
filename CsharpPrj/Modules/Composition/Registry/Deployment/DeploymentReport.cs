using System.Text.Json;
using System.Linq;

namespace Modules.Registry;

/// <summary>部署校验 finding 类别。</summary>
public enum DeploymentFindingKind
{
    Missing,
    VersionConflict,
    ManifestParseError,
    SharedBoundaryCopy
}

/// <summary>单个部署校验 finding（结构事实，不含叙述）。</summary>
public sealed record DeploymentFinding(
    DeploymentFindingKind Kind,
    string Asset,
    IReadOnlyList<string> Detail);

/// <summary>单个资产在“扩展表面”上的解析状态（按简单名聚合，跨清单唯一）。</summary>
public sealed record DeploymentAsset(
    string Name,
    bool Resolved,
    string? ResolvedPath,
    string? ResolvedVia,
    string? AssemblyVersion,
    IReadOnlyList<string> DeclaredByManifests);

/// <summary>
/// 一次扩展代数快照的部署报告 —— <b>唯一</b>的部署事实来源。
/// 解析语义 = <see cref="ExtensionLoadContext"/> 的 resolver 联合解析投影；不另写路径规则。
/// </summary>
public sealed record DeploymentReport(
    int Generation,
    IReadOnlyList<string> ManifestPaths,
    int DeclaredOccurrences,
    IReadOnlyList<DeploymentAsset> Assets,
    IReadOnlyList<DeploymentFinding> Findings)
{
    public int ManifestCount => ManifestPaths.Count;

    public int UniqueAssets => Assets.Count;

    public IReadOnlyList<DeploymentAsset> MissingAssets =>
        Assets.Where(a => !a.Resolved).ToArray();

    public int MissingCount => Assets.Count(a => !a.Resolved);

    public bool HasMissing => Assets.Any(a => !a.Resolved);

    /// <summary>
    /// 清单 → 插件单元（唯一映射；禁各处再写 deps.json 文件名 switch）。
    /// 判据载体 = 清单路径（结构事实），不依赖执行者叙述。
    /// </summary>
    public static string UnitOf(string manifestPath) => ToProductLabel(manifestPath);

    /// <summary>
    /// 某单元的缺失资产（按 <see cref="DeploymentAsset.DeclaredByManifests"/> 归属；跨清单解析成功即不计缺）。
    /// <para>隔离判定的唯一入口：装配门只看自身单元，**不得**用全局 <see cref="MissingCount"/> 推导任何单元状态。</para>
    /// </summary>
    public IReadOnlyList<DeploymentAsset> MissingOfUnit(string unit)
        => MissingAssets
            .Where(a => a.DeclaredByManifests.Any(m =>
                string.Equals(UnitOf(m), unit, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    /// <summary>单元 → 缺失数（隔离判定/可见化）。</summary>
    public IReadOnlyDictionary<string, int> MissingPerUnit
    {
        get
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in MissingAssets)
            {
                foreach (var manifest in asset.DeclaredByManifests)
                {
                    var unit = UnitOf(manifest);
                    map[unit] = map.TryGetValue(unit, out var count) ? count + 1 : 1;
                }
            }

            return map;
        }
    }

    /// <summary>单元状态摘要（唯一渲染口径；无缺失 ⇒ <c>units=all-clear</c>）。</summary>
    public string FormatPerUnit()
    {
        var perUnit = MissingPerUnit;
        return perUnit.Count == 0
            ? "units=all-clear"
            : "units=" + string.Join(", ", perUnit
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    /// <summary>有界摘要（不拼接全量列表）。</summary>
    public string FormatSummary()
        => $"manifestCount={ManifestCount}, declaredAssets={DeclaredOccurrences}, uniqueAssets={UniqueAssets}, missingCount={MissingCount}";

    /// <summary>前 N 条缺失（跨清单去重；同一资产只出现一次）。</summary>
    public string FormatTopMissing(int maxItems = 10)
    {
        var missing = MissingAssets;
        var lines = missing.Take(Math.Max(0, maxItems)).Select(a =>
            $"{a.Name}（清单: {string.Join(", ", a.DeclaredByManifests.Select(Path.GetFileName))}）");
        var text = string.Join(" | ", lines);
        return missing.Count > maxItems ? $"{text} | …共 {missing.Count} 项" : text;
    }

    /// <summary>按缺失声明反推需要重建的产品（去重）。</summary>
    public string BuildRemedy()
    {
        var products = MissingAssets
            .SelectMany(a => a.DeclaredByManifests)
            .Select(ToProductLabel)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return products.Length == 0
            ? "检查扩展清单与部署拓扑（无缺失声明来源）。"
            : $"重建 {string.Join(", ", products)}（对应产品构建入口）后重启宿主；薄运行目录不承载上游闭包属预期，缺失即权威目录未构建/清单未刷新。";
    }

    /// <summary>写完整报告（诊断用；失败返回 null，不阻断启动）。</summary>
    public string? TryWriteReportFile(string? directory = null)
    {
        try
        {
            directory ??= Path.Combine(Path.GetTempPath(), "WorkFlowPrj", "DeploymentReports");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory,
                $"deployment-gen{Generation}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            var payload = new
            {
                schemaVersion = 1,
                generation = Generation,
                manifestCount = ManifestCount,
                declaredAssets = DeclaredOccurrences,
                uniqueAssets = UniqueAssets,
                missingCount = MissingCount,
                manifests = ManifestPaths,
                missing = MissingAssets.Select(a => new
                {
                    name = a.Name,
                    declaredBy = a.DeclaredByManifests
                }),
                findings = Findings.Select(f => new
                {
                    kind = f.Kind.ToString(),
                    asset = f.Asset,
                    detail = f.Detail
                })
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string ToProductLabel(string manifestPath)
    {
        var fileName = Path.GetFileName(manifestPath);
        return fileName switch
        {
            "Agent.Launcher.deps.json" => "Agent",
            "GameEditor.Host.deps.json" => "Engine",
            "Produce.Launcher.deps.json" => "Produce",
            "PrjBridge.deps.json" => "PrjBridge",
            _ => Path.GetFileNameWithoutExtension(fileName)
        };
    }
}
