using Rge.Unified.Models;
using Rge.Unified.Systems;
using ExecutionFramework.Engine;
using RgeEntity = Rge.Unified.Models.Entity;

namespace ExecutionFramework.Systems;

public sealed class ConditionSystem : IIntentSystem
{
    private readonly TConfig.IFunctionExecutor? _functionExecutor;
    private readonly EntityExecutionHelper _helper;

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