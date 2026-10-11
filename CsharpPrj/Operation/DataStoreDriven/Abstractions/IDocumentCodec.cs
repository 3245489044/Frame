using System.Collections.Generic;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 文档编解码端口（存储层与文本格式的**单一接缝**）。
/// 由上层适配层注入实现（默认生产 = USharp <c>UsharpDataPorts.Codec</c>；<see cref="JsonDocumentCodec"/>
/// 为可选 JSON 实现，供特定通道/旧数据），使「编译语法 = 数据配置语法」（同一 USharp 承载声明与行载荷）。
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
    // ---- 记录实例端口（N0）：[Doc] = [Record] <recordId>: table=<t> | key=<k> 实例规范文本 ----

    /// <summary>记录实例 → 规范文本（记录头 + 字段体；规范形单源 = 前端 <c>UsharpRecordDecl.ToCanonical</c>）。</summary>
    string SerializeRecord(string recordId, string? table, string? key, UsharpRecord body);

    /// <summary>规范文本 → 记录实例（失败返回 false）。</summary>
    bool TryDeserializeRecord(string text, out string recordId, out string? table, out string? key, out UsharpRecord body);

    /// <summary>
    /// 对象体记录 → 规范文本（体经实现侧映射为记录字段；<paramref name="extraFields"/> 为附加头字段，
    /// 如 ThreadId/Status/时间戳 —— 供过滤/排序下推）。
    /// </summary>
    string SerializeRecord<T>(string recordId, string? table, string? key, T body, IReadOnlyList<KeyValuePair<string, UsharpValue>>? extraFields = null);

    /// <summary>规范文本 → 对象体（失败返回 false；未知字段（头字段）由映射器忽略）。</summary>
    bool TryDeserializeRecord<T>(string text, out T? body);
}
