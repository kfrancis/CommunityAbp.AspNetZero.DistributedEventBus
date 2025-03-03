using System.Collections.Concurrent;
using Abp.Extensions;
using Abp.Reflection.Extensions;
using Microsoft.Extensions.Configuration;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;

public static class AppConfigurations
{
    private static readonly ConcurrentDictionary<string, IConfigurationRoot> s_configurationCache;

    static AppConfigurations()
    {
        s_configurationCache = new ConcurrentDictionary<string, IConfigurationRoot>();
    }

    public static IConfigurationRoot Get(string path, string? environmentName = null, bool addUserSecrets = false)
    {
        var cacheKey = path + "#" + environmentName + "#" + addUserSecrets;
        return s_configurationCache.GetOrAdd(
            cacheKey,
            _ => BuildConfiguration(path, environmentName, addUserSecrets)
        );
    }

    private static IConfigurationRoot BuildConfiguration(string path, string? environmentName = null,
        bool addUserSecrets = false)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(path)
            .AddJsonFile("appsettings.json", true, true);

        if (!environmentName.IsNullOrWhiteSpace())
        {
            builder = builder.AddJsonFile($"appsettings.{environmentName}.json", true);
        }

        builder = builder.AddEnvironmentVariables();

        if (addUserSecrets)
        {
            builder.AddUserSecrets(typeof(AppConfigurations).GetAssembly());
        }

        return builder.Build();
    }
}
