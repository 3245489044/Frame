
namespace TConfig
{
    public interface IEventNameResolver
    {
        ResolvedEventInfo Resolve(string eventName);

        bool IsValidEventName(string eventName);
    }

    public class ResolvedEventInfo
    {
        public string OriginalName { get; set; } = string.Empty;

        public string NormalizedName { get; set; } = string.Empty;

        public string EventType { get; set; } = string.Empty;

        public bool IsValid { get; set; }
    }
}
