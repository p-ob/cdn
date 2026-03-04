namespace NpmCdn.Api.Configuration;

public class AllowedOriginsOptions
{
    public const string SectionName = "AllowedOrigins";

    /// <summary>
    /// The list of allowed origins (e.g., "https://mysite.com") that are permitted to use this CDN.
    /// When empty or null, all origins are allowed.
    /// </summary>
    public string[] Origins { get; set; } = [];
}
