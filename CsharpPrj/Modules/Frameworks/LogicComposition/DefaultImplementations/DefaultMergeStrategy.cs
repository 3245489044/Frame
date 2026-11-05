
using Share.LogicComposition.Abstractions;
using Share.LogicComposition.Models;

namespace Share.LogicComposition.DefaultImplementations
{
    public class DefaultMergeStrategy<T> : IMergeStrategy<T>
        where T : class
    {
        public MergeResult<T> Merge(T source, T target, Models.MergeOptions? options = null)
        {
            try
            {
                var conflicts = new List<MergeConflict>();
                var stats = new MergeStatistics();

                ObjectMerger.MergeWithConflicts(target, source, options, conflicts);

                stats.OverwrittenItems = conflicts.Count;
                stats.TotalItems = conflicts.Count;
                stats.AddConflicts(conflicts);

                return MergeResult<T>.Success(target, conflicts, stats);
            }
            catch (Exception ex)
            {
                return MergeResult<T>.Failure($"合并失败: {ex.Message}", target);
            }
        }
    }
}
