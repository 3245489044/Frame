namespace Modules.Wiring.Model;

/// <summary>端口方向（与连线的一端一一对应）。</summary>
public enum PortDirection
{
    In,
    Out,
}

/// <summary>值形态声明。<see cref="ClrType"/> 为空即未定型（D2 报告点名，禁静默兜底）。</summary>
public sealed record FormSpec(string Id, Type? ClrType = null, bool Sealed = false);
