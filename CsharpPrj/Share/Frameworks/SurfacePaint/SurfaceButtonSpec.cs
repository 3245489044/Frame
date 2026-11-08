#nullable enable

namespace Share.SurfacePaint;

/// <summary>
/// 按钮外壳规格（载体无关常量单源）：最小高度 / 内边距 / 悬停与按下偏移 / 去 3D 边。
/// <para>载体只负责把数值写进平台属性（FlatStyle/FlatAppearance/MinimumSize/Padding）。</para>
/// </summary>
public static class SurfaceButtonSpec
{
    /// <summary>最小高度（DIP）。</summary>
    public const int MinHeightDip = 24;

    /// <summary>水平内边距（DIP）。</summary>
    public const int PaddingXDip = 8;

    /// <summary>悬停提亮比例。</summary>
    public const float HoverLighten = 0.08f;

    /// <summary>按下压暗比例。</summary>
    public const float PressedDarken = 0.08f;

    /// <summary>描边宽度（0 = 去系统 3D 边）。</summary>
    public const int BorderSize = 0;
}
