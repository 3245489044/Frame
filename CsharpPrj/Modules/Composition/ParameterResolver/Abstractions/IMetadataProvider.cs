
namespace ParameterResolver.Abstractions;

public interface IMetadataProvider<TSelf> where TSelf : IMetadataProvider<TSelf>
{
    static abstract IReadOnlyList<ColumnDefinition<TSelf>> GetColumns();
}

public readonly record struct ColumnDefinition<TSource>(
    string Header,
    Func<TSource, object?> ValueExtractor,
    string? FormatExpression = null,
    bool SkipIfNull = true
);