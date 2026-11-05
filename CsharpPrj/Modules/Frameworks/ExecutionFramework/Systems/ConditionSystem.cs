using Rge.Unified.Models;
using Rge.Unified.Systems;
using ExecutionFramework.Engine;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Systems;

public sealed class ConditionSystem : IIntentSystem
{
    private readonly TConfig.IFunctionExecutor? _functionExecutor;
    private readonly EntityExecutionHelper _helper;

    /// <summary>
    /// 诊断探针（宿主注入日志器）：条件函数「未成功 / 非 bool」被按假分支处理时上报分类+原因。
    /// <para>本钩子不改变控制流语义（缺省仍判假），只消除「点击 success 但静默走 else」的不可见性。</para>
    /// </summary>
    public static Action<string, object?>? Trace { get; set; }

    public ConditionSystem(
        TConfig.IFunctionExecutor? functionExecutor = null,
        IntentSystemRouter? router = null,
        Rge.Unified.Resolution.ReferenceResolver? resolver = null)
    {
        _functionExecutor = functionExecutor;
        _helper = new EntityExecutionHelper(router, resolver);
    }

    public string StepType => "Condition";

    public async Task<IntentResult> ExecuteAsync(
        RgeEntity entity,
        IReadOnlyDictionary<string, object?> resolvedParameters,
        ExecutionGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.SubGraph == null || entity.SubGraph.Entities.Count == 0)
            return IntentResult.Success(null);

        bool conditionResult = await EvaluateConditionAsync(entity, resolvedParameters, cancellationToken);

        int trueBranchCount = GetTrueBranchCount(entity, entity.SubGraph.Entities.Count);

        var branchEntities = conditionResult
            ? entity.SubGraph.Entities.Values.Take(trueBranchCount).ToList()
            : entity.SubGraph.Entities.Values.Skip(trueBranchCount).ToList();

        var results = new List<IntentResult>();
        foreach (var child in branchEntities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _helper.ExecuteEntityAsync(child, entity.SubGraph!, cancellationToken);
            if (!result.IsSuccess)
                return IntentResult.Failure(result.ErrorMessage ?? "分支子实体执行失败");

            results.Add(result);
        }

        return IntentResult.Success(conditionResult, new Dictionary<string, object?>
        {
            ["conditionResult"] = conditionResult,
            ["branchResults"] = results
        });
    }

    private async Task<bool> EvaluateConditionAsync(
        RgeEntity entity,
        IReadOnlyDictionary<string, object?> resolvedParameters,
        CancellationToken cancellationToken)
    {
        var funcRef = entity.FuncRef;
        if (_functionExecutor == null || string.IsNullOrEmpty(funcRef))
            return true;

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _functionExecutor.ExecuteAsync(
            funcRef, resolvedParameters.ToNonNullDictionary(), null);

        if (result.IsSuccess && result.Value is bool boolVal)
            return boolVal;

        // 失败可见化：非成功（参数缺失/函数未注册/执行异常）或返回值非 bool 一律按假分支，
        // 但必须留下结构化原因（否则表现为「点击成功但视图不变」）。
        Trace?.Invoke("ConditionSystem_FunctionNotTruthy", new
        {
            entity = entity.Name,
            funcRef,
            isSuccess = result.IsSuccess,
            valueType = result.Value?.GetType().Name ?? "(null)",
            error = result.ErrorMessage ?? string.Empty,
            parameterNames = string.Join(",", resolvedParameters.Keys)
        });
        return false;
    }

    private static int GetTrueBranchCount(RgeEntity entity, int totalCount)
    {
        var metadata = entity.Components.Get<Dictionary<string, object?>>(ComponentKeys.Metadata);
        if (metadata != null && metadata.TryGetValue("TrueBranchCount", out var metaVal)
            && metaVal != null && int.TryParse(metaVal.ToString(), out var parsed))
        {
            return Math.Clamp(parsed, 0, totalCount);
        }

        return totalCount;
    }
}