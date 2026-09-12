using DataCenter.Protocol.Generation;

namespace Operation.StateBlock.Generation;

/// <summary>
/// 代际读口的默认实现（不可变值）—— generation / fingerprint / schemaVersion。
/// 换代流程以本值对象为交换载荷（D5）：Export 旧代数 → Import 新代数（generation+1、fingerprint 更新、Seq 连续）。
/// </summary>
/// <param name="Generation">插件代数（热更新次数；初始 0）。</param>
/// <param name="Fingerprint">内容指纹（未计算 = null）。</param>
/// <param name="SchemaVersion">结构版本（初始 1）。</param>
public sealed record GenerationState(int Generation, string? Fingerprint, int SchemaVersion) : IStateGeneration
{
    /// <summary>初始代际（装配期起点）。</summary>
    public static GenerationState Initial { get; } = new(0, null, 1);

    /// <summary>换代推进（generation+1）。注意 <paramref name="fingerprint"/> 为 <c>null</c> 时**置空**指纹；
    /// 调用方若意图"保持既有"，须显式传入现值（见 <c>DataCenterHost.Advance</c> 的 <c>plan.Fingerprint ?? state.Fingerprint</c>）。</summary>
    public GenerationState Next(string? fingerprint = null)
        => new(Generation + 1, fingerprint, SchemaVersion);

    /// <summary>结构演进（schemaVersion+1，触发迁移）。</summary>
    public GenerationState WithSchemaVersion(int schemaVersion)
        => new(Generation, Fingerprint, schemaVersion);
}
