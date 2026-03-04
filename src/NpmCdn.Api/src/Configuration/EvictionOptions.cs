namespace NpmCdn.Api.Configuration;

public class EvictionOptions
{
    /// <summary>
    /// How frequently the sweeper background job should run.
    /// Default is every 24 hours.
    /// </summary>
    public TimeSpan RunInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The maximum age of an package's inactivity before it is evicted from storage.
    /// Default is 30 days.
    /// </summary>
    public TimeSpan MaxInactivityPeriod { get; set; } = TimeSpan.FromDays(30);
}