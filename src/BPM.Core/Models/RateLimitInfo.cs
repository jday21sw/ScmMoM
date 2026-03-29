namespace BPM.Core.Models;

public class RateLimitInfo
{
    public int Remaining { get; set; }
    public int Limit { get; set; }
    public DateTimeOffset ResetAt { get; set; }
}
