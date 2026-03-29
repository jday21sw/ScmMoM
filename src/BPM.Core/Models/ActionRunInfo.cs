namespace BPM.Core.Models;

public class ActionRunInfo
{
    public string RepoName { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Conclusion { get; set; }
    public string Branch { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long RunNumber { get; set; }
    public string Event { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public long RunId { get; set; }
    public long CheckSuiteId { get; set; }

    /// <summary>Computed color for conclusion text.</summary>
    public string ConclusionColor => Conclusion switch
    {
        "success" => "#22863A",
        "failure" => "#CB2431",
        "cancelled" => "#6A737D",
        _ => "#24292E"
    };

    /// <summary>Display text for conclusion (shows status if still running).</summary>
    public string ConclusionDisplay => Conclusion ?? Status;

    /// <summary>True when the run is still in progress (for flashing effect).</summary>
    public bool IsRunning => Status is "in_progress" or "queued" or "waiting" or "pending";
}
