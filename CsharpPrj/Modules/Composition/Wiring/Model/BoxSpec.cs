namespace Modules.Wiring.Model;

/// <summary>
/// 盒：实现体 + 端口集合，**不含连接知识**（连接只由 <see cref="WireSpec"/> 建立）。
/// <para><see cref="Terminal"/>=true 的盒的 out 端口免 D6（孤儿出）判据。</para>
/// </summary>
public sealed record BoxSpec(string Id, string Impl, IReadOnlyList<PortSpec> Ports, bool Terminal = false);
