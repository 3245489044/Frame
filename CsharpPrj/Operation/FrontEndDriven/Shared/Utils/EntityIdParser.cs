
#nullable enable

using TConfig.ECS;

namespace FrontEndDriven.Shared.Utils;

public static class EntityIdParser
{
    public static bool TryParse(string str, out Entity entity)
    {
        entity = default;
        if (string.IsNullOrEmpty(str))
            return false;

        if (str.StartsWith("Entity(") && str.EndsWith(")"))
        {
            var inner = str[7..^1];
            var parts = inner.Split(':');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var id) &&
                int.TryParse(parts[1], out var version))
            {
                entity = new Entity(id, version);
                return true;
            }
        }

        return false;
    }

}
