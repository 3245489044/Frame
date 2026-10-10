using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Sqlite;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// USharp 数据层端口组合门面（唯一注入入口 / SingleImplementation）：
/// 把三端口（<see cref="IDocumentCodec"/> / <see cref="ITableSchemaSource"/> / <see cref="IDeclarationStore"/>）
/// 的 USharp 实现构建收敛于此一处，组合根只调本门面，**禁在各装配点内联** <c>Usharp*</c> 构造字面
/// （消除「改一处漏另一侧」的双路径不一致）。
/// <para>
/// 依赖方向：本类落在唯一桥接层 <c>Usharp.Adapters.Declarations</c>（可同时见 Usharp 与 DataStoreDriven）；
/// 存储层与 <c>Usharp.Semantics</c> 仍不引 USharp 实现，实现一律经组合根注入。
/// </para>
/// </summary>
public static class UsharpDataPorts
{
    /// <summary>表声明文件名（<c>[Table]</c> 真源，位于配置目录 <c>nodes/</c> 下）。</summary>
    public const string TablesDeclarationFileName = "tables.ushar";

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
        => UsharpTableSchemaSource.FromFiles(
            string.IsNullOrWhiteSpace(configDirectory)
                ? Array.Empty<string>()
                : new[] { Path.Combine(configDirectory, "nodes", TablesDeclarationFileName) });

    /// <summary>声明入库闭环：磁盘 <c>.ushar</c> 导入配置库并返回运行期唯一声明源（库优先、磁盘回退）。</summary>
    public static Task<IDeclarationStore> DeclarationStoreAsync(
        IConfigStore configStore, string? diskRoot, CancellationToken ct = default)
        => DeclarationStoreBootstrap.BuildAndImportAsync(configStore, diskRoot, ct);
}
