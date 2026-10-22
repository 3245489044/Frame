namespace Share.ObjectiveBoundary;

/// <summary>
/// 运行期边界状态快照（跨轮持久化，存于 AxiomContext.Data[BoundaryKeys.BoundaryState]）。
/// 记录当前生效的有效根目录、已执行的重构动作与上一轮 Φ（供重构验证/回退闭环）。
/// </summary>
public sealed record BoundaryContext(
    string? EffectiveRoot,
    string LastAction,
    double PhiOld,
    int ReconstructCount,
    string? DataDir,
    int LastReconstructRound = 0)
{
    public static BoundaryContext Initial(string? root)
        => new(root, BoundaryActionKind.None, 0d, 0, null);

    public BoundaryContext Apply(BoundaryAssessment a, string? newRoot, string? dataDir = null)
        => this with
        {
            EffectiveRoot = newRoot ?? EffectiveRoot,
            LastAction = a.Action,
            PhiOld = a.PhiOld,
            ReconstructCount = a.Triggered ? ReconstructCount + 1 : ReconstructCount,
            DataDir = dataDir ?? DataDir,
        };
}
