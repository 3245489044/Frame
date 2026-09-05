#nullable enable

namespace Modules.LayoutAnalysis.UIInspector
{
    public class ValidationResult
    {
        public bool IsValid { get; }
        public string Message { get; }
        public string PropertyName { get; }
        public object? ExpectedValue { get; }
        public object? ActualValue { get; }

        public ValidationResult(bool isValid, string propertyName, string message)
            : this(isValid, propertyName, message, null, null) { }

        public ValidationResult(bool isValid, string propertyName, string message, object? expectedValue, object? actualValue)
        {
            IsValid = isValid;
            PropertyName = propertyName;
            Message = message;
            ExpectedValue = expectedValue;
            ActualValue = actualValue;
        }

        public static ValidationResult Success(string propertyName)
        {
            return new ValidationResult(true, propertyName, LayoutAnalysisConfig.ReportTemplates.ValidationSuccess);
        }

        public static ValidationResult Failure(string propertyName, string message)
        {
            return new ValidationResult(false, propertyName, message);
        }

        public static ValidationResult Failure(string propertyName, object expected, object actual)
        {
            return new ValidationResult(false, propertyName, 
                string.Format(LayoutAnalysisConfig.ReportTemplates.ValidationMismatch, expected, actual), expected, actual);
        }
    }
}
