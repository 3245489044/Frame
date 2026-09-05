using System.Text.Json;
using System.Text.Json.Nodes;

namespace Share.ScopeFramework;

/// <summary>
/// ScopeAction → 既有 action 单 JSON 文本的唯一序列化器（替代各 step 手拼字符串的 ScopeJson 辅助）。
/// 键序固定为规范序列、仅输出非 null 字段（payload/relatedNodes 按各自类型输出），
/// 从而保证 Preview 与执行同一条路径、字段含义不变。消费方（Agent RunJsonAsync / 测试 JsonElement）均按键取值，序无关。
/// </summary>
internal static class ScopeActionSerializer
{
    /// <summary>将动作序列化为既有 action 单 JSON 文本。</summary>
    public static string Serialize(ScopeAction a)
    {
        // S0 直通：原样回显原始 JSON，保文本兼容（NoScope_Passthrough_RawJsonEchoed）。
        if (a.RawJson is not null)
            return a.RawJson;

        var obj = new JsonObject { ["action"] = a.Action };

        void Set(string key, object? value)
        {
            if (value is null)
                return;
            obj[key] = value switch
            {
                string s => JsonValue.Create(s),
                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                IReadOnlyList<string> list => new JsonArray(list.Select(s => JsonValue.Create(s)).ToArray()),
                _ => JsonSerializer.SerializeToNode(value),
            };
        }

        // 规范键序（覆盖全部既有 action 产物字段）。
        Set("target", a.Target);
        Set("op", a.Op);
        Set("nodeKey", a.NodeKey);
        Set("nodeType", a.NodeType);
        Set("threadId", a.ThreadId);
        Set("payload", a.Payload);
        Set("taskKey", a.TaskKey);
        Set("input", a.Input);
        Set("mode", a.Mode);
        Set("active", a.Active);
        Set("priority", a.Priority);
        Set("maxRetries", a.MaxRetries);
        Set("executorId", a.ExecutorId);
        Set("relatedNodes", a.RelatedNodes);
        Set("key", a.Key);
        Set("sourceType", a.SourceType);
        Set("sourceId", a.SourceId);
        Set("targetType", a.TargetType);
        Set("targetId", a.TargetId);
        Set("refType", a.RefType);
        Set("resourceType", a.ResourceType);
        Set("id", a.Id);
        Set("name", a.Name);

        return obj.ToJsonString();
    }
}
