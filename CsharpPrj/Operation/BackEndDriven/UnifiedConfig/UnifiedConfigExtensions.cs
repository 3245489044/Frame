
using System.Text.Json;
using TConfig;

namespace BackEndDriven.UnifiedConfig
{
    public static class UnifiedConfigExtensions
    {
        public static UIConfiguration ToUIConfiguration(this UnifiedUiConfig uiConfig)
        {
            if (uiConfig == null)
                throw new ArgumentNullException(nameof(uiConfig));

            var result = new UIConfiguration
            {
                Form = uiConfig.Form != null
                    ? new FormConfig
                    {
                        Title = uiConfig.Form.Title ?? string.Empty,
                        Width = uiConfig.Form.Width,
                        Height = uiConfig.Form.Height
                    }
                    : new FormConfig()
            };

            result.Views = uiConfig.Views
                .Select(v => new ViewConfig
                {
                    Name = v.Name ?? string.Empty,
                    Type = string.IsNullOrWhiteSpace(v.Type) ? "Panel" : v.Type,
                    Controls = v.Controls ?? new List<ControlConfig>()
                })
                .ToList();

            return result;
        }

        public static string ToJsonString(this UnifiedConfig config)
        {
            return JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
    }
}
