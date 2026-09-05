
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Declarative.Mappers
{
    public static class MappingExtensions
    {
        public static IDeclarativeMapper<TDecl1, TRuntime2> Then<TDecl1, TRuntime1, TRuntime2>(
            this IDeclarativeMapper<TDecl1, TRuntime1> first,
            IDeclarativeMapper<TRuntime1, TRuntime2> second)
            where TDecl1 : IDeclaration
            where TRuntime1 : IDeclaration
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            return new CompositeMapper<TDecl1, TRuntime1, TRuntime2>(first, second);
        }

        public static IAsyncDeclarativeMapper<TDecl1, TRuntime2> Then<TDecl1, TRuntime1, TRuntime2>(
            this IAsyncDeclarativeMapper<TDecl1, TRuntime1> first,
            IAsyncDeclarativeMapper<TRuntime1, TRuntime2> second)
            where TDecl1 : IDeclaration
            where TRuntime1 : IDeclaration
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            return new AsyncCompositeMapper<TDecl1, TRuntime1, TRuntime2>(first, second);
        }

        public static IAsyncDeclarativeMapper<TDeclaration, TRuntime> ToAsync<TDeclaration, TRuntime>(
            this IDeclarativeMapper<TDeclaration, TRuntime> mapper)
            where TDeclaration : IDeclaration
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));

            return new AsyncMapperWrapper<TDeclaration, TRuntime>(mapper);
        }
    }

    internal class CompositeMapper<TDecl1, TRuntime1, TRuntime2>
        : IDeclarativeMapper<TDecl1, TRuntime2>
        where TDecl1 : IDeclaration
        where TRuntime1 : IDeclaration
    {
        private readonly IDeclarativeMapper<TDecl1, TRuntime1> _first;
        private readonly IDeclarativeMapper<TRuntime1, TRuntime2> _second;

        public string Name => $"{_first.Name} → {_second.Name}";
        public string Description => $"{_first.Description} → {_second.Description}";

        public CompositeMapper(
            IDeclarativeMapper<TDecl1, TRuntime1> first,
            IDeclarativeMapper<TRuntime1, TRuntime2> second)
        {
            _first = first;
            _second = second;
        }

        public TRuntime2 Map(TDecl1 declaration)
        {
            var intermediate = _first.Map(declaration);
            return _second.Map(intermediate);
        }
    }

    internal class AsyncCompositeMapper<TDecl1, TRuntime1, TRuntime2>
        : IAsyncDeclarativeMapper<TDecl1, TRuntime2>
        where TDecl1 : IDeclaration
        where TRuntime1 : IDeclaration
    {
        private readonly IAsyncDeclarativeMapper<TDecl1, TRuntime1> _first;
        private readonly IAsyncDeclarativeMapper<TRuntime1, TRuntime2> _second;

        public string Name => $"{_first.Name} → {_second.Name}";
        public string Description => $"{_first.Description} → {_second.Description}";

        public AsyncCompositeMapper(
            IAsyncDeclarativeMapper<TDecl1, TRuntime1> first,
            IAsyncDeclarativeMapper<TRuntime1, TRuntime2> second)
        {
            _first = first;
            _second = second;
        }

        public async Task<TRuntime2> MapAsync(TDecl1 declaration, CancellationToken cancellationToken = default)
        {
            var intermediate = await _first.MapAsync(declaration, cancellationToken);
            return await _second.MapAsync(intermediate, cancellationToken);
        }
    }

    internal class AsyncMapperWrapper<TDeclaration, TRuntime>
        : IAsyncDeclarativeMapper<TDeclaration, TRuntime>
        where TDeclaration : IDeclaration
    {
        private readonly IDeclarativeMapper<TDeclaration, TRuntime> _inner;

        public string Name => _inner.Name;
        public string Description => _inner.Description;

        public AsyncMapperWrapper(IDeclarativeMapper<TDeclaration, TRuntime> inner)
        {
            _inner = inner;
        }

        public Task<TRuntime> MapAsync(TDeclaration declaration, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_inner.Map(declaration));
        }
    }
}
