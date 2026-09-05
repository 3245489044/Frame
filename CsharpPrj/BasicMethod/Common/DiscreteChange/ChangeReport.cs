#nullable enable

using System;

namespace Common.DiscreteChange
{
    public class ChangeReport<T>
    {
        public DateTime FromTimestamp { get; set; }
        public DateTime ToTimestamp { get; set; }
        public ChangeVector<T> ChangeVector { get; set; } = new();
        public ChangeSummary Summary { get; set; } = new();
    }
}
