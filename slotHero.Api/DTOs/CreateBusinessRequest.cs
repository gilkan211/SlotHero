using System.ComponentModel.DataAnnotations;

namespace SlotHero.Api.DTOs;

/// <summary>
/// Client-facing request for registering a new business, exposing only safe fields to prevent over-posting.
/// </summary>
public record CreateBusinessRequest(
    string GoogleId,
    [Required][EmailAddress] string Email,
    [Required][StringLength(100)] string BusinessName,
    string? GoogleRefreshToken = null);
