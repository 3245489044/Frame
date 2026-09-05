using Modules.LayoutAnalysis.UIOverlapDetector.Core;
using Modules.LayoutAnalysis.UIOverlapDetector.Models;

namespace Modules.LayoutAnalysis.UIOverlapDetector.Reporter;

public class ConsoleOverlapReporter : IOverlapReporter
{
    private readonly Action<string>? _outputAction;

    public ConsoleOverlapReporter(Action<string>? outputAction = null)
    {
        _outputAction = outputAction;
    }

    public void Report(OverlapAnalysisResult result)
    {
        var report = FormatReport(result);
        WriteLine(report);
    }

    public string FormatReport(OverlapAnalysisResult result)
    {
        return result.FormatSummary();
    }

    private void WriteLine(string message)
    {
        if (_outputAction != null)
        {
            _outputAction(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }
}
