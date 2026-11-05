
#nullable enable

namespace Modules.GraphProcessing.Normalization
{
    public class DedupeMatch
    {
        public string Source { get; }

        public string Target { get; }

        public double Score { get; }

        public bool IsMatch => Score >= 0.8;

        public DedupeMatch(string source, string target, double score)
        {
            Source = source;
            Target = target;
            Score = score;
        }

        public override string ToString()
            => $"Match: '{Source}' <-> '{Target}' (Score: {Score:F2})";
    }

    public class StringDedupeResult
    {
        public IReadOnlyList<DedupeMatch> Matches { get; }

        public int RemovedCount { get; }

        public IReadOnlyList<string> Log { get; }

        public StringDedupeResult(
            IReadOnlyList<DedupeMatch> matches,
            int removedCount,
            IReadOnlyList<string> log)
        {
            Matches = matches;
            RemovedCount = removedCount;
            Log = log;
        }
    }

    public class SmartDeduplicationEngine
    {
        private readonly double _threshold;

        public SmartDeduplicationEngine(double threshold = 0.8)
        {
            _threshold = Math.Clamp(threshold, 0, 1);
        }

        public StringDedupeResult Execute(IReadOnlyList<string> items)
        {
            var matches = new List<DedupeMatch>();
            var log = new List<string>();
            var removed = new HashSet<int>();

            log.Add($"=== Smart Deduplication Start ===");
            log.Add($"Items: {items.Count}, Threshold: {_threshold}");

            for (int i = 0; i < items.Count; i++)
            {
                if (removed.Contains(i)) continue;

                for (int j = i + 1; j < items.Count; j++)
                {
                    if (removed.Contains(j)) continue;

                    var score = CalculateSimilarity(items[i], items[j]);
                    if (score >= _threshold)
                    {
                        matches.Add(new DedupeMatch(items[i], items[j], score));
                        removed.Add(j);
                        log.Add($"Match found: '{items[i]}' <-> '{items[j]}' (Score: {score:F2})");
                    }
                }
            }

            log.Add($"Total matches: {matches.Count}, Removed: {removed.Count}");
            log.Add($"=== Smart Deduplication Complete ===");

            return new StringDedupeResult(matches.AsReadOnly(), removed.Count, log.AsReadOnly());
        }

        public double CalculateSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return 0;

            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            var distance = LevenshteinDistance(a, b);
            var maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1.0;

            return 1.0 - (double)distance / maxLen;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            var lenA = a.Length;
            var lenB = b.Length;
            var dp = new int[lenA + 1, lenB + 1];

            for (int i = 0; i <= lenA; i++) dp[i, 0] = i;
            for (int j = 0; j <= lenB; j++) dp[0, j] = j;

            for (int i = 1; i <= lenA; i++)
            {
                for (int j = 1; j <= lenB; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }

            return dp[lenA, lenB];
        }
    }
}
