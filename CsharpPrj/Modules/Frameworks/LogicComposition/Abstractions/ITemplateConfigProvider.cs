
namespace Share.LogicComposition.Abstractions
{
    public interface ITemplateConfigProvider<TConfig>
        where TConfig : class
    {
        void Register(string name, TConfig template);

        void RegisterRange(IEnumerable<KeyValuePair<string, TConfig>> templates);

        TConfig Get(string name);

        bool TryGet(string name, out TConfig? template);

        IEnumerable<string> GetAllNames();

        void SetDefault(string name);

        bool Contains(string name);

        int Count { get; }

        void Clear();
    }
}
