namespace MathematicalAlgorithms
{
    public class ComputationProgress
    {
        public double Percentage { get; }

        public string Status { get; }

        public int CurrentChunk { get; }

        public int TotalChunks { get; }

        public int ProcessedRows { get; }

        public int TotalRows { get; }

        public ComputationProgress(double percentage, string status,
            int currentChunk = 0, int totalChunks = 0,
            int processedRows = 0, int totalRows = 0)
        {
            Percentage = Math.Clamp(percentage, 0, 100);
            Status = status ?? string.Empty;
            CurrentChunk = currentChunk;
            TotalChunks = totalChunks;
            ProcessedRows = processedRows;
            TotalRows = totalRows;
        }

        public ComputationProgress(double percentage, string status)
            : this(percentage, status, 0, 0, 0, 0)
        {
        }

        public ComputationProgress(double percentage) : this(percentage, string.Empty)
        {
        }

        public static ComputationProgress Start(int totalRows) =>
            new(0, "Starting computation...", 0, 0, 0, totalRows);

        public override string ToString() => $"{Percentage:F1}% - {Status}";
    }
}
