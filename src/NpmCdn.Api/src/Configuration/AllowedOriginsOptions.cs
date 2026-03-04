namespace NpmCdn.Api.Configuration;

public class AllowedOriginsOptions
{
    public const string SectionName = "AllowedOrigins";

    /// <summary>
    /// The list of allowed origins (e.g., "https://mysite.com") that are permitted to use this CDN.
    /// When empty, all origins are allowed.
    /// </summary>
    public required string[] Origins { get; set; } = [];
}