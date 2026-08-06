using Microsoft.Extensions.Configuration;

namespace ModulusSample.Api.Extensions;

internal static class ConfigurationExtensions
{
    internal static void AddModuleConfiguration(this IConfigurationBuilder configurationBuilder, string[] modules, string environmentName)
    {
        foreach (string module in modules)
        {
            configurationBuilder.AddJsonFile($"modules.{module}.json", false, true);
            configurationBuilder.AddJsonFile($"modules.{module}.{environmentName}.json", true, true);
        }
    }
}
