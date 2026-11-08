using System;
using System.Collections.Generic;

namespace ValidationFramework.Declaration;

/// <summary>
/// 声明解析器（N-08：Parse 阶段的可插拔端口；各声明提供自己的源→表映射）。
/// </summary>
/// <typeparam name="TSource">声明源（文本/JSON/对象）。</typeparam>
/// <typeparam name="TTable">解析产物表。</typeparam>
public interface IDeclarationParser<in TSource, out TTable>
{
    /// <summary>解析声明源；诊断写入 <paramref name="errors"/>（不抛为正常路径）。</summary>
    TTable Parse(TSource source, ICollection<string> errors);
}

/// <summary>
/// 声明编译器骨架（N-08）：<c>Parse → ValidateCross（fail-closed）→ Build/指纹</c> 的单一实现。
/// <para>各声明只提供 <see cref="IDeclarationParser{TSource,TTable}"/> 与跨表校验/指纹委托；错误码与文案由调用方原样保留。</para>
/// </summary>
/// <typeparam name="TSource">声明源。</typeparam>
/// <typeparam name="TTable">解析产物表（引用类型）。</typeparam>
public static class DeclarationCompiler<TSource, TTable> where TTable : class
{
    /// <summary>编译声明；任一阶段产生错误 ⇒ 返回 <c>null</c>（不产出半套表）。</summary>
    /// <param name="source">声明源。</param>
    /// <param name="parser">解析器。</param>
    /// <param name="validateCross">跨表校验（可空）。</param>
    /// <param name="fingerprint">内容指纹（单源 = Signature.Sha256 等）。</param>
    /// <param name="withFingerprint">把指纹写回产物表。</param>
    /// <param name="errors">诊断（空 = 可用）。</param>
    public static TTable? Compile(
        TSource source,
        IDeclarationParser<TSource, TTable> parser,
        Action<TTable, ICollection<string>>? validateCross,
        Func<TTable, string> fingerprint,
        Func<TTable, string, TTable> withFingerprint,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(parser);

        var list = new List<string>();
        TTable table;
        try
        {
            table = parser.Parse(source, list);
        }
        catch (Exception ex)
        {
            list.Add(ex.Message);
            errors = list;
            return null;
        }

        validateCross?.Invoke(table, list);
        errors = list;
        if (list.Count > 0) return null;

        return withFingerprint(table, fingerprint(table));
    }
}
