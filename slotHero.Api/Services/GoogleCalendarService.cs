using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Options;

namespace SlotHero.Api.Services;

/// <summary>
/// Integrates with the Google Calendar API to retrieve upcoming appointments,
/// enabling businesses to surface their real-time availability on the SlotHero platform.
/// </summary>
public class GoogleCalendarService : IGoogleCalendarService
{
    private readonly GoogleSettings _settings;
    private readonly ILogger<GoogleCalendarService> _logger;

    public GoogleCalendarService(IOptions<GoogleSettings> settings, ILogger<GoogleCalendarService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Fetches upcoming calendar events for a business using its stored refresh token,
    /// allowing SlotHero to determine which time slots are already booked.
    /// </summary>
    public async Task<IEnumerable<Event>> GetUpcomingEventsAsync(string refreshToken, string businessId, CancellationToken ct = default)
    {
        try
        {
            // We use a refresh token (not an access token) because access tokens expire after ~1 hour;
            // the refresh token lets us obtain fresh credentials on each request without re-prompting the user.
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _settings.ClientId,
                    ClientSecret = _settings.ClientSecret
                },
                // Readonly scope ensures we never modify a business's calendar — SlotHero only reads availability.
                Scopes = [CalendarService.Scope.CalendarEventsReadonly],
                // We supply the refresh token directly via TokenResponse, so no persistent DataStore is needed.
                DataStore = new NullDataStore()
            });

            // RedirectUri is not set on the flow initializer because this is a background server-to-server
            // refresh-token flow — no user-facing redirect occurs. The SDK only requires it during the
            // initial authorization code exchange, which has already happened at OAuth consent time.
            var credential = new UserCredential(flow, "user", new TokenResponse
            {
                RefreshToken = refreshToken
            });

            var service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SlotHero"
            });

            var request = service.Events.List("primary");
            request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow;
            request.ShowDeleted = false;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            // Cap at 10 to keep the initial availability snapshot lightweight;
            // full sync is handled separately via webhook push notifications.
            request.MaxResults = 10;

            _logger.LogInformation("Fetching upcoming Google Calendar events for business {BusinessId}", businessId);

            // Pass the cancellation token to avoid resource leaks if the caller (e.g., an HTTP request) is aborted.
            var response = await request.ExecuteAsync(ct);

            return response.Items ?? [];
        }
        catch (TokenResponseException ex)
        {
            _logger.LogWarning(ex, "Google refresh token is invalid or expired for business {BusinessId}", businessId);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch calendar events from Google for business {BusinessId}", businessId);
            return [];
        }
    }
}