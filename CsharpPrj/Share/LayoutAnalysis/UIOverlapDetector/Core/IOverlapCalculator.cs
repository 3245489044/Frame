using Modules.LayoutAnalysis.UIInspector;
using Modules.LayoutAnalysis.UIOverlapDetector.Models;

namespace Modules.LayoutAnalysis.UIOverlapDetector.Core;

public interface IOverlapCalculator
{
    OverlapAnalysisResult Calculate(IReadOnlyList<ControlMetadata> controls);

    OverlapAnalysisResult Calculate(IReadOnlyList<ControlMetadata> controls, 
                                    bool includeParentChild = false,
                                    double minOverlapThreshold = 0.0);
}
