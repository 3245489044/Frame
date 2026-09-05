
#nullable enable

namespace UniversalMiddleware.Conversion.Components;

public class SplitContainerComponent
{
    public string Orientation { get; set; } = "horizontal";

    public int SplitterDistance { get; set; } = -1;

    public bool IsFixed { get; set; } = false;
}
