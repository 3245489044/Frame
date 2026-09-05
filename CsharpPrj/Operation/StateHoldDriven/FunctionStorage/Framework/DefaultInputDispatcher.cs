#region USINGS
using System.Text.Json;
using Common.Diagnostics;
using WorkflowDefinition = FunctionStorage.Models.WorkflowDefinition;
#endregion

namespace FunctionStorage
{
    public static class DefaultInputDispatcher
    {
        public static Func<string, WorkflowDefinition?>? GetWorkflowDelegate { get; set; }

        public static string GetAllDefaultInputAsJson(string workflowName)
        {
            FsLog.Log("DefaultInputDispatcher_GetAllDefaultInputAsJson", new { workflowName });

            var def = GetWorkflowDelegate?.Invoke(workflowName);
            if (def?.DefaultInput == null || def.DefaultInput.Count == 0)
            {
                FsLog.Log("DefaultInputDispatcher_GetAllDefaultInputAsJson_empty", new { workflowName });
                return "{}";
            }

            var json = JsonSerializer.Serialize(def.DefaultInput, new JsonSerializerOptions { WriteIndented = true });
            FsLog.Log("DefaultInputDispatcher_GetAllDefaultInputAsJson_json", new { json });
            return json;
        }

        public static string GetDefaultInputSmart(string workflowName)
        {
            FsLog.Log("DefaultInputDispatcher_GetDefaultInputSmart", new { workflowName });

            var def = GetWorkflowDelegate?.Invoke(workflowName);
            if (def?.DefaultInput == null || def.DefaultInput.Count == 0)
            {
                FsLog.Log("DefaultInputDispatcher_GetDefaultInputSmart_empty", new { workflowName });
                return string.Empty;
            }

            if (def.DefaultInput.Count == 1)
            {
                var first = def.DefaultInput.First();
                var text = first.Value?.ToString() ?? string.Empty;

                if (!string.IsNullOrEmpty(text) && IsJsonString(text))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        var formattedJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                        FsLog.Log("DefaultInputDispatcher_GetDefaultInputSmart_json_value",
                            new { key = first.Key, originalLength = text.Length, formattedLength = formattedJson.Length });
                        return formattedJson;
                    }
                    catch
                    {
                    }
                }

                FsLog.Log("DefaultInputDispatcher_GetDefaultInputSmart_single",
                    new { key = first.Key, value = text });
                return text;
            }

            var json = JsonSerializer.Serialize(def.DefaultInput, new JsonSerializerOptions { WriteIndented = true });
            FsLog.Log("DefaultInputDispatcher_GetDefaultInputSmart_multi", new { json });
            return json;
        }

        private static bool IsJsonString(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.TrimStart();
            if (trimmed.Length == 0)
                return false;

            if (trimmed[0] != '{' && trimmed[0] != '[')
                return false;

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                return doc.RootElement.ValueKind == JsonValueKind.Object ||
                       doc.RootElement.ValueKind == JsonValueKind.Array;
            }
            catch
            {
                return false;
            }
        }

    }
}
