#nullable enable

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 稳定键**单源**（禁各处手拼 <c>"m:" + round</c>）。键格式是渲染层派生物，作者不得手写。
/// </summary>
public static class FragmentKeys
{
    /// <summary>段键：<c>r{round}</c> / <c>head</c> / <c>op</c> / <c>empty</c> / <c>degraded</c>。</summary>
    public static string RoundSection(int round) => $"r{round}";

    /// <summary>锚点消息行键。</summary>
    public static string Message(int round, int groupIndex) => $"m:{round}:{groupIndex}";

    /// <summary>前导行键。</summary>
    public static string Lead(int round, int index) => $"l:{round}:{index}";

    /// <summary>头提示层行键。</summary>
    public static string Head(int index) => $"h:0:{index}";

    /// <summary>扁平附属行键（无语义表路径）。</summary>
    public static string Attach(int round, int groupIndex, int index)
        => $"a:{round}:{groupIndex}:{index}";

    /// <summary>分组内附属行键。</summary>
    public static string AttachRow(int round, int groupIndex, int group, int row)
        => $"a:{round}:{groupIndex}:{group}:{row}";

    /// <summary>分组键。</summary>
    public static string Group(int round, int groupIndex, int group)
        => $"g:{round}:{groupIndex}:{group}";

    /// <summary>结果折叠键（挂在所属行键上）。</summary>
    public static string Result(string rowKey) => $"{rowKey}-result";
}
