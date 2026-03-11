using System;
using System.ComponentModel.DataAnnotations;

namespace SlotHero.Core.Models;

/// <summary>
/// A business that manages calendar-based appointments and waitlists through SlotHero.
/// </summary>
public class Business
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Google account identifier for Calendar integration.
    /// </summary>
    public string GoogleId { get; set; } = string.Empty;

    /// <summary>
    /// Contact email for the business account.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown to clients on the waitlist.
    /// </summary>
    public string BusinessName { get; set; } = string.Empty;

    /// <summary>
    /// Encrypted OAuth refresh token for persistent Google Calendar access.
    /// </summary>
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when the business registered.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}