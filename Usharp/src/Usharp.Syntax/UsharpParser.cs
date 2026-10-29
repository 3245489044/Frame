using Plate.DslMethod;
using Plate.DslMethod.Logic;
using Plate.DslMethod.Model;
using Plate.DslMethod.Parsing;

namespace Usharp.Syntax;

/// <summary>
/// Usharp 语法入口（薄包，零自有行解析）：文本 → <see cref="DslDocument"/>。
/// 复用 <see cref="DslParser"/>（作用域由调用方给出：编译期 = 核心 ∪ 已装载包；缺省 = 本包全量
/// <see cref="UsharpPrefixes.Scope"/>）与 <see cref="DslIndex"/>（符号索引 + 反向引用）。
/// </summary>
public static class UsharpParser
{
    /// <summary>Usharp 默认库标识（符号索引键 <c>lib::STYP::name</c> 的 lib 段）。</summary>
    public const string DefaultLib = "ushar";

    /// <summary>解析一段 Usharp 文本（作用域 = 本包全量前缀，兼容入口）。</summary>
    public static DslDocument Parse(string text, string sourceFile, string lib = DefaultLib)
        => DslParser.Parse(text, sourceFile, lib, UsharpPrefixes.Scope);

    /// <summary>按指定前缀作用域解析（编译期按已装载包构造作用域，未装载的前缀不被识别）。</summary>
    public static DslDocument Parse(string text, string sourceFile, string lib, DslPrefixSet scope)
        => DslParser.Parse(text, sourceFile, lib, scope);

    /// <summary>五前缀是否均已注册（查本包作用域；缺失即语言面未接线）。</summary>
    public static bool PrefixesRegistered()
        => UsharpPrefixes.Scope.Find(UsharpLanguage.SpacePrefix) is { Kind: DslSymbolKind.Space }
        && UsharpPrefixes.Scope.Find(UsharpLanguage.MatrixPrefix) is { Kind: DslSymbolKind.Matrix }
        && UsharpPrefixes.Scope.Find(UsharpLanguage.FlowPrefix) is { Kind: DslSymbolKind.Flow }
        && UsharpPrefixes.Scope.Find(UsharpLanguage.RefPrefix) is { Kind: DslSymbolKind.Ref }
        && UsharpPrefixes.Scope.Find(UsharpLanguage.ObsPrefix) is { Kind: DslSymbolKind.Obs };
}
