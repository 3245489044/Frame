
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.Declarative.Abstractions;

namespace BasicMethod.Declarative.Relations
{
    public class Join : RelationBase
    {
        private readonly IRelation _left;
        private readonly IRelation _right;
        private readonly Func<IReadOnlyDictionary<string, object?>,
            IReadOnlyDictionary<string, object?>, bool>? _condition;

        public override IEnumerable<IReadOnlyDictionary<string, object?>> Tuples
        {
            get
            {
                var rightList = _right.Tuples.ToList();

                foreach (var leftTuple in _left.Tuples)
                {
                    foreach (var rightTuple in rightList)
                    {
                        if (_condition == null || _condition(leftTuple, rightTuple))
                        {
                            var merged = new Dictionary<string, object?>();

                            foreach (var kvp in leftTuple)
                                merged[kvp.Key] = kvp.Value;

                            foreach (var kvp in rightTuple)
                            {
                                if (!merged.ContainsKey(kvp.Key))
                                    merged[kvp.Key] = kvp.Value;
                            }

                            yield return merged;
                        }
                    }
                }
            }
        }

        public Join(
            string name,
            IRelation left,
            IRelation right,
            Func<IReadOnlyDictionary<string, object?>,
                IReadOnlyDictionary<string, object?>, bool>? condition = null)
            : base(name, MergeAttributes(left.Attributes, right.Attributes))
        {
            _left = left ?? throw new ArgumentNullException(nameof(left));
            _right = right ?? throw new ArgumentNullException(nameof(right));
            _condition = condition;
        }

        public static Join Natural(string name, IRelation left, IRelation right)
        {
            var commonAttrs = left.Attributes
                .Intersect(right.Attributes, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (commonAttrs.Count == 0)
                return new Join(name, left, right);

            return new Join(name, left, right, (l, r) =>
                commonAttrs.All(attr =>
                    l.TryGetValue(attr, out var lv) &&
                    r.TryGetValue(attr, out var rv) &&
                    Equals(lv, rv)));
        }

        public static Join Equi(
            string name,
            IRelation left,
            IRelation right,
            string leftAttribute,
            string rightAttribute)
        {
            return new Join(name, left, right, (l, r) =>
            {
                if (!l.TryGetValue(leftAttribute, out var lv))
                    return false;
                if (!r.TryGetValue(rightAttribute, out var rv))
                    return false;
                return Equals(lv, rv);
            });
        }

        private static IReadOnlyList<string> MergeAttributes(
            IReadOnlyList<string> left,
            IReadOnlyList<string> right)
        {
            var result = new List<string>(left);
            foreach (var attr in right)
            {
                if (!result.Contains(attr, StringComparer.OrdinalIgnoreCase))
                    result.Add(attr);
            }
            return result.AsReadOnly();
        }
    }
}
