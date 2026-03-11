using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SlotHero.Api.DTOs;

/// <summary>
/// Client-facing request for registering a new business, exposing only safe fields to prevent over-posting.
/// </summary>
public record CreateBusinessRequest(
    string GoogleId,
    [Required][EmailAddress] string Email,
    [Required][StringLength(100)] string BusinessName,
    [property: JsonPropertyName("encryptedRefreshToken")] string? EncryptedRefreshToken = null);
