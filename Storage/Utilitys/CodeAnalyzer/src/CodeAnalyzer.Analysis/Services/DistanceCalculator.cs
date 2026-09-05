using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class DistanceCalculator
{
    public static List<ElementDistance> CalculateDistances(List<EnhancedCodeElement> elements, AnalysisConfiguration config)
    {
        if (elements == null || elements.Count < 2) return new List<ElementDistance>();

        var distances = new List<ElementDistance>();
        var pairs = FindRelatedPairs(elements);

        foreach (var pair in pairs)
        {
            var distance = CalculateComprehensiveDistance(pair.Element1, pair.Element2, config);
            if (distance != null) distances.Add(distance);
        }

        return distances;
    }

    private static List<(EnhancedCodeElement Element1, EnhancedCodeElement Element2)> FindRelatedPairs(List<EnhancedCodeElement> elements)
    {
        var pairs = new List<(EnhancedCodeElement, EnhancedCodeElement)>();
        for (int i = 0; i < elements.Count; i++)
        {
            for (int j = i + 1; j < elements.Count; j++)
            {
                if (elements[i].Type == elements[j].Type || AreTypesCompatible(elements[i].Type, elements[j].Type))
                {
                    pairs.Add((elements[i], elements[j]));
                }
            }
        }
        return pairs;
    }

    private static bool AreTypesCompatible(ElementType type1, ElementType type2)
    {
        if ((type1 == ElementType.Method || type1 == ElementType.Property) &&
            (type2 == ElementType.Method || type2 == ElementType.Property))
            return true;
        return type1 == type2;
    }

    private static ElementDistance? CalculateComprehensiveDistance(EnhancedCodeElement e1, EnhancedCodeElement e2, AnalysisConfiguration config)
    {
        double distance = 0;

        double positionDistance = Math.Abs(e1.LineNumber - e2.LineNumber) * config.PositionDistanceWeight;
        distance += positionDistance;

        if (e1.Type != e2.Type)
            distance += config.TypeDifferencePenalty;

        double complexityDiff = Math.Abs(e1.ComplexityScore - e2.ComplexityScore) * 0.1;
        distance += complexityDiff;

        var sharedTypes = e1.UsedTypes.Intersect(e2.UsedTypes).Count();
        distance -= sharedTypes * 0.5;

        var sharedRefs = e1.References.Keys.Intersect(e2.References.Keys).Count();
        distance -= sharedRefs * 0.3;

        double similarity = 1.0 / (1.0 + distance);

        return new ElementDistance(e1.Id, e2.Id, distance, similarity, 0);
    }
}
