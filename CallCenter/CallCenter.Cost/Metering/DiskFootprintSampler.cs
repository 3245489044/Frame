namespace CallCenter.Cost.Metering;

/// <summary>
/// 白名单目录占用采样（可选，默认关）。目录/深度/文件数均有界，避免全盘枚举。
/// 任一白名单根不可访问 ⇒ 跳过；全部失败 ⇒ null（Unknown）。
/// </summary>
public static class DiskFootprintSampler
{
    public static long? Sample(IReadOnlyList<string>? roots, int maxDepth, int maxFiles)
    {
        if (roots is null || roots.Count == 0 || maxFiles <= 0)
        {
            return null;
        }

        long total = 0;
        var files = 0;
        var sampledAny = false;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = maxDepth > 0,
            MaxRecursionDepth = Math.Max(0, maxDepth),
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            sampledAny = true;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", options))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // 单文件不可读 ⇒ 跳过（不把不可得当 0 之外的结论）
                    }

                    if (++files >= maxFiles)
                    {
                        return total;
                    }
                }
            }
            catch
            {
                // 单根失败 ⇒ 继续其它白名单根
            }
        }

        return sampledAny ? total : null;
    }

    /// <summary>两次采样差值（允许负值：清理）；任一侧 Unknown ⇒ null。</summary>
    public static long? Delta(long? before, long? after)
        => before is null || after is null ? null : after.Value - before.Value;
}
