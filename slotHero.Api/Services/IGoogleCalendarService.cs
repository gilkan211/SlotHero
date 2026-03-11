using Google.Apis.Calendar.v3.Data;

namespace SlotHero.Api.Services;

/// <summary>
/// Abstracts the calendar provider so the availability logic is decoupled from Google's SDK,
/// making the system testable and open to alternative calendar integrations in the future.
/// </summary>
public interface IGoogleCalendarService
{
    /// <summary>
    /// Fetches upcoming calendar events for a business using its stored refresh token,
    /// allowing SlotHero to determine which time slots are already booked.
    /// </summary>
    Task<IEnumerable<Event>> GetUpcomingEventsAsync(string refreshToken, string businessId, CancellationToken ct = default);
}
