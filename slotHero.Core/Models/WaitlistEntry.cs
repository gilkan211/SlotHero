using System;
using System.ComponentModel.DataAnnotations;

namespace SlotHero.Core.Models;

/// <summary>
/// A client waiting to be notified when a calendar slot becomes available.
/// </summary>
public class WaitlistEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The business this entry belongs to.
    /// </summary>
    public Guid BusinessId { get; set; }

    /// <summary>
    /// Client's name for display purposes.
    /// </summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>
    /// Client's phone number used for SMS slot notifications.
    /// </summary>
    [Required]
    public string ClientPhone { get; set; } = string.Empty;

    /// <summary>
    /// Priority ranking on the waitlist; lower values are contacted first.
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Whether the client is still actively waiting for a slot.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// UTC timestamp of when the client joined the waitlist.
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}