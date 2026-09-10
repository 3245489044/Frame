#nullable enable
using TConfig;

namespace Modules.ConfigurationModels.Abstractions

{
    public interface IConfigContainer
    {
        UIConfiguration? CurrentConfig { get; }

        string ContainerName { get; }

        ContainerConfig? ContainerConfig { get; set; }

        bool IsLoaded { get; }

        Task LoadConfigAsync(UIConfiguration config);

        IConfigContainer? FindSubContainer(string name);

        IConfigContainer CreateSubContainer(string name, ContainerConfig? config = null);

        IEnumerable<IConfigContainer> GetSubContainers();

        object? FindControlByName(string name);

        T? FindControl<T>(string name) where T : class;

        void ClearContent();
    }
}
