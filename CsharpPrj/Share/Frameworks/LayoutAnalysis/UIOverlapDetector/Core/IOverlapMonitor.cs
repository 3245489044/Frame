using Modules.LayoutAnalysis.UIInspector;
using Modules.LayoutAnalysis.UIOverlapDetector.Models;

namespace Modules.LayoutAnalysis.UIOverlapDetector.Core;

public class OverlapDetectedEventArgs : EventArgs
{
    public OverlapAnalysisResult Result { get; }

    public string Source { get; }

    public OverlapDetectedEventArgs(OverlapAnalysisResult result, string source)
    {
        Result = result;
        Source = source;
    }
}

public interface IOverlapMonitor : IDisposable
{
    void Attach(object form, ControlTreeWalker walker);

    void Detach();

    OverlapAnalysisResult CheckNow();

    event EventHandler<OverlapDetectedEventArgs>? OverlapDetected;

    bool IsAttached { get; }
}
