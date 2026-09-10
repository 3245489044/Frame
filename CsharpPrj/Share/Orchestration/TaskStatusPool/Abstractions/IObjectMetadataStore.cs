
#nullable enable

namespace Share.TaskStatusPool.Abstractions
{
    public interface IObjectMetadataStore
    {
        Dictionary<string, object>? GetMetadata(object target);

        void SetMetadata(object target, Dictionary<string, object> metadata);
    }
}
