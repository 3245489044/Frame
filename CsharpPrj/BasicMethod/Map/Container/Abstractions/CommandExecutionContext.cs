
namespace Container.Abstractions;

public class CommandExecutionContext
{
    public string Action { get; set; } = "";

    public string Target { get; set; } = "";

    public string? Value { get; set; }

    public string CommandText { get; set; } = "";

    public IRuntimeContext? RuntimeContext { get; set; }

    public bool Success { get; set; }

    public string? ResultValue { get; set; }

    public string? ErrorMessage { get; set; }
}
