#region USINGS
using System.Text.Json.Serialization;
using System.Text.Json;
#endregion



namespace FunctionStorage.Models
{
    public class WorkflowDefinition
    {
        [JsonPropertyName("workflowName")]
        public string WorkflowName { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("steps")]
        public List<WorkflowStep>? Steps { get; set; }

        [JsonPropertyName("outputMapping")]
        public Dictionary<string, string>? OutputMapping { get; set; }

        [JsonPropertyName("defaultInput")]
        public Dictionary<string, object>? DefaultInput { get; set; }

        [JsonPropertyName("stepSelector")]
        public string? StepSelector { get; set; }

        [JsonPropertyName("ioMapping")]
        public FieldMappingCollection? IoMapping { get; set; }
    }

    public class WorkflowStep
    {
        [JsonPropertyName("stepName")]
        public string StepName { get; set; } = string.Empty;

        [JsonPropertyName("funcName")]
        public string FuncName { get; set; } = string.Empty;

        [JsonPropertyName("inputKeys")]
        public List<string> InputKeys { get; set; } = new();

        [JsonPropertyName("outputKey")]
        public string OutputKey { get; set; } = string.Empty;

        [JsonPropertyName("parameters")]
        public List<string>? Parameters { get; set; }
    }

    public class FieldMappingCollection
    {
        [JsonPropertyName("inputMapping")]
        public Dictionary<string, string>? InputMapping
        {
            get => _inputMapping;
            set
            {
                _inputMapping = value;
                if (value != null)
                {
                    foreach (var kvp in value)
                        InputToControl[kvp.Key] = kvp.Value;
                }
            }
        }
        private Dictionary<string, string>? _inputMapping;

        [JsonPropertyName("outputMapping")]
        public Dictionary<string, string>? OutputMapping
        {
            get => _outputMapping;
            set
            {
                _outputMapping = value;
                if (value != null)
                {
                    foreach (var kvp in value)
                        OutputToControl[kvp.Key] = kvp.Value;
                }
            }
        }
        private Dictionary<string, string>? _outputMapping;


        [JsonIgnore]
        public Dictionary<string, string> InputToControl { get; set; } = new();

        [JsonIgnore]
        public Dictionary<string, string> OutputToControl { get; set; } = new();
    }
}
