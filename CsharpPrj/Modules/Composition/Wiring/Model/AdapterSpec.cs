namespace Modules.Wiring.Model;

/// <summary>
/// 适配声明：形态转换的**唯一**声明点（I4）。
/// <para><see cref="Impl"/> 为实现键（运行期委托由上层按此键绑定，框架不解析实现）。</para>
/// </summary>
public sealed record AdapterSpec(string Id, string From, string To, string? Impl = null);
