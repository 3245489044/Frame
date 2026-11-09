#nullable enable

namespace SurfacePaint.Contracts;

/// <summary>
/// 指针输入（唯一来源 = 载体采集；窗口客户区 DIP）。
/// <para>纪律：指针是**输入**，不是渲染逻辑；只有 Gather 的 surface-pointer 域可读本结构。</para>
/// </summary>
/// <param name="X">窗口客户区 X（DIP）。</param>
/// <param name="Y">窗口客户区 Y（DIP）。</param>
/// <param name="Inside">是否在窗口客户区内（窗口外 ⇒ 光权重 0）。</param>
/// <param name="StampMs">采集时刻（<see cref="System.Environment.TickCount64"/>）。</param>
public readonly record struct SurfacePointerState(float X, float Y, bool Inside, long StampMs)
{
    /// <summary>不可用占位（端口未就绪/窗口外）：权重 0，禁伪造坐标冒充有值。</summary>
    public static readonly SurfacePointerState Unavailable = new(0f, 0f, false, 0L);
}

/// <summary>
/// 指针来源端口（载体实现；进程内只允许 1 个活动源）。
/// <para>未 Ready ⇒ 光退化为「只有太阳项」并落 <c>pointer-missing</c>；禁返回 null 冒充有值。</para>
/// </summary>
public interface ISurfacePointerSource
{
    /// <summary>端口就绪态（Missing/NotReady ⇒ 调用方必须使用 <see cref="SurfacePointerState.Unavailable"/>）。</summary>
    PortStatus Status { get; }

    /// <summary>读取当前指针（UI 线程采集；无指针事件 ⇒ 复用上一采样，禁定时轮询）。</summary>
    SurfacePointerState Read();
}
