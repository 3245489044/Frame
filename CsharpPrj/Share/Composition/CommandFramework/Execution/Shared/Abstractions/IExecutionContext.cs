
namespace CommandFramework.Abstractions
{
  
    public interface IParsedCommand
    {
        string? RawCommand { get; }

        string? Verb { get; }

        string? Action { get; }

        string? Target { get; }

        IReadOnlyDictionary<string, object>? Parameters { get; }

        bool IsNonEventCommand { get; }

        bool IsComment { get; }

        bool IsSuccess { get; }

        string? ErrorMessage { get; }
    }
}
