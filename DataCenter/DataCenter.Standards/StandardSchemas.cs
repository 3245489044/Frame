using System.Text;
using BasicMethod.Declarative.Ledger;

namespace DataCenter.Standards;

/// <summary>
/// 结构门文本的**内嵌单一源**：<c>Frame\DataCenter\schemas\*.schema.json</c> 以链接方式内嵌进本程序集，
/// 使装载期结构校验不依赖任何路径/文件系统（库调用方零接线）。
/// </summary>
public static class StandardSchemas
{
    /// <summary>端口台账结构门文本。</summary>
    public static string Ports => Read("ports.schema.json");

    /// <summary>事件台账结构门文本。</summary>
    public static string Events => Read("events.schema.json");

    /// <summary>替身台账结构门文本。</summary>
    public static string Fakes => Read("fakes.schema.json");

    /// <summary>数据片段结构门文本。</summary>
    public static string Data => Read("data.schema.json");

    private static string Read(string fileName)
        => ReadEmbedded($"DataCenter.Standards.Schemas.{fileName}");

    private static string ReadEmbedded(string resourceName)
    {
        using var stream = typeof(StandardSchemas).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new DeclarationViolationException(
                $"内嵌结构门缺失：{resourceName}（构建期嵌入规则见 DataCenter.Standards.csproj 的 EmbeddedSchemas 组）");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
