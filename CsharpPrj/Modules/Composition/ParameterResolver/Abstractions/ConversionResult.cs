
namespace ParameterResolver.Abstractions;

public class ConversionResult
{
    public bool IsSuccess { get; }

    public object? Value { get; }

    public string? Error { get; }

    private ConversionResult(bool isSuccess, object? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static ConversionResult Success(object? value)
        => new(true, value, null);

    public static ConversionResult Failure(string error)
        => new(false, null, error);
}
