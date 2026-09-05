#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace FrameworkAbstractions.ComponentRegistry
{
    public class HealthCheckReport
    {
        public bool AllCriticalRegistered { get; set; }
        public List<string> MissingCritical { get; set; } = new();
        public List<string> MissingRecommended { get; set; } = new();
        public List<string> MissingOptional { get; set; } = new();
        public List<ComponentRegistration> Registered { get; set; } = new();
        public List<ComponentRegistration> Missing { get; set; } = new();

        public bool HasMissingCritical => MissingCritical.Count > 0;
        public bool HasMissingRecommended => MissingRecommended.Count > 0;

        public override string ToString()
        {
            var lines = new List<string>();

            if (AllCriticalRegistered)
                lines.Add("[组件注册验证] 通过 | 已注册: " + Registered.Count + " | 缺失: " + Missing.Count);
            else
                lines.Add("[组件注册验证] 失败 | 已注册: " + Registered.Count + " | 缺失: " + Missing.Count + " (其中关键: " + MissingCritical.Count + ")");

            if (Registered.Count > 0)
            {
                lines.Add("  已注册组件:");
                foreach (var r in Registered)
                {
                    var level = r.Criticality == CriticalityLevel.High ? "关键" :
                                r.Criticality == CriticalityLevel.Medium ? "建议" : "可选";
                    var implType = r.Instance?.GetType().FullName ?? "(未设置)";
                    lines.Add("    [" + level + "] " + r.ServiceName + " (" + r.ServiceType.Name + ") -> " + implType);
                }
            }

            if (Missing.Count > 0)
            {
                lines.Add("  缺失组件:");
                foreach (var m in Missing)
                {
                    var level = m.Criticality == CriticalityLevel.High ? "关键" :
                                m.Criticality == CriticalityLevel.Medium ? "建议" : "可选";
                    lines.Add("    [" + level + "] " + m.ServiceName + " (" + m.ServiceType.Name + ")");
                    if (!string.IsNullOrEmpty(m.Description))
                        lines.Add("      - " + m.Description);
                }
            }

            return string.Join("\n", lines);
        }

        public string ToSummary()
        {
            var parts = new List<string>
            {
                "已注册=" + Registered.Count,
                "缺失=" + Missing.Count
            };
            if (MissingCritical.Count > 0)
                parts.Add("缺失关键=" + MissingCritical.Count);
            if (MissingRecommended.Count > 0)
                parts.Add("缺失建议=" + MissingRecommended.Count);

            var status = AllCriticalRegistered ? "通过" : "失败";
            return "[组件注册验证 " + status + "] " + string.Join(", ", parts);
        }
    }
}
