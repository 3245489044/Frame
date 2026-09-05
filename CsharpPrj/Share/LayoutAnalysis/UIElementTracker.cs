#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Framework.Tree;
using Modules.LayoutAnalysis.Models;
using Modules.LayoutAnalysis.UIInspector;

namespace Modules.LayoutAnalysis
{
    public class UIElementTracker
    {
        private readonly ReflectionExtractor _extractor;
        private readonly LayoutDataCollector _layoutCollector;
        private readonly LayoutReportGenerator _reportGenerator;
        private readonly LayoutValidator _validator;

        public UIElementTracker()
        {
            _extractor = new ReflectionExtractor();
            _layoutCollector = new LayoutDataCollector();
            _reportGenerator = new LayoutReportGenerator();
            _validator = new LayoutValidator();
        }

        public UIElementTracker(
            ReflectionExtractor? extractor = null,
            LayoutDataCollector? layoutCollector = null,
            LayoutReportGenerator? reportGenerator = null,
            LayoutValidator? validator = null)
        {
            _extractor = extractor ?? new ReflectionExtractor();
            _layoutCollector = layoutCollector ?? new LayoutDataCollector();
            _reportGenerator = reportGenerator ?? new LayoutReportGenerator();
            _validator = validator ?? new LayoutValidator();
        }


        public ControlMetadata? ExtractMetadata(object target, string? parentName = null)
        {
            if (target == null) return null;

            try
            {
                var x = _extractor.Extract<int?>(target, LayoutAnalysisConfig.PropertyNames.Left) ??
                        _extractor.Extract<int?>(target, LayoutAnalysisConfig.PropertyNames.X);
                var y = _extractor.Extract<int?>(target, LayoutAnalysisConfig.PropertyNames.Top) ??
                        _extractor.Extract<int?>(target, LayoutAnalysisConfig.PropertyNames.Y);
                var width = _extractor.Extract<int?>(target, LayoutAnalysisConfig.PropertyNames.Width);
                var height = _extractor.Extract<int?>(target, LayoutAnalysisConfig.PropertyNames.Height);
                var name = _extractor.Extract<string>(target, LayoutAnalysisConfig.PropertyNames.Name) ??
                           _extractor.Extract<string>(target, LayoutAnalysisConfig.PropertyNames.Id) ??
                           target.GetType().Name;
                var text = _extractor.Extract<string>(target, LayoutAnalysisConfig.PropertyNames.Text) ?? string.Empty;
                var hasChildren = _extractor.Extract<bool?>(target, LayoutAnalysisConfig.PropertyNames.HasChildren);

                return new ControlMetadata
                {
                    Name = name,
                    TypeName = target.GetType().Name,
                    Left = x.GetValueOrDefault(),
                    Top = y.GetValueOrDefault(),
                    Width = width.GetValueOrDefault(),
                    Height = height.GetValueOrDefault(),
                    Text = text,
                    ParentName = parentName ?? string.Empty,
                    HasChildren = hasChildren.GetValueOrDefault()
                };
            }
            catch
            {
                return null;
            }
        }

        public Dictionary<string, object?> ExtractAllProperties(object target)
        {
            return _extractor.ExtractAllProperties(target);
        }

        public T? ExtractTypedValue<T>(object target, string path)
        {
            return _extractor.Extract<T>(target, path);
        }


        public List<ControlMetadata> WalkTree(Control root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var walker = new ControlTreeWalker();
            return walker.Walk(root);
        }

        public ControlMetadata? FindByName(List<ControlMetadata> controls, string name)
        {
            ArgumentNullException.ThrowIfNull(controls);
            if (string.IsNullOrEmpty(name)) return null;

            var index = LayoutTreeTraversal.CreateIndex(controls);
            return index.TryGet(name, out var found) ? found : null;
        }

        public List<ControlMetadata> FindByType(List<ControlMetadata> controls, string typeName)
            => LayoutTreeTraversal.FindByType(controls, typeName);


        public LayoutCollectionResult CollectLayout(List<ElementPosition> elements, Rectangle referenceBounds)
        {
            return _layoutCollector.Collect(elements, referenceBounds);
        }

        public LayoutCollectionResult CollectLayoutFromMetadata(List<ControlMetadata> controls, Rectangle referenceBounds)
        {
            var elements = new List<ElementPosition>(controls.Count);
            var nameToLayoutId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var layoutIdToControl = new Dictionary<string, ControlMetadata>(StringComparer.OrdinalIgnoreCase);
            var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in controls)
            {
                var baseName = string.IsNullOrEmpty(c.Name) ? c.TypeName : c.Name;
                string layoutId;
                if (nameCount.TryGetValue(baseName, out var count))
                {
                    nameCount[baseName] = count + 1;
                    layoutId = baseName + "_" + count;
                }
                else
                {
                    nameCount[baseName] = 1;
                    layoutId = baseName;
                }

                if (!string.IsNullOrEmpty(c.Name) && !nameToLayoutId.ContainsKey(c.Name))
                    nameToLayoutId[c.Name] = layoutId;
                if (!nameToLayoutId.ContainsKey(baseName))
                    nameToLayoutId[baseName] = layoutId;

                layoutIdToControl[layoutId] = c;
                elements.Add(new ElementPosition(layoutId, c.Left, c.Top, c.Width, c.Height));
            }

            var result = _layoutCollector.Collect(elements, referenceBounds, entity =>
            {
                if (!layoutIdToControl.TryGetValue(entity.Id, out var metadata))
                    return null;
                if (string.IsNullOrEmpty(metadata.ParentName))
                    return null;
                return nameToLayoutId.TryGetValue(metadata.ParentName, out var parentId) ? parentId : null;
            });

            foreach (var entity in result.NormalizedEntities)
            {
                if (!layoutIdToControl.TryGetValue(entity.Id, out var metadata))
                    continue;
                if (string.IsNullOrEmpty(metadata.ParentName))
                    continue;
                if (!nameToLayoutId.TryGetValue(metadata.ParentName, out var mappedParentId))
                    continue;

                entity.Properties[LayoutAnalysisConfig.PropertyNames.ParentName] = new NormalizedProperty
                {
                    Name = LayoutAnalysisConfig.PropertyNames.ParentName,
                    RawValue = metadata.ParentName,
                    NormalizedValue = mappedParentId,
                    ValueType = LayoutAnalysisConfig.DefaultValues.StringType
                };
            }

            return result;
        }


        public string GenerateReport(List<ControlMetadata> controls, string title, int width, int height)
        {
            return _reportGenerator.GenerateTextReport(controls, title, width, height);
        }

        public string GenerateJsonReport(List<ControlMetadata> controls)
        {
            return _reportGenerator.GenerateJsonReport(controls);
        }

        public string GenerateValidationReport(List<ValidationResult> results)
        {
            return _reportGenerator.GenerateSummaryReport(results);
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


        public string ExportLayout(LayoutCollectionResult result, string format = "json")
        {
            return _layoutCollector.Export(result, format);
        }


        public LayoutGraphContext BuildGraph(LayoutCollectionResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (result.GraphContext == null)
            {
                result.GraphContext = new LayoutGraphBuilder().Build(result);
            }
            return result.GraphContext;
        }

        public string ExportGraph(LayoutCollectionResult result, string format = "text")
        {
            return _layoutCollector.ExportGraph(result, format);
        }
    }
}
