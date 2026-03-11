namespace SlotHero.Api.DTOs;

/// <summary>
/// Safe projection of a business entity, excluding sensitive fields like the encrypted refresh token.
/// </summary>
public record BusinessResponseDto(Guid Id, string GoogleId, string Email, string BusinessName, DateTime CreatedAt);
