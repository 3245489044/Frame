using Graphkey.Core;

namespace Common.PlaceholderFramework.Core
{
    public class PlaceholderMetadata
    {
        public string Kind { get; set; }

        public string Key { get; set; }

        public int? Index { get; set; }

        public string NodeId { get; set; }

        public int? NodeIndex { get; set; }

        public string ResolvedValue { get; set; }

        public string SourceModule { get; set; }

        public string SourceMember { get; set; }

        public DateTime Timestamp { get; set; }

        public string BatchId { get; set; }

        public string MetadataId { get; set; }

        public string HandlerName { get; set; }

        [Obsolete("此属性当前未被使用，将在未来版本中移除。请使用 MetadataId 关联外部扩展数据。")]
        public string ExtraData { get; set; }

        public static PlaceholderMetadata FromPlaceholder(
            Placeholder ph,
            string resolvedValue,
            string module = null,
            string member = null,
            string batchId = null,
            string handlerName = null,
            string extraData = null)
            => new()
            {
                Kind = ph.Kind.ToString(),
                Key = ph.Key,
                Index = ph.Index,
                NodeId = ph.NodeId,
                NodeIndex = ph.NodeIndex,
                ResolvedValue = resolvedValue,
                SourceModule = module,
                SourceMember = member,
                Timestamp = DateTime.UtcNow,
                BatchId = batchId,
                MetadataId = Guid.NewGuid().ToString("N"),
                HandlerName = handlerName,
                ExtraData = extraData
            };

        public override string ToString()
            => $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Kind}] Key={Key}, Value={ResolvedValue}, Batch={BatchId}";
    }
}
