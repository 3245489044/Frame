using System.Text.Json;

namespace CallCenter.LlmGate.Hosting;

/// <summary>请求体形态事实（调用前）。</summary>
public sealed record OpenAiRequestFacts(
    string? Model,
    bool Stream,
    int MessageCount,
    string RoleHistogram,
    int SystemChars,
    int UserChars,
    int HistoryChars,
    int ToolCount,
    string? ToolNamesHash,
    int TotalChars);

/// <summary>响应形态事实（调用后）。</summary>
public sealed record OpenAiResponseFacts(
    string? Model,
    string? FinishReason,
    int ContentChars,
    int ToolCallCount,
    IReadOnlyDictionary<string, long?>? Usage);

/// <summary>OpenAI 兼容 wire 的 JSON 解析（只读结构，不做领域判断）。</summary>
public static class OpenAiWireFacts
{
    public static OpenAiRequestFacts ParseRequest(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new OpenAiRequestFacts(null, false, 0, "", 0, 0, 0, 0, null, 0);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var model = OpenAiJson.ReadString(root, "model");
            var stream = OpenAiJson.ReadBool(root, "stream") ?? false;

            var messageCount = 0;
            int system = 0, user = 0, assistant = 0, tool = 0, other = 0;
            int systemChars = 0, userChars = 0, historyChars = 0;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    messageCount++;
                    var role = (OpenAiJson.ReadString(message, "role") ?? "other").ToLowerInvariant();
                    var content = OpenAiJson.ReadContentText(message);
                    switch (role)
                    {
                        case "system":
                            system++;
                            systemChars += content.Length;
                            break;
                        case "user":
                            user++;
                            if (user == 1)
                            {
                                userChars += content.Length;
                            }
                            else
                            {
                                historyChars += content.Length;
                            }

                            break;
                        case "assistant":
                            assistant++;
                            historyChars += content.Length;
                            break;
                        case "tool":
                            tool++;
                            historyChars += content.Length;
                            break;
                        default:
                            other++;
                            historyChars += content.Length;
                            break;
                    }
                }
            }

            var toolCount = 0;
            string? toolNamesHash = null;
            if (root.TryGetProperty("tools", out var tools) && tools.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var item in tools.EnumerateArray())
                {
                    toolCount++;
                    var name = item.TryGetProperty("function", out var fn)
                        ? OpenAiJson.ReadString(fn, "name")
                        : OpenAiJson.ReadString(item, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name!);
                    }
                }

                if (names.Count > 0)
                {
                    names.Sort(StringComparer.Ordinal);
                    toolNamesHash = CallCenter.LlmGate.Fingerprint.RequestFingerprint.Compute(string.Join("\n", names));
                }
            }

            var histogram = string.Join(",", new[]
            {
                ("system", system), ("user", user), ("assistant", assistant), ("tool", tool), ("other", other),
            }.Where(p => p.Item2 > 0).Select(p => $"{p.Item1}={p.Item2}"));

            return new OpenAiRequestFacts(
                model, stream, messageCount, histogram, systemChars, userChars, historyChars,
                toolCount, toolNamesHash, systemChars + userChars + historyChars);
        }
        catch (JsonException)
        {
            return new OpenAiRequestFacts(null, false, 0, "", 0, 0, 0, 0, null, 0);
        }
    }

    public static OpenAiResponseFacts ParseResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new OpenAiResponseFacts(null, null, 0, 0, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return ParseResponse(doc.RootElement);
        }
        catch (JsonException)
        {
            return new OpenAiResponseFacts(null, null, 0, 0, null);
        }
    }

    public static OpenAiResponseFacts ParseResponse(JsonElement root)
    {
        var model = OpenAiJson.ReadString(root, "model");
        string? finish = null;
        var contentChars = 0;
        var toolCalls = 0;

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            finish = OpenAiJson.ReadString(choice, "finish_reason");
            if (choice.TryGetProperty("message", out var message))
            {
                contentChars = OpenAiJson.ReadContentText(message).Length;
                if (message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
                {
                    toolCalls = tc.GetArrayLength();
                }
            }
        }

        return new OpenAiResponseFacts(model, finish, contentChars, toolCalls, OpenAiJson.ReadUsage(root));
    }
}
