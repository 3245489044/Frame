using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Operation.DataStoreDriven.Abstractions;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 声明投影源：磁盘路径集 → 一次编译（吸收各 <c>FromFiles</c> 的 <c>Exists+ReadAllText+Compile</c> 样板）。
/// <para>空集不编译（调用方据 <c>Empty</c> 回落）；同一路径集只编译一次由 <see cref="UsharpTableProjection"/> 保证。</para>
/// </summary>
public static class DeclaredProjectionSource
{
    /// <summary>读盘并编译（缺件/空集 ⇒ null，调用方回落 <c>Empty</c>）。</summary>
    public static UsharpCompilation? CompileFiles(IEnumerable<string>? paths)
    {
        var files = (paths ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .ToList();
        return files.Count == 0 ? null : UsharpCompilation.CompileFiles(files);
    }
}

/// <summary>
/// <c>[Table]</c> 双投影工厂：**同一份 <c>tables.ushar</c> 只编译一次**，同时产出列 DDL 源与写面源。
/// </summary>
public static class UsharpTableProjection
{
    /// <summary>由同一编译产物产出双投影（不重复编译）。</summary>
    public static (ITableSchemaSource Schema, ITableWriteSpecSource WriteSpec) FromCompilation(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return (UsharpTableSchemaSource.FromCompilation(compilation),
                UsharpTableWriteSpecSource.FromCompilation(compilation));
    }

    /// <summary>由同一批源文件产出双投影（一次读盘 + 一次编译；空集 ⇒ 双空源）。</summary>
    public static (ITableSchemaSource Schema, ITableWriteSpecSource WriteSpec) FromFiles(IEnumerable<string>? paths)
    {
        var compilation = DeclaredProjectionSource.CompileFiles(paths);
        return compilation is null
            ? (UsharpTableSchemaSource.Empty, UsharpTableWriteSpecSource.Empty)
            : FromCompilation(compilation);
    }
}
