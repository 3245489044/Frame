
namespace Modules.Transformers
{
    public class ConditionalTransformer<TInput, TOutput> : ITransformer<TInput, TOutput>
    {
        private readonly Func<TInput, bool> _condition;
        private readonly ITransformer<TInput, TOutput> _trueBranch;
        private readonly ITransformer<TInput, TOutput>? _falseBranch;

        public string Name { get; }

        public ConditionalTransformer(
            string name,
            Func<TInput, bool> condition,
            ITransformer<TInput, TOutput> trueBranch,
            ITransformer<TInput, TOutput>? falseBranch = null)
        {
            Name = name;
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            _trueBranch = trueBranch ?? throw new ArgumentNullException(nameof(trueBranch));
            _falseBranch = falseBranch;
        }

        public TOutput Transform(TInput input)
        {
            if (_condition(input))
                return _trueBranch.Transform(input);

            if (_falseBranch != null)
                return _falseBranch.Transform(input);

            throw new InvalidOperationException(
                $"ConditionalTransformer '{Name}': condition is false and no falseBranch provided.");
        }
    }
}
