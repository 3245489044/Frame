
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Plate.JsonMethod.Utilities
{
    public enum JsonArrayMergeMode
    {
        Replace,
        Append,
        Merge,
        Deduplicate
    }

    public static class JsonMergeService
    {
        public static void DeepMerge(JsonObject target, JsonObject source,
            JsonArrayMergeMode arrayMode = JsonArrayMergeMode.Replace)
        {
            DeepMergeCore(target, source, skipKeys: null, arrayMode, trackModified: false);
        }

        public static bool DeepMergeSkipKeys(JsonObject target, JsonObject source,
            HashSet<string>? skipKeys = null,
            JsonArrayMergeMode arrayMode = JsonArrayMergeMode.Deduplicate)
        {
            return DeepMergeCore(target, source, skipKeys, arrayMode, trackModified: true);
        }

        private static bool DeepMergeCore(JsonObject target, JsonObject source,
            HashSet<string>? skipKeys, JsonArrayMergeMode arrayMode, bool trackModified)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (source == null) throw new ArgumentNullException(nameof(source));

            bool modified = false;
            foreach (var kv in source)
            {
                if (skipKeys?.Contains(kv.Key) == true)
                    continue;

                if (kv.Value == null) continue;
                if (kv.Value is JsonObject sourceObj &&
                    target.ContainsKey(kv.Key) && target[kv.Key] is JsonObject targetObj)
                {
                    if (DeepMergeCore(targetObj, sourceObj, skipKeys, arrayMode, trackModified))
                        modified = true;
                }
                else if (kv.Value is JsonArray sourceArr &&
                         target.ContainsKey(kv.Key) && target[kv.Key] is JsonArray targetArr)
                {
                    if (MergeArrays(targetArr, sourceArr, arrayMode))
                        modified = true;
                }
                else
                {
                    target[kv.Key] = kv.Value.DeepClone();
                    modified = true;
                }
            }
            return trackModified ? modified : false;
        }

        public static JsonObject Merge(JsonObject @base, JsonObject @override,
            JsonArrayMergeMode arrayMode = JsonArrayMergeMode.Replace)
        {
            var result = @base.DeepClone().AsObject();
            DeepMerge(result, @override, arrayMode);
            return result;
        }

        public static void ShallowMerge(JsonObject target, JsonObject source)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (source == null) throw new ArgumentNullException(nameof(source));
            foreach (var kv in source)
            {
                if (!target.ContainsKey(kv.Key))
                    target[kv.Key] = kv.Value?.DeepClone();
            }
        }

        public static void ShallowOverwrite(JsonObject target, JsonObject source)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (source == null) throw new ArgumentNullException(nameof(source));
            foreach (var kv in source)
            {
                target[kv.Key] = kv.Value?.DeepClone();
            }
        }

        public static string Merge(string baseJson, string overrideJson,
            JsonArrayMergeMode arrayMode = JsonArrayMergeMode.Replace)
        {
            if (string.IsNullOrEmpty(baseJson)) return overrideJson ?? "{}";
            if (string.IsNullOrEmpty(overrideJson)) return baseJson;
            var baseObj = JsonNode.Parse(baseJson)?.AsObject();
            var overrideObj = JsonNode.Parse(overrideJson)?.AsObject();
            if (baseObj == null) return overrideJson ?? "{}";
            if (overrideObj == null) return baseJson;
            var result = Merge(baseObj, overrideObj, arrayMode);
            return result.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        public static T MergeModels<T>(T baseModel, T overrideModel,
            JsonArrayMergeMode arrayMode = JsonArrayMergeMode.Replace) where T : class, new()
        {
            var options = new JsonSerializerOptions { WriteIndented = false };
            var baseJson = JsonSerializer.SerializeToNode(baseModel, options)!.AsObject();
            var overrideJson = JsonSerializer.SerializeToNode(overrideModel, options)!.AsObject();
            var merged = Merge(baseJson, overrideJson, arrayMode);
            return JsonSerializer.Deserialize<T>(merged.ToJsonString(), options)!;
        }

        private static bool MergeArrays(JsonArray target, JsonArray source, JsonArrayMergeMode arrayMode)
        {
            bool modified = false;
            switch (arrayMode)
            {
                case JsonArrayMergeMode.Replace:
                    target.Clear();
                    foreach (var item in source) target.Add(item?.DeepClone());
                    modified = source.Any();
                    break;

                case JsonArrayMergeMode.Append:
                    foreach (var item in source) target.Add(item?.DeepClone());
                    modified = source.Any();
                    break;

                case JsonArrayMergeMode.Merge:
                    var maxCount = Math.Max(target.Count, source.Count);
                    for (int i = 0; i < maxCount; i++)
                    {
                        if (i < target.Count && i < source.Count)
                        {
                            if (target[i] is JsonObject targetObj && source[i] is JsonObject sourceObj)
                            {
                                if (DeepMergeCore(targetObj, sourceObj, null, arrayMode, true))
                                    modified = true;
                            }
                            else
                            {
                                target[i] = source[i]?.DeepClone();
                                modified = true;
                            }
                        }
                        else if (i < source.Count)
                        {
                            target.Add(source[i]?.DeepClone());
                            modified = true;
                        }
                    }
                    break;

                case JsonArrayMergeMode.Deduplicate:
                    foreach (var srcItem in source)
                    {
                        if (srcItem is JsonObject srcItemObj)
                        {
                            var srcId = GetItemKey(srcItemObj);
                            bool exists;
                            if (srcId != null)
                            {
                                exists = target.Any(dstItem =>
                                    dstItem is JsonObject dstItemObj && GetItemKey(dstItemObj) == srcId);
                            }
                            else
                            {
                                exists = target.Any(dstItem => JsonNode.DeepEquals(dstItem, srcItem));
                            }

                            if (!exists)
                            {
                                target.Add(srcItem.DeepClone());
                                modified = true;
                            }
                        }
                        else
                        {
                            if (!target.Any(d => JsonNode.DeepEquals(d, srcItem)))
                            {
                                target.Add(srcItem?.DeepClone());
                                modified = true;
                            }
                        }
                    }
                    break;
            }
            return modified;
        }

        public static string? GetItemKey(JsonObject obj)
        {
            if (obj.TryGetPropertyValue("id", out var idVal) && idVal is JsonValue idJson)
                return idJson.GetValue<string>();
            if (obj.TryGetPropertyValue("key", out var keyVal) && keyVal is JsonValue keyJson)
                return keyJson.GetValue<string>();
            if (obj.TryGetPropertyValue("name", out var nameVal) && nameVal is JsonValue nameJson)
                return nameJson.GetValue<string>();
            return null;
        }
    }
}
