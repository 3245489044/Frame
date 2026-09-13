
namespace Share.RoslynJson.Mapping;

/// <summary>语法映射选项（未命中规则时的兜底映射形态控制）。</summary>
public sealed class SyntaxMappingOptions
{
    /// <summary>兜底映射是否写入源位置成员（<c>line</c>/<c>column</c>）。</summary>
    public bool IncludeLocation { get; set; } = true;

    /// <summary>兜底映射是否递归子节点（false = 只输出 kind/text）。</summary>
    public bool IncludeChildren { get; set; } = true;

    /// <summary>递归深度上限（超出即截断并加诊断，防畸形/超深语法树）。</summary>
    public int MaxDepth { get; set; } = 32;

    /// <summary>兜底映射是否记录「未注册规则」警告（false = 静默兜底）。</summary>
    public bool WarnOnUnmapped { get; set; } = true;

    public static SyntaxMappingOptions Default => new SyntaxMappingOptions();

    /// <summary>仅顶层（不递归子节点）预设。</summary>
    public static SyntaxMappingOptions Shallow => new SyntaxMappingOptions { IncludeChildren = false };
}
