#nullable enable

using System;
using System.Collections.Generic;

namespace Common.DiscreteChange
{
    public class StateSnapshot<T>
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<T> Data { get; set; } = new();
    }
}
