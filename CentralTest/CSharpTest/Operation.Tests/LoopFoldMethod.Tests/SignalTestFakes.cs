using System.Threading;
using System.Threading.Tasks;
using Operation.LoopFoldMethod.Attempt;
using Operation.LoopFoldMethod.Fold;
using Operation.LoopFoldMethod.Operator;
using Operation.LoopFoldMethod.Signal;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>确定性信号驱动假算子：按步骤键固定产出（stepKey → "已执行:stepKey"），零 AI。测信号驱动步进全分支。</summary>
internal sealed class RecordingSignalOperator : ISignalDrivenOperator<int, string>
{
    public List<string> ExecutedSteps { get; } = new();
    public List<FoldSignal<string>> SeenSignals { get; } = new();

    public Task<SignalFoldStep<int, string>> ExpandAsync(
        int state, FoldSignal<string> signal, string stepKey, FoldContext<string> ctx, CancellationToken ct)
    {
        ExecutedSteps.Add(stepKey);
        SeenSignals.Add(signal);
        return Task.FromResult(new SignalFoldStep<int, string>(state + 1, $"已执行:{stepKey}", stepKey));
    }
}

/// <summary>恒抛取消的假算子：验证取消终止（Cancelled）。</summary>
internal sealed class CancellingSignalOperator : ISignalDrivenOperator<int, string>
{
    public Task<SignalFoldStep<int, string>> ExpandAsync(
        int state, FoldSignal<string> signal, string stepKey, FoldContext<string> ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new SignalFoldStep<int, string>(state + 1, $"已执行:{stepKey}", stepKey));
    }
}

/// <summary>记录观测器（假实现）：记录顶点追加与状态推进。</summary>
internal sealed class RecordingSignalObserver : IFoldObserver<int, string>
{
    public List<FoldVertex<string>> Vertices { get; } = new();
    public List<int> StatesAdvanced { get; } = new();

    public void OnVertex(FoldVertex<string> vertex) => Vertices.Add(vertex);

    public void OnStateAdvanced(int state) => StatesAdvanced.Add(state);
}

