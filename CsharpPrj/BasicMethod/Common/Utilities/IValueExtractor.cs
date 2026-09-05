
namespace Modules.Common.Utilities
{
    public interface IValueExtractor
    {
        string? ExtractValue(object? parameters, string key);

        IReadOnlyDictionary<string, string?> ExtractAllValues(object? parameters);
    }
}
