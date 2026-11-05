using Modules.Parsing.Parsing;

namespace Modules.Parsing.Models;

public class MetaCommand
{
    public const string CurrentVersion = "3.0";

    public string Version { get; set; } = CurrentVersion;
    public MetaVerb Verb { get; set; }
    public string? Target { get; set; }
    public string? Action { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
    public string? OriginalCommand { get; set; }
    public Guid CommandId { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ValidationResult ValidateCommand()
    {
        var errors = new List<ValidationError>();

        if (Verb == MetaVerb.Do && string.IsNullOrWhiteSpace(Action))
            errors.Add(new ValidationError("Action", "Action is required for DO verb"));

        if (Verb != MetaVerb.Do && Verb != MetaVerb.Wait && Verb != MetaVerb.Assert && string.IsNullOrWhiteSpace(Target))
            errors.Add(new ValidationError("Target", "Target is required for this verb"));

        if (Verb == MetaVerb.Wait && string.IsNullOrWhiteSpace(Target)
            && (Parameters == null || !Parameters.ContainsKey("condition")))
            errors.Add(new ValidationError("Target", "Condition (Target or Parameters['condition']) is required for WAIT verb"));

        if (Verb == MetaVerb.Assert && string.IsNullOrWhiteSpace(Target) && (Parameters == null || Parameters.Count == 0))
            errors.Add(new ValidationError("Parameters", "Either Target or Parameters are required for ASSERT verb"));

        return new ValidationResult(errors);
    }

    public string ToDebugString()
    {
        return $"CommandId: {CommandId}, Verb: {Verb}, Action: {Action}, Target: {Target}, Parameters: {Parameters?.Count ?? 0}";
    }
}
