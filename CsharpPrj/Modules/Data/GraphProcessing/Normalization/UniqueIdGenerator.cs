
#nullable enable

namespace Modules.GraphProcessing.Normalization
{
    public interface IUniqueIdGenerator
    {
        string Generate(string baseId, int chainIndex, Func<string, bool> isNameTaken);

        void Update(string pattern, int maxAttempts);
    }

    public class DefaultUniqueIdGenerator : IUniqueIdGenerator
    {
        private readonly object _syncLock = new();
        private string _pattern;
        private int _maxAttempts;

        public DefaultUniqueIdGenerator(string pattern = "{0}_chain{1}_dup{2}", int maxAttempts = 100)
        {
            if (!pattern.Contains("{0}") || !pattern.Contains("{1}") || !pattern.Contains("{2}"))
                throw new ArgumentException("Pattern must contain {0}, {1}, {2} placeholders.");

            (_pattern, _maxAttempts) = (pattern, maxAttempts);
        }

        public void Update(string pattern, int maxAttempts)
        {
            lock (_syncLock)
            {
                (_pattern, _maxAttempts) = (pattern, maxAttempts);
            }
        }

        public string Generate(string baseId, int chainIndex, Func<string, bool> isNameTaken)
        {
            string pattern;
            int maxAttempts;

            lock (_syncLock)
            {
                (pattern, maxAttempts) = (_pattern, _maxAttempts);
            }

            return GenerateInternal(baseId, chainIndex, isNameTaken, pattern, maxAttempts, "id");
        }

        public string Generate(string baseId, int chainIndex, Func<string, bool> isNameTaken, string renamePattern, int maxDuplicates)
        {
            return GenerateInternal(baseId, chainIndex, isNameTaken, renamePattern, maxDuplicates, "name");
        }

        private static string GenerateInternal(
            string baseId, int chainIndex, Func<string, bool> isNameTaken,
            string pattern, int maxAttempts, string entityType)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                string name = string.Format(pattern, baseId, chainIndex, i);
                if (!isNameTaken(name))
                    return name;
            }

            throw new InvalidOperationException(
                $"Failed to generate unique {entityType} for '{baseId}' after {maxAttempts} attempts.");
        }
    }
}
