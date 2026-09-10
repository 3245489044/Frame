#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Modules.LayoutAnalysis.UIInspector
{
    public class LayoutReportGenerator
    {
        public string GenerateTextReport(IEnumerable<ControlMetadata> controls, string formTitle, int formWidth, int formHeight)
        {
            var report = new StringBuilder();
            report.AppendLine(LayoutAnalysisConfig.ReportTemplates.TextReportTitle);
            report.AppendLine(string.Format(LayoutAnalysisConfig.ReportTemplates.TextReportForm, formTitle, formWidth, formHeight));
            report.AppendLine(string.Format(LayoutAnalysisConfig.ReportTemplates.TextReportCount, controls.Count()));
            report.AppendLine(LayoutAnalysisConfig.ReportTemplates.TextReportSeparator);

            foreach (var ctrl in controls.OrderBy(c => c.Top).ThenBy(c => c.Left))
            {
                report.AppendLine(string.Format(LayoutAnalysisConfig.ReportTemplates.TextReportItem,
                    ctrl.TypeName, ctrl.Name, ctrl.Left, ctrl.Top, ctrl.Width, ctrl.Height, ctrl.Text));
            }

            report.AppendLine(LayoutAnalysisConfig.ReportTemplates.TextReportSeparator);
            return report.ToString();
        }

        public string GenerateJsonReport(IEnumerable<ControlMetadata> controls)
        {
            return JsonSerializer.Serialize(controls, new JsonSerializerOptions { WriteIndented = true });
        }

        public string GenerateSummaryReport(List<ValidationResult> validationResults)
        {
            var report = new StringBuilder();
            report.AppendLine(LayoutAnalysisConfig.ReportTemplates.ValidationReportTitle);

            var passed = validationResults.Count(r => r.IsValid);
            var failed = validationResults.Count(r => !r.IsValid);

            report.AppendLine(string.Format(LayoutAnalysisConfig.ReportTemplates.ValidationPassed, passed));
            report.AppendLine(string.Format(LayoutAnalysisConfig.ReportTemplates.ValidationFailed, failed));

            if (failed > 0)
            {
                report.AppendLine(LayoutAnalysisConfig.ReportTemplates.TextReportSeparator);
                report.AppendLine(LayoutAnalysisConfig.ReportTemplates.ValidationFailedDetail);
                foreach (var result in validationResults.Where(r => !r.IsValid))
                {
                    report.AppendLine(string.Format(LayoutAnalysisConfig.ReportTemplates.ValidationFailedItem, result.PropertyName, result.Message));
                }
            }

            return report.ToString();
        }
    }
}
