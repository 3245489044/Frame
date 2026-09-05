#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Common.DiscreteChange
{
    public class DiscreteChangeCalculator<T> : IDiscreteChangeCalculator<T>
    {
        private readonly IEqualityComparer<T> _comparer;

        public DiscreteChangeCalculator() : this(EqualityComparer<T>.Default) { }
        public DiscreteChangeCalculator(IEqualityComparer<T> comparer) => _comparer = comparer;

        public ChangeVector<T> CalculateSetDifference(IEnumerable<T> before, IEnumerable<T> after)
        {
            var beforeSet = new HashSet<T>(before, _comparer);
            var afterSet = new HashSet<T>(after, _comparer);
            return new ChangeVector<T>
            {
                Added = afterSet.Except(beforeSet).ToList(),
                Removed = beforeSet.Except(afterSet).ToList(),
                Unchanged = beforeSet.Intersect(afterSet).ToList()
            };
        }

        public List<T> CalculateSetIntersection(IEnumerable<T> set1, IEnumerable<T> set2)
        {
            var set1Hash = new HashSet<T>(set1, _comparer);
            return set2.Where(set1Hash.Contains).ToList();
        }

        public List<T> CalculateSetUnion(IEnumerable<T> set1, IEnumerable<T> set2)
        {
            var union = new HashSet<T>(set1, _comparer);
            foreach (var item in set2) union.Add(item);
            return union.ToList();
        }

        public List<ModifiedItem<T>> CalculateModifiedItems(IEnumerable<T> before, IEnumerable<T> after, Func<T, T, bool> isModified)
        {
            var beforeDict = before.ToDictionary(item => item, _comparer);
            var afterDict = after.ToDictionary(item => item, _comparer);
            var result = new List<ModifiedItem<T>>();
            foreach (var key in beforeDict.Keys.Intersect(afterDict.Keys))
            {
                if (isModified(beforeDict[key], afterDict[key]))
                    result.Add(new ModifiedItem<T> { OldValue = beforeDict[key], NewValue = afterDict[key] });
            }
            return result;
        }

        public ChangeVector<T> CompareSnapshots(StateSnapshot<T> before, StateSnapshot<T> after, Func<T, T, bool>? isModified = null)
        {
            var vector = CalculateSetDifference(before.Data, after.Data);
            if (isModified != null)
                vector.Modified = CalculateModifiedItems(before.Data, after.Data, isModified);
            return vector;
        }

        public ChangeReport<T> GenerateChangeReport(StateSnapshot<T> before, StateSnapshot<T> after, Func<T, T, bool>? isModified = null)
        {
            var changes = CompareSnapshots(before, after, isModified);
            return new ChangeReport<T>
            {
                FromTimestamp = before.Timestamp,
                ToTimestamp = after.Timestamp,
                ChangeVector = changes,
                Summary = new ChangeSummary
                {
                    Added = changes.Added.Count,
                    Removed = changes.Removed.Count,
                    Modified = changes.Modified?.Count ?? 0,
                    Unchanged = changes.Unchanged.Count
                }
            };
        }

        public ChangeReport<T> GenerateChangeReport(IEnumerable<T> before, IEnumerable<T> after, Func<T, T, bool>? isModified = null)
        {
            return GenerateChangeReport(
                new StateSnapshot<T> { Timestamp = DateTime.MinValue, Data = before.ToList() },
                new StateSnapshot<T> { Timestamp = DateTime.UtcNow, Data = after.ToList() },
                isModified);
        }

        public StateSnapshot<T> CreateSnapshot(IEnumerable<T> data) =>
            new StateSnapshot<T> { Timestamp = DateTime.UtcNow, Data = data.ToList() };
    }
}
