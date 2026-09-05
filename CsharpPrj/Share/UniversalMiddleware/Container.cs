#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TConfig;


namespace UniversalMiddleware.Abstractions
{

    public class ContainerConfig
    {
        [JsonPropertyName("sourceType")]
        public string SourceType { get; set; } = "static";

        [JsonPropertyName("defaultConfig")]
        public string? DefaultConfig { get; set; }

        [JsonPropertyName("referenceKey")]
        public string? ReferenceKey { get; set; }

        [JsonPropertyName("autoLoad")]
        public bool AutoLoad { get; set; } = true;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public static class ContainerSourceTypes
    {
        public const string Static = "static";
        public const string Dynamic = "dynamic";
        public const string Reference = "reference";
    }


    public interface IConfigContainer
    {
        string ContainerName { get; }

        ContainerConfig? ContainerConfig { get; set; }

        bool IsLoaded { get; }


        Task LoadConfigAsync(TConfig.UIConfiguration config);


        void ClearContent();


        IConfigContainer? FindSubContainer(string name);

        IConfigContainer CreateSubContainer(string name, ContainerConfig? config = null);

        IEnumerable<IConfigContainer> GetSubContainers();


        object? FindControlByName(string name);

        T? FindControl<T>(string name) where T : class;


        event EventHandler<ContainerConfigLoadedEventArgs>? ConfigLoaded;

        event EventHandler? ContentChanged;
    }

    public class ContainerConfigLoadedEventArgs : EventArgs
    {
        public string ConfigPath { get; }

        public bool Success { get; }

        public string? ErrorMessage { get; }

        public object? LoadedConfig { get; }

        public ContainerConfigLoadedEventArgs(string configPath, bool success, string? error = null, object? loadedConfig = null)
        {
            ConfigPath = configPath;
            Success = success;
            ErrorMessage = error;
            LoadedConfig = loadedConfig;
        }
    }


    public interface IContainerManager
    {
        void RegisterContainer(string name, IConfigContainer container);

        void UnregisterContainer(string name);

        IConfigContainer? GetContainer(string name);

        IEnumerable<string> GetContainerNames();

        void ClearAll();
    }

    public class ContainerManager : IContainerManager
    {
        private readonly ConcurrentDictionary<string, IConfigContainer> _containers = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterContainer(string name, IConfigContainer container)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Container name cannot be null or empty", nameof(name));
            if (container == null)
                throw new ArgumentNullException(nameof(container));

            _containers[name] = container;
        }

        public void UnregisterContainer(string name)
        {
            if (!string.IsNullOrEmpty(name))
                _containers.TryRemove(name, out _);
        }

        public IConfigContainer? GetContainer(string name)
        {
            return _containers.TryGetValue(name, out var container) ? container : null;
        }

        public IEnumerable<string> GetContainerNames() => _containers.Keys;

        public void ClearAll() => _containers.Clear();
    }

}
