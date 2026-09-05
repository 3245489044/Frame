using Operation.LoopFoldMethod.Attempt;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.FoldHead;
using Operation.LoopFoldMethod.Operator;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>确定性假算子：固定展开序列（每步状态 +1），零 AI。供 unfold 全分支测试。</summary>
internal sealed class IncrementOperator : IFoldOperator<int, int>
{
    private int _i;
    public Task<FoldStep<int, int>> Expand(int state, FoldContext<int> ctx, CancellationToken ct)
    {
        _i++;
        return Task.FromResult(new FoldStep<int, int>(state + 1, _i));
    }
}

/// <summary>常量算子：恒返回同状态（用于收敛 / 失控测试）。</summary>
internal sealed class ConstantOperator : IFoldOperator<int, int>
{
    private readonly int _value;
    private int _i;
    public ConstantOperator(int value) => _value = value;
    public Task<FoldStep<int, int>> Expand(int state, FoldContext<int> ctx, CancellationToken ct)
    {
        _i++;
        return Task.FromResult(new FoldStep<int, int>(_value, _i));
    }
}

/// <summary>空算子：恒返回 null（测试折叠器空值守卫 → 熔断执法）。</summary>
internal sealed class NullOperator : IFoldOperator<int, int>
{
    public Task<FoldStep<int, int>> Expand(int state, FoldContext<int> ctx, CancellationToken ct)
        => Task.FromResult<FoldStep<int, int>>(null!);
}

/// <summary>记录上下文算子：记录每次展开收到的坐标与前置顶点数（验证 FoldContext 记账面）。</summary>
internal sealed class RecordContextOperator : IFoldOperator<int, int>
{
    public List<int> SeenCoordinates { get; } = new();
    public List<int> SeenPreviousCounts { get; } = new();
    public Task<FoldStep<int, int>> Expand(int state, FoldContext<int> ctx, CancellationToken ct)
    {
        SeenCoordinates.Add(ctx.Coordinate);
        SeenPreviousCounts.Add(ctx.PreviousVertices.Count);
        return Task.FromResult(new FoldStep<int, int>(state + 1, ctx.Coordinate));
    }
}

/// <summary>求和折叠头投影（假实现）：前缀各状态点之和 → 判定点。测「前缀 → 判定点」折叠正确性。</summary>
internal sealed class SumHeadProjector : IFoldHeadProjector<int>
{
    public int Head(IReadOnlyList<int> trace)
    {
        var sum = 0;
        foreach (var s in trace) sum += s;
        return sum;
    }
}

/// <summary>记录观测器（假实现）：记录顶点追加与状态推进。</summary>
internal sealed class RecordingObserver : IFoldObserver<int, int>
{
    public List<FoldVertex<int>> Vertices { get; } = new();
    public List<int> StatesAdvanced { get; } = new();
    public void OnVertex(FoldVertex<int> vertex) => Vertices.Add(vertex);
    public void OnStateAdvanced(int state) => StatesAdvanced.Add(state);
}
