#nullable enable

namespace Common.DiscreteChange
{
    public class ModifiedItem<T>
    {
        public T OldValue { get; set; } = default!;
        public T NewValue { get; set; } = default!;
    }
}
