
namespace Share.LogicComposition.Models
{
    public class ConnectionPoint
    {
        public ConnectionPointType Type { get; }

        public string? ElementKey { get; }

        public enum ConnectionPointType
        {
            ChainStart,

            ChainEnd,

            ByElementKey
        }

        public static ConnectionPoint Start() =>
            new(ConnectionPointType.ChainStart, null);

        public static ConnectionPoint End() =>
            new(ConnectionPointType.ChainEnd, null);

        public static ConnectionPoint ByKey(string elementKey) =>
            new(ConnectionPointType.ByElementKey, elementKey);

        private ConnectionPoint(ConnectionPointType type, string? elementKey)
        {
            Type = type;
            ElementKey = elementKey;
        }
    }
}
