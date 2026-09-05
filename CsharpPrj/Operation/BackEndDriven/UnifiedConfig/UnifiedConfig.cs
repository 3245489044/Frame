
using TConfig;

namespace BackEndDriven.UnifiedConfig
{
    public class UnifiedConfig
    {
        public string? Schema { get; set; }

        public string? Version { get; set; }

        public string? Description { get; set; }

        public List<UnifiedRegistrationEntry> Functions { get; set; } = new();

        public UnifiedUiConfig? Ui { get; set; }
    }

    public class UnifiedRegistrationEntry
    {
        public string Name { get; set; } = "";

        public string Type { get; set; } = "delegate";

        public string Source { get; set; } = "";

        public string Method { get; set; } = "";

        public List<UnifiedParameterConfig> Parameters { get; set; } = new();

        public string Description { get; set; } = "";
    }

    public class UnifiedParameterConfig
    {
        public string Name { get; set; } = "";

        public string Type { get; set; } = "System.String";

        public bool IsOptional { get; set; }

        public string DefaultValue { get; set; } = "";
    }

    public class UnifiedUiConfig
    {
        public UnifiedFormConfig? Form { get; set; }

        public List<UnifiedViewConfig> Views { get; set; } = new();
    }

    public class UnifiedFormConfig
    {
        public string? Title { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }
    }

    public class UnifiedViewConfig
    {
        public string? Name { get; set; }

        public string? Type { get; set; }

        public List<ControlConfig> Controls { get; set; } = new();
    }
}
