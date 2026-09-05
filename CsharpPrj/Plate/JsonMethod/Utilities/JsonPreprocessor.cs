
using System.Text.Json;
using Plate.JsonMethod.Serialization;

namespace Plate.JsonMethod.Utilities
{
    public class JsonPreprocessor
    {
        private readonly List<Func<string, string>> _steps = new();
        private readonly List<string> _stepNames = new();

        public Action<string>? Logger { get; set; }

        public void RegisterStep(Func<string, string> step, string? name = null)
        {
            _steps.Add(step);
            _stepNames.Add(name ?? $"Step{_steps.Count}");
        }

        public string Preprocess(string json)
        {
            var current = json;
            for (int i = 0; i < _steps.Count; i++)
            {
                var previous = current;
                try
                {
                    Logger?.Invoke($"[JsonPreprocessor] 执行步骤 {i + 1}/{_steps.Count}: {_stepNames[i]}");
                    current = _steps[i](current);
                }
                catch (Exception ex)
                {
                    Logger?.Invoke($"[JsonPreprocessor] 步骤 {_stepNames[i]} 失败: {ex.Message}");
                    current = previous;
                    Logger?.Invoke($"[JsonPreprocessor] 步骤 {_stepNames[i]} 已回滚，继续执行后续步骤");
                }
            }
            return current;
        }

        public async Task<string> PreprocessFileAsync(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("JSON file not found", path);
            var json = await File.ReadAllTextAsync(path);
            return Preprocess(json);
        }

        public T? PreprocessAndDeserialize<T>(string json, JsonSerializerOptions? options = null)
        {
            var processed = Preprocess(json);
            return JsonSerializer.Deserialize<T>(processed, options ?? JsonSerializerFactory.Contexts.Default);
        }

        public async Task<T?> LoadPreprocessAndDeserializeAsync<T>(string path, JsonSerializerOptions? options = null)
        {
            var json = await PreprocessFileAsync(path);
            return JsonSerializer.Deserialize<T>(json, options ?? JsonSerializerFactory.Contexts.Default);
        }

        public int StepCount => _steps.Count;

        public void Clear()
        {
            _steps.Clear();
            _stepNames.Clear();
        }
    }
}
