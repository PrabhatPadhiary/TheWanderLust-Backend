#nullable enable
namespace TheWanderLustWebAPI.Settings
{
    /// <summary>
    /// Viator Partner API (affiliate, Basic access). The key is set in Render as
    /// Viator__ApiKey (and locally in appsettings.Development.json). Without a
    /// key the offers endpoints return no results and the app hides the rows.
    /// </summary>
    public class ViatorSettings
    {
        public string? ApiKey { get; set; }
        public string BaseUrl { get; set; } = "https://api.viator.com/partner";
        public string Currency { get; set; } = "INR";
    }
}
