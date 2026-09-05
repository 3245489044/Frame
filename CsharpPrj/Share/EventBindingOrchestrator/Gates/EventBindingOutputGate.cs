
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using TConfig;
using Share.EventBindingOrchestrator.Diagnostics;

namespace Share.EventBindingOrchestrator.Gates;

public sealed class ControlBindingSnapshot
{
    public string ControlName { get; init; } = string.Empty;

    public string EventName { get; init; } = string.Empty;

    public string FunctionName { get; init; } = string.Empty;

    public bool IsBound { get; init; }

    public string Status { get; init; } = string.Empty;
}

public sealed class EventBindingOutput
{
    public BindingResult Result { get; init; } = new();

    public IReadOnlyList<ControlBindingSnapshot> Snapshot { get; init; } =
        Array.Empty<ControlBindingSnapshot>();

    public int VerifiedBoundCount { get; init; }

    public int TotalDeclarationCount { get; init; }
}

public static class EventBindingOutputGate
{
    public static EventBindingOutput BuildOutput(
        EventBindingContext ctx,
        int verifiedBoundCount,
        IReadOnlyList<ControlBindingSnapshot>? snapshot = null,
        int totalDeclarations = 0)
    {
        var result = new BindingResult
        {
            BoundControls = ctx.BoundControls.ToList(),
            SkippedControls = ctx.SkippedControls.ToList()
        };

        EventBindingDiagnosticContext.Record("outputgate.build", new
        {
            boundCount = result.BoundCount,
            skippedCount = result.SkippedCount,
            errorCount = result.ErrorCount,
            verifiedBoundCount,
            totalDeclarations,
            timestamp = DateTime.UtcNow
        });

        return new EventBindingOutput
        {
            Result = result,
            Snapshot = snapshot ?? Array.Empty<ControlBindingSnapshot>(),
            VerifiedBoundCount = verifiedBoundCount,
            TotalDeclarationCount = totalDeclarations
        };
    }

    public static EventBindingOutput BuildFromRaw(
        IReadOnlyList<ControlBindingInfo> boundControls,
        IReadOnlyList<ControlBindingInfo> skippedControls,
        IReadOnlyList<ControlBindingSnapshot>? snapshot = null,
        int? verifiedBoundCount = null)
    {
        var result = new BindingResult
        {
            BoundControls = boundControls.ToList(),
            SkippedControls = skippedControls.ToList()
        };

        return new EventBindingOutput
        {
            Result = result,
            Snapshot = snapshot ?? Array.Empty<ControlBindingSnapshot>(),
            VerifiedBoundCount = verifiedBoundCount ?? boundControls.Count,
            TotalDeclarationCount = boundControls.Count + skippedControls.Count
        };
    }
}