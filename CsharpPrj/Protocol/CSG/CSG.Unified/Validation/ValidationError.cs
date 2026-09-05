namespace CSG.Unified;

public record ValidationError(
    CsgErrorCode Code,
    string Rule,
    string Message,
    string? SourceId = null,
    string? TargetId = null);
