using Modules.Wiring.Model;
using Modules.Wiring.Runtime;

namespace Modules.Wiring.Compile;

/// <summary>
/// 已解析的连接：两端端口 + 绑定的 Adapter + 编译后的判据谓词。
/// <para><see cref="JudgeError"/> 非空表示 <c>judge=</c> 语法不受支持（不静默忽略）。</para>
/// </summary>
public sealed record LinkedWire(
    WireSpec Spec,
    PortSpec From,
    PortSpec To,
    AdapterSpec? Adapter,
    Func<ValueEnvelope, bool>? Judge,
    string? JudgeError = null)
{
    public string Id => Spec.Id;
}
