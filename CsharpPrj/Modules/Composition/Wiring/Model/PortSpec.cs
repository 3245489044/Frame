namespace Modules.Wiring.Model;

/// <summary>
/// 端口声明：<c>{Owner, Name, Dir, Form, Required, Card, Scope}</c>。
/// <para><see cref="Ref"/> = <c>owner.port</c> 坐标（wire 唯一引用形态，禁裸键反解，I6）。</para>
/// </summary>
public sealed record PortSpec(
    string Owner,
    string Name,
    PortDirection Dir,
    string Form,
    bool Required = false,
    int Card = 1,
    string Scope = "global",
    string? KeyTemplate = null)
{
    /// <summary>键空间坐标（wire 端点唯一引用形态）。</summary>
    public string Ref => Owner + "." + Name;

    public bool IsIn => Dir == PortDirection.In;

    public bool IsOut => Dir == PortDirection.Out;
}
