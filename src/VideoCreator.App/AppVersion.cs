using System.Reflection;

namespace VideoCreator.App;

public static class AppVersion
{
    public static string InformationalVersion { get; } = typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("Application version metadata is missing.");

    public static string Version { get; } = InformationalVersion.Split('+', 2)[0];
}
