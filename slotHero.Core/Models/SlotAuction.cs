using System;
using System.ComponentModel.DataAnnotations;

namespace SlotHero.Core.Models;

/// <summary>
/// A freed calendar slot offered to waitlisted clients through a priority-based auction.
/// </summary>
public class SlotAuction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The business that owns the freed calendar slot.
    /// </summary>
    public Guid BusinessId { get; set; }

    /// <summary>
    /// Google Calendar event identifier for the freed slot.
    /// </summary>
    public string GoogleEventId { get; set; } = string.Empty;

    /// <summary>
    /// Original start time of the slot being offered.
    /// </summary>
    public DateTime SlotStartTime { get; set; }

    /// <summary>
    /// Current auction lifecycle status.
    /// </summary>
    public SlotStatus Status { get; set; } = SlotStatus.Pending;

    /// <summary>
    /// The client who confirmed and claimed the slot, if any.
    /// </summary>
    public Guid? FinalConfirmedClientId { get; set; }
}