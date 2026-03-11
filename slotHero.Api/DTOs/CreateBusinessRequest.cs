namespace SlotHero.Api.DTOs;

/// <summary>
/// Client-facing request for registering a new business, exposing only safe fields to prevent over-posting.
/// </summary>
public record CreateBusinessRequest(string GoogleId, string Email, string BusinessName);
