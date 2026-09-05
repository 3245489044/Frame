
#nullable enable
using System;
using System.Collections.Generic;
using FrontEndDriven.Validation;

namespace FrontEndDriven.Diagnostics;

public class RenderAuditReport
{
    public DateTime Timestamp { get; set; }

    public string PipelineName { get; set; } = string.Empty;

    public bool IsFirstApply { get; set; }

    public int TotalEntities { get; set; }

    public Dictionary<string, int> EntityTypeDistribution { get; set; } = new();

    public int TotalControlsCreated { get; set; }

    public int FailedControls { get; set; }

    public int ControlsInPool { get; set; }

    public int CanonicalNodeCount { get; set; }

    public int NodesWithLayout { get; set; }

    public int NodesWithStyle { get; set; }

    public int NodesWithEvents { get; set; }

    public int AppliedNodeCount { get; set; }

    public int AddedNodes { get; set; }

    public int RemovedNodes { get; set; }

    public int ChangedProperties { get; set; }

    public List<ControlSnapshot> ControlSnapshots { get; set; } = new();

    public string ValidationSummary { get; set; } = string.Empty;

    public int ValidationTotalChecked { get; set; }

    public int ValidationPassedCount { get; set; }

    public int ValidationErrorCount { get; set; }

    public int ValidationWarningCount { get; set; }

    public bool ValidationIsValid { get; set; } = true;

    public Dictionary<ValidationCategory, int> ValidationErrorsByCategory { get; set; } = new();

    public Dictionary<ValidationCategory, int> ValidationWarningsByCategory { get; set; } = new();

    public List<string> ValidationErrorDetails { get; set; } = new();

    public List<string> ValidationWarningDetails { get; set; } = new();
}

public class ControlSnapshot
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int Left { get; set; }

    public int Top { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool Visible { get; set; }

    public bool Enabled { get; set; }
}
