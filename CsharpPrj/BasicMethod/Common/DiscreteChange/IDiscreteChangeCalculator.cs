#nullable enable

using System;
using System.Collections.Generic;

namespace Common.DiscreteChange
{
    public interface IDiscreteChangeCalculator<T>
    {
        ChangeVector<T> CalculateSetDifference(IEnumerable<T> before, IEnumerable<T> after);
        List<T> CalculateSetIntersection(IEnumerable<T> set1, IEnumerable<T> set2);
        List<T> CalculateSetUnion(IEnumerable<T> set1, IEnumerable<T> set2);
        List<ModifiedItem<T>> CalculateModifiedItems(IEnumerable<T> before, IEnumerable<T> after, Func<T, T, bool> isModified);
        ChangeVector<T> CompareSnapshots(StateSnapshot<T> before, StateSnapshot<T> after, Func<T, T, bool>? isModified = null);
        ChangeReport<T> GenerateChangeReport(StateSnapshot<T> before, StateSnapshot<T> after, Func<T, T, bool>? isModified = null);
        ChangeReport<T> GenerateChangeReport(IEnumerable<T> before, IEnumerable<T> after, Func<T, T, bool>? isModified = null);
        StateSnapshot<T> CreateSnapshot(IEnumerable<T> data);
    }
}
