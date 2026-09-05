
namespace CommandFramework.Abstractions;

public class ControlFinderOptions
{
    public int MaxDepth { get; set; } = 10;

    public bool EnableDiagnostics { get; set; } = false;

    public Action<string>? DiagnosticOutput { get; set; }

    public bool SearchOpenForms { get; set; } = true;

    public bool EnableControlIndex { get; set; } = false;
}
