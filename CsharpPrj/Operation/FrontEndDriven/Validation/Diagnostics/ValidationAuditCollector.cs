
#nullable enable
using System;
using System.Linq;
using FrontEndDriven.Diagnostics;

namespace FrontEndDriven.Validation.Diagnostics;

public static class ValidationAuditCollector
{
    public static void Collect(RenderAuditReport report, ValidationResult validationResult)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (validationResult == null)
            throw new ArgumentNullException(nameof(validationResult));

        report.ValidationSummary = validationResult.GetSummary();

        report.ValidationTotalChecked = validationResult.TotalChecked;
        report.ValidationPassedCount = validationResult.PassedCount;
        report.ValidationErrorCount = validationResult.Errors.Count;
        report.ValidationWarningCount = validationResult.Warnings.Count;
        report.ValidationIsValid = validationResult.IsValid;

        report.ValidationErrorsByCategory = Enum.GetValues<ValidationCategory>()
            .ToDictionary(c => c, c => validationResult.GetErrorsByCategory(c).Count);

        report.ValidationWarningsByCategory = Enum.GetValues<ValidationCategory>()
            .ToDictionary(c => c, c => validationResult.GetWarningsByCategory(c).Count);

        report.ValidationErrorDetails = validationResult.Errors
            .Take(20)
            .Select(e => e.ToString())
            .ToList();

        report.ValidationWarningDetails = validationResult.Warnings
            .Take(20)
            .Select(w => w.ToString())
            .ToList();
    }
}
