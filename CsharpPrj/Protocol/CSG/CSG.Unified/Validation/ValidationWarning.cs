namespace CSG.Unified;

public record ValidationWarning(string Rule, string Message, string? SourceId = null, string? TargetId = null);
