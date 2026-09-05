#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Modules.LayoutAnalysis.Models;
using UniversalLayoutMath;

namespace Modules.LayoutAnalysis
{
    public class RelationCalculator
    {
        public List<SpatialRelation> CalculateSpatialRelations(List<ElementPosition> elements)
        {
            var relations = new List<SpatialRelation>();

            for (int i = 0; i < elements.Count; i++)
            {
                for (int j = i + 1; j < elements.Count; j++)
                {
                    var rel = CalculatePairRelation(elements[i], elements[j]);
                    if (rel != null)
                    {
                        relations.Add(rel);
                    }
                }
            }

            return relations;
        }

        private SpatialRelation? CalculatePairRelation(ElementPosition a, ElementPosition b)
        {
            var rectA = new Rect(a.X, a.Y, a.Width, a.Height);
            var rectB = new Rect(b.X, b.Y, b.Width, b.Height);

            if (OverlapMath.WouldOverlap(rectA, rectB))
            {
                var overlapRect = OverlapMath.CalculateOverlapRect(rectA, rectB);
                var ratio = OverlapMath.CalculateOverlapRatio(rectA, rectB, overlapRect);
                var level = OverlapMath.ClassifyOverlap(ratio);

                return new SpatialRelation
                {
                    From = a.Id,
                    To = b.Id,
                    Type = LayoutAnalysisConfig.RelationDescriptions.Overlap,
                    Description = MapOverlapLevelToDescription(level, ratio)
                };
            }

            var direction = DetermineDirection(rectA, rectB);
            if (!string.IsNullOrEmpty(direction))
            {
                return new SpatialRelation
                {
                    From = a.Id,
                    To = b.Id,
                    Type = LayoutAnalysisConfig.RelationDescriptions.Spatial,
                    Description = direction
                };
            }

            return null;
        }

        private static string MapOverlapLevelToDescription(OverlapLevel level, double ratio)
        {
            return level switch
            {
                OverlapLevel.Complete => LayoutAnalysisConfig.RelationDescriptions.CompleteOverlap,
                OverlapLevel.Severe => string.Format(LayoutAnalysisConfig.FormatStrings.OverlapRatioFormat, LayoutAnalysisConfig.RelationDescriptions.SevereOverlap, ratio),
                OverlapLevel.Moderate => string.Format(LayoutAnalysisConfig.FormatStrings.OverlapRatioFormat, LayoutAnalysisConfig.RelationDescriptions.ModerateOverlap, ratio),
                OverlapLevel.Slight => string.Format(LayoutAnalysisConfig.FormatStrings.OverlapRatioFormat, LayoutAnalysisConfig.RelationDescriptions.SlightOverlap, ratio),
                OverlapLevel.None => LayoutAnalysisConfig.RelationDescriptions.Contact,
                _ => LayoutAnalysisConfig.RelationDescriptions.Contact
            };
        }

        private static string? DetermineDirection(Rect from, Rect to)
        {
            var fromCenterX = from.Left + from.Width / 2;
            var fromCenterY = from.Top + from.Height / 2;
            var toCenterX = to.Left + to.Width / 2;
            var toCenterY = to.Top + to.Height / 2;

            int dx = toCenterX - fromCenterX;
            int dy = toCenterY - fromCenterY;

            int threshold = LayoutAnalysisConfig.NumericConstants.DirectionThreshold;

            bool left = dx < -threshold;
            bool right = dx > threshold;
            bool above = dy < -threshold;
            bool below = dy > threshold;

            if (left && above) return LayoutAnalysisConfig.RelationDescriptions.TopLeft;
            if (left && below) return LayoutAnalysisConfig.RelationDescriptions.BottomLeft;
            if (right && above) return LayoutAnalysisConfig.RelationDescriptions.TopRight;
            if (right && below) return LayoutAnalysisConfig.RelationDescriptions.BottomRight;
            if (left) return LayoutAnalysisConfig.RelationDescriptions.Left;
            if (right) return LayoutAnalysisConfig.RelationDescriptions.Right;
            if (above) return LayoutAnalysisConfig.RelationDescriptions.Top;
            if (below) return LayoutAnalysisConfig.RelationDescriptions.Bottom;

            return null;
        }

        public List<HierarchyRelation> CalculateHierarchyRelations<T>(List<T> elements,
            Func<T, string> getId, Func<T, string?> getParentId)
        {
            var relations = new List<HierarchyRelation>();

            var idToElement = new Dictionary<string, T>();
            foreach (var element in elements)
            {
                var id = getId(element);
                if (!string.IsNullOrEmpty(id) && !idToElement.ContainsKey(id))
                {
                    idToElement[id] = element;
                }
            }

            foreach (var element in elements)
            {
                var parentId = getParentId(element);
                if (!string.IsNullOrEmpty(parentId) && idToElement.TryGetValue(parentId, out var parent))
                {
                    relations.Add(new HierarchyRelation
                    {
                        ChildId = getId(element),
                        ParentId = parentId,
                        ChildType = element.GetType().Name
                    });
                }
            }

            return relations;
        }

        public List<SemanticRelation> InferSemanticRelations(List<ElementSnapshot> elements)
        {
            var relations = new List<SemanticRelation>();

            var nameGroups = elements.GroupBy(e =>
                e.Name?.Split(LayoutAnalysisConfig.Separators.NameSplitChars, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? LayoutAnalysisConfig.DefaultValues.UnknownGroup);

            foreach (var group in nameGroups.Where(g => g.Count() > 1))
            {
                var members = group.ToList();
                for (int i = 0; i < members.Count; i++)
                {
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        relations.Add(new SemanticRelation
                        {
                            From = members[i].Id,
                            To = members[j].Id,
                            RelationType = LayoutAnalysisConfig.RelationDescriptions.Group,
                            Evidence = string.Format(LayoutAnalysisConfig.RelationDescriptions.EvidencePrefix, group.Key)
                        });
                    }
                }
            }

            for (int i = 0; i < elements.Count; i++)
            {
                for (int j = i + 1; j < elements.Count; j++)
                {
                    if (IsNearby(elements[i].Position, elements[j].Position, LayoutAnalysisConfig.NumericConstants.ProximityThreshold))
                    {
                        relations.Add(new SemanticRelation
                        {
                            From = elements[i].Id,
                            To = elements[j].Id,
                            RelationType = LayoutAnalysisConfig.RelationDescriptions.Proximity,
                            Evidence = LayoutAnalysisConfig.RelationDescriptions.EvidenceProximity
                        });
                    }
                }
            }

            return relations;
        }

        private static bool IsNearby(ElementPosition a, ElementPosition b, int threshold)
        {
            var dx = Math.Abs(a.X - b.X);
            var dy = Math.Abs(a.Y - b.Y);
            return dx < threshold && dy < threshold;
        }
    }
}
