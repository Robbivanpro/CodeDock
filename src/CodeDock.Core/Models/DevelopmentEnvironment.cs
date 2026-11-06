namespace CodeDock.Core.Models;

public enum DevelopmentEnvironment
{
    VisualStudioCode,
    VisualStudio2022,
    Antigravity,
    MicrosoftTeams,
    Spotify,
    MicrosoftEdge,
    GoogleChrome,
    MicrosoftOutlook,
    WhatsApp,
    Obsidian
}

public static class DevelopmentEnvironmentExtensions
{
    public static IReadOnlyList<DevelopmentEnvironment> CodeEnvironments { get; } =
    [
        DevelopmentEnvironment.VisualStudioCode,
        DevelopmentEnvironment.VisualStudio2022,
        DevelopmentEnvironment.Antigravity
    ];

    public static IReadOnlyList<DevelopmentEnvironment> AppEnvironments { get; } =
    [
        DevelopmentEnvironment.MicrosoftTeams,
        DevelopmentEnvironment.MicrosoftOutlook,
        DevelopmentEnvironment.WhatsApp,
        DevelopmentEnvironment.Spotify,
        DevelopmentEnvironment.Obsidian,
        DevelopmentEnvironment.MicrosoftEdge,
        DevelopmentEnvironment.GoogleChrome
    ];

    public static bool IsCodeEnvironment(this DevelopmentEnvironment environment) => environment is
        DevelopmentEnvironment.VisualStudioCode or
        DevelopmentEnvironment.VisualStudio2022 or
        DevelopmentEnvironment.Antigravity;

    public static bool IsBrowserEnvironment(this DevelopmentEnvironment environment) => environment is
        DevelopmentEnvironment.MicrosoftEdge or
        DevelopmentEnvironment.GoogleChrome;

    public static bool IsSingleInstanceApp(this DevelopmentEnvironment environment) => environment is
        DevelopmentEnvironment.MicrosoftTeams or
        DevelopmentEnvironment.MicrosoftOutlook or
        DevelopmentEnvironment.WhatsApp or
        DevelopmentEnvironment.Spotify or
        DevelopmentEnvironment.Obsidian;

    public static string GetDisplayName(this DevelopmentEnvironment environment) => environment switch
    {
        DevelopmentEnvironment.VisualStudioCode => "Visual Studio Code",
        DevelopmentEnvironment.VisualStudio2022 => "Visual Studio 2022",
        DevelopmentEnvironment.Antigravity => "Antigravity",
        DevelopmentEnvironment.MicrosoftTeams => "Microsoft Teams",
        DevelopmentEnvironment.MicrosoftOutlook => "Microsoft Outlook",
        DevelopmentEnvironment.WhatsApp => "WhatsApp",
        DevelopmentEnvironment.Spotify => "Spotify",
        DevelopmentEnvironment.Obsidian => "Obsidian",
        DevelopmentEnvironment.MicrosoftEdge => "Microsoft Edge",
        DevelopmentEnvironment.GoogleChrome => "Google Chrome",
        _ => environment.ToString()
    };
}
