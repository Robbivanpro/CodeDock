namespace CodeDock.Core.Models;

public enum IdeSessionState
{
    Idle,
    Opening,
    Associated,
    Embedding,
    Embedded,
    Detaching,
    Closing,
    Closed,
    Failed
}
