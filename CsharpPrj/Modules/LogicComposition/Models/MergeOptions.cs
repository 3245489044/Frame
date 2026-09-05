
using System.Reflection;

namespace Share.LogicComposition.Models
{
    public class MergeOptions
    {
        public MergeStrategy Strategy { get; set; } = MergeStrategy.SkipNull;

        public bool IncludeFields { get; set; } = true;

        public bool IncludeProperties { get; set; } = true;

        public BindingFlags BindingFlags { get; set; } = BindingFlags.Public | BindingFlags.Instance;

        public HashSet<string>? SkipNames { get; set; }

        public static readonly MergeOptions Default = new();

        public static readonly MergeOptions Overwrite = new() { Strategy = MergeStrategy.Overwrite };

        public static readonly MergeOptions FillDefaults = new() { Strategy = MergeStrategy.FillDefaults };

        public static readonly MergeOptions SkipNullWithEmptyString = new() { Strategy = MergeStrategy.SkipNullWithEmptyString };
    }

    public enum MergeStrategy
    {
        SkipNull,

        Overwrite,

        FillDefaults,

        SkipNullWithEmptyString
    }
}
