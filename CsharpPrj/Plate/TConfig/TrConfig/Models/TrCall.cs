
namespace TConfig
{
    public class TrCall : ITrCall
    {
        public string ConfigName { get; set; } = string.Empty;

        public List<object?> Parameters { get; set; } = new();

        public ITrCall? NestedCall { get; set; }

        public int NestedCallParameterIndex { get; set; } = 0;

        public TrPipeline? SubPipeline { get; set; }

        public bool UseExplicitParameters { get; set; }

        public Dictionary<string, object> Metadata { get; set; } = new();

        public static TrCall Create(string configName, params object[] parameters) =>
            new TrCall { ConfigName = configName, Parameters = parameters?.ToList<object?>() ?? new List<object?>() };

        public TrCall WithNestedCall(string configName, params object[] parameters)
        {
            NestedCall = Create(configName, parameters);
            return this;
        }

        public TrCall WithNestedCallAt(int index)
        {
            NestedCallParameterIndex = index;
            return this;
        }

        public TrCall WithMetadata(string key, object value)
        {
            Metadata[key] = value;
            return this;
        }
    }
}
