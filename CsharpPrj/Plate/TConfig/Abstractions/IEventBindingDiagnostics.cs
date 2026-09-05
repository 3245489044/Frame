
using System.Collections.Generic;

namespace TConfig
{
    public interface IEventBindingDiagnostics
    {
        int TotalBoundCount { get; }

        int TotalSkippedCount { get; }

        int TotalErrorCount { get; }

        EventBindingContext? LastBindingContext { get; }

        ControlBindingInfo? GetControlBinding(string controlName);

        bool IsControlBound(string controlName);

        IReadOnlyList<ControlBindingInfo> GetBoundControls();

        IReadOnlyList<ControlBindingInfo> GetSkippedControls();

        string GetDiagnosticSummary();

        void CaptureFromContext(EventBindingContext context);

        void Reset();
    }
}
