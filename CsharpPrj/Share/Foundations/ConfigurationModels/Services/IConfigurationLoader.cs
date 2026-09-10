#nullable enable
using TConfig;

namespace Modules.ConfigurationModels.Services

{
    public interface IConfigurationLoader
    {
        Task<UIConfiguration> LoadConfigAsync(string configPath);

        UIConfiguration GetConfig(string configPath);

        Task SaveConfigAsync(string configPath, UIConfiguration config);

        JsonValidationResult? ValidateConfigContent(string jsonContent);

        Task ReloadConfigAsync(string configPath);

        void RegisterConfigChangeHandler(string configPath, Action<string> handler);

        void UnregisterConfigChangeHandler(string configPath);

        void ClearCache();
    }

    public class JsonValidationResult
    {
        public bool IsValid { get; }
        public string? ErrorMessage { get; }

        private JsonValidationResult(bool isValid, string? errorMessage = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static JsonValidationResult Success() => new(true);
        public static JsonValidationResult Failure(string error) => new(false, error);
    }
}
