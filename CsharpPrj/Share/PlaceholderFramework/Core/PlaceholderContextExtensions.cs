using Graphkey.Core;
using SkeletonFlow;

namespace Common.PlaceholderFramework.Core
{
    public static class PlaceholderContextExtensions
    {
        private const string BatchIdKey = "__Placeholder_BatchId";
        private const string ModuleNameKey = "__Placeholder_ModuleName";

        #region BatchId

        public static string GetBatchId(this SkeletonContext ctx)
        {
            if (ctx.Items.TryGetValue(BatchIdKey, out var value) && value is string batchId)
                return batchId;
            var newBatchId = Guid.NewGuid().ToString("N");
            ctx.SetItem(BatchIdKey, newBatchId);
            return newBatchId;
        }

        public static void SetBatchId(this SkeletonContext ctx, string batchId)
            => ctx.SetItem(BatchIdKey, batchId);

        public static string GetModuleName(this SkeletonContext ctx)
            => ctx.Items.TryGetValue(ModuleNameKey, out var v) && v is string name ? name : "Unknown";

        public static void SetModuleName(this SkeletonContext ctx, string moduleName)
            => ctx.SetItem(ModuleNameKey, moduleName);

        #endregion

        #region Placeholder 批量操作

        public static void SetPlaceholders(this SkeletonContext ctx, IDictionary<string, Placeholder> placeholders)
        {
            foreach (var kvp in placeholders)
            {
                ctx.SetItem(kvp.Key, kvp.Value);
            }
        }

        public static IEnumerable<KeyValuePair<string, Placeholder>> GetAllPlaceholders(this SkeletonContext ctx)
        {
            return ctx.Items
                .Where(kv => kv.Value is Placeholder)
                .Select(kv => new KeyValuePair<string, Placeholder>(kv.Key, (Placeholder)kv.Value));
        }

        public static int GetPlaceholderCount(this SkeletonContext ctx)
            => ctx.Items.Values.OfType<Placeholder>().Count();

        public static bool TryGetPlaceholder(this SkeletonContext ctx, string key, out Placeholder placeholder)
        {
            if (ctx.Items.TryGetValue(key, out var value) && value is Placeholder ph)
            {
                placeholder = ph;
                return true;
            }
            placeholder = null;
            return false;
        }

        public static bool RemovePlaceholder(this SkeletonContext ctx, string key)
            => ctx.RemoveItem(key);

        public static void ClearPlaceholders(this SkeletonContext ctx)
        {
            var keys = ctx.Items.Where(kv => kv.Value is Placeholder).Select(kv => kv.Key).ToList();
            foreach (var key in keys)
                ctx.RemoveItem(key);
        }

        #endregion

        #region 便捷设置方法

        public static void SetInputPlaceholder(this SkeletonContext ctx, string key, string inputKey)
            => ctx.SetItem(key, Placeholder.Input(inputKey));

        public static void SetPrevPlaceholder(this SkeletonContext ctx, string key, string prevKey = null, int? index = null)
        {
            var ph = prevKey != null
                ? Placeholder.PrevByKey(prevKey)
                : index.HasValue
                    ? Placeholder.PrevByIndex(index.Value)
                    : Placeholder.Prev();
            ctx.SetItem(key, ph);
        }

        public static void SetNodeRefPlaceholder(this SkeletonContext ctx, string key, string nodeId, string outputKey = null, int? index = null)
        {
            var ph = outputKey != null
                ? Placeholder.From(nodeId, outputKey)
                : index.HasValue
                    ? Placeholder.From(nodeId, index.Value)
                    : Placeholder.From(nodeId);
            ctx.SetItem(key, ph);
        }

        public static void SetNodeIndexPlaceholder(this SkeletonContext ctx, string key, int nodeIndex)
            => ctx.SetItem(key, Placeholder.Node(nodeIndex));

        #endregion
    }
}
