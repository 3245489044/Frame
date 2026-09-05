#nullable enable

using System.Collections.Generic;

namespace Common.Models
{
    public class CircularDependency
    {
        public List<string> ModuleIds { get; set; } = new();
        public int Depth { get; set; }
    }
}
