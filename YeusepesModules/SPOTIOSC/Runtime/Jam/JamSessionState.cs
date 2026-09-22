namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed class JamSessionState
{
    public string? SessionId { get; set; }
    public string? JoinToken { get; set; }
    public string? ShareableUrl { get; set; }
    public bool IsActive { get; set; }
    public int MaxMemberCount { get; set; }
    public int ParticipantCount { get; set; }
    public bool IsListening { get; set; }
    public bool IsControlling { get; set; }
    public bool QueueOnlyMode { get; set; }
    public bool HostIsGroup { get; set; }

    public void Reset()
    {
        SessionId = null;
        JoinToken = null;
        ShareableUrl = null;
        IsActive = false;
        MaxMemberCount = 0;
        ParticipantCount = 0;
        IsListening = false;
        IsControlling = false;
        QueueOnlyMode = false;
        HostIsGroup = false;
    }
}
