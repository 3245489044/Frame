
#nullable enable

namespace Modules.GraphProcessing.Utilities
{
    public interface IChainIdGenerator
    {
        string GenerateId();
    }

    public class DefaultChainIdGenerator : IChainIdGenerator
    {
        public string GenerateId() => Guid.NewGuid().ToString("N");
    }

    public interface INameGenerator
    {
        string GenerateName(string prefix);
    }

    public class DefaultNameGenerator : INameGenerator
    {
        private long _counter;

        public DefaultNameGenerator()
        {
            _counter = DateTime.UtcNow.Ticks;
        }

        public string GenerateName(string prefix)
            => $"{prefix}_{Interlocked.Increment(ref _counter)}";
    }

    public class BaseIdentifierHandler
    {
        private readonly IChainIdGenerator _idGenerator;
        private readonly INameGenerator _nameGenerator;

        public BaseIdentifierHandler(
            IChainIdGenerator? idGenerator = null,
            INameGenerator? nameGenerator = null)
        {
            _idGenerator = idGenerator ?? new DefaultChainIdGenerator();
            _nameGenerator = nameGenerator ?? new DefaultNameGenerator();
        }

        public string GenerateChainId() => _idGenerator.GenerateId();

        public string GenerateName(string prefix) => _nameGenerator.GenerateName(prefix);

        public string NormalizeIdentifier(string identifier)
            => identifier?.Trim() ?? string.Empty;

        public bool IsValidIdentifier(string? identifier)
            => !string.IsNullOrWhiteSpace(identifier);
    }
}
