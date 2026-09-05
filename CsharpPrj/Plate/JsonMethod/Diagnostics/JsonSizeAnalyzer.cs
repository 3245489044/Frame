
using System.Text;
using System.Text.Json;

namespace Plate.JsonMethod.Diagnostics
{
    public class JsonSizeInfo
    {
        public long RawBytes { get; set; }

        public int ElementCount { get; set; }

        public double EstimatedKb { get; set; }

        public string EstimatedMb => $"{EstimatedKb / 1024.0:F2} MB";
    }

    public class DiagnosticResult
    {
        public string PipelineName { get; set; } = "";

        public long DurationMs { get; set; }

        public long MemoryDeltaBytes { get; set; }

        public bool Success { get; set; }

        public string MemoryDeltaFormat => MemoryDeltaBytes >= 1024
            ? $"{MemoryDeltaBytes / 1024.0:F2} KB"
            : $"{MemoryDeltaBytes} B";
    }

    public static class JsonSizeAnalyzer
    {
        public static JsonSizeInfo AnalyzeJsonSize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new JsonSizeInfo { RawBytes = 0 };

            var bytes = Encoding.UTF8.GetByteCount(json);
            using var doc = JsonDocument.Parse(json);
            var elementCount = CountElements(doc.RootElement);

            return new JsonSizeInfo
            {
                RawBytes = bytes,
                ElementCount = elementCount,
                EstimatedKb = Math.Round(bytes / 1024.0, 2)
            };
        }

        private static int CountElements(JsonElement element)
        {
            int count = 1;
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in element.EnumerateObject())
                        count += CountElements(prop.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        count += CountElements(item);
                    break;
            }
            return count;
        }
    }
}
