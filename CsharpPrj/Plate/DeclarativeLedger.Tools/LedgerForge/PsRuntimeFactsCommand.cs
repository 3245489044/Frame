using System.Text.Json;
using Plate.PowerShellHost;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// PS 运行时契约根事实导出（A4）：唯一解析实现 = <see cref="PowerShellHostRoots"/>（与 Agent PsRuntime 运行期共用）。
/// <para>
/// 输入：仓库根 / Agent 根 / Frame 根。读 <c>&lt;agent&gt;\Config\ps1-runtime.json</c> 的
/// <c>projectRoot</c> + <c>contractDirs</c>，输出基根、原始根、已解析根、缺失根。
/// </para>
/// <para>退出码：0 已产出；2 输入缺失或配置不可读。</para>
/// </summary>
internal static class PsRuntimeFactsCommand
{
    /// <summary>Agent 侧 ps1-runtime 的契约根数组键（schema 常量，与 PsRuntimeHost 同值）。</summary>
    internal const string ContractDirsKey = "contractDirs";

    /// <summary>契约根回退（相对 Frame 根；与 PsRuntimeHost 的回退同值）。</summary>
    internal const string ContractDirsFallback = "PowerShellScript\\BasicMethod";

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static int Run(string? repoRoot, string? agentRoot, string? frameRoot, string? outPath)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(agentRoot))
        {
            Console.Error.WriteLine("[ledger:forge] ps-runtime-facts 需要 --repo <仓库根> 与 --agent <Agent 根>");
            return 2;
        }

        if (!Directory.Exists(agentRoot))
        {
            Console.Error.WriteLine($"[ledger:forge] ps-runtime-facts Agent 根不存在: {agentRoot}");
            return 2;
        }

        var configPath = Path.Combine(agentRoot, "Config", "ps1-runtime.json");
        var fallback = string.IsNullOrWhiteSpace(frameRoot)
            ? null
            : new[] { Path.Combine(frameRoot, ContractDirsFallback) };

        DeclaredRootsFacts facts;
        try
        {
            facts = PowerShellHostRoots.ResolveFromConfig(
                repoRoot, File.Exists(configPath) ? configPath : null, ContractDirsKey, frameRoot, fallback);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ledger:forge] ps-runtime-facts 失败: {ex.Message}");
            return 2;
        }

        var payload = new PsRuntimeFactsPayload(
            facts.ProjectRoot,
            facts.ConfigSource,
            facts.FallbackReason,
            facts.BaseRoot,
            facts.RawRoots,
            facts.ResolvedRoots,
            facts.MissingRoots);

        var json = JsonSerializer.Serialize(payload, OutputOptions);
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Out.WriteLine(json);
        }
        else
        {
            File.WriteAllText(outPath, json);
        }

        return 0;
    }

    private sealed record PsRuntimeFactsPayload(
        string? ProjectRoot,
        string? ConfigSource,
        string? FallbackReason,
        string BaseRoot,
        IReadOnlyList<string> RawRoots,
        IReadOnlyList<string> ResolvedRoots,
        IReadOnlyList<string> MissingRoots);
}
