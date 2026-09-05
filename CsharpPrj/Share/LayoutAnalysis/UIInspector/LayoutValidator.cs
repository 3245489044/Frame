#nullable enable

using System.Collections.Generic;

namespace Modules.LayoutAnalysis.UIInspector
{
    public class LayoutValidator
    {
        public ValidationResult ValidatePosition(ControlMetadata info, int expectedX, int expectedY)
        {
            bool isValid = info.Left == expectedX && info.Top == expectedY;
            if (isValid)
                return ValidationResult.Success(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixPosition);

            return ValidationResult.Failure(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixPosition,
                string.Format(LayoutAnalysisConfig.ValidationMessages.PositionError, expectedX, expectedY, info.Left, info.Top));
        }

        public ValidationResult ValidateSize(ControlMetadata info, int expectedWidth, int expectedHeight)
        {
            bool isValid = info.Width == expectedWidth && info.Height == expectedHeight;
            if (isValid)
                return ValidationResult.Success(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixSize);

            return ValidationResult.Failure(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixSize,
                string.Format(LayoutAnalysisConfig.ValidationMessages.SizeError, expectedWidth, expectedHeight, info.Width, info.Height));
        }

        public ValidationResult ValidateWidth(ControlMetadata info, int expectedWidth)
        {
            if (info.Width == expectedWidth)
                return ValidationResult.Success(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixWidth);

            return ValidationResult.Failure(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixWidth, expectedWidth, info.Width);
        }

        public ValidationResult ValidateHeight(ControlMetadata info, int expectedHeight)
        {
            if (info.Height == expectedHeight)
                return ValidationResult.Success(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixHeight);

            return ValidationResult.Failure(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixHeight, expectedHeight, info.Height);
        }

        public ValidationResult ValidateText(ControlMetadata info, string expectedText)
        {
            bool isValid = info.Text == expectedText;
            if (isValid)
                return ValidationResult.Success(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixText);

            return ValidationResult.Failure(info.Name + LayoutAnalysisConfig.PropertyNames.SuffixText,
                string.Format(LayoutAnalysisConfig.ValidationMessages.TextError, expectedText, info.Text));
        }

        public ValidationResult ValidateExists(ControlMetadata info, string controlName)
        {
            if (info != null)
                return ValidationResult.Success(controlName);

            return ValidationResult.Failure(controlName, string.Format(LayoutAnalysisConfig.ValidationMessages.NotFound, controlName));
        }
    }
}
