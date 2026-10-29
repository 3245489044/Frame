namespace Modules.Node.Stream;

/// <summary>
/// 帧级键渲染端口（通用；域无关）：把**帧身份**按该域声明的 <c>Display</c> 面渲染成键。
/// <para>
/// 存在理由：框架不得内置键形（原先 <c>StreamFrameKeys</c> 手拼 <c>{prefix}{task}.f{seq}</c>，
/// 与声明面 `agent.keyspace.ushar` 的 Display 模板构成两套真相）。键形唯一来源 = 键空间声明，
/// 实现由产品侧（<c>Agent.Contract</c>）注入；框架只定义端口与调用点。
/// </para>
/// </summary>
public interface IFrameKeyRenderer
{
    /// <summary>渲染帧级键（读侧内存合成，不落库）。</summary>
    /// <param name="familyId">键族 id（声明面单源）。</param>
    /// <param name="container">容器（分区键）。</param>
    /// <param name="identity">帧身份（轴值来源）。</param>
    string Render(string familyId, string container, StreamFrameIdentity identity);
}
