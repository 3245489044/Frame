#nullable enable

namespace FrontEndDriven.ControlRender.Layout;

/// <summary>
/// 布局失效判定用几何快照（不可变值对象）：唯一封装「物理基准是否真的变了」的差集判定输入，
/// 与 HostBasisSystem.ResolveBasis 读取的基准数据同源（宿主客户区尺寸 + form 相对原点 + DPI），
/// 避免「判定与重排各读一套物理属性」导致两套规则。
/// Step1：宿主 Resize 事件仅作「提醒」，由本快照之差决定是否调度重排（值没变 → 不调度）。
/// </summary>
public readonly record struct GeometrySnapshot
{
    /// <summary>宿主客户区宽度（布局基准尺寸，与 ResolveTargetSize 同源）。</summary>
    public int Width { get; init; }

    /// <summary>宿主客户区高度（布局基准尺寸，与 ResolveTargetSize 同源）。</summary>
    public int Height { get; init; }

    /// <summary>宿主全局 form 相对原点 X（与 ResolveHostFormPosition 同源）。</summary>
    public int OriginX { get; init; }

    /// <summary>宿主全局 form 相对原点 Y（与 ResolveHostFormPosition 同源）。</summary>
    public int OriginY { get; init; }

    /// <summary>宿主当前设备 DPI（覆盖显示缩放；Step3 DPI 缩放覆盖的直接判据）。</summary>
    public int DeviceDpi { get; init; }

    /// <summary>快照是否无效（宿主未就绪/尺寸非法 → 不能用于值差判定）。</summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <summary>判定：当前快照相较于上一次快照是否发生几何变化。</summary>
    public readonly bool Changed(in GeometrySnapshot other)
        => Width != other.Width
        || Height != other.Height
        || OriginX != other.OriginX
        || OriginY != other.OriginY
        || DeviceDpi != other.DeviceDpi;

    public override readonly string ToString()
        => $"{Width}x{Height}@({OriginX},{OriginY})dpi={DeviceDpi}";
}