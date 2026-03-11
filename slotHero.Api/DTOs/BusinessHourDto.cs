namespace SlotHero.Api.DTOs;

/// <summary>
/// Client-facing representation of a business operating window for a given day.
/// </summary>
public record BusinessHourDto(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime);
