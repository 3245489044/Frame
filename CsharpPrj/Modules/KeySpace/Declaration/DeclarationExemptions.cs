using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Modules.Declaration;

/// <summary>
/// 声明闭合孤儿豁免册（F3）：门禁（PS）与运行期（C#）**共读同一份** JSON（单源，禁两份清单）。
/// <para>载体 = <see cref="RelativePath"/>；定位顺序 = 环境变量 <c>WF_DECLARATION_EXEMPTIONS</c>（显式文件）
/// → <c>WF_REPO_ROOT</c>（仓库根）→ 自 <see cref="AppContext.BaseDirectory"/> 向上最多 8 层。</para>
/// <para>缺失 / 解析失败 ⇒ 空集（不伪造豁免；Throw 姿态下即按孤儿处理，fail-closed）。</para>
/// </summary>
public static class DeclarationExemptions
{
    /// <summary>豁免册载体（仓库相对；单源）。</summary>
    public const string RelativePath = "Frame/PowerShellScript/Baselines/Declarations/declaration-exemptions.json";

    private static readonly Lazy<IReadOnlySet<string>> Cached = new(LoadCore, isThreadSafe: true);

    /// <summary>全部豁免 implId（只读）。</summary>
    public static IReadOnlySet<string> All => Cached.Value;

    /// <summary>该实现是否被豁免。</summary>
    public static bool Contains(string? implId)
        => !string.IsNullOrWhiteSpace(implId) && All.Contains(implId!.Trim());

    private static IReadOnlySet<string> LoadCore()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var path = ResolvePath();
        if (path is null) return result;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text!.Trim());
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("implId", out var implId)
                    && implId.ValueKind == JsonValueKind.String)
                {
                    var text = implId.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text!.Trim());
                }
            }
        }
        catch (JsonException) { /* 非法册：按空集（可见化由门禁 DC5 承担，不在运行期静默造假） */ }
        catch (IOException) { }

        return result;
    }

    private static string? ResolvePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("WF_DECLARATION_EXEMPTIONS");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath)) return explicitPath;

        var relative = RelativePath.Replace('/', Path.DirectorySeparatorChar);

        var repoRoot = Environment.GetEnvironmentVariable("WF_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            var candidate = Path.Combine(repoRoot!, relative);
            if (File.Exists(candidate)) return candidate;
        }

        var dir = AppContext.BaseDirectory;
        for (var depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(dir); depth++)
        {
            var candidate = Path.Combine(dir!, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}