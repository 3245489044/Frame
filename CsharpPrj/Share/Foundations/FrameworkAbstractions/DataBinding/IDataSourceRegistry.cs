#nullable enable

namespace FrameworkAbstractions.DataBinding
{
    public interface IDataSourceRegistry
    {
        void RegisterDataSource<T>(string key, T dataSource) where T : class;

        T? GetDataSource<T>(string key) where T : class;

        void UpdateDataSource(string key, object? value);
    }
}
