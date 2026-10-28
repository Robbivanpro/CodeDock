namespace CodeDock.Core.Models;

public sealed record IdeWindowCandidate(
    long Handle,
    int ProcessId,
    string Title,
    DevelopmentEnvironment Environment);
