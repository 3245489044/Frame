using Modules.KeySpace.Model;

namespace Modules.KeySpace.Abstractions;

/// <summary>
/// 坐标解析端口（唯一实现 = <c>CoordinateResolver</c>）：把「任意输入」解析为「声明面规定的坐标规范值」。
/// <para>
/// 纪律：未命中 ⇒ <see cref="CoordinateResult.Ok"/>=false（禁回退 raw）；未装配 ⇒ 调用方 fail-fast。
/// </para>
/// </summary>
public interface ICoordinateResolver
{
    /// <summary>解析坐标（<paramref name="coordinateName"/> 形如 <c>family/axis</c>）。</summary>
    /// <param name="coordinateName">坐标名（声明面 <c>axis &lt;family&gt;/&lt;axis&gt;</c>）。</param>
    /// <param name="raw">原始值（如成员实例键 / 裸业务键）。</param>
    CoordinateResult Resolve(string coordinateName, string? raw);

    /// <summary>已装配的坐标名（诊断用；未声明 ⇒ 空集）。</summary>
    IReadOnlyCollection<string> Coordinates { get; }
}
