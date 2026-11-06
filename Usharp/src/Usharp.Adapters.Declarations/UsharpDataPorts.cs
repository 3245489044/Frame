using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Sqlite;
using Plate.DslMethod.Usharp;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// USharp **全域唯一交汇门面**（唯一注入入口 / SingleImplementation）：
/// 把 C# ↔ USharp 的四类交汇语义各收一个入口，并合于此一门面 ——
/// ① 值 ↔ CLR 对象（<see cref="ToValue"/> / <see cref="ToObject{T}"/>，唯一映射实现 = <see cref="UsharpValueMapper"/>）；
/// ② 值 ↔ 文本（<see cref="FromJson(string)"/> / <see cref="ToPayloadText"/> / <see cref="ToJson"/>，唯一 JSON 出口 = <see cref="UsharpValueJson"/>；文档编解码 <see cref="Codec"/>）；
/// ③ 值 ↔ SQL 下推（<see cref="EnsureSqlFunctions"/> / <see cref="SchemaSource"/>）；
/// ④ 声明文本 → 既有 C# 类型（<see cref="DeclarationStoreAsync"/>）。
/// <para>
/// 组合根与消费侧只调本门面，**禁在各装配点内联** <c>new Usharp*</c> 构造字面，
/// **禁**绕过门面直引 <c>Usharp.Adapters.*</c> / <c>Usharp.Semantics.*</c> 类型（消除「改一处漏另一侧」的双路径不一致）。
/// </para>
/// <para>
/// 依赖方向：本类落在唯一桥接层 <c>Usharp.Adapters.Declarations</c>（可同时见 Usharp 与 DataStoreDriven）；
/// 存储层与 <c>Usharp.Semantics</c> 仍不引 USharp 实现，实现一律经组合根注入。
/// </para>
/// </summary>
public static class UsharpDataPorts
{
    /// <summary>表声明文件名（<c>[Table]</c> 真源，位于配置目录 <c>nodes/</c> 下）。</summary>
    public const string TablesDeclarationFileName = "tables.ushar";

    // ===== ① 值 ↔ CLR 对象（唯一映射实现 = UsharpValueMapper）=====

    /// <summary>任意 CLR 对象 → 中立 USharp 值（转调 <see cref="UsharpValueMapper.ToValue"/>）。</summary>
    public static UsharpValue ToValue(object? value) => UsharpValueMapper.ToValue(value);

    /// <summary>中立 USharp 值 → 目标对象（转调 <see cref="UsharpValueMapper.TryToObject{T}"/>；失败 ⇒ default）。</summary>
    public static T? ToObject<T>(UsharpValue value)
        => UsharpValueMapper.TryToObject<T>(value).Match(ok => ok, _ => default);

    /// <summary>中立 USharp 值 → 目标对象（失败可见：原因经 <paramref name="reason"/> 透出）。</summary>
    /// <param name="value">USharp 值。</param>
    /// <param name="reason">失败原因（成功为 null）。</param>
    public static T? ToObject<T>(UsharpValue value, out string? reason)
    {
        var result = UsharpValueMapper.TryToObject<T>(value);
        reason = result.Ok ? null : result.Reason;
        return result.Ok ? result.Value : default;
    }

    // ===== ② 值 ↔ 文本（唯一 JSON 出口 = UsharpValueJson；兼容读出/迁移）=====

    /// <summary>载荷 JSON 文本 → 中立值（兼容读：空 ⇒ <c>Null</c>，非法 ⇒ 原样字符串标量；转调 <see cref="UsharpValueJson.ParseOrString"/>）。</summary>
    public static UsharpValue FromJson(string? json) => UsharpValueJson.ParseOrString(json);

    /// <summary>载荷 JSON 元素 → 中立值（转调 <see cref="UsharpValueJson.ParseOrString"/>）。</summary>
    public static UsharpValue FromJson(JsonElement element) => UsharpValueJson.ParseOrString(element.GetRawText());

    /// <summary>中立值 → 载荷文本视图（字符串标量 ⇒ 原样文本；其余 ⇒ JSON；转调 <see cref="UsharpValueJson.ToPayloadText"/>）。</summary>
    public static string ToPayloadText(UsharpValue? value) => UsharpValueJson.ToPayloadText(value);

    /// <summary>中立值 → JSON 文本（转调 <see cref="UsharpValueJson.ToJson"/>）。</summary>
    public static string ToJson(UsharpValue? value) => UsharpValueJson.ToJson(value);

    /// <summary>文档编解码端口（USharp 实现，无状态单例）。取用时确保 <c>usharp_get</c> 已注册
    /// （唯一注入入口 ⇒ 组合根解析 codec 即完成注册，早于任何 Store 初始化）。</summary>
    public static IDocumentCodec Codec
    {
        get
        {
            EnsureSqlFunctions();
            return UsharpDocumentCodec.Instance;
        }
    }

    /// <summary>
    /// 显式注册 <c>usharp_get</c> SQL 函数（**唯一注册点**，§2.4）：组合根装配期调用，幂等。
    /// 存储层的过滤/排序/CAS 经 <c>usharp_get</c> 在 SQL 侧下推，且建 <c>usharp_get</c> 表达式索引前须已注册
    /// ⇒ 必须早于任何 <c>Store.InitializeAsync</c>。去除了 <see cref="UsharpDocumentCodec"/> 的静态构造副作用。
    /// </summary>
    public static void EnsureSqlFunctions() => UsharpSqlFunction.EnsureRegistered();

    /// <summary>
    /// 表结构声明源：<c>configDirectory/nodes/tables.ushar</c> → 列 DDL 投影；
    /// 配置目录缺省 / 声明缺件 ⇒ 空源（调用方回落手写 DDL，零行为变更）。
    /// </summary>
    public static ITableSchemaSource SchemaSource(string? configDirectory)
        => UsharpTableSchemaSource.FromFiles(TableDeclarationPaths(configDirectory));

    /// <summary>
    /// 表写面声明源：<c>configDirectory/nodes/tables.ushar</c> → <see cref="TableWriteSpec"/> 投影；
    /// 配置目录缺省 / 声明缺件 ⇒ 空源（调用方按 fail-closed 视作不可写）。
    /// </summary>
    public static ITableWriteSpecSource WriteSpecSource(string? configDirectory)
        => UsharpTableWriteSpecSource.FromFiles(TableDeclarationPaths(configDirectory));

    /// <summary>
    /// 表结构 + 写面**双投影**：同一份 <c>tables.ushar</c> 只编译一次（消费方应优先用本入口）。
    /// </summary>
    public static (ITableSchemaSource Schema, ITableWriteSpecSource WriteSpec) TableSources(string? configDirectory)
        => UsharpTableProjection.FromFiles(TableDeclarationPaths(configDirectory));

    private static IEnumerable<string> TableDeclarationPaths(string? configDirectory)
        => string.IsNullOrWhiteSpace(configDirectory)
            ? Array.Empty<string>()
            : new[] { Path.Combine(configDirectory, "nodes", TablesDeclarationFileName) };

    /// <summary>声明入库闭环：磁盘 <c>.ushar</c> 导入配置库并返回运行期唯一声明源（库优先、磁盘回退）。</summary>
    public static Task<IDeclarationStore> DeclarationStoreAsync(
        IConfigStore configStore, string? diskRoot, CancellationToken ct = default)
        => DeclarationStoreBootstrap.BuildAndImportAsync(configStore, diskRoot, ct: ct);
}
