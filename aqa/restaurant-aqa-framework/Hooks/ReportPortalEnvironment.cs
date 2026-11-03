using System.Runtime.CompilerServices;
using Restaurant_Aqa_Framework.Configuration;

namespace Restaurant_Aqa_Framework.Hooks;

/// <summary>
/// Maps <c>RP_URL</c>, <c>RP_PROJECT</c> and <c>RP_API_KEY</c> onto the names the ReportPortal agent reads
/// (<c>RP_Server_Url</c> and the rest). Runs when this assembly loads, which is before the agent reads
/// configuration, so <c>.env</c> has to be loaded here. A later load is too late: this method would
/// already have forced <c>RP_Enabled</c> off, and dotenv will not overwrite that. Reporting stays off
/// unless all three are set: the agent's own default is to turn itself on.
/// </summary>
internal static class ReportPortalEnvironment
{
    [ModuleInitializer]
    internal static void Apply()
    {
        EnvConfig.EnsureLoaded();

        var url = Environment.GetEnvironmentVariable("RP_URL");
        var project = Environment.GetEnvironmentVariable("RP_PROJECT");
        var apiKey = Environment.GetEnvironmentVariable("RP_API_KEY");
        var configured = !string.IsNullOrWhiteSpace(url)
            && !string.IsNullOrWhiteSpace(project)
            && !string.IsNullOrWhiteSpace(apiKey);

        Environment.SetEnvironmentVariable("RP_Enabled", configured ? "true" : "false");
        if (!configured)
            return;

        Environment.SetEnvironmentVariable("RP_Server_Url", url);
        Environment.SetEnvironmentVariable("RP_Server_Project", project);
        Environment.SetEnvironmentVariable("RP_Server_ApiKey", apiKey);
        Environment.SetEnvironmentVariable("RP_Launch_Name", "Restaurant AQA");
        Environment.SetEnvironmentVariable("RP_Launch_Description", "Direction 1 level 3");
    }
}
