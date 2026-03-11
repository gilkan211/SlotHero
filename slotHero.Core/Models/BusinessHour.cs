using System.ComponentModel.DataAnnotations;

namespace SlotHero.Core.Models;

/// <summary>
/// Defines a single operating window for a business on a given day of the week.
/// </summary>
public class BusinessHour
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid BusinessId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    public Business? Business { get; set; }
}
