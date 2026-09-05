#nullable enable

using System.Collections.Generic;

namespace Common.DiscreteChange
{
    public class ChangeVector<T>
    {
        public List<T> Added { get; set; } = new();
        public List<T> Removed { get; set; } = new();
        public List<T> Unchanged { get; set; } = new();
        public List<ModifiedItem<T>> Modified { get; set; } = new();
    }
}
