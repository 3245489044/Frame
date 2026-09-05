
namespace CommandFramework.Abstractions;

public record struct BindingResult(
    string? FunctionName,
    string? Parameter,
    string? Target);
