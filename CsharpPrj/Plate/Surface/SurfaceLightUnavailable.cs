#nullable enable

namespace UiBackend;

/// <summary>
/// 光场不可用载荷（显式 Unknown）：保证聚拢点提供者**永不返回 null**，同时原因码可见。
/// <para>禁止用 <see cref="SurfaceLight.DefaultFrame"/> 冒充真实光（兜底光不同源）。</para>
/// </summary>
/// <param name="Reason">原因码（如 light-unavailable）。</param>
public sealed record SurfaceLightUnavailable(string Reason);
