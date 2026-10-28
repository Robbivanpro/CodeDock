namespace CodeDock.Core.Models;

public sealed record StorageLoadResult<T>(T Value, string? Warning = null);
