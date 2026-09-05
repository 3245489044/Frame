#nullable enable

namespace Common.DiscreteChange
{
    public class ChangeSummary
    {
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Modified { get; set; }
        public int Unchanged { get; set; }
        public int Total => Added + Removed + Modified + Unchanged;
    }
}
