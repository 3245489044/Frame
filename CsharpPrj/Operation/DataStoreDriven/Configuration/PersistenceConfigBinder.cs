using System;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Microsoft.Extensions.Configuration;

namespace Operation.DataStoreDriven.Configuration;

/// <summary>
/// 配置绑定器：把 <c>AgentConfig:Persistence</c> 配置节显式映射到 <see cref="PersistenceOptions"/>。
/// 采用手写绑定，避免引入 Configuration.Binder 包。覆盖式绑定：仅写入节内显式值，
/// 未提供的字段保留 <paramref name="options"/> 既有默认值（默认值唯一权威 = Agent.Launcher.AgentConfig）。
/// </summary>
public static class PersistenceConfigBinder
{
    public static void BindSection(IConfigurationSection section, PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);

        if (Enum.TryParse<PersistenceProvider>(section["Provider"], ignoreCase: true, out var provider))
            options.Provider = provider;

        if (!string.IsNullOrWhiteSpace(section["DatabasePath"]))
            options.DatabasePath = section["DatabasePath"]!;

        if (!string.IsNullOrWhiteSpace(section["ConnectionString"]))
            options.ConnectionString = section["ConnectionString"]!;

        if (!string.IsNullOrWhiteSpace(section["TablePrefix"]))
            options.TablePrefix = section["TablePrefix"]!;

        if (bool.TryParse(section["AutoCreateSchema"], out var autoCreate))
            options.AutoCreateSchema = autoCreate;
    }
}
