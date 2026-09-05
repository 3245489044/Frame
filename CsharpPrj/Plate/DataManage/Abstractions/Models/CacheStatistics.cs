#nullable enable

namespace DataManage.Abstractions.Models
{
    public class CacheStatistics
    {
        public long Hits { get; set; }

        public long Misses { get; set; }

        public long Evictions { get; set; }

        public long CurrentSize { get; set; }

        public long MaxSize { get; set; }

        public double HitRate => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0;

        public double MemoryUsageBytes { get; set; }
    }
}