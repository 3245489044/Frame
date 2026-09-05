using Framework.Tree;
using Modules.LayoutAnalysis.UIInspector;
using Modules.LayoutAnalysis.UIOverlapDetector.Core;
using Modules.LayoutAnalysis.UIOverlapDetector.Models;
using UniversalLayoutMath;

namespace Modules.LayoutAnalysis.UIOverlapDetector.Calculator;

    public class OverlapCalculator : IOverlapCalculator
    {
        private readonly Func<ControlMetadata, ControlMetadata?>? _entityTreeParentResolver;

        public OverlapCalculator(Func<ControlMetadata, ControlMetadata?>? entityTreeParentResolver = null)
        {
            _entityTreeParentResolver = entityTreeParentResolver;
        }

        public OverlapAnalysisResult Calculate(IReadOnlyList<ControlMetadata> controls)
        {
            return Calculate(controls, includeParentChild: false, minOverlapThreshold: 0.0);
        }

        public OverlapAnalysisResult Calculate(
            IReadOnlyList<ControlMetadata> controls,
            bool includeParentChild = false,
            double minOverlapThreshold = 0.0)
        {
            var result = new OverlapAnalysisResult
            {
                TotalControls = controls.Count,
                AnalyzedAt = DateTime.UtcNow
            };

            if (controls.Count < 2)
                return result;

            var nameToMetadata = LayoutTreeTraversal.BuildNameIndex(controls);

            var pathFinder = new TreePathFinder<ControlMetadata>(c =>
            {
                if (c == null || string.IsNullOrEmpty(c.ParentName))
                    return null;
                return nameToMetadata.TryGetValue(c.ParentName, out var parent) ? parent : null;
            });

            for (int i = 0; i < controls.Count; i++)
            {
                for (int j = i + 1; j < controls.Count; j++)
                {
                    var pair = CalculatePairOverlap(controls[i], controls[j], nameToMetadata, pathFinder);

                    if (pair == null)
                        continue;

                    if (pair.OverlapPercentage <= minOverlapThreshold)
                        continue;

                    result.AllPairs.Add(pair);

                    if (!pair.IsParentChild || includeParentChild)
                    {
                        result.ProblematicPairs.Add(pair);
                    }
                }
            }

            return result;
        }

    private Models.OverlapPair? CalculatePairOverlap(
        ControlMetadata a,
        ControlMetadata b,
        Dictionary<string, ControlMetadata> nameToMetadata,
        TreePathFinder<ControlMetadata> pathFinder)
    {
        var rectA = new Rect(a.ScreenLeft, a.ScreenTop, a.Width, a.Height);
        var rectB = new Rect(b.ScreenLeft, b.ScreenTop, b.Width, b.Height);

        var overlapPair = OverlapMath.DetectOverlap(a.Name, rectA, b.Name, rectB);
        if (overlapPair == null)
            return null;

        var relationChecker = new AncestorRelationChecker(
            _entityTreeParentResolver, nameToMetadata);
        var isParentChild = IsParentChildRelation(a, b, pathFinder, relationChecker);

        return new Models.OverlapPair
        {
            ControlAName = a.Name,
            ControlBName = b.Name,
            ControlAType = a.TypeName,
            ControlBType = b.TypeName,
            ControlABounds = new System.Drawing.Rectangle(a.ScreenLeft, a.ScreenTop, a.Width, a.Height),
            ControlBBounds = new System.Drawing.Rectangle(b.ScreenLeft, b.ScreenTop, b.Width, b.Height),
            Intersection = new System.Drawing.Rectangle(
                overlapPair.OverlapRect.X, overlapPair.OverlapRect.Y,
                overlapPair.OverlapRect.Width, overlapPair.OverlapRect.Height),
            OverlapArea = overlapPair.OverlapArea,
            OverlapPercentage = overlapPair.OverlapRatio,
            Severity = overlapPair.Level,
            IsParentChild = isParentChild,
            ControlAParentName = a.ParentName,
            ControlBParentName = b.ParentName
        };
    }

    private static bool IsParentChildRelation(
        ControlMetadata a,
        ControlMetadata b,
        TreePathFinder<ControlMetadata> pathFinder,
        AncestorRelationChecker relationChecker)
    {
        if (relationChecker.EntityParentResolver != null)
        {
            if (EntityIsAncestorOf(a, b, relationChecker.EntityParentResolver)) return true;
            if (EntityIsAncestorOf(b, a, relationChecker.EntityParentResolver)) return true;
        }

        if (pathFinder.IsDescendantOf(a, b)) return true;
        if (pathFinder.IsDescendantOf(b, a)) return true;
        return false;
    }

    private static bool EntityIsAncestorOf(
        ControlMetadata node,
        ControlMetadata possibleAncestor,
        Func<ControlMetadata, ControlMetadata?> resolver)
    {
        if (node == null || possibleAncestor == null)
            return false;

        var targetName = possibleAncestor.Name;
        if (string.IsNullOrEmpty(targetName))
            return false;

        const int MaxDepth = 100;
        var current = node;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            var parent = resolver(current);
            if (parent == null)
                return false;

            if (string.Equals(parent.Name, targetName, StringComparison.OrdinalIgnoreCase))
                return true;

            current = parent;
        }
        return false;
    }

    private readonly record struct AncestorRelationChecker(
        Func<ControlMetadata, ControlMetadata?>? EntityParentResolver,
        Dictionary<string, ControlMetadata> NameToMetadata)
    {
    }
}
