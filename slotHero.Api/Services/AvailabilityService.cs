using Google.Apis.Calendar.v3.Data;
using SlotHero.Api.DTOs;

namespace SlotHero.Api.Services;

/// <summary>
/// Computes available booking slots by combining business hours with existing calendar events.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Returns available time slots for a business on a given date,
    /// accounting for slot duration and buffer time between appointments.
    /// </summary>
    Task<List<TimeSlotDto>> GetAvailableSlotsAsync(Guid businessId, DateTimeOffset date, TimeSpan slotDuration, TimeSpan buffer, CancellationToken ct);
}

public class AvailabilityService : IAvailabilityService
{
    private readonly IBusinessService _businessService;
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(IBusinessService businessService, ILogger<AvailabilityService> logger)
    {
        _businessService = businessService;
        _logger = logger;
    }

    public async Task<List<TimeSlotDto>> GetAvailableSlotsAsync(Guid businessId, DateTimeOffset date, TimeSpan slotDuration, TimeSpan buffer, CancellationToken ct)
    {
        if (slotDuration.TotalMinutes <= 0)
            throw new ArgumentException("Slot duration must be greater than zero.");

        var allHours = await _businessService.GetBusinessHoursAsync(businessId, ct);

        if (allHours is null)
            throw new KeyNotFoundException("Business not found.");

        var dayHours = allHours
            .Where(h => h.DayOfWeek == date.DayOfWeek)
            .ToList();

        // Query Google only for events within the requested day to reduce API payload
        var dayStart = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, date.Offset);
        var dayEnd = dayStart.AddDays(1);

        var (events, error) = await _businessService.GetUpcomingEventsAsync(businessId, ct, timeMin: dayStart, timeMax: dayEnd);

        // Failsafe: if calendar sync failed, do not assume the day is free
        if (error is not null || events is null)
        {
            _logger.LogWarning("Google Calendar sync failed for BusinessId: {BusinessId}", businessId);
            throw new InvalidOperationException("Cannot determine availability because calendar sync failed.");
        }

        var busyBlocks = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        foreach (var ev in events)
        {
            // Handle timed events
            var evStart = ev.Start?.DateTimeDateTimeOffset;
            var evEnd = ev.End?.DateTimeDateTimeOffset;

            // All-day events expose Date (yyyy-MM-dd) instead of DateTimeDateTimeOffset
            if (evStart is null && ev.Start?.Date is string startDate)
                evStart = DateTimeOffset.Parse($"{startDate}T00:00:00{date.ToString("zzz")}");
            if (evEnd is null && ev.End?.Date is string endDate)
                evEnd = DateTimeOffset.Parse($"{endDate}T00:00:00{date.ToString("zzz")}");

            if (evStart is null || evEnd is null)
                continue;

            if (evStart.Value < dayEnd && evEnd.Value > dayStart)
                busyBlocks.Add((evStart.Value, evEnd.Value));
        }

        var now = DateTimeOffset.UtcNow.ToOffset(date.Offset);
        var slots = new List<TimeSlotDto>();

        foreach (var window in dayHours)
        {
            var windowStart = new DateTimeOffset(date.Year, date.Month, date.Day, window.StartTime.Hours, window.StartTime.Minutes, window.StartTime.Seconds, date.Offset);
            var windowEnd = new DateTimeOffset(date.Year, date.Month, date.Day, window.EndTime.Hours, window.EndTime.Minutes, window.EndTime.Seconds, date.Offset);

            var pointer = windowStart;

            // Time travel prevention: never offer slots in the past regardless of timezone
            if (pointer < now)
                pointer = now;

            pointer = RoundUpTo15Minutes(pointer);

            while (pointer.Add(slotDuration) <= windowEnd)
            {
                var slotStart = pointer;
                var slotEnd = pointer.Add(slotDuration);

                var overlappingEvent = busyBlocks.FirstOrDefault(b => slotStart < b.End && slotEnd > b.Start);

                if (overlappingEvent == default)
                {
                    slots.Add(new TimeSlotDto(slotStart, slotEnd));
                    pointer = pointer.Add(slotDuration).Add(buffer);
                    pointer = RoundUpTo15Minutes(pointer);
                }
                else
                {
                    // Jump to the end of the overlapping event, enforce buffer, and re-align to the 15-min grid
                    pointer = overlappingEvent.End.Add(buffer);
                    pointer = RoundUpTo15Minutes(pointer);
                }
            }
        }

        return slots;
    }

    /// <summary>
    /// Rounds a DateTimeOffset up to the next 15-minute boundary.
    /// If already aligned, returns the value unchanged.
    /// </summary>
    private static DateTimeOffset RoundUpTo15Minutes(DateTimeOffset time)
    {
        var ticks = time.Ticks;
        var intervalTicks = TimeSpan.FromMinutes(15).Ticks;
        var remainder = ticks % intervalTicks;

        return remainder == 0 ? time : new DateTimeOffset(ticks + intervalTicks - remainder, time.Offset);
    }
}
