using System;
using System.Collections.Generic;
using System.IO;

namespace Modules.Registry;

/// <summary>
/// 代目录布局（VM-R2b）—— 「构建到新目录（gen-&lt;n&gt;）→ 换 ALC」的**名与路径规则唯一来源**。
/// <para>
/// Agent 侧（<c>AgentGenerationCoordinate</c>）与宿主侧重装配桥（<c>LauncherBootstrap</c> / 重装配桥）共用本类，
/// 消除「宿主侧重写一套 gen-&lt;n&gt; 命名」的平行实现。纯函数、无 ALC/状态耦合。
/// </para>
/// <para>
/// 权威：Docs\analysis\2026\10\10.9\2026-10-09-Vm框架-换代路线收敛-剩余步骤施工单.md §4.1；
/// 构建落点见 Projects\BuildFramework\Build-AgentGeneration.ps1。
/// </para>
/// </summary>
public static class ExtensionGenerationLayout
{
    /// <summary>Agent 扩展基座清单文件名（被替换项）。</summary>
    public const string AgentManifestFileName = "Agent.Launcher.deps.json";

    /// <summary>program 清单文件名（与 Agent.Vm.Program 产物同名）。</summary>
    public const string ProgramManifestFileName = "vm-program.json";

    /// <summary>代目录名前缀（<c>gen-&lt;n&gt;</c>）。</summary>
    public const string GenerationDirPrefix = "gen-";

    /// <summary>代根目录名（与 Build-AgentGeneration.ps1 默认 GenRoot 一致：<c>BuildOutput\Generations</c>）。</summary>
    public const string GenerationsDirName = "Generations";

    /// <summary>代目录路径：<c>&lt;genRoot&gt;\gen-&lt;n&gt;</c>。</summary>
    public static string GenerationDir(string genRoot, int generation)
    {
        if (string.IsNullOrWhiteSpace(genRoot))
            throw new ArgumentException("genRoot 不可为空。", nameof(genRoot));

        return Path.Combine(genRoot, GenerationDirPrefix + generation);
    }

    /// <summary>代内 Agent 基座清单路径：<c>&lt;genRoot&gt;\gen-&lt;n&gt;\Agent.Launcher.deps.json</c>。</summary>
    public static string GenerationManifestPath(string genRoot, int generation)
        => Path.Combine(GenerationDir(genRoot, generation), AgentManifestFileName);

    /// <summary>代内 program 清单路径：<c>&lt;genRoot&gt;\gen-&lt;n&gt;\vm-program.json</c>。</summary>
    public static string ProgramManifestPath(string genRoot, int generation)
        => Path.Combine(GenerationDir(genRoot, generation), ProgramManifestFileName);

    /// <summary>
    /// 组装按代清单：把基清单中的 Agent 基座替换为 <c>gen-&lt;n&gt;</c> 内的清单（其余沿用）。
    /// 缺件（gen 内清单不存在 / 基清单无 Agent 基座项 / 基清单为空）⇒ 返回 false（调用方回落既有清单）。
    /// </summary>
    public static bool TryBuildGenerationManifests(
        IReadOnlyList<string>? baseManifests, int generation, string genRoot, out string[] manifests)
    {
        manifests = Array.Empty<string>();
        if (baseManifests is null || baseManifests.Count == 0 || string.IsNullOrWhiteSpace(genRoot))
            return false;

        var generationManifest = GenerationManifestPath(genRoot, generation);
        if (!File.Exists(generationManifest))
            return false;

        var replaced = false;
        var result = new List<string>(baseManifests.Count);
        foreach (var manifest in baseManifests)
        {
            if (!replaced
                && string.Equals(Path.GetFileName(manifest), AgentManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(generationManifest);
                replaced = true;
            }
            else
            {
                result.Add(manifest);
            }
        }

        if (!replaced)
            return false;

        manifests = result.ToArray();
        return true;
    }
}
