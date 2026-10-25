using Operation.ReconcileDriven.Trace;
using Share.EventBindingOrchestrator.Invocation;

namespace Operation.ReconcileDriven.Reconcile;

/// <summary>
/// 对账失败异常（strict 模式裁决）：结构化携带判据号、缺失 EventKind、顺序断点、串台 ScopeId、EventSeq 断点。
/// 禁止静默通过——任一环断裂即抛。继承 <see cref="InvocationFaultException"/>，使内核保留 ReconcileFault 归因。
/// </summary>
public sealed class EventReconcileException : InvocationFaultException
{
    public EventReconcileException(string trigger, ReconcileVerdict verdict, SignalTrace trace)
        : base(InvocationOutcome.ReconcileFault, BuildMessage(trigger, verdict, trace))
    {
        Trigger = trigger;
        Verdict = verdict;
        Trace = trace;
    }

    /// <summary>触发标识（函数名/控件名）。</summary>
    public string Trigger { get; }

    /// <summary>四判裁决。</summary>
    public ReconcileVerdict Verdict { get; }

    /// <summary>实际信号账（供取证/复算）。</summary>
    public SignalTrace Trace { get; }

    private static string BuildMessage(string trigger, ReconcileVerdict v, SignalTrace t)
    {
        var observed = t.Count == 0
            ? "(无)"
            : string.Join("→", t.Records.Select(r => $"{r.EventKind}#{r.EventSeq}@{r.ScopeId}"));
        return $"ReconcileFailed {{ trigger={trigger}, {v.ToSummary()}, observed={observed} }}";
    }
}
