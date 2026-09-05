
#nullable enable

using System.Text.Json.Serialization;
using System.Windows.Forms;
using Modules.InternalStorage.Core;
using ExecutionCenter.Abstractions;
using TConfig;
using FrontEndDriven.Pipeline;

namespace FrontEndDriven.Gate;

public abstract record EcsOperation<TResult>
{
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public abstract string OperationName { get; }
}

/// <summary>
/// 原子 Load+Render 操作（P3/RD2）：LoadConfigAndRenderAsync 合并为单一原子操作，
/// 消除「Dispatch(LoadConfig) + 独立 ExecuteRenderStage」双轨编排。
/// P7：结果类型升为 RenderRunResult（含挂载统计），「渲染执行 ≠ 挂载可见」由 Succeeded 判据暴露。
/// 注：旧 operation 族（LoadConfig/ExecuteRender/QueryControls/ClearScope/RecalculateLayout）已废弃——
///     全仓无构造调用方（死分支），随 EcsGate.DispatchAsync 精简一并删除（RedundancyElimination）。
///     作用域清理语义收敛到 EcsGate.DestroyScopeAsync 私有门面（ICompositionLifecycle.ScopeDestroyed 单一路径）。
/// </summary>
public sealed record LoadAndRenderOperation : EcsOperation<RenderRunResult>
{
    public override string OperationName => "LoadAndRender";
    public required UIConfiguration Config { get; init; }
    public required string Scope { get; init; }

    [JsonIgnore]
    public Control? HostControl { get; init; }
}

