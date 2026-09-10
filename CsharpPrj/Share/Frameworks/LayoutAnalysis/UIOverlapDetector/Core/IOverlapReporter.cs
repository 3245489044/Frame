using Modules.LayoutAnalysis.UIOverlapDetector.Models;

namespace Modules.LayoutAnalysis.UIOverlapDetector.Core;

public interface IOverlapReporter
{
    void Report(OverlapAnalysisResult result);

    string FormatReport(OverlapAnalysisResult result);
}
