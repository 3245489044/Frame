#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Modules.LayoutAnalysis.UIInspector
{
    public class UIInspector
    {
        private readonly ReflectionExtractor _extractor;
        private readonly ControlTreeWalker _walker;
        private readonly LayoutValidator _validator;
        private readonly LayoutReportGenerator _reportGenerator;

        public UIInspector()
        {
            _extractor = new ReflectionExtractor();
            _walker = new ControlTreeWalker();
            _validator = new LayoutValidator();
            _reportGenerator = new LayoutReportGenerator();
        }

        public List<ControlMetadata> InspectForm(Form form)
        {
            return _walker.Walk(form);
        }

        public List<ControlMetadata> InspectControl(Control control)
        {
            return _walker.Walk(control);
        }

        public string GenerateLayoutReport(Form form)
        {
            var controls = InspectForm(form);
            return _reportGenerator.GenerateTextReport(controls, form.Text, form.Width, form.Height);
        }

        public string GenerateLayoutReport(List<ControlMetadata> controls, string formTitle, int width, int height)
        {
            return _reportGenerator.GenerateTextReport(controls, formTitle, width, height);
        }

        public ValidationResult ValidatePosition(ControlMetadata metadata, int expectedX, int expectedY)
        {
            return _validator.ValidatePosition(metadata, expectedX, expectedY);
        }

        public ValidationResult ValidateSize(ControlMetadata metadata, int expectedWidth, int expectedHeight)
        {
            return _validator.ValidateSize(metadata, expectedWidth, expectedHeight);
        }

        public ValidationResult ValidateText(ControlMetadata metadata, string expectedText)
        {
            return _validator.ValidateText(metadata, expectedText);
        }

        public ValidationResult ValidateControl(ControlMetadata metadata, string propertyName, object expectedValue)
        {
            return propertyName switch
            {
                LayoutAnalysisConfig.PropertyNames.Left => _validator.ValidatePosition(metadata, (int)expectedValue, metadata.Top),
                LayoutAnalysisConfig.PropertyNames.Top => _validator.ValidatePosition(metadata, metadata.Left, (int)expectedValue),
                LayoutAnalysisConfig.PropertyNames.Width => _validator.ValidateWidth(metadata, (int)expectedValue),
                LayoutAnalysisConfig.PropertyNames.Height => _validator.ValidateHeight(metadata, (int)expectedValue),
                LayoutAnalysisConfig.PropertyNames.Text => _validator.ValidateText(metadata, (string)expectedValue),
                _ => ValidationResult.Failure(propertyName, string.Format(LayoutAnalysisConfig.ValidationMessages.UnsupportedProperty, propertyName))
            };
        }

        public ControlMetadata? FindControl(List<ControlMetadata> controls, string name)
        {
            ArgumentNullException.ThrowIfNull(controls);
            if (string.IsNullOrEmpty(name)) return null;

            var index = LayoutTreeTraversal.CreateIndex(controls);
            return index.TryGet(name, out var found) ? found : null;
        }

        public List<ControlMetadata> FindControlsByType(List<ControlMetadata> controls, string typeName)
            => LayoutTreeTraversal.FindByType(controls, typeName);

        public Dictionary<string, object?> ExtractAllProperties(object target)
        {
            return _extractor.ExtractAllProperties(target);
        }
    }
}
