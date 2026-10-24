#nullable enable

using System.Collections.Generic;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>写入单位（L0 Kernel）：节 = 可独立重绘的最小块（surface 域 + 唯一键）。</summary>
public readonly record struct SectionId(string Surface, string Key);

/// <summary>节落地模式（框架判定，不由领域决定）：Skip=指纹未变 / Patch=键控对齐 / BulkReplace=整体替换。</summary>
public enum SectionWriteMode
{
    Skip = 0,
    Patch,
    BulkReplace,
}

/// <summary>节易变性：volatile 节（tick/stream）在预算压力下可丢；stable 节永不丢。</summary>
public enum SectionVariance
{
    Stable = 0,
    Volatile,
}

/// <summary>
/// 单节写计划（一拍唯一写请求的组成单位）。载荷形态由字段表达（Html/Tree/Stream/Trim/LeafValue/FullDocument），
/// 由 <see cref="SectionPlanAdapter"/> 从 <see cref="UiWritePlan"/> 单向派生（L2 不直接构造）。
/// <para><see cref="Nodes"/>/<see cref="Bytes"/> 为结构估算（构建时 O(n) 顺带计数，不遍历 DOM）。</para>
/// </summary>
public sealed record SectionPlan(
    SectionId Id,
    SectionWriteMode Mode,
    string? Html = null,
    UiNode? Tree = null,
    IReadOnlyList<string>? DropKeys = null,
    int Nodes = 0,
    int Bytes = 0,
    SectionVariance Variance = SectionVariance.Stable,
    UiStreamAppend? Stream = null,
    UiStreamTrim? Trim = null,
    string? LeafValue = null,
    UiInteractionSnapshot? Interaction = null,
    bool FullDocument = false)
{
    /// <summary>目标 DOM 名（= 既有 UiWriteTarget.Name 口径）。</summary>
    public string Name => Id.Key;
}

/// <summary>每拍写预算：<paramref name="MaxNodes"/> 为主预算（节点数），其余为辅助。</summary>
public readonly record struct WriteBudget(int MaxNodes, int MaxBytes, int MaxMs);

/// <summary>单节落地结果（遥测载体：nodes/bytes/jsMs/outcome 四要素，供门禁复算）。</summary>
public readonly record struct SectionOutcome(
    SectionId Id, SectionWriteMode Mode, int Nodes, int Bytes, long JsMs, string Outcome, string? Reason = null);
