namespace SlotHero.Api.Services;

/// <summary>
/// Holds the Google OAuth credentials and redirect configuration required
/// to authenticate with the Google Calendar API on behalf of businesses.
/// </summary>
public class GoogleSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}
