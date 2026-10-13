using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// 当前生效代指针文件名（生成物；内容 = 代目录名 <c>gen-&lt;n&gt;</c>）——
    /// 「生效代」的**唯一权威**（消除「最高代目录」启发式：多代并存时最大 n 未必是生效代）。
    /// </summary>
    public const string CurrentPointerFileName = "current";

    /// <summary>当前生效代指针路径：<c>&lt;genRoot&gt;\current</c>。</summary>
    public static string CurrentPointerPath(string genRoot)
    {
        if (string.IsNullOrWhiteSpace(genRoot))
            throw new ArgumentException("genRoot 不可为空。", nameof(genRoot));

        return Path.Combine(genRoot, CurrentPointerFileName);
    }

    /// <summary>
    /// 写入「当前生效代」指针（构建成功后由 <c>Build-AgentGeneration.ps1</c> 调用；内容 = 代目录名）。
    /// 幂等覆盖；写失败返回 false（不抛）。装载/换代只认本指针，构建只写新代目录 + 原子切指针。
    /// </summary>
    public static bool TryWriteCurrentPointer(string genRoot, int generation)
    {
        if (string.IsNullOrWhiteSpace(genRoot) || generation < 1)
            return false;

        try
        {
            Directory.CreateDirectory(genRoot);
            File.WriteAllText(
                CurrentPointerPath(genRoot),
                GenerationDirPrefix + generation.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 读取「当前生效代」指针。缺指针 / 内容非法 / 指向的代目录或其基座清单缺件 ⇒ false
    /// （调用方回落「最高代」枚举，保证首启兼容）。
    /// </summary>
    public static bool TryReadCurrentGeneration(string genRoot, out int generation, out string manifestPath)
    {
        generation = 0;
        manifestPath = string.Empty;
        if (string.IsNullOrWhiteSpace(genRoot))
            return false;

        try
        {
            var pointerPath = CurrentPointerPath(genRoot);
            if (!File.Exists(pointerPath))
                return false;

            var name = File.ReadAllText(pointerPath).Trim();
            if (name.Length <= GenerationDirPrefix.Length
                || !name.StartsWith(GenerationDirPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var digits = name.Substring(GenerationDirPrefix.Length);
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
                return false;

            var candidate = GenerationManifestPath(genRoot, n);
            if (!File.Exists(candidate))
                return false;

            generation = n;
            manifestPath = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

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
    /// 枚举代号（升序；只认 <c>gen-&lt;n&gt;</c>，n ≥ 1）—— 代目录坐标的**唯一定性来源**（装载面解析 / 保留策略共用）。
    /// 目录缺失或无代 ⇒ 空表。
    /// </summary>
    public static IReadOnlyList<int> EnumerateGenerations(string genRoot)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(genRoot) || !Directory.Exists(genRoot))
            return result;

        foreach (var dir in Directory.GetDirectories(genRoot))
        {
            var name = Path.GetFileName(dir);
            if (name.Length <= GenerationDirPrefix.Length
                || !name.StartsWith(GenerationDirPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var digits = name.Substring(GenerationDirPrefix.Length);
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
                continue;

            result.Add(n);
        }

        result.Sort();
        return result;
    }

    /// <summary>
    /// 解析**生效代**目录及其 Agent 基座清单（装载面 = 代目录；VM-R12/D2 零复制装载）。
    /// <b>生效代单源（V1）</b>：优先读显式 <c>current</c> 指针；缺指针才回落「最高代」枚举（首启兼容）。
    /// 目录名须匹配 <c>gen-&lt;n&gt;</c>（n ≥ 1）且含 <see cref="AgentManifestFileName"/>；无代 / 缺件 ⇒ false。
    /// 调用方须只读此处坐标，禁自写裸目录扫描。
    /// </summary>
    public static bool TryResolveLatestGenerationManifest(string genRoot, out int generation, out string manifestPath)
    {
        // 生效代单源：显式指针优先（多代并存时最大 n ≠ 生效代）。
        if (TryReadCurrentGeneration(genRoot, out generation, out manifestPath))
            return true;

        generation = 0;
        manifestPath = string.Empty;

        var generations = EnumerateGenerations(genRoot);
        if (generations.Count == 0)
            return false;

        var latest = generations[generations.Count - 1];
        var candidate = GenerationManifestPath(genRoot, latest);
        if (!File.Exists(candidate))
            return false;

        generation = latest;
        manifestPath = candidate;
        return true;
    }

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
