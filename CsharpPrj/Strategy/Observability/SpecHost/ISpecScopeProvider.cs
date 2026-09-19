namespace Strategy.Observability.SpecHost;

/// <summary>
/// 环境作用域提供者（领域侧实现 = <c>AgentTrace.AmbientScopeId()</c>；无作用域 = 仅 <c>Port(…)</c> 根可用）。
/// <para>注入而非直依赖：宿主据此在缺作用域时报可定位异常（「run/round 未进入」），不伪造参数（G-5）。</para>
/// </summary>
public interface ISpecScopeProvider
{
    /// <summary>当前环境作用域标识（RoundId → ScopeKey → RunId；无 → null）。</summary>
    string? AmbientScopeId();
}
