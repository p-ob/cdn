namespace NpmCdn.Api.Configuration;

public class CacheOptions
{
    public const string SectionName = "CacheControl";

    /// <summary>
    /// The maximum age for exact, immutable versions that are permanently pinned.
    /// Default is 365 Days.
    /// </summary>
    public TimeSpan ExactVersionMaxAge { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// The maximum age for dynamically resolved versions before the edge revalidates the latest tag.
    /// Default is 10 Minutes.
    /// </summary>
    public TimeSpan DynamicVersionMaxAge { get; set; } = TimeSpan.FromMinutes(10);
}