using System;

namespace Share.RoslynJson.Configuration;

/// <summary>
/// 生成器输入判定（路径级，无领域语义）：配置文件与源文件扩展名的匹配约定。
/// </summary>
public static class GeneratorInputMatcher
{
    /// <summary>路径是否以指定扩展名结尾（Ordinal 忽略大小写）。</summary>
    public static bool HasExtension(string? path, string extension)
        => !string.IsNullOrEmpty(path) && path!.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>路径是否为指定文件名（Ordinal 忽略大小写）。</summary>
    public static bool IsFile(string? path, string fileName)
        => !string.IsNullOrEmpty(path) && path!.EndsWith(fileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>路径是否为生成器输入（源扩展名或配置文件名）。</summary>
    public static bool IsGeneratorInput(string? path, string configFileName, string sourceExtension)
        => HasExtension(path, sourceExtension) || IsFile(path, configFileName);
}
