
namespace Share.LogicComposition.Models
{
    public class InsertPosition
    {
        public InsertMode Mode { get; }

        public string? TargetKey { get; }

        public int? Index { get; }

        public static InsertPosition End() =>
            new(InsertMode.End, null, null);

        public static InsertPosition Start() =>
            new(InsertMode.Start, null, null);

        public static InsertPosition After(string elementKey) =>
            new(InsertMode.After, elementKey, null);

        public static InsertPosition Before(string elementKey) =>
            new(InsertMode.Before, elementKey, null);

        public static InsertPosition AtIndex(int index) =>
            new(InsertMode.AtIndex, null, index);

        private InsertPosition(InsertMode mode, string? targetKey, int? index)
        {
            Mode = mode;
            TargetKey = targetKey;
            Index = index;
        }
    }

    public enum InsertMode
    {
        End,

        Start,

        After,

        Before,

        AtIndex
    }
}
