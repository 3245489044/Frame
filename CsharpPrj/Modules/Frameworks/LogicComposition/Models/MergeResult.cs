
namespace Share.LogicComposition.Models
{
    public class MergeResult<T>
    {
        public T? Result { get; }

        public bool IsSuccess { get; }

        public IReadOnlyList<MergeConflict> Conflicts { get; }

        public string? ErrorMessage { get; }

        public MergeStatistics Statistics { get; }

        private MergeResult(T? result, bool isSuccess,
            IReadOnlyList<MergeConflict>? conflicts,
            string? errorMessage, MergeStatistics? statistics)
        {
            Result = result;
            IsSuccess = isSuccess;
            Conflicts = conflicts ?? Array.Empty<MergeConflict>();
            ErrorMessage = errorMessage;
            Statistics = statistics ?? new MergeStatistics();
        }

        public static MergeResult<T> Success(T result,
            IReadOnlyList<MergeConflict>? conflicts = null,
            MergeStatistics? statistics = null) =>
            new(result, true, conflicts, null, statistics);

        public static MergeResult<T> Failure(string error,
            T? partialResult = default) =>
            new(partialResult, false, null, error, null);
    }

    public class MergeConflict
    {
        public string Key { get; }

        public object? SourceValue { get; }

        public object? TargetValue { get; }

        public string Resolution { get; }

        public MergeConflict(string key, object? sourceValue,
            object? targetValue, string resolution)
        {
            Key = key;
            SourceValue = sourceValue;
            TargetValue = targetValue;
            Resolution = resolution;
        }
    }

    public class MergeStatistics
    {
        public int TotalItems { get; set; }

        public int OverwrittenItems { get; set; }

        public int SkippedItems { get; set; }

        public int AddedItems { get; set; }

        public int ConflictCount => Conflicts.Count;

        private List<MergeConflict> _conflicts = new();

        public IReadOnlyList<MergeConflict> Conflicts => _conflicts;

        public void AddConflict(MergeConflict conflict) => _conflicts.Add(conflict);

        public void AddConflicts(IEnumerable<MergeConflict> conflicts) => _conflicts.AddRange(conflicts);
    }
}
