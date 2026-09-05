
namespace CommandFramework.Models;

public class CommandExecutionContext
{
    public string Name { get; set; } = string.Empty;

    public Dictionary<string, object?>? Parameters { get; set; }

    public object? Data { get; set; }

    public string? Target { get; set; }

    public object? Result { get; set; }

    public string? LastError { get; set; }
}
