
namespace Share.EventBindingOrchestrator.Abstractions
{
    public interface IControlMetadataStore
    {
        Dictionary<string, object>? GetMetadata(object control);

        void SetMetadata(object control, Dictionary<string, object> metadata);
    }
}

