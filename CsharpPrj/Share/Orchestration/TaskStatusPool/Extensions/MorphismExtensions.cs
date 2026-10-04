
#nullable enable
using BasicMethod.Initialization.Guards;
using Share.TaskStatusPool.Abstractions;

namespace Share.TaskStatusPool.Extensions
{
    public static class MorphismExtensions
    {

        public static IMorphism<TIn, TOut> Then<TIn, TMid, TOut>(
            this IMorphism<TIn, TMid> first,
            IMorphism<TMid, TOut> second)
        {
            PortGuard.Required(first, nameof(first));
            PortGuard.Required(second, nameof(second));

            return new CompositeMorphism<TIn, TMid, TOut>(first, second);
        }

        public static IAsyncMorphism<TIn, TOut> Then<TIn, TMid, TOut>(
            this IAsyncMorphism<TIn, TMid> first,
            IAsyncMorphism<TMid, TOut> second)
        {
            PortGuard.Required(first, nameof(first));
            PortGuard.Required(second, nameof(second));

            return new AsyncCompositeMorphism<TIn, TMid, TOut>(first, second);
        }


        public static IAsyncMorphism<TIn, TOut> ToAsync<TIn, TOut>(
            this IMorphism<TIn, TOut> morphism)
        {
            PortGuard.Required(morphism, nameof(morphism));
            return new AsyncAdapterMorphism<TIn, TOut>(morphism);
        }

        public static IOrderedMorphism<TIn, TOut> ToOrdered<TIn, TOut>(
            this IMorphism<TIn, TOut> morphism,
            int order = 0)
        {
            PortGuard.Required(morphism, nameof(morphism));
            return new OrderedMorphismAdapter<TIn, TOut>(morphism, order);
        }


        public static IMorphism<TIn, TOut> FromFunc<TIn, TOut>(
            string name,
            Func<TIn, TOut> func)
        {
            PortGuard.Required(name, nameof(name));
            PortGuard.Required(func, nameof(func));

            return new FuncMorphism<TIn, TOut>(name, func);
        }

        public static IAsyncMorphism<TIn, TOut> FromAsyncFunc<TIn, TOut>(
            string name,
            Func<TIn, Task<TOut>> func)
        {
            PortGuard.Required(name, nameof(name));
            PortGuard.Required(func, nameof(func));

            return new AsyncFuncMorphism<TIn, TOut>(name, func);
        }


        private sealed class CompositeMorphism<TIn, TMid, TOut> : IMorphism<TIn, TOut>
        {
            private readonly IMorphism<TIn, TMid> _first;
            private readonly IMorphism<TMid, TOut> _second;

            public string Name => $"{_first.Name} ∘ {_second.Name}";

            public CompositeMorphism(IMorphism<TIn, TMid> first, IMorphism<TMid, TOut> second)
            {
                _first = first;
                _second = second;
            }

            public TOut Morph(TIn input) => _second.Morph(_first.Morph(input));
        }

        private sealed class AsyncCompositeMorphism<TIn, TMid, TOut> : IAsyncMorphism<TIn, TOut>
        {
            private readonly IAsyncMorphism<TIn, TMid> _first;
            private readonly IAsyncMorphism<TMid, TOut> _second;

            public string Name => $"{_first.Name} ∘ {_second.Name}";

            public AsyncCompositeMorphism(IAsyncMorphism<TIn, TMid> first, IAsyncMorphism<TMid, TOut> second)
            {
                _first = first;
                _second = second;
            }

            public async Task<TOut> MorphAsync(TIn input, CancellationToken cancellationToken = default)
            {
                var mid = await _first.MorphAsync(input, cancellationToken);
                return await _second.MorphAsync(mid, cancellationToken);
            }
        }

        private sealed class AsyncAdapterMorphism<TIn, TOut> : IAsyncMorphism<TIn, TOut>
        {
            private readonly IMorphism<TIn, TOut> _inner;

            public string Name => $"{_inner.Name} (async)";

            public AsyncAdapterMorphism(IMorphism<TIn, TOut> inner) => _inner = inner;

            public Task<TOut> MorphAsync(TIn input, CancellationToken cancellationToken = default)
                => Task.FromResult(_inner.Morph(input));
        }

        private sealed class OrderedMorphismAdapter<TIn, TOut> : IOrderedMorphism<TIn, TOut>
        {
            private readonly IMorphism<TIn, TOut> _inner;

            public string Name => _inner.Name;
            public int Order { get; }

            public OrderedMorphismAdapter(IMorphism<TIn, TOut> inner, int order)
            {
                _inner = inner;
                Order = order;
            }

            public TOut Morph(TIn input) => _inner.Morph(input);
        }

        private sealed class FuncMorphism<TIn, TOut> : IMorphism<TIn, TOut>
        {
            private readonly Func<TIn, TOut> _func;

            public string Name { get; }

            public FuncMorphism(string name, Func<TIn, TOut> func)
            {
                Name = name;
                _func = func;
            }

            public TOut Morph(TIn input) => _func(input);
        }

        private sealed class AsyncFuncMorphism<TIn, TOut> : IAsyncMorphism<TIn, TOut>
        {
            private readonly Func<TIn, Task<TOut>> _func;

            public string Name { get; }

            public AsyncFuncMorphism(string name, Func<TIn, Task<TOut>> func)
            {
                Name = name;
                _func = func;
            }

            public Task<TOut> MorphAsync(TIn input, CancellationToken cancellationToken = default)
                => _func(input);
        }
    }
}
