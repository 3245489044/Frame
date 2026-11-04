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

        /// <summary>资源唯一键：<c>类型:Location:Id</c>（Location 缺失时以空串占位，避免不同命名空间同 Id 的资源互撞）。</summary>
        public string GetUniqueKey() => $"{ResourceType}{UniqueKeySeparator}{Location ?? string.Empty}{UniqueKeySeparator}{Id}";
    }
}
