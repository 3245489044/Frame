using System;
using System.IO;

namespace Share.LogicComposition.Conversion.Generation;

/// <summary>
/// 生成物约定（路径 / 头注释）——多宿主（写盘 Task、重生成守卫）必须逐字一致，故共用纯函数。
/// 确定性纪律：头注释只含**相对源路径**，不含时间戳 / 绝对路径 / 机器名。
/// </summary>
public static class GeneratorArtifactInfo
{
    /// <summary>紧凑源对应生成物路径（同目录同名，扩展名替换）。</summary>
    /// <param name="sourcePath">源路径。</param>
    /// <param name="generatedExtension">生成物扩展名（如 <c>.spec.json</c>）。</param>
    public static string ToOutputPath(string sourcePath, string generatedExtension)
    {
        if (string.IsNullOrEmpty(sourcePath)) throw new ArgumentException("路径不能为空", nameof(sourcePath));
        if (string.IsNullOrEmpty(generatedExtension)) throw new ArgumentException("产物扩展名不能为空", nameof(generatedExtension));

        return Path.ChangeExtension(sourcePath, generatedExtension);
    }

    /// <summary>相对工程目录的路径（分隔符保留原样）；不可相对化（跨盘/异常）时原样返回。</summary>
    /// <param name="projectDirectory">工程目录（如 <c>$(MSBuildProjectDirectory)</c>）。</param>
    /// <param name="path">目标路径（绝对路径）。</param>
    public static string ToProjectRelativePath(string projectDirectory, string path)
    {
        if (string.IsNullOrEmpty(projectDirectory) || string.IsNullOrEmpty(path))
            throw new ArgumentException("工程目录与路径均不能为空");

        var baseDirectory = Path.GetFullPath(projectDirectory).TrimEnd('\\', '/');
        var target = Path.GetFullPath(path);

        var prefix = baseDirectory + Path.DirectorySeparatorChar;
        return target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? target.Substring(prefix.Length)
            : target;
    }

    /// <summary>生成物头注释（生成物侧以 <c>JsonCommentHandling.Skip</c> 或 C# 注释读取）。</summary>
    /// <param name="sourceRelativePath">源相对工程目录的路径（唯一变量，保证跨机确定性）。</param>
    /// <param name="regenerationHint">领域补充提示（重生成命令 / 守卫名；null = 省略）。</param>
    public static string GeneratedFileHeader(string sourceRelativePath, string? regenerationHint = null)
        => "生成物，勿手改：由 " + sourceRelativePath + " 展开。\n"
           + (string.IsNullOrEmpty(regenerationHint) ? "源为唯一真源。" : "源为唯一真源；" + regenerationHint);
}
