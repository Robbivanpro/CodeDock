namespace CodeDock.Core.Models;

public sealed record IdeInstallation(
    DevelopmentEnvironment Environment,
    string DisplayName,
    string ExecutablePath,
    string? Version = null);
