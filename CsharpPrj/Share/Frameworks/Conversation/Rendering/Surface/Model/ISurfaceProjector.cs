#nullable enable

using Share.Conversation.Rendering.Surface;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 载体投影器（每载体恰一个实现）：把已解析表面投影为该载体的产物。
/// <para>投影器属载体的方言落点（HTML/WinForms/Text），本契约只固定「载体 id + 纯函数投影」。</para>
/// </summary>
/// <typeparam name="TPayload">载体产物类型（文本/控件快照/节点树…）。</typeparam>
public interface ISurfaceProjector<out TPayload>
{
    /// <summary>载体 id（与声明面 carriers 键一致）。</summary>
    string CarrierId { get; }

    /// <summary>投影一次已解析表面（纯函数；缺失/不支持通过表面 Issues 表达）。</summary>
    /// <param name="surface">已解析表面。</param>
    TPayload Project(ResolvedSurface surface);
}
