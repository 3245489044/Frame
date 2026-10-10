namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 文档编解码端口（存储层与文本格式的**单一接缝**）。
/// 默认实现 = <see cref="JsonDocumentCodec"/>（零行为变更）；USharp 实现由上层适配层注入，
/// 使「编译语法 = 数据配置语法」（同一 USharp 承载声明与行载荷）。
/// <para>
/// 存储层只依赖本端口（零 Format 知识）；USharp 文本的解析/规范形归上层适配层（Usharp 侧）。
/// </para>
/// </summary>
public interface IDocumentCodec
{
    /// <summary>格式标识（诊断/落库标签：<c>json</c> / <c>usharp</c>）。</summary>
    string Format { get; }

    /// <summary>对象 → 文档文本。</summary>
    string Serialize<T>(T value);

    /// <summary>文档文本 → 对象（失败返回 <c>null</c>）。</summary>
    T? Deserialize<T>(string text);

    /// <summary>文档文本 → 对象（容错读：失败置 null 并返回 false）。</summary>
    bool TryDeserialize<T>(string text, out T? value);

    /// <summary>
    /// 取顶层成员的标量文本（**影子索引列**派生：真源文档 → 可被 SQL 索引的标量）。
    /// 找不到 / 非标量 ⇒ 返回 <c>null</c>。
    /// </summary>
    string? ExtractScalar(string document, string memberName);
}
