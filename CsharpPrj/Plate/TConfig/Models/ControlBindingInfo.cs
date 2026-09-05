
namespace TConfig;

public class ControlBindingInfo
{
    public string ControlName { get; set; } = string.Empty;

    public string ControlType { get; set; } = string.Empty;

    public int BoundEventCount { get; set; }

    public List<string> FunctionNames { get; set; } = new();

    public string Status { get; set; } = "Bound";

    public string? ErrorMessage { get; set; }

    public string? SkipReason { get; set; }
}
