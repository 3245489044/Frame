using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Operation.DataStoreDriven.Abstractions;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// <c>[Table]</c> 声明 → 中性列 DDL 投影（实现 <see cref="ITableSchemaSource"/>）：
/// 把 Usharp 数据层的表声明（列名 / 类型 / 标志）映射为 SQLite 列 DDL 片段，
/// 使建表 SQL 由声明派生（消除手写 <c>SchemaColumns</c> 漂移）。
/// </summary>
public sealed class UsharpTableSchemaSource : ITableSchemaSource
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _tables;

    private UsharpTableSchemaSource(IReadOnlyDictionary<string, IReadOnlyList<string>> tables) => _tables = tables;

    /// <inheritdoc />
    public IReadOnlyList<string>? GetColumns(string tableId)
        => !string.IsNullOrWhiteSpace(tableId) && _tables.TryGetValue(tableId, out var cols) ? cols : null;

    /// <summary>空源（无声明 ⇒ 调用方回落手写 DDL）。</summary>
    public static UsharpTableSchemaSource Empty { get; } =
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>由已编译程序集投影（以 TableId 与 Container 双键登记，便于按逻辑名或容器名取用）。</summary>
    public static UsharpTableSchemaSource FromCompilation(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var decl in compilation.Tables)
        {
            var cols = decl.Columns.Select(ToDdl).ToList();
            map[decl.TableId] = cols;
            if (!string.IsNullOrWhiteSpace(decl.Container)) map[decl.Container] = cols;
        }
        return new UsharpTableSchemaSource(map);
    }

    /// <summary>由源文件集构建（磁盘读取；缺件/空集返回 <see cref="Empty"/>）。</summary>
    public static UsharpTableSchemaSource FromFiles(IEnumerable<string> paths)
    {
        var list = (paths ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Select(p => (Path: p, Text: File.ReadAllText(p)))
            .ToList();
        return list.Count == 0 ? Empty : FromCompilation(UsharpCompilation.Compile(list));
    }

    /// <summary>列声明 → SQLite 列 DDL 片段（类型 + 标志受控词映射集中于此）。</summary>
    private static string ToDdl(UsharpColumnDecl c)
    {
        var flags = new HashSet<string>(c.Flags, StringComparer.OrdinalIgnoreCase);
        var sql = SqlType(c.Type, flags);
        if (flags.Contains(FlagAuto)) sql += " PRIMARY KEY AUTOINCREMENT";
        if (flags.Contains(FlagUnique)) sql += " NOT NULL UNIQUE";
        else if (flags.Contains(FlagSource)) sql += " NOT NULL";
        return $"[{c.Name}] {sql}";
    }

    private static string SqlType(string type, HashSet<string> flags)
        => flags.Contains(FlagBlob) || string.Equals(type, "bytes", StringComparison.OrdinalIgnoreCase)
            ? "BLOB"
            : type switch
            {
                "int" => "INTEGER",
                "bool" => "INTEGER",
                "decimal" => "REAL",
                _ => "TEXT",
            };

    private const string FlagAuto = "auto";
    private const string FlagUnique = "unique";
    private const string FlagSource = "source";
    private const string FlagBlob = "blob";
}
