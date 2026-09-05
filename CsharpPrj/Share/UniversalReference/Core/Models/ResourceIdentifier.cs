namespace UniversalWorkflowFramework.Core.Models
{
    public class ResourceIdentifier
    {
        /// <summary>资源键拼接分隔符（单一事实源）。</summary>
        public const string UniqueKeySeparator = ":";

        public string? ResourceType { get; set; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Location { get; set; }

        public ResourceIdentifier()
        {
            ResourceType = string.Empty;
            Id = string.Empty;
        }

        public string GetUniqueKey() => $"{ResourceType}{UniqueKeySeparator}{Id}";
    }
}
