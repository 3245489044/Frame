namespace MathematicalAlgorithms
{
    public class ElementDistance
    {
        public string SourceId { get; }
        public string TargetId { get; }
        public double Distance { get; }
        public double Strength { get; }
        public int Count { get; }

        public ElementDistance(string sourceId, string targetId, double distance, double strength, int count)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
            Distance = distance;
            Strength = strength;
            Count = count;
        }
    }
}
