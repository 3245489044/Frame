using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Operation.DataStoreDriven.Abstractions;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// <c>[Table] … write:</c> 声明 → <see cref="TableWriteSpec"/> 投影（实现 <see cref="ITableWriteSpecSource"/>）：
/// 把数据层的表写面声明（可写性 / 编辑器 / 字段规格 / 写后动作 / 原因）从代码提升为声明，缺省 fail-closed。
/// </summary>
public sealed class UsharpTableWriteSpecSource : ITableWriteSpecSource
{
    private readonly IReadOnlyDictionary<string, TableWriteSpec> _specs;

    private UsharpTableWriteSpecSource(IReadOnlyDictionary<string, TableWriteSpec> specs) => _specs = specs;

    /// <inheritdoc />
    public TableWriteSpec? GetWriteSpec(string tableId)
        => !string.IsNullOrWhiteSpace(tableId) && _specs.TryGetValue(tableId, out var spec) ? spec : null;

    /// <summary>空源（无声明 ⇒ 调用方按 fail-closed 视作不可写）。</summary>
    public static UsharpTableWriteSpecSource Empty { get; } =
        new(new Dictionary<string, TableWriteSpec>(StringComparer.OrdinalIgnoreCase));

    /// <summary>由已编译程序集投影（以 TableId 与 Container 双键登记）。</summary>
    public static UsharpTableWriteSpecSource FromCompilation(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var map = new Dictionary<string, TableWriteSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var decl in compilation.Tables)
        {
            var spec = ToSpec(decl);
            map[decl.TableId] = spec;
            if (!string.IsNullOrWhiteSpace(decl.Container)) map[decl.Container] = spec;
        }
        return new UsharpTableWriteSpecSource(map);
    }

    /// <summary>由源文件集构建（磁盘读取；缺件/空集返回 <see cref="Empty"/>）。</summary>
    public static UsharpTableWriteSpecSource FromFiles(IEnumerable<string> paths)
    {
        var list = (paths ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Select(p => (Path: p, Text: File.ReadAllText(p)))
            .ToList();
        return list.Count == 0 ? Empty : FromCompilation(UsharpCompilation.Compile(list));
    }

    private static TableWriteSpec ToSpec(UsharpTableDecl decl)
    {
        var w = decl.WriteSpec;
        // 缺声明（无 write: 段）⇒ fail-closed 且 reason 缺省说明（声明完备性门禁报红）。
        if (w is null)
        {
            return TableWriteSpec.NotWritable(decl.TableId, "（未声明 write: 段：默认不可写）");
        }

        return new TableWriteSpec(
            Table: decl.TableId,
            Editable: w.Editable,
            Editor: w.Editor,
            KeyImmutable: w.KeyImmutable,
            WritableFields: w.WritableFields,
            AllowTypes: w.AllowTypes,
            AfterWrite: w.AfterWrite,
            Reason: w.Reason);
    }
}
