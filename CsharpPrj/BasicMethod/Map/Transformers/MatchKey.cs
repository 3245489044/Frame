
namespace Modules.Transformers
{
    public static class MatchKey
    {
        public static object Of(params object?[] keys)
            => keys.Length switch
            {
                2 => (keys[0], keys[1]),
                3 => (keys[0], keys[1], keys[2]),
                4 => (keys[0], keys[1], keys[2], keys[3]),
                5 => (keys[0], keys[1], keys[2], keys[3], keys[4]),
                _ => Tuple.Create(keys)
            };
    }
}
