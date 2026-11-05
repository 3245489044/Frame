
namespace Share.RoslynJson.Mapping;

/// <summary>
/// 语法映射选项（未命中规则时的兜底映射形态控制）。
/// 递归深度上限不在此重复声明：统一读 <c>ConversionContext.Options.MaxDepth</c>
/// （与 IR 发射层同一预算口，避免映射 32 / 发射 64 两套口径）。
/// </summary>
public sealed class SyntaxMappingOptions
{
    /// <summary>兜底映射是否写入源位置成员（<c>line</c>/<c>column</c>）。</summary>
    public bool IncludeLocation { get; set; } = true;

    /// <summary>兜底映射是否递归子节点（false = 只输出 kind/text）。</summary>
    public bool IncludeChildren { get; set; } = true;

    /// <summary>兜底映射是否记录「未注册规则」警告（false = 静默兜底）。</summary>
    public bool WarnOnUnmapped { get; set; } = true;

    public static SyntaxMappingOptions Default => new SyntaxMappingOptions();

    /// <summary>仅顶层（不递归子节点）预设。</summary>
    public static SyntaxMappingOptions Shallow => new SyntaxMappingOptions { IncludeChildren = false };
}
