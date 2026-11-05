
namespace Share.LogicComposition.Models
{
    public class CompositionOptions
    {
        public DuplicateHandling DuplicateHandling { get; set; } = DuplicateHandling.Skip;

        public bool ValidateBeforeMerge { get; set; } = true;

        public bool TrackConflicts { get; set; } = true;

        public Func<object, string?>? KeyExtractor { get; set; }

        public static readonly CompositionOptions Default = new();

        public static readonly CompositionOptions Overwrite = new()
        {
            DuplicateHandling = DuplicateHandling.Overwrite
        };

        public static readonly CompositionOptions Skip = new()
        {
            DuplicateHandling = DuplicateHandling.Skip
        };
    }

    public enum DuplicateHandling
    {
        Skip,

        Overwrite,

        KeepBoth,

        Merge
    }
}
