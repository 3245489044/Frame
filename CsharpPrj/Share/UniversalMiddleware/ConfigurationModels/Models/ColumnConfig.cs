#nullable enable
using System.Text.Json.Serialization;

namespace Modules.ConfigurationModels
{
    public class ColumnConfig
    {
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; } = 100;
    }
}
