namespace CodexQuotaTitle.Integration;

public sealed record PluginRuntimePaths(string PluginRoot, string PluginData)
{
    public static PluginRuntimePaths FromEnvironment()
    {
        var pluginRoot = Environment.GetEnvironmentVariable("PLUGIN_ROOT");
        var pluginData = Environment.GetEnvironmentVariable("PLUGIN_DATA");
        if (string.IsNullOrWhiteSpace(pluginRoot) || string.IsNullOrWhiteSpace(pluginData))
        {
            throw new InvalidOperationException("PLUGIN_ROOT and PLUGIN_DATA are required for the integration probe.");
        }

        return new PluginRuntimePaths(Path.GetFullPath(pluginRoot), Path.GetFullPath(pluginData));
    }
}
