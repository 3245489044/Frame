
namespace Modules.Transformers
{
    public class TryTransformer<TInput, TOutput> : ITransformer<TInput, Result<TOutput>>
    {
        private readonly ITransformer<TInput, TOutput> _inner;

        public string Name => $"Try({_inner.Name})";

        public TryTransformer(ITransformer<TInput, TOutput> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Result<TOutput> Transform(TInput input)
        {
            try
            {
                var output = _inner.Transform(input);
                return Result<TOutput>.Success(output);
            }
            catch (Exception ex)
            {
                return Result<TOutput>.Failure(ex);
            }
        }
    }
}
