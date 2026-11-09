namespace CallCenter.LlmGate.Ledger;

/// <summary>LlmGate 数据根/配置路径解析（单一真源；不读 Agent 环境变量）。</summary>
public static class GateDataPaths
{
    public const string DataSubDir = "llm-gate";
    public const string DataRootEnv = "LLM_GATE_DATA_ROOT";
    public const string RepoRootEnv = "WF_REPO_ROOT";

    /// <summary>仓库根（Frame + Projects 同时存在；向上回溯，避免硬编码盘符）。</summary>
    public static string? TryFindRepoRoot()
    {
        var env = Environment.GetEnvironmentVariable(RepoRootEnv);
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return Path.GetFullPath(env);
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Frame"))
                && Directory.Exists(Path.Combine(dir.FullName, "Projects")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>数据根：显式 &gt; 环境变量 &gt; 仓库 Data/Tests &gt; 输出目录回退。</summary>
    public static string ResolveDataRoot(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }

        var env = Environment.GetEnvironmentVariable(DataRootEnv);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return Path.GetFullPath(env);
        }

        var repo = TryFindRepoRoot();
        return repo is null
            ? Path.Combine(AppContext.BaseDirectory, "llm-gate-data")
            : Path.Combine(repo, "Data", "Tests");
    }

    /// <summary>调用账本目录（&lt;dataRoot&gt;/llm-gate）。</summary>
    public static string ResolveCallDirectory(string? explicitRoot = null)
        => Path.Combine(ResolveDataRoot(explicitRoot), DataSubDir);

    /// <summary>默认门配置（Config/llm-gate/gate.json）。</summary>
    public static string DefaultConfigPath()
    {
        var repo = TryFindRepoRoot();
        return repo is null
            ? Path.Combine(AppContext.BaseDirectory, "llm-gate", "gate.json")
            : Path.Combine(repo, "Config", "llm-gate", "gate.json");
    }

    /// <summary>默认价格表（Config/llm-gate/pricing.json）。</summary>
    public static string DefaultPricingPath()
    {
        var repo = TryFindRepoRoot();
        return repo is null
            ? Path.Combine(AppContext.BaseDirectory, "llm-gate", "pricing.json")
            : Path.Combine(repo, "Config", "llm-gate", "pricing.json");
    }
}
