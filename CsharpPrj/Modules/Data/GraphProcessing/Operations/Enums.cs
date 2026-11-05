
#nullable enable

namespace Modules.GraphProcessing.Operations
{
    public enum GraphUpdateType
    {
        Insert,
        Delete,
        Replace,
        Merge,
        Split,
        Redirect,
        Composite
    }

    public enum EdgeDirection
    {
        AfterSource,
        BeforeTarget,
        Between
    }

    public enum MergeStrategy
    {
        Append,
        Prepend,
        Interleave
    }

    public enum InsertPosition
    {
        AfterSource,
        BeforeTarget,
        Between,
        AtStart,
        AtEnd
    }

    public enum MergeNodesStrategy
    {
        CombineOutputs,
        PreserveFirst,
        PreserveLast
    }

    public enum CompositeStrategy
    {
        Sequential,
        Parallel
    }

    public enum SymbolMatchMode
    {
        First,
        Last,
        All,
        Nearest
    }
}
