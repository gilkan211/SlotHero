namespace SlotHero.Api.DTOs;

/// <summary>
/// Represents a single available time slot for client booking.
/// </summary>
public record TimeSlotDto(DateTimeOffset StartTime, DateTimeOffset EndTime);
