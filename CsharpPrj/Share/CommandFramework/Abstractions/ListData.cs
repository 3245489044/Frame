
using System.Linq;
using FrameworkAbstractions.Controls;

namespace CommandFramework.Abstractions;

public class ListData : IControlValue
{
    public string[] Items { get; }

    public int Count => Items.Length;

    public bool IsEmpty => Items.Length == 0;

    public ListData(string[] items)
    {
        Items = items ?? System.Array.Empty<string>();
    }

    public string Serialize() => string.Join("\n", Items);

    public override string ToString() => Serialize();

    public static ListData Parse(string value)
    {
        if (string.IsNullOrEmpty(value))
            return new ListData(System.Array.Empty<string>());

        string[] items;
        if (value.Contains('\n'))
        {
            items = value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            items = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        }

        return new ListData(
            items.Select(s => s.Trim('\r', ' ', '\t'))
                 .Where(s => !string.IsNullOrEmpty(s))
                 .ToArray());
    }

    public static ListData From(IEnumerable<string> items)
        => new ListData(items.ToArray());

    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)Items).GetEnumerator();

    public string this[int index] => Items[index];

    public bool Contains(string item) => Items.Contains(item);

    public string ToCommaSeparated() => string.Join(", ", Items);
}
