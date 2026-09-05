
namespace Modules.Framework.Abstractions
{
    public interface IObjectFactory
    {
        object Create(string typeName);

        T Create<T>(string name) where T : new();

        object Create(Type type, string name);

        void RegisterType(string typeName, Func<object> factory);

        bool UnregisterType(string typeName);

        string[] GetRegisteredTypes();
    }
}
