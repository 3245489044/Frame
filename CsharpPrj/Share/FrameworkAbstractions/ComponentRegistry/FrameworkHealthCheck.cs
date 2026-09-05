#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FrameworkAbstractions.ComponentRegistry
{
    public static class FrameworkHealthCheck
    {
        public static HealthCheckReport Execute(params Type[] typesToScan)
        {

            var report = AppServiceProvider.ValidateAll();

            var requiredComponents = ScanRequiredComponents(typesToScan);
            foreach (var (serviceType, attr) in requiredComponents)
            {
                if (!AppServiceProvider.IsRegistered(serviceType))
                {
                    if (report.Missing.Any(m => m.ServiceType == serviceType))
                        continue;

                    var registration = new ComponentRegistration
                    {
                        ServiceType = serviceType,
                        ServiceName = attr.ComponentName,
                        Criticality = attr.Criticality,
                        Description = attr.Description,
                        Instance = null
                    };

                    report.Missing.Add(registration);
                    switch (attr.Criticality)
                    {
                        case CriticalityLevel.High:
                            if (!report.MissingCritical.Contains(attr.ComponentName))
                                report.MissingCritical.Add(attr.ComponentName);
                            break;
                        case CriticalityLevel.Medium:
                            if (!report.MissingRecommended.Contains(attr.ComponentName))
                                report.MissingRecommended.Add(attr.ComponentName);
                            break;
                        case CriticalityLevel.Low:
                            if (!report.MissingOptional.Contains(attr.ComponentName))
                                report.MissingOptional.Add(attr.ComponentName);
                            break;
                    }
                }
            }

            report.AllCriticalRegistered = report.MissingCritical.Count == 0;
            return report;
        }

        public static HealthCheckReport ExecuteAndReport(Action<string> logAction, params Type[] typesToScan)
        {
            var report = Execute(typesToScan);
            logAction(report.ToString());

            if (report.HasMissingCritical)
            {
                logAction("");
                logAction("⚠️ 警告：存在缺失的关键组件，部分功能可能无法正常工作！");
                logAction("请检查 FrameworkInitializer.RegisterComponents() 中的组件注册。");
            }

            return report;
        }

        private static List<(Type ServiceType, RequiredComponentAttribute Attribute)> ScanRequiredComponents(Type[] types)
        {
            var results = new List<(Type, RequiredComponentAttribute)>();

            foreach (var type in types)
            {
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    var attr = prop.GetCustomAttribute<RequiredComponentAttribute>();
                    if (attr != null)
                    {
                        results.Add((prop.PropertyType, attr));
                    }
                }

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    var attr = field.GetCustomAttribute<RequiredComponentAttribute>();
                    if (attr != null)
                    {
                        results.Add((field.FieldType, attr));
                    }
                }
            }

            return results;
        }

        public static bool IsHealthy()
        {
            var report = Execute();
            return report.AllCriticalRegistered;
        }

        public static List<string> GetMissingCriticalComponents()
        {
            var report = Execute();
            return report.MissingCritical;
        }
    }
}
