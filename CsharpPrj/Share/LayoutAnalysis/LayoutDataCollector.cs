#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Modules.LayoutAnalysis.Formatting;
using Modules.LayoutAnalysis.Models;

namespace Modules.LayoutAnalysis
{
    public class LayoutDataCollector
    {
        private readonly DataNormalizer _normalizer;
        private readonly RelationCalculator _calculator;
        private readonly DataOrganizer _organizer;
        private readonly LayoutFormatterManager _formatterManager;
        private readonly LayoutGraphBuilder _graphBuilder;

        public LayoutDataCollector()
            : this(new DataNormalizer(), new RelationCalculator(), new DataOrganizer())
        {
        }

        public LayoutDataCollector(
            DataNormalizer normalizer,
            RelationCalculator calculator,
            DataOrganizer organizer)
        {
            _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _organizer = organizer ?? throw new ArgumentNullException(nameof(organizer));
            _formatterManager = new LayoutFormatterManager();
            _graphBuilder = new LayoutGraphBuilder();
        }

        public LayoutCollectionResult Collect(List<ElementPosition> rawEntities, Rectangle referenceBounds,
            Func<NormalizedEntity, string?>? parentResolver = null)
        {
            var result = new LayoutCollectionResult();

            result.RawEntities = rawEntities;

            var normalized = _normalizer.NormalizePositions(rawEntities, referenceBounds);

            foreach (var entity in normalized)
            {
                entity.Tags = _normalizer.GenerateSemanticTags(entity);
            }
            result.NormalizedEntities = normalized;

            var normalizedPositions = normalized.Select(e => e.AbsolutePosition).ToList();
            result.SpatialRelations = _calculator.CalculateSpatialRelations(normalizedPositions);

            result.HierarchyRelations = _calculator.CalculateHierarchyRelations(
                normalized,
                e => e.Id,
                parentResolver ?? ResolveParentFromProperties);

            result.SemanticRelations = _calculator.InferSemanticRelations(
                normalized.Select(e => new ElementSnapshot
                {
                    Id = e.Id,
                    Name = e.Id,
                    Position = e.AbsolutePosition,
                    Type = LayoutAnalysisConfig.DefaultValues.DefaultType
                }).ToList());

            result.GraphContext = _graphBuilder.Build(result);

            return result;
        }

        private static string? ResolveParentFromProperties(NormalizedEntity e)
        {
            if (e.Properties.TryGetValue(LayoutAnalysisConfig.PropertyNames.ParentName, out var parentProp) &&
                parentProp?.NormalizedValue is string pn && !string.IsNullOrEmpty(pn))
                return pn;
            if (e.Properties.TryGetValue(LayoutAnalysisConfig.PropertyNames.ParentId, out var idProp) &&
                idProp?.NormalizedValue is string pi && !string.IsNullOrEmpty(pi))
                return pi;
            return null;
        }

        public LayoutGraphContext CollectGraph(List<ElementPosition> rawEntities, Rectangle referenceBounds)
        {
            return Collect(rawEntities, referenceBounds).GraphContext ?? new LayoutGraphContext();
        }

        public string Export(LayoutCollectionResult result, string format = "json")
        {
            return _formatterManager.FormatWithRelations(
                result.NormalizedEntities,
                result.SpatialRelations,
                result.HierarchyRelations,
                format);
        }

        public string ExportGraph(LayoutCollectionResult result, string format = "text")
        {
            ArgumentNullException.ThrowIfNull(result);

            if (result.GraphContext == null)
            {
                result.GraphContext = _graphBuilder.Build(result);
            }

            return format.Equals(LayoutAnalysisConfig.FormatterNames.Json, StringComparison.OrdinalIgnoreCase)
                ? LayoutGraphFormatter.GenerateJson(result.GraphContext)
                : LayoutGraphFormatter.GenerateSummary(result.GraphContext);
        }
    }
}
