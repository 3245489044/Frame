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

        // 父链回退只用"全局唯一名"索引：重名项移除，宁可不下"父子"结论，也不误抑制真实重叠（§4.12）。
        var nameToMetadata = BuildUniqueNameIndex(controls);

        for (int i = 0; i < controls.Count; i++)
        {
            for (int j = i + 1; j < controls.Count; j++)
            {
                var pair = CalculatePairOverlap(controls[i], controls[j], nameToMetadata);

                if (pair == null)
                    continue;

                // 阈值语义修正（LayoutAnalysis.md §4.11，原 :55 用 <= 丢掉恰好等于阈值的点）：
                // 先排除零重叠，再以"小于阈值"过滤，等于阈值保留。
                if (pair.OverlapPercentage <= 0)
                    continue;
                if (pair.OverlapPercentage < minOverlapThreshold)
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
        Dictionary<string, ControlMetadata> nameToMetadata)
    {
        // 唯一坐标口径 = form 相对（含 SplitterPanel 修正），见 ControlMetadata.FormLeft/FormTop。
        var rectA = new Rect(a.FormLeft, a.FormTop, a.Width, a.Height);
        var rectB = new Rect(b.FormLeft, b.FormTop, b.Width, b.Height);

        var overlapPair = OverlapMath.DetectOverlap(a.Name, rectA, b.Name, rectB);
        if (overlapPair == null)
            return null;

        var isParentChild = IsParentChildRelation(a, b, nameToMetadata);

        return new Models.OverlapPair
        {
            ControlAName = a.Name,
            ControlBName = b.Name,
            ControlAType = a.TypeName,
            ControlBType = b.TypeName,
            ControlABounds = new System.Drawing.Rectangle(a.FormLeft, a.FormTop, a.Width, a.Height),
            ControlBBounds = new System.Drawing.Rectangle(b.FormLeft, b.FormTop, b.Width, b.Height),
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

    /// <summary>只保留全局唯一名称；同名（多实例）条目全部剔除，避免按名解析到错误实例。</summary>
    private static Dictionary<string, ControlMetadata> BuildUniqueNameIndex(IReadOnlyList<ControlMetadata> controls)
    {
        var byName = new Dictionary<string, ControlMetadata>(StringComparer.OrdinalIgnoreCase);
        var duplicated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in controls)
        {
            if (control == null || string.IsNullOrEmpty(control.Name)) continue;
            if (byName.ContainsKey(control.Name))
            {
                duplicated.Add(control.Name);
                continue;
            }
            byName[control.Name] = control;
        }
        foreach (var name in duplicated) byName.Remove(name);
        return byName;
    }

    private bool IsParentChildRelation(
        ControlMetadata a,
        ControlMetadata b,
        Dictionary<string, ControlMetadata> nameToMetadata)
    {
        // 父子抑制只按"对象身份"判定：同名但不同实例的控件不得因 Name 相等被误抑制（§4.12）。
        if (_entityTreeParentResolver != null)
        {
            if (EntityIsAncestorOf(a, b, _entityTreeParentResolver)) return true;
            if (EntityIsAncestorOf(b, a, _entityTreeParentResolver)) return true;
        }

        if (NameIndexIsAncestorOf(a, b, nameToMetadata)) return true;
        if (NameIndexIsAncestorOf(b, a, nameToMetadata)) return true;
        return false;
    }

    private static bool EntityIsAncestorOf(
        ControlMetadata node,
        ControlMetadata possibleAncestor,
        Func<ControlMetadata, ControlMetadata?> resolver)
    {
        if (node == null || possibleAncestor == null)
            return false;

        const int MaxDepth = 100;
        var current = node;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            var parent = resolver(current);
            if (parent == null)
                return false;

            if (ReferenceEquals(parent, possibleAncestor))
                return true;

            current = parent;
        }
        return false;
    }

    /// <summary>
    /// 名称索引回退：仅当父名唯一命中且命中对象身份恰好等于候选祖先时才认定父子；
    /// 父名重复（同名控件）或命中到无关实例时返回 false，避免把真实重叠误抑制。
    /// </summary>
    private static bool NameIndexIsAncestorOf(
        ControlMetadata node,
        ControlMetadata possibleAncestor,
        Dictionary<string, ControlMetadata> nameToMetadata)
    {
        if (node == null || possibleAncestor == null || ReferenceEquals(node, possibleAncestor))
            return false;

        // 同名不同实例：名称无法建立父子身份，不做抑制。
        if (string.Equals(node.Name, possibleAncestor.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        const int MaxDepth = 100;
        var current = node;
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            var parentName = current.ParentName;
            if (string.IsNullOrEmpty(parentName))
                return false;

            if (!nameToMetadata.TryGetValue(parentName, out var parent) || parent == null)
                return false;

            if (ReferenceEquals(parent, possibleAncestor))
                return true;

            current = parent;
        }
        return false;
    }
}
